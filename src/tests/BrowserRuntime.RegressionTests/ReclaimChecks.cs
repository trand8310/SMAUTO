using System.ComponentModel;
using Microsoft.Playwright;
using QTP.Common;

internal static class ReclaimChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        {
            var profile = Path.Combine(Path.GetTempPath(), "smad-cleanup-lock-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profile);
            await using var cleanup = new ProfileCleanupQueue(1, retryDelays: [TimeSpan.FromMilliseconds(20)]);
            using (var lockedFile = new FileStream(Path.Combine(profile, "Account Web Data"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                var failed = await cleanup.Enqueue(profile).WaitAsync(TimeSpan.FromSeconds(5));
                check(!failed.Deleted && failed.Attempts == 2 && !string.IsNullOrWhiteSpace(failed.Error),
                    "实际配置文件占用转换为清理结果并完成重试，不产生故障结果任务");
            }
            var recovered = await cleanup.Enqueue(profile).WaitAsync(TimeSpan.FromSeconds(5));
            check(recovered.Deleted && !Directory.Exists(profile), "实际文件句柄释放后重新清理成功");
        }
        check(BrowserFailureClassifier.Classify(new Win32Exception(1455)) == BrowserStopKind.ResourceExhausted,
            "页面文件资源不足具有独立故障分类");
        check(BrowserFailureClassifier.Classify(new AggregateException(new IOException(), new OutOfMemoryException())) == BrowserStopKind.ResourceExhausted,
            "嵌套启动及清理错误仍能识别资源不足");
        check(BrowserFailureClassifier.Classify(new BrowserStartupCanceledException(BrowserStopKind.LifetimeExpired, new OperationCanceledException(), default)) == BrowserStopKind.LifetimeExpired,
            "连接阶段的到期取消保留原始原因");
        check(BrowserFailureClassifier.Classify(new TimeoutException()) == BrowserStopKind.OperationTimedOut &&
            BrowserFailureClassifier.Classify(new OperationCanceledException()) == BrowserStopKind.Canceled,
            "超时与取消分开报告");
        {
            int attempts = 0;
            await using var cleanup = new ProfileCleanupQueue(1, path =>
            {
                if (path.EndsWith("locked")) { Interlocked.Increment(ref attempts); throw new IOException("file locked"); }
                return Task.CompletedTask;
            }, [TimeSpan.FromMilliseconds(400)]);
            var locked = cleanup.Enqueue(Path.Combine(Path.GetTempPath(), "locked"));
            await Until(() => attempts == 1);
            var good = await cleanup.Enqueue(Path.Combine(Path.GetTempPath(), "good")).WaitAsync(TimeSpan.FromMilliseconds(250));
            check(good.Deleted && !locked.IsCompleted, "单个锁定目录的退避不会阻塞其他目录");
            var failed = await locked;
            check(!failed.Deleted && failed.Attempts == 2 && failed.Error == "file locked", "目录清理耗尽重试保留失败结果");
            await cleanup.DrainAsync(); check(cleanup.PendingCount == 0, "失败目录结束后清理队列也归零");
        }
        {
            int attempts = 0;
            await using var cleanup = new ProfileCleanupQueue(1, _ =>
            {
                if (++attempts == 1) throw new IOException("temporary lock");
                return Task.CompletedTask;
            }, [TimeSpan.FromMilliseconds(20)]);
            var first = cleanup.Enqueue(Path.Combine(Path.GetTempPath(), "retry-profile"));
            var second = cleanup.Enqueue(Path.Combine(Path.GetTempPath(), "retry-profile"));
            check(ReferenceEquals(first, second), "同一路径的并发清理共享结果任务");
            var result = await first;
            check(result.Deleted && result.Attempts == 2 && result.Error == null, "目录短暂锁定恢复后报告成功");
        }
        {
            await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
            var processes = new Processes();
            var connecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var manager = new BrowserRuntimeManager(provider, processes, new(1, 1), async (_, _, ct) =>
            {
                connecting.SetResult(); await Task.Delay(Timeout.Infinite, ct); return new Browser().Value;
            });
            var opening = manager.AcquireAsync(Options("expire-connecting"));
            await connecting.Task;
            processes.Sessions["expire-connecting"].RequestStop(BrowserStopKind.LifetimeExpired);
            try { await opening; throw new Exception("Expired connection succeeded"); }
            catch (BrowserStartupCanceledException error)
            { check(error.Reason == BrowserStopKind.LifetimeExpired, "连接未完成时到期会中断连接等待并保留原因"); }
            check(processes.Sessions.IsEmpty && provider.ActiveLeases == 0 && manager.Snapshot.Opening == 0,
                "连接阶段到期也归还进程、租约与启动名额");
        }
        {
            await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
            var processes = new Processes();
            await using var manager = new BrowserRuntimeManager(provider, processes, new(1, 1), (_, _, _) => Task.FromResult(new Browser().Value));
            var lease = await manager.AcquireAsync(Options("expiry-signal"));
            lease.ProcessSession.RequestStop(BrowserStopKind.LifetimeExpired);
            check(lease.Token.IsCancellationRequested && processes.CloseCalls == 0 && lease.StopKind == BrowserStopKind.LifetimeExpired,
                "到期先通知任务取消而非立即强制关闭");
            manager.Reclaimed += _ => throw new Exception("observer failure");
            var results = await Task.WhenAll(lease.CloseWithResultAsync(), lease.CloseWithResultAsync());
            check(results.All(x => x.ExitConfirmed && x.Reason == BrowserStopKind.LifetimeExpired) && processes.CloseCalls == 1,
                "并发回收共享关闭并保留到期原因");
            check(provider.ActiveLeases == 0, "回收观察者异常不影响资源释放");
        }
        {
            await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
            var processes = new Processes(); bool failClose = true;
            processes.Closing = _ => failClose ? Task.FromException(new IOException("process still alive")) : Task.CompletedTask;
            await using var manager = new BrowserRuntimeManager(provider, processes, new(1, 1), (_, _, _) => Task.FromResult(new Browser().Value));
            var lease = await manager.AcquireAsync(Options("close-report"));
            lease.RequestStop(BrowserStopKind.OperationTimedOut);
            var failed = await lease.CloseWithResultAsync();
            check(!failed.ExitConfirmed && failed.CleanupErrors.Contains("process still alive") && provider.ActiveLeases == 1,
                "回收失败结果不伪造进程退出");
            failClose = false; var recovered = await lease.CloseWithResultAsync();
            check(recovered.ExitConfirmed && recovered.Reason == BrowserStopKind.OperationTimedOut && provider.ActiveLeases == 0,
                "关闭重试成功保留原始故障原因");
        }
        {
            await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
            var processes = new Processes();
            await using var manager = new BrowserRuntimeManager(provider, processes, new(2, 1), (_, _, _) => Task.FromResult(new Browser().Value));
            var healthy = await manager.AcquireAsync(Options("resource-healthy"));
            processes.Starting = _ => throw new Win32Exception(1455);
            try { await manager.AcquireAsync(Options("resource-failed")); throw new Exception("Expected resource failure"); }
            catch (Win32Exception) { }
            check(!manager.Snapshot.Accepting && manager.Snapshot.AdmissionStopKind == BrowserStopKind.ResourceExhausted && !healthy.Token.IsCancellationRequested,
                "资源不足停止新浏览器启动但保留健康任务");
            var before = processes.Starts;
            try { await manager.AcquireAsync(Options("resource-blocked")); throw new Exception("Expected rejection"); }
            catch (InvalidOperationException) { }
            check(processes.Starts == before, "资源不足时不继续尝试启动");
            await healthy.DisposeAsync();
        }
    }
    private static BrowserStartOptions Options(string id) => new(id, "fake-chrome", Path.Combine(Path.GetTempPath(), id), TimeSpan.FromMinutes(1));
    private static async Task Until(Func<bool> test)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!test()) await Task.Delay(10, deadline.Token);
    }
}
