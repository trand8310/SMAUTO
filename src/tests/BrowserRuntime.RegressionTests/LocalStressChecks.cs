using System.Diagnostics;
using System.Text.Json;
using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Common;
using QTP.Plugins;
using System.Management;

internal static class LocalStressChecks
{
    private sealed record Sample(int Round, long PrivateBytes, int Handles, int Processes, int DriverLeases, int CdpEntries,
        long DriverPrivateBytes, int DriverHandles, int DriverProcesses);
    private sealed record TouchProbe(bool Dispatched, bool ClickVerified, bool ScriptResponsive, bool OtherSessionResponsive, string? Error)
    {
        public bool V3TapVerified { get; init; }
    }
    public static async Task RunTouchOnlyAsync(string executable, Action<bool, string> check)
    {
        var root = Path.Combine(Environment.CurrentDirectory, "tests/BrowserRuntime.RegressionTests/artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var provider = new PlaywrightProvider();
        await using var processes = new ChromiumSessionManager();
        await using var runtime = new BrowserRuntimeManager(provider, processes, new(2, 1));
        BrowserStartOptions Options(string id, TimeSpan? lifetime) => new(id, Path.GetFullPath(executable), Path.Combine(root, id),
            lifetime ?? TimeSpan.FromMinutes(1), "--headless=new --no-first-run --no-default-browser-check --disable-background-networking about:blank");
        var result = await ProbeTouchAsync(runtime, Options, check);
        var path = Path.Combine(root, "touch-report.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { Executable = Path.GetFullPath(executable), Touch = result }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("TOUCH REPORT " + path);
    }
    public static async Task RunAsync(string executable, Action<bool, string> check)
    {
        var samples = new List<Sample>();
        var root = Path.Combine(Environment.CurrentDirectory, "tests/BrowserRuntime.RegressionTests/artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await using var provider = new PlaywrightProvider();
        await using var processes = new ChromiumSessionManager(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(500));
        await using var runtime = new BrowserRuntimeManager(provider, processes, new(2, 1));
        BrowserStartOptions Options(string id, TimeSpan? lifetime = null) => new(id, Path.GetFullPath(executable), Path.Combine(root, id),
            lifetime ?? TimeSpan.FromMinutes(2), "--headless=new --no-first-run --no-default-browser-check --disable-background-networking about:blank");
        for (int round = 0; round < 6; round++)
        {
            if (round > 0) runtime.BeginRun(new(2, 1));
            using var cancel = new CancellationTokenSource();
            var a = await runtime.AcquireAsync(Options($"round-{round}-a"), cancel.Token);
            var b = await runtime.AcquireAsync(Options($"round-{round}-b"));
            var pageA = await FixtureAsync(a, "a"); var pageB = await FixtureAsync(b, "b");
            var cdpA = new CDPSessionManager(a.Context); var cdpB = new CDPSessionManager(b.Context);
            a.RegisterCleanup(() => cdpA.DisposeAsync().AsTask()); b.RegisterCleanup(() => cdpB.DisposeAsync().AsTask());
            var inputA = await cdpA.GetOrCreateSessionAsync(pageA); var inputB = await cdpB.GetOrCreateSessionAsync(pageB);
            await CdpTouchRuntime.InitializeAsync(pageA, inputA); await CdpTouchRuntime.InitializeAsync(pageB, inputB);
            check(!ReferenceEquals(a.Context, b.Context) && !ReferenceEquals(inputA, inputB) &&
                await pageA.EvaluateAsync<string>("window.owner") == "a" && await pageB.EvaluateAsync<string>("window.owner") == "b",
                $"第 {round + 1} 轮页面与 CDP 分属独立任务");
            try { await cdpA.GetOrCreateSessionAsync(pageB); throw new Exception("Cross-context CDP was accepted"); }
            catch (InvalidOperationException) { check(true, "跨任务页面不能交给其他 CDP 管理器"); }
            try { await CdpTouchRuntime.InitializeAsync(pageA, inputB); throw new Exception("Cross-page input was accepted"); }
            catch (InvalidOperationException) { check(true, "真实 CDP 输入绑定拒绝跨任务页面"); }
            using (var held = await CdpTouchRuntime.AcquireAsync(inputA))
            {
                using var independent = await CdpTouchRuntime.AcquireAsync(inputB).WaitAsync(TimeSpan.FromSeconds(1));
                check(true, "一个任务持有输入锁不阻塞其他任务");
            }
            if (round == 0)
            {
                cancel.Cancel();
                check(a.Token.IsCancellationRequested && !b.Token.IsCancellationRequested, "真实取消只作用于所属浏览器会话");
            }
            else if (round == 1)
            {
                // Clone the process handle so the manager may dispose its own handle independently.
                using var owned = Process.GetProcessById(a.ProcessSession.Process.Id);
                owned.Kill(entireProcessTree: true); await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await Until(() => a.Token.IsCancellationRequested);
                check(!b.Token.IsCancellationRequested, "一个受管进程崩溃不取消健康浏览器");
            }
            await a.DisposeAsync();
            check(await pageB.EvaluateAsync<string>("window.owner").WaitAsync(TimeSpan.FromSeconds(3)) == "b", "故障会话释放后其他任务仍可操作页面");
            var stop = runtime.StopAsync(); await Until(() => b.Token.IsCancellationRequested);
            await b.DisposeAsync(); await stop;
            check(processes.Count == 0 && provider.ActiveLeases == 0 && cdpA.Count == 0 && cdpB.Count == 0,
                $"第 {round + 1} 轮停止后进程、租约与 CDP 缓存归零");
            try { using var stale = await CdpTouchRuntime.AcquireAsync(inputA); throw new Exception("Stale input session reused"); }
            catch (InvalidOperationException) { check(true, "已释放输入会话不可复用"); }
            var cleanups = await Task.WhenAll(a.ProcessSession.DirectoryCleanup!, b.ProcessSession.DirectoryCleanup!);
            check(cleanups.All(x => x.Deleted) && !Directory.Exists(a.ProcessSession.UserDir) && !Directory.Exists(b.ProcessSession.UserDir),
                "真实用户目录清理有明确成功结果");
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var self = Process.GetCurrentProcess();
            var driver = ObserveDriver();
            samples.Add(new(round + 1, self.PrivateMemorySize64, self.HandleCount, processes.Count, provider.ActiveLeases, cdpA.Count + cdpB.Count,
                driver.Bytes, driver.Handles, driver.Count));
        }
        var stable = samples.Skip(1).ToArray();
        check(stable[^1].Handles - stable[0].Handles <= 32, "六轮启停后宿主句柄增长在观测阈值内");
        check(stable[^1].PrivateBytes - stable[0].PrivateBytes <= 64L * 1024 * 1024, "六轮启停后宿主内存增长在观测阈值内");
        check(stable.All(x => x.DriverProcesses == 1), "六轮复用仅保留一个所属 Playwright 驱动进程");
        check(stable[^1].DriverHandles - stable[0].DriverHandles <= 32 && stable[^1].DriverPrivateBytes - stable[0].DriverPrivateBytes <= 64L * 1024 * 1024,
            "六轮启停后驱动内存及句柄增长在观测阈值内");
        runtime.BeginRun(new(2, 1));
        var expiring = await runtime.AcquireAsync(Options("expire", TimeSpan.FromSeconds(2)));
        using (var observed = Process.GetProcessById(expiring.ProcessSession.Process.Id))
        {
            var notified = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            expiring.ProcessSession.StopRequested += reason =>
            {
                if (reason == BrowserStopKind.LifetimeExpired) notified.TrySetResult(!observed.HasExited);
            };
            check(await notified.Task.WaitAsync(TimeSpan.FromSeconds(5)), "真实到期通知发生在强制终止之前");
            check(expiring.Token.IsCancellationRequested && expiring.StopKind == BrowserStopKind.LifetimeExpired, "真实到期取消传播并保留原因");
            await observed.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        var expiryResult = await expiring.CloseWithResultAsync();
        check(expiryResult.ExitConfirmed && expiryResult.ProcessResult?.Forced == true && expiryResult.Reason == BrowserStopKind.LifetimeExpired,
            "任务未响应到期取消时强制回收并报告结果");
        check((await expiring.ProcessSession.DirectoryCleanup!).Deleted, "到期强制回收后目录也得到清理");
        await CheckLockedProfileAsync(executable, root, check);
        var probe = await ProbeTouchAsync(runtime, Options, check);
        await runtime.StopAsync();
        check(processes.Count == 0 && provider.ActiveLeases == 0 && processes.PendingDirectoryCleanups == 0,
            "并发验证与触屏诊断结束后所有受管资源归零");
        await provider.DisposeAsync();
        await Until(() => ObserveDriver().Count == 0);
        check(true, "最终销毁 Provider 后所属驱动进程退出");
        var reportPath = Path.Combine(root, "report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            Executable = Path.GetFullPath(executable), Samples = samples, Touch = probe,
            Note = "Six rounds are a bounded regression observation, not a long-duration memory leak proof."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("STRESS REPORT " + reportPath);
    }
    private static async Task<IPage> FixtureAsync(BrowserRuntimeLease lease, string owner)
    {
        var page = await lease.Context.NewPageAsync();
        await page.SetViewportSizeAsync(400, 800);
        await page.EvaluateAsync("owner => { window.owner=owner; window.clicks=0; window.events=[]; document.body.innerHTML='<button id=\"tap\" style=\"width:150px;height:70px\">tap</button><iframe id=\"child\"></iframe>'; document.querySelector('#tap').onclick=()=>window.clicks++; for(const t of ['touchstart','touchend','touchcancel']) document.addEventListener(t,()=>window.events.push(t)); document.querySelector('#child').srcdoc='<p>local frame</p>'; }", owner).WaitAsync(TimeSpan.FromSeconds(5));
        await Until(() => page.Frames.Count == 2);
        return page;
    }
    private static async Task CheckLockedProfileAsync(string executable, string root, Action<bool, string> check)
    {
        await using var cleanup = new ProfileCleanupQueue(1);
        await using var processes = new ChromiumSessionManager(cleanup: cleanup);
        var locked = await processes.StartChromium("locked", Path.GetFullPath(executable), Path.Combine(root, "locked"), TimeSpan.FromMinutes(1),
            "--headless=new --no-first-run --disable-background-networking about:blank");
        var stream = new FileStream(Path.Combine(locked.UserDir, "owned-lock-fixture"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        try
        {
            await processes.CloseAsync("locked");
            var failed = await locked.DirectoryCleanup!;
            check(locked.LastCloseResult?.ExitConfirmed == true && !failed.Deleted && failed.Error != null,
                "真实锁定目录不会把进程回收误报成目录清理成功");
            try
            {
                await processes.StartChromium("reuse", Path.GetFullPath(executable), locked.UserDir, TimeSpan.FromMinutes(1));
                throw new Exception("Pending profile reused");
            }
            catch (InvalidOperationException) { check(true, "清理失败的目录不能被新浏览器复用"); }
            var healthy = await processes.StartChromium("cleanup-healthy", Path.GetFullPath(executable), Path.Combine(root, "cleanup-healthy"), TimeSpan.FromMinutes(1),
                "--headless=new --no-first-run --disable-background-networking about:blank");
            await processes.CloseAsync(healthy.UniqueId);
            var healthyCleanup = await healthy.DirectoryCleanup!;
            check(healthyCleanup.Deleted, $"真实锁定目录失败不妨碍其他会话目录清理 {healthyCleanup.Error}");
        }
        finally { stream.Dispose(); }
        var retry = await processes.RetryProfileCleanupAsync(locked.UserDir);
        check(retry.Deleted && !Directory.Exists(locked.UserDir), "释放实际文件锁后可重试清理失败目录");
    }
    private static async Task<TouchProbe> ProbeTouchAsync(BrowserRuntimeManager runtime,
        Func<string, TimeSpan?, BrowserStartOptions> options, Action<bool, string> check)
    {
        var probe = await runtime.AcquireAsync(options("touch-probe", null));
        var peer = await runtime.AcquireAsync(options("touch-peer", null));
        bool dispatched = false, verified = false, responsive = false, peerResponsive = false;
        bool v3Verified = false;
        string? error = null;
        try
        {
            var page = await FixtureAsync(probe, "probe"); var peerPage = await FixtureAsync(peer, "peer");
            var cdp = await probe.Context.NewCDPSessionAsync(page);
            probe.RegisterCleanup(async () => { CdpTouchRuntime.Invalidate(cdp); await cdp.DetachAsync().WaitAsync(TimeSpan.FromSeconds(2)); });
            await CdpTouchRuntime.InitializeAsync(page, cdp);
            await cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object> { ["expression"] = "window.clicks", ["returnByValue"] = true }).WaitAsync(TimeSpan.FromSeconds(3));
            try
            {
                await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                    { ["type"] = "touchStart", ["touchPoints"] = new object[] { new { x = 60, y = 40, id = 0 } } }).WaitAsync(TimeSpan.FromSeconds(3));
                await Task.Delay(60);
                await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                    { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() }).WaitAsync(TimeSpan.FromSeconds(3));
                dispatched = true;
                var reading = cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object> { ["expression"] = "window.clicks", ["returnByValue"] = true });
                _ = reading.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                var value = await reading.WaitAsync(TimeSpan.FromSeconds(5));
                responsive = true; verified = value?.GetProperty("result").GetProperty("value").GetInt32() == 1;
                if (verified)
                {
                    var human = new HumanTouchOperator();
                    check(await human.TapAsync(page, cdp, page.Locator("#tap")), "兼容内核上的 v3 Tap 可实际执行");
                    check(await page.EvaluateAsync<int>("window.clicks").WaitAsync(TimeSpan.FromSeconds(3)) == 2, "v3 Tap 产生真实页面点击");
                    var taps = await Task.WhenAll(human.TapAsync(page, cdp, page.Locator("#tap")), human.TapAsync(page, cdp, page.Locator("#tap")));
                    check(taps.All(x => x) && await page.EvaluateAsync<int>("window.clicks") == 4, "兼容内核上的并发 v3 Tap 各执行一次");
                    var events = await page.EvaluateAsync<string[]>("window.events");
                    check(events.SequenceEqual(Enumerable.Range(0, 4).SelectMany(_ => new[] { "touchstart", "touchend" })), "真实触屏事件按完整接触串行且无残留触点");
                    v3Verified = true;
                }
            }
            catch (Exception ex) when (ex is TimeoutException or PlaywrightException or OperationCanceledException)
            { error = ex.GetType().Name + ": " + ex.Message; probe.RequestStop(BrowserStopKind.OperationTimedOut, error); }
            peerResponsive = await peerPage.EvaluateAsync<string>("window.owner").WaitAsync(TimeSpan.FromSeconds(3)) == "peer";
            check(peerResponsive && !peer.Token.IsCancellationRequested, "触屏兼容性诊断未损坏其他浏览器会话");
            Console.WriteLine("TOUCH DIAGNOSTIC " + JsonSerializer.Serialize(new TouchProbe(dispatched, verified, responsive, peerResponsive, error) { V3TapVerified = v3Verified }));
        }
        finally
        {
            await probe.DisposeAsync(); await peer.DisposeAsync();
            await probe.ProcessSession.DirectoryCleanup!; await peer.ProcessSession.DirectoryCleanup!;
        }
        return new(dispatched, verified, responsive, peerResponsive, error) { V3TapVerified = v3Verified };
    }
    private static (long Bytes, int Handles, int Count) ObserveDriver()
    {
        if (!OperatingSystem.IsWindows()) return (0, 0, 0);
        long bytes = 0; int handles = 0, count = 0;
        using var query = new ManagementObjectSearcher($"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId={Environment.ProcessId} AND Name='node.exe'");
        using var children = query.Get();
        foreach (ManagementObject child in children)
        using (child)
        {
            try
            {
                using var process = Process.GetProcessById(Convert.ToInt32(child["ProcessId"]));
                if (!process.HasExited) { bytes += process.PrivateMemorySize64; handles += process.HandleCount; count++; }
            }
            catch (ArgumentException) { }
        }
        return (bytes, handles, count);
    }
    private static async Task Until(Func<bool> test)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!test()) await Task.Delay(10, deadline.Token);
    }
}
