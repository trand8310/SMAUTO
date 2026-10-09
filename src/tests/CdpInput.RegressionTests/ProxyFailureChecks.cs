using System.Reflection;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using QTP.Common;
using QTP.Common.Models;
using QTP.Plugins;
using SMAd.Models;
using SMAd.PageActions;

internal static class ProxyFailureChecks
{
    public static async Task RunBrowserAsync(IBrowser browser, Action<bool, string> check)
    {
        // Reserve a loopback port without listening; all proxy connection attempts are refused locally.
        using var endpoint = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);
        endpoint.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var port = ((System.Net.IPEndPoint)endpoint.LocalEndPoint!).Port;
        var proxyContext = await browser.NewContextAsync(new() { Proxy = new() { Server = $"http://127.0.0.1:{port}" } });
        var worker = new SMAdTask(null!, null!, null!, null!, null!, new QTP.Common.Infrastructure.AppSettings());
        using var cancellation = new CancellationTokenSource();
        var ctx = new WorkerRunContext(new() { LinkedCts = cancellation, TaskArgs = JObject.Parse("{isProxyMode:true}") });
        var handle = typeof(SMAdTask).GetMethod("HandleRequestFailure", BindingFlags.NonPublic | BindingFlags.Instance)!;
        proxyContext.RequestFailed += (_, request) => handle.Invoke(worker, new object[] { ctx, request });
        try
        {
            var page = await proxyContext.NewPageAsync();
            bool failed = false;
            try { await page.GotoAsync("http://proxy-fixture.invalid/", new() { Timeout = 5000 }); }
            catch (PlaywrightException) { failed = true; }
            check(failed && ctx.ProxyFailure == ProxyFailureKind.Connection && cancellation.IsCancellationRequested && browser.IsConnected,
                "真实Chromium代理端口失效及时取消当前执行，不影响共享浏览器连接");
        }
        finally { await proxyContext.CloseAsync(); }

