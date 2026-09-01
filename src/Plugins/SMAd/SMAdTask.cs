using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlaywrightHumanInput;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using QTP.Common.Win32;
using SMAd;
using SMAd.HumanInput;
using SMAd.LandingPolicy;
using SMAd.Models;
using SMAd.PlaywrightHumanInput;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace QTP.Plugins
{
    public sealed class SMAdTask : QTPServiceBase
    {


        private const string AliAppDownloadModalCloseSelector = ".androidOpenModal .closeBtn, .iosOpenModal .closeIcon";

        private static readonly object CdpFinalFailureLock = new();
        private static int CdpFinalFailureCount;
        private static bool CdpFinalFailureRestartRequested;
        private const int CdpFinalFailureRestartThreshold = 10;

        public async Task<bool> IsElementPartiallyVisibleAsync(
        ILocator locator,
        double minVisibleHeight = 24)
        {
            try
            {
                if (locator == null)
                    return false;

                if (await locator.CountAsync() <= 0)
                    return false;

                if (!await locator.First.IsVisibleAsync())
                    return false;

                return await locator.First.EvaluateAsync<bool>(
                    @"(element, minVisibleHeight) => {
                        const rect = element.getBoundingClientRect();
                        const vh = window.innerHeight || document.documentElement.clientHeight || 0;
                        const vw = window.innerWidth || document.documentElement.clientWidth || 0;

                        if (!rect || rect.width <= 0 || rect.height <= 0)
                            return false;

                        const visibleTop = Math.max(rect.top, 0);
                        const visibleBottom = Math.min(rect.bottom, vh);
                        const visibleLeft = Math.max(rect.left, 0);
                        const visibleRight = Math.min(rect.right, vw);

                        const visibleHeight = visibleBottom - visibleTop;
                        const visibleWidth = visibleRight - visibleLeft;

                        return visibleHeight >= minVisibleHeight && visibleWidth > 20;
                    }",
                    minVisibleHeight);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsClosedPlaywrightException(PlaywrightException ex)
        {
            if (string.IsNullOrWhiteSpace(ex.Message))
                return false;

            return ex.Message.Contains("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("has been closed", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("Target closed", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("browser has been closed", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("context has been closed", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("page has been closed", StringComparison.OrdinalIgnoreCase);
        }

        public static uint GetStableHash(string s)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in s)
                {
                    hash = (hash ^ c) * 16777619;
                }
                return hash;
            }
        }
        public static QTPPlugin GetInfo()
        {
            return new QTPPlugin()
            {
                ClassName = "QTP.Plugins.SMAdTask",
                Name = "SMAd",
                FileName = "SMAd.dll",
            };
        }
        public override string Title => "神马搜索";
        private readonly TaskStatsAggregator _aggregator;
        private readonly AdeHelper _adeHelper;
        private ChromiumSessionManager _processManager;
        private ChineseNameGenerator _nameGenerator;
        private readonly IPlaywrightProvider _playwrightProvider;
        public SMAdTask(
            IPlaywrightProvider playwrightProvider,
            TaskStatsAggregator aggregator, ChromiumSessionManager manager, AdeHelper adeHelper, ChineseNameGenerator nameGenerator, AppSettings appSettings) : base(appSettings)
        {
            _playwrightProvider = playwrightProvider;
            _aggregator = aggregator;
            _processManager = manager;
            _adeHelper = adeHelper;
            _nameGenerator = nameGenerator;
        }

        public static Task<bool> IsPageTop(IPage page)
        {
            return page.EvaluateAsync<bool>("window.pageYOffset == 0;");
        }
        public static Task<bool> IsPageEnd(IPage page)
        {
            return page.EvaluateAsync<bool>("(window.innerHeight + window.pageYOffset) >= document.body.offsetHeight || Math.abs((window.innerHeight + window.pageYOffset) - document.body.offsetHeight) < 10;");
        }

        public async Task BrowseForAsync(WorkerRunContext ctx, int minSeconds = 3, int maxSeconds = 8, CancellationToken token = default)
        {
            await ctx.Human.BrowseForAsync(
                ctx.Page!,
                ctx.CdpSession!,
                duration: TimeSpan.FromSeconds(CommonHelper.RandomRange(minSeconds, maxSeconds)),
                cancellationToken: token);
        }

        public async Task BrowseForAsync(WorkerRunContext ctx, TimeSpan duration, CancellationToken token = default)
        {
            await ctx.Human.BrowseForAsync(
                ctx.Page!,
                ctx.CdpSession!,
                duration: duration,
                cancellationToken: token);
        }

        public static async Task<bool> IsElementInViewportAsync(ILocator locator)
        {
            if (!await locator.IsVisibleAsync())
            {
                return false;
            }
            return await locator.EvaluateAsync<bool>(@"(element) => {
            const rect = element.getBoundingClientRect();
            return (
              rect.top >= 0 &&
              rect.left >= 0 &&
              rect.bottom <= (window.innerHeight || document.documentElement.clientHeight) &&
              rect.right <= (window.innerWidth || document.documentElement.clientWidth));
             }");
        }

        public static async Task<List<ILocator>> GetVisibleElementsAsync(ILocator locator)
        {
            var result = new List<ILocator>();

            int count = await locator.CountAsync();
            if (count == 0)
                return result;
            for (int i = 0; i < count; i++)
            {
                var el = locator.Nth(i);
                if (await IsElementInViewportAsync(el))
                {
                    result.Add(el);
                }
            }
            return result;
        }

        /// <summary>
        /// 下滑前先检查是否接近顶部
        /// </summary>
        /// <param name="page"></param>
        /// <returns></returns>
        private async Task<double> GetVerticalScrollTopAsync(IPage page)
        {
            try
            {
                return await page.EvaluateAsync<double>(
                    @"() => {
                const se = document.scrollingElement || document.documentElement || document.body;
                return se ? (se.scrollTop || 0) : 0;
            }");
            }
            catch
            {
                return 0;
            }
        }
        /// <summary>
        /// 下滑前先检查是否接近顶部
        /// </summary>
        /// <param name="page"></param>
        /// <param name="threshold"></param>
        /// <returns></returns>

        private async Task<bool> IsNearTopAsync(IPage page, double threshold = 8)
        {
            try
            {
                double top = await GetVerticalScrollTopAsync(page);
                return top <= threshold;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// 处理页面元素
        /// </summary>
        /// <param name="page"></param>
        /// <param name="cdpSession"></param>
        /// <param name="token"></param>

        public void ProcessingPageElementTask(WorkerRunContext ctx, CancellationToken token)
        {
            if (Interlocked.Exchange(ref ctx.PageElementGuardStarted, 1) == 1)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                            var page = ctx.Page;
                            var cdpSession = ctx.CdpSession;
                            if (page == null || cdpSession == null)
                                continue;

                            if (page.IsClosed)
                                break;

                            var closeBtn = page.Locator(AliAppDownloadModalCloseSelector);
                            var closeBtnCount = await closeBtn.CountAsync();
                            if (closeBtnCount <= 0)
                                continue;

                            for (var index = 0; index < closeBtnCount; index++)
                            {
                                if (token.IsCancellationRequested || page.IsClosed)
                                    break;

                                var target = closeBtn.Nth(index);
                                if (!await target.IsVisibleAsync())
                                    continue;

                                await CDPHelper.MouseClickAsync(page, cdpSession, target);
                                LogWriteLine($"{this.Title}:ProcessingPageElementTask 已关闭1688弹框");
                                await Task.Delay(CommonHelper.RandomRange(300, 600), token);
                                break;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (PlaywrightException ex) when (IsClosedPlaywrightException(ex))
                        {
                            LogWriteLine($"{this.Title}:ProcessingPageElementTask 页面已关闭: {ex.Message}");
                            break;
                        }
                        catch (Exception ex)
                        {
                            LogWriteLine($"{this.Title}:ProcessingPageElementTask异常: {ex.Message}");
                            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref ctx.PageElementGuardStarted, 0);
                }
            }, token);
        }

        /// <summary>
        /// 清除1688APP下载
        /// </summary>
        /// <param name="page"></param>
        /// <param name="cdpSession"></param>
        /// <returns></returns>
        private async Task ClearPageCloseBtn(WorkerRunContext ctx, CancellationToken token)
        {
            try
            {
                var page = ctx.Page;
                if (page == null || page.IsClosed || ctx.CdpSession == null)
                    return;

                //                var closeBtn = ctx.Page!.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new");
                var closeBtn = page.Locator(".androidOpenModal .closeBtn, .iosOpenModal .closeIcon");
                if (await closeBtn.CountAsync() > 0)
                {
                    var target = closeBtn.First;
                    if (await target.IsVisibleAsync())
                    {
                        await ctx.Human.ClickAsync(page, ctx.CdpSession, target, token);
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        /// <summary>
        /// 清除1688询价对话框
        /// </summary>
        /// <param name="page"></param>
        /// <param name="cdpSession"></param>
        /// <returns></returns>
        private async Task ClearSuccessTipNewCloseNew(WorkerRunContext ctx, CancellationToken token)
        {
            try
            {
                var page = ctx.Page;
                if (page == null || page.IsClosed || ctx.CdpSession == null)
                    return;

                var closeBtn = page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new,.androidOpenModal .closeBtn, .iosOpenModal .closeIcon");
                if (await closeBtn.CountAsync() > 0)
                {
                    var target = closeBtn.First;
                    if (await target.IsVisibleAsync())
                    {
                        await ctx.Human.ClickAsync(page, ctx.CdpSession, target, token);
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        private static List<string> InitFPArgs(TaskConfig config)
        {
            JToken taskArgs = config.TaskArgs;
            var result = new List<string>();

            uint hash_code = (uint)Math.Abs($"{taskArgs.ToString()}".GetHashCode()) % 1048560;
            uint fingerprint = hash_code;
            config.Fingerprint = (int)fingerprint;

            var uaFullVersion = taskArgs.SelectToken("dev.uaFullVersion")?.Value<string>() ?? "135.0.7049.119";


            var useragent = taskArgs.SelectToken("dev.ua").Value<string>();
            var os = taskArgs.SelectToken("os").Value<int>();
            result.Add("--platform=Windows");
            result.Add($"--screen-color-depth={taskArgs.SelectToken("dev.screen.colorDepth")?.Value<int>() ?? 24}");
            result.Add($"--platform-version={taskArgs.SelectToken("dev.platformVersion")?.Value<string>() ?? ""}");
            result.Add($"--full-version={taskArgs.SelectToken("dev.uaFullVersion")?.Value<string>() ?? ""}");
            if (taskArgs.SelectToken("dev.brands") == null || taskArgs.SelectToken("dev.brands").Count() == 0)
            {
                result.Add($"--disable-brand-version-list");
            }
            else
            {
                //result.Add($"--brand-version-list=\"{JsonConvert.SerializeObject(taskArgs.SelectToken("dev.brands"), Formatting.None)}\"");

                string json = JsonConvert.SerializeObject(
                taskArgs.SelectToken("dev.brands"),
                Formatting.None);
                json = json.Replace("\"", "\\\"");
                result.Add($"--brand-version-list=\"{json}\"");
            }

            if (taskArgs.SelectToken("dev.fullVersionList") == null || taskArgs.SelectToken("dev.fullVersionList").Count() == 0)
            {
                result.Add($"--disable-full-version-list");
            }
            else
            {
                string json = JsonConvert.SerializeObject(
                taskArgs.SelectToken("dev.fullVersionList"),
                Formatting.None);
                json = json.Replace("\"", "\\\"");
                result.Add($"--full-version-list=\"{json}\"");
            }
            result.Add($"--fingerprint={fingerprint}");
            var grease_cipher = Math.Abs(string.Join(".", uaFullVersion.Split('.').Take(2)).GetHashCode()) % 65535;
            result.Add($"--ssl-grease-cipher={grease_cipher}");
            result.Add($"--force-webrtc-ip-handling-policy");
            var isProxyMode = taskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            if (isProxyMode)
            {
                var realIp = taskArgs.SelectToken("realIp")?.Value<string>() ?? taskArgs.SelectToken("ipInfo.query")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(realIp))
                {
                    result.Add($"--webrtc-ip={realIp}");
                    if (new bool[] { false, false, true, false, false, true, false, false, true, false }[CommonHelper.RandomRange(0, 10)])
                    {
                        result.Add($"--webrtc-ip-handling-policy=disable_non_proxied_udp");
                    }
                    else
                    {
                        result.Add($"--webrtc-ip-handling-policy=default_public_interface_only");
                    }
                }
                else
                {
                    result.Add($"--webrtc-ip-handling-policy=disable_non_proxied_udp");
                }
            }
            else
            {
                result.Add($"--webrtc-ip-handling-policy=disable_non_proxied_udp");
            }


            result.Add($"--hardware-concurrency={(taskArgs.SelectToken("dev.cpu")?.Value<int>() ?? 8)}");
            result.Add($"--device-memory={(taskArgs.SelectToken("dev.ram")?.Value<int>() ?? 8)}");
            var js_memory_info = new string[] { "10000000|10000000|1136000000", "29400000|31200000|1130000000", "10000000|10000000|1136000000", "29400000|31200000|1130000000", "29400000|31200000|1130000000" };
            result.Add($"--js-memory-info={js_memory_info[(hash_code % 4)]}");
            var storages = new int[] { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 };
            long storage = (long)storages[(hash_code % 10)] * 1024 * 1024 * 1024;
            var usage_storage = (long)Math.Ceiling(storage * (CommonHelper.RandomRange(30, 80) * 0.01));
            result.Add($"--storage-quota=0|{(storage - usage_storage)}");
            result.Add("--enable-rects-noise");
            result.Add("--enable-canvas-noise");
            result.Add("--enable-image-noise");
            result.Add("--enable-text-noise");
            result.Add("--enable-font-noise");
            result.Add("--enable-audio-noise");
            return result;
        }


        public async Task CloseBrowserProcess(string uniqueId)
        {
            await _processManager.CloseAsync(uniqueId);
        }

        public async Task<bool> CanPageScrollAsync(IPage page)
        {
            if (page == null || page.IsClosed)
                return false;

            try
            {
                return await page.EvaluateAsync<bool>(@"() => {

                    const threshold = 5;

                    // ========= 1. 页面本身是否可滚动 =========
                    const doc = document.documentElement;
                    const body = document.body;

                    const pageScrollHeight = Math.max(
                        doc?.scrollHeight || 0,
                        body?.scrollHeight || 0
                    );

                    const pageClientHeight = Math.max(
                        doc?.clientHeight || 0,
                        window.innerHeight || 0
                    );

                    if (pageScrollHeight > pageClientHeight + threshold)
                        return true;


                    // ========= 2. 是否存在可滚动容器 =========
                    const elements = document.querySelectorAll('*');

                    for (const el of elements) {

                        if (!(el instanceof HTMLElement))
                            continue;

                        const style = window.getComputedStyle(el);

                        if (
                            (style.overflowY === 'auto' || style.overflowY === 'scroll') &&
                            el.scrollHeight > el.clientHeight + threshold
                        ) {
                            return true;
                        }
                    }

                    return false;
                }");
            }
            catch
            {
                return false;
            }
        }
        private sealed class PageScrollState
        {
            public double ScrollY { get; set; }
            public double ClientHeight { get; set; }
            public double ScrollHeight { get; set; }
            public bool CanScrollDown { get; set; }
        }

        private static async Task<PageScrollState> GetPageScrollStateAsync(IPage page)
        {
            try
            {
                return await page.EvaluateAsync<PageScrollState>(@"() => {
                    const doc = document.documentElement;
                    const body = document.body;

                    const scrollY = window.scrollY || window.pageYOffset || doc.scrollTop || body?.scrollTop || 0;
                    const clientHeight = window.innerHeight || doc.clientHeight || body?.clientHeight || 0;
                    const scrollHeight = Math.max(
                        doc.scrollHeight || 0,
                        body?.scrollHeight || 0,
                        doc.offsetHeight || 0,
                        body?.offsetHeight || 0,
                        doc.clientHeight || 0
                    );

                    // 留一点容差，避免小数误差导致明明到底了还继续滑
                    const canScrollDown = (scrollY + clientHeight) < (scrollHeight - 2);

                    return {
                        scrollY,
                        clientHeight,
                        scrollHeight,
                        canScrollDown
                    };
                }");
            }
            catch
            {
                return new PageScrollState
                {
                    ScrollY = 0,
                    ClientHeight = 0,
                    ScrollHeight = 0,
                    CanScrollDown = false
                };
            }
        }

        private void ResetCdpFinalFailureTracker(string traceTag)
        {
            lock (CdpFinalFailureLock)
            {
                if (CdpFinalFailureCount > 0)
                    LogWriteLine($"{traceTag} CDP最终失败统计已清零: count={CdpFinalFailureCount}");

                CdpFinalFailureCount = 0;

            }
        }

        private void HandleCdpFinalFailureForRestart(string traceTag, Exception lastException)
        {
            if (lastException.Message.Contains("no such file or directory", StringComparison.OrdinalIgnoreCase))
            {
                LogWriteLine($"{traceTag} CDP最终失败命中 no such file or directory，立即重启计算机。LastError={lastException}");
                SafeRestartHelper.ForceRestart(1);
                return;
            }

            bool shouldRestart = false;
            int failureCount;

            lock (CdpFinalFailureLock)
            {
                CdpFinalFailureCount++;
                failureCount = CdpFinalFailureCount;

                shouldRestart = !CdpFinalFailureRestartRequested
                    && failureCount > CdpFinalFailureRestartThreshold;

                if (shouldRestart)
                    CdpFinalFailureRestartRequested = true;
            }

            LogWriteLine($"{traceTag} CDP最终失败统计: count={failureCount}, threshold>{CdpFinalFailureRestartThreshold}, error={lastException.Message}");

            if (!shouldRestart)
                return;

            LogWriteLine($"{traceTag} CDP连接最终失败全局次数超过{CdpFinalFailureRestartThreshold}次，准备重启计算机。LastError={lastException}");
            SafeRestartHelper.ForceRestart(1);
        }

        private async Task<IBrowser?> ConnectOverCDPWithRetryAsync(
        IPlaywright playwright,
        string endpoint,
        string traceTag,
        CancellationToken token,
        int maxAttempts = 3,
        int delayMs = 200,
        bool requireUsableContext = true)
        {
            if (playwright == null)
                throw new ArgumentNullException(nameof(playwright));
            if (string.IsNullOrWhiteSpace(endpoint))
                throw new ArgumentException("CDP endpoint cannot be null or empty.", nameof(endpoint));
            if (maxAttempts <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            if (delayMs < 0)
                throw new ArgumentOutOfRangeException(nameof(delayMs));

            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                IBrowser? browser = null;

                try
                {
                    LogWriteLine($"{traceTag} CDP连接尝试 {attempt}/{maxAttempts}: {endpoint}");

                    browser = await playwright.Chromium.ConnectOverCDPAsync(endpoint);

                    if (browser == null)
                        throw new InvalidOperationException("ConnectOverCDPAsync returned null browser.");

                    if (!browser.IsConnected)
                        throw new InvalidOperationException("Browser is not connected after ConnectOverCDPAsync.");

                    if (requireUsableContext)
                    {
                        // 至少等待到 contexts 可访问
                        var contexts = browser.Contexts;
                        if (contexts == null)
                            throw new InvalidOperationException("Browser contexts is null.");

                        // 有些场景刚连上时 contexts 为空，但通常很快就会出现默认 context
                        // 给一个很短的稳定窗口，不再做长重试
                        if (contexts.Count == 0)
                        {
                            await Task.Delay(100, token);
                            contexts = browser.Contexts;
                        }

                        if (contexts == null || contexts.Count == 0)
                            throw new InvalidOperationException("Browser has no available contexts after CDP connect.");
                    }

                    LogWriteLine($"{traceTag} CDP连接成功: {endpoint}");
                    ResetCdpFinalFailureTracker(traceTag);
                    return browser;
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (browser != null)
                            await browser.CloseAsync();
                    }
                    catch { }

                    throw;
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    try
                    {
                        if (browser != null)
                            await browser.CloseAsync();
                    }
                    catch { }

                    LogWriteLine($"{traceTag} CDP连接失败 {attempt}/{maxAttempts}: {ex.Message}");

                    if (attempt >= maxAttempts)
                        break;

                    await Task.Delay(delayMs, token);
                }
            }

            if (lastException != null)
            {
                LogWriteLine($"{traceTag} CDP连接最终失败: {lastException}");
                HandleCdpFinalFailureForRestart(traceTag, lastException);
            }

            return null;
        }

        private static int ParseSleepMilliseconds(JToken taskArgs, int defaultMinMs = 8000, int defaultMaxMs = 15000)
        {
            var sleep = CommonHelper.RandomRange(defaultMinMs, defaultMaxMs);

            var sleepText = taskArgs.SelectToken("task.sleep")?.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(sleepText))
                return sleep;

            if (sleepText.Contains('-'))
            {
                var parts = sleepText
                    .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var minSeconds) &&
                    int.TryParse(parts[1], out var maxSeconds) &&
                    minSeconds >= 0 &&
                    maxSeconds >= 0)
                {
                    if (minSeconds > maxSeconds)
                        (minSeconds, maxSeconds) = (maxSeconds, minSeconds);

                    return CommonHelper.RandomRange(minSeconds * 1000, maxSeconds * 1000);
                }
            }
            else if (int.TryParse(sleepText, out var seconds) && seconds >= 0)
            {
                return seconds * 1000;
            }

            return sleep;
        }

        #region ExecuteWorkerAsync

        public override async Task<(bool, bool, int)> ExecuteWorkerAsync(string uniqueId, JObject taskArgs, CancellationToken token)
        {
            WorkerRunContext? ctx = null;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            try
            {
                var config = BuildTaskConfig(uniqueId, taskArgs, linkedCts);

                ctx = new WorkerRunContext(config)
                {
                    LandingDispatcher = new LandingPageStrategyDispatcher(new ILandingPageStrategy[]
                    {
                        new AliLandingPageStrategy(this),
                        new DefaultLandingPageStrategy(this),
                    })
                };

                this.QTPExecuteStart(config.TaskId);
                LogWriteLine($"{this.Title}:ExecuteWorker:Start");

                ctx.Playwright = await _playwrightProvider.GetAsync();
                linkedCts.Token.ThrowIfCancellationRequested();

                var browser = await StartAndConnectBrowserAsync(ctx, linkedCts.Token);
                if (browser == null)
                {
                    if (ctx.ProxyFailed)
                    {
                        LogWriteLine($"{this.Title}:ExecuteWorker:浏览器/CDP建立失败，疑似代理异常: {ctx.ProxyFailedReason}");
                    }
                    else
                    {
                        LogWriteLine($"{this.Title}:ExecuteWorker:浏览器启动或CDP连接失败");
                    }

                    return (false, false, 0);
                }

                ctx.Browser = browser;

                if (!ctx.Browser.IsConnected)
                {
                    ctx.ProxyFailed = true;
                    ctx.ProxyFailedReason ??= "Browser.IsConnected == false";
                    LogWriteLine($"{this.Title}:ExecuteWorker:Browser未连接: {ctx.ProxyFailedReason}");
                    return (false, false, 0);
                }

                if (ctx.Browser.Contexts == null || ctx.Browser.Contexts.Count == 0)
                {
                    ctx.ProxyFailed = true;
                    ctx.ProxyFailedReason ??= "Browser.Contexts.Count == 0";
                    LogWriteLine($"{this.Title}:ExecuteWorker:Browser无可用Context: {ctx.ProxyFailedReason}");
                    return (false, false, 0);
                }

                ctx.Context = ctx.Browser.Contexts[0];
                ctx.CdpManager = new CDPSessionManager(ctx.Context);

                await ConfigureContextAsync(ctx, linkedCts.Token);
                await AttachLifecycleEventsAsync(ctx, linkedCts.Token);

                if (ctx.ProxyFailed)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:初始化阶段已判定代理异常: {ctx.ProxyFailedReason}");
                    return (false, ctx.PageTriggerClick, ctx.PageAdsCount);
                }

                if (ctx.PageCrashed)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:初始化阶段页面崩溃: {ctx.LastFailureReason}");
                    return (false, ctx.PageTriggerClick, ctx.PageAdsCount);
                }

                var ok = await RunMainFlowAsync(ctx, linkedCts.Token);
                if (!ok)
                {
                    if (ctx.ProxyFailed)
                    {
                        LogWriteLine($"{this.Title}:ExecuteWorker:任务失败，代理异常: {ctx.ProxyFailedReason}");
                    }
                    else if (ctx.PageCrashed)
                    {
                        LogWriteLine($"{this.Title}:ExecuteWorker:任务失败，页面崩溃: {ctx.LastFailureReason}");
                    }
                    else if (!string.IsNullOrWhiteSpace(ctx.LastFailureReason))
                    {
                        LogWriteLine($"{this.Title}:ExecuteWorker:任务失败: {ctx.LastFailureReason}");
                    }
                }
                await Task.Delay(CommonHelper.RandomRange(1200, 2500));
                return (ok, ctx.PageTriggerClick, ctx.PageAdsCount);
            }
            catch (OperationCanceledException)
            {
                if (ctx?.ProxyFailed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled(代理异常): {ctx.ProxyFailedReason}");
                    return (false, ctx.PageTriggerClick, ctx.PageAdsCount);
                }

                if (ctx?.PageCrashed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled(页面崩溃): {ctx.LastFailureReason}");
                    return (false, ctx.PageTriggerClick, ctx.PageAdsCount);
                }

                if (ctx != null && !string.IsNullOrWhiteSpace(ctx.LastFailureReason))
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled: {ctx.LastFailureReason}");
                    return (false, ctx.PageTriggerClick, ctx.PageAdsCount);
                }

                LogWriteLine($"{this.Title}:ExecuteWorker:Canceled");
                return (false, ctx?.PageTriggerClick ?? false, ctx?.PageAdsCount ?? 0);
            }
            catch (PlaywrightException ex)
            {
                if (ctx != null)
                    ctx.LastFailureReason = ex.Message;

                if (ctx?.ProxyFailed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:PlaywrightException(代理异常): {ctx.ProxyFailedReason}");
                    return (false, ctx.PageTriggerClick, ctx.PageAdsCount);
                }

                if (ctx?.PageCrashed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:PlaywrightException(页面崩溃): {ctx.LastFailureReason}");
                    return (false, ctx.PageTriggerClick, ctx.PageAdsCount);
                }

                LogWriteLine($"{this.Title}:ExecuteWorker:PlaywrightException: {ex}");
                return (false, ctx?.PageTriggerClick ?? false, ctx?.PageAdsCount ?? 0);
            }
            catch (Exception ex)
            {
                if (ctx != null)
                    ctx.LastFailureReason = ex.Message;

                if (ctx?.ProxyFailed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:异常(代理异常): {ctx.ProxyFailedReason}");
                    return (false, ctx.PageTriggerClick, ctx.PageAdsCount);
                }

                if (ctx?.PageCrashed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:异常(页面崩溃): {ctx.LastFailureReason}");
                    return (false, ctx.PageTriggerClick, ctx.PageAdsCount);
                }

                LogWriteLine(ex.ToString());
                return (false, ctx?.PageTriggerClick ?? false, ctx?.PageAdsCount ?? 0);
            }
            finally
            {
                try
                {
                    if (!linkedCts.IsCancellationRequested)
                        linkedCts.Cancel();
                }
                catch
                {
                }

                if (ctx != null)
                {
                    try
                    {
                        if (ctx.CdpManager != null)
                            await ctx.CdpManager.DisposeAsync();
                    }
                    catch
                    {
                    }

                    try
                    {
                        if (ctx.Browser != null && ctx.Browser.IsConnected)
                            await ctx.Browser.CloseAsync();
                    }
                    catch
                    {
                    }

                    try
                    {
                        await CloseBrowserProcess(uniqueId);
                    }
                    catch
                    {
                    }
                }
            }
        }
        #endregion

        #region Main Flow

        private async Task<bool> RunMainFlowAsync(WorkerRunContext ctx, CancellationToken token)
        {

            string brand = ctx.Config.TaskArgs.SelectToken("dev.make")?.Value<string>() ?? "";
            string model = ctx.Config.TaskArgs.SelectToken("dev.model")?.Value<string>() ?? "";
            var dev = ctx.Config.TaskArgs.SelectToken("dev") as JObject ?? new JObject();
            int seed = StableSeed.Create(dev);
            ctx.Human = HumanInputFactory.Create(
                ctx.Config.Os,
                seed,
                brand,
                model,
                message => LogWriteLine($"{this.Title}:{message}"));


            for (ctx.PvIndex = 1; ctx.PvIndex <= ctx.Config.TotalPV; ctx.PvIndex++)
            {
                token.ThrowIfCancellationRequested();
                LogWriteLine($"{this.Title}:pv：{ctx.Config.TotalPV}/{ctx.PvIndex}");
                await EnsureSinglePageAsync(ctx, token);

                if (ctx.Page == null || ctx.Page.IsClosed)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: EnsureSinglePage后 Page为空或已关闭");
                    return false;
                }

                if (ctx.Browser == null || !ctx.Browser.IsConnected)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: EnsureSinglePage后 Browser为空或已断开");
                    return false;
                }

                var entry = await PrepareEntryAsync(ctx, token);
                if (!entry.Success)
                {
                    if (entry.EndTask)
                        return CompleteSuccess(ctx);

                    continue;
                }

                if (ctx.Config.IsTest)
                {
                    entry.FirstPageUrl = "https://xdssp.mediav.com/s?type=22&r=20&showid=MJ7NJn&url=https%3A%2F%2Fp4psearch.1688.com%2Fhamlet.html%3Fscene%3D6%26cosite%3D360PMP%26_force_strategy_%3D273%26trackid%3D1289273_4648165_35243290_%7Bcreativeid%7D%26qhclickid%3D%7Bsource_id%7D%26m_ac%3D1289273%26mvosr%3D%7Bsource_id%7D%26mvaid%3D1289273";
                }

                if (string.IsNullOrWhiteSpace(entry.FirstPageUrl))
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: FirstPageUrl为空");
                    continue;
                }

                if (ctx.Page == null || ctx.Page.IsClosed)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: Navigate前 Page为空或已关闭");
                    continue;
                }

                if (ctx.Browser == null || !ctx.Browser.IsConnected)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: Navigate前 Browser为空或已断开");
                    continue;
                }

                bool gotoOk;
                try
                {
                    gotoOk = await NavigateToEntryAsync(ctx, entry.FirstPageUrl!, token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (PlaywrightException ex) when (IsClosedPlaywrightException(ex))
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: NavigateToEntryAsync 页面已关闭: {ex.Message}");
                    continue;
                }

                if (!gotoOk)
                    continue;


                if (ctx.Config.IsTest)
                {
                    await RunTestBranchAsync(ctx, entry, token);
                    return CompleteSuccess(ctx);
                }

                ctx.ResetPerPvState();

                if (ctx.Page == null || ctx.Page.IsClosed)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: 导航后 Page为空或已关闭");
                    continue;
                }

                if (ctx.Page.Url.Contains("punish?x5secdata"))
                {
                    this.X5Secdata(ctx.Config.TaskId, 1, ctx.Page.Url);
                    return CompleteSuccess(ctx);
                }


                LogWriteLine($"{this.Title}:ExecuteWorker: {((ctx.Config.PageLoadedDelayMs) / 1000.0):N2}");

                var delayMs = CommonHelper.RandomRange(3000, 10000);
                await Task.Delay(delayMs, token);
                // 不要用 Math.Abs，避免配置时间比 delayMs 小时反而多等
                var restMs = Math.Max(0, ctx.Config.PageLoadedDelayMs - delayMs);
                if (restMs > 500)
                {
                    await BrowseForAsync(ctx, duration: TimeSpan.FromMilliseconds(restMs), token);
                }


                token.ThrowIfCancellationRequested();
                if (ctx.Page == null || ctx.Page.IsClosed)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: 广告检测前 Page为空或已关闭");
                    continue;
                }

                if (ctx.Browser == null || !ctx.Browser.IsConnected)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: 广告检测前 Browser已断开");
                    continue;
                }

                await BrowseForAsync(ctx, 5, 8, token);

                await Task.Delay(CommonHelper.RandomRange(3500, 8500), token);



                await DecideJumpClickAsync(ctx, token);
                if (ctx.JumpClick)
                {

                    await ctx.Human.ScrollByIntentAsync(
                        ctx.Page!,
                        ctx.CdpSession!,
                        HumanActionIntent.Reading,
                        token);

                    var clickFlow = await TryExecuteJumpClickAsync(ctx, token);
                    if (clickFlow == FlowControl.EndTask)
                        return CompleteSuccess(ctx);
                }

                var sleepFlow = await ExecuteTaskSleepPhaseAsync(ctx, token);
                if (sleepFlow == FlowControl.EndTask)
                    return CompleteSuccess(ctx);

                if (sleepFlow == FlowControl.NextPv)
                {
                    continue;
                }
                return CompleteSuccess(ctx);
            }

            return CompleteSuccess(ctx);
        }


        private bool CompleteSuccess(WorkerRunContext ctx)
        {
            this.QTPExecuteComplete(ctx.Config.TaskId);
            LogWriteLine($"{this.Title}:ExecuteWorker:Complete");
            return true;
        }

        #endregion

        #region Config / Context

        private TaskConfig BuildTaskConfig(string uniqueId, JObject taskArgs, CancellationTokenSource linkedCts)
        {

            var os = taskArgs.SelectToken("os")!.Value<int>();

            var sw1 = taskArgs.SelectToken("dev.sw")?.Value<int>() ?? 1080;
            var sh1 = taskArgs.SelectToken("dev.sh")?.Value<int>() ?? 1920;
            float deviceScale = 1.0f;
            int sw = 0;
            int sh = 0;
            if (os == 1 || os == 2)
            {
                var profileResult = AndroidViewportMatcher.Match(sw1, sh1);
                deviceScale = profileResult.DeviceScaleFactor;
                sw = profileResult.CssWidth;
                sh = profileResult.CssHeight;

            }
            else
            {
                var profileResult = WindowsViewportMatcher.Match(sw1, sh1);
                deviceScale = profileResult.DeviceScaleFactor;
                sw = profileResult.CssWidth;
                sh = profileResult.CssHeight;
            }



            var maxTouchPoints = os == 1 || os == 2 ? CommonHelper.RandomRange(4, 6) : 0;




            var kernelVersion = taskArgs.SelectToken("kernelVersion")?.Value<string>() ?? _appSettings.KernelVersion;
            var processIndex = taskArgs.SelectToken("processIndex")?.Value<int>() ?? 1;
            var cacheName = taskArgs.SelectToken("cacheName")!.Value<string>();

            return new TaskConfig
            {
                UniqueId = uniqueId,
                TaskArgs = taskArgs,
                LinkedCts = linkedCts,
                TaskId = taskArgs.SelectToken("task.id")!.Value<int>(),
                TaskUrl = taskArgs.SelectToken("task.url")!.Value<string>(),
                SleepMs = ParseSleepMilliseconds(taskArgs),
                PageLoadingTimeoutMs = taskArgs.SelectToken("pageLoadingTimeout")?.Value<int>() * 1000 ?? 30000,
                PageLoadedDelayMs = ParsePageLoadedDelayMilliseconds(taskArgs),
                UserAgent = taskArgs.SelectToken("dev.ua")!.Value<string>(),
                Os = os,
                DeviceScale = deviceScale,
                Sw = sw,
                Sh = sh,
                PvsTriggerOne = taskArgs.SelectToken("pvsTriggerOne")?.Value<bool>() ?? true,
                CurrentUV = taskArgs.SelectToken("currentUV")?.Value<int>() ?? 0,
                KernelVersion = kernelVersion,
                MaxTouchPoints = 0,
                ProcessIndex = processIndex,
                IsTest = taskArgs.SelectToken("isTest")?.Value<bool>() ?? false,
                TotalPV = taskArgs.SelectToken("totalPV")?.Value<int>() ?? 1,
                CacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", "Chrome", kernelVersion, "User_Cache", cacheName),
                UserDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", "Chrome", kernelVersion, "User_Data", $"{processIndex}_{Guid.NewGuid():n}")
            };
        }

        private int ParsePageLoadedDelayMilliseconds(JObject taskArgs)
        {
            var pageloadedDelay = CommonHelper.RandomRange(8000, 15000);
            var token = taskArgs.SelectToken("pageloadedDelay");
            if (token == null)
                return pageloadedDelay;

            var str = token.Value<string>();
            if (string.IsNullOrWhiteSpace(str))
                return pageloadedDelay;

            if (str.Contains("-"))
            {
                var values = str.Split('-', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => Convert.ToInt32(s))
                    .ToArray();

                if (values.Length == 2)
                    return CommonHelper.RandomRange(values[0] * 1000, values[1] * 1000);
            }

            if (int.TryParse(str, out var v))
                return v * 1000;

            return pageloadedDelay;
        }

        #endregion

        #region Browser Boot / Events

        private async Task<IBrowser?> StartAndConnectBrowserAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var args = BuildChromiumArgs(ctx.Config, out var proxyServer);
            var chromePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "File", "chrome-win", ctx.Config.KernelVersion, "chrome.exe");
            var session = await _processManager.StartChromium(
            ctx.Config.UniqueId,
            chromePath,
            ctx.Config.UserDataDir,
            TimeSpan.FromSeconds(_appSettings.IpTtl),
            $"about:blank {string.Join(" ", args)}",
            proxyServer,
            readyTimeout: TimeSpan.FromSeconds(15),
            token: token);
            ctx.DebugPort = session.DebugPort;
            var endpoint = $"http://localhost:{session.DebugPort}";
            token.ThrowIfCancellationRequested();

            return await ConnectOverCDPWithRetryAsync(
                   ctx.Playwright!,
                   endpoint,
                   BuildTraceTag(ctx),
                   token,
                   maxAttempts: 3,
                   delayMs: 200,
                   requireUsableContext: true);
        }

        private List<string> BuildChromiumArgs(TaskConfig config, out string proxyServer)
        {

            var screen = config.TaskArgs.SelectToken("dev.screen");
            int sw = screen?.SelectToken("width")?.Value<int>() ?? config.Sw;
            int sh = screen?.SelectToken("height")?.Value<int>() ?? config.Sh;
            int availWidth = screen?.SelectToken("availWidth")?.Value<int>() ?? config.Sw;
            int availHeight = screen?.SelectToken("availHeight")?.Value<int>() ?? config.Sh;

            var args = new List<string>
            {
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-logging",
                "--enable-unsafe-swiftshader",
                "--use-fake-ui-for-media-stream",
                "--use-fake-device-for-media-stream",
                "--show-avatar-button=never",
                "--disable-http2-grease-settings",
                "--hide-bad-flags",
                "--hide-crashed-bubble",
                "--enable-unsafe-swiftshader",
                $"--user-agent=\"{config.UserAgent}\"",
                $"--window-position=0,0",
                $"--window-size={sw},{sh}",
                $"--screen-size={sw},{sh}",
                $"--screen-avail-size={availWidth},{availHeight}",
                $"--device-pixel-ratio={config.DeviceScale}",
            };

            if (config.Os == 1 || config.Os == 2)
            {

            }

            proxyServer = string.Empty;
            proxyServer = string.Empty;
            var isProxyMode = config.TaskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            if (isProxyMode)
            {
                proxyServer = config.TaskArgs.SelectToken("proxy_server")!.Value<string>();
                var protocol = config.TaskArgs.SelectToken("protocol")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(protocol) && protocol.Equals("socks5"))
                {
                    args.Add($"--proxy-server=\"socks5://{proxyServer}\"");
                    var proxyServerIp = proxyServer.Split(':').FirstOrDefault() ?? "";
                    if (!string.IsNullOrWhiteSpace(proxyServerIp))
                        args.Add($"--host-resolver-rules=\"MAP * ~NOTFOUND , EXCLUDE {proxyServerIp}\"");
                    args.Add($"--proxy-bypass-list=<-loopback>");
                }
                else
                {
                    args.Add($"--proxy-server=\"{proxyServer}\"");
                }

            }



            if (config.TaskArgs.SelectToken("isHiddenMode")?.Value<bool>() ?? false)
                args.Add("--headless");

            if (config.TaskArgs.SelectToken("incognito")?.Value<bool>() ?? false)
            {
                args.Add("--incognito");
                args.Add("--enable-incognito-themes");
            }
            else
            {
                args.Add($"--disk-cache-dir=\"{config.CacheDir}\"");
            }

            args.AddRange(InitFPArgs(config));
            return args;
        }

        private async Task ConfigureContextAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (ctx.Context == null)
                throw new InvalidOperationException("Context is null.");

            if (ctx.Config.TaskArgs.SelectToken("ipInfo.lon") != null &&
                ctx.Config.TaskArgs.SelectToken("ipInfo.lat") != null)
            {
                await ctx.Context.SetGeolocationAsync(new Geolocation
                {
                    Latitude = ctx.Config.TaskArgs.SelectToken("ipInfo.lat")!.Value<float>(),
                    Longitude = ctx.Config.TaskArgs.SelectToken("ipInfo.lon")!.Value<float>()
                });
            }

            ctx.Page = ctx.Context.Pages[0];
            await InitPageAsync(ctx, ctx.Page, token);
        }

        private Task AttachLifecycleEventsAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (ctx.Browser == null || ctx.Context == null)
                return Task.CompletedTask;

            ctx.Browser.Disconnected += (_, _) =>
            {
                try
                {
                    CancelLinkedContext(ctx, "BrowserDisconnected");
                }
                catch
                {
                }
            };

            ctx.Context.Page += (_, newPage) =>
            {
                _ = HandleContextPageAsync(ctx, newPage);
            };

            return Task.CompletedTask;
        }

        private async Task HandleContextPageAsync(WorkerRunContext ctx, IPage newPage)
        {
            try
            {
                if (!ctx.Config.LinkedCts.IsCancellationRequested)
                    await InitPageAsync(ctx, newPage, ctx.Config.LinkedCts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }


        private static bool IsLikelyProxyFailureText(string? errorText)
        {
            if (string.IsNullOrWhiteSpace(errorText))
                return false;

            return
                errorText.Contains("ERR_INVALID_AUTH_CREDENTIALS", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("ERR_TUNNEL_CONNECTION_FAILED", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("ERR_PROXY_CONNECTION_FAILED", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("ERR_NO_SUPPORTED_PROXIES", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("ERR_SOCKS_CONNECTION_FAILED", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("ERR_PROXY_CERTIFICATE_INVALID", StringComparison.OrdinalIgnoreCase) ||
                errorText.Contains("ERR_EMPTY_RESPONSE", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProxyAuthOrTunnelFailure(string? failure)
        {
            if (string.IsNullOrWhiteSpace(failure))
                return false;

            return failure.Contains("ERR_INVALID_AUTH_CREDENTIALS", StringComparison.OrdinalIgnoreCase)
                || failure.Contains("ERR_TUNNEL_CONNECTION_FAILED", StringComparison.OrdinalIgnoreCase);
        }
        private static bool IsMainPageRequest(IRequest request, IPage page)
        {
            try
            {
                if (request == null || page == null)
                    return false;

                // 最优先：主 Frame 的 document 导航请求
                if (request.IsNavigationRequest &&
                    string.Equals(request.ResourceType, "document", StringComparison.OrdinalIgnoreCase) &&
                    request.Frame == page.MainFrame)
                {
                    return true;
                }

                // 兜底：URL 完全一致时，也认为是当前主页面请求
                var reqUrl = request.Url ?? string.Empty;
                var pageUrl = page.Url ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(reqUrl) &&
                    !string.IsNullOrWhiteSpace(pageUrl) &&
                    string.Equals(reqUrl, pageUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsMainPageRequest2(IRequest request, IPage page)
        {
            try
            {
                if (request == null || page == null)
                    return false;

                if (request.IsNavigationRequest &&
                    string.Equals(request.ResourceType, "document", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                var reqUrl = request.Url ?? string.Empty;
                var pageUrl = page.Url ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(reqUrl) &&
                    !string.IsNullOrWhiteSpace(pageUrl) &&
                    string.Equals(reqUrl, pageUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }


        private async Task InitPageAsync(WorkerRunContext ctx, IPage page, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            await page.SetViewportSizeAsync(ctx.Config.Sw, ctx.Config.Sh);
            var cdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(page);
            await cdpSession.SendAsync("Page.enable");

            cdpSession.Event("Page.downloadWillBegin").OnEvent += (_, _) =>
            {
                Interlocked.Increment(ref ctx.TriggerDownloadSign);
            };

            if (ctx.Config.Os == 1 || ctx.Config.Os == 2)
            {
                await CDPHelper.InitCDPSession(cdpSession, ctx.Config.MaxTouchPoints);
            }
            else
            {

            }

            //await CDPHelper.SetDeviceMetricsOverride(cdpSession, ctx.Config.Sw, ctx.Config.Sh, ctx.Config.DeviceScale, (ctx.Config.Os == 1 || ctx.Config.Os == 2 ? true : false));

            await CDPHelper.SetBrowserPermission(cdpSession);

            page.Dialog += async (_, dialog) =>
            {
                try { await dialog.DismissAsync(); } catch { }
            };

            page.Crash += (_, _) =>
            {
                try
                {
                    ctx.PageCrashed = true;
                    ctx.LastFailureReason = "Page crashed";
                    CancelLinkedContext(ctx, "PageCrashed");
                }
                catch { }
            };

            page.RequestFailed += (_, e) =>
            {
                try
                {
                    var failure = e.Failure ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(failure))
                        return;

                    var reqUrl = e.Url ?? string.Empty;
                    var pageUrl = page.Url ?? string.Empty;

                    // 统一记录最后失败原因，便于排查
                    ctx.LastFailureReason = $"RequestFailed: {failure}, req={reqUrl}, page={pageUrl}";

                    bool isProxyFailureAnyRequest =
                        (failure.Contains("ERR_INVALID_AUTH_CREDENTIALS", StringComparison.OrdinalIgnoreCase) ||
                        failure.Contains("ERR_TUNNEL_CONNECTION_FAILED", StringComparison.OrdinalIgnoreCase) && pageUrl.Equals(reqUrl));

                    //bool isMainPageEmptyResponse =
                    //    failure.Contains("ERR_EMPTY_RESPONSE", StringComparison.OrdinalIgnoreCase) &&
                    //    IsMainPageRequest(e, page);

                    if (!isProxyFailureAnyRequest)// && !isMainPageEmptyResponse
                        return;

                    if (ctx.ProxyFailed)
                        return;

                    ctx.ProxyFailed = true;
                    ctx.ProxyFailedReason = $"请求失败: {failure}, req={reqUrl}, page={pageUrl}";

                    CancelLinkedContext(ctx, "RequestFailedProxy");
                }
                catch
                {
                }
            };





            //page.RequestFailed += (_, e) =>
            //{

            //    //try
            //    //{
            //    //    if (!string.IsNullOrWhiteSpace(e.Failure) &&
            //    //        (e.Failure.Contains("ERR_INVALID_AUTH_CREDENTIALS") ||
            //    //         (e.Failure.Contains("ERR_TUNNEL_CONNECTION_FAILED") && page.Url.Equals(e.Url))))
            //    //    {
            //    //        LogWriteLine($"page.RequestFailed:{e.Failure},{e.Url},{page.Url}");
            //    //        if (!ctx.Config.LinkedCts.IsCancellationRequested)
            //    //            ctx.Config.LinkedCts.Cancel();
            //    //    }
            //    //}
            //    //catch { }
            //};

            page.Download += async (_, download) =>
            {
                Interlocked.Increment(ref ctx.TriggerDownloadSign);
                try { await download.CancelAsync(); } catch { }
            };

            if (ctx.Page == page)
                ctx.CdpSession = cdpSession;
        }

        #endregion

        #region Prepare / Navigate

        private async Task EnsureSinglePageAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            while (ctx.Context!.Pages.Count > 1)
            {
                token.ThrowIfCancellationRequested();
                await ctx.Context.Pages[^1].CloseAsync();
            }

            ctx.Page = ctx.Context.Pages[0];
            await ctx.Page.GotoAsync("about:blank");
            ctx.CdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(ctx.Page);
        }

        private string BuildTraceTag(WorkerRunContext ctx)
        {
            return $"{this.Title}[taskId={ctx.Config.TaskId},uniqueId={ctx.Config.UniqueId},uv={ctx.Config.CurrentUV},pv={ctx.PvIndex},port={ctx.DebugPort}]";
        }

        private void CancelLinkedContext(WorkerRunContext ctx, string reason)
        {
            try
            {
                if (!ctx.Config.LinkedCts.IsCancellationRequested)
                {
                    if (string.IsNullOrWhiteSpace(ctx.LastFailureReason))
                        ctx.LastFailureReason = reason;

                    ctx.Config.LinkedCts.Cancel();
                }
            }
            catch
            {
            }
        }

        private async Task<EntryPreparationResult> PrepareEntryAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = new EntryPreparationResult
            {
                Success = true,
                FirstPageUrl = ctx.Config.TaskUrl,
                EndTask = false
            };

            return result;
        }

        private async Task<bool> NavigateToEntryAsync(WorkerRunContext ctx, string url, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                await ctx.Page!.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = ctx.Config.PageLoadingTimeoutMs
                });
            }
            catch (TimeoutException ex)
            {
                LogWriteLine($"加载超时:{ex.Message}");
                if (ctx.Page!.Url.Contains("sm.cn"))
                {
                    var title = await ctx.Page!.TitleAsync();
                    if (!title.StartsWith("阿里巴巴1688.com"))
                        return false;
                }

            }

            ctx.CurrentPageUrl = ctx.Page!.Url;
            ctx.PagesCount = ctx.Context!.Pages.Count;

            this.QTPExecuteDSP(ctx.Config.TaskId);
            return true;
        }

        #endregion

        #region Ads / JumpClick

        /// <summary>
        /// 处理点击比例
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task DecideJumpClickAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int clickRate = ctx.Config.TaskArgs.SelectToken("task.click_rate")!.Value<int>();
            ctx.JumpClick = false;
            ctx.PageTriggerClick = false;

            if (clickRate <= 0)
            {
                return;
            }

            var ctr = await _aggregator.GetClickRatioAsync(ctx.Config.TaskId, clickRate);
            LogWriteLine($"点击比率:{(ctr * 100):N2}%");
            ctx.JumpClick = await _aggregator.CanClickthroughAsync(ctx.Config.TaskId, clickRate);
        }

        /// <summary>
        /// 触发广告
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<FlowControl> TryExecuteJumpClickAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var sponsoreds = ctx.Page!.Locator("div[ad_dot_url^='http'],div.ad-wolong-container:has(a[data-url^='http'])");
            var sponsoredCount = await sponsoreds.CountAsync();
            if (sponsoredCount <= 0)
            {
                return FlowControl.Continue;
            }

            await Task.Delay(CommonHelper.RandomRange(3500, 8500), token);

            var candidates = await BuildSponsoredCandidatesAsync(ctx, sponsoreds, sponsoredCount, token);

            foreach (var sponsored in candidates)
            {
                token.ThrowIfCancellationRequested();

                await ctx.Human.MoveToElementAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    sponsored,
                    maxSwipes: 10,
                    cancellationToken: token);


                if (!await IsElementPartiallyVisibleAsync(sponsored))
                {
                    LogWriteLine($"{this.Title}:广告位滑动后仍不可见，跳过");
                    continue;
                }

                await Task.Delay(CommonHelper.RandomRange(500, 1500), token);

                var target = await PickSponsoredTargetAsync(sponsored, token);
                if (target == null)
                    continue;

                var dataUrl = await target.GetAttributeAsync("data-url");
                if (string.IsNullOrWhiteSpace(dataUrl))
                    continue;

                var text = await target.InnerTextAsync();
                var box = await target.BoundingBoxAsync();

                if (box != null)
                    LogWriteLine($"触发广告位:{text}:({box.X},{box.Y},{box.Width},{box.Height})");
                else
                    LogWriteLine($"触发广告位:{text}");

                var click = await ClickAndDetectNavigationAsync(ctx, target, token);
                if (!click.Attempted)
                    continue;

                if (ctx.TriggerDownloadSign > 0)
                {
                    this.QTPExecuteClickthrough(ctx.Config.TaskId);
                    LogWriteLine($"{this.Title}:ExecuteWorker:Clickthrough");
                    ctx.PageTriggerClick = true;
                    return FlowControl.EndTask;
                }

                if (click.Navigated)
                {
                    this.QTPExecuteClickthrough(ctx.Config.TaskId);
                    LogWriteLine($"{this.Title}:ExecuteWorker:Clickthrough");
                    ctx.PageTriggerClick = true;
                    await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                    return await HandleLandingPageAsync(ctx, token);
                }
            }
            return FlowControl.Continue;
        }




        /// <summary>
        /// 获取候选的广告
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="sponsoreds"></param>
        /// <param name="count"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<List<ILocator>> BuildSponsoredCandidatesAsync(WorkerRunContext ctx, ILocator sponsoreds, int count, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var scored = new List<(int Score, ILocator Locator)>();

            foreach (var i in Enumerable.Range(0, count))
            {
                token.ThrowIfCancellationRequested();

                var sponsored = sponsoreds.Nth(i);
                var alis = sponsored.Locator("a.c-title,a.ad-desc,a.img-item,a[data-url^='http']");
                var alisCount = await alis.CountAsync();

                if (alisCount == 0)
                {
                    scored.Add((1000 + i, sponsored));
                    continue;
                }

                var dataUrl = await alis.First.GetAttributeAsync("data-url");
                if (string.IsNullOrWhiteSpace(dataUrl))
                {
                    scored.Add((1000 + i, sponsored));
                    continue;
                }

                int score = 0;
                if (dataUrl.Contains("baidu.com")) score = 50;
                else if (dataUrl.Contains("jd.com")) score = 60;
                else if (dataUrl.Contains("qq.com")) score = 70;
                else if (dataUrl.Contains("pinduoduo.com")) score = 80;
                else if (dataUrl.Contains("1688.com")) score = 800;
                else if (dataUrl.Contains("taobao.com")) score = 900;





                scored.Add((score * 1000 + i, sponsored));
            }

            return scored.OrderBy(x => x.Score).Select(x => x.Locator).ToList();
        }

        private async Task<ILocator?> PickSponsoredTargetAsync(ILocator sponsored, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var alis = sponsored.Locator("a.c-title,a[data-url^='http']");
            var visible = await GetVisibleElementsAsync(alis);
            if (visible.Count == 0)
                return null;

            var urls = new List<(ILocator Locator, string Url)>();
            foreach (var el in visible)
            {
                token.ThrowIfCancellationRequested();

                var dataUrl = await el.GetAttributeAsync("data-url");
                if (!string.IsNullOrWhiteSpace(dataUrl))
                    urls.Add((el, dataUrl));
            }

            if (urls.Count == 0)
                return null;

            var exts = new[] { ".apk", ".zip", ".exe", ".7z", ".rar" };
            var filtered = urls
                .Where(x => !exts.Any(ext => x.Url.Contains(ext, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(x => x.Url.Length)
                .ToList();

            if (filtered.Count > 0)
            {
                var groups = filtered
                       .GroupBy(x => new Uri(x.Url).Host, StringComparer.OrdinalIgnoreCase)
                       .OrderByDescending(g => g.Count())
                       .ToList();

                foreach (var g in groups)
                {
                    var list = g.ToList();

                    var uMob = list
                        .Where(x => x.Url.Contains(".u-mob.", StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    if (uMob.Count > 0)
                        return uMob[Random.Shared.Next(uMob.Count)].Locator;

                    if (list.Count > 0)
                        return list[Random.Shared.Next(list.Count)].Locator;
                }
            }

            return urls.OrderByDescending(x => x.Url.Length).First().Locator;
        }

        #endregion

        #region Landing Dispatcher / Strategies

        private async Task<FlowControl> HandleLandingPageAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ctx.LandingDispatcher == null)
                return FlowControl.Continue;



            var metrics = _aggregator.GetLocalMetrics(ctx.Config.TaskId, "dsp_second_jump_rate", "dsp_second_jump", "dsp_second_jump_click");
            if (metrics["dsp_second_jump_rate"] > 0)
            {
                _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_second_jump");

                if (metrics["dsp_second_jump_click"] > 0 && metrics["dsp_second_jump"] > 0)
                {
                    LogWriteLine($"[{ctx.Config.TaskId}] 二跳比率:{(metrics["dsp_second_jump_click"] / (double)metrics["dsp_second_jump"] * 100):N2}%");
                }

                bool canSeondJump = metrics["dsp_second_jump_rate"] == 100
                    || metrics["dsp_second_jump_click"] == 0
                    || ((metrics["dsp_second_jump_click"] / (double)metrics["dsp_second_jump"]) * 100 < metrics["dsp_second_jump_rate"]);

                if (!canSeondJump)
                    return FlowControl.Continue;


                _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_second_jump_click");
            }

            return await ctx.LandingDispatcher.DispatchAsync(ctx, token);
        }









        #endregion

        #region Generic Landing Helpers

        public async Task<ILocator?> ResolveOfferItemsAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var url = ctx.Page!.Url;
            ILocator? offerItems = null;
            if (url.Contains("m.p4psearch.1688.com"))
            {
                await BrowseForAsync(ctx, 3, 8, token);

                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("//div[starts-with(@class,'offer-item')]");
            }
            else if (url.Contains("m.1688.com"))
            {
                await BrowseForAsync(ctx, 3, 8, token);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("//div[starts-with(@class,'offer-item')]");
            }
            else if (url.Contains("uland.taobao.com"))
            {
                await BrowseForAsync(ctx, 3, 8, token);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("//a[starts-with(@class,'link')]");
            }
            return offerItems;
        }


        #endregion

        #region Task Sleep Phase

        private async Task<FlowControl> ExecuteTaskSleepPhaseAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();


            if (ctx.Page!.Url.Contains("1688.com") && ctx.Page.Url.Contains("_tmd_") && ctx.Page!.Url.Contains("punish?x5secdata"))
            {
                return FlowControl.EndTask;
            }


            if (ctx.JumpClick && ctx.PageTriggerClick)
            {
                await TryHandleRfq1688Async(ctx, token);
            }
            this.QTPExecuteSuccess(ctx.Config.TaskId);
            LogWriteLine($"{this.Title}:ExecuteWorker:Success");

            if (ctx.Config.TotalPV > 1)
            {
                if (ctx.JumpClick && !ctx.PageTriggerClick)
                {
                    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                    return FlowControl.NextPv;
                }

                if (ctx.JumpClick && ctx.PageTriggerClick && !ctx.Config.PvsTriggerOne)
                {
                    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                    return FlowControl.NextPv;
                }
            }

            if (ctx.TriggerDownloadSign > 0)
                return FlowControl.EndTask;

            if (ctx.Page!.Url.StartsWith("https://login.m.taobao.com")
                || ctx.Page.Url.StartsWith("https://havanalogin.taobao.com")
                || ctx.Page.Url.StartsWith("https://plogin.m.jd.com"))
            {
                await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                return FlowControl.EndTask;
            }
            if (ctx.Page!.Url.StartsWith("https://h5.m.taobao.com"))
            {
                if (await ctx.Page.GetByText("获取验证码").CountAsync() > 0)
                {
                    await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                    return FlowControl.EndTask;
                }
            }
            DateTime start = DateTime.Now;

            if (ctx.JumpClick && ctx.PageTriggerClick)
            {
                await TryHandleAllAsync(ctx, token);
            }

            LogWriteLine("延时停留");
            var loop = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                loop++;

                try
                {
                    LogWriteLine("滑动操作");
                    await ctx.Human.BrowseOnceAsync(ctx.Page!, ctx.CdpSession!, token);
                    if ((int)(DateTime.Now - start).TotalMilliseconds >= ctx.Config.SleepMs)
                        break;

                    await Task.Delay(CommonHelper.RandomRange(1500, 2500), token);
                    if (ctx.TriggerDownloadSign > 0)
                        return FlowControl.EndTask;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    break;
                }
            }

            LogWriteLine("动作完成");
            return FlowControl.EndTask;
        }

        /// <summary>
        /// 1688详情页,非询价处理
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task TryHandleNoRfq1688Async(WorkerRunContext ctx, CancellationToken token)
        {
            try
            {
                await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                await ClearPageCloseBtn(ctx, token);
                await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                await ClearSuccessTipNewCloseNew(ctx, token);
                await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);

                await BrowseForAsync(ctx, 3, 8, token);

                if (CommonHelper.Chance(0.25))
                {
                    var locator_detail = ctx.Page!.Locator("*:text-is('全部商品')");
                    var locator_detail_count = await locator_detail.CountAsync();
                    if (locator_detail_count == 0)
                    {
                        locator_detail = ctx.Page.Locator("*:text-is('进店看看')");
                        locator_detail_count = await locator_detail.CountAsync();
                    }
                    if (locator_detail_count == 0)
                    {
                        locator_detail = ctx.Page.Locator("*:text-is('进店看厂')");
                        locator_detail_count = await locator_detail.CountAsync();
                    }
                    if (locator_detail_count == 0)
                    {
                        locator_detail = ctx.Page.Locator(".recommend-container");
                        locator_detail_count = await locator_detail.CountAsync();
                    }

                    if (locator_detail_count > 0)
                    {
                        await ctx.Human.MoveToElementAsync(
                            ctx.Page!,
                            ctx.CdpSession!,
                            locator_detail,
                            maxSwipes: 10,
                            cancellationToken: token);


                        if (!await IsElementPartiallyVisibleAsync(locator_detail))
                        {
                            await locator_detail.ScrollIntoViewIfNeededAsync();
                        }



                        await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);
                        var clickRes2 = await ClickAndDetectNavigationAsync(ctx, locator_detail.First, token);
                        if (clickRes2.Navigated)
                        {
                            await Task.Delay(CommonHelper.RandomRange(3500, 5500), token);
                            await ClearPageCloseBtn(ctx, token);
                            await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                            await ClearSuccessTipNewCloseNew(ctx, token);
                            await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                            await BrowseForAsync(ctx, 3, 8, token);

                            locator_detail = ctx.Page
                                .Locator("body,iframe")
                                .Filter(new() { Visible = true })
                                .First;

                            if (await locator_detail.CountAsync() > 0)
                            {
                                await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);

                                await ctx.Human.MoveToElementAsync(
                                    ctx.Page!,
                                    ctx.CdpSession!,
                                    locator_detail.First,
                                    maxSwipes: 10,
                                    cancellationToken: token);

                                if (!await IsElementPartiallyVisibleAsync(locator_detail.First))
                                {
                                    await locator_detail.First.ScrollIntoViewIfNeededAsync();
                                }




                                var clickRes3 = await ClickAndDetectNavigationAsync(ctx, locator_detail.First, token);
                                if (clickRes3.Navigated)
                                {
                                    await Task.Delay(CommonHelper.RandomRange(3500, 5500), token);
                                    await ClearPageCloseBtn(ctx, token);
                                    await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                                    await ClearSuccessTipNewCloseNew(ctx, token);
                                    await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                                    await BrowseForAsync(ctx, 3, 8, token);

                                }

                            }
                        }

                    }
                    else
                    {
                        locator_detail = ctx.Page
                            .Locator("body,iframe")
                            .Filter(new() { Visible = true })
                            .First;

                        if (await locator_detail.CountAsync() > 0)
                        {
                            var clickRes3 = await ClickAndDetectNavigationAsync(ctx, locator_detail.First, token);
                            if (clickRes3.Navigated)
                            {
                                await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                                await ClearPageCloseBtn(ctx, token);
                                await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                                await ClearSuccessTipNewCloseNew(ctx, token);
                                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                await BrowseForAsync(ctx, 3, 8, token);
                            }

                        }
                    }
                }



            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
            return;
        }

        /// <summary>
        /// 1688详情页,询价处理
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task TryHandleRfq1688Async(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!ctx.Page!.Url.Contains("1688.com"))
                return;

            try
            {
                var metrics = _aggregator.GetLocalMetrics(ctx.Config.TaskId, "dsp_rfq1688", "dsp_rfq1688_click");
                if (metrics["dsp_rfq1688"] > 0)
                    LogWriteLine($"1688询价比率:{(metrics["dsp_rfq1688_click"] / (double)metrics["dsp_rfq1688"] * 100):N2}%");

                bool canClick = true;

                if (!canClick)
                {
                    await TryHandleNoRfq1688Async(ctx, token);
                    return;
                }


                await Task.Delay(CommonHelper.RandomRange(3500, 5500), token);
                var el = ctx.Page!.Locator("#od_xst_phone_input_val_new,#new_od_xst_phone_input_val_new");
                if (await el.CountAsync() == 0)
                {
                    var queryBtn = ctx.Page.Locator(".queryBtnTitleTop");
                    if (await queryBtn.CountAsync() == 0)
                        queryBtn = ctx.Page.GetByText("立即询价");

                    if (await queryBtn.CountAsync() > 0)
                    {
                        await ctx.Human.ClickAsync(ctx.Page, ctx.CdpSession!, queryBtn.First, token);
                        await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);
                        el = ctx.Page.Locator("#od_xst_phone_input_val_new,#new_od_xst_phone_input_val_new");
                    }
                }

                if (await el.CountAsync() == 0)
                {
                    await TryHandleNoRfq1688Async(ctx, token);
                    return;
                }

                _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_rfq1688_click");

                var phone = await _adeHelper.GetPhoneNumberAsync();
                if (string.IsNullOrWhiteSpace(phone))
                {
                    await TryHandleNoRfq1688Async(ctx, token);
                    return;
                }

                await el.First.FillAsync("");
                await Task.Delay(CommonHelper.RandomRange(50, 100), token);
                await el.First.PressSequentiallyAsync(phone);
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                var answerContents = ctx.Page.Locator("div.new_answer_content span,div.answer_content span");
                if (await answerContents.CountAsync() > 0)
                {
                    int count = await answerContents.CountAsync();
                    var answer = answerContents.Nth(CommonHelper.RandomRange(0, count));
                    await ctx.Human.ClickAsync(ctx.Page, ctx.CdpSession!, answer.First, token);
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                }
                else
                {
                    var chatText = ChatTextHelper.GetChatText();
                    el = ctx.Page.Locator("textarea#new_od_xst_msg_input_val_new_message,textarea#od_xst_msg_input_val_new_message");
                    if (await el.CountAsync() > 0)
                    {
                        await el.First.FillAsync("");
                        await Task.Delay(CommonHelper.RandomRange(50, 100), token);
                        await el.First.PressSequentiallyAsync(chatText);
                        await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                    }
                }

                el = ctx.Page.Locator(".new_successTipNew_wangwang_new,.successTipNew_call_new");
                if (await el.CountAsync() > 0)
                {
                    try
                    {
                        await ctx.Human.ClickAsync(ctx.Page, ctx.CdpSession!, el.First, token);
                        await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                        var sms = ctx.Page.GetByText("获取验证码");
                        if (await sms.CountAsync() > 0)
                        {
                            var close1 = ctx.Page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new");
                            if (await close1.CountAsync() > 0)
                                await ctx.Human.ClickAsync(ctx.Page, ctx.CdpSession!, close1.First, token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch { }

                    var close2 = ctx.Page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new,.newCloseIcon_content");
                    if (await close2.CountAsync() > 0)
                        await ctx.Human.ClickAsync(ctx.Page, ctx.CdpSession!, close2.First, token);
                }
                await ClearPageCloseBtn(ctx, token);
                await ClearSuccessTipNewCloseNew(ctx, token);
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                await BrowseForAsync(ctx, 3, 8, token);

                if (CommonHelper.Chance(0.25))
                {
                    var locator = ctx.Page.Locator("*:text-is('进店看看')");
                    var locator_count = await locator.CountAsync();
                    if (locator_count == 0)
                    {
                        locator = ctx.Page.Locator("*:text-is('进店看厂')");
                        locator_count = await locator.CountAsync();
                    }
                    if (locator_count == 0)
                    {
                        locator = ctx.Page.Locator("*:text-is('全部商品')");
                        locator_count = await locator.CountAsync();
                    }
                    if (locator_count == 0)
                    {
                        locator = ctx.Page.Locator(".recommend-container");
                        locator_count = await locator.CountAsync();
                    }

                    if (locator_count > 0)
                    {

                        await ctx.Human.MoveToElementAsync(
                            ctx.Page!,
                            ctx.CdpSession!,
                            locator.First,
                            maxSwipes: 10,
                            cancellationToken: token);

                        if (!await IsElementPartiallyVisibleAsync(locator.First))
                        {
                            await locator.First.ScrollIntoViewIfNeededAsync();

                        }

                        await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);
                        var clickRes2 = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                        if (clickRes2.Navigated)
                        {
                            await ClearPageCloseBtn(ctx, token);
                            await ClearSuccessTipNewCloseNew(ctx, token);
                            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                            await BrowseForAsync(ctx, 3, 8, token);


                            locator = ctx.Page
                                .Locator("body,iframe")
                                .Filter(new() { Visible = true })
                                .First;

                            if (await locator.CountAsync() > 0)
                            {
                                var clickRes3 = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                                if (clickRes3.Navigated)
                                {
                                    await ClearPageCloseBtn(ctx, token);
                                    await ClearSuccessTipNewCloseNew(ctx, token);
                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                    await BrowseForAsync(ctx, 3, 8, token);
                                }

                            }
                        }

                    }
                    else
                    {
                        locator = ctx.Page
                            .Locator("body,iframe")
                            .Filter(new() { Visible = true })
                            .First;

                        if (await locator.CountAsync() > 0)
                        {
                            var clickRes3 = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                            if (clickRes3.Navigated)
                            {
                                await ClearPageCloseBtn(ctx, token);
                                await ClearSuccessTipNewCloseNew(ctx, token);
                                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                await BrowseForAsync(ctx, 3, 8, token);


                            }

                        }
                    }

                }

            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }



        private async Task TryHandleAllAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ctx.Page!.Url.Contains("taobao.com") || ctx.Page!.Url.Contains("1688.com") || ctx.Page!.Url.Contains("jd.com") || ctx.Page!.Url.Contains("baidu.com"))
                return;
            try
            {
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                var acceptBtn = ctx.Page!.Locator(
                      "button:visible, a:visible, [role='button']:visible, input[type='button']:visible, input[type='submit']:visible, div:visible, span:visible"
                  ).Filter(new()
                  {
                      HasTextRegex = new Regex(
                          @"同意|接受|允许|我同意|我接受|允许全部|全部接受|全部同意|确认|继续|知道了|Agree|Accept|Allow|Accept All|Allow All|I Agree|I Accept|Consent|Got it|Continue|Accept Cookies|Allow Cookies",
                          RegexOptions.IgnoreCase
                      )
                  }).First;
                if (await acceptBtn.CountAsync() > 0)
                {
                    await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);
                    await ctx.Human.ClickAsync(ctx.Page, ctx.CdpSession!, acceptBtn.First, token);
                }
                await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);

                await ctx.Human.ScrollByIntentAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    HumanActionIntent.Reading,
                    token);

                ClickResult? clickResult = null;
                var bd_locator = ctx.Page.Locator("div.baidu-ad").Filter(new() { Visible = true });
                var bd_locator_count = await bd_locator.CountAsync();
                if (bd_locator_count > 0)
                {

                    var nodes = Enumerable.Range(0, bd_locator_count)
                     .OrderBy(_ => Guid.NewGuid())
                     .Select(i => bd_locator.Nth(i))
                     .ToList();
                    foreach (var node in nodes)
                    {
                        await ctx.Human.MoveToElementAsync(
                            ctx.Page!,
                            ctx.CdpSession!,
                            node,
                            maxSwipes: 10,
                            cancellationToken: token);

                        if (!await IsElementPartiallyVisibleAsync(node))
                        {
                            await node.ScrollIntoViewIfNeededAsync();
                        }


                        var box = await node.BoundingBoxAsync();
                        clickResult = await ClickAndDetectNavigationAsync(ctx, node, token);
                        if (clickResult.Navigated)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    if (ctx.Page.Frames.Count > 0)
                    {
                        foreach (var frame in ctx.Page.Frames)
                        {
                            if (!frame.Url.Contains("baidu.com"))
                                continue;
                            var el = await frame.FrameElementAsync();
                            if (el == null)
                                continue;
                            var box = await el.BoundingBoxAsync();
                            if (box == null || box.Width <= 0 || box.Height <= 0)
                                continue;
                            clickResult = await ClickAndDetectNavigationAsync(ctx, el, token);
                            if (clickResult.Navigated)
                            {
                                break;
                            }
                        }
                    }
                    else
                    {
                        var options = new ClickAreaOptions
                        {
                            MinXPercent = 0.1,
                            MaxXPercent = 0.9,
                            MinYPercent = 0.30,
                            MaxYPercent = 0.70,
                            StrictPreferredArea = false,
                            MaxCount = 50
                        };
                        var nodes = await PlaywrightClickableHelper.GetClickableNodesAsync(ctx.Page, options);
                        if (nodes.Count() > 0)
                        {
                            foreach (var node in nodes.Take(2).OrderByDescending(g => Guid.NewGuid()))
                            {
                                if (string.IsNullOrWhiteSpace(node.Selector))
                                    continue;
                                try
                                {
                                    var locator = ctx.Page.Locator(node.Selector).First;
                                    if (await locator.CountAsync() == 0)
                                        continue;
                                    clickResult = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                                    if (clickResult.Navigated)
                                    {
                                        break;
                                    }
                                }
                                catch
                                {
                                }
                            }
                        }
                    }

                }

                if (clickResult != null && clickResult.Navigated)
                {
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                    await ctx.Human.ScrollByIntentAsync(
                        ctx.Page!,
                        ctx.CdpSession!,
                        HumanActionIntent.Reading,
                        token);


                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                    var options = new ClickAreaOptions
                    {
                        MinXPercent = 0.1,
                        MaxXPercent = 0.9,
                        MinYPercent = 0.30,
                        MaxYPercent = 0.70,
                        StrictPreferredArea = false,
                        MaxCount = 50
                    };


                    var nodes = await PlaywrightClickableHelper.GetClickableNodesAsync(ctx.Page, options);
                    if (nodes.Count() > 0)
                    {
                        foreach (var node in nodes.Take(3).OrderByDescending(g => Guid.NewGuid()))
                        {
                            if (string.IsNullOrWhiteSpace(node.Selector))
                                continue;
                            try
                            {
                                var locator = ctx.Page.Locator(node.Selector).First;
                                if (await locator.CountAsync() == 0)
                                    continue;
                                clickResult = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                                if (clickResult.Navigated)
                                {
                                    break;
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }





        #endregion

        #region Test Branch

        /// <summary>
        /// 测试方法
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="entry"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task RunTestBranchAsync(WorkerRunContext ctx, EntryPreparationResult entry, CancellationToken token)
        {
            await Task.Delay(TimeSpan.FromSeconds(150), token);

        }

        #endregion

        #region Click Helpers

        /// <summary>
        /// 点击目标,处理弹窗
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="element"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<ClickResult> ClickAndDetectNavigationAsync(WorkerRunContext ctx, ILocator element, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                ctx.PagesCount = ctx.Context!.Pages.Count;
                ctx.CurrentPageUrl = ctx.Page!.Url;
                if (!await ctx.Human.ClickAsync(ctx.Page, ctx.CdpSession!, element, token))
                    return ClickResult.Fail();
                await Task.Delay(CommonHelper.RandomRange(50, 100), token);

                try
                {
                    await ctx.Page.WaitForURLAsync(
                        u => !u.Equals(ctx.CurrentPageUrl),
                        new PageWaitForURLOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 10000
                        });
                }
                catch (TimeoutException) { }

                if (ctx.Context.Pages.Count > ctx.PagesCount)
                {
                    ctx.Page = ctx.Context.Pages[^1];
                    ctx.CdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(ctx.Page);
                    await CDPHelper.InitCDPSession(ctx.CdpSession, ctx.Config.MaxTouchPoints);
                    return ClickResult.SuccessNewPage();
                }

                if (!ctx.Page.Url.StartsWith(ctx.CurrentPageUrl))
                    return ClickResult.SuccessSamePage();

                return ClickResult.NoNavigation();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return ClickResult.Fail();
            }
        }
        public async Task<ClickResult> ClickAndDetectNavigationAsync(WorkerRunContext ctx, IElementHandle element, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                ctx.PagesCount = ctx.Context!.Pages.Count;
                ctx.CurrentPageUrl = ctx.Page!.Url;
                if (!await ctx.Human.ClickAsync(ctx.Page!, ctx.CdpSession!, element, token))
                    return ClickResult.Fail();
                await Task.Delay(CommonHelper.RandomRange(50, 100), token);
                try
                {
                    await ctx.Page!.WaitForURLAsync(
                        u => !u.Equals(ctx.CurrentPageUrl),
                        new PageWaitForURLOptions
                        {
                            WaitUntil = WaitUntilState.DOMContentLoaded,
                            Timeout = 10000
                        });
                }
                catch (TimeoutException) { }

                if (ctx.Context.Pages.Count > ctx.PagesCount)
                {
                    ctx.Page = ctx.Context.Pages[^1];
                    ctx.CdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(ctx.Page);
                    await CDPHelper.InitCDPSession(ctx.CdpSession, ctx.Config.MaxTouchPoints);
                    return ClickResult.SuccessNewPage();
                }

                if (!ctx.Page.Url.StartsWith(ctx.CurrentPageUrl))
                    return ClickResult.SuccessSamePage();

                return ClickResult.NoNavigation();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return ClickResult.Fail();
            }
        }
        #endregion

    }
}
