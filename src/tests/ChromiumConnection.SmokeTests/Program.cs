using System.Reflection;
using Microsoft.Playwright;
using QTP.Common;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}

var endpointType = typeof(ChromiumSessionManager).Assembly.GetType("QTP.Common.ChromiumDevToolsEndpoint")!;
var parse = endpointType.GetMethod("TryParse")!;
bool Parse(params string[] lines) => (bool)parse.Invoke(null, new object?[] { lines, null })!;
Check(Parse("49152", "/devtools/browser/test"), "解析有效发现文件");
Check(!Parse("0", "/devtools/browser/test") && !Parse("65536", "/devtools/browser/test"), "拒绝非法端口");
Check(!Parse("49152") && !Parse("49152", ""), "部分写入等待重试");
Check(!Parse("49152", "/devtools/page/test"), "拒绝页面级端点");
Check(!Parse("49152", "/devtools/browser/test?x=1"), "拒绝异常浏览器路径");

if (args.Length == 0)
{
    Console.WriteLine($"All {checks} parser checks passed. Pass a Chromium executable path for browser smoke tests.");
    return;
}

string exe = Path.GetFullPath(args[0]);
string root = Path.Combine(Environment.CurrentDirectory, "tests", "ChromiumConnection.SmokeTests", "artifacts", Guid.NewGuid().ToString("N"));
await using var manager = new ChromiumSessionManager();
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var starts = Enumerable.Range(0, 2).Select(i => manager.StartChromium(
    $"smoke-{i}", exe, Path.Combine(root, i.ToString()), TimeSpan.FromMinutes(2),
    arguments: "--headless=new --no-first-run --no-default-browser-check --disable-background-networking about:blank",
    readyTimeout: TimeSpan.FromSeconds(20), token: timeout.Token));
var sessions = await Task.WhenAll(starts);
Check(sessions[0].DebugPort != sessions[1].DebugPort, "并发浏览器自动分配不同端口");
Check(sessions.All(s => s.CdpEndpoint.StartsWith($"ws://127.0.0.1:{s.DebugPort}/devtools/browser/")), "每个会话保存浏览器 WebSocket 地址");
using var playwright = await Playwright.CreateAsync();
foreach (var session in sessions)
{
    var browser = await playwright.Chromium.ConnectOverCDPAsync(session.CdpEndpoint,
        new BrowserTypeConnectOverCDPOptions { Timeout = 5000 });
    Check(browser.IsConnected && browser.Contexts.Count > 0, "直接 WebSocket CDP 连接可用");
    var page = await browser.Contexts[0].NewPageAsync();
    // 连接测试直接验证 DOM；自定义内核的 load 事件兼容性另行验证。
    var title = await page.EvaluateAsync<string>("() => { document.title = 'auto-port-smoke'; document.body.innerHTML = '<p>ok</p>'; return document.title; }")
        .WaitAsync(TimeSpan.FromSeconds(5));
    Check(title == "auto-port-smoke", "连接后可操作页面 DOM");
    await page.CloseAsync();
    await browser.CloseAsync();
    await manager.CloseAsync(session.UniqueId);
}
Check(manager.Count == 0, "关闭后清理受管会话");
using var canceled = new CancellationTokenSource();
canceled.Cancel();
try
{
    await manager.StartChromium("canceled", exe, Path.Combine(root, "canceled"), TimeSpan.FromMinutes(1), token: canceled.Token);
    throw new InvalidOperationException("Canceled launch succeeded");
}
catch (OperationCanceledException) { Check(manager.Count == 0, "取消启动不留下会话"); }
Console.WriteLine($"All {checks} checks passed.");
