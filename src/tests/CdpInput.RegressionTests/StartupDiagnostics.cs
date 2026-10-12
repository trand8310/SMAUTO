using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using PlaywrightHumanInput;
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
                stage = "local page navigation";
                const string html = "<meta name='viewport' content='width=device-width,initial-scale=1'><style>body{margin:0;height:2800px}button{width:220px;height:80px;margin:40px}</style><button id='tap' onclick='window.clicks++'>Touch test</button><button id='popup' style='position:absolute;top:1900px;left:0' onclick=\"window.open('about:blank')\">New page</button><script>window.clicks=0</script>";
                await page.GotoAsync("data:text/html;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html)), new() { Timeout = 15000 });
                stage = "tap visible target";
                if (!await ctx.human.TapAsync(page, cdp, page.Locator("#tap"), cancellationToken: token)) throw new Exception("Touch was not dispatched");
                stage = "read DOM after tap";
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
                if (actualUrl.Length != 0)
                {
                    stage = "actual page navigation";
                    var entry = await EntryPageNavigator.NavigateAsync(ctx.Page!, actualUrl, settings.PageLoadingTimeout * 1000, token, Log);
                    if (!entry.Opened) throw new Exception(entry.Reason);
                    stage = "actual page touch scroll";
                    await ctx.human.SwipeByIntentAsync(ctx.Page!, ctx.CdpSession!, SwipeIntent.Reading, token).WaitAsync(TimeSpan.FromSeconds(15), token);
                    stage = "actual page DOM after scroll";
                    await ctx.Page!.EvaluateAsync<string>("document.readyState").WaitAsync(TimeSpan.FromSeconds(5), token);
                    stage = "actual page touch input";
                    var inputs = ctx.Page.Locator("input[type=search], input[type=text], input[name=q], textarea");
                    var inputCount = await inputs.CountAsync().WaitAsync(TimeSpan.FromSeconds(5), token);
                    bool touchedInput = false;
                    for (int i = 0; i < Math.Min(inputCount, 8); i++)
                    {
                        var input = inputs.Nth(i);
                        if (!await input.IsVisibleAsync().WaitAsync(TimeSpan.FromSeconds(5), token)) continue;
                        touchedInput = await ctx.human.TapAsync(ctx.Page, ctx.CdpSession!, input, cancellationToken: token);
                        if (touchedInput) break;
                    }
                    Log($"Actual search page input touched={touchedInput}, candidates={inputCount}");
                    stage = "actual page DOM after tap";
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
}
