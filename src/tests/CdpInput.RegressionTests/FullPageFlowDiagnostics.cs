using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using QTP;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Plugins;

/// <summary>Runs production orchestration against controlled pages, with no external task/statistics service.</summary>
internal static class FullPageFlowDiagnostics
{
    public static async Task RunAsync(string[] args)
    {
        string Option(string key, string fallback) => args.FirstOrDefault(a => a.StartsWith(key + "="))?[(key.Length + 1)..] ?? fallback;
        var root = Path.GetFullPath(Option("--base", "Build/publish"));
        var settings = JObject.Parse(await File.ReadAllTextAsync(Path.Combine(root, "appsettings.user.json")))["AppSettings"]!.ToObject<AppSettings>()!;
        if (settings.HompageTrigger != 0) throw new InvalidOperationException("This fixture uses direct entry navigation; homepage-trigger fixtures are not implemented.");
        var rounds = Math.Clamp(int.Parse(Option("--rounds", "4")), 1, 12);
        var samples = File.ReadLines(Path.Combine(root, "Data", $"dev_{settings.UsingDevIndex}.log")).Take(1500).Select(JObject.Parse)
            .GroupBy(d => (d.Value<string>("make"), d.Value<string>("model"))).Select(g => g.First()).Take(12).ToArray();
        var reportRoot = Path.GetFullPath(Path.Combine("artifacts", "full-page-flow-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(reportRoot);
        var reports = new List<object>();
        int failures = 0;
        Console.WriteLine($"FULL FLOW kernel={settings.KernelVersion}, headless={settings.IsHiddenMode}, incognito={settings.Incognito}, TTL={settings.IpTtl}s, proxy=false");
        await using var provider = new PlaywrightProvider();
        await using var processes = new ChromiumSessionManager();
        var executable = Path.Combine(root, "File", "chrome-win", settings.KernelVersion, "chrome.exe");
        for (int round = 0; round < rounds; round++)
        {
            var fixture = new FlowFixture(round);
            var logs = new System.Collections.Concurrent.ConcurrentQueue<string>();
            void Log(string line) { logs.Enqueue(line); Console.WriteLine(line); }
            await using var stats = new TaskStatsAggregator(null!, settings, NullLogger<TaskStatsAggregator>.Instance);
            int taskId = -10000 - round;
            // Keep the real CTR decision while eliminating its external baseline request.
            var baseline = (IDictionary)typeof(TaskStatsAggregator).GetField("_taskGlobalBaseline", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stats)!;
            var key = typeof(TaskStatsAggregator).GetMethod("GetClickHourKey", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { taskId })!;
            baseline[key] = new TaskStats { DSP = 1 };
            baseline[new TaskStatsAggregator.TaskHourKey(taskId, DateTime.UtcNow.AddHours(9).ToString("yyyyMMddHH"))] = new TaskStats { DSP = 1 };
            var adapter = new ExecutableOverrideProcesses(processes, executable);
            await using var runtime = new BrowserRuntimeManager(provider, adapter,
                new(Math.Max(1, settings.MaximumConcurrency), Math.Max(1, settings.BrowserLaunchConcurrency)), async (pw, endpoint, token) =>
                {
                    // Use the production connector, then intercept all browser requests before any navigation.
                    var browser = await (Task<IBrowser>)typeof(BrowserRuntimeManager).GetMethod("ConnectAsync", BindingFlags.Static | BindingFlags.NonPublic)!
                        .Invoke(null, new object[] { pw, endpoint, token })!;
                    await browser.Contexts[0].RouteAsync("**/*", fixture.RouteAsync);
                    return browser;
                });
            var worker = new SMAdTask(provider, stats, processes, null!, null!, settings, runtime);
            worker.OnLogEventHandler += (_, e) => Log(e.Message);
            var stateEvents = new List<string>();
            worker.OnStateChangedEventHandler += (_, e) => { lock (stateEvents) stateEvents.Add(e.Type.ToString()); };
            var dev = (JObject)samples[round % samples.Length].DeepClone();
            if (dev["storage"]?.Type == JTokenType.String) dev["storage"] = long.Parse(dev.Value<string>("storage")!.Split(',')[0].Trim());
            var ua = dev.Value<string>("ua") ?? "";
            var version = Regex.Match(ua, @"Chrome/([\d.]+)");
            dev["full_version"] = version.Success ? version.Groups[1].Value : settings.KernelVersion;
            var model = Regex.Match(ua, @"(?<=Android\s+)([\d.]+);([\S\s]+)(?=Build)");
            if (model.Success) { dev["osv"] = model.Groups[1].Value.Trim(); dev["model"] = Regex.Replace(model.Groups[2].Value.Trim(), "^.*?;\\s*", ""); }
            var taskArgs = new JObject
            {
                ["task"] = new JObject { ["id"] = taskId, ["url"] = fixture.EntryUrl, ["click_rate"] = 100, ["sleep"] = "8-12" },
                ["dev"] = dev, ["os"] = 1, ["kernelVersion"] = settings.KernelVersion,
                ["cacheName"] = "full-flow-test", ["processIndex"] = 0, ["totalPV"] = 1,
                ["isProxyMode"] = false, ["isHiddenMode"] = settings.IsHiddenMode, ["incognito"] = settings.Incognito,
                ["isTest"] = false, ["pageLoadingTimeout"] = settings.PageLoadingTimeout, ["pageloadedDelay"] = settings.PageloadedDelay,
                ["pvsTriggerOne"] = settings.PVsTriggerOne, ["protocol"] = settings.Protocol, ["scaleX"] = 1, ["scaleY"] = 1
            };
            var timer = Stopwatch.StartNew();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(settings.IpTtl));
            var result = await worker.ExecuteWorkerWithResultAsync("full-flow-" + Guid.NewGuid().ToString("N"), taskArgs, deadline.Token);
            var actions = result.ActionLogPath != null && File.Exists(result.ActionLogPath)
                ? File.ReadLines(result.ActionLogPath).Select(line => System.Text.Json.JsonSerializer.Deserialize<SMAd.PageActions.PageActionRecord>(line)!).ToArray()
                : Array.Empty<SMAd.PageActions.PageActionRecord>();
            var first = actions.FirstOrDefault(a => a.Intent == "Navigate" && a.Succeeded && a.AfterUrl.StartsWith(fixture.LandingUrl));
            var second = actions.FirstOrDefault(a => a.Intent == "Navigate" && a.Succeeded && a.AfterUrl.StartsWith(fixture.DetailUrl));
            var completedStay = logs.Any(line => line == "动作完成");
            bool fullSuccess = result.IsSuccess && result.CompletedPvs == 1 && result.CompletedClickPvs == 1 &&
                fixture.EntryHits > 0 && fixture.LandingHits > 0 && fixture.DetailHits > 0 && first != null && second != null && completedStay &&
                result.BrowserReclaim?.ExitConfirmed == true && result.BrowserStopKind == BrowserStopKind.Completed;
            if (!fullSuccess) failures++;
            reports.Add(new { Round = round + 1, Site = fixture.Site, fixture.FirstPopup, fixture.SecondPopup,
                Outcome = fullSuccess ? "complete" : "incomplete", ElapsedMs = timer.ElapsedMilliseconds,
                fixture.EntryHits, fixture.LandingHits, fixture.DetailHits, FirstJump = first, SecondJump = second,
                StayCompleted = completedStay, result.Status, result.FailureReason, result.CompletedPvs, result.CompletedClickPvs,
                result.BrowserStopKind, Reclaim = result.BrowserReclaim is { } reclaim ? new
                { reclaim.ExitConfirmed, reclaim.ProcessResult?.Forced, reclaim.ProcessResult?.ExitCode, reclaim.ProcessResult?.DiagnosticTail } : null,
                StateEvents = stateEvents.ToArray(), Actions = actions });
            await File.WriteAllLinesAsync(Path.Combine(reportRoot, $"round-{round + 1}.log"), logs);
            await File.WriteAllTextAsync(Path.Combine(reportRoot, "report.json"), System.Text.Json.JsonSerializer.Serialize(new
            { settings.KernelVersion, settings.IsHiddenMode, settings.Incognito, Proxy = false, Fixture = "All browser requests intercepted; no ad billing or external statistics", Reports = reports }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"FULL FLOW ROUND {round + 1}: {(fullSuccess ? "complete" : "incomplete")}, site={fixture.Site}, first={first != null}, second={second != null}, stay={completedStay}, result={result.Status}");
        }
        Console.WriteLine("FULL FLOW REPORT " + Path.Combine(reportRoot, "report.json"));
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    private sealed class ExecutableOverrideProcesses(ChromiumSessionManager inner, string executable) : IBrowserProcessManager
    {
        public Task<ChromiumSession> StartChromium(string id, string ignoredExecutable, string userDir, TimeSpan ttl,
            string arguments = "--incognito", string? proxyServer = null, TimeSpan? readyTimeout = null, CancellationToken token = default) =>
            inner.StartChromium(id, executable, userDir, ttl, arguments + " --no-proxy-server", proxyServer, readyTimeout, token);
        public Task CloseAsync(string id) => inner.CloseAsync(id);
        public bool TryGetSession(string id, out ChromiumSession? session) => inner.TryGetSession(id, out session);
    }

    private sealed class FlowFixture
    {
        public FlowFixture(int round) { Site = round % 2 == 0 ? "BaiduB2B" : "1688"; FirstPopup = round % 2 == 0; SecondPopup = !FirstPopup; }
        public string Site { get; }
        public bool FirstPopup { get; }
        public bool SecondPopup { get; }
        public string EntryUrl => "https://search.fixture.invalid/entry";
        public string LandingUrl => Site == "BaiduB2B" ? "https://b2b.baidu.com/m/aitf/list" : "https://m.1688.com/list";
        public string DetailUrl => "https://detail.fixture.invalid/item";
        public int EntryHits, LandingHits, DetailHits;
        public async Task RouteAsync(IRoute route)
        {
            var url = route.Request.Url;
            string? html = null;
            if (route.Request.IsNavigationRequest)
            {
                if (url.StartsWith(EntryUrl))
                {
                    Interlocked.Increment(ref EntryHits);
                    html = $"<div style='height:1100px'>本地完整流程：入口阅读区</div><div ad_dot_url='{LandingUrl}'><a class='c-title' data-url='{LandingUrl}' href='{LandingUrl}' {(FirstPopup ? "target='_blank'" : "")}>本地测试目标，点击进入落地页</a><span>广告（本地夹具）</span></div><div style='height:2400px'>入口尾部阅读区</div>";
                }
                else if (url.StartsWith(LandingUrl))
                {
                    Interlocked.Increment(ref LandingHits);
                    html = "<header>本地落地页：浏览后随机二跳</header>" + string.Join("", Enumerable.Range(0, 35).Select(i =>
                    {
                        var link = $"<a class='product-item-link list-title' href='{DetailUrl}?id={i}' {(SecondPopup ? "target='_blank'" : "")}>测试项目 {i}：点击进入详情</a>";
                        return Site == "BaiduB2B" ? link : $"<div class='offer-item'>{link}</div>";
                    }));
                }
                else if (url.StartsWith(DetailUrl))
                {
                    Interlocked.Increment(ref DetailHits);
                    html = "<header>本地详情页：二跳已完成</header>" + string.Join("", Enumerable.Range(0, 40).Select(i => $"<p style='height:120px'>详情阅读内容 {i}</p>"));
                }
            }
            if (html == null) { await route.FulfillAsync(new() { Status = 204, Body = "" }); return; }
            await route.FulfillAsync(new() { Status = 200, ContentType = "text/html;charset=utf-8", Body = "<meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><style>body{margin:0;padding:12px;font:18px sans-serif}a{display:block;box-sizing:border-box;padding:32px 16px;margin:12px 0;min-height:140px;background:#eef;color:#123;border:1px solid #abc}header{height:56px}span{font-size:14px}</style>" + html });
        }
    }
}
