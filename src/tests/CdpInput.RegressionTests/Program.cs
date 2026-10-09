using System.Reflection;
using System.Text.Json;
using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Plugins;
using SMAd.Models;
using QTP.Common.Infrastructure;
using QTP.Common;
using QTP.Common.Models;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine($"PASS {name}"); checks++; }
V3Checks.Run(Check);
await ProxyFailureChecks.Run(Check);
await PageActionChecks.Run(Check);
async Task Throws<T>(Func<Task> work, string name) where T : Exception
{
    try { await work(); } catch (T) { Check(true, name); return; }
    throw new Exception(name);
}
var page = Stub.Create<IPage>();
var other = Stub.Create<IPage>();
var cdp = Stub.Create<ICDPSession>();
var sent = Stub.Of(cdp);
await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CdpTouchRuntime.InitializeAsync(page, cdp, 6)));
Check(sent.Commands.Count(x => x == "Emulation.setTouchEmulationEnabled") == 1, "并发初始化只配置一次");
await Throws<InvalidOperationException>(() => CdpTouchRuntime.InitializeAsync(other, cdp), "拒绝错误页面与会话配对");

// Tap cancellation diagnostics must distinguish the local budget from upstream cancellation.
{
    var diagnosticEngine = new HumanTouchEngine();
    var diagnosticTarget = Stub.Create<IElementHandle>();
    var diagnosticLogs = new System.Collections.Concurrent.ConcurrentQueue<string>();
    using (var heldInput = await HumanInputCoordinator.AcquireAsync(diagnosticEngine.Session, CancellationToken.None))
    {
        await Throws<OperationCanceledException>(() => diagnosticEngine.TapAsync(page, cdp, diagnosticTarget,
            timeout: 50, log: diagnosticLogs.Enqueue), "Tap 总预算耗尽仍传播取消");
        Check(diagnosticLogs.Any(x => x.Contains("source=tap deadline") && x.Contains("stage=wait for input lock")),
            "Tap 超时日志指出预算来源和阻塞阶段");
        diagnosticLogs.Clear();
        using var upstreamCancellation = new CancellationTokenSource();
        upstreamCancellation.CancelAfter(50);
        await Throws<OperationCanceledException>(() => diagnosticEngine.TapAsync(page, cdp, diagnosticTarget,
            timeout: 5000, log: diagnosticLogs.Enqueue, cancellationToken: upstreamCancellation.Token),
            "Tap 上层取消仍传播取消");
        Check(diagnosticLogs.Any(x => x.Contains("source=upstream cancellation")), "Tap 上层取消与自身超时区分");
        Check(sent.TouchTypes.Count == 0, "取消等待输入锁时不派发触摸");
    }
    using var releasedInput = await HumanInputCoordinator.AcquireAsync(diagnosticEngine.Session, CancellationToken.None);
    Check(true, "取消诊断后输入锁仍可正常获取");
}

