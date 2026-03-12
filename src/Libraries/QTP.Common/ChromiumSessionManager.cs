using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
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
        private readonly SemaphoreSlim _launchLimiter = new(10); // Windows 并发启动限流
        private readonly Channel<ChromiumSession> _cleanupQueue = Channel.CreateUnbounded<ChromiumSession>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });

        private readonly CancellationTokenSource _cts = new();
        private readonly Task _cleanupLoopTask;
        private readonly Task _expireLoopTask;

        public ChromiumSessionManager()
        {
            _cleanupLoopTask = CleanupLoopAsync(_cts.Token);
            _expireLoopTask = ExpireScanLoopAsync(_cts.Token);
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
            token.ThrowIfCancellationRequested();
            await _launchLimiter.WaitAsync(token);

            int port = 0;
            Process? proc = null;
            var started = false;

            try
            {
                port = RemotePortManager.AcquirePort();

                var fullArgs = $"{arguments} --user-data-dir=\"{userDataDir}\" --remote-debugging-port={port}";
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = fullArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                proc = Process.Start(psi);
                if (proc == null)
                    throw new InvalidOperationException($"Unable to start chromium process: {exePath}");

                started = true;

                var readyTs = readyTimeout ?? TimeSpan.FromSeconds(20);
                await WaitForDebugPortReadyAsync(proc, port, readyTs, token);

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

                // 正常情况下 uniqueId 为 GUID，不应命中旧 session；这里只是防御性兜底
                if (_sessions.TryGetValue(uniqueId, out var oldSession))
                {
                    if (Interlocked.Exchange(ref oldSession.CloseStarted, 1) == 0)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await CloseInternalAsync(oldSession);
                            }
                            catch
                            {
                                // fire-and-forget，忽略异常
                            }
                            finally
                            {
                                _sessions.TryRemove(uniqueId, out _);
                            }
                        });
                    }
                }

                _sessions[uniqueId] = session;
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
                    catch { }

                    try
                    {
                        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await proc.WaitForExitAsync(waitCts.Token);
                    }
                    catch { }

                    try
                    {
                        proc.Dispose();
                    }
                    catch { }
                }

                if (port != 0)
                {
                    try { RemotePortManager.Release(port); } catch { }
                }

                if (started)
                {
                    _ = CleanupUserDirLaterAsync(userDataDir);
                }

                throw;
            }
            finally
            {
                _launchLimiter.Release();
            }
        }

        /// <summary>
        /// 关闭指定会话
        /// 说明：关闭属于清理动作，不依赖外部业务 token。
        /// </summary>
        public async Task CloseAsync(string uniqueId, CancellationToken token = default)
        {
            if (!_sessions.TryGetValue(uniqueId, out var session))
                return;

            if (Interlocked.Exchange(ref session.CloseStarted, 1) == 1)
                return;

            try
            {
                await CloseInternalAsync(session);
            }
            finally
            {
                _sessions.TryRemove(uniqueId, out _);
            }
        }

        /// <summary>
        /// 关闭 Chromium 进程、释放端口、安排缓存目录清理。
        /// 不依赖外部业务 token，全部使用内部超时控制。
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
                        // 进程可能已经退出，忽略
                    }

                    try
                    {
                        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await session.Process.WaitForExitAsync(waitCts.Token);
                    }
                    catch
                    {
                        // 等待退出失败/超时，也继续做 finally 清理
                    }
                }
            }
            catch
            {
                // 清理阶段尽力而为，不再向上打断
            }
            finally
            {
                try
                {
                    session.Process.Dispose();
                }
                catch { }

                try
                {
                    RemotePortManager.Release(session.DebugPort);
                }
                catch { }

                try
                {
                    using var cleanupQueueCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _cleanupQueue.Writer.WriteAsync(session, cleanupQueueCts.Token);
                }
                catch
                {
                    // 队列不可写/manager停止时，直接降级为后台删目录
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
                        timeoutMs: 1000,
                        token: ct);

                    if (tcpOk)
                    {
                        var versionOk = await CanQueryDevToolsVersionAsync(
                            port: port,
                            token: ct,
                            timeoutMs: 1500);

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

                await Task.Delay(200, ct);
            }
        }

        private static int? SafeGetExitCode(Process process)
        {
            try
            {
                if (process.HasExited)
                    return process.ExitCode;
            }
            catch { }

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
                await client.ConnectAsync(host, port, ct);
#else
            var connectTask = client.ConnectAsync(host, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct));
            if (completed != connectTask)
                return false;
            await connectTask;
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
        /// 不依赖 HttpClient，避免额外复杂度
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
                    ct);

                if (response.StatusCode != HttpStatusCode.OK)
                    return false;

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

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

        private async Task CleanupLoopAsync(CancellationToken token = default)
        {
            try
            {
                await foreach (var session in _cleanupQueue.Reader.ReadAllAsync(token))
                {
                    await TryDeleteDirectoryWithRetryAsync(session.UserDir);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
            catch
            {
                // 后台循环不要炸出
            }
        }

        private async Task CleanupUserDirLaterAsync(string userDir)
        {
            try
            {
                await TryDeleteDirectoryWithRetryAsync(userDir);
            }
            catch { }
        }

        /// <summary>
        /// 删除缓存目录：不依赖业务 token，尽量清干净
        /// </summary>
        private async Task TryDeleteDirectoryWithRetryAsync(string userDir)
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
                        await Task.Delay(delay);

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

                while (await timer.WaitForNextTickAsync(token))
                {
                    var now = DateTime.UtcNow;

                    foreach (var session in _sessions.Values)
                    {
                        token.ThrowIfCancellationRequested();

                        if (now >= session.ExpireAt)
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await CloseAsync(session.UniqueId, CancellationToken.None);
                                }
                                catch
                                {
                                    // 后台过期清理，忽略异常
                                }
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常退出
            }
            catch
            {
                // 后台扫描不要影响主流程
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _cts.Cancel();
            }
            catch { }

            var closeTasks = new List<Task>();

            foreach (var uniqueId in _sessions.Keys)
            {
                closeTasks.Add(CloseAsync(uniqueId, CancellationToken.None));
            }

            try
            {
                await Task.WhenAll(closeTasks);
            }
            catch
            {
                // 尽力而为
            }

            _cleanupQueue.Writer.TryComplete();

            try
            {
                await _cleanupLoopTask;
            }
            catch { }

            try
            {
                await _expireLoopTask;
            }
            catch { }

            _launchLimiter.Dispose();
            _cts.Dispose();
        }
    }
}
