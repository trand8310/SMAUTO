using System.Reflection;
using Microsoft.Playwright;
using SMAd.Models;
using SMAd.PageActions;
using SMAd.LandingPolicy;
using PlaywrightHumanInput;

internal static class PageActionChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var worker = new QTP.Plugins.SMAdTask(null!, null!, null!, null!, null!, new QTP.Common.Infrastructure.AppSettings());
        var empty = new WorkerRunContext(new() { LinkedCts = new(), TotalPV = 0 });
        var mainFlow = typeof(QTP.Plugins.SMAdTask).GetMethod("RunMainFlowAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var completed = await (Task<bool>)mainFlow.Invoke(worker, new object[] { empty, CancellationToken.None })!;
        check(!completed && empty.LastFailureReason != null, "未执行任何页面轮次不能返回任务成功");
        var complete = typeof(QTP.Plugins.SMAdTask).GetMethod("CompleteSuccess", BindingFlags.Instance | BindingFlags.NonPublic)!;
        check(!(bool)complete.Invoke(worker, new object[] { empty })!, "所有成功出口都要求实际完成页面结果");
        var page = ActionStub.Create<IPage>();
        var state = ActionStub.Of(page);
        var locator = ActionStub.Create<ILocator>();
        state.Url = "https://example.test/list";
        state.Locator = locator;
        var frame = ActionStub.Create<IFrame>();
        state.Frame = frame;
        var ctx = new WorkerRunContext(new() { LinkedCts = new() });
        ctx.SetActivePage(page, Stub.Create<ICDPSession>());
        var executor = new PageActionExecutor(ctx, _ => { });
        var result = await executor.ExecuteAsync("Navigate", (_, _) => Task.FromResult(false), null, default);
        check(!result.Attempted && !result.Succeeded, "目标未派发不记作点击");
        result = await executor.ExecuteAsync("Navigate", (_, _) => { state.Url += "/detail"; return Task.FromResult(true); }, null, default);
        check(result.Navigated && !result.OpenedNewPage, "旧 URL 前缀扩展也正确识别导航");
        var observation = ctx.ActionRecords.Last();
        check(observation.ObservationDelayMs is >= 2000 and <= 6000 &&
            observation.ObservationElapsedMs >= observation.ObservationDelayMs && observation.DestinationReadyMs != null,
            "当前页导航完成后等待随机2000至6000毫秒并单独记录观察时间");
        result = await executor.ExecuteAsync("Reload", (_, _) => { state.Raise("FrameNavigated", frame); return Task.FromResult(true); }, null, default);
        check(result.Navigated, "相同 URL 的主框架导航可被确认");
        var popup = ActionStub.Create<IPage>();
        ActionStub.Of(popup).Url = "https://example.test/new";
        ActionStub.Of(popup).Locator = locator;
        IPage? activated = null;
        result = await executor.ExecuteAsync("Popup", (_, _) => { state.Raise("Popup", popup); return Task.FromResult(true); },
            (p, _) => { activated = p; return Task.CompletedTask; }, default);
        check(result.OpenedNewPage && ReferenceEquals(activated, popup), "来源 popup 在输入过程中被捕获并激活");
        check(ctx.ActionRecords.Last().ObservationDelayMs is >= 2000 and <= 6000,
            "新页面激活后也执行统一观察期");
        result = await executor.ExecuteAsync("Download", (_, _) => { state.Raise("Download", ActionStub.Create<IDownload>()); return Task.FromResult(true); }, null, default);
        check(result.Downloaded && result.Succeeded && !result.Navigated, "下载结果独立于 URL 导航");
        check(ctx.ActionRecords.Last().ObservationDelayMs == 0, "下载不增加页面观察延迟");
        result = await executor.ExecuteAsync("Tab", (_, _) => Task.FromResult(true), null, default, (_, _) => Task.FromResult(true));
        check(result.EffectVerified && !result.Navigated, "DOM 效果无需等待 URL 改变");
        result = await executor.ExecuteAsync("NoEffect", (_, _) => Task.FromResult(true), null, default, timeoutMs: 80);
        check(result.Attempted && !result.Succeeded && result.Outcome == ClickOutcome.NoEffect, "已派发但未确认效果不能报告成功");
        check(ctx.BrowserFailureKind == null && !ctx.Config.LinkedCts.IsCancellationRequested,
            "普通点击无效果只记录动作结果，不污染浏览器故障分类");
        result = await executor.ExecuteAsync("ExpectedDestination", (_, _) => { state.Url += "?unexpected=1"; return Task.FromResult(true); },
            null, default, (_, _) => Task.FromResult(false), timeoutMs: 80);
        check(!result.Succeeded && result.Attempted, "URL 变化仍必须满足指定的业务效果判据");
        ActionStub.Of(page).ReadyError = true;
        result = await executor.ExecuteAsync("BadDestination", (_, _) => { state.Url += "?bad=1"; return Task.FromResult(true); }, null, default);
        check(result.Attempted && !result.Navigated && result.Reason != null, "目标页未就绪保留已派发状态和失败原因");
        state.ReadyError = false;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel(); bool dispatched = false;
        try { await executor.ExecuteAsync("Cancelled", (_, _) => { dispatched = true; return Task.FromResult(true); }, null, cancelled.Token); }
        catch (OperationCanceledException) { }
        check(!dispatched && ctx.ActionRecords.Last().Outcome == ClickOutcome.Cancelled, "取消动作不派发并记录取消结果");
        check(state.Count("Popup") == 0 && state.Count("Download") == 0 && state.Count("FrameNavigated") == 0, "每条退出路径清理动作事件监听");
        check(File.Exists(ctx.ActionLogPath) && File.ReadAllLines(ctx.ActionLogPath).Length == ctx.ActionRecords.Count,
            "动作实际结果逐条持久化为 JSONL");

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = executor.ExecuteAsync("First", async (_, _) => { started.SetResult(); await release.Task; return false; }, null, default);
        await started.Task;
        var queued = executor.ExecuteAsync("Stale", (_, _) => { dispatched = true; return Task.FromResult(true); }, null, default);
        var other = ActionStub.Create<IPage>();
        ctx.SetActivePage(other, Stub.Create<ICDPSession>());
        dispatched = false;
        release.SetResult(); await first; result = await queued;
        check(!dispatched && !result.Attempted, "排队期间切换页面拒绝旧页面输入");

        var observingPage = ActionStub.Create<IPage>();
        var observingState = ActionStub.Of(observingPage);
        observingState.Url = "https://example.test/observe";
        observingState.Locator = locator;
        var observingCtx = new WorkerRunContext(new() { LinkedCts = new() });
        observingCtx.SetActivePage(observingPage, Stub.Create<ICDPSession>());
        var observationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observingExecutor = new PageActionExecutor(observingCtx, message =>
        {
            if (message.StartsWith("Navigation ready:")) observationStarted.TrySetResult();
        });
        var observingAction = observingExecutor.ExecuteAsync("Observe", (_, _) =>
        {
            observingState.Url += "/ready"; return Task.FromResult(true);
        }, null, default, timeoutMs: 80);
        await observationStarted.Task;
        using (var lockCancel = new CancellationTokenSource(40))
        {
            bool inputBlocked = false;
            try { using var ignored = await HumanInputCoordinator.AcquireAsync(observingCtx.human.Session, lockCancel.Token); }
            catch (OperationCanceledException) { inputBlocked = true; }
            check(inputBlocked, "导航观察期间同一会话后台触屏输入被阻止");
        }
        var queuedDuringObservation = observingExecutor.ExecuteAsync("QueuedDuringObservation",
            (_, _) => Task.FromResult(false), null, default, timeoutMs: 40);
        check((await observingAction).Navigated && observingCtx.BrowserFailureKind == null,
            "观察等待不挤占80毫秒导航预算，不把已成功导航误报超时");
        check((await queuedDuringObservation).Reason == "Target was not actionable" && observingCtx.BrowserFailureKind == null,
            "后台动作等待观察期不计入自身执行超时");

        observationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var observeCancel = new CancellationTokenSource())
        {
            var cancelledObservation = observingExecutor.ExecuteAsync("CancelObservation", (_, _) =>
            {
                observingState.Url += "/cancel"; return Task.FromResult(true);
            }, null, observeCancel.Token);
            await observationStarted.Task;
            observeCancel.Cancel();
            bool observationCancelled = false;
            try { await cancelledObservation; } catch (OperationCanceledException) { observationCancelled = true; }
            check(observationCancelled && observingCtx.ActionRecords.Last().Outcome == ClickOutcome.Cancelled,
                "观察期间上层取消立即传播并记录取消");
        }
        using (await HumanInputCoordinator.AcquireAsync(observingCtx.human.Session, default))
            check(true, "观察取消后释放会话输入锁");

        observationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var closedObservation = observingExecutor.ExecuteAsync("CloseObservation", (_, _) =>
        {
            observingState.Url += "/close"; return Task.FromResult(true);
        }, null, default);
        await observationStarted.Task;
        observingState.Closed = true;
        var closedResult = await closedObservation.WaitAsync(TimeSpan.FromSeconds(1));
        check(!closedResult.Succeeded && closedResult.Reason == "Destination changed during observation",
            "目标页面关闭后立即结束观察且不报告成功");

        var recoveryCtx = new WorkerRunContext(new() { LinkedCts = new(), TotalPV = 2, SleepMs = 100 });
        recoveryCtx.SetActivePage(page, Stub.Create<ICDPSession>());
        check(!await OptionalPageOperation.RunAsync(recoveryCtx, "Tap", () => Task.FromException(new OperationCanceledException()),
            default, _ => { }) && !recoveryCtx.Config.LinkedCts.IsCancellationRequested,
            "局部触屏取消跳过当前动作，保留当前访问");
        check(!await OptionalPageOperation.RunAsync(recoveryCtx, "Locator", () => Task.FromException(new TimeoutException()),
            default, _ => { }), "定位超时可局部恢复");
        using (var taskCancel = new CancellationTokenSource())
        {
            taskCancel.Cancel();
            bool propagated = false;
            try { await OptionalPageOperation.RunAsync(recoveryCtx, "Cancelled", () => Task.CompletedTask, taskCancel.Token, _ => { }); }
            catch (OperationCanceledException) { propagated = true; }
            check(propagated, "任务取消不能被局部恢复吞掉");
        }
        recoveryCtx.PageCrashed = true;
        bool crashPropagated = false;
        try { await OptionalPageOperation.RunAsync(recoveryCtx, "Crash", () => Task.FromException(new OperationCanceledException()), default, _ => { }); }
        catch (OperationCanceledException) { crashPropagated = true; }
        check(crashPropagated, "页面崩溃时不继续操作");
        recoveryCtx.PageCrashed = false;
        var disconnected = ActionStub.Create<IBrowser>();
        ActionStub.Of(disconnected).Connected = false;
        recoveryCtx.Browser = disconnected;
        bool disconnectPropagated = false;
        try { await OptionalPageOperation.RunAsync(recoveryCtx, "Disconnect", () => Task.FromException(new TimeoutException()), default, _ => { }); }
        catch (TimeoutException) { disconnectPropagated = true; }
        check(disconnectPropagated, "浏览器断开时不将超时视为可恢复");
        recoveryCtx.Browser = null;
        using (await HumanInputCoordinator.AcquireAsync(recoveryCtx.human.Session, default))
        {
            var directTap = await SMAd.SmAdTouch.TapAsync(page, recoveryCtx.CdpSession!,
                Stub.Create<IElementHandle>(), timeout: 40);
            check(!directTap && !recoveryCtx.Config.LinkedCts.IsCancellationRequested,
                "直接触屏点击等待输入锁局部超时返回false，不结束访问");
        }
        recoveryCtx.JumpClick = true;
        recoveryCtx.ClickRequested = true;
        recoveryCtx.CurrentVisitReady = true;
        state.EvaluateError = new OperationCanceledException("Local swipe deadline");
        var stayMethod = typeof(QTP.Plugins.SMAdTask).GetMethod("ExecuteTaskSleepPhaseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var stayClock = System.Diagnostics.Stopwatch.StartNew();
        var stayResult = await (Task<FlowControl>)stayMethod.Invoke(worker, new object[] { recoveryCtx, CancellationToken.None })!;
        state.EvaluateError = null;
        check(stayResult == FlowControl.NextPv && stayClock.ElapsedMilliseconds >= 90,
            "点击未完成且滑动超时，仍静态阅读完停留时间再进入下一PV");
        recoveryCtx.CompleteCurrentVisit();
        recoveryCtx.CompleteCurrentVisit();
        check(recoveryCtx.CompletedPvs == 1 && recoveryCtx.CompletedClickPvs == 0 && !recoveryCtx.PageTriggerClick,
            "访问完成独立计数且不伪造点击，同一PV只记录一次");
        recoveryCtx.ResetPerPvState();
        recoveryCtx.CompleteCurrentVisit();
        check(recoveryCtx.CompletedPvs == 1, "未打开就绪页面不能记录访问完成");
        recoveryCtx.CurrentVisitReady = true;
        recoveryCtx.PageTriggerClick = true;
        recoveryCtx.CompleteCurrentVisit();
        recoveryCtx.ResetPerPvState();
        var buildResult = typeof(QTP.Plugins.SMAdTask).GetMethod("BuildWorkerResult", BindingFlags.Static | BindingFlags.NonPublic)!;
        var visitResult = (QTP.Common.Models.WorkerExecutionResult)buildResult.Invoke(null, new object?[] { recoveryCtx, true, false, null })!;
        check(visitResult.CompletedPvs == 2 && visitResult.CompletedClickPvs == 1 && visitResult.PageTriggerClick && visitResult.ClickRequested,
            "执行结果分别保留访问数、点击访问数和请求状态，跨PV不丢失已完成点击");

        var entryPage = ActionStub.Create<IPage>();
        var entryState = ActionStub.Of(entryPage);
        entryState.Locator = locator;
        int entryAttempts = 0;
        entryState.Navigate = () => ++entryAttempts == 1
            ? Task.FromException<IResponse?>(new PlaywrightException("Download is starting"))
            : Task.FromResult<IResponse?>(null);
        var entryResult = await EntryPageNavigator.NavigateAsync(entryPage, "https://example.test/search", 5000, default, _ => { });
        check(entryResult.Opened && entryAttempts == 2, "入口转下载后在同一页面重试一次，可恢复正常访问");
        entryAttempts = 0;
        entryState.Navigate = () => { entryAttempts++; return Task.FromException<IResponse?>(new PlaywrightException("Download is starting")); };
        entryResult = await EntryPageNavigator.NavigateAsync(entryPage, "https://example.test/search", 5000, default, _ => { });
        check(!entryResult.Opened && entryResult.Downloaded && entryAttempts == 2 && entryResult.Reason != null,
            "连续入口下载返回明确未打开结果，不抛未分类异常且不无限重试");
        check(entryState.Count("Download") == 0 && entryState.Count("Response") == 0,
            "入口导航成功和下载失败路径均清理诊断监听");
        entryAttempts = 0;
        entryState.Navigate = () =>
        {
            if (++entryAttempts > 1) return Task.FromResult<IResponse?>(null);
            entryState.Raise("Download", ActionStub.Create<IDownload>());
            return Task.FromException<IResponse?>(new PlaywrightException("net::ERR_ABORTED"));
        };
        entryResult = await EntryPageNavigator.NavigateAsync(entryPage, "https://example.test/search", 5000, default, _ => { });
        check(entryResult.Opened && entryAttempts == 2, "ERR_ABORTED具有下载事件证据时才作为入口下载恢复");
        entryAttempts = 0;
        entryState.Navigate = () => { entryAttempts++; return Task.FromException<IResponse?>(new PlaywrightException("net::ERR_ABORTED")); };
        bool ordinaryAbortPropagated = false;
        try { await EntryPageNavigator.NavigateAsync(entryPage, "https://example.test/search", 5000, default, _ => { }); }
        catch (PlaywrightException) { ordinaryAbortPropagated = true; }
        check(ordinaryAbortPropagated && entryAttempts == 1, "无下载证据的导航中断不能被误判成下载或自动重试");
        entryAttempts = 0;
        using (var entryCancellation = new CancellationTokenSource())
        {
            entryState.Navigate = () => { entryAttempts++; entryCancellation.Cancel(); return Task.FromException<IResponse?>(new PlaywrightException("Download is starting")); };
            bool entryCancelled = false;
            try { await EntryPageNavigator.NavigateAsync(entryPage, "https://example.test/search", 5000, entryCancellation.Token, _ => { }); }
            catch (OperationCanceledException) { entryCancelled = true; }
            check(entryCancelled && entryAttempts == 1 && entryState.Count("Download") == 0,
                "入口下载重试期间任务取消立即传播并清理监听");
        }

        state.Url = "https://unknown.test/";
        check(OfferTargetResolver.Resolve(page) == null, "未知站点不生成 body 或 iframe 降级目标");
        state.Url = "https://unknown.test/?site=m.1688.com";
        check(OfferTargetResolver.Resolve(page) == null, "站点识别只读取真实 host");

        var human = new HumanTouchSession();
        using (await HumanInputCoordinator.AcquireAsync(human, default))
        {
            using var token = new CancellationTokenSource(40);
            bool rejected = false;
            try { using var ignored = await HumanInputCoordinator.AcquireAsync(human, token.Token); }
            catch (OperationCanceledException) { rejected = true; }
            check(rejected, "同一会话跨页面键盘和触摸互斥且锁等待可取消");
        }
        using (await HumanInputCoordinator.AcquireAsync(human, default)) { check(true, "取消等待不会损坏输入协调器"); }

        var scrollFrame = ActionStub.Create<IFrame>();
        var values = new Queue<double>(new[] { 25d, 40d, 45d, 45d, 45d, 45d });
        ActionStub.Of(scrollFrame).Evaluate = () => new ScrollTargetState { Key = "document", ScrollTop = values.Count > 0 ? values.Dequeue() : 45 };
        var scrollPage = ActionStub.Create<IPage>(); ActionStub.Of(scrollPage).Frame = scrollFrame;
        var resolver = new ScrollTargetResolver();
        bool moved = await resolver.DidScrollAsync(scrollPage, new() { Key = "document", ScrollTop = 0 }, new(), 0, 0, HumanSwipeDirection.Up, 5);
        check(moved && values.Count == 0, "惯性滚动需连续三个稳定采样才确认");
        ActionStub.Of(scrollFrame).Evaluate = () => new ScrollTargetState { Key = "different", ScrollTop = 500 };
        check(!await resolver.DidScrollAsync(scrollPage, new() { Key = "document" }, new(), 0, 0, HumanSwipeDirection.Up, 5), "不同滚动容器的偏移不得混用");
        ActionStub.Of(scrollFrame).Evaluate = () => new ScrollTargetState { Key = "document", ScrollTop = 30 };
        check(!await resolver.DidScrollAsync(scrollPage, new() { Key = "document" }, new(), 0, 0, HumanSwipeDirection.Down, 5), "反方向移动不能当作预期滚动成功");
        using var scrollCancel = new CancellationTokenSource(); scrollCancel.Cancel();
        bool scrollStopped = false;
        try { await resolver.DidScrollAsync(scrollPage, new(), new(), 0, 0, HumanSwipeDirection.Up, 5, scrollCancel.Token); }
        catch (OperationCanceledException) { scrollStopped = true; }
        check(scrollStopped, "滚动稳定等待传播取消");

        ActionStub.Of(locator).Box = new LocatorBoundingBoxResult { X = 150, Y = 230, Width = 40, Height = 20 };
        var rect = await resolver.GetElementRectAsync(locator);
        check(rect?.X == 150 && rect.Y == 230, "定位采用主视口 BoundingBox 而非 frame 局部坐标");
    }
}

