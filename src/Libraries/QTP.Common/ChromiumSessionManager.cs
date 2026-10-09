using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace QTP.Common
{
    public sealed class ChromiumSession
    {
        public string UniqueId { get; init; } = "";
        public string? Proxy { get; init; }
        public Process Process { get; init; } = null!;
        public int DebugPort { get; init; }
        public string CdpEndpoint { get; init; } = "";
        public string UserDir { get; init; } = "";
        public DateTime ExpireAt { get; init; }
        public TimeSpan Lifetime { get; init; } = TimeSpan.MaxValue;
        internal long CreatedTimestamp { get; init; } = Stopwatch.GetTimestamp();
        public ChromiumCloseResult? LastCloseResult { get; internal set; }
        public Task<ProfileCleanupResult>? DirectoryCleanup { get; internal set; }
        public event Action<BrowserStopKind>? StopRequested;
        public BrowserStopKind? StopKind { get; private set; }
        public void RequestStop(BrowserStopKind kind)
        {
            Action<BrowserStopKind>? handlers;
            lock (CloseSync)
            {
                if (StopKind != null) return;
                StopKind = kind; handlers = StopRequested;
            }
            if (handlers != null) foreach (Action<BrowserStopKind> handler in handlers.GetInvocationList())
                try { handler(kind); } catch { }
        }

        // 0 = 未关闭，1 = 关闭中/已关闭
        public int CloseStarted;
        internal readonly object CloseSync = new();
        internal Task? CloseTask;
        public bool ExitConfirmed { get; internal set; }
    }

    public sealed class ChromiumSessionManager : IAsyncDisposable, IBrowserProcessManager
    {
        private readonly ConcurrentDictionary<string, ChromiumSession> _sessions = new();

        // Tracks in-flight starts so shutdown cannot race process registration.
        private readonly object _startSync = new();
        private int _starting;
        private TaskCompletionSource _startsDrained = CompletedSource();
        private Task? _disposal;

        private readonly ProfileCleanupQueue _cleanup;
        private readonly HashSet<string> _reservedIds = new();
        private readonly Dictionary<string, string> _ownedProfiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Task<ProfileCleanupResult>> _profileCleanupTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Task> _reclaims = new();
        private readonly TimeSpan _scanInterval;
        private readonly TimeSpan _expiryGrace;
        public event Action<ChromiumCloseResult>? Reclaimed;
        public event Action<ProfileCleanupResult>? ProfileCleaned;
        public int PendingDirectoryCleanups => _cleanup.PendingCount;
        public Task WaitForDirectoryCleanupAsync() => _cleanup.DrainAsync();

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _expireLoopTask;

        private int _disposeStarted;

        public ChromiumSessionManager(TimeSpan? expiryScanInterval = null, TimeSpan? expiryGracePeriod = null,
            ProfileCleanupQueue? cleanup = null)
        {
            _scanInterval = expiryScanInterval ?? TimeSpan.FromSeconds(1);
            _expiryGrace = expiryGracePeriod ?? TimeSpan.FromSeconds(5);
            if (_scanInterval <= TimeSpan.Zero || _expiryGrace < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(expiryScanInterval));
            _cleanup = cleanup ?? new ProfileCleanupQueue();
            _expireLoopTask = ExpireScanLoopAsync(_cts.Token);
        }

        public int Count => _sessions.Count;

        public IReadOnlyCollection<ChromiumSession> GetAllSessions()
        {
            return _sessions.Values.ToArray();
        }

        public bool TryGetSession(string uniqueId, out ChromiumSession? session)
        {
            if (_sessions.TryGetValue(uniqueId, out var found))
            {
                session = found;
                return true;
            }

            session = null;
            return false;
        }

        public bool Contains(string uniqueId)
        {
            return _sessions.ContainsKey(uniqueId);
        }

        /// <summary>
        /// 启动 Chromium，并等待 remote debugging port 可用
        /// </summary>
        public async Task<ChromiumSession> StartChromium(string uniqueId, string exePath, string userDataDir,
            TimeSpan ttl, string arguments = "--incognito", string? proxyServer = null,
            TimeSpan? readyTimeout = null, CancellationToken token = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(uniqueId);
            ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(userDataDir);
            if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
            userDataDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userDataDir));
            if (userDataDir == Path.GetPathRoot(userDataDir)) throw new ArgumentException("A filesystem root cannot be a browser profile.");
            lock (_startSync)
            {
                ThrowIfDisposed();
                if (_reservedIds.Contains(uniqueId)) throw new InvalidOperationException("Chromium execution ID is already reserved.");
                if (_ownedProfiles.ContainsKey(userDataDir)) throw new InvalidOperationException("Browser profile is active or awaiting cleanup.");
                _reservedIds.Add(uniqueId); _ownedProfiles.Add(userDataDir, uniqueId);
                if (_starting++ == 0) _startsDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _cts.Token);
            var ct = lifetime.Token;
            Process? proc = null;
            bool directoryCreated = false;
            try
            {
                ct.ThrowIfCancellationRequested();
                if (_sessions.ContainsKey(uniqueId)) throw new InvalidOperationException($"Chromium session already exists: {uniqueId}");
                directoryCreated = !Directory.Exists(userDataDir);
                Directory.CreateDirectory(userDataDir);
                File.Delete(Path.Combine(userDataDir, "DevToolsActivePort"));
                proc = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"{arguments} --user-data-dir=\"{userDataDir}\" --remote-debugging-port=0",
                    UseShellExecute = false, CreateNoWindow = true
                }) ?? throw new InvalidOperationException("Chromium process was not created.");
                var endpoint = await WaitForDevToolsEndpointAsync(proc, userDataDir, readyTimeout ?? TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                var session = new ChromiumSession { UniqueId = uniqueId, Proxy = proxyServer, Process = proc,
                    DebugPort = endpoint.Port, CdpEndpoint = endpoint.WebSocketUrl, UserDir = userDataDir,
                    ExpireAt = DateTime.UtcNow.Add(ttl), Lifetime = ttl };
                lock (_startSync)
                {
                    ThrowIfDisposed(); ct.ThrowIfCancellationRequested();
                    if (!_sessions.TryAdd(uniqueId, session)) throw new InvalidOperationException($"Failed to register Chromium: {uniqueId}");
                }
                return session;
            }
            catch (Exception startupError)
            {
                if (proc != null)
                {
                    var failed = new ChromiumSession { UniqueId = uniqueId, Process = proc, UserDir = userDataDir,
                        ExpireAt = DateTime.UtcNow.Add(ttl) };
                    if (_sessions.TryAdd(uniqueId, failed))
                    {
                        try { await CloseAsync(uniqueId).ConfigureAwait(false); }
                        catch (Exception closeError) { throw new AggregateException("Startup failed and process exit was not confirmed.", startupError, closeError); }
                    }
                    else await CloseInternalAsync(failed).ConfigureAwait(false);
                }
                else if (directoryCreated)
                    ScheduleProfileCleanup(new ChromiumSession { UniqueId = uniqueId, UserDir = userDataDir });
                throw;
            }
            finally
            {
                lock (_startSync)
                {
                    if (!_sessions.ContainsKey(uniqueId)) _reservedIds.Remove(uniqueId);
                    if (proc == null && !directoryCreated) _ownedProfiles.Remove(userDataDir);
                    if (--_starting == 0) _startsDrained.TrySetResult();
                }
            }
        }

        /// <summary>
        /// 关闭指定会话
        /// </summary>
        public Task CloseAsync(string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId) || !_sessions.TryGetValue(uniqueId, out var session)) return Task.CompletedTask;
            lock (session.CloseSync)
            {
                // Concurrent callers await the same close; a failed close can be retried.
                if (session.CloseTask == null || session.CloseTask.IsFaulted)
                    session.CloseTask = CloseAndRemoveAsync(session);
                return session.CloseTask;
            }
        }

        public async Task<ChromiumCloseResult?> CloseWithResultAsync(string uniqueId, BrowserStopKind reason = BrowserStopKind.Completed)
        {
            if (!_sessions.TryGetValue(uniqueId, out var session)) return null;
            session.RequestStop(reason);
            try { await CloseAsync(uniqueId).ConfigureAwait(false); } catch { }
            return session.LastCloseResult;
        }
        private async Task CloseAndRemoveAsync(ChromiumSession session)
        {
            Interlocked.Exchange(ref session.CloseStarted, 1);
            await CloseInternalAsync(session).ConfigureAwait(false);
            _sessions.TryRemove(new KeyValuePair<string, ChromiumSession>(session.UniqueId, session));
            lock (_startSync) _reservedIds.Remove(session.UniqueId);
        }
        public Task CloseAllAsync() => Task.WhenAll(_sessions.Keys.Select(CloseAsync));
        private async Task CloseInternalAsync(ChromiumSession session)
        {
            session.RequestStop(BrowserStopKind.Completed);
            var clock = Stopwatch.StartNew();
            bool forced = false; int? exitCode = null; string? error = null;
            try
            {
                if (!session.ExitConfirmed)
                {
                    if (!session.Process.HasExited)
                    {
                        forced = true;
                        session.Process.Kill(entireProcessTree: true);
                        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await session.Process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
                    }
                    if (!session.Process.HasExited) throw new IOException($"Chromium exit not confirmed: {session.UniqueId}");
                    exitCode = SafeGetExitCode(session.Process);
                    session.ExitConfirmed = true;
                    session.Process.Dispose();
                    ScheduleProfileCleanup(session);
                }
            }
            catch (Exception ex) { error = ex.Message; throw; }
            finally
            {
                session.LastCloseResult = new(session.UniqueId, session.StopKind ?? BrowserStopKind.Completed,
                    session.ExitConfirmed, forced, exitCode, clock.Elapsed, error,
                    session.DirectoryCleanup ?? Task.FromResult(new ProfileCleanupResult(session.UserDir, false, 0, "Process exit not confirmed")));
                Publish(Reclaimed, session.LastCloseResult);
            }
        }
        private void ScheduleProfileCleanup(ChromiumSession session)
        {
            session.DirectoryCleanup ??= _cleanup.Enqueue(session.UserDir);
            lock (_startSync) _profileCleanupTasks[session.UserDir] = session.DirectoryCleanup;
            _ = ObserveCleanupAsync(session.UserDir, session.UniqueId, session.DirectoryCleanup);
        }
        private async Task ObserveCleanupAsync(string path, string executionId, Task<ProfileCleanupResult> cleanup)
        {
            var result = await cleanup.ConfigureAwait(false);
            if (result.Deleted) lock (_startSync)
            {
                if (_ownedProfiles.TryGetValue(path, out var owner) && owner == executionId &&
                    _profileCleanupTasks.TryGetValue(path, out var latest) && ReferenceEquals(latest, cleanup))
                { _ownedProfiles.Remove(path); _profileCleanupTasks.Remove(path); }
            }
            Publish(ProfileCleaned, result);
        }
        public Task<ProfileCleanupResult> RetryProfileCleanupAsync(string userDir)
        {
            userDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userDir));
            lock (_startSync)
            {
                ThrowIfDisposed();
                if (!_ownedProfiles.TryGetValue(userDir, out var owner)) throw new InvalidOperationException("Profile is not owned by this manager.");
                if (_reservedIds.Contains(owner)) throw new InvalidOperationException("Profile is still used by an execution.");
                if (_profileCleanupTasks.TryGetValue(userDir, out var previous) &&
                    (!previous.IsCompleted || previous.IsCompletedSuccessfully && previous.Result.Deleted))
                    return previous;
                var cleanup = _cleanup.Enqueue(userDir);
                _profileCleanupTasks[userDir] = cleanup;
                _ = ObserveCleanupAsync(userDir, owner, cleanup);
                return cleanup;
            }
        }
        private static void Publish<T>(Action<T>? handlers, T result)
        {
            if (handlers != null) foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(result); } catch { }
        }

        /// <summary>
        /// 等待 Chromium 的 remote debugging port 真正 ready
        /// </summary>
        private static async Task<ChromiumDevToolsEndpoint> WaitForDevToolsEndpointAsync(
            Process process,
            string userDataDir,
            TimeSpan timeout,
            CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);

            var ct = timeoutCts.Token;
            Exception? lastError = null;
            var stopwatch = Stopwatch.StartNew();
            var activePortFile = Path.Combine(userDataDir, "DevToolsActivePort");

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (process.HasExited)
                    {
                        throw new InvalidOperationException(
                            $"Chromium exited before DevTools became ready. ExitCode={SafeGetExitCode(process)}");
                    }

                    try
                    {
                        if (File.Exists(activePortFile))
                        {
                            var lines = await File.ReadAllLinesAsync(activePortFile, ct).ConfigureAwait(false);
                            if (ChromiumDevToolsEndpoint.TryParse(lines, out var endpoint) &&
                                await CanQueryDevToolsVersionAsync(endpoint!.Port, ct,
                                    expectedWebSocketUrl: endpoint.WebSocketUrl).ConfigureAwait(false))
                                return endpoint;
                        }
                    }
                    catch (IOException ex) { lastError = ex; }
                    catch (UnauthorizedAccessException ex) { lastError = ex; }
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Timed out waiting for Chromium DevToolsActivePort after {stopwatch.Elapsed.TotalSeconds:N1}s. File={activePortFile}",
                    lastError);
            }
        }

        private static int? SafeGetExitCode(Process process)
        {
            try
            {
                if (process.HasExited)
                    return process.ExitCode;
            }
            catch
            {
            }

            return null;
        }

        /// <summary>
        /// 测试 DevTools /json/version 是否可访问
        /// </summary>
        private static async Task<bool> CanQueryDevToolsVersionAsync(
            int port,
            CancellationToken token,
            int timeoutMs = 1500,
            string? expectedWebSocketUrl = null)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            linkedCts.CancelAfter(timeoutMs);
            var ct = linkedCts.Token;

            try
            {
                using var handler = new SocketsHttpHandler
                {
                    UseProxy = false,
                    AutomaticDecompression = DecompressionMethods.None,
                    ConnectTimeout = TimeSpan.FromMilliseconds(timeoutMs)
                };

                using var httpClient = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };

                using var response = await httpClient.GetAsync(
                    $"http://127.0.0.1:{port}/json/version",
                    HttpCompletionOption.ResponseHeadersRead,
                    ct).ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.OK)
                    return false;

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                if (!doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsProp))
                    return false;

                if (wsProp.ValueKind != JsonValueKind.String)
                    return false;

                var wsUrl = wsProp.GetString();
                return Uri.TryCreate(wsUrl, UriKind.Absolute, out var actual) &&
                    actual.Scheme == "ws" && actual.Port == port &&
                    (expectedWebSocketUrl == null ||
                     actual.AbsolutePath == new Uri(expectedWebSocketUrl).AbsolutePath);
            }
            catch (OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task ExpireScanLoopAsync(CancellationToken token)
        {
            try
            {
                using var timer = new PeriodicTimer(_scanInterval);
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    foreach (var session in _sessions.Values)
                    {
                        if (session.ExitConfirmed || session.CloseStarted != 0 || _reclaims.ContainsKey(session.UniqueId)) continue;
                        BrowserStopKind? reason = null;
                        try { if (session.Process.HasExited) reason = BrowserStopKind.BrowserCrashed; } catch { }
                        if (reason == null && Stopwatch.GetElapsedTime(session.CreatedTimestamp) >= session.Lifetime)
                            reason = BrowserStopKind.LifetimeExpired;
                        if (reason == null) continue;
                        var reclaim = Task.Run(() => ReclaimExpiredAsync(session, reason.Value, token));
                        _reclaims[session.UniqueId] = reclaim;
                        _ = reclaim.ContinueWith(_ => _reclaims.TryRemove(session.UniqueId, out var removed),
                            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
        private async Task ReclaimExpiredAsync(ChromiumSession session, BrowserStopKind reason, CancellationToken token)
        {
            session.RequestStop(reason);
            try
            {
                if (reason == BrowserStopKind.LifetimeExpired) await Task.Delay(_expiryGrace, token).ConfigureAwait(false);
                if (_sessions.TryGetValue(session.UniqueId, out var current) && ReferenceEquals(current, session))
                    await CloseAsync(session.UniqueId).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch { } // CloseInternalAsync publishes the failed close, preserving ownership for retry.
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposeStarted) != 0)
                throw new ObjectDisposedException(nameof(ChromiumSessionManager));
        }

        public ValueTask DisposeAsync()
        {
            lock (_startSync)
            {
                if (_disposal == null || _disposal.IsFaulted)
                {
                    Interlocked.Exchange(ref _disposeStarted, 1);
                    _disposal = Task.Run(DisposeCoreAsync);
                }
                return new(_disposal);
            }
        }
        private async Task DisposeCoreAsync()
        {
            _cts.Cancel();
            Task starts;
            lock (_startSync) starts = _startsDrained.Task;
            await starts.ConfigureAwait(false);
            await _expireLoopTask.ConfigureAwait(false);
            await Task.WhenAll(_reclaims.Values.ToArray()).ConfigureAwait(false);
            await CloseAllAsync().ConfigureAwait(false);
            if (!_sessions.IsEmpty) throw new IOException("Some managed Chromium processes have not exited.");
            await _cleanup.DisposeAsync().ConfigureAwait(false);
            _cts.Dispose();
        }
        private static TaskCompletionSource CompletedSource()
        {
            var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            t.SetResult(); return t;
        }
    }
}
