using Microsoft.Playwright;
using System.Diagnostics;

namespace QTP.Common;

/// <summary>One execution owns one process and connection. A driver is borrowed through a lease.</summary>
public sealed class BrowserRuntimeManager : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly IPlaywrightProvider _provider;
    private readonly IBrowserProcessManager _processes;
    private readonly Func<IPlaywright, string, CancellationToken, Task<IBrowser>> _connect;
    private BrowserRuntimeOptions _options;
    private SemaphoreSlim _running;
    private SemaphoreSlim _launching;
    private CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, BrowserRuntimeLease> _sessions = new();
    private readonly HashSet<string> _executionIds = new();
    private readonly HashSet<string> _profiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _executionProfiles = new();
    private int _waiting, _opening, _acquiring;
    private bool _accepting = true, _disposed;
    private Task? _stop;
    private Task? _disposal;
    private TaskCompletionSource _idle = Completed();
    private TaskCompletionSource _acquisitionsDrained = Completed();
    private BrowserStopKind? _admissionStopKind;
    public event Action<BrowserReclaimResult>? Reclaimed;
    public bool IsResourceLimited { get { lock (_sync) return _admissionStopKind == BrowserStopKind.ResourceExhausted; } }

    public BrowserRuntimeManager(IPlaywrightProvider provider, IBrowserProcessManager processes,
        BrowserRuntimeOptions? options = null, Func<IPlaywright, string, CancellationToken, Task<IBrowser>>? connect = null)
    {
        _provider = provider; _processes = processes; _options = Validate(options ?? new());
        _running = new(_options.MaximumRunning, _options.MaximumRunning);
        _launching = new(_options.MaximumLaunching, _options.MaximumLaunching);
        _connect = connect ?? ConnectAsync;
    }
    public BrowserRuntimeSnapshot Snapshot
    {
        get
        {
            lock (_sync) return new(_options.MaximumRunning, _options.MaximumLaunching, _waiting, _opening,
                _sessions.Values.Count(x => !x.IsClosing), _sessions.Values.Count(x => x.IsClosing), _accepting)
                { AdmissionStopKind = _admissionStopKind };
        }
    }
    /// <summary>Configuration changes take effect between runs, never under live waiters.</summary>
    public void BeginRun(BrowserRuntimeOptions options)
    {
        Validate(options);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_acquiring != 0 || _sessions.Count != 0 || (_stop != null && !_stop.IsCompleted))
                throw new InvalidOperationException("Wait for the previous run to stop before changing browser limits.");
            _running.Dispose(); _launching.Dispose(); _lifetime.Dispose();
            _options = options;
            _running = new(options.MaximumRunning, options.MaximumRunning);
            _launching = new(options.MaximumLaunching, options.MaximumLaunching);
            _lifetime = new(); _accepting = true; _stop = null; _admissionStopKind = null;
        }
    }
    public async Task<BrowserRuntimeLease> AcquireAsync(BrowserStartOptions options, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.UserDataDir);
        var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.UserDataDir));
        token.ThrowIfCancellationRequested();
        CancellationToken stopToken;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_accepting) throw new InvalidOperationException("Browser runtime is stopping.");
            if (_executionIds.Contains(options.ExecutionId)) throw new InvalidOperationException("Execution already has a browser session.");
            if (!_profiles.Add(profile)) throw new InvalidOperationException("Browser profile is already owned by another execution.");
            _executionIds.Add(options.ExecutionId);
            _executionProfiles.Add(options.ExecutionId, profile);
            if (_acquiring == 0) _acquisitionsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_acquiring++ == 0 && _sessions.Count == 0) _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting++; stopToken = _lifetime.Token;
        }
        using var acquiring = CancellationTokenSource.CreateLinkedTokenSource(token, stopToken);
        var ct = acquiring.Token;
        bool runHeld = false, launchHeld = false, openingCounted = false, transferred = false, startupAttempted = false;
        PlaywrightLease? driver = null;
        ChromiumSession? process = null;
        IBrowser? browser = null;
        CancellationTokenSource? sessionLifetime = null;
        BrowserRuntimeLease? preparedLease = null;
        Action<BrowserStopKind>? startupStopped = null;
        Exception? startupFailure = null;
        var clock = Stopwatch.StartNew();
        try
        {
            await _running.WaitAsync(ct).ConfigureAwait(false); runHeld = true;
            await _launching.WaitAsync(ct).ConfigureAwait(false); launchHeld = true;
            lock (_sync) { _waiting--; _opening++; openingCounted = true; }
            lock (_sync) if (!_accepting) throw new InvalidOperationException("Browser admission was stopped.");
            ct.ThrowIfCancellationRequested();
            driver = await _provider.AcquireAsync(ct).ConfigureAwait(false);
            sessionLifetime = CancellationTokenSource.CreateLinkedTokenSource(token, stopToken, driver.DriverToken);
            var sessionToken = sessionLifetime.Token;
            startupAttempted = true;
            process = await _processes.StartChromium(options.ExecutionId, options.ExecutablePath, options.UserDataDir,
                options.Lifetime, options.Arguments, options.Proxy, options.ReadyTimeout ?? TimeSpan.FromSeconds(15), sessionToken).ConfigureAwait(false);
            startupStopped = _ => { try { sessionLifetime.Cancel(); } catch (ObjectDisposedException) { } };
            process.StopRequested += startupStopped;
            if (process.StopKind != null) sessionLifetime.Cancel();
            browser = await _connect(driver.Playwright, process.CdpEndpoint, sessionToken).ConfigureAwait(false);
            sessionToken.ThrowIfCancellationRequested();
            if (!browser.IsConnected || browser.Contexts.Count == 0)
                throw new InvalidOperationException("Browser connection has no usable default context.");
            var lease = new BrowserRuntimeLease(this, process, driver, browser, browser.Contexts[0], sessionLifetime, token);
            preparedLease = lease;
            sessionToken.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (!_accepting) throw new OperationCanceledException(stopToken);
                _sessions.Add(options.ExecutionId, lease);
                transferred = true;
            }
            return lease;
        }
        catch (PlaywrightException ex)
        {
            startupFailure = ex;
            if (driver != null) await CheckDriverAfterFailureAsync(driver).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            startupFailure = ex is OperationCanceledException && process?.StopKind is { } stopped
                ? new BrowserStartupCanceledException(stopped, ex, ct) : ex;
            if (BrowserFailureClassifier.Classify(ex) == BrowserStopKind.ResourceExhausted)
            {
                // Keep existing sessions alive; new startup is blocked until an explicit new run.
                lock (_sync) { _accepting = false; _admissionStopKind = BrowserStopKind.ResourceExhausted; }
            }
            if (!ReferenceEquals(startupFailure, ex)) throw startupFailure;
            throw;
        }
        finally
        {
            if (process != null && startupStopped != null) process.StopRequested -= startupStopped;
            Exception? closeError = null;
            if (!transferred)
            {
                preparedLease?.DetachBindings();
                if (browser != null) try { await browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch { }
                if (process == null && startupAttempted) _processes.TryGetSession(options.ExecutionId, out process);
                if (process != null || startupAttempted)
                {
                    try { await _processes.CloseAsync(options.ExecutionId).ConfigureAwait(false); }
                    catch (Exception ex) { closeError = ex; }
                }
                // If a process could not be terminated, retain its ownership and permit in a quarantine lease.
                if (closeError != null && process != null && driver != null)
                {
                    var quarantine = new BrowserRuntimeLease(this, process, driver, browser,
                        browser?.Contexts.FirstOrDefault(), sessionLifetime!);
                    lock (_sync) _sessions[options.ExecutionId] = quarantine;
                    quarantine.RequestStop(startupFailure == null ? BrowserStopKind.CleanupFailed : BrowserFailureClassifier.Classify(startupFailure), "Process cleanup failed");
                    transferred = true;
                }
                else
                {
                    if (driver != null) await driver.DisposeAsync().ConfigureAwait(false);
                    sessionLifetime?.Dispose();
                    if (runHeld) _running.Release();
                }
            }
            if (launchHeld) _launching.Release();
            lock (_sync)
            {
                if (openingCounted) _opening--; else _waiting--;
                _acquiring--;
                if (_acquiring == 0) _acquisitionsDrained.TrySetResult();
                if (!transferred) ReleaseIdentity(options.ExecutionId);
                SignalIdle();
            }
            if (startupFailure != null) PublishReclaim(new(options.ExecutionId, BrowserFailureClassifier.Classify(startupFailure),
                closeError == null, clock.Elapsed, closeError == null ? [startupFailure.Message] : [startupFailure.Message, closeError.Message], process?.LastCloseResult));
            if (closeError != null) throw new IOException("Browser startup cleanup could not confirm process exit.", closeError);
        }
    }
    internal Task CloseProcessAsync(string executionId) => _processes.CloseAsync(executionId);
    internal void PublishReclaim(BrowserReclaimResult result)
    {
        if (Reclaimed is { } handlers) foreach (Action<BrowserReclaimResult> handler in handlers.GetInvocationList())
            try { handler(result); } catch { }
    }
    public Task CloseSessionAsync(string executionId)
    {
        lock (_sync) return _sessions.TryGetValue(executionId, out var lease) ? lease.DisposeAsync().AsTask() : Task.CompletedTask;
    }
    internal void Release(BrowserRuntimeLease lease)
    {
        lock (_sync)
        {
            if (_sessions.TryGetValue(lease.ExecutionId, out var current) && ReferenceEquals(current, lease))
            {
                _sessions.Remove(lease.ExecutionId); ReleaseIdentity(lease.ExecutionId);
                _running.Release(); SignalIdle();
            }
        }
    }
    public Task StopAsync()
    {
        lock (_sync)
        {
            _accepting = false;
            if (_stop == null || _stop.IsFaulted) _stop = Task.Run(StopCoreAsync);
            return _stop;
        }
    }
    private async Task StopCoreAsync()
    {
        BrowserRuntimeLease[] stopping;
        lock (_sync) stopping = _sessions.Values.ToArray();
        foreach (var lease in stopping) lease.RequestStop(BrowserStopKind.RuntimeStopping, "Browser runtime stopping");
        try { _lifetime.Cancel(); } catch (AggregateException) { }
        Task idle;
        lock (_sync) idle = _idle.Task;
        try { await idle.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); return; }
        catch (TimeoutException) { }
        Task acquired;
        lock (_sync) acquired = _acquisitionsDrained.Task;
        await acquired.ConfigureAwait(false);
        BrowserRuntimeLease[] leases;
        lock (_sync) leases = _sessions.Values.ToArray();
        await Task.WhenAll(leases.Select(x => x.DisposeAsync().AsTask())).ConfigureAwait(false);
        // Startup also owns a driver and permit: wait for its rollback before allowing provider disposal.
        await idle.ConfigureAwait(false);
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _disposed = true;
            if (_disposal == null || _disposal.IsFaulted) _disposal = Task.Run(async () =>
            {
                await StopAsync().ConfigureAwait(false);
                _running.Dispose(); _launching.Dispose(); _lifetime.Dispose();
            });
            return new(_disposal);
        }
    }
    private void ReleaseIdentity(string executionId)
    {
        _executionIds.Remove(executionId);
        if (_executionProfiles.Remove(executionId, out var profile)) _profiles.Remove(profile);
    }
    private void SignalIdle() { if (_acquiring == 0 && _sessions.Count == 0) _idle.TrySetResult(); }
    private static BrowserRuntimeOptions Validate(BrowserRuntimeOptions options)
    {
        if (options.MaximumRunning < 1 || options.MaximumLaunching < 1) throw new ArgumentOutOfRangeException(nameof(options));
        return options;
    }
    private static TaskCompletionSource Completed()
    { var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); t.SetResult(); return t; }
    private static async Task<IBrowser> ConnectAsync(IPlaywright playwright, string endpoint, CancellationToken token)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var connecting = playwright.Chromium.ConnectOverCDPAsync(endpoint, new() { Timeout = 5000 });
            try { return await connecting.WaitAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                _ = CloseLateConnectionAsync(connecting); throw;
            }
            catch (Exception ex) { last = ex; }
            if (attempt < 2) await Task.Delay(200, token).ConfigureAwait(false);
        }
        throw last!;
    }
    private static async Task CloseLateConnectionAsync(Task<IBrowser> connecting)
    { try { await (await connecting.ConfigureAwait(false)).CloseAsync().ConfigureAwait(false); } catch { } }
    private async Task CheckDriverAfterFailureAsync(PlaywrightLease lease)
    {
        if (_provider is not PlaywrightProvider managed) return;
        try
        {
            // A browser disconnection alone does not prove that the shared driver is broken.
            var creating = lease.Playwright.APIRequest.NewContextAsync();
            IAPIRequestContext probe;
            try { probe = await creating.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (TimeoutException) { _ = CloseLateProbeAsync(creating); throw; }
            await probe.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Connection closed", StringComparison.OrdinalIgnoreCase))
        { managed.Invalidate(lease); }
        catch { } // Busy or cancelled probes are not evidence of driver death.
    }
    private static async Task CloseLateProbeAsync(Task<IAPIRequestContext> creating)
    { try { await (await creating.ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false); } catch { } }
}
