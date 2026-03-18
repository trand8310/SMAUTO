using QTP.Common.Win32;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;

namespace QTP.Common
{
    public sealed class ChromiumSession
    {
        public string UniqueId { get; init; } = "";
        public string? Proxy { get; init; }
        public Process Process { get; init; } = null!;
        public int DebugPort { get; init; }
        public string UserDir { get; init; } = "";
        public DateTime ExpireAt { get; init; }

        // 0 = 未关闭，1 = 关闭中/已关闭
        public int CloseStarted;
    }

    public sealed class ChromiumSessionManager : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, ChromiumSession> _sessions = new();

        // 启动并发建议先保守一点，避免页面文件和系统资源被瞬间打爆
        private readonly SemaphoreSlim _launchLimiter = new(4, 4);

        private readonly Channel<ChromiumSession> _cleanupQueue = Channel.CreateUnbounded<ChromiumSession>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _cleanupLoopTask;
        private readonly Task _expireLoopTask;

        private int _disposeStarted;

        public ChromiumSessionManager()
        {
            _cleanupLoopTask = CleanupLoopAsync(_cts.Token);
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
        public async Task<ChromiumSession> StartChromium(
            string uniqueId,
            string exePath,
            string userDataDir,
            TimeSpan ttl,
            string arguments = "--incognito",
            string? proxyServer = null,
            TimeSpan? readyTimeout = null,
            CancellationToken token = default)
        {
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(uniqueId))
                throw new ArgumentNullException(nameof(uniqueId));
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentNullException(nameof(exePath));
            if (string.IsNullOrWhiteSpace(userDataDir))
                throw new ArgumentNullException(nameof(userDataDir));
            if (ttl <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(ttl));

            var entered = false;
            var started = false;
            int port = 0;
            Process? proc = null;

            try
            {
                await _launchLimiter.WaitAsync(token).ConfigureAwait(false);
                entered = true;

                ThrowIfDisposed();

                if (_sessions.ContainsKey(uniqueId))
                    throw new InvalidOperationException($"Chromium session already exists. uniqueId={uniqueId}");

                port = RemotePortManager.AcquirePort();

                var fullArgs = $"{arguments} --user-data-dir=\"{userDataDir}\" --remote-debugging-port={port}";
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = fullArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                try
                {
                    proc = Process.Start(psi);
                    if (proc == null)
                        throw new InvalidOperationException($"Unable to start chromium process: {exePath}");
                }
                catch (Win32Exception ex) when (ex.NativeErrorCode == 1455)
                {
                    if (ShouldRestartFor1455())
                    {
                        SafeRestartHelper.RequestSystemRestart("连续 1455，系统资源不足");
                    }
                    throw new InvalidOperationException($"Chromium 启动失败：页面文件太小或系统提交内存不足。uniqueId={uniqueId}, exePath={exePath}",ex);

                    //throw new InvalidOperationException($"Chromium 启动失败：页面文件太小或系统提交内存不足。uniqueId={uniqueId}, exePath={exePath}", ex);
                }






                started = true;

                var readyTs = readyTimeout ?? TimeSpan.FromSeconds(10);
                await WaitForDebugPortReadyAsync(proc, port, readyTs, token).ConfigureAwait(false);

                var session = new ChromiumSession
                {
                    UniqueId = uniqueId,
                    Proxy = proxyServer,
                    Process = proc,
                    DebugPort = port,
                    UserDir = userDataDir,
                    ExpireAt = DateTime.UtcNow.Add(ttl),
                    CloseStarted = 0
                };

                if (!_sessions.TryAdd(uniqueId, session))
                    throw new InvalidOperationException($"Failed to register chromium session. uniqueId={uniqueId}");

                return session;
            }
            catch
            {
                if (proc != null)
                {
                    try
                    {
                        if (!proc.HasExited)
                            proc.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    try
                    {
                        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await proc.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                    }

                    try
                    {
                        proc.Dispose();
                    }
                    catch
                    {
                    }
                }

                if (port != 0)
                {
                    try
                    {
                        RemotePortManager.Release(port);
                    }
                    catch
                    {
                    }
                }

                if (started)
                {
                    _ = CleanupUserDirLaterAsync(userDataDir);
                }

                throw;
            }
            finally
            {
                if (entered)
                    _launchLimiter.Release();
            }
        }

        /// <summary>
        /// 关闭指定会话
        /// </summary>
        public async Task CloseAsync(string uniqueId)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
                return;

            if (!_sessions.TryGetValue(uniqueId, out var session))
                return;

            if (Interlocked.Exchange(ref session.CloseStarted, 1) == 1)
                return;

            try
            {
                await CloseInternalAsync(session).ConfigureAwait(false);
            }
            finally
            {
                _sessions.TryRemove(uniqueId, out _);
            }
        }

        /// <summary>
        /// 关闭全部会话
        /// </summary>
        public async Task CloseAllAsync()
        {
            var tasks = _sessions.Keys.Select(CloseAsync).ToArray();
            if (tasks.Length == 0)
                return;

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch
            {
                // 尽力而为
            }
        }

