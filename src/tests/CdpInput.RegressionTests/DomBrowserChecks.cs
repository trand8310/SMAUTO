using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Common;

internal static class DomBrowserChecks
{
    public static async Task RunAsync(string executable, Action<bool, string> check)
    {
        await using var processes = new ChromiumSessionManager();
        var profile = Path.Combine(Path.GetTempPath(), "smad-dom-" + Guid.NewGuid().ToString("N"));
        var process = await processes.StartChromium("dom-fixture", Path.GetFullPath(executable), profile,
            TimeSpan.FromMinutes(2), arguments: "--headless=new --no-first-run --disable-background-networking about:blank");
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.ConnectOverCDPAsync(process.CdpEndpoint, new() { Timeout = 5000 });
        try
        {
            var page = await browser.Contexts[0].NewPageAsync();
            await page.SetViewportSizeAsync(400, 800);
            await page.EvaluateAsync(@"() => {
                document.body.style.margin='0';
                const frame=document.createElement('iframe');
                frame.id='fixture'; frame.style='position:absolute;left:40px;top:80px;width:250px;height:220px;border:0';
                frame.srcdoc='<style>body{margin:0}#panel{height:120px;width:180px;overflow:auto}#content{height:1200px}#target{position:relative;top:600px;height:30px;width:80px}</style><div id=panel><div id=content><button id=target>target</button></div></div>';
                document.body.append(frame);
            }").WaitAsync(TimeSpan.FromSeconds(5));
            var target = page.FrameLocator("#fixture").Locator("#target");
            await target.WaitForAsync(new() { State = WaitForSelectorState.Attached, Timeout = 5000 });
            var resolver = new ScrollTargetResolver();
            var before = await resolver.GetAtPointAsync(page, 60, 120);
            check(before.Kind == "element" && before.ClientHeight == 120, "本地真实 DOM 解析 iframe 内滚动容器");
            var rectBefore = await resolver.GetElementRectAsync(target);
            var frame = page.Frames.Single(f => f.ParentFrame != null);
            await frame.EvaluateAsync("() => document.querySelector('#panel').scrollTop=500");
            var stable = await resolver.DidScrollAsync(page, before, new(), 60, 120, HumanSwipeDirection.Up, 5);
            check(stable, "本地真实 DOM 在原 iframe 容器采样并确认稳定");
            var rectAfter = await resolver.GetElementRectAsync(target);
            check(rectAfter != null && rectBefore != null && Math.Abs(rectBefore.Y - rectAfter.Y - 500) < 1 && rectAfter.X >= 40,
                "本地真实 iframe BoundingBox 使用主视口偏移");
            var now = await resolver.GetAtPointAsync(page, 60, 120);
            check(before.Key == now.Key && now.ScrollTop == 500, "滚动后触点解析保留容器节点身份");
        }
        finally
        {
            await browser.CloseAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await processes.CloseAsync(process.UniqueId);
        }
    }
}
