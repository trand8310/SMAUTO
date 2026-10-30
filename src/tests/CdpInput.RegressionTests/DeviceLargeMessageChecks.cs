using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using QTP.Common;
using SMAd.DeviceEmulation;

internal static class DeviceLargeMessageChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool requiredStartupResponse = false)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = deadline.Token;
        using var portFinder = new TcpListener(IPAddress.Loopback, 0);
        portFinder.Start(); int port = ((IPEndPoint)portFinder.LocalEndpoint).Port; portFinder.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var peerReady = new TaskCompletionSource<WebSocket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var detached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sending = new SemaphoreSlim(1, 1);
        var commands = new ConcurrentQueue<string>();
        var logs = new ConcurrentQueue<string>();
        int fatal = 0;
        string payload = "DO_NOT_LOG_PAYLOAD" + new string('界', 2 * 1024 * 1024); // >6 MiB UTF-8, fragments split multibyte characters.
        var jsonOptions = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        async Task Send(object value, bool fragment = false)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions);
            var peer = await peerReady.Task.WaitAsync(token);
            await sending.WaitAsync(token);
            try
            {
                int chunk = fragment ? 32771 : Math.Max(1, bytes.Length);
                for (int offset = 0; offset < bytes.Length; offset += chunk)
                {
                    int count = Math.Min(chunk, bytes.Length - offset);
                    await peer.SendAsync(bytes.AsMemory(offset, count), WebSocketMessageType.Text, offset + count == bytes.Length, token);
                }
            }
            finally { sending.Release(); }
        }
        object Attached(string session, string? target = null, bool large = false) => new
        {
            method = "Target.attachedToTarget",
            @params = new { sessionId = session, targetInfo = new { targetId = target ?? session, type = "page", url = large ? payload : "about:blank" } }
        };
        var server = Task.Run(async () =>
        {
            var request = await listener.GetContextAsync().WaitAsync(token);
            using var peer = (await request.AcceptWebSocketAsync(null)).WebSocket;
            peerReady.TrySetResult(peer);
            var buffer = new byte[16384];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    ValueWebSocketReceiveResult received;
                    do
                    {
                        received = await peer.ReceiveAsync(buffer.AsMemory(), token);
                        if (received.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, received.Count);
                    } while (!received.EndOfMessage);
                    using var document = JsonDocument.Parse(message.ToArray());
                    var command = document.RootElement;
                    int id = command.GetProperty("id").GetInt32();
                    string method = command.GetProperty("method").GetString()!;
                    string? session = command.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
                    commands.Enqueue(method);
                    if (requiredStartupResponse && method == "Target.setAutoAttach" && command.GetProperty("params").GetProperty("autoAttach").GetBoolean())
                        await Send(new { id, result = new { payload } }, true);
                    else if (session == "large-response-page" && method == "Emulation.setTouchEmulationEnabled")
                        await Send(new { id, result = new { payload }, sessionId = session }, true);
                    else await Send(new { id, result = new { } });
                    if (method == "Target.detachFromTarget" && command.GetProperty("params").GetProperty("sessionId").GetString() == "large-response-page")
                        detached.TrySetResult();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (WebSocketException) { }
        }, token);
        DeviceTargetController? controller = null;
        async Task Ready(string target)
        {
            var session = DeviceTargetSession.Create(); ((DeviceTargetSession)(object)session).Target = target;
            await controller!.WaitForPageAsync(session, token);
        }
        try
        {
            try
            {
                controller = await DeviceTargetController.CreateAsync($"ws://127.0.0.1:{port}/",
                    new(400, 800, 400, 720, 400, 696, 1, true, 400, 800), 5,
                    error => { Interlocked.Increment(ref fatal); failed.TrySetResult(error); }, token, logs.Enqueue);
            }
            catch (BrowserFailureException ex) when (requiredStartupResponse)
            {
                check(ex.Kind == BrowserStopKind.DeviceProtocolFailed && logs.Any(line => line.Contains("oversized response rejected")),
                    "超大关键启动响应明确报DeviceProtocolFailed，不伪造设备初始化成功");
                return;
            }
            if (requiredStartupResponse) throw new Exception("Oversized startup response was accepted.");
            await Send(Attached("healthy-page")); await Ready("healthy-page");
            check(!commands.Contains("Page.enable") && !commands.Contains("Emulation.setDeviceMetricsOverride"),
                "设备控制连接仅初始化触屏，不订阅Page事件或覆盖原生尺寸");
            await Send(new { method = "Page.frameNavigated", @params = new { frame = new { url = payload } }, sessionId = "healthy-page" }, true);
            // A second complete message proves the oversized message was drained through EndOfMessage.
            await Send(Attached("after-event")); await Ready("after-event");
            check(fatal == 0 && logs.Any(line => line.Contains("Page.frameNavigated") && line.Contains("drained and ignored")),
                "超过4MiB的无关分片事件不会停止执行，后续目标和响应仍可处理");
            await Send(Attached("oversize-page", large: true), true); await Ready("oversize-page");
            check(fatal == 0 && logs.Any(line => line.Contains("Target.attachedToTarget") && line.Contains("routed without payload")),
                "超大Target附加事件保留必要元信息，新页面触屏可就绪");
            await Send(Attached("large-response-page"));
            bool responseFailed = false;
            try { await Ready("large-response-page"); }
            catch (BrowserFailureException ex) { responseFailed = ex.Kind == BrowserStopKind.DeviceProtocolFailed; }
            await detached.Task.WaitAsync(token); await Ready("healthy-page");
            check(responseFailed && fatal == 0 && logs.Any(line => line.Contains("oversized response rejected") && line.Contains("Emulation.setTouchEmulationEnabled")),
                "超大关键目标响应仅让所属初始化失败，记录对应命令且健康页面继续可用");
            await Send(new { id = 999999, result = new { payload } }, true);
            await Send(Attached("replacement", "after-event")); await Ready("after-event");
            await Send(new { method = "Target.detachedFromTarget", @params = new { sessionId = "after-event", ignored = payload } }, true);
            await Send(Attached("after-detach")); await Ready("after-detach"); await Ready("after-event");
            check(fatal == 0 && logs.Any(line => line.Contains("Target.detachedFromTarget") && line.Contains("routed without payload")),
                "超大迟到分离事件不清除替换会话，过期大响应不影响后续输入准备");
            check(logs.Any(line => line.Contains("bytes=")) && logs.All(line => !line.Contains("DO_NOT_LOG_PAYLOAD") && line.Length < 1024),
                "大消息诊断只记录类型、长度、ID和处置，不记录正文");
            await (await peerReady.Task).CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "fixture closed", token);
            var failure = await failed.Task.WaitAsync(token);
            check(fatal == 1 && BrowserFailureClassifier.Classify(failure) == BrowserStopKind.CdpDisconnected,
                "大消息恢复后真正连接断开仍停止执行，分类为CdpDisconnected");
        }
        finally
        {
            if (controller != null) await controller.DisposeAsync(); deadline.Cancel(); listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