var dispatcher = new CdpTouchDispatcher();
var samples = new[] { new TouchSample { Point = new(30, 40), RadiusX = 3, RadiusY = 3, Force = .5 } };
var plan = new GesturePlan { StartHoldMs = 40 };
await Task.WhenAll(dispatcher.DispatchAsync(cdp, samples, plan, new()), dispatcher.DispatchAsync(cdp, samples, plan, new()));
Check(sent.TouchTypes.SequenceEqual(new[] { "touchStart", "touchEnd", "touchStart", "touchEnd" }), "并发手势按完整接触串行执行");
sent.TouchTypes.Clear();
using (var cancel = new CancellationTokenSource())
{
    sent.OnTouch = t => { if (t == "touchStart") cancel.Cancel(); };
    await Throws<OperationCanceledException>(() => dispatcher.DispatchAsync(cdp, samples, plan, new(), cancel.Token), "按下后取消向上层传播");
    Check(sent.TouchTypes.SequenceEqual(new[] { "touchStart", "touchCancel" }), "取消后释放触点");
    sent.OnTouch = null;
    sent.TouchTypes.Clear();
    await Throws<OperationCanceledException>(() => dispatcher.DispatchAsync(cdp, samples, plan, new(), cancel.Token), "提前取消不发送输入");
    Check(sent.TouchTypes.Count == 0, "提前取消没有残留接触");
}
var failed = Stub.Create<ICDPSession>();
Stub.Of(failed).FailTouch = "touchStart";
await Throws<InvalidOperationException>(() => dispatcher.DispatchAsync(failed, samples, plan, new()), "按下失败向上层传播");
Check(Stub.Of(failed).TouchTypes.SequenceEqual(new[] { "touchStart", "touchCancel" }), "按下发送失败仍尝试清理");
var badRelease = Stub.Create<ICDPSession>();
Stub.Of(badRelease).FailTouch = "touchEnd";
await Throws<InvalidOperationException>(() => dispatcher.DispatchAsync(badRelease, samples, plan, new()), "释放触点失败不能报告执行成功");
await Throws<InvalidOperationException>(() => dispatcher.DispatchAsync(badRelease, samples, plan, new()), "释放失败的会话禁止继续输入");
var browserContext = Stub.Create<IBrowserContext>();
Stub.Of(browserContext).NewSession = () => Task.FromResult(Stub.Create<ICDPSession>());
await using (var manager = new CDPSessionManager(browserContext))
{
    var all = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => manager.GetOrCreateSessionAsync(page)));
    Check(all.All(x => ReferenceEquals(x, all[0])) && Stub.Of(browserContext).SessionCreates == 1, "同页并发共享一个会话");
    var first = all[0];
    await first.DetachAsync();
    Check(manager.Count == 0, "CDP关闭后移除缓存");
    var next = await manager.GetOrCreateSessionAsync(page);
    Check(!ReferenceEquals(first, next), "关闭后创建新会话");
    var ctx = new WorkerRunContext(new TaskConfig { LinkedCts = new() });
    ctx.SetActivePage(page, next);
    Check(ReferenceEquals(ctx.ActivePage!.Page, page) && ReferenceEquals(ctx.ActivePage.Session, next), "页面与会话整体发布");
    Stub.Of(page).Raise("Close", page);
    Check(manager.Count == 0, "页面关闭也移除对应缓存");
    await manager.DisposeAsync();
    Check(Stub.Of(next).Detaches == 1, "管理器显式分离存活会话");
    await Throws<ObjectDisposedException>(() => manager.GetOrCreateSessionAsync(page), "释放后禁止访问");
}
var pending = new TaskCompletionSource<ICDPSession>(TaskCreationOptions.RunContinuationsAsynchronously);
var delayed = Stub.Create<IBrowserContext>();
Stub.Of(delayed).NewSession = () => pending.Task;
var delayedManager = new CDPSessionManager(delayed);
var creating = delayedManager.GetOrCreateSessionAsync(other);
var disposing = delayedManager.DisposeAsync().AsTask();
Check(!disposing.IsCompleted, "释放等待正在创建的会话");
var delayedSession = Stub.Create<ICDPSession>();
pending.SetResult(delayedSession);
await disposing;
await Throws<ObjectDisposedException>(async () => await creating, "创建过程中释放不返回失效会话");
Check(Stub.Of(delayedSession).Detaches == 1, "创建完成的迟到会话也被分离");

