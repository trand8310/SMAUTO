using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Common;

internal static class MovementBrowserChecks
{
    public static async Task RunAsync(string executable, Action<bool, string> check)
    {
        await using var processes = new ChromiumSessionManager();
        var profile = Path.Combine(Path.GetTempPath(), "smad-movement-" + Guid.NewGuid().ToString("N"));
        var process = await processes.StartChromium("movement-fixture", Path.GetFullPath(executable), profile,
            TimeSpan.FromMinutes(3), arguments: "--headless=new --no-first-run --disable-background-networking about:blank");
        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.ConnectOverCDPAsync(process.CdpEndpoint);
        try
        {
            await ProxyFailureChecks.RunBrowserAsync(browser, check);
            var page = await browser.Contexts[0].NewPageAsync();
            await page.SetViewportSizeAsync(400, 800);
            var cdp = await page.Context.NewCDPSessionAsync(page);
            var human = new HumanTouchOperator();
            human.Engine.Rhythm.Enabled = false;
            async Task LoadFixture(string html)
            {
                await page.GotoAsync("data:text/html;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(html)),
                    new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 5000 });
            }
            async Task Fixture(int targetTop)
            {
                await page.GotoAsync("about:blank");
                await LoadFixture($$"""
                    <meta name="viewport" content="width=device-width,initial-scale=1">
                    <style>body{margin:0;height:2600px}header,footer{position:fixed;left:0;right:0;z-index:10;background:#ddd}
                    header{top:0;height:100px}footer{bottom:0;height:80px}
                    #target{position:absolute;left:70px;top:{{targetTop}}px;width:200px;height:70px}</style>
                    <header>Search</header><button id="target" onclick="window.clicks++">Target</button><footer>Download</footer>
                    <script>window.clicks=0</script>
                    """);
            }
            await Fixture(1700);
            var target = page.Locator("#target");
            var distant = await human.MoveToTargetAsync(page, cdp, target);
            check(distant.Ready && distant.Traces.Count > 0, "真实触屏将屏外目标移入固定顶栏和底栏之间");
            var box = await target.BoundingBoxAsync();
            check(box != null && box.Y + box.Height / 2 >= 176 && box.Y + box.Height / 2 <= 576,
                "移动结果位于视口舒适区域");
            check(await human.TapAsync(page, cdp, target) && await page.EvaluateAsync<int>("window.clicks") == 1,
                "移到目标后真实触屏点击成功");

            await Fixture(740);
            var partial = await human.MoveToTargetAsync(page, cdp, page.Locator("#target"));
            check(partial.Ready && partial.Traces.Count > 0 && partial.Traces.All(t => t.Direction == HumanSwipeDirection.Up),
                "底栏覆盖的部分可见目标继续纵向移动而非误判到位或横向滑动");
            check(await human.TapAsync(page, cdp, page.Locator("#target")), "底栏目标移出遮挡后可点击");

            await page.GotoAsync("about:blank");
            await LoadFixture("""
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <style>body{margin:0}#panel{position:absolute;left:20px;top:140px;width:350px;height:450px;overflow:auto}
                #content{position:relative;height:1900px}#target{position:absolute;top:1300px;left:50px;width:180px;height:60px}</style>
                <div id="panel"><div id="content"><button id="target">Target</button></div></div>
                """);
            var nested = await human.MoveToTargetAsync(page, cdp, page.Locator("#target"));
            check(nested.Ready && await page.Locator("#panel").EvaluateAsync<double>("el=>el.scrollTop") > 0,
                "真实触屏移动嵌套滚动容器内的目标");
            await page.EvaluateAsync("() => { const overlay=document.createElement('div'); overlay.style='position:fixed;inset:0;z-index:999;background:white'; document.body.append(overlay); }");
            var covered = await human.Engine.MoveToTargetAsync(page, cdp, page.Locator("#target"), maxSwipes: 0);
            check(!covered.Ready, "被遮罩覆盖的目标不得报告已到位");

            await LoadFixture("""
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <style>body{margin:0;height:2000px}.offer{position:absolute;left:70px;width:180px;height:60px}
                header,footer{position:fixed;left:0;right:0;z-index:10;background:#ddd}
                header{top:0;height:100px}footer{bottom:0;height:80px}
                #panel{position:absolute;left:0;top:400px;width:350px;height:100px;overflow:hidden}</style>
                <header>Search</header><footer>Download</footer>
                <div class="offer-item offer" style="top:20px">Header covered</div>
                <div class="offer-item offer" style="top:180px" onclick="window.clicks++">Visible</div>
                <div class="offer-item offer" style="top:740px">Footer covered</div>
                <div class="offer-item offer" style="top:1200px">Offscreen</div>
                <div id="panel"><div class="offer-item offer" style="top:130px">Clipped</div></div>
                <script>window.clicks=0</script>
                """);
            var visibleFilter = typeof(SMAd.LandingPolicy.BaiduB2BLandingPageStrategy).GetMethod(
                "GetVisibleCandidateIndicesAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            async Task<int[]> VisibleIndices() => await (Task<int[]>)visibleFilter.Invoke(null,
                new object[] { page.Locator(".offer"), CancellationToken.None })!;
            check((await VisibleIndices()).SequenceEqual(new[] { 1 }),
                "爱采购当前视口候选排除顶栏底栏遮挡、屏外及容器裁剪元素");
            var sharedFilter = typeof(SMAd.LandingPolicy.AliLandingPageStrategy).Assembly
                .GetType("SMAd.LandingPolicy.ViewportLandingInteraction")!.GetMethod(
                    "GetVisibleCandidateIndicesAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var aliIndices = await (Task<int[]>)sharedFilter.Invoke(null, new object[] {
                page.Locator("div[class^='offer-item']"), CancellationToken.None })!;
            check(aliIndices.SequenceEqual(new[] { 1 }), "1688 商品卡片使用同一视口筛选，排除遮挡及屏外商品");
            var scrollBeforeTap = await page.EvaluateAsync<double>("scrollY");
            check(await human.TapAsync(page, cdp, page.Locator(".offer").Nth(1)) &&
                await page.EvaluateAsync<int>("window.clicks") == 1 &&
                await page.EvaluateAsync<double>("scrollY") == scrollBeforeTap,
                "当前视口候选直接真实触屏点击，无目标定位滚动");
            await page.EvaluateAsync("() => { const mask=document.createElement('div'); mask.style='position:fixed;inset:0;z-index:999;background:white'; document.body.append(mask); }");
            check((await VisibleIndices()).Length == 0, "遮罩覆盖后当前视口无候选，不选择屏外元素");