public class ActionStub : DispatchProxy
{
    public string Url = "about:blank";
    public ILocator? Locator;
    public IFrame? Frame;
    public bool ReadyError;
    public bool Closed;
    public bool Connected = true;
    public Exception? EvaluateError;
    public Func<Task<IResponse?>>? Navigate;
    public int Status;
    public IRequest? Request;
    public IPage? Page;
    public bool NavigationRequest;
    public string? Failure;
    public object? Box;
    public Func<object>? Evaluate;
    private readonly Dictionary<string, Delegate?> _events = new();
    public static T Create<T>() where T : class => Create<T, ActionStub>();
    public static ActionStub Of(object value) => (ActionStub)value;
    public int Count(string name) => _events.GetValueOrDefault(name)?.GetInvocationList().Length ?? 0;
    public void Raise(string name, object value) => _events.GetValueOrDefault(name)?.DynamicInvoke(this, value);
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        var name = method!.Name;
        if (name.StartsWith("add_") || name.StartsWith("remove_"))
        {
            var key = name[name.IndexOf('_')..][1..];
            _events.TryGetValue(key, out var previous);
            _events[key] = name.StartsWith("add_") ? Delegate.Combine(previous, (Delegate)args![0]!) : Delegate.Remove(previous, (Delegate)args![0]!);
            return null;
        }
        if (name == "get_Url") return Url;
        if (name == "get_Status") return Status;
        if (name == "get_Request") return Request;
        if (name == "get_Frame") return Frame;
        if (name == "get_Page") return Page;
        if (name == "get_IsNavigationRequest") return NavigationRequest;
        if (name == "get_Failure") return Failure;
        if (name == "get_MainFrame") return Frame;
        if (name == "get_IsClosed") return Closed;
        if (name == "get_IsConnected") return Connected;
        if (name == "get_First") return this;
        if (name == "Locator") return Locator;
        if (name == "GotoAsync" && Navigate != null) return Navigate();
        if (name == "WaitForLoadStateAsync" && ReadyError) return Task.FromException(new TimeoutException("Not ready"));
        if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
        if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var type = method.ReturnType.GetGenericArguments()[0];
            if (name == "EvaluateAsync" && EvaluateError != null)
                return typeof(Task).GetMethods().Single(m => m.Name == "FromException" && m.IsGenericMethod)
                    .MakeGenericMethod(type).Invoke(null, new object[] { EvaluateError });
            var value = name == "EvaluateAsync" ? Evaluate?.Invoke() : name == "BoundingBoxAsync" ? Box : type.IsValueType ? Activator.CreateInstance(type) : null;
            return typeof(Task).GetMethod("FromResult")!.MakeGenericMethod(type).Invoke(null, new[] { value });
        }
        return null;
    }
}