        var httpContext = await browser.NewContextAsync();
        try
        {
            var page = await httpContext.NewPageAsync();
            await httpContext.RouteAsync("https://http-error-fixture.test/**", route => route.FulfillAsync(new()
            {
                Status = 503, ContentType = "text/html", Body = "<html><body>Service unavailable</body></html>"
            }));
            var result = await EntryPageNavigator.NavigateAsync(page, "https://http-error-fixture.test/", 5000, default, _ => { });
            check(!result.Opened && result.HttpStatus == 503 && browser.IsConnected,
                "真实Chromium收到503且有可见body时仍拒绝计为入口访问成功");
        }
        finally { await httpContext.CloseAsync(); }
    }

    public static async Task Run(Action<bool, string> check)
    {
        check(ProxyFailureClassifier.Classify("net::ERR_PROXY_CONNECTION_FAILED", true, false) == ProxyFailureKind.Connection,
            "资源请求的明确代理连接失败也能识别");
        check(ProxyFailureClassifier.Classify("net::ERR_TUNNEL_CONNECTION_FAILED", true, true) == ProxyFailureKind.TargetRoute &&
            ProxyFailureClassifier.Classify("net::ERR_TUNNEL_CONNECTION_FAILED", true, false) == null,
            "主页面隧道失败与第三方资源路径失败分开处理");
        check(new[] { "ERR_INVALID_AUTH_CREDENTIALS", "ERR_CONNECTION_RESET", "ERR_TIMED_OUT", "ERR_EMPTY_RESPONSE", "ERR_NAME_NOT_RESOLVED" }
            .All(code => ProxyFailureClassifier.Classify("net::" + code, true, true) == null),
            "普通网络错误和源站认证不能证明代理失效");
        check(ProxyFailureClassifier.Classify("net::ERR_PROXY_CONNECTION_FAILED", false, true) == null,
            "直连模式不标记代理失效");
        check(ProxyFailureClassifier.Address("127.0.0.1:1080", "SOCKS5") == "socks5://127.0.0.1:1080",
            "SOCKS协议统一解析且忽略大小写");
        check(ProxyFailureClassifier.Address("[::1]:1080", "socks5") == "socks5://[::1]:1080",
            "IPv6代理地址不再按冒号错误拆分");
        bool mismatch = false;
        try { ProxyFailureClassifier.Address("http://127.0.0.1:8080", "socks5"); }
        catch (ArgumentException) { mismatch = true; }
        check(mismatch, "检测与浏览器协议冲突时拒绝启动");

        var worker = new SMAdTask(null!, null!, null!, null!, null!, new QTP.Common.Infrastructure.AppSettings());
        var page = ActionStub.Create<IPage>();
        var frame = ActionStub.Create<IFrame>();
        ActionStub.Of(page).Frame = frame;
        ActionStub.Of(frame).Page = page;
        ActionStub.Of(page).Locator = ActionStub.Create<ILocator>();
        var request = ActionStub.Create<IRequest>();
        var requestState = ActionStub.Of(request);
        requestState.Frame = frame;
        requestState.NavigationRequest = true;
        requestState.Url = "https://destination.test/";
        requestState.Failure = "net::ERR_TUNNEL_CONNECTION_FAILED";
        var ctx = new WorkerRunContext(new() { LinkedCts = new(), TaskArgs = JObject.Parse("{isProxyMode:true}") });
        ctx.SetActivePage(page, Stub.Create<ICDPSession>());
        var handleFailure = typeof(SMAdTask).GetMethod("HandleRequestFailure", BindingFlags.NonPublic | BindingFlags.Instance)!;
        handleFailure.Invoke(worker, new object[] { ctx, request });
        check(ctx.ProxyFailed && ctx.Config.LinkedCts.IsCancellationRequested && ctx.BrowserFailureKind == BrowserStopKind.ProxyFailed,
            "about:blank首次导航隧道失败立即停止且记录代理回收原因");
        var originalReason = ctx.ProxyFailedReason;
        Parallel.For(0, 20, _ => ctx.TryMarkProxyFailure(ProxyFailureKind.Authentication, "later failure"));
        check(ctx.ProxyFailedReason == originalReason, "并发代理失败保留首次分类和原因");
        var buildResult = typeof(SMAdTask).GetMethod("BuildWorkerResult", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (WorkerExecutionResult)buildResult.Invoke(null, new object?[] { ctx, true, false, null })!;
        check(result.Status == WorkerExecutionStatus.Failed && result.ProxyFailure == ProxyFailureKind.TargetRoute,
            "失效代理返回明确失败类型供MainClient停止剩余UV");

        var httpContext = new WorkerRunContext(new() { LinkedCts = new() });
        httpContext.SetActivePage(page, Stub.Create<ICDPSession>());
        var response = ActionStub.Create<IResponse>();
        ActionStub.Of(response).Status = 503;
        ActionStub.Of(response).Request = request;
        ActionStub.Of(response).Url = requestState.Url;
        var handleResponse = typeof(SMAdTask).GetMethod("HandleDocumentResponse", BindingFlags.NonPublic | BindingFlags.Instance)!;
        handleResponse.Invoke(worker, new object[] { httpContext, response });
        httpContext.CurrentVisitReady = true;
        httpContext.CompleteCurrentVisit();
        check(!httpContext.ProxyFailed && httpContext.CompletedPvs == 0,
            "主文档503不误判代理失效且不计有效访问");
        var action = new PageActionExecutor(httpContext, _ => { });
        httpContext.PageHttpStatuses.TryRemove(page, out _);
        var click = await action.ExecuteAsync("HTTP error", (_, _) =>
        {
            ActionStub.Of(page).Url = requestState.Url;
            handleResponse.Invoke(worker, new object[] { httpContext, response });
            return Task.FromResult(true);
        }, null, default);
        check(!click.Succeeded && click.Reason?.Contains("503") == true && httpContext.ActionRecords.Last().ObservationDelayMs == 0,
            "点击到HTTP错误页面不会记跳转成功或继续阅读");
        var dispatched = false;
        var sourceError = await action.ExecuteAsync("Invalid source", (_, _) => { dispatched = true; return Task.FromResult(true); }, null, default);
        check(!dispatched && !sourceError.Attempted, "HTTP错误页面不再派发后续点击");
        var optionalRan = false;
        var optional = await OptionalPageOperation.RunAsync(httpContext, "Invalid source", () => { optionalRan = true; return Task.CompletedTask; }, default, _ => { });
        check(!optional && !optionalRan, "HTTP错误页面跳过可选滑动与目标查找");
        ActionStub.Of(response).Status = 407;
        handleResponse.Invoke(worker, new object[] { ctx, response });
        var authContext = new WorkerRunContext(new() { LinkedCts = new(), TaskArgs = JObject.Parse("{isProxyMode:true}") });
        handleResponse.Invoke(worker, new object[] { authContext, response });
        check(authContext.ProxyFailure == ProxyFailureKind.Authentication && authContext.Config.LinkedCts.IsCancellationRequested,
            "代理407触发认证失败并取消当前执行");

        var entry = ActionStub.Create<IPage>();
        ActionStub.Of(entry).Navigate = () => Task.FromResult<IResponse?>(response);
        var navigation = await EntryPageNavigator.NavigateAsync(entry, requestState.Url, 5000, default, _ => { });
        check(!navigation.Opened && navigation.HttpStatus == 407, "入口HTTP错误响应不算页面就绪");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool cancelled = false;
        try { await new ProxyTester().TestAsync("127.0.0.1:1080", cancellation.Token, "socks5"); }
        catch (OperationCanceledException) { cancelled = true; }
        check(cancelled, "代理预检测尊重上游取消");

        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            var tester = new ProxyTester(new[] { "http://health-fixture.invalid/" });
            foreach (var protocol in new[] { "http", "socks5" })
            {
                using var inFlightCancellation = new CancellationTokenSource();
                var testing = tester.TestAsync($"127.0.0.1:{port}", inFlightCancellation.Token, protocol);
                using var connection = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
                var buffer = new byte[256];
                var bytes = await connection.GetStream().ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                check(bytes > 0 && (protocol == "socks5" ? buffer[0] == 5 : buffer[0] == (byte)'G'),
                    $"真实代理预检测发送正确{protocol}协议握手");
                inFlightCancellation.Cancel();
                bool inFlightCancelled = false;
                try { await testing.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (OperationCanceledException) { inFlightCancelled = true; }
                check(inFlightCancelled, $"{protocol}代理检测进行中取消及时退出并等待请求清理");
            }
        }
        finally { listener.Stop(); }
    }
}