            await LoadFixture("""
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <style>body{margin:0;height:2000px}a.word{position:absolute;left:60px;width:200px;height:50px}
                header{position:fixed;top:0;left:0;right:0;height:100px;z-index:10;background:#ddd}</style>
                <header>Search</header>
                <div class="ab-recommend-words" style="display:none"><a class="word" href="#hidden">Hidden first section</a></div>
                <div class="ab-recommend-words">
                  <a class="word" href="#visible" style="top:200px" onclick="event.preventDefault();window.clicks++">Visible word</a>
                  <a class="word" href="#offscreen" style="top:1200px">Offscreen word</a>
                  <a class="word" href="#covered" style="top:20px">Covered word</a>
                </div><script>window.clicks=0</script>
                """);
            var words = page.Locator("div[class*='ab-recommend-words'] a.word");
            var wordIndices = await (Task<int[]>)sharedFilter.Invoke(null,
                new object[] { words, CancellationToken.None })!;
            check(wordIndices.SequenceEqual(new[] { 1 }),
                "推荐词跨多个区域筛选，隐藏首区域不妨碍选择当前可见词");
            check(await human.TapAsync(page, cdp, words.Nth(wordIndices.Single())) &&
                await page.EvaluateAsync<int>("window.clicks") == 1 && await page.EvaluateAsync<double>("scrollY") == 0,
                "推荐词直接真实触屏点击，不移动寻找屏外词");

            var entryLogs = new System.Collections.Concurrent.ConcurrentQueue<string>();
            await using var entryContext = await browser.NewContextAsync(new() { AcceptDownloads = true });
            var entryPage = await entryContext.NewPageAsync();
            int entryRequests = 0;
            EventHandler<IDownload> cancelEntryDownload = async (_, download) =>
            {
                try { await download.CancelAsync(); } catch { }
            };
            entryPage.Download += cancelEntryDownload;
            await entryPage.RouteAsync("https://entry-fixture.test/**", async route =>
            {
                if (!route.Request.IsNavigationRequest)
                {
                    await route.FulfillAsync(new() { Status = 204 });
                    return;
                }
                if (Interlocked.Increment(ref entryRequests) == 1)
                    await route.FulfillAsync(new() { Status = 200, ContentType = "application/octet-stream",
                        Headers = new Dictionary<string, string> { ["content-disposition"] = "attachment; filename=entry-test.bin" }, Body = "entry download" });
                else
                    await route.FulfillAsync(new() { Status = 200, ContentType = "text/html", Body = "<html><body>Search ready</body></html>" });
            });
            try
            {
                var entry = await SMAd.PageActions.EntryPageNavigator.NavigateAsync(entryPage,
                    "https://entry-fixture.test/search", 5000, default, message =>
                    { entryLogs.Enqueue(message); Console.WriteLine("ENTRY DIAGNOSTIC: " + message); });
                check(entry.Opened && entryRequests == 2 && browser.IsConnected,
                    "真实附件响应引发入口下载后，同一浏览器重试恢复HTML页面");
                check(entryLogs.Any(x => x.StartsWith("Entry download:") && x.Contains("entry-test.bin")),
                    "真实入口下载诊断记录下载URL及建议文件名");
            }
            finally
            {
                await entryPage.UnrouteAsync("https://entry-fixture.test/**");
                entryPage.Download -= cancelEntryDownload;
            }
        }
        finally
        {
            await browser.CloseAsync();
            await processes.CloseAsync(process.UniqueId);
        }
    }
}