        /// <summary>
        /// 关闭 Chromium 进程、释放端口、安排缓存目录清理
        /// </summary>
        private async Task CloseInternalAsync(ChromiumSession session)
        {
            try
            {
                if (!session.Process.HasExited)
                {
                    try
                    {
                        session.Process.Kill(entireProcessTree: true);
                    }
                    catch
                    {
                    }

                    try
                    {
                        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await session.Process.WaitForExitAsync(waitCts.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                try
                {
                    session.Process.Dispose();
                }
                catch
                {
                }

                try
                {
                    RemotePortManager.Release(session.DebugPort);
                }
                catch
                {
                }

                try
                {
                    using var writeCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await _cleanupQueue.Writer.WriteAsync(session, writeCts.Token).ConfigureAwait(false);
                }
                catch
                {
                    _ = CleanupUserDirLaterAsync(session.UserDir);
                }
            }
        }

        /// <summary>
        /// 等待 Chromium 的 remote debugging port 真正 ready
        /// </summary>
        private static async Task WaitForDebugPortReadyAsync(
            Process process,
            int port,
            TimeSpan timeout,
            CancellationToken token)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);

            var ct = timeoutCts.Token;
            Exception? lastError = null;
            var start = DateTime.UtcNow;

            while (true)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (process.HasExited)
                    {
                        throw new InvalidOperationException(
                            $"Chromium exited before debug port became ready. ExitCode={SafeGetExitCode(process)}");
                    }

                    var tcpOk = await CanConnectTcpAsync(
                        host: "127.0.0.1",
                        port: port,
                        timeoutMs: 5000,
                        token: ct).ConfigureAwait(false);

                    if (tcpOk)
                    {
                        var versionOk = await CanQueryDevToolsVersionAsync(
                            port: port,
                            token: ct,
                            timeoutMs: 5000).ConfigureAwait(false);

                        if (versionOk)
                            return;
                    }
                }
                catch (OperationCanceledException)
                {
                    token.ThrowIfCancellationRequested();

                    var elapsed = DateTime.UtcNow - start;
                    throw new TimeoutException(
                        $"Timed out waiting for Chromium debug port {port} to become ready after {elapsed.TotalSeconds:N1}s.",
                        lastError);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
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
        /// 测试 TCP 连接
        /// </summary>
        private static async Task<bool> CanConnectTcpAsync(
            string host,
            int port,
            int timeoutMs,
            CancellationToken token)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            linkedCts.CancelAfter(timeoutMs);
            var ct = linkedCts.Token;

            try
            {
                using var client = new TcpClient();

#if NET8_0_OR_GREATER
                await client.ConnectAsync(host, port, ct).ConfigureAwait(false);
#else
                var connectTask = client.ConnectAsync(host, port);
                var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
                if (completed != connectTask)
                    return false;
                await connectTask.ConfigureAwait(false);
#endif

                return client.Connected;
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

        /// <summary>
        /// 测试 DevTools /json/version 是否可访问
        /// </summary>
        private static async Task<bool> CanQueryDevToolsVersionAsync(
            int port,
            CancellationToken token,
            int timeoutMs = 1500)
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
                return !string.IsNullOrWhiteSpace(wsUrl);
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

        private async Task CleanupLoopAsync(CancellationToken token)
        {
            try
            {
                await foreach (var session in _cleanupQueue.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    await TryDeleteDirectoryWithRetryAsync(session.UserDir).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        private async Task CleanupUserDirLaterAsync(string userDir)
        {
            try
            {
                await TryDeleteDirectoryWithRetryAsync(userDir).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 删除缓存目录：不依赖业务 token，尽量清干净
        /// </summary>
        private static async Task TryDeleteDirectoryWithRetryAsync(string userDir)
        {
            if (string.IsNullOrWhiteSpace(userDir))
                return;

            var delays = new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(30)
            };

            foreach (var delay in delays)
            {
                try
                {
                    if (!Directory.Exists(userDir))
                        return;

                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay).ConfigureAwait(false);

                    Directory.Delete(userDir, recursive: true);
                    return;
                }
                catch
                {
                    // 继续重试
                }
            }
        }

        private async Task ExpireScanLoopAsync(CancellationToken token)
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var now = DateTime.UtcNow;
                    List<string>? expiredIds = null;

                    foreach (var kv in _sessions)
                    {
                        token.ThrowIfCancellationRequested();

                        var session = kv.Value;
                        if (now >= session.ExpireAt)
                        {
                            expiredIds ??= new List<string>();
                            expiredIds.Add(kv.Key);
                        }
                    }

                    if (expiredIds == null || expiredIds.Count == 0)
                        continue;

                    foreach (var uniqueId in expiredIds)
                    {
                        try
                        {
                            await CloseAsync(uniqueId).ConfigureAwait(false);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        private static bool ShouldRestartFor1455()
        {
            var ms = SystemMemoryHelper.GetMemoryStatus();

            var memoryLoad = ms.dwMemoryLoad;
            var availPhysMb = SystemMemoryHelper.ToMb(ms.ullAvailPhys);
            var availPageFileMb = SystemMemoryHelper.ToMb(ms.ullAvailPageFile);

            var dangerous =
                memoryLoad >= 88 ||
                availPhysMb <= 1024 ||
                availPageFileMb <= 1024;

            if (!dangerous)
                return false;

            return MemoryCrisisGuard.ShouldRestartNow();


        }
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposeStarted) != 0)
                throw new ObjectDisposedException(nameof(ChromiumSessionManager));
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
                return;

            try
            {
                _cts.Cancel();
            }
            catch
            {
            }

            try
            {
                await CloseAllAsync().ConfigureAwait(false);
            }
            catch
            {
            }

            _cleanupQueue.Writer.TryComplete();

            try
            {
                await _cleanupLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }

            try
            {
                await _expireLoopTask.ConfigureAwait(false);
            }
            catch
            {
            }

            _launchLimiter.Dispose();
            _cts.Dispose();
        }
    }
}