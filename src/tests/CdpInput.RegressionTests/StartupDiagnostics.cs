using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using HumanTouchP3.Input;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Plugins;
using SMAd.Models;
using SMAd.PageActions;
using SMAd;

internal static class StartupDiagnostics
{
    public static async Task RunAsync(string[] args)
    {
        string Option(string key, string fallback) => args.FirstOrDefault(a => a.StartsWith(key + "="))?[(key.Length + 1)..] ?? fallback;
        var root = Path.GetFullPath(Option("--base", "Build/publish"));
        var settings = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(root, "appsettings.user.json")))["AppSettings"]!.ToObject<AppSettings>()!;
        var executable = Path.Combine(root, "File", "chrome-win", settings.KernelVersion, "chrome.exe");
        var data = File.ReadLines(Path.Combine(root, "Data", $"dev_{settings.UsingDevIndex}.log")).Take(1500).Select(JObject.Parse)
            .GroupBy(d => (d.Value<string>("make"), d.Value<string>("model"))).Select(g => g.First()).Take(20).ToArray();
        var rounds = Math.Clamp(int.Parse(Option("--rounds", "6")), 1, 30);
        var actualUrl = Option("--url", "");
        var reportRoot = Path.GetFullPath(Path.Combine("artifacts", "browser-operation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(reportRoot);
        var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void Log(string message) { logs.Enqueue(message); Console.WriteLine(message); }
        var reports = new List<object>();
        int failures = 0;
        await using var provider = new PlaywrightProvider();
        await using var processes = new ChromiumSessionManager();
        await using var runtime = new BrowserRuntimeManager(provider, processes,
            new(Math.Max(1, settings.MaximumConcurrency), Math.Max(1, settings.BrowserLaunchConcurrency)));
        Log($"CONFIG kernel={settings.KernelVersion}, headless={settings.IsHiddenMode}, incognito={settings.Incognito}, lifetime={settings.IpTtl}s, proxy=false, rounds={rounds}");
        for (int round = 0; round < rounds; round++)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(settings.IpTtl));
            var token = cancellation.Token;
            var dev = (JObject)data[round % data.Length].DeepClone();
            var os = settings.UsingDevIndex == 4 ? 2 : settings.UsingDevIndex == 7 ? 7 : 1;
            // The local catalogue lists storage variants; API devices carry one selected value.
            if (dev["storage"]?.Type == JTokenType.String)
                dev["storage"] = long.Parse(dev.Value<string>("storage")!.Split(',')[0].Trim());
            var ua = dev.Value<string>("ua") ?? "";
            var match = Regex.Match(ua, @"Chrome/([\d.]+)");
            dev["full_version"] = os == 2 ? dev.Value<string>("osv") : match.Success ? match.Groups[1].Value : settings.KernelVersion;
            var model = Regex.Match(ua, @"(?<=Android\s+)([\d.]+);([\S\s]+)(?=Build)");
            if (os == 1 && model.Success)
            {
                dev["osv"] = model.Groups[1].Value.Trim();
                dev["model"] = Regex.Replace(model.Groups[2].Value.Trim(), "^.*?;\\s*", "");
            }
            if (os == 7) { dev["gpu"] = dev["renderer"]; dev["vendor"] = dev["vender"]; }
            var worker = new SMAdTask(provider, null!, processes, null!, null!, settings, runtime);
            worker.OnLogEventHandler += (_, e) => Log(e.Message);
            var taskArgs = new JObject
            {
                ["task"] = new JObject { ["id"] = 0, ["url"] = "about:blank" }, ["dev"] = dev,
                ["os"] = os, ["kernelVersion"] = settings.KernelVersion,
                ["cacheName"] = "diagnostic", ["totalPV"] = 1, ["processIndex"] = 0,
                ["isProxyMode"] = false, ["isHiddenMode"] = settings.IsHiddenMode, ["incognito"] = settings.Incognito,
                ["pageLoadingTimeout"] = settings.PageLoadingTimeout, ["pageloadedDelay"] = settings.PageloadedDelay,
                ["protocol"] = settings.Protocol, ["scaleX"] = 1, ["scaleY"] = 1
            };
            TaskConfig config = (TaskConfig)Invoke(worker, "BuildTaskConfig", "diagnostic-" + round, taskArgs, cancellation)!;
            var buildArgs = new object?[] { config, null };
            var switches = (List<string>)Invoke(worker, "BuildChromiumArgs", buildArgs)!;
            BrowserRuntimeLease? lease = null;
            WorkerRunContext? ctx = null;
            string stage = "launch", outcome = "success";
            string? error = null;
            bool? exitedBeforeCleanup = null;
            bool connectedBeforeCleanup = false;
            var clock = Stopwatch.StartNew();
            try
            {
                lease = await runtime.AcquireAsync(new("diagnostic-" + round, executable, Path.Combine(reportRoot, "profile-" + round),
                    TimeSpan.FromSeconds(settings.IpTtl), "about:blank " + string.Join(" ", switches) + " --no-proxy-server"), token);
                ctx = new WorkerRunContext(config) { BrowserSession = lease, Browser = lease.Browser, Context = lease.Context,
                    CdpManager = new CDPSessionManager(lease.Context) };
                lease.RegisterCleanup(async () =>
                {
                    if (ctx.DeviceTargets != null) await ctx.DeviceTargets.DisposeAsync();
                    await ctx.CdpManager.DisposeAsync();
                });
                stage = "production page initialization";
                await (Task)Invoke(worker, "AttachLifecycleEventsAsync", ctx, token)!;
                await (Task)Invoke(worker, "ConfigureContextAsync", ctx, token)!;
                var user = HumanUserProfile.CreateRandom(SMAd.PlaywrightHumanInput.StableSeed.Create(dev));
                ctx.human = new HumanTouchOperator(new HumanTouchOperatorOptions
                {
                    Session = new HumanTouchSession(user, dev.Value<string>("make") ?? "", dev.Value<string>("model") ?? "", desktopCdp: true),
                    EnablePageContextAwareness = true, PageContextRefreshEveryGestures = 1, Log = Log
                });
                ctx.human.Engine.IsActivePage = p => ReferenceEquals(ctx.Page, p);
                var page = ctx.Page!;
                var cdp = ctx.CdpSession!;
                if (args.Contains("--entry-readiness-live"))
                {
                    stage = "configured entry live readiness";
                    var source = await File.ReadAllTextAsync("Plugins/SMAd/SMAdTask.cs", token);
                    var configuredUrl = Regex.Match(source, "(?m)^\\s*entry\\.FirstPageUrl\\s*=\\s*\"(https?://[^\"]+)\";");
                    if (!configuredUrl.Success) throw new Exception("No active test entry URL found");
                    var timer = Stopwatch.StartNew();
                    var result = await EntryPageNavigator.NavigateAsync(page, configuredUrl.Groups[1].Value,
                        config.PageLoadingTimeoutMs, token, Log);
                    if (!result.Opened) throw new Exception("Test entry did not open: " + result.Reason);
                    var readiness = await page.EvaluateAsync<string>("() => document.readyState").WaitAsync(TimeSpan.FromSeconds(5), token);
                    var ads = await page.Locator("div[ad_dot_url^='http'],div.ad-wolong-container:has(a[data-url^='http'])").CountAsync().WaitAsync(TimeSpan.FromSeconds(5), token);
                    Log($"LIVE ENTRY readiness={readiness}, reason={result.Reason ?? "DOMContentLoaded"}, navigationMs={timer.ElapsedMilliseconds}, adMarkers={ads}, configuredPostLoadDelayMs={config.PageLoadedDelayMs}");
                    await Task.Delay(config.PageLoadedDelayMs, token);
                    var markerDetails = await page.EvaluateAsync<string>(@"() => { const markers = Array.from(document.querySelectorAll('div[ad_dot_url^=""http""],div.ad-wolong-container:has(a[data-url^=""http""])')); return JSON.stringify(markers.map(el => {
                        const r = el.getBoundingClientRect(), s = getComputedStyle(el);
                        return { visible: r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && Number(s.opacity) > 0,
                            inViewport: r.bottom > 0 && r.top < innerHeight && r.right > 0 && r.left < innerWidth,
                            adDot: el.hasAttribute('ad_dot_url'), wolong: el.classList.contains('ad-wolong-container'), containsMarkers: markers.map((other, index) => other !== el && el.contains(other) ? index : -1).filter(index => index >= 0), rect: { top: Math.round(r.top), height: Math.round(r.height) },
                            text: (el.innerText || '').replace(/\s+/g, ' ').slice(0, 100) };
                    })); }").WaitAsync(TimeSpan.FromSeconds(5), token);
                    Log($"LIVE ENTRY markers after configured delay: {markerDetails}");
                }
                if (args.Contains("--ai-site-live") || args.Contains("--ai-site-inspect") || args.Contains("--ai-site-close"))
                {
                    stage = "configured AI site live test";
                    var source = await File.ReadAllTextAsync("Plugins/SMAd/SMAdTask.cs", token);
                    var configuredUrl = Regex.Match(source, "(?m)^\\s*entry\\.FirstPageUrl\\s*=\\s*\"(https://aisite\\.[^\"]+)\";");
                    if (!configuredUrl.Success) throw new Exception("No active AI site URL found in the test branch");
                    ctx.SetActivePage(page, cdp);
                    ctx.human.Engine.IsActiveBinding = (p, s) => ctx.ActivePage is { } active &&
                        ReferenceEquals(active.Page, p) && ReferenceEquals(active.Session, s);
                    var entry = await EntryPageNavigator.NavigateAsync(page, configuredUrl.Groups[1].Value,
                        settings.PageLoadingTimeout * 1000, token, Log);
                    if (!entry.Opened) throw new Exception("AI site entry failed: " + entry.Reason);
                    await Task.Delay(5000, token);
                    if (args.Contains("--ai-site-close"))
                    {
                        stage = "close AI site popup";
                        var method = typeof(SMAd.LandingPolicy.AiSiteLandingPageStrategy).GetMethod("ClosePopupAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
                        var closeTask = (Task)method.Invoke(null, new object[] { ctx, token, (Action<string>)Log })!;
                        await closeTask;
                        var closeResult = closeTask.GetType().GetProperty("Result")!.GetValue(closeTask)!.ToString();
                        Log("AI site close result=" + closeResult);
                        if (closeResult != "Closed") throw new Exception("Popup closure not confirmed: " + closeResult);
                    }
                    else if (args.Contains("--ai-site-inspect"))
                    {
                        stage = "inspect AI site popup";
                        var snapshot = await page.EvaluateAsync<string>(@"() => JSON.stringify(Array.from(document.querySelectorAll('[class*=close], [aria-label], .animate-container')).map(e => {
                          const r=e.getBoundingClientRect(), s=getComputedStyle(e), x=r.left+r.width/2, y=r.top+r.height/2;
                          return {tag:e.tagName, cls:e.getAttribute('class'), aria:e.getAttribute('aria-label'), role:e.getAttribute('role'),
                            visible:s.display!=='none' && s.visibility!=='hidden' && r.width>0 && r.height>0,
                            rect:[Math.round(r.x),Math.round(r.y),Math.round(r.width),Math.round(r.height)],
                            hit:document.elementFromPoint(x,y)===e || e.contains(document.elementFromPoint(x,y)),
                            ancestors:Array.from((function*(n){for(let i=0;n&&i<6;i++,n=n.parentElement)yield n;})(e.parentElement)).map(n=>({tag:n.tagName,cls:n.getAttribute('class')}))};
                        }).filter(x=>x.visible).slice(0,40))").WaitAsync(TimeSpan.FromSeconds(5), token);
                        Log("AI SITE POPUP ELEMENTS " + snapshot);
                    }
                    else
                    {
                        ctx.LandingDispatcher = new SMAd.LandingPolicy.LandingPageStrategyDispatcher(
                            new[] { new SMAd.LandingPolicy.AiSiteLandingPageStrategy(worker) });
                        stage = "production AI site strategy";
                        await (Task<FlowControl>)Invoke(worker, "HandleLandingPageAsync", ctx, token)!;
                        Log("AI site production strategy returned");
                    }
                }
                if (args.Contains("--touch-timing"))
                {
                    stage = "local touch timing";
                    await VerifyTouchTimingAsync(page, cdp, ctx.human, token, Log);
                }
                if (args.Contains("--ai-site-no-result"))
                {
                    stage = "AI site no-result visibility";
                    await VerifyAiSiteNoResultAsync(page, token, Log);
                }
                if (args.Contains("--ai-site-close-fixture"))
                {
                    stage = "AI site close fixture";
                    ctx.SetActivePage(page, cdp);
                    ctx.human.Engine.IsActiveBinding = (p, s) => ctx.ActivePage is { } active &&
                        ReferenceEquals(active.Page, p) && ReferenceEquals(active.Session, s);
                    await VerifyAiSiteCloseAsync(ctx, token, Log);
                }
                if (args.Contains("--entry-timeout-fixture"))
                {
                    stage = "partial document navigation";
                    await VerifyPartialDocumentAsync(page, token, Log);
                }
                if (args.Contains("--entry-search-ready-fixture"))
                {
                    stage = "search result early readiness fixture";
                    await VerifySearchResultReadinessAsync(page, token, Log);
                    stage = "entry scroll space fixture";
                    await VerifyEntryScrollSpaceAsync(page, token, Log);
                    stage = "search ad detection fixture";
                    await VerifySearchAdDetectionAsync(page, token, Log);
                }
                if (!args.Contains("--actual-only") && !args.Contains("--entry-readiness-live") && !args.Contains("--entry-timeout-fixture") && !args.Contains("--entry-search-ready-fixture") && !args.Contains("--touch-timing") && !args.Contains("--ai-site-no-result") && !args.Contains("--ai-site-close-fixture") && !args.Contains("--ai-site-live") && !args.Contains("--ai-site-inspect") && !args.Contains("--ai-site-close"))
                {
                stage = "local page navigation";
                const string html = "<meta name='viewport' content='width=device-width,initial-scale=1'><style>body{margin:0;height:2800px}button{width:220px;height:80px;margin:40px}</style><button id='tap' onclick='window.clicks++'>Touch test</button><button id='popup' style='position:absolute;top:1900px;left:0' onclick=\"window.open('about:blank')\">New page</button><script>window.clicks=0</script>";
                await page.GotoAsync("data:text/html;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html)), new() { Timeout = 15000 });
                stage = "tap visible target";
                if (!await ctx.human.TapAsync(page, cdp, page.Locator("#tap"), cancellationToken: token)) throw new Exception("Touch was not dispatched");
                stage = "read DOM after tap";
                await page.WaitForFunctionAsync("() => window.clicks === 1", null, new() { Timeout = 2000 }).WaitAsync(token);
                var clicks = await page.EvaluateAsync<int>("window.clicks").WaitAsync(TimeSpan.FromSeconds(5), token);
                if (clicks != 1) throw new Exception($"Touch did not trigger click: {clicks}");
                stage = "rapid page creation and closure";
                var browserCdp = await lease.Browser.NewBrowserCDPSessionAsync();
                try
                {
                    for (int i = 0; i < 12; i++)
                    {
                        var created = await browserCdp.SendAsync("Target.createTarget", new Dictionary<string, object> { ["url"] = "about:blank" }).WaitAsync(TimeSpan.FromSeconds(5), token);
                        await browserCdp.SendAsync("Target.closeTarget", new Dictionary<string, object> { ["targetId"] = created!.Value.GetProperty("targetId").GetString()! }).WaitAsync(TimeSpan.FromSeconds(5), token);
                    }
                }
                finally { await browserCdp.DetachAsync(); }
                if (ctx.Config.LinkedCts.IsCancellationRequested || lease.Token.IsCancellationRequested) throw new Exception("Closing a secondary page canceled the browser session");
                stage = "swipe and locate target";
                var movement = await ctx.human.MoveToTargetAsync(page, cdp, page.Locator("#popup"), cancellationToken: token);
                if (!movement.Ready) throw new Exception("Scroll did not reach target");
                stage = "tap popup and observe navigation";
                var executor = new PageActionExecutor(ctx, Log);
                var result = await executor.ExecuteAsync("Diagnostic popup", (b, ct) => ctx.human.TapAsync(b.Page, b.Session, b.Page.Locator("#popup"), cancellationToken: ct),
                    async (p, ct) => await (Task)Invoke(worker, "ActivatePageAsync", ctx, p, ct)!, token);
                if (!result.Succeeded) throw new Exception(result.Reason);
                }
                if (actualUrl.Length != 0)
                {
                    stage = "actual page navigation";
                    var entry = await EntryPageNavigator.NavigateAsync(ctx.Page!, actualUrl, settings.PageLoadingTimeout * 1000, token, Log);
                    if (!entry.Opened) throw new Exception(entry.Reason);
                    stage = "actual page touch input";
                    var inputs = ctx.Page.Locator("input[type=search], input[type=text], input[name=q], textarea");
                    var inputCount = await inputs.CountAsync().WaitAsync(TimeSpan.FromSeconds(5), token);
                    bool touchedInput = false;
                    for (int i = 0; i < Math.Min(inputCount, 8); i++)
                    {
                        var input = inputs.Nth(i);
                        if (!await input.IsVisibleAsync().WaitAsync(TimeSpan.FromSeconds(5), token)) continue;
                        touchedInput = await ctx.human.TapAsync(ctx.Page, ctx.CdpSession!, input, cancellationToken: token);
                        if (touchedInput)
                            touchedInput = await input.EvaluateAsync<bool>("el => document.activeElement === el").WaitAsync(TimeSpan.FromSeconds(5), token);
                        if (touchedInput) break;
                    }
                    Log($"Actual search page input touched={touchedInput}, candidates={inputCount}");
                    if (!touchedInput) throw new Exception("No search input received a touch");
                    stage = "actual page DOM after tap";
                    await ctx.Page.EvaluateAsync<string>("document.readyState").WaitAsync(TimeSpan.FromSeconds(5), token);
                    stage = "actual page touch scroll";
                    var actualSwipe = await ctx.human.SwipeByIntentAsync(ctx.Page!, ctx.CdpSession!, SwipeIntent.Reading, token).WaitAsync(TimeSpan.FromSeconds(15), token);
                    Log($"Actual page swipe verified={actualSwipe?.ScrollChanged == true}");
                    if (actualSwipe?.ScrollChanged != true) throw new Exception("Actual page scroll was not verified");
                    stage = "actual page DOM after scroll";
                    await ctx.Page.EvaluateAsync<string>("document.readyState").WaitAsync(TimeSpan.FromSeconds(5), token);
                }
                stage = "completed";
            }
            catch (Exception ex) { failures++; outcome = "failed"; error = ex.ToString(); Log($"ROUND {round + 1} FAIL stage={stage}: {ex.Message}"); }
            finally
            {
                if (lease != null)
                {
                    try { exitedBeforeCleanup = lease.ProcessSession.Process.HasExited; } catch { }
                    connectedBeforeCleanup = lease.Browser.IsConnected;
                    cancellation.Cancel();
                    lease.RequestStop(outcome == "success" ? BrowserStopKind.Completed : BrowserStopKind.ExecutionFailed, $"Diagnostic ended at {stage}");
                    await lease.DisposeAsync();
                }
                reports.Add(new { Round = round + 1, Model = dev.Value<string>("model"), Stage = stage, Outcome = outcome,
                    ElapsedMs = clock.ElapsedMilliseconds, Error = error, ExitedBeforeCleanup = exitedBeforeCleanup,
                    ConnectedBeforeCleanup = connectedBeforeCleanup, Reclaim = lease?.ReclaimResult is { } r ? new
                    { r.Reason, r.ExitConfirmed, r.ProcessResult?.Forced, r.ProcessResult?.ExitCode, r.ProcessResult?.DiagnosticTail } : null });
                Log($"ROUND {round + 1}: {outcome}, stage={stage}, exitedBeforeCleanup={exitedBeforeCleanup}, connectedBeforeCleanup={connectedBeforeCleanup}");
                await File.WriteAllTextAsync(Path.Combine(reportRoot, "report.json"), System.Text.Json.JsonSerializer.Serialize(new
                { Executable = executable, settings.KernelVersion, settings.IsHiddenMode, settings.Incognito, Proxy = false, ActualUrl = actualUrl, Reports = reports }, new JsonSerializerOptions { WriteIndented = true }));
                await File.WriteAllLinesAsync(Path.Combine(reportRoot, "trace.log"), logs);
            }
        }
        Console.WriteLine("DIAGNOSTIC REPORT " + Path.Combine(reportRoot, "report.json"));
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    private static object? Invoke(object instance, string name, params object?[] args)
    {
        try { return instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(instance, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static async Task VerifyPartialDocumentAsync(IPage page, CancellationToken token, Action<string> log)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(token);
            await using var stream = client.GetStream();
            var requestBuffer = new byte[4096];
            await stream.ReadAsync(requestBuffer, token);
            var bytes = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: 100000\r\nConnection: close\r\n\r\n<!doctype html><html><body><h1>Visible partial page</h1>");
            await stream.WriteAsync(bytes, token);
            await stream.FlushAsync(token);
            await release.Task.WaitAsync(token);
        }, token);
        try
        {
            var result = await EntryPageNavigator.NavigateAsync(page, $"http://127.0.0.1:{port}/partial", 2000, token, log);
            if (!result.Opened || result.HttpStatus != 200 || result.Reason?.Contains("timed out") != true)
                throw new Exception($"Partial document was not accepted after timeout: {result.Reason}");
            log("Partial document accepted after navigation timeout with verified visible content");
        }
        finally
        {
            release.TrySetResult();
            listener.Stop();
            await server;
        }
    }

    private static async Task VerifySearchResultReadinessAsync(IPage page, CancellationToken token, Action<string> log)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("https://wm.m.sm.cn/**", async route =>
        {
            if (route.Request.Url.Contains("blocked.js", StringComparison.Ordinal))
            {
                await release.Task.WaitAsync(token);
                await route.FulfillAsync(new() { Status = 200, ContentType = "text/javascript", Body = "" });
                return;
            }
            var url = route.Request.Url;
            var style = url.Contains("hidden=1", StringComparison.Ordinal) ? "display:none" :
                url.Contains("opacity=1", StringComparison.Ordinal) ? "opacity:0" :
                url.Contains("clipped=1", StringComparison.Ordinal) ? "height:1px;overflow:hidden" : "";
            var margin = url.Contains("clipped=1", StringComparison.Ordinal) ? "margin-top:100px" : "";
            var markup = $"<meta name='viewport' content='width=device-width,initial-scale=1'><body><div style='{style}'><div style='{margin}' ad_dot_url='https://example.test/'><a data-url='https://example.test/'>Visible result</a></div></div><script src='/blocked.js'></script></body>";
            await route.FulfillAsync(new() { Status = url.Contains("error=1", StringComparison.Ordinal) ? 503 : 200, ContentType = "text/html; charset=utf-8", Body = markup });
        });
        try
        {
            var clock = Stopwatch.StartNew();
            var visible = await EntryPageNavigator.NavigateAsync(page, "https://wm.m.sm.cn/s?q=fixture", 10000, token, log);
            if (!visible.Opened || visible.Reason != "Visible search result ready" || clock.Elapsed >= TimeSpan.FromSeconds(8))
                throw new Exception($"Visible result did not become ready before DOMContentLoaded: {visible.Reason}, elapsed={clock.ElapsedMilliseconds}");
            log($"Visible search ad accepted early: elapsedMs={clock.ElapsedMilliseconds}");
            release.TrySetResult();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 5000 }).WaitAsync(token);
            foreach (var variant in new[] { "hidden", "opacity", "clipped", "error" })
            {
                release = new(TaskCreationOptions.RunContinuationsAsynchronously);
                clock.Restart();
                var result = await EntryPageNavigator.NavigateAsync(page, $"https://wm.m.sm.cn/s?{variant}=1", 1800, token, log);
                if (result.Reason == "Visible search result ready" || clock.Elapsed < TimeSpan.FromMilliseconds(1400) ||
                    (variant == "error" && (result.Opened || result.HttpStatus != 503)))
                    throw new Exception($"{variant} incorrectly accepted as an early result");
                log($"{variant} search ad did not trigger early readiness: elapsedMs={clock.ElapsedMilliseconds}");
                release.TrySetResult();
            }
            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var diagnosticLines = new List<string>();
            var delayed = await EntryPageNavigator.NavigateAsync(page, "https://wm.m.sm.cn/s?hidden=1", 5500, token,
                message => { diagnosticLines.Add(message); log(message); });
            if (delayed.Opened || !diagnosticLines.Any(x => x.Contains("Entry wait:") && x.Contains("probe=ok") && x.Contains("candidates=1") && x.Contains("visible=0")) ||
                !diagnosticLines.Any(x => x.Contains("Entry attempt end:") && x.Contains("responseMs=") && x.Contains("commitMs=") && x.Contains("domContentLoadedMs=-1")))
                throw new Exception("Delayed entry diagnostic did not preserve response and invisible-ad evidence");
            release.TrySetResult();
        }
        finally
        {
            release.TrySetResult();
            await page.UnrouteAsync("https://wm.m.sm.cn/**");
        }
    }

    private static async Task VerifyEntryScrollSpaceAsync(IPage page, CancellationToken token, Action<string> log)
    {
        foreach (var (name, html, expected) in new (string, string, bool?)[]
        {
            ("short", "<body><p>Short result</p></body>", false),
            ("long", "<body style='margin:0'><div style='height:5000px'>Long result</div></body>", true),
            ("nested", "<body><div style='height:100px;overflow:auto'><div style='height:600px'>Results</div></div></body>", true),
            ("hidden", "<body><div style='display:none;height:100px;overflow:auto'><div style='height:600px'></div></div></body>", false),
            ("iframe", "<body><iframe></iframe></body>", null)
        })
        {
            await page.SetContentAsync(html).WaitAsync(token);
            var actual = await EntryScrollProbe.HasScrollSpaceAsync(page, token);
            if (actual != expected) throw new Exception($"Entry scroll fixture {name}: expected={expected}, actual={actual}");
            log($"Entry scroll fixture {name}: scrollSpace={actual?.ToString() ?? "unknown"}");
        }
    }

    private static async Task VerifySearchAdDetectionAsync(IPage page, CancellationToken token, Action<string> log)
    {
        async Task Check(string name, string html, bool previouslyObserved, SearchAdDetectionStatus expected, int count = 0)
        {
            await page.SetContentAsync(html).WaitAsync(token);
            var result = await SearchAdMarkerDetector.DetectAsync(page, previouslyObserved, () => true, token);
            if (result.Status != expected || result.Count != count)
                throw new Exception($"Ad detection fixture {name}: expected={expected}/{count}, actual={result.Status}/{result.Count}, reason={result.Reason}");
            log($"Ad detection fixture {name}: status={result.Status}, count={result.Count}, reason={result.Reason}");
        }
        await Check("known markers", "<body><div ad_dot_url='https://example.test/'>One</div><div ad_dot_url='https://example.test/'>Two</div></body>", false, SearchAdDetectionStatus.Found, 2);
        await Check("no ad", "<body><p>Ordinary result</p></body>", false, SearchAdDetectionStatus.ConfirmedAbsent);
        await Check("hidden label", "<body><span style='display:none'>广告</span></body>", false, SearchAdDetectionStatus.ConfirmedAbsent);
        await Check("unrecognised visible label", "<body><div>Result <span>广告</span></div></body>", false, SearchAdDetectionStatus.Uncertain);
        await Check("previous marker disappeared", "<body><p>Ordinary result</p></body>", true, SearchAdDetectionStatus.Uncertain);
        await Check("delayed marker", "<body><span>广告</span><script>setTimeout(() => { const el = document.createElement('div'); el.setAttribute('ad_dot_url', 'https://example.test/'); document.body.appendChild(el); }, 200)</script></body>", false, SearchAdDetectionStatus.Found, 1);
        var changed = await SearchAdMarkerDetector.DetectAsync(page, false, () => false, token);
        if (changed.Status != SearchAdDetectionStatus.Uncertain) throw new Exception("Changed page binding was treated as an ad result");
        log("Ad detection fixture changed binding: uncertain");
    }

    private static async Task VerifyTouchTimingAsync(IPage page, ICDPSession cdp, HumanTouchOperator human,
        CancellationToken token, Action<string> log)
    {
        const string html = "<meta name='viewport' content='width=device-width,initial-scale=1'><style>body{margin:0;height:5000px}a{display:inline-block;width:100px;height:100px;margin:10px;background:#ddd}</style><a id='probe1' href='#tap1'>One</a><a id='probe2' href='#tap2'>Two</a><a id='probe3' href='#tap3'>Three</a>";
        await page.GotoAsync("data:text/html;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html)), new() { Timeout = 15000 });
        for (int i = 1; i <= 3; i++)
        {
            if (!await human.TapAsync(page, cdp, page.Locator("#probe" + i), cancellationToken: token))
                throw new Exception($"Tap {i} was not dispatched");
            var clock = Stopwatch.StartNew();
            while (!page.Url.EndsWith("#tap" + i, StringComparison.Ordinal) && clock.Elapsed < TimeSpan.FromSeconds(5))
                await Task.Delay(50, token);
            if (!page.Url.EndsWith("#tap" + i, StringComparison.Ordinal)) throw new Exception($"Tap {i} did not navigate to its fragment");
        }
        for (int i = 0; i < 3; i++)
        {
            var swipe = await human.SwipeByIntentAsync(page, cdp, SwipeIntent.Reading, token);
            if (swipe?.ScrollChanged != true) throw new Exception($"Swipe {i + 1} was not verified");
        }
        log("Touch timing verified: 3 click effects and 3 scroll effects");
    }

    private static async Task VerifyAiSiteNoResultAsync(IPage page, CancellationToken token, Action<string> log)
    {
        var method = typeof(SMAd.LandingPolicy.AiSiteLandingPageStrategy).GetMethod("FindVisibleAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        Task<ILocator?> Find(ILocator locator) => (Task<ILocator?>)method.Invoke(null, new object[] { locator, token })!;
        const string text = "抱歉，未能匹配到合适的课程";
        var html = $"<meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><div class='ad-card-title'>可见课程</div><div style='display:none'>{text}</div><div style='display:none'>{text}</div>";
        await page.GotoAsync("data:text/html;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html)), new() { Timeout = 15000 });
        var candidates = page.GetByText(text);
        if (await candidates.CountAsync() != 2 || await Find(candidates) != null)
            throw new Exception("Hidden no-result text was mistaken for visible content");
        await page.SetContentAsync($"<div style='display:none'>{text}</div><div>{text}</div>");
        var visible = await Find(page.GetByText(text));
        if (visible == null || !await visible.IsVisibleAsync()) throw new Exception("Visible no-result message was not found");
        await page.SetContentAsync("<div class='ad-card-title'>可见课程</div>");
        if (await Find(page.GetByText(text)) != null) throw new Exception("Absent no-result message was found");
        log("AI site visibility verified: hidden placeholders ignored; visible message detected; absent message ignored");
    }

    private static async Task VerifyAiSiteCloseAsync(WorkerRunContext ctx, CancellationToken token, Action<string> log)
    {
        var page = ctx.Page!;
        var method = typeof(SMAd.LandingPolicy.AiSiteLandingPageStrategy).GetMethod("ClosePopupAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        async Task<string> Close()
        {
            var task = (Task)method.Invoke(null, new object[] { ctx, token, log })!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task)!.ToString()!;
        }
        await page.RouteAsync("https://ai-popup-fixture.test/**", route => route.FulfillAsync(new()
        { Status = 200, ContentType = "text/html", Body = "<meta name='viewport' content='width=device-width,initial-scale=1'><body>fixture</body>" }));
        await page.GotoAsync("https://ai-popup-fixture.test/", new() { Timeout = 10000 });
        await page.SetContentAsync("<div class='gt-jmy-h5-dialog' style='display:none'><button class='close-btn'>hidden</button></div><button class='layui-layer-btn'>confirm</button>");
        if (await Close() != "NotPresent") throw new Exception("Hidden close or unrelated action mistaken for popup");
        await page.SetContentAsync("<style>.close-btn{width:100px;height:70px}.gt-jmy-h5-dialog{padding:40px}</style><div class='gt-jmy-h5-dialog' style='display:none'><button class='close-btn'>hidden</button></div><div class='gt-jmy-h5-dialog'><button class='close-btn' onclick=\"this.parentElement.style.display='none'\">close</button></div>");
        if (await Close() != "Closed") throw new Exception("Visible popup was not closed");
        await page.SetContentAsync("<style>.close-btn{width:100px;height:70px}.gt-jmy-h5-dialog{padding:40px}</style><div class='gt-jmy-h5-dialog'><button class='close-btn'>no effect</button></div>");
        if (await Close() != "NotConfirmed") throw new Exception("Popup with no closing effect was reported as closed");
        await page.SetContentAsync("<style>.close-icon{width:100px;height:70px}</style><div class='close-area'><button class='close-icon' onclick=\"this.style.display='none'\">close</button></div>");
        if (await Close() != "NotConfirmed") throw new Exception("Unknown popup container was treated as closed");
        log("AI site popup verified: hidden candidate ignored, visible dialog closed, unchanged or unknown container not confirmed");
    }
}
