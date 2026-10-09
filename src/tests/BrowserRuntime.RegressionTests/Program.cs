using System.Collections.Concurrent;
using System.Reflection;
using MainClient.UiTask;
using Microsoft.Playwright;
using QTP.Common;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    checks++; Console.WriteLine($"PASS {name}");
}
async Task Throws<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); } catch (T) { Check(true, name); return; }
    throw new Exception("Expected failure: " + name);
}
TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
async Task Until(Func<bool> condition)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    while (!condition()) await Task.Delay(10, deadline.Token);
}
BrowserStartOptions Options(string id, string? profile = null) => new(id, "fake-chrome", profile ?? Path.Combine(Path.GetTempPath(), "runtime-regression", id), TimeSpan.FromMinutes(1));

// Provider generations, cancellation isolation and disposal ordering.
{
    var created = new TaskCompletionSource<IPlaywright>(TaskCreationOptions.RunContinuationsAsynchronously);
    int factories = 0, disposed = 0;
    var driver = Fake.Create<IPlaywright>((m, _) => { if (m.Name == "Dispose") Interlocked.Increment(ref disposed); return null; });
    var provider = new PlaywrightProvider(() => { Interlocked.Increment(ref factories); return created.Task; });
    using var cancel = new CancellationTokenSource();
    var canceled = provider.AcquireAsync(cancel.Token);
    var live = provider.AcquireAsync();
    await Until(() => factories == 1); cancel.Cancel();
    await Throws<OperationCanceledException>(async () => await canceled, "取消一个初始化等待者不影响其他任务");
    created.SetResult(driver);
    var lease = await live;
    Check(factories == 1 && provider.ActiveLeases == 1, "并发初始化仅创建一个驱动");
    var stopping = provider.DisposeAsync().AsTask();
    await Until(() => lease.DriverToken.IsCancellationRequested);
    Check(!stopping.IsCompleted && disposed == 0, "驱动销毁等待活动租约归还");
    await Throws<ObjectDisposedException>(async () => await provider.AcquireAsync(), "停止后拒绝借用驱动");
    await lease.DisposeAsync(); await lease.DisposeAsync(); await stopping; await provider.DisposeAsync();
    Check(disposed == 1 && provider.ActiveLeases == 0, "驱动与租约重复释放只执行一次");
}
{
    int factories = 0, disposed = 0;
    var provider = new PlaywrightProvider(() =>
    {
        if (Interlocked.Increment(ref factories) == 1) throw new IOException("factory failure");
        return Task.FromResult(Fake.Create<IPlaywright>((m, _) => { if (m.Name == "Dispose") Interlocked.Increment(ref disposed); return null; }));
    });
    await Throws<IOException>(async () => await provider.AcquireAsync(), "驱动初始化失败不会永久缓存故障");
    var old = await provider.AcquireAsync();
    provider.Invalidate(old);
    var fresh = await provider.AcquireAsync();
    Check(old.DriverToken.IsCancellationRequested && !fresh.DriverToken.IsCancellationRequested && factories == 3, "驱动失效只取消旧代租约并允许创建新代");
    Check(disposed == 0, "失效驱动仍等待借用者释放");
    await old.DisposeAsync(); await fresh.DisposeAsync(); await provider.DisposeAsync();
    Check(disposed == 2, "旧代与新代驱动均被释放");
}
{
    var pending = new TaskCompletionSource<IPlaywright>(TaskCreationOptions.RunContinuationsAsynchronously);
    int disposed = 0;
    var provider = new PlaywrightProvider(() => pending.Task);
    var acquire = provider.AcquireAsync();
    var stop = provider.DisposeAsync().AsTask();
    pending.SetResult(Fake.Create<IPlaywright>((m, _) => { if (m.Name == "Dispose") disposed++; return null; }));
    await Throws<ObjectDisposedException>(async () => await acquire, "初始化期间退出不会返回已停止的驱动");
    await stop; Check(disposed == 1, "退出期间完成的初始化也被清理");
}
// Process exit, rather than connection closure, controls the running permit.
{
    await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
    var processes = new Processes();
    var release = Gate();
    processes.Closing = id => id == "a" ? release.Task : Task.CompletedTask;
    await using var manager = new BrowserRuntimeManager(provider, processes, new(1, 2), (_, _, _) => Task.FromResult(new Browser().Value));
    var a = await manager.AcquireAsync(Options("a"));
    var closing = a.DisposeAsync().AsTask();
    await Until(() => processes.CloseCalls == 1);
    var b = manager.AcquireAsync(Options("b"));
    await Until(() => manager.Snapshot.Waiting == 1);
    Check(processes.Starts == 1 && !b.IsCompleted && provider.ActiveLeases == 1, "进程退出前保留运行名额与驱动租约");
    release.SetResult(); await closing;
    var next = await b; Check(processes.Starts == 2, "确认进程退出后下一个任务才能启动");
    await next.DisposeAsync();
    Check(manager.Snapshot.Running == 0 && provider.ActiveLeases == 0, "会话完成释放全部资源");
    manager.BeginRun(new(2, 1));
    Check(manager.Snapshot.MaximumRunning == 2 && manager.Snapshot.MaximumLaunching == 1, "下一轮可独立配置启动与运行上限");
}
{
    await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
    var processes = new Processes(); var starting = Gate();
    processes.Starting = async ct => await starting.Task.WaitAsync(ct);
    await using var manager = new BrowserRuntimeManager(provider, processes, new(3, 1), (_, _, _) => Task.FromResult(new Browser().Value));
    var a = manager.AcquireAsync(Options("launch-a"));
    await Until(() => processes.Starts == 1);
    var b = manager.AcquireAsync(Options("launch-b"));
    using var cancel = new CancellationTokenSource();
    var c = manager.AcquireAsync(Options("launch-c"), cancel.Token);
    await Until(() => manager.Snapshot.Waiting == 2);
    Check(manager.Snapshot.Opening == 1 && processes.Starts == 1, "启动并发上限独立限制建连阶段");
    cancel.Cancel(); await Throws<OperationCanceledException>(async () => await c, "排队取消不会启动浏览器");
    await Throws<InvalidOperationException>(async () => await manager.AcquireAsync(Options("launch-a")), "拒绝重复执行标识");
    await Throws<InvalidOperationException>(async () => await manager.AcquireAsync(Options("other", Options("launch-a").UserDataDir)), "拒绝并发共享用户数据目录");
    starting.SetResult(); var leases = await Task.WhenAll(a, b);
    Check(processes.Starts == 2 && manager.Snapshot.Running == 2, "启动名额在建连后释放且运行会话可并存");
    await Task.WhenAll(leases.Select(x => x.DisposeAsync().AsTask()));
}
{
    await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
    var processes = new Processes(); int connects = 0;
    await using var manager = new BrowserRuntimeManager(provider, processes, new(1, 1), (_, _, _) =>
        Interlocked.Increment(ref connects) == 1 ? Task.FromException<IBrowser>(new IOException("connect failure")) : Task.FromResult(new Browser().Value));
    await Throws<IOException>(async () => await manager.AcquireAsync(Options("connect-fail")), "连接失败向调用方报告");
    Check(processes.Sessions.IsEmpty && provider.ActiveLeases == 0 && processes.CloseCalls == 1, "连接失败关闭已启动进程并归还租约");
    var healthy = await manager.AcquireAsync(Options("healthy")); await healthy.DisposeAsync();
    Check(connects == 2, "连接失败后并发名额可以继续使用");
}
{
    await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
    var processes = new Processes(); var browser = new Browser();
    await using var manager = new BrowserRuntimeManager(provider, processes, new(1, 1), (_, _, _) => Task.FromResult(browser.Value));
    var lease = await manager.AcquireAsync(Options("disconnect"));
    browser.Disconnect();
    Check(lease.Token.IsCancellationRequested && lease.StopReason == "Browser disconnected", "浏览器断开取消所属任务");
    lease.RegisterCleanup(() => throw new IOException("cleanup error"));
    await Task.WhenAll(lease.DisposeAsync().AsTask(), lease.DisposeAsync().AsTask());
    Check(lease.CleanupErrors.Count == 1 && processes.CloseCalls == 1 && browser.ContextCloses == 0, "页面清理失败仍关闭进程且不单独关闭借用的默认上下文");
}
{
    await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
    var processes = new Processes(); bool fail = true;
    processes.Closing = _ => fail ? Task.FromException(new IOException("exit unconfirmed")) : Task.CompletedTask;
    await using var manager = new BrowserRuntimeManager(provider, processes, new(1, 1), (_, _, _) => Task.FromResult(new Browser().Value));
    var lease = await manager.AcquireAsync(Options("retry-close"));
    await Throws<IOException>(async () => await lease.DisposeAsync(), "进程退出未确认时报告释放失败");
    Check(manager.Snapshot.Closing == 1 && provider.ActiveLeases == 1, "释放失败保留所有权与并发名额");
    fail = false; await lease.DisposeAsync();
    Check(provider.ActiveLeases == 0 && processes.Sessions.IsEmpty, "后续重试成功才归还资源");
}
{
    await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
    var processes = new Processes();
    await using var manager = new BrowserRuntimeManager(provider, processes, new(1, 1), (_, _, _) => Task.FromResult(new Browser().Value));
    var lease = await manager.AcquireAsync(Options("stop-a"));
    var pending = manager.AcquireAsync(Options("stop-b"));
    var stop = manager.StopAsync();
    await Throws<OperationCanceledException>(async () => await pending, "停止取消仍在排队的任务");
    Check(lease.Token.IsCancellationRequested && !stop.IsCompleted, "停止等待运行任务归还租约");
    await lease.DisposeAsync(); await stop;
    await Throws<InvalidOperationException>(async () => await manager.AcquireAsync(Options("stopped")), "停止后禁止新任务进入");
    manager.BeginRun(new(1, 1));
    var restarted = await manager.AcquireAsync(Options("restart")); await restarted.DisposeAsync();
    Check(processes.Starts == 2, "完整停止后可开始新一轮任务");
}
{
    await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
    var processes = new Processes { FailAfterStart = true }; bool failClose = true;
    processes.Closing = _ => failClose ? Task.FromException(new IOException("exit unconfirmed")) : Task.CompletedTask;
    await using var manager = new BrowserRuntimeManager(provider, processes, new(1, 1), (_, _, _) => Task.FromResult(new Browser().Value));
    await Throws<IOException>(async () => await manager.AcquireAsync(Options("partial-start")), "启动失败且退出未确认时报告清理错误");
    Check(manager.Snapshot.Running == 1 && provider.ActiveLeases == 1 && processes.Sessions.Count == 1, "部分启动失败保留隔离会话及运行名额");
    failClose = false; await manager.CloseSessionAsync("partial-start");
    Check(manager.Snapshot.Running == 0 && provider.ActiveLeases == 0, "隔离会话可通过执行标识重试释放");
}
{
    await using var provider = new PlaywrightProvider(() => Task.FromResult(Fake.Create<IPlaywright>((_, _) => null)));
    var processes = new Processes(); var browsers = new List<Browser>();
    await using var manager = new BrowserRuntimeManager(provider, processes, new(2, 1), (_, _, _) =>
    { var browser = new Browser(); browsers.Add(browser); return Task.FromResult(browser.Value); });
    var a = await manager.AcquireAsync(Options("isolated-a")); var b = await manager.AcquireAsync(Options("isolated-b"));
    browsers[0].Disconnect();
    Check(a.Token.IsCancellationRequested && !b.Token.IsCancellationRequested, "一个浏览器断开不会取消其他会话");
    await a.DisposeAsync(); await b.DisposeAsync();
}
// Pipeline completion includes consumer cleanup, even after cancellation or producer failure.
{
    var entered = Gate(); var cleanup = Gate(); using var cancel = new CancellationTokenSource();
    var pipeline = new PipelineRunner<int>(1, 1, async (writer, ct) => { await writer.WriteAsync(1, ct); },
        async (_, _, ct) => { entered.SetResult(); try { await Task.Delay(Timeout.Infinite, ct); } finally { await cleanup.Task; } });
    var run = pipeline.RunAsync(cancel.Token); await entered.Task; cancel.Cancel();
    await Task.Delay(30); Check(!run.IsCompleted, "取消流水线等待消费者的资源清理");
    cleanup.SetResult(); await run; Check(run.IsCompletedSuccessfully, "消费者清理结束后停止完成");
}
{
    var entered = Gate(); var cleanup = Gate();
    var pipeline = new PipelineRunner<int>(1, 1,
        async (writer, ct) => { await writer.WriteAsync(1, ct); await entered.Task; throw new IOException("producer failed"); },
        async (_, _, ct) => { entered.SetResult(); try { await Task.Delay(Timeout.Infinite, ct); } finally { await cleanup.Task; } });
    var run = pipeline.RunAsync(default); await entered.Task; await Task.Delay(30);
    Check(!run.IsCompleted, "生产者失败也等待运行任务清理");
    cleanup.SetResult(); await Throws<IOException>(async () => await run, "生产者异常在清理结束后报告");
}
{
    var pipeline = new PipelineRunner<int>(1, 1,
        async (writer, ct) => { for (int i = 0; i < 100; i++) await writer.WriteAsync(i, ct); },
        (_, _, _) => Task.FromException(new IOException("consumer failed")));
    pipeline.Faulted += _ => throw new InvalidOperationException("event failed");
    await Throws<InvalidOperationException>(async () => await pipeline.RunAsync(default).WaitAsync(TimeSpan.FromSeconds(5)), "消费者事件异常会取消受队列背压阻塞的生产者");
}
await ReclaimChecks.RunAsync(Check);
if (args.Contains("--touch-probe"))
    await LocalStressChecks.RunTouchOnlyAsync(args[0], Check);
