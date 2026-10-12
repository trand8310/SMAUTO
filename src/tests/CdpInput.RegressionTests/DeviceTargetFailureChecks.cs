using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using SMAd.DeviceEmulation;

internal static class DeviceTargetFailureChecks
{
    public static async Task Run(Action<bool, string> check, string? baselineAssembly = null)
    {
        var displaySession = Stub.Create<ICDPSession>();
        Stub.Of(displaySession).DisplayMetrics = new { width = 400, height = 800, availWidth = 400, availHeight = 800, dpr = 1 };
        bool displayRejected = false;
        try { await DevicePageDisplay.ApplyAsync(displaySession, new(400, 800, 400, 720, 400, 696, 1, true, 400, 800), default); }
        catch (QTP.Common.BrowserFailureException ex)
        {
            displayRejected = ex.Kind == QTP.Common.BrowserStopKind.DeviceConfigurationFailed &&
                ex.Message.Contains("available=400x720") && ex.Message.Contains("availHeight");
        }
        check(displayRejected, "原生屏幕配置不匹配记录期望值、实际值及独立故障分类");
        check(!Stub.Of(displaySession).Commands.Contains("Emulation.setDeviceMetricsOverride"), "原生尺寸错误诊断不引入模拟尺寸回退");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var portFinder = new TcpListener(IPAddress.Loopback, 0);
        portFinder.Start(); var port = ((IPEndPoint)portFinder.LocalEndpoint).Port; portFinder.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var socket = new TaskCompletionSource<WebSocket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var healthy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var swapped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool badClosed = false;
        bool badDetached = false;
        var serving = Task.Run(async () =>
        {
            var request = await listener.GetContextAsync().WaitAsync(token);
            using var peer = (await request.AcceptWebSocketAsync(null)).WebSocket;
            socket.SetResult(peer);
            var buffer = new byte[4096];
            async Task Send(object value) => await peer.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)), WebSocketMessageType.Text, true, token);
            object Attached(string id, string? target = null) => new { method = "Target.attachedToTarget", @params = new { sessionId = id, targetInfo = new { targetId = target ?? id, type = "page" }, waitingForDebugger = true } };
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var received = await peer.ReceiveAsync(buffer, token);
                    if (received.MessageType == WebSocketMessageType.Close) break;
                    using var document = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
                    var command = document.RootElement;
                    var id = command.GetProperty("id").GetInt32();
                    var method = command.GetProperty("method").GetString();
                    var session = command.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
                    if (session is "closing-page" or "old-swap" && method == "Emulation.setTouchEmulationEnabled")
                        await Send(new { id, error = new { code = -32001, message = "Session with given id not found" } });
                    else await Send(new { id, result = new { } });
                    if (method == "Target.setAutoAttach" && command.GetProperty("params").GetProperty("autoAttach").GetBoolean())
                        await Send(Attached("closing-page"));
                    if (method == "Target.closeTarget")
                    {
                        badClosed = command.GetProperty("params").GetProperty("targetId").GetString() == "closing-page";
                        await Send(Attached("healthy-page"));
                    }
                    if (method == "Target.detachFromTarget")
                    {
                        if (command.GetProperty("params").GetProperty("sessionId").GetString() == "closing-page")
                        { badDetached = true; await Send(Attached("healthy-page")); }
                    }
                    if (session == "healthy-page" && method == "Runtime.runIfWaitingForDebugger")
                    {
                        healthy.TrySetResult();
                        if (baselineAssembly == null)
                        { await Send(Attached("old-swap", "same-page")); await Send(Attached("new-swap", "same-page")); }
                    }
                    if (session == "old-swap")
                        await Send(new { method = "Target.detachedFromTarget", @params = new { sessionId = "old-swap" } });
                    if (session == "new-swap" && method == "Runtime.runIfWaitingForDebugger") swapped.TrySetResult();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (WebSocketException) { }
        }, token);
        int fatal = 0;
        var logs = new List<string>();
        System.Runtime.Loader.AssemblyLoadContext? baselineLoad = null;
        IAsyncDisposable controller;
        if (baselineAssembly == null)
            controller = await DeviceTargetController.CreateAsync($"ws://127.0.0.1:{port}/", new(400, 800, 400, 720, 400, 696, 1, true, 400, 800),
                5, _ => Interlocked.Increment(ref fatal), token, message => { lock (logs) logs.Add(message); });
        else
        {
            baselineLoad = new System.Runtime.Loader.AssemblyLoadContext("baseline-controller", isCollectible: true);
            var assembly = baselineLoad.LoadFromAssemblyPath(Path.GetFullPath(baselineAssembly));
            var oldProfile = Activator.CreateInstance(assembly.GetType("SMAd.DeviceEmulation.DeviceDisplayProfile")!,
                new object[] { 400, 800, 400, 720, 400, 696, 1f, true, 400, 800 });
            var creating = (Task)assembly.GetType("SMAd.DeviceEmulation.DeviceTargetController")!.GetMethod("CreateAsync")!
                .Invoke(null, new object[] { $"ws://127.0.0.1:{port}/", oldProfile!, 5, (Action<Exception>)(_ => Interlocked.Increment(ref fatal)), token })!;
            await creating;
            controller = (IAsyncDisposable)creating.GetType().GetProperty("Result")!.GetValue(creating)!;
        }
        try
        {
            await healthy.Task.WaitAsync(token);
            var pageSession = DeviceTargetSession.Create();
            await (Task)controller.GetType().GetMethod("WaitForPageAsync")!.Invoke(controller, new object[] { pageSession, token })!;
            if (baselineAssembly != null)
                check(badClosed && fatal == 1, "修复前已发布程序集将单个页面关闭竞争误报为整个浏览器故障");
            else
            {
                check(!badClosed && badDetached && fatal == 0, "单个页面关闭竞争只分离故障会话，不关闭页面或整个浏览器");
                check(logs.Any(x => x.Contains("closing-page") && x.Contains("Session with given id not found")),
                    "页面关闭竞争保留目标和协议错误诊断");
                check(true, "次要页面初始化失败后其他页面仍可就绪");
                await swapped.Task.WaitAsync(token);
                ((DeviceTargetSession)(object)pageSession).Target = "same-page";
                await (Task)controller.GetType().GetMethod("WaitForPageAsync")!.Invoke(controller, new object[] { pageSession, token })!;
                check(fatal == 0, "旧会话迟到的分离事件不清除同页面新会话的就绪状态");
                await (await socket.Task).CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "fixture connection closed", token);
                while (Volatile.Read(ref fatal) == 0) await Task.Delay(10, token);
                check(fatal == 1, "设备CDP连接真正断开时仍上报执行故障");
            }
        }
        finally
        {
            await controller.DisposeAsync();
            deadline.Cancel(); listener.Stop();
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
            baselineLoad?.Unload();
        }
    }
}

public class DeviceTargetSession : DispatchProxy
{
    public string Target = "healthy-page";
    public static ICDPSession Create() => Create<ICDPSession, DeviceTargetSession>();
    protected override object? Invoke(MethodInfo? method, object?[]? args) =>
        method!.Name == "SendAsync"
            ? Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(new { targetInfo = new { targetId = Target } }))
            : throw new NotSupportedException(method.Name);
}
