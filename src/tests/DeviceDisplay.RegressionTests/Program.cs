using System.Globalization;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using QTP.Common;
using SMAd.DeviceEmulation;
using SMAd.Models;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
TaskConfig Config(string json = "{}", string ua = "Android Chrome Mobile") => new()
{ Sw = 412, Sh = 915, DeviceScale = 2.625f, Os = 1, TaskArgs = JObject.Parse(json), UserAgent = ua };
var profile = DeviceDisplayProfile.From(Config());
Check(profile.AvailableHeight == 915 && profile.ViewportHeight == 811, "屏幕可用高度不扣浏览器工具栏，网页视口单独扣栏位");
Check(DeviceDisplayProfile.From(Config(ua: "Android; wv) Chrome Mobile")).ViewportHeight == 867, "WebView 默认不扣 Chrome 工具栏");
var explicitProfile = DeviceDisplayProfile.From(Config("{dev:{availHeight:867,viewportHeight:800,windowWidth:640,windowHeight:1040}}"));
Check(explicitProfile.AvailableHeight == 867 && explicitProfile.ViewportHeight == 800 && explicitProfile.WindowWidth == 640, "支持明确覆盖屏幕可用区域、视口及 Windows 窗口尺寸");
var oldCulture = CultureInfo.CurrentCulture;
try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); Check(profile.DprArgument == "2.625", "DPR 启动参数不受区域小数格式影响"); }
finally { CultureInfo.CurrentCulture = oldCulture; }
try { DeviceDisplayProfile.From(Config("{dev:{viewportHeight:0}}")); throw new Exception("Accepted invalid size"); }
catch (ArgumentOutOfRangeException) { Check(true, "非法视口尺寸在启动前拒绝"); }
using (var cancel = new CancellationTokenSource())
{
    var worker = new QTP.Plugins.SMAdTask(null!, null!, null!, null!, null!, new QTP.Common.Infrastructure.AppSettings());
    var build = typeof(QTP.Plugins.SMAdTask).GetMethod("BuildTaskConfig", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var input = JObject.Parse("{os:1,kernelVersion:'135',cacheName:'display-test',task:{id:1,url:'about:blank'},dev:{sw:1080,sh:2400,ua:'Android Chrome Mobile',dpr:3}}");
    var config = (TaskConfig)build.Invoke(worker, new object[] { "display-test", input, cancel })!;
    Check(config.Sw == 360 && config.Sh == 800 && config.DeviceScale == 3, "明确 DPR 时根据物理分辨率生成 CSS 屏幕尺寸");
    input["dev"]!["cssWidth"] = 400; input["dev"]!["cssHeight"] = 900;
    config = (TaskConfig)build.Invoke(worker, new object[] { "display-test", input, cancel })!;
    Check(config.Sw == 400 && config.Sh == 900, "明确 CSS 屏幕尺寸优先于自动匹配");
}
if (args.Length > 0)
{
    if (!args.Contains("--require-native-avail")) explicitProfile = explicitProfile with { AvailableHeight = 915 };
    using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var token = deadline.Token;
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    string url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
    var fixture = ServeAsync(listener, token);
    var root = Path.Combine(Environment.CurrentDirectory, "tests/DeviceDisplay.RegressionTests/artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    await using var provider = new PlaywrightProvider();
    await using var processes = new ChromiumSessionManager();
    await using var runtime = new BrowserRuntimeManager(provider, processes, new(1, 1));
    var headed = args.Contains("--headed");
    await using var lease = await runtime.AcquireAsync(new("display", Path.GetFullPath(args[0]), Path.Combine(root, "profile"),
        TimeSpan.FromMinutes(2), $"{(headed ? "" : "--headless=new")} --no-first-run --no-default-browser-check --disable-popup-blocking --window-size=500,1043 " +
        $"--screen-size={explicitProfile.ScreenWidth},{explicitProfile.ScreenHeight} --screen-avail-size={explicitProfile.AvailableWidth},{explicitProfile.AvailableHeight} --device-pixel-ratio={explicitProfile.DprArgument} about:blank"), token);
    var errors = new List<Exception>();
    await using var controller = await DeviceTargetController.CreateAsync(lease.ProcessSession.CdpEndpoint, explicitProfile, 5,
        ex => { Console.WriteLine("Device target failure: " + ex); lock (errors) errors.Add(ex); }, token);
    await using var sessions = new QTP.Plugins.CDPSessionManager(lease.Context);
    var page = lease.Context.Pages[0];
    var cdp = await sessions.GetOrCreateSessionAsync(page);
    await controller.WaitForPageAsync(cdp, token);
    var nativeWindow = await DevicePageDisplay.ApplyAsync(cdp, explicitProfile, token);
    Console.WriteLine("Window actual: " + nativeWindow);
    Check(nativeWindow.GetProperty("bounds").GetProperty("width").GetInt32() == 640 &&
        nativeWindow.GetProperty("bounds").GetProperty("height").GetInt32() == 1040, "实际浏览器外部窗口使用独立 DIP 尺寸");
    await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Load, Timeout = 15000 });
    var reports = new List<object>();
    async Task Inspect(IPage p, string label)
    {
        await p.WaitForFunctionAsync("window.firstMetrics !== undefined", new PageWaitForFunctionOptions { Timeout = 10000 });
        var metrics = await p.EvaluateAsync<JsonElement>("window.firstMetrics");
        Console.WriteLine(label + " actual: " + metrics);
        reports.Add(new { Page = label, FirstScriptMetrics = metrics });
        Check(metrics.GetProperty("sw").GetInt32() == 412 && metrics.GetProperty("sh").GetInt32() == 915 &&
            metrics.GetProperty("aw").GetInt32() == 412 && metrics.GetProperty("ah").GetInt32() == explicitProfile.AvailableHeight &&
            Math.Abs(metrics.GetProperty("dpr").GetDouble() - 2.625) < .001 &&
            Math.Abs(metrics.GetProperty("vw").GetDouble() - 412) < 1 && Math.Abs(metrics.GetProperty("vh").GetDouble() - 800) < 1,
            label + "首个脚本已读取正确的屏幕、可用屏幕、DPR 和网页视口");
    }
    await Inspect(page, "主页面");
    var popup = await page.RunAndWaitForPopupAsync(() => page.EvaluateAsync("url => window.open(url, '_blank')", url + "popup"));
    await popup.WaitForLoadStateAsync(); await Inspect(popup, "弹窗标签页");
    var popupCdp = await sessions.GetOrCreateSessionAsync(popup);
    await controller.WaitForPageAsync(popupCdp, token);
    await DevicePageDisplay.ApplyAsync(popupCdp, explicitProfile, token);
    var newWindow = await page.RunAndWaitForPopupAsync(() => page.EvaluateAsync("url => window.open(url, '_blank', 'width=250,height=300')", url + "window"));
    await newWindow.WaitForLoadStateAsync(); await Inspect(newWindow, "新窗口");
    var newCdp = await sessions.GetOrCreateSessionAsync(newWindow);
    await controller.WaitForPageAsync(newCdp, token);
    var bounds = await DevicePageDisplay.ApplyAsync(newCdp, explicitProfile, token);
    Check(bounds.GetProperty("bounds").GetProperty("width").GetInt32() == 640, "业务指定的小弹窗恢复为任务窗口配置");
    await popup.GotoAsync(url + "redirect"); await Inspect(popup, "同标签页再次导航");
    var blank = await lease.Context.NewPageAsync();
    var blankCdp = await sessions.GetOrCreateSessionAsync(blank);
    await controller.WaitForPageAsync(blankCdp, token); await DevicePageDisplay.ApplyAsync(blankCdp, explicitProfile, token);
    await blank.GotoAsync(url + "newpage"); await Inspect(blank, "主动创建的页面");
    lock (errors) Check(errors.Count == 0, "新页面目标没有初始化错误");
    if (!args.Contains("--require-native-avail"))
    {
        try
        {
            await DevicePageDisplay.ApplyAsync(cdp, explicitProfile with { AvailableHeight = 867 }, token);
            throw new Exception("Native mismatch was accepted");
        }
        catch (InvalidOperationException)
        {
            Check(await page.EvaluateAsync<int>("screen.availHeight") == 915,
                "原生可用屏幕不匹配时拒绝配置，页面属性未被 JS 修改");
        }
    }
    await File.WriteAllTextAsync(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new { Executable = args[0], NativeOnly = true, Headed = headed, Profile = explicitProfile, Reports = reports }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("REPORT " + Path.Combine(root, "report.json"));
    deadline.Cancel(); listener.Stop(); try { await fixture; } catch (OperationCanceledException) { }
}
Console.WriteLine($"{checks} checks passed.");

static async Task ServeAsync(TcpListener listener, CancellationToken token)
{
    var jobs = new List<Task>();
    try
    {
        while (!token.IsCancellationRequested)
        {
            var client = await listener.AcceptTcpClientAsync(token);
            jobs.Add(ServeClientAsync(client, token));
            jobs.RemoveAll(job => job.IsCompletedSuccessfully);
        }
    }
    finally
    {
        try { await Task.WhenAll(jobs); } catch (OperationCanceledException) { }
    }
}

static async Task ServeClientAsync(TcpClient client, CancellationToken token)
{
    const string html = "<meta name='viewport' content='width=device-width, initial-scale=1'><script>window.firstMetrics={sw:screen.width,sh:screen.height,aw:screen.availWidth,ah:screen.availHeight,dpr:devicePixelRatio,vw:visualViewport.width,vh:visualViewport.height};</script><p>local display fixture</p>";
    using (client)
    {
        using var stream = client.GetStream();
        var buffer = new byte[8192]; if (await stream.ReadAsync(buffer, token) == 0) return;
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, token); await stream.WriteAsync(body, token);
    }
}
