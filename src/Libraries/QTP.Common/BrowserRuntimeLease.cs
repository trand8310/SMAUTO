using Microsoft.Playwright;
using System.Diagnostics;

namespace QTP.Common;

public sealed class BrowserRuntimeLease : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly List<Func<Task>> _cleanups = new();
    private readonly BrowserRuntimeManager _owner;
    private readonly PlaywrightLease _driver;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly CancellationTokenRegistration _driverStopped;
    private readonly CancellationTokenRegistration _callerStopped;
    private readonly Action<BrowserStopKind> _processStopped;
    private readonly IBrowser? _browser;
    private readonly IBrowserContext? _context;
    private Task? _close;
    private readonly EventHandler<IBrowser> _disconnected;
    public ChromiumSession ProcessSession { get; }
    public string ExecutionId => ProcessSession.UniqueId;
    public IPlaywright Playwright => _driver.Playwright;
    public IBrowser Browser => _browser ?? throw new InvalidOperationException("Startup did not establish a browser connection.");
    /// <summary>Borrowed CDP default context: never explicitly dispose it separately.</summary>
    public IBrowserContext Context => _context ?? throw new InvalidOperationException("Startup did not establish a browser context.");
    public CancellationToken Token => _token;
    public bool IsClosing { get { lock (_sync) return _close != null; } }
    public string? StopReason { get; private set; }
    public BrowserStopKind? StopKind { get; private set; }
    public BrowserReclaimResult? ReclaimResult { get; private set; }
    public IReadOnlyList<Exception> CleanupErrors { get { lock (_sync) return _errors.ToArray(); } }
    private readonly List<Exception> _errors = new();
    internal PlaywrightLease DriverLease => _driver;
    internal BrowserRuntimeLease(BrowserRuntimeManager owner, ChromiumSession process, PlaywrightLease driver,
        IBrowser? browser, IBrowserContext? context, CancellationTokenSource lifetime, CancellationToken callerToken = default)
    {
        _owner = owner; ProcessSession = process; _driver = driver; _browser = browser; _context = context; _lifetime = lifetime;
        _token = lifetime.Token;
        _disconnected = (_, _) =>
        {
            var kind = BrowserStopKind.CdpDisconnected;
            try { if (process.Process.HasExited && process.Process.ExitCode != 0) kind = BrowserStopKind.BrowserCrashed; } catch { }
            RequestStop(kind, "Browser disconnected");
        };
        if (_browser != null) _browser.Disconnected += _disconnected;
        _processStopped = kind => RequestStop(kind);
        process.StopRequested += _processStopped;
        if (process.StopKind is { } stopped) RequestStop(stopped);
        _driverStopped = driver.DriverToken.Register(() => RequestStop(BrowserStopKind.DriverDisconnected, "Playwright driver invalidated"));
        _callerStopped = callerToken.Register(() => RequestStop(BrowserStopKind.Canceled, "Task canceled"));
    }
    public void RegisterCleanup(Func<Task> cleanup)
    {
        lock (_sync)
        {
            if (_close != null) throw new InvalidOperationException("Session is closing.");
            _cleanups.Add(cleanup);
        }
    }
    public void RequestStop(string reason)
        => RequestStop(BrowserStopKind.CleanupFailed, reason);
    public void RequestStop(BrowserStopKind kind, string? reason = null)
    {
        lock (_sync)
        {
            if (StopKind == null || (StopKind == BrowserStopKind.CdpDisconnected && kind == BrowserStopKind.BrowserCrashed))
                { StopKind = kind; StopReason = reason ?? kind.ToString(); }
        }
        try { _lifetime.Cancel(); } catch (ObjectDisposedException) { } catch (AggregateException) { }
    }
    public async Task<BrowserReclaimResult> CloseWithResultAsync()
    {
        var closing = DisposeAsync().AsTask();
        try { await closing.ConfigureAwait(false); } catch { }
        return ReclaimResult ?? throw new InvalidOperationException("Session reclaim did not produce a result.");
    }
    internal void DetachBindings()
    {
        if (_browser != null) _browser.Disconnected -= _disconnected;
        ProcessSession.StopRequested -= _processStopped;
        _driverStopped.Dispose(); _callerStopped.Dispose();
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            // Failed process termination keeps its running permit; explicit shutdown may retry it.
            if (_close == null || _close.IsFaulted) _close = Task.Run(CloseAsync);
            return new(_close);
        }
    }
    private async Task CloseAsync()
    {
        var clock = Stopwatch.StartNew();
        RequestStop(BrowserStopKind.Completed, "Session closing");
        ProcessSession.RequestStop(StopKind ?? BrowserStopKind.Completed);
        if (_browser != null) _browser.Disconnected -= _disconnected;
        Func<Task>[] callbacks;
        lock (_sync) { callbacks = _cleanups.ToArray(); _cleanups.Clear(); }
        foreach (var cleanup in callbacks)
        {
            try { await cleanup().WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false); }
            catch (Exception ex) { lock (_sync) _errors.Add(ex); }
        }
        try
        {
            if (_browser?.IsConnected == true) await _browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception ex) { lock (_sync) _errors.Add(ex); }
        // This is the final authority on releasing the running slot.
        bool released = false;
        try
        {
            await _owner.CloseProcessAsync(ExecutionId).ConfigureAwait(false);
            await _driver.DisposeAsync().ConfigureAwait(false);
            DetachBindings();
            _lifetime.Dispose();
            released = true;
        }
        catch (Exception ex) { lock (_sync) _errors.Add(ex); throw; }
        finally
        {
            lock (_sync) ReclaimResult = new(ExecutionId, StopKind ?? BrowserStopKind.Completed, released,
                clock.Elapsed, _errors.Select(x => x.Message).ToArray(), ProcessSession.LastCloseResult);
            _owner.PublishReclaim(ReclaimResult);
            if (released) _owner.Release(this);
        }
    }
}