var downloadPage = Stub.Create<IPage>();
var downloadContext = Stub.Create<IBrowserContext>();
Stub.Of(downloadContext).NewSession = () => Task.FromResult(Stub.Create<ICDPSession>());
await using (var manager = new CDPSessionManager(downloadContext))
using (var cancellation = new CancellationTokenSource())
{
    var ctx = new WorkerRunContext(new() { LinkedCts = cancellation, MaxTouchPoints = 5, Sw = 400, Sh = 800, DeviceScale = 1, Os = 1 }) { CdpManager = manager };
    var worker = new SMAdTask(null!, null!, null!, null!, null!, new AppSettings());
    var init = typeof(SMAdTask).GetMethod("InitPageAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
    Task Init() => (Task)init.Invoke(worker, new object[] { ctx, downloadPage, cancellation.Token })!;
    await Task.WhenAll(Init(), Init(), Init());
    Check(Stub.Of(downloadPage).SubscriberCount("Download") == 1, "页面重复初始化只注册一个下载监听");
    Stub.Of(downloadPage).Raise("Download", Stub.Create<IDownload>());
    Check(ctx.TriggerDownloadSign == 0, "入口就绪前下载仅诊断，不计入业务下载");
    ctx.EnableBusinessDownloads();
    Stub.Of(downloadPage).Raise("Download", Stub.Create<IDownload>());
    Check(ctx.TriggerDownloadSign == 1, "单次下载计数一次");
    Stub.Of(downloadPage).Raise("Download", Stub.Create<IDownload>());
    Check(ctx.TriggerDownloadSign == 2, "不同下载各计数一次");
    ctx.ResetPerPvState();
    Stub.Of(downloadPage).Raise("Download", Stub.Create<IDownload>());
    Check(ctx.TriggerDownloadSign == 0 && !ctx.BusinessDownloadsEnabled, "新PV重新禁用业务下载计数");
}

if (args.Length > 0)
{
    if (args.Contains("--movement")) await MovementBrowserChecks.RunAsync(args[0], Check);
    else if (args.Contains("--dom-only")) await DomBrowserChecks.RunAsync(args[0], Check);
    else
    await BrowserChecks.RunAsync(args[0], Check, args.Contains("--raw-touch"));
}
Console.WriteLine($"All {checks} checks passed.");

public class Stub : DispatchProxy
{
    private int _windowWidth = 500, _windowHeight = 928;
    public List<string> Commands = new();
    public List<string> TouchTypes = new();
    public Action<string>? OnTouch;
    public string? FailTouch;
    public Func<Task<ICDPSession>>? NewSession;
    public int SessionCreates, Detaches;
    private readonly Dictionary<string, Delegate?> _events = new();
    public int SubscriberCount(string name) => _events.GetValueOrDefault(name)?.GetInvocationList().Length ?? 0;
    public void Raise(string name, object value) => _events.GetValueOrDefault(name)?.DynamicInvoke(this, value);
    public static T Create<T>() where T : class => Create<T, Stub>();
    public static Stub Of(object obj) => (Stub)obj;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        if (name.StartsWith("add_") || name.StartsWith("remove_"))
        {
            var key = name[(name.StartsWith("add_") ? 4 : 7)..];
            _events.TryGetValue(key, out var old);
            _events[key] = name.StartsWith("add_") ? Delegate.Combine(old, (Delegate)args![0]!) : Delegate.Remove(old, (Delegate)args![0]!);
            return null;
        }
        if (name == "get_Pages") return Array.Empty<IPage>();
        if (name == "get_IsClosed") return false;
        if (name == "NewCDPSessionAsync") { SessionCreates++; return NewSession!(); }
        if (name == "DetachAsync")
        {
            Detaches++;
            if (_events.TryGetValue("Close", out var handler)) handler?.DynamicInvoke(this, this);
            return Task.CompletedTask;
        }
        if (name == "SendAsync")
        {
            string command = (string)args![0]!;
            Commands.Add(command);
            if (command == "Browser.getWindowForTarget")
                return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { windowId = 1 }));
            if (command == "Browser.setWindowBounds")
            {
                var parameters = JsonSerializer.SerializeToElement(args[1]);
                var bounds = parameters.GetProperty("bounds");
                if (bounds.TryGetProperty("width", out var width)) _windowWidth = width.GetInt32();
                if (bounds.TryGetProperty("height", out var height)) _windowHeight = height.GetInt32();
            }
            if (command == "Browser.getWindowBounds")
                return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { bounds = new { width = _windowWidth, height = _windowHeight } }));
            if (command == "Runtime.evaluate" && ((Dictionary<string, object>)args[1]!)["expression"].ToString()!.StartsWith("screen.width ==="))
                return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { result = new { value = true } }));
            if (command == "Input.dispatchTouchEvent")
            {
                var type = (string)((Dictionary<string, object>)args[1]!)["type"];
                TouchTypes.Add(type); OnTouch?.Invoke(type);
                if (type == FailTouch) return Task.FromException<JsonElement?>(new InvalidOperationException("send failed"));
            }
            return Task.FromResult<JsonElement?>(null);
        }
        if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
        if (method.ReturnType == typeof(bool)) return false;
        return null;
    }
}