else if (args.Contains("--stress"))
    await LocalStressChecks.RunAsync(args[0], Check);
else if (args.Length > 0)
{
    await using var provider = new PlaywrightProvider();
    await using var processes = new ChromiumSessionManager();
    await using var manager = new BrowserRuntimeManager(provider, processes, new(2, 1));
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var root = Path.Combine(Environment.CurrentDirectory, "tests", "BrowserRuntime.RegressionTests", "artifacts", Guid.NewGuid().ToString("N"));
    var sessions = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => manager.AcquireAsync(new BrowserStartOptions(
        $"real-{i}", Path.GetFullPath(args[0]), Path.Combine(root, i.ToString()), TimeSpan.FromMinutes(2),
        "--headless=new --no-first-run --no-default-browser-check --disable-background-networking about:blank"), timeout.Token)));
    Check(sessions.All(s => s.Browser.IsConnected) && provider.ActiveLeases == 2, "真实 Chromium 并发连接共享驱动并持有独立租约");
    Check(sessions[0].ProcessSession.DebugPort != sessions[1].ProcessSession.DebugPort, "真实 Chromium 会话自动分配独立端口");
    foreach (var session in sessions)
    {
        var page = await session.Context.NewPageAsync();
        var value = await page.EvaluateAsync<int>("() => { document.body.textContent = 'runtime fixture'; return 42; }").WaitAsync(TimeSpan.FromSeconds(5));
        Check(value == 42, "真实会话可操作本地空白页面");
        await page.CloseAsync();
    }
    var stop = manager.StopAsync();
    await Until(() => sessions.All(s => s.Token.IsCancellationRequested));
    await Task.WhenAll(sessions.Select(s => s.DisposeAsync().AsTask())); await stop;
    Check(processes.Count == 0 && sessions.All(s => s.ProcessSession.ExitConfirmed) && provider.ActiveLeases == 0,
        "真实停止确认进程退出并归还全部驱动租约");
}
Console.WriteLine($"All {checks} browser runtime checks passed.");

