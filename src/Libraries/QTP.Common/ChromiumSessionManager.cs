using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace QTP.Common
{
    public sealed record ChromiumSession(
        string UniqueId,
        string? Proxy,
        Process Process,
        int DebugPort,
        string UserDir,
        DateTime ExpireAt
    );


    public sealed class ChromiumSessionManager : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<string, ChromiumSession> _sessions = new();
        private readonly SemaphoreSlim _launchLimiter = new(10); // Windows 启动限流,并发启动10个进程
        private readonly Channel<ChromiumSession> _cleanupQueue = Channel.CreateUnbounded<ChromiumSession>();
        private readonly CancellationTokenSource _cts = new();

        public ChromiumSessionManager()
        {
            _ = CleanupLoopAsync(_cts.Token);
            _ = ExpireScanLoopAsync(_cts.Token);
        }

        public async Task<ChromiumSession> StartChromium(string uniqueId, string exePath, string userDataDir, TimeSpan ttl, string arguments = "--incognito", string? proxyServer = null)
        {
            await _launchLimiter.WaitAsync();
            try
            {
                var port = RemotePortManager.AcquirePort();
                var fullArgs = $"{arguments} --user-data-dir=\"{userDataDir}\" --remote-debugging-port={port}";
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = fullArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var proc = Process.Start(psi)!;

                var session = new ChromiumSession(
                    uniqueId,
                    proxyServer,
                    proc,
                    port,
                    userDataDir,
                    DateTime.UtcNow.Add(ttl)
                );

                _sessions[uniqueId] = session;
                return session;
            }
            finally
            {
                _launchLimiter.Release();
            }
        }

        public async Task CloseAsync(string uniqueId)
        {
            if (!_sessions.TryRemove(uniqueId, out var s))
                return;

            try
            {
                if (!s.Process.HasExited)
                {
                    s.Process.Kill(true);
                    await s.Process.WaitForExitAsync();
                }
            }
            catch { }

            RemotePortManager.Release(s.DebugPort);
            await _cleanupQueue.Writer.WriteAsync(s);
        }

        private async Task CleanupLoopAsync(CancellationToken token)
        {
            await foreach (var s in _cleanupQueue.Reader.ReadAllAsync(token))
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), token); // 给 Windows 释放句柄
                    Directory.Delete(s.UserDir, true);
                }
                catch { /* Windows 删不掉很常见，忽略 */ }
            }
        }

        private async Task ExpireScanLoopAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

            while (await timer.WaitForNextTickAsync(token))
            {
                var now = DateTime.UtcNow;

                foreach (var s in _sessions.Values)
                {
                    if (now >= s.ExpireAt)
                    {
                        _ = CloseAsync(s.UniqueId);
                    }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();

            foreach (var taskId in _sessions.Keys)
            {
                _ = CloseAsync(taskId);
            }
        }
    }

}
