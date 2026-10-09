using System.Reflection;
using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Common;
using QTP.Common.Models;
using QTP.Common.Infrastructure;
using QTP.Plugins;
using SMAd.Models;

internal static class BrowserChecks
{
    public static async Task RunAsync(string executable, Action<bool, string> check, bool rawTouch = false)
    {
        await using var processes = new ChromiumSessionManager();
        var profile = Path.Combine(Environment.CurrentDirectory, "tests/CdpInput.RegressionTests/artifacts", Guid.NewGuid().ToString("N"));
        var process = await processes.StartChromium("input-smoke", Path.GetFullPath(executable), profile,
            TimeSpan.FromMinutes(10), arguments: "--headless=new --no-first-run --disable-background-networking about:blank");
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.ConnectOverCDPAsync(process.CdpEndpoint, new() { Timeout = 5000 });
        var context = browser.Contexts[0];
        var page = await context.NewPageAsync();
        page.Crash += (_, _) => Console.WriteLine("BROWSER DIAGNOSTIC: page crashed");
        page.Close += (_, _) => Console.WriteLine("BROWSER DIAGNOSTIC: page closed");
        browser.Disconnected += (_, _) => Console.WriteLine("BROWSER DIAGNOSTIC: browser disconnected");
        await page.SetViewportSizeAsync(400, 800);
        await page.EvaluateAsync(@"() => {
            document.head.innerHTML='<meta name=""viewport"" content=""width=device-width, initial-scale=1"">';
            document.body.innerHTML='<button id=""tap"" style=""width:150px;height:70px"">tap</button>';
            window.events=[]; window.clicks=0;
            for(const t of ['touchstart','touchend','touchcancel']) document.addEventListener(t,()=>window.events.push(t));
            document.querySelector('#tap').onclick=()=>window.clicks++;
        }");
        await using var sessions = new CDPSessionManager(context);
        var cdp = await sessions.GetOrCreateSessionAsync(page);
        using var cancel = new CancellationTokenSource();
        var ctx = new WorkerRunContext(new() { LinkedCts = cancel, MaxTouchPoints = 5, Sw = 400, Sh = 800, DeviceScale = 1, Os = 1 })
            { Context = context, CdpManager = sessions };
        var task = new SMAdTask(null!, null!, processes, null!, null!, new AppSettings());
        var initialize = typeof(SMAdTask).GetMethod("InitPageAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Task Init() => (Task)initialize.Invoke(task, new object[] { ctx, page, cancel.Token })!;
        await Task.WhenAll(Init(), Init(), Init());
        ctx.SetActivePage(page, cdp);
        var baseline = await cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object> { ["expression"] = "window.clicks", ["returnByValue"] = true }).WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine($"BROWSER DIAGNOSTIC: before touch={baseline}");
        if (rawTouch)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchStart", ["touchPoints"] = new object[] { new { x = 60, y = 40, id = 0 } } });
            await Task.Delay(60);
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
            var raw = await cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object> { ["expression"] = "window.clicks", ["returnByValue"] = true }).WaitAsync(TimeSpan.FromSeconds(5));
            Console.WriteLine($"BROWSER DIAGNOSTIC: raw touch={raw}");
            check(raw?.GetProperty("result").GetProperty("value").GetInt32() == 1, "原始 CDP 触屏对照触发 click");
            await browser.CloseAsync();
            return;
        }
        var human = new HumanTouchOperator();
        check(await human.TapAsync(page, cdp, page.Locator("#tap")), "真实浏览器 Tap 发送成功");
        var direct = await cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object> { ["expression"] = "window.clicks", ["returnByValue"] = true }).WaitAsync(TimeSpan.FromSeconds(5));
        Console.WriteLine($"BROWSER DIAGNOSTIC: CDP click result={direct}");
        check(await page.EvaluateAsync<int>("window.clicks").WaitAsync(TimeSpan.FromSeconds(5)) == 1, "触屏触发真实 click");
        check((await page.EvaluateAsync<string[]>("window.events")).SequenceEqual(new[] { "touchstart", "touchend" }), "页面收到触摸按下与释放");
        await page.EvaluateAsync("() => { const d=document.createElement('div'); d.id='overlay'; d.style='position:fixed;inset:0;z-index:9999;background:white'; document.body.append(d); }");
        check(!await human.TapAsync(page, cdp, page.Locator("#tap")), "遮挡目标拒绝点击");
        check(await page.EvaluateAsync<int>("window.clicks") == 1, "遮挡时没有多余 click");
        await page.EvaluateAsync("document.querySelector('#overlay').remove()");
        await Task.WhenAll(human.TapAsync(page, cdp, page.Locator("#tap")), human.TapAsync(page, cdp, page.Locator("#tap")));
        check(await page.EvaluateAsync<int>("window.clicks") == 3, "真实页面并发 Tap 各执行一次");
        var types = await page.EvaluateAsync<string[]>("window.events");
        check(types.SequenceEqual(Enumerable.Range(0, 3).SelectMany(_ => new[] { "touchstart", "touchend" })), "真实触屏事件无交叉");

        await page.EvaluateAsync(@"() => { const a=document.createElement('a'); a.id='download'; a.textContent='download';
            a.download='test.txt'; a.href=URL.createObjectURL(new Blob(['test'],{type:'text/plain'}));
            a.style='display:block;width:160px;height:60px'; document.body.append(a); }");
        ctx.EnableBusinessDownloads();
        var downloaded = page.WaitForDownloadAsync(new() { Timeout = 5000 });
        check(await human.TapAsync(page, cdp, page.Locator("#download")), "触屏触发下载链接");
        await downloaded;
        await Task.Delay(150);
        check(ctx.TriggerDownloadSign == 1, "重复初始化后单个下载只计数一次");
        await sessions.DisposeAsync();
        bool detached = false;
        try { await cdp.SendAsync("Runtime.evaluate", new Dictionary<string, object> { ["expression"] = "1" }); } catch (PlaywrightException) { detached = true; }
        check(detached, "真实 CDP 会话已显式分离");
        await page.CloseAsync();
        await browser.CloseAsync();
        await processes.CloseAsync(process.UniqueId);
    }
}