public class Fake : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    { var value = Create<T, Fake>(); ((Fake)(object)value).Handler = handler; return value; }
}
public sealed class Browser
{
    public IBrowser Value { get; }
    public int ContextCloses;
    private bool connected = true;
    private EventHandler<IBrowser>? disconnected;
    public Browser()
    {
        var context = Fake.Create<IBrowserContext>((m, _) => { if (m.Name == "CloseAsync") { ContextCloses++; return Task.CompletedTask; } throw new NotSupportedException(m.Name); });
        Value = Fake.Create<IBrowser>((m, a) => m.Name switch
        {
            "get_IsConnected" => connected,
            "get_Contexts" => new IBrowserContext[] { context },
            "add_Disconnected" => Add((EventHandler<IBrowser>)a![0]!),
            "remove_Disconnected" => Remove((EventHandler<IBrowser>)a![0]!),
            "CloseAsync" => Close(),
            _ => throw new NotSupportedException(m.Name)
        });
    }
    private object? Add(EventHandler<IBrowser> h) { disconnected += h; return null; }
    private object? Remove(EventHandler<IBrowser> h) { disconnected -= h; return null; }
    private Task Close() { Disconnect(); return Task.CompletedTask; }
    public void Disconnect() { connected = false; disconnected?.Invoke(this, Value); }
}
public sealed class Processes : IBrowserProcessManager
{
    public readonly ConcurrentDictionary<string, ChromiumSession> Sessions = new();
    public int Starts, CloseCalls;
    public Func<CancellationToken, Task>? Starting;
    public Func<string, Task>? Closing;
    public bool FailAfterStart;
    public async Task<ChromiumSession> StartChromium(string uniqueId, string exePath, string userDataDir, TimeSpan ttl,
        string arguments = "--incognito", string? proxyServer = null, TimeSpan? readyTimeout = null, CancellationToken token = default)
    {
        Interlocked.Increment(ref Starts);
        if (Starting != null) await Starting(token);
        token.ThrowIfCancellationRequested();
        var session = new ChromiumSession { UniqueId = uniqueId, UserDir = userDataDir, CdpEndpoint = "fake-endpoint" };
        Sessions.TryAdd(uniqueId, session);
        if (FailAfterStart) throw new IOException("partial start failure");
        return session;
    }
    public async Task CloseAsync(string uniqueId)
    {
        if (!Sessions.ContainsKey(uniqueId)) return;
        Interlocked.Increment(ref CloseCalls);
        if (Closing != null) await Closing(uniqueId);
        Sessions.TryRemove(uniqueId, out _);
    }
    public bool TryGetSession(string uniqueId, out ChromiumSession? session) => Sessions.TryGetValue(uniqueId, out session);
}
