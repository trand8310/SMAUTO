using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using PlaywrightHumanInput;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using SMAd;
using SMAd.LandingPolicy;
using SMAd.Models;
using SMAd.DeviceEmulation;
using SMAd.PlaywrightHumanInput;

namespace QTP.Plugins
{
    public sealed class SMAdTask : QTPServiceBase
    {


        private const string AliAppDownloadModalCloseSelector = ".androidOpenModal .closeBtn, .iosOpenModal .closeIcon";





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
        private BrowserRuntimeManager? _browserRuntime;
        private bool _ownsBrowserRuntime;
        public SMAdTask(
            IPlaywrightProvider playwrightProvider,
            TaskStatsAggregator aggregator, ChromiumSessionManager manager, AdeHelper adeHelper, ChineseNameGenerator nameGenerator, AppSettings appSettings, BrowserRuntimeManager? browserRuntime = null) : base(appSettings)
        {
            _playwrightProvider = playwrightProvider;
            _browserRuntime = browserRuntime;
            _aggregator = aggregator;
            _processManager = manager;
            _adeHelper = adeHelper;
            _nameGenerator = nameGenerator;
        }



        public async Task BrowseForAsync(WorkerRunContext ctx, int minTimes = 3, int maxTimes = 8, CancellationToken token = default)
        {
            //await ctx.human!.BrowseForAsync(
            //    ctx.Page!,
            //    ctx.CdpSession!,
            //    duration: TimeSpan.FromSeconds(CommonHelper.RandomRange(minSeconds, maxSeconds)),
            //    cancellationToken: token);

            await TryOptionalPageOperationAsync(ctx, "Browse", () => ctx.human!.BrowseTimesAsync(
                ctx.Page!,
                ctx.CdpSession!,
                minTimes: minTimes,
                maxTimes: maxTimes,
                cancellationToken: token), token);

        }

        public async Task BrowseForAsync(WorkerRunContext ctx, TimeSpan duration, CancellationToken token = default)
        {
            await TryOptionalPageOperationAsync(ctx, "Browse", () => ctx.human!.BrowseForAsync(
                ctx.Page!,
                ctx.CdpSession!,
                duration: duration,
                cancellationToken: token), token);
        }


        public async Task BrowseTimesAsync(WorkerRunContext ctx,
            int minTimes = 2,
            int maxTimes = 5,
            CancellationToken token = default)
        {
            await TryOptionalPageOperationAsync(ctx, "Browse", () => ctx.human!.BrowseTimesAsync(
                ctx.Page!,
                ctx.CdpSession!,
                minTimes: minTimes,
                maxTimes: maxTimes,
                cancellationToken: token), token);

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
        /// 处理页面元素
        /// </summary>
        /// <param name="page"></param>
        /// <param name="cdpSession"></param>
        /// <param name="token"></param>

        public void ProcessingPageElementTask(WorkerRunContext ctx, CancellationToken token)
        {
            if (Interlocked.Exchange(ref ctx.PageElementGuardStarted, 1) == 1)
                return;

            ctx.PageElementGuardTask = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                            var active = ctx.ActivePage;
                            var page = active?.Page;
                            var cdpSession = active?.Session;
                            if (page == null || cdpSession == null)
                                continue;

                            if (page.IsClosed)
                                continue;

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

                                var result = await GetActions(ctx).ExecuteAsync("CloseOverlay", async (b, ct) =>
                                {
                                    if (!ReferenceEquals(b, active)) return false;
                                    return await ctx.human.Engine.TapAsync(b.Page, b.Session, target, cancellationToken: ct);
                                }, null, token, (_, ct) => target.IsHiddenAsync().WaitAsync(ct), timeoutMs: 4000);
                                if (result.EffectVerified)
                                    LogWriteLine($"{Title}:ProcessingPageElementTask 已确认1688弹框关闭");
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
        private async Task ClearPageCloseBtn(IPage page, ICDPSession cdpSession)
        {
            try
            {
                //                var closeBtn = ctx.Page!.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new");
                var closeBtn = page.Locator(".androidOpenModal .closeBtn, .iosOpenModal .closeIcon");
                if (await closeBtn.CountAsync() > 0)
                {
                    var target = closeBtn.First;
                    if (await target.IsVisibleAsync())
                    {
                        await SmAdTouch.TapAsync(page, cdpSession, target);
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
        private async Task ClearSuccessTipNewCloseNew(IPage page, ICDPSession cdpSession)
        {
            try
            {
                var closeBtn = page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new,.androidOpenModal .closeBtn, .iosOpenModal .closeIcon");
                if (await closeBtn.CountAsync() > 0)
                {
                    var target = closeBtn.First;
                    if (await target.IsVisibleAsync())
                    {
                        await SmAdTouch.TapAsync(page, cdpSession, target);
                    }
                }
            }
            catch (Exception)
            {

            }
        }

        private static List<string> InitFPArgs(JToken taskArgs, int maxTouchPoints)
        {
            var result = new List<string>();
            uint fingerprint = 0;
            var dev_hash = Math.Abs($"{taskArgs.SelectToken("dev")}".GetHashCode());

            if (taskArgs.SelectToken("dev.fingerprint") != null)
            {
                fingerprint = taskArgs.SelectToken("dev.fingerprint").Value<uint>();
            }
            else
            {
                fingerprint = CommonHelper.RandomNumber();
            }



            #region 指纹参数设置

            /*
            --platform="Android" 
            --platform-version="14" 
            --full-version="123.0.6261.171" 
            --brand="XiaoMiBrowser" 
            --brand-version="19.1.40221" 
            --product-model="23227RK66C" 
            --fingerprint=2113123  
            --time-zone=Asia/Shanghai
            --webrtc-ip=62.210.125.97 
            --webgl-vendor="Qualcomm" 
            --webgl-renderer="Adreno (TM) 610"  
            --max-touch-points=5  
            --hardware-concurrency=8 
            --device-memory=8 
            --device-pixel-ratio=2.625 
            --screen-size=412,915 
            --screen-avail-size=412,915 
            --enable-rects-noise 
            --enable-image-noise 
            --enable-text-noise 
            --enable-font-noise
            --enable-audio-noise 
            --disable-pdf-viewer
            --touch-emulator-point=0,82
            --netinfo-type=wifi
            --netinfo-effective=4g
            --netinfo-rtt=0
            --enable-battery-charging
            --battery-level=1.0
            --battery-charging-time=0
            --battery-discharging-time=0
            --cookie-enabled
             */
            #endregion

            var gpu = taskArgs.SelectToken("dev.gpu").Value<string>();
            var vendor = taskArgs.SelectToken("dev.vendor").Value<string>();
            var useragent = taskArgs.SelectToken("dev.ua").Value<string>();

            var os = taskArgs.SelectToken("os").Value<int>();

            if (os == 2)
            {
                result.Add("--platform=\"iOS\"");
                result.Add("--screen-color-depth=32");
            }
            else if (os == 7)
            {
                result.Add("--platform=\"Windows\"");
            }
            else
            {
                result.Add("--platform=\"Android\"");
                result.Add("--screen-color-depth=24");
            }
            var make = taskArgs.SelectToken("dev.make")?.Value<string>().ToLower();

            var full_version = taskArgs.SelectToken("dev.full_version").Value<string>();
            var full_version_values = full_version.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries);

            result.Add($"--platform-version=\"{taskArgs.SelectToken("dev.osv").Value<string>()}\"");
            result.Add($"--full-version={full_version}");
            var brand = "Google Chrome";
            if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.brand")?.Value<string>()))
            {
                brand = taskArgs.SelectToken("dev.brand")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(make))
                {
                    result.Add($"--make=\"{make}\"");
                }
                result.Add($"--brand=\"{brand}\"");
                result.Add($"--brand-name=\"{brand}\"");
                if ((make ?? "default").GetHashCode() % 2 == 0)
                {
                    result.Add($"--disable-full-version-list");
                    result.Add($"--disable-brand-version-list");
                }
                if (make == "xiaomi")
                {
                    if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.brand_version")?.Value<string>()))
                        result.Add($"--brand-version=\"{taskArgs.SelectToken("dev.brand_version")?.Value<string>()}\"");
                }

                if (os == 1 || os == 2)
                {
                    if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.model")?.Value<string>()))
                    {
                        if (os == 1)
                        {
                            result.Add($"--product-model=\"{taskArgs.SelectToken("dev.model")?.Value<string>()}\"");
                        }
                    }
                }
            }
            else
            {
                result.Add($"--brand=\"{brand}\"");
            }






            result.Add($"--fingerprint={fingerprint}");
            var grease_cipher = Math.Abs(string.Join(".", full_version_values.Take(2)).GetHashCode()) % 65535;
            result.Add($"--ssl-grease-cipher={grease_cipher}");
            if (os == 1 || os == 2)
            {
                result.Add($"--netinfo-type={new string[] { "wifi", "4g" }[CommonHelper.RandomRange(0, 2)]}");
                result.Add($"--netinfo-effective=4g");
                result.Add($"--netinfo-rtt={CommonHelper.RandomRange(100, 300)}");
            }

            result.Add($"--force-webrtc-ip-handling-policy");
            result.Add($"--webrtc-ip-handling-policy=disable_non_proxied_udp");
            var isProxyMode = taskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            if (isProxyMode)
            {
                var realIp = taskArgs.SelectToken("realIp")?.Value<string>() ?? taskArgs.SelectToken("ipInfo.query")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(realIp))
                {
                    result.Add($"--webrtc-ip={realIp}");
                }
            }



            #region webgl
            //--webgl-vendor="Google Inc. (Qualcomm)" --webgl-renderer="ANGLE (Qualcomm, Adreno (TM) 750, OpenGL ES 3.2)" ^
            if (dev_hash % 2 == 0)
            {
                result.Add($"--webgl-vendor=\"Google Inc. ({vendor})\"");
                result.Add($"--webgl-renderer=\"ANGLE ({vendor}, {gpu}, OpenGL ES 3.2)\"");
            }
            else
            {
                result.Add($"--webgl-vendor=\"{vendor}\"");
                result.Add($"--webgl-renderer=\"{gpu}\"");
            }

            #endregion

            if (dev_hash % 2 == 0)
            {
                result.Add($"--geolocation-permission=allow");
            }
            else
            {
                result.Add($"--geolocation-permission=block");
            }


            result.Add($"--hardware-concurrency={(taskArgs.SelectToken("dev.cpu")?.Value<int>() ?? 8)}");

            var ram = taskArgs.SelectToken("dev.ram").Value<string>().Split(',', StringSplitOptions.RemoveEmptyEntries);
            int deviceMemory = Convert.ToInt32(ram[CommonHelper.RandomRange(0, ram.Length)].Trim());
            if (deviceMemory < 4) deviceMemory = 4;
            result.Add($"--device-memory={(deviceMemory > 8 ? 8 : deviceMemory)}");
            var js_memory_info = new string[] { "10000000|10000000|1136000000", "29400000|31200000|1130000000", "10000000|10000000|1136000000", "29400000|31200000|1130000000", "29400000|31200000|1130000000" };
            result.Add($"--js-memory-info=\"{js_memory_info[(dev_hash % 4)]}\"");
            if (os == 1 || os == 2)
            {
                result.Add($"--max-touch-points={maxTouchPoints}");
            }

            //--storage
            //268435456
            //2147483648
            //69250036530
            var storage = taskArgs.SelectToken("dev.storage").Value<long>() * 1024 * 1024 * 1024;
            var usage_storage = (long)Math.Ceiling(storage * (CommonHelper.RandomRange(30, 80) * 0.01));


            result.Add($"--storage-quota=\"0|{(storage - usage_storage)}\"");

            result.Add("--enable-rects-noise");
            result.Add("--enable-canvas-noise");
            result.Add("--enable-text-noise");
            result.Add("--enable-audio-noise");
            if (dev_hash % 2 == 0)
            {
                result.Add("--disable-pdf-viewer");
            }
            if (os == 1 || os == 2)
            {
                int level = CommonHelper.RandomRange(10, 101);
                if (new bool[] { false, false, true, false, false, true, false, false, true, false }[CommonHelper.RandomRange(0, 10)])
                {
                    result.Add($"--enable-battery-charging=1");
                    result.Add($"--battery-level={Convert.ToDecimal((level * 0.01).ToString("f2"))}");
                    result.Add($"--battery-charging-time=0");
                    result.Add($"--battery-discharging-time=0");
                }
                else
                {
                    result.Add($"--enable-battery-charging=0");
                    result.Add($"--battery-level={Convert.ToDecimal((level * 0.01).ToString("f2"))}");
                    result.Add($"--battery-charging-time=0");
                    result.Add($"--battery-discharging-time=0");
                }
            }

            return result;
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
            => (await ExecuteWorkerWithResultAsync(uniqueId, taskArgs, token)).ToLegacyTuple();

        private static WorkerExecutionResult BuildWorkerResult(
            WorkerRunContext? ctx, bool success = false, bool canceled = false, string? fallbackReason = null)
        {
            string? reason = null;
            if (ctx?.ProxyFailed == true) success = false;
            if (!success)
            {
                reason = canceled ? fallbackReason ?? "任务已取消"
                    : ctx?.ProxyFailed == true ? ctx.ProxyFailedReason ?? "代理异常"
                    : ctx?.PageCrashed == true ? ctx.LastFailureReason ?? "页面崩溃"
                    : ctx?.LastFailureReason;
                if (string.IsNullOrWhiteSpace(reason)) reason = fallbackReason ?? "业务流程未完成";
            }
            return new WorkerExecutionResult(
                canceled ? WorkerExecutionStatus.Canceled
                    : success ? WorkerExecutionStatus.Succeeded : WorkerExecutionStatus.Failed,
                ctx?.PageTriggerClick == true || ctx?.CompletedClickPvs > 0, ctx?.PageAdsCount ?? 0, reason)
            { CompletedPvs = ctx?.CompletedPvs ?? 0, ActionLogPath = ctx?.ActionLogPath,
                ClickRequested = ctx?.ClickRequested ?? false, CompletedClickPvs = ctx?.CompletedClickPvs ?? 0,
                ProxyFailure = ctx?.ProxyFailure };
        }

        private BrowserReclaimResult? _lastBrowserReclaim;
        private BrowserStopKind? _lastBrowserFailure;
        public override async Task<WorkerExecutionResult> ExecuteWorkerWithResultAsync(string uniqueId, JObject taskArgs, CancellationToken token)
        {
            _lastBrowserReclaim = null; _lastBrowserFailure = null;
            var result = await ExecuteWorkerCoreAsync(uniqueId, taskArgs, token);
            return result with
            {
                BrowserReclaim = _lastBrowserReclaim,
                BrowserStopKind = _lastBrowserReclaim?.Reason ?? _lastBrowserFailure,
                Status = _lastBrowserReclaim?.ExitConfirmed == false ? WorkerExecutionStatus.Failed : result.Status,
                FailureReason = _lastBrowserReclaim?.ExitConfirmed == false ? result.FailureReason ?? "Browser process exit not confirmed" : result.FailureReason
            };
        }
        private async Task<WorkerExecutionResult> ExecuteWorkerCoreAsync(string uniqueId, JObject taskArgs, CancellationToken token)
        {
            WorkerRunContext? ctx = null;
            bool flowSucceeded = false;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            try
            {
                var config = BuildTaskConfig(uniqueId, taskArgs, linkedCts);

                ctx = new WorkerRunContext(config)
                {
                    LandingDispatcher = new LandingPageStrategyDispatcher(new ILandingPageStrategy[]
                    {
                        new UMobLandingPageStrategy(this),
                        new AiSiteLandingPageStrategy(this),
                        new AiStudyLandingPageStrategy(this),
                        new AliLandingPageStrategy(this),
                        new BaiduB2BLandingPageStrategy(this),
                        new DefaultLandingPageStrategy(this),
                    })
                };

                this.QTPExecuteStart(config.TaskId);
                LogWriteLine($"{this.Title}:ExecuteWorker:Start");


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

                    return BuildWorkerResult(ctx, fallbackReason: "浏览器启动或 CDP 连接失败");
                }

                ctx.Browser = browser;

                if (!ctx.Browser.IsConnected)
                {
                    ctx.LastFailureReason = "Browser.IsConnected == false";
                    LogWriteLine($"{this.Title}:ExecuteWorker:Browser未连接: {ctx.ProxyFailedReason}");
                    return BuildWorkerResult(ctx);
                }

                if (ctx.Browser.Contexts == null || ctx.Browser.Contexts.Count == 0)
                {
                    ctx.LastFailureReason = "Browser.Contexts.Count == 0";
                    LogWriteLine($"{this.Title}:ExecuteWorker:Browser无可用Context: {ctx.ProxyFailedReason}");
                    return BuildWorkerResult(ctx);
                }

                ctx.Context = ctx.Browser.Contexts[0];
                ctx.CdpManager = new CDPSessionManager(ctx.Context);

                await AttachLifecycleEventsAsync(ctx, linkedCts.Token);
                await ConfigureContextAsync(ctx, linkedCts.Token);

                if (ctx.ProxyFailed)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:初始化阶段已判定代理异常: {ctx.ProxyFailedReason}");
                    return BuildWorkerResult(ctx);
                }

                if (ctx.PageCrashed)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:初始化阶段页面崩溃: {ctx.LastFailureReason}");
                    return BuildWorkerResult(ctx);
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
                flowSucceeded = ok;
                return BuildWorkerResult(ctx, success: ok);
            }
            catch (OperationCanceledException)
            {
                if (token.IsCancellationRequested)
                    return BuildWorkerResult(ctx, canceled: true);

                if (ctx?.ProxyFailed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled(代理异常): {ctx.ProxyFailedReason}");
                    return BuildWorkerResult(ctx);
                }

                if (ctx?.PageCrashed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled(页面崩溃): {ctx.LastFailureReason}");
                    return BuildWorkerResult(ctx);
                }

                if (ctx != null && !string.IsNullOrWhiteSpace(ctx.LastFailureReason))
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled: {ctx.LastFailureReason}");
                    return BuildWorkerResult(ctx);
                }

                LogWriteLine($"{this.Title}:ExecuteWorker:Canceled");
                return BuildWorkerResult(ctx, fallbackReason: "执行被内部取消");
            }
            catch (PlaywrightException ex)
            {
                if (ctx != null && BrowserFailureClassifier.Classify(ex) == BrowserStopKind.OperationTimedOut)
                    ctx.BrowserFailureKind = BrowserStopKind.OperationTimedOut;
                if (ctx != null)
                    ctx.LastFailureReason = ex.Message;

                if (ctx?.ProxyFailed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:PlaywrightException(代理异常): {ctx.ProxyFailedReason}");
                    return BuildWorkerResult(ctx);
                }

                if (ctx?.PageCrashed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:PlaywrightException(页面崩溃): {ctx.LastFailureReason}");
                    return BuildWorkerResult(ctx);
                }

                LogWriteLine($"{this.Title}:ExecuteWorker:PlaywrightException: {ex}");
                return BuildWorkerResult(ctx, fallbackReason: ex.Message);
            }
            catch (Exception ex)
            {
                if (ctx != null && BrowserFailureClassifier.Classify(ex) == BrowserStopKind.OperationTimedOut)
                    ctx.BrowserFailureKind = BrowserStopKind.OperationTimedOut;
                if (ctx != null)
                    ctx.LastFailureReason = ex.Message;

                if (ctx?.ProxyFailed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:异常(代理异常): {ctx.ProxyFailedReason}");
                    return BuildWorkerResult(ctx);
                }

                if (ctx?.PageCrashed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:异常(页面崩溃): {ctx.LastFailureReason}");
                    return BuildWorkerResult(ctx);
                }

                LogWriteLine(ex.ToString());
                return BuildWorkerResult(ctx, fallbackReason: ex.Message);
            }
            finally
            {
                // Record the end reason before cancelling the internal cleanup token.
                ctx?.BrowserSession?.RequestStop(token.IsCancellationRequested ? BrowserStopKind.Canceled
                    : !flowSucceeded ? ctx.BrowserFailureKind ?? BrowserStopKind.Completed : BrowserStopKind.Completed);
                try
                {
                    if (!linkedCts.IsCancellationRequested)
                        linkedCts.Cancel();
                }
                catch
                {
                }

                if (ctx?.BrowserSession != null)
                {
                    try { await ctx.BrowserSession.DisposeAsync(); }
                    catch (Exception ex) { LogWriteLine($"Browser session cleanup failed: {ex.Message}"); }
                    foreach (var error in ctx.BrowserSession.CleanupErrors)
                        LogWriteLine($"Browser session cleanup warning: {error.Message}");
                    _lastBrowserReclaim = ctx.BrowserSession.ReclaimResult;
                }
                else if (ctx != null) await CleanupPageSessionAsync(ctx);
                if (_ownsBrowserRuntime && _browserRuntime != null)
                {
                    try { await _browserRuntime.DisposeAsync(); }
                    catch (Exception ex) { LogWriteLine($"Browser runtime cleanup failed: {ex.Message}"); }
                }
            }
        }
        #endregion

        #region Main Flow

        private async Task<bool> RunMainFlowAsync(WorkerRunContext ctx, CancellationToken token)
        {
            if (ctx.Config.TotalPV <= 0)
            { ctx.LastFailureReason = "No page visits requested"; return false; }

            string brand = ctx.Config.TaskArgs.SelectToken("dev.make")?.Value<string>() ?? "";
            string model = ctx.Config.TaskArgs.SelectToken("dev.model")?.Value<string>() ?? "";


            // 1. 固定 Seed
            var device = ctx.Config.TaskArgs["dev"] as JObject
                ?? throw new ArgumentException("任务参数缺少 dev 设备对象");
            int accountSeed = StableSeed.Create(device);
            var user = HumanUserProfile.CreateRandom(
            seed: accountSeed,
            handedness: HumanHandedness.Right);
            // 2) 不指定 sessionSeed：每次启动都会生成新的会话随机序列。
            //    因此长期画像一致，但不会重启后重复同一套轨迹。
            var session = new HumanTouchSession(
                user,
                brand,
                model,
                desktopCdp: true);

            ctx.human = new HumanTouchOperator(new HumanTouchOperatorOptions
            {
                Session = session,
                DelayFactor = 1.0,
                AllowBackReview = true,
                EnablePageContextAwareness = true,
                PageContextRefreshEveryGestures = 1,
                Log = message => LogWriteLine($"{Title}:Touch {message}")
            });


            ctx.human.Engine.IsActivePage = p => ReferenceEquals(ctx.Page, p);
            ctx.human.Engine.InputExecuted += input => ctx.RecordAction(new(
                DateTimeOffset.UtcNow, input.Kind, input.Page.Url, input.Page.Url, true,
                input.EffectVerified, input.DurationMs,
                null, input.EffectVerified ? ClickOutcome.VerifiedEffect : ClickOutcome.NoEffect, input.Trace));
            foreach (var page in ctx.Context!.Pages)
            {
                var cdp = await ctx.CdpManager!.GetOrCreateSessionAsync(page);
                SmAdTouch.Bind(page, cdp, token, ctx.human);
            }

            int secondJumpRate = 0;
            if (!string.IsNullOrWhiteSpace(_appSettings.SecondJumpRate))
            {
                if (_appSettings.SecondJumpRate.Contains("-"))
                {
                    var values = _appSettings.SecondJumpRate.Split('-');
                    if (values.Length == 2)
                        secondJumpRate = CommonHelper.RandomRange(Int32.Parse(values[0]), Int32.Parse(values[1]));
                }
                else
                {
                    secondJumpRate = Int32.Parse(_appSettings.SecondJumpRate);
                }
            }
            else
            {
                secondJumpRate = CommonHelper.RandomRange(45, 75);
            }

            if (secondJumpRate > 0)
            {
                if (_aggregator.GetLocalMetric(ctx.Config.TaskId, "dsp_second_jump_rate") == 0)
                {
                    _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_second_jump_rate", secondJumpRate);
                }
            }


            for (ctx.PvIndex = 1; ctx.PvIndex <= ctx.Config.TotalPV; ctx.PvIndex++)
            {
                token.ThrowIfCancellationRequested();
                LogWriteLine($"{this.Title}:pv：{ctx.Config.TotalPV}/{ctx.PvIndex}");
                ctx.ResetPerPvState();
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
                    { ctx.LastFailureReason = "Entry preparation failed"; return false; }

                    continue;
                }


                if (ctx.Config.IsTest)
                {
                    //entry.FirstPageUrl = "https://wm.m.sm.cn/s?from=10000&q=塑料";
                    //entry.FirstPageUrl = "https://pro.m.jd.com/mall/active/KtpmHjYN5sC8vyEfvBSesVjwn9Z/index.html?babelChannel=ttt12";
                    //entry.FirstPageUrl = "https://pro.m.jd.com/mall/active/27cGVLCp2Rk5UAemjMvigeJXok9/index.html?babelChannel=ttt1&hy_entry=UC_SearchSkin";
                    //entry.FirstPageUrl = "https://m.1688.com/zw/hamlet.html?scene=8&q=%E7%AF%AE%E7%90%83%E8%B6%B3%E7%90%83&imgurl=img/ibank/O1CN014k1XW01LMa13eBYoI_!!2207873421285-0-cib.jpg&cosite=smjj&keywordid=74320369958&trackid={}&format=shandian&bd_vid=11084568593119754510&outerId=618324461983&creative=50000002313693958&trackid=88585857717827007619670&clickid=11084568593119754510&uctrackid=czoxMTY5NjMwNTUyNjMzNDM1MDE2MTtjOjUwMDAwMDAyMzEzNjkzOTU4O2Q6ZG1wXy01NjI5MzQyMTI1NDM3MjIyOTQ4O3A6d2w=&flowfrom=shenma";
                    //entry.FirstPageUrl = "https://m.1688.com/zw/hamlet.html?scene=3&q=%E5%A1%91%E6%96%99%E6%A8%A1%E5%85%B7%E5%A4%9A%E5%B0%91%E9%92%B1&cosite=smjj&trackid=885827136664257764685798&format=normal&location=landing_t4&m_k=80038854275&m_clk=15542951353857784139&m_q=%E5%A1%91%E6%96%99&m_ac=210412920&m_p=124655212&m_a=1523902729&m_c=50000002440881896&d11=&d22=&d12=&d23=&clickid=15542951353857784139&uctrackid=czoxMzQzNDI3MzM1MTU0OTc2Nzg1NztjOjUwMDAwMDAyNDQwODgxODk2O2Q6ZG1wXy0zODE5MDc0MTIxNTI1ODQ4NjMwO3A6d2w=&flowfrom=shenma";
                    //entry.FirstPageUrl = "https://pro.m.jd.com/mall/active/6PRJiy2LHsUc6oezS9u5rjfYqmj/index.html";
                    //entry.FirstPageUrl = "https://ada.baidu.com/site/wjzil0aoc/agent?imid=0e6e62a63da5f8b552f4c1cfa0e24a24&wid=4b534c47-561f-4f2a-3d1e-1773718306115_0_0#QD=BDHYYF2-HEBAO&bd_vid=Pjn1nj6drH6knHcYn1bkP1Tkg1cznW-xnNtknjKxP7tkn16dnjm4PWDLnW6&fid=Pjn1nj6drH6knHcYn1bkP1Tkg1cznW-xnf&ch=4&bd_bxst=EiaKyOnXEhX906pda0DD0n_FVfHh0cjI00000KQ0leEGkEjQLqHdseHfVnExdef0000000000000ReKnmkRf8iDj0000fcrC5z0000jBLvzx5fD00Kn0560ikEjQLtjo8ShzknZ5d5gjVPaYQtUszqO0leEG__HK1qHdseHfV7OAtnExsr8elTHTkIj0ltQs_UldvnQFzJpq3oHs000005OOOOOOOOOOmtdeXs/merchant_bot_layer";
                    //entry.FirstPageUrl = "https://cunliangtech.com/getTwo2/jiaoyu/30/538y9i3v.html?bd_vid=9456839952995755725";
                    //entry.FirstPageUrl = "https://site.u-mob.cn/211562631/7489236/25120851a4df3de2f64bf2874399e5322bf9ba.html?uctrackid=czo2NzU1ODEyMDY1NjUzNDk4MjQ7Yzo1MDAwMDAwMjQ1MzEwMzcxODtkOmRtcF81MDAyNzcxNjQyNzUzMzI3MzY0O3A6d2w=&keyword=%E4%B8%AD%E5%9B%BD%E9%BB%84%E9%87%91%E6%8A%95%E8%B5%84%E7%BD%91&query=%E7%8E%B0%E5%9C%A8%E4%B9%B0%E9%BB%84%E9%87%91%E6%8A%95%E8%B5%84%E6%80%8E%E4%B9%88&codedip=118%2E249%2E20%2E238&regioncode=17957122#/jinfan/page0";
                    //entry.FirstPageUrl = "https://www.louisvuitton.cn/zhs-cn/homepage";
                    //entry.FirstPageUrl = "https://www.ncpjy.cn/content.html?q=%E7%A0%94%E7%A9%B6%E7%94%9F&keywordid=1361648768492&site=23&bd_vid=11661194032580761324";
                    //entry.FirstPageUrl = "http://prom.sjk520.top/db_p_h5/v1/keysearch.html?app_id=9001&content_id=50700164&keyword=%E5%92%A8%E8%AF%A2%E5%85%AC%E5%8F%B8&plan=4&bd_vid=8521736992881948758";
                    //entry.FirstPageUrl = "https://aisite.wejianzhan.com/site/wjzsorv8/8fde5eff-530e-43ad-a8be-37ab96c77d4b?q=AI%E5%9F%B9%E8%AE%AD&pm_key=47622062&multi_key=5_211314986_70005&page_scene=48&bword=%E5%B9%B3%E9%9D%A2%E8%AE%BE%E8%AE%A1ai%E8%BD%AF%E4%BB%B6%E6%95%99%E7%A8%8B&intent=%E5%AD%A6%E4%B9%A0%E6%9C%9F-1&adGroupId=124118580&campaignId=1501472115&planname=20250423_%E7%A5%9E%E9%A9%AC_ocpc_AI%E5%9F%B9%E8%AE%AD_wise&kid=-1&ip=113.121.217.233&clickid=18286375348828523544&uctrackid=czo3MjU4OTY0ODE0NDk2MDMyMjA3O2M6NTAwMDAwMDIzODMwMTY1Nzg7ZDpkbXBfMzk4MTAwNDA5MDE3NjMzNzk5OTtwOnds&flowfrom=shenma&wid=19669bb7138d4ce3834a9f198b6ff99e_0_0#showRetainPopup";
                    //entry.FirstPageUrl = "https://b2b.baidu.com/m/aitf/s?q=%E6%89%8B%E6%9C%BA%E6%9D%A1%E7%A0%81%E6%89%AB%E6%8F%8F%E5%99%A8&fid=519938827&styl=b&sid=90311_811014_70004_70027&a_keywordid=77982777850&creativeId=50000002365855081&clickid=5426022486127520210&uctrackid=czoxNjQzNjA1NTU1MTExNzcyNDUyMTtjOjUwMDAwMDAyMzY1ODU1MDgxO2Q6ZG1wXy02NjAzMDY3MTY1MjQ2NTA3NzY3O3A6d2w=&flowfrom=shenma";
                    //entry.FirstPageUrl = "https://wm.m.sm.cn/s?from=wm100000&q=%E6%9C%89%E6%B2%A1%E6%9C%89%E7%90%86%E8%B4%A2%E7%9A%84%E8%BD%AF%E4%BB%B6";
                    //entry.FirstPageUrl = "https://wm.m.sm.cn/s?from=wm100000&q=9game";
                    //entry.FirstPageUrl = "https://b2b.baidu.com/m/aitf/s?q=24k%E9%95%80%E9%87%91%E5%9B%9E%E6%94%B6%E4%BB%B7%E6%A0%BC&fid=519938828&styl=b&sid=90311_811015_70000_70019&a_keywordid=75706230683&creativeId=50000002335958907&clickid=180377737532562088&uctrackid=czo0NzE4MTM4Mjk1NDk5NzQ4Mjg3O2M6NTAwMDAwMDIzMzU5NTg5MDc7ZDpkbXBfLTgzNjI1MDg1MzY1MjE5Mzg5OTg7cDp3bA==&flowfrom=shenma\r\n";
                    //entry.FirstPageUrl = "https://so.m.sm.cn/s?q=鱿鱼游戏&from=751111&safe=1&by=suggest&snum=6";
                    //entry.FirstPageUrl = "https://m.1688.com///_____tmd_____/punish?x5secdata=xf86Wdfu_WkBrkNgrkvOe0eXAoOUDbQAO89fQ0aNI2Blp-KnxXlfyiKRTCqq_PdAaQfhVWzwaFtQsA7CZOnO48Uzi6kKFOHkhYUf2D_VE8cBFh9Yd_8-6BEdES8McRTNkj4Wn-EAZKhDJdLzn2vscZ5iHAQvIACc7u_xc368YHkSnRCw-wrlFWCJSR_HAiSuGfCJaJWPFAbVreKS7QYOLRpcKuF4NRtd7ZedbLYY_FXN1_9sPges-2uYcZt1Y_huvuxUJairOPmv0b7yBdfBT-LiJ_vGq6R2sxwCmpVxYfeSzaM7R_pcLWKPn_859ZXPIFCoiq4ZlebxU0OREPlnCEQB2WkRbtQK_FIiwSsmFsLI9xLi4B1A-5_pFhMJeW4Ix-6SySYtLSYhO52qUmOut4ZIODQQkIxN4QlUghTVExMpVFz-sgbtD4lWHzBmA402fGV_FesadRCCCW1L0-avEkZwECU2U6cJv_FMqzUtb5WEoMjweXbCnMzJyDFX8aXTF70qfn6DBSen0rUkE77MzZ3C03GReDPJvCTIzSP7dE5g6kAwiFOliNJyqg9B-rLZgsrpryBTqOrT8yjQhbujLseX511AbcFl_KzR-oJyGR672iD5UnuVm1ctWJ-LpdTcVOLRaXFxHxzHjWLa3D-rGNlhOaEta4qMqERkPLqg5zZ9U__bx__m.1688.com%2f&x5step=1";
                    //entry.FirstPageUrl = "https://www.jqlive16.cc";
                    //entry.FirstPageUrl = "https://m.p4psearch.1688.com/page.html?spm=a2638t.27966843.0.0.67b6436csKR08G&q=%E8%A1%A3%E6%9C%8D%E5%A5%B3%E6%AC%BE&exp=wxReListExp:C;wxCpxGuessExp:B&hpageId=wx-list-v3";
                    //entry.FirstPageUrl = "https://www.louisvuitton.cn/zhs-cn/men/accessories/belts/_/N-t1g9dx5w?utm_source=shenma&utm_medium=cpc&utm_campaign=A1_W_OT_E_BZ_BZ_M_E_AO_RTOMNI&utm_term=MAIN-DES3";
                    //entry.FirstPageUrl = "https://abrahamjuliot.github.io/creepjs/";
                    //entry.FirstPageUrl = "https://pixelscan.net/fingerprint-check";
                    //entry.FirstPageUrl = "https://so.m.sm.cn/s?q=%E9%B1%BF%E9%B1%BC%E6%B8%B8%E6%88%8F&from=751111&safe=1&by=suggest&snum=6";
                    entry.FirstPageUrl = "https://abrahamjuliot.github.io/creepjs";
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
                ctx.CurrentVisitReady = true;


                if (ctx.Config.IsTest)
                {
                    await RunTestBranchAsync(ctx, entry, token);
                    ctx.CompleteCurrentVisit();
                    return CompleteSuccess(ctx);
                }

                if (ctx.Page == null || ctx.Page.IsClosed)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: 导航后 Page为空或已关闭");
                    continue;
                }

                if (ctx.Page.Url.Contains("punish?x5secdata"))
                {
                    this.X5Secdata(ctx.Config.TaskId, 1, ctx.Page.Url);
                    ctx.LastFailureReason = "Entry page requires verification"; return false;
                }

                if (entry.IsHomepageTrigger)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker: ExecuteHomepageTriggerAsync");
                    var homepageOk = false;
                    await TryOptionalPageOperationAsync(ctx, "Homepage search", async () =>
                        homepageOk = await ExecuteHomepageTriggerAsync(ctx, entry.QueryWord, token), token);
                    if (!homepageOk)
                        LogWriteLine("搜索动作未完成，继续当前页面访问与停留");
                }
                else
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker: {((ctx.Config.PageLoadedDelayMs) / 1000.0):N2}");

                    await Task.Delay(Math.Max(0, ctx.Config.PageLoadedDelayMs), token);
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

                var adsOk = await DetectAndUploadAdWordsAsync(ctx, entry.QueryWord, token);
                if (adsOk && ctx.PageAdsCount == 0)
                {
                    token.ThrowIfCancellationRequested();
                    ctx.Config.LinkedCts.Token.ThrowIfCancellationRequested();
                    ctx.CompleteCurrentVisit();
                    LogWriteLine("没有广告标记，快速完成当前访问，跳过滑动、点击和停留");
                    continue;
                }
                if (!adsOk)
                    LogWriteLine("广告检测未完成，继续当前页面访问与停留");

                await BrowseForAsync(ctx, 5, 8, token);
                await Task.Delay(CommonHelper.RandomRange(3500, 8500), token);

                if (ctx.Page == null || ctx.Page.IsClosed)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: DecideJumpClick前 Page为空或已关闭");
                    continue;
                }

                if (ctx.Browser == null || !ctx.Browser.IsConnected)
                {
                    LogWriteLine($"{this.Title}:RunMainFlow: DecideJumpClick前 Browser已断开");
                    continue;
                }
                await DecideJumpClickAsync(ctx, token);
                if (ctx.JumpClick)
                {
                    ctx.ClickRequested = true;
                    var clickFlow = FlowControl.Continue;
                    await TryOptionalPageOperationAsync(ctx, "First jump", async () =>
                    {
                        await ctx.human!.SwipeByIntentAsync(
                            ctx.Page!,
                            ctx.CdpSession!,
                            SwipeIntent.Reading,
                            token);

                        clickFlow = await TryExecuteJumpClickAsync(ctx, token);
                    }, token);
                    if (clickFlow == FlowControl.EndTask)
                    {
                        ctx.CompleteCurrentVisit();
                        return CompleteSuccess(ctx);
                    }
                }

                if (ctx.Page != null && ctx.PageHttpStatuses.TryGetValue(ctx.Page, out var documentStatus) && documentStatus >= 400)
                {
                    LogWriteLine($"目标页 HTTP {documentStatus}，跳过当前PV停留及计数");
                    continue;
                }
                var sleepFlow = await ExecuteTaskSleepPhaseAsync(ctx, token);
                if (sleepFlow == FlowControl.Failed) return false;
                token.ThrowIfCancellationRequested();
                ctx.Config.LinkedCts.Token.ThrowIfCancellationRequested();
                ctx.CompleteCurrentVisit();
                if (sleepFlow == FlowControl.EndTask)
                    return CompleteSuccess(ctx);

                if (sleepFlow == FlowControl.NextPv)
                {
                    continue;
                }
                return CompleteSuccess(ctx);
            }

            if (ctx.CompletedPvs == 0)
            { ctx.LastFailureReason ??= "No PV completed successfully"; return false; }
            return CompleteSuccess(ctx);
        }


        private bool CompleteSuccess(WorkerRunContext ctx)
        {
            if (ctx.ProxyFailed) return false;
            if (ctx.CompletedPvs == 0)
            { ctx.LastFailureReason ??= "No confirmed page outcome"; return false; }
            this.QTPExecuteSuccess(ctx.Config.TaskId);
            this.QTPExecuteComplete(ctx.Config.TaskId);
            LogWriteLine($"{this.Title}:ExecuteWorker:Complete");
            return true;
        }

        #endregion

        #region Config / Context

        private TaskConfig BuildTaskConfig(string uniqueId, JObject taskArgs, CancellationTokenSource linkedCts)
        {

            var os = taskArgs.SelectToken("os")!.Value<int>();
            var make = taskArgs.SelectToken("dev.make")?.Value<string>() ?? "default";
            var model = taskArgs.SelectToken("dev.model")?.Value<string>() ?? "default";
            var sw1 = taskArgs.SelectToken("dev.sw")?.Value<int>() ?? 1080;
            var sh1 = taskArgs.SelectToken("dev.sh")?.Value<int>() ?? 1920;
            float deviceScale = 1.0f;
            int sw = 0;
            int sh = 0;
            if (os == 1 || os == 2)
            {
                var profileResult = AndroidViewportMatcher.Match(sw1, sh1, make, model);
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



            var explicitDpr = taskArgs.SelectToken("dev.dpr")?.Value<float>();
            if (explicitDpr.HasValue)
            {
                if (!float.IsFinite(explicitDpr.Value) || explicitDpr.Value <= 0)
                    throw new ArgumentOutOfRangeException("dev.dpr", "DPR must be finite and positive.");
                deviceScale = explicitDpr.Value;
                sw = checked((int)Math.Round(sw1 / (double)deviceScale, MidpointRounding.AwayFromZero));
                sh = checked((int)Math.Round(sh1 / (double)deviceScale, MidpointRounding.AwayFromZero));
            }
            sw = taskArgs.SelectToken("dev.cssWidth")?.Value<int>() ?? sw;
            sh = taskArgs.SelectToken("dev.cssHeight")?.Value<int>() ?? sh;

            var maxTouchPoints = os == 1 || os == 2 ? CommonHelper.RandomRange(5, 6) : 0;

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
                IsLocalAdWord = taskArgs.SelectToken("isLocalAdWord")?.Value<bool>() ?? false,
                PageLoadingTimeoutMs = taskArgs.SelectToken("pageLoadingTimeout")?.Value<int>() * 1000 ?? 30000,
                PageLoadedDelayMs = ParsePageLoadedDelayMilliseconds(taskArgs),
                HomepageTrigger = taskArgs.SelectToken("hompageTrigger")?.Value<int>() ?? 0,
                PriorityNon1688 = taskArgs.SelectToken("priorityNon1688")?.Value<bool>() ?? false,
                UserAgent = taskArgs.SelectToken("dev.ua")!.Value<string>(),
                Os = os,
                DeviceScale = deviceScale,
                Sw = sw,
                Sh = sh,
                ScreenWidth = sw1,
                ScreenHeight = sh1,
                WordName = taskArgs.SelectToken("wordname")?.Value<string>() ?? "default",
                NoTrigger1688 = taskArgs.SelectToken("noTrigger1688")?.Value<bool>() ?? false,
                CleaningWords = taskArgs.SelectToken("cleaningWords")?.Value<bool>() ?? false,
                NotTriggerDownload = taskArgs.SelectToken("notTriggerDownload")?.Value<bool>() ?? false,
                PvsTriggerOne = taskArgs.SelectToken("pvsTriggerOne")?.Value<bool>() ?? true,
                CurrentUV = taskArgs.SelectToken("currentUV")?.Value<int>() ?? 0,

                KernelVersion = kernelVersion,
                MaxTouchPoints = maxTouchPoints,
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
            var args = BuildChromiumArgs(ctx.Config, out var proxyServer);
            var chromePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "File", "chrome-win", ctx.Config.KernelVersion, "chrome.exe");
            if (_browserRuntime == null)
            {
                _browserRuntime = new(_playwrightProvider, _processManager, new(Math.Max(1, _appSettings.MaximumConcurrency),
                    Math.Max(1, _appSettings.BrowserLaunchConcurrency)));
                _ownsBrowserRuntime = true;
            }
            BrowserRuntimeLease lease;
            try
            {
                lease = await _browserRuntime.AcquireAsync(new(ctx.Config.UniqueId, chromePath, ctx.Config.UserDataDir,
                    TimeSpan.FromSeconds(_appSettings.IpTtl), $"about:blank {string.Join(" ", args)}", proxyServer,
                    TimeSpan.FromSeconds(15)), token);
            }
            catch (Exception ex) { _lastBrowserFailure = BrowserFailureClassifier.Classify(ex); throw; }
            ctx.BrowserSession = lease;
            ctx.Playwright = lease.Playwright;
            ctx.DebugPort = lease.ProcessSession.DebugPort;
            var registration = lease.Token.Register(() =>
            {
                ctx.LastFailureReason ??= lease.StopReason ?? "Browser session stopped";
                try { ctx.Config.LinkedCts.Cancel(); } catch (ObjectDisposedException) { }
            });
            lease.RegisterCleanup(async () =>
            {
                registration.Dispose();
                await CleanupPageSessionAsync(ctx);
            });
            return lease.Browser;
        }

        private async Task CleanupPageSessionAsync(WorkerRunContext ctx)
        {
            var errors = new List<Exception>();
            if (ctx.DeviceTargets != null)
            {
                try { await ctx.DeviceTargets.DisposeAsync(); }
                catch (Exception ex) { errors.Add(ex); }
            }
            try { await Task.WhenAll(ctx.PageLifecycleTasks.Keys).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception ex) { LogWriteLine($"Page initialization cleanup: {ex.Message}"); }
            if (ctx.PageElementGuardTask != null)
            {
                try { await ctx.PageElementGuardTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception ex) { LogWriteLine($"Page guard cleanup: {ex.Message}"); }
            }
            if (ctx.CdpManager != null)
            {
                try { await ctx.CdpManager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (Exception ex) { errors.Add(ex); }
            }
            if (errors.Count > 0) throw new AggregateException("Page and device CDP cleanup failed.", errors);
        }

        private List<string> BuildChromiumArgs(TaskConfig config, out string proxyServer)
        {


            var scaleX = config.TaskArgs.SelectToken("scaleX")?.Value<float>() ?? 1.0;
            var scaleY = config.TaskArgs.SelectToken("scaleY")?.Value<float>() ?? 1.0;

            var display = config.DisplayProfile ??= DeviceDisplayProfile.From(config);

            var args = (new List<string>
            {
                "--disable-field-trial-config",
                "--disable-background-networking",
                "--disable-background-timer-throttling",
                "--disable-backgrounding-occluded-windows",
                "--disable-breakpad",
                "--no-default-browser-check",
                "--disable-dev-shm-usage",
                "--disable-edgeupdater",
                "--disable-features=AvoidUnnecessaryBeforeUnloadCheckSync,BoundaryEventDispatchTracksNodeRemoval,DestroyProfileOnBrowserClose,DialMediaRouteProvider,GlobalMediaControls,HttpsUpgrades,LensOverlay,MediaRouter,PaintHolding,ThirdPartyStoragePartitioning,Translate,AutoDeElevate,RenderDocument,OptimizationHints,msForceBrowserSignIn,msEdgeUpdateLaunchServicesPreferredVersion,DnsOverHttps,UseDnsHttpsSvcbAlpn",
                "--enable-features=CDPScreenshotNewSurface",
                "--disable-hang-monitor",
                "--disable-prompt-on-repost",
                "--disable-renderer-backgrounding",
                "--force-color-profile=srgb",
                "--no-first-run",
                "--password-store=basic",
                "--use-mock-keychain",
                "--no-service-autorun",
                "--export-tagged-pdf",
                "--disable-search-engine-choice-screen",
                "--edge-skip-compat-layer-relaunch",
                "--disable-infobars",
                "--disable-sync",
                "--disable-blink-features=AutomationControlled",
                "--disable-logging",
                "--disable-quic",
                "--use-fake-ui-for-media-stream",
                "--use-fake-device-for-media-stream",
                "--enable-unsafe-swiftshader",
                "--show-avatar-button=never",
                "--disable-http2-grease-settings",
                "--hide-bad-flags",
                "--hide-crashed-bubble",
                "--force-prefers-no-reduced-motion",
                "--virtual-clipboard",
                "--touch-events=enabled",
                $"--user-agent=\"{config.UserAgent}\"",
                $"--window-position=0,0",
                $"--window-size={display.WindowWidth},{display.WindowHeight}",
                $"--device-pixel-ratio={display.DprArgument}",
                $"--screen-size={display.ScreenWidth},{display.ScreenHeight}",
                $"--screen-avail-size={display.AvailableWidth},{display.AvailableHeight}",
                $"--screen-color-depth=24",
            }).Distinct().ToList();
            

            if (config.Os == 1 || config.Os == 2)
            {

            }

            proxyServer = string.Empty;
            var isProxyMode = config.TaskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            if (isProxyMode)
            {
                proxyServer = config.TaskArgs.SelectToken("proxy_server")!.Value<string>();
                var protocol = config.TaskArgs.SelectToken("protocol")?.Value<string>();
                var endpoint = ProxyFailureClassifier.Address(proxyServer!, protocol);
                args.Add($"--proxy-server=\"{endpoint}\"");
                if (new Uri(endpoint).Scheme == "socks5")
                {
                    var proxyServerIp = new Uri(endpoint).Host;
                    if (!string.IsNullOrWhiteSpace(proxyServerIp))
                        args.Add($"--host-resolver-rules=\"MAP * ~NOTFOUND , EXCLUDE {proxyServerIp}\"");
                    args.Add($"--proxy-bypass-list=<-loopback>");
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
                args.Add($"--user-data-dir=\"{config.CacheDir}\"");
            }

            args.AddRange(InitFPArgs(config.TaskArgs, config.MaxTouchPoints));
            return args;
        }

        private async Task ConfigureContextAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (ctx.Context == null)
                throw new InvalidOperationException("Context is null.");

            var display = ctx.Config.DisplayProfile ??= DeviceDisplayProfile.From(ctx.Config);
            ctx.DeviceTargets = await DeviceTargetController.CreateAsync(ctx.BrowserSession!.ProcessSession.CdpEndpoint, display, ctx.Config.MaxTouchPoints,
                ex =>
                {
                    ctx.LastFailureReason = "Device initialization failed: " + ex.Message;
                    ctx.BrowserSession?.RequestStop(BrowserStopKind.StartupFailed, ctx.LastFailureReason);
                    CancelLinkedContext(ctx, ctx.LastFailureReason);
                }, token);

            if (ctx.Config.TaskArgs.SelectToken("ipInfo.lon") != null &&
                ctx.Config.TaskArgs.SelectToken("ipInfo.lat") != null)
            {
                await ctx.Context.SetGeolocationAsync(new Geolocation
                {
                    Latitude = ctx.Config.TaskArgs.SelectToken("ipInfo.lat")!.Value<float>(),
                    Longitude = ctx.Config.TaskArgs.SelectToken("ipInfo.lon")!.Value<float>()
                });
            }

            var firstPage = ctx.Context.Pages.Count == 0 ? await ctx.Context.NewPageAsync().WaitAsync(token) : ctx.Context.Pages[0];
            await ActivatePageAsync(ctx, firstPage, token);
        }

        private Task AttachLifecycleEventsAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (ctx.Browser == null || ctx.Context == null)
                return Task.CompletedTask;

            // Context events also cover the first response of a popup, before page initialization awaits CDP.
            ctx.Context.RequestFailed += (_, request) => HandleRequestFailure(ctx, request);
            ctx.Context.Response += (_, response) => HandleDocumentResponse(ctx, response);

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
                var job = HandleContextPageAsync(ctx, newPage);
                ctx.PageLifecycleTasks.TryAdd(job, 0);
                _ = job.ContinueWith(t => ctx.PageLifecycleTasks.TryRemove(t, out var removed), TaskScheduler.Default);
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
            catch (Exception ex)
            {
                LogWriteLine($"Page initialization failed: {ex.Message}");
            }
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



        private async Task ActivatePageAsync(WorkerRunContext ctx, IPage page, CancellationToken token)
        {
            await InitPageAsync(ctx, page, token);
            var session = await ctx.CdpManager!.GetOrCreateSessionAsync(page);
            await CdpTouchRuntime.InitializeAsync(page, session, ctx.Config.MaxTouchPoints, token);
            ctx.SetActivePage(page, session);
        }

        private async Task InitPageAsync(WorkerRunContext ctx, IPage page, CancellationToken token)
        {
            var entry = ctx.PageInitializations.GetOrAdd(page, p => new Lazy<Task>(
                () => InitPageCoreAsync(ctx, p, ctx.Config.LinkedCts.Token), LazyThreadSafetyMode.ExecutionAndPublication));
            try { await entry.Value.WaitAsync(token); }
            catch
            {
                if (entry.IsValueCreated && entry.Value.IsCompleted && !entry.Value.IsCompletedSuccessfully)
                    ((ICollection<KeyValuePair<IPage, Lazy<Task>>>)ctx.PageInitializations).Remove(new(page, entry));
                throw;
            }
        }

        private async Task InitPageCoreAsync(WorkerRunContext ctx, IPage page, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var cdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(page);
            if (ctx.DeviceTargets != null) await ctx.DeviceTargets.WaitForPageAsync(cdpSession, token);
            var display = ctx.Config.DisplayProfile ??= DeviceDisplayProfile.From(ctx.Config);
            var actualWindow = await DevicePageDisplay.ApplyAsync(cdpSession, display, token);
            LogWriteLine($"Display: screen={display.ScreenWidth}x{display.ScreenHeight}, available={display.AvailableWidth}x{display.AvailableHeight}, viewport={display.ViewportWidth}x{display.ViewportHeight}, DPR={display.DprArgument}, window(DIP)={actualWindow}");
            await cdpSession.SendAsync("Page.enable").WaitAsync(TimeSpan.FromSeconds(5), token);

            await CdpTouchRuntime.InitializeAsync(page, cdpSession, ctx.Config.MaxTouchPoints, token);
            SmAdTouch.Bind(page, cdpSession, token, ctx.human);
            page.Close += (_, _) =>
            {
                ctx.PageInitializations.TryRemove(page, out _);
                ctx.PageHttpStatuses.TryRemove(page, out _);
            };

            //await CDPHelper.SetBrowserPermission(cdpSession);

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
                    ctx.BrowserSession?.RequestStop(BrowserStopKind.BrowserCrashed, "Page crashed");
                    CancelLinkedContext(ctx, "PageCrashed");
                }
                catch { }
            };







            page.Download += async (_, download) =>
            {
                if (ctx.BusinessDownloadsEnabled)
                    Interlocked.Increment(ref ctx.TriggerDownloadSign);
                else
                    LogWriteLine($"非业务下载: url={download.Url}, filename={download.SuggestedFilename}，不计入点击下载");
                try { await download.CancelAsync(); } catch { }
            };


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

            var page = ctx.Context.Pages.Count == 0 ? await ctx.Context.NewPageAsync().WaitAsync(token) : ctx.Context.Pages[0];
            await ActivatePageAsync(ctx, page, token);
            await page.GotoAsync("about:blank", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 5000 }).WaitAsync(token);
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
                QueryWord = string.Empty,
                IsHomepageTrigger = false,
                EndTask = false
            };

            if (_aggregator.CanHomepageTrigger(ctx.Config.TaskId))
            {
                result.FirstPageUrl = result.FirstPageUrl.Replace("&q=[QUERY]", "");
                result.IsHomepageTrigger = true;
                return result;
            }

            if (!result.FirstPageUrl.Contains("[QUERY]"))
                return result;

            var retry = await RetryPolicy.ExecuteAsync(
                async ct =>
                {
                    ct.ThrowIfCancellationRequested();
                    return await _adeHelper.GetWordAsync();
                },
                maxAttempts: 6,
                successPredicate: q => !string.IsNullOrWhiteSpace(q),
                onRetry: (attempt, ex) =>
                {
                    if (ex != null)
                        LogWriteLine($"获取词条重试:{attempt}, ex={ex.Message}");
                    else
                        LogWriteLine($"获取词条重试:{attempt}");
                },
                delayMsFactory: _ => CommonHelper.RandomRange(100, 200),
                token: token);

            if (!retry.IsSuccess || string.IsNullOrWhiteSpace(retry.Value))
            {
                LogWriteLine("无法获取词条,请检查服务器");
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                result.Success = false;
                result.EndTask = true;
                return result;
            }

            result.QueryWord = retry.Value;
            result.FirstPageUrl = result.FirstPageUrl.Replace("[QUERY]", retry.Value);
            LogWriteLine($"{this.Title}:搜索词条{retry.Value}");
            _aggregator.EnqueueAdWordExtracted(retry.Value);

            return result;
        }

        private void HandleRequestFailure(WorkerRunContext ctx, IRequest request)
        {
            try
            {
                var failure = request.Failure;
                var mainNavigation = request.IsNavigationRequest && ReferenceEquals(request.Frame, request.Frame.Page.MainFrame);
                if (mainNavigation) ctx.LastFailureReason = $"RequestFailed: {failure}, req={request.Url}";
                var kind = ProxyFailureClassifier.Classify(failure,
                    ctx.Config.TaskArgs.SelectToken("isProxyMode")?.Value<bool>() == true, mainNavigation);
                if (kind.HasValue) StopForProxyFailure(ctx, kind.Value, $"Proxy {kind}: {failure}, req={request.Url}");
            }
            catch (PlaywrightException ex) { LogWriteLine($"Request failure inspection: {ex.Message}"); }
        }

        private void HandleDocumentResponse(WorkerRunContext ctx, IResponse response)
        {
            if (!response.Request.IsNavigationRequest) return;
            var page = response.Request.Frame.Page;
            if (!ReferenceEquals(response.Request.Frame, page.MainFrame)) return;
            ctx.PageHttpStatuses[page] = response.Status;
            if (response.Status >= 400)
            {
                ctx.LastFailureReason = $"Main document HTTP {response.Status}: {response.Url}";
                LogWriteLine(ctx.LastFailureReason);
            }
            if (response.Status == 407 && ctx.Config.TaskArgs.SelectToken("isProxyMode")?.Value<bool>() == true)
                StopForProxyFailure(ctx, ProxyFailureKind.Authentication, $"Proxy HTTP 407: {response.Url}");
        }

        private void StopForProxyFailure(WorkerRunContext ctx, ProxyFailureKind kind, string reason)
        {
            if (!ctx.TryMarkProxyFailure(kind, reason)) return;
            LogWriteLine(reason);
            ctx.BrowserSession?.RequestStop(BrowserStopKind.ProxyFailed, reason);
            CancelLinkedContext(ctx, "ProxyFailed");
        }

        private async Task<bool> NavigateToEntryAsync(WorkerRunContext ctx, string url, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ctx.DisableBusinessDownloads();

            try
            {
                var navigation = await SMAd.PageActions.EntryPageNavigator.NavigateAsync(ctx.Page!, url,
                    ctx.Config.PageLoadingTimeoutMs, token, message => LogWriteLine(message));
                if (!navigation.Opened)
                {
                    if (navigation.HttpStatus == 407 && ctx.Config.TaskArgs.SelectToken("isProxyMode")?.Value<bool>() == true)
                        StopForProxyFailure(ctx, ProxyFailureKind.Authentication, $"Entry proxy HTTP 407: {url}");
                    ctx.LastFailureReason = navigation.Reason;
                    LogWriteLine($"入口页面未打开，跳过当前PV: {navigation.Reason}");
                    return false;
                }
            }
            catch (PlaywrightException ex) when (ProxyFailureClassifier.Classify(ex.Message,
                ctx.Config.TaskArgs.SelectToken("isProxyMode")?.Value<bool>() == true, true) is { } kind)
            {
                StopForProxyFailure(ctx, kind, $"Entry proxy {kind}: {ex.Message}");
                return false;
            }
            catch (TimeoutException ex)
            {
                ctx.LastFailureReason = $"Entry navigation timed out: {ex.Message}";
                ctx.BrowserSession?.RequestStop(BrowserStopKind.OperationTimedOut, ctx.LastFailureReason);
                return false;
            }
            ctx.CurrentPageUrl = ctx.Page!.Url;
            ctx.PagesCount = ctx.Context!.Pages.Count;

            this.QTPExecuteDSP(ctx.Config.TaskId);
            ctx.EnableBusinessDownloads();
            return true;
        }

        private async Task<bool> ExecuteHomepageTriggerAsync(WorkerRunContext ctx, string? q, CancellationToken token)
        {
            var word = string.IsNullOrWhiteSpace(q) ? await _adeHelper.GetWordAsync().WaitAsync(token) : q;
            if (string.IsNullOrWhiteSpace(word)) { ctx.LastFailureReason = "Search word unavailable"; return false; }
            var binding = ctx.ActivePage!;
            var input = binding.Page.Locator("textarea#kw");
            try
            {
                if (!await SmAdTouch.TapAsync(binding.Page, binding.Session, input)) return false;
                await SmAdTouch.ReplaceTextAsync(binding.Page, input, word, token);
                var result = await GetActions(ctx).ExecuteAsync("Search", (b, ct) => ctx.human.Engine.TapAsync(
                    b.Page, b.Session, b.Page.Locator("div.submit").First, cancellationToken: ct),
                    (page, ct) => ActivatePageAsync(ctx, page, ct), token,
                    (page, ct) => page.Locator("#results, #main .result, .result-container").First.IsVisibleAsync().WaitAsync(ct));
                if (!result.Succeeded) { ctx.LastFailureReason = result.Reason ?? "Search produced no confirmed result"; return false; }
                LogWriteLine($"{Title}:搜索完成");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { ctx.LastFailureReason = $"Search: {ex.Message}"; return false; }
        }

        #endregion

        #region Ads / JumpClick

        /// <summary>
        /// 检测页面广告词标记
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="q"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<bool> DetectAndUploadAdWordsAsync(WorkerRunContext ctx, string? q, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (ctx.Page == null || ctx.Page.IsClosed)
            {
                LogWriteLine("广告检测终止: Page为空或已关闭");
                return false;
            }

            if (ctx.Browser == null || !ctx.Browser.IsConnected)
            {
                LogWriteLine("广告检测终止: Browser为空或已断开");
                return false;
            }

            try
            {
                var adDotUrls = ctx.Page.Locator("div[ad_dot_url^='http'],div.ad-wolong-container:has(a[data-url^='http'])");
                ctx.PageAdsCount = await adDotUrls.CountAsync();

                if (ctx.PageAdsCount <= 0)
                {
                    LogWriteLine("广告检测完成: 没有广告标记");
                    return true;
                }

                if (string.IsNullOrWhiteSpace(q))
                    return true;

                _aggregator.EnqueueAdWordHit(q);

                int ad1688 = 0;
                int adOther = 0;

                var domains1688 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var domainsOther = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var brandsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var i in Enumerable.Range(0, ctx.PageAdsCount))
                {
                    token.ThrowIfCancellationRequested();

                    if (ctx.Page == null || ctx.Page.IsClosed)
                    {
                        LogWriteLine("广告检测中断: Page已关闭");
                        return false;
                    }

                    if (ctx.Browser == null || !ctx.Browser.IsConnected)
                    {
                        LogWriteLine("广告检测中断: Browser已断开");
                        return false;
                    }

                    var item = adDotUrls.Nth(i);
                    var links = item.Locator("a[data-url]");
                    int linkCount = await links.CountAsync();
                    if (linkCount == 0)
                        continue;
                    string? dataUrl = null;
                    for (int j = 0; j < linkCount; j++)
                    {
                        var value = await links.Nth(j).GetAttributeAsync("data-url");
                        if (string.IsNullOrWhiteSpace(value))
                            continue;
                        dataUrl = value.Trim();
                        break;
                    }
                    if (string.IsNullOrWhiteSpace(dataUrl))
                        continue;
                    if (!Uri.TryCreate(dataUrl, UriKind.Absolute, out var uri))
                        continue;

                    var rootDomain = string.Join(".", uri.Host.Split(".").Reverse().Take(2).Reverse());



                    var tagText = "广告";
                    if (await item.GetByText("汇川广告", new() { Exact = true }).CountAsync() > 0)
                    {
                        tagText = "汇川广告";
                    }
                    else if (await item.GetByText("品牌广告", new() { Exact = true }).CountAsync() > 0)
                    {
                        tagText = "品牌广告";
                    }

                    brandsSet.Add(tagText);

                    if (rootDomain.Equals("1688.com", StringComparison.OrdinalIgnoreCase))
                    {
                        domains1688.Add(rootDomain);
                        ad1688++;
                    }
                    else
                    {
                        domainsOther.Add(rootDomain);
                        adOther++;
                    }
                }

                if (domainsOther.Count > 0 && domains1688.Count == 0)
                    QTPUploadAdWord("no1688", q);

                if (domainsOther.Count > 0)
                    QTPUploadAdWord("other", q);

                if (domains1688.Count > 0)
                    QTPUploadAdWord("1688", q);

                var allDomains = domains1688.Concat(domainsOther).ToList();
                if (allDomains.Count > 0)
                {
                    var allBrands = brandsSet.ToList();
                    _aggregator.EnqueueAdKeywordDomain(new AdKeywordDomain
                    {
                        Keyword = q,
                        Domains = allDomains,
                        Brands = allBrands
                    });
                }

                if (ctx.Config.NoTrigger1688 && adOther == 0)
                {
                    LogWriteLine("只有1688广告标记,重试");
                    return false;
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlaywrightException ex) when (IsClosedPlaywrightException(ex))
            {
                LogWriteLine($"广告检测失败: 页面/上下文/浏览器已关闭, {ex.Message}");
                return false;
            }
        }


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

                await ctx.human!.MoveToElementAsync(
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

            if (!ctx.Config.PriorityNon1688)
            {
                return Enumerable.Range(0, count)
                    .OrderBy(_ => Guid.NewGuid())
                    .Select(i => sponsoreds.Nth(i))
                    .ToList();
            }

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

        /// <summary>
        /// 1688列表页重定向
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task TryHandle1688RecommendWordsAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var page = ctx.Page;
            if (!_appSettings.p4psearch || _appSettings.p4psearchRate <= 0 || page == null || page.IsClosed
                || !Uri.TryCreate(page.Url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || !string.Equals(uri.Host, "m.1688.com", StringComparison.OrdinalIgnoreCase)) return;
            var initialUrl = page.Url;

            _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_p4psearch");
            var metrics = _aggregator.GetLocalMetrics(ctx.Config.TaskId, "dsp_p4psearch", "dsp_p4psearch_click");
            var attempts = metrics["dsp_p4psearch"];
            var clicks = metrics["dsp_p4psearch_click"];
            var actualRate = attempts > 0 ? clicks * 100.0 / attempts : 0;
            var desiredRate = Math.Clamp(_appSettings.p4psearchRate, 0, 100);
            LogWriteLine($"1688推荐词: 实际点击比率={actualRate:N2}%，目标={desiredRate}%");
            // Retain the existing cumulative-rate policy rather than changing task distribution.
            if (desiredRate < 100 && clicks > 0 && actualRate >= desiredRate) return;

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            var ct = budget.Token;
            try
            {
                if (!await ViewportLandingInteraction.BrowseAsync(ctx, this, "1688推荐词", token)) return;
                budget.CancelAfter(5000);
                if (page.IsClosed || !ReferenceEquals(ctx.Page, page) || page.Url != initialUrl) return;

                // Include all recommendation sections; DOM visibility alone does not mean on-screen.
                var links = page.Locator("div[class*='ab-recommend-words'] a.word");
                var candidates = await ViewportLandingInteraction.GetVisibleCandidateIndicesAsync(links, ct);
                if (candidates.Length == 0)
                {
                    LogWriteLine("1688推荐词: 当前视口没有未被遮挡的推荐词，跳过点击");
                    return;
                }
                var index = candidates[Random.Shared.Next(candidates.Length)];
                ct.ThrowIfCancellationRequested();
                if (page.IsClosed || !ReferenceEquals(ctx.Page, page) || page.Url != initialUrl) return;
                LogWriteLine($"1688推荐词: 当前可点候选={candidates.Length}，随机选择[{index}]，直接触屏点击");
                budget.CancelAfter(Timeout.Infinite);
                var click = await ClickAndDetectNavigationAsync(ctx, links.Nth(index), token);
                if (click.Attempted)
                    _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_p4psearch_click");
                LogWriteLine($"1688推荐词: 点击结果={click.Outcome}, 原因={click.Reason}");
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                LogWriteLine("1688推荐词: 目标筛选预算耗尽，跳过后续推荐词操作");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (PlaywrightException ex)
            {
                LogWriteLine($"1688推荐词: 页面操作失败，跳过后续推荐词操作: {ex.Message}");
            }
        }

        private Task<bool> TryOptionalPageOperationAsync(WorkerRunContext ctx, string action, Func<Task> work,
            CancellationToken token) => SMAd.PageActions.OptionalPageOperation.RunAsync(ctx, action, work, token,
                message => LogWriteLine(message));

        public Task<ILocator?> ResolveOfferItemsAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ctx.Page == null ? null : OfferTargetResolver.Resolve(ctx.Page));
        }


        #endregion

        #region Task Sleep Phase

        private async Task<FlowControl> ExecuteTaskSleepPhaseAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ctx.Config.LinkedCts.Token.ThrowIfCancellationRequested();
            if (!SMAd.PageActions.OptionalPageOperation.CanContinue(ctx, token))
            {
                ctx.LastFailureReason = "Page or browser unavailable before stay";
                return FlowControl.Failed;
            }

            if (ctx.Page!.Url.Contains("1688.com") && ctx.Page.Url.Contains("_tmd_") && ctx.Page!.Url.Contains("punish?x5secdata"))
            {
                ctx.LastFailureReason = "Page requires verification";
                return FlowControl.Failed;
            }


            if (ctx.JumpClick && ctx.PageTriggerClick)
            {
                await TryOptionalPageOperationAsync(ctx, "1688 detail", () => TryHandleRfq1688Async(ctx, token), token);
                await TryOptionalPageOperationAsync(ctx, "Qianhu detail", () => TryHandleQianhuFormAsync(ctx, token), token);
                await TryOptionalPageOperationAsync(ctx, "Louisvuitton detail", () => TryHandleLouisvuittonAsync(ctx, token), token);
            }


            if (ctx.TriggerDownloadSign > 0)
                return FlowControl.EndTask;

            if (ctx.Page!.Url.StartsWith("https://login.m.taobao.com")
                || ctx.Page.Url.StartsWith("https://havanalogin.taobao.com")
                || ctx.Page.Url.StartsWith("https://plogin.m.jd.com"))
            {
                ctx.LastFailureReason = "Login required";
                return FlowControl.Failed;
            }
            if (ctx.Page!.Url.StartsWith("https://h5.m.taobao.com"))
            {
                if (await ctx.Page.GetByText("获取验证码").CountAsync() > 0)
                {
                    ctx.LastFailureReason = "Verification required";
                    return FlowControl.Failed;
                }
            }
            if (ctx.JumpClick && ctx.PageTriggerClick)
            {
                await TryOptionalPageOperationAsync(ctx, "Detail actions", () => TryHandleAllAsync(ctx, token), token);
            }
            var stay = System.Diagnostics.Stopwatch.StartNew();

            LogWriteLine("延时停留");
            var loop = 0;
            var canSwipe = true;




            while (stay.ElapsedMilliseconds < Math.Max(0, ctx.Config.SleepMs))
            {
                token.ThrowIfCancellationRequested();
                ctx.Config.LinkedCts.Token.ThrowIfCancellationRequested();
                if (!SMAd.PageActions.OptionalPageOperation.CanContinue(ctx, token))
                    throw new PlaywrightException("Stay phase page or browser is no longer available");
                loop++;

                if (canSwipe)
                {
                    LogWriteLine("滑动操作");
                    using var remaining = CancellationTokenSource.CreateLinkedTokenSource(token);
                    remaining.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(1, ctx.Config.SleepMs - stay.ElapsedMilliseconds)));
                    canSwipe = await TryOptionalPageOperationAsync(ctx, "Stay swipe", () =>
                        ctx.human.BrowseOnceAsync(ctx.Page!, ctx.CdpSession!, remaining.Token), token);
                    if (!canSwipe) LogWriteLine("停留滑动未完成，本轮剩余时间改为静态阅读");
                }
                if (stay.ElapsedMilliseconds >= ctx.Config.SleepMs) break;
                await Task.Delay((int)Math.Min(1500, Math.Max(0, ctx.Config.SleepMs - stay.ElapsedMilliseconds)), token);
                if (ctx.TriggerDownloadSign > 0) return FlowControl.EndTask;
            }

            LogWriteLine("动作完成");
            if (ctx.Config.TotalPV > 1 && ctx.JumpClick && (!ctx.PageTriggerClick || !ctx.Config.PvsTriggerOne))
                return FlowControl.NextPv;
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
                await ClearPageCloseBtn(ctx.Page!, ctx.CdpSession!);
                await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                await ClearSuccessTipNewCloseNew(ctx.Page!, ctx.CdpSession!);
                await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);

                await BrowseForAsync(ctx, 3, 8, token);

                if (!_appSettings.NoTrigger1688Shop || CommonHelper.Chance(0.25))
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
                        await ctx.human!.MoveToElementAsync(
                            ctx.Page!,
                            ctx.CdpSession!,
                            locator_detail,
                            maxSwipes: 10,
                            cancellationToken: token);


                        if (!await IsElementPartiallyVisibleAsync(locator_detail))
                        {
                            await SmAdTouch.ScrollFallbackAsync(ctx.Page!, locator_detail, token);
                        }



                        await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);
                        var clickRes2 = await ClickAndDetectNavigationAsync(ctx, locator_detail.First, token);
                        if (clickRes2.Navigated)
                        {
                            await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                            await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                            await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                            await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                            await BrowseForAsync(ctx, 3, 8, token);

                            locator_detail = ctx.Page
                                .Locator("a[href]:visible")
                                .Filter(new() { Visible = true })
                                .First;

                            if (await locator_detail.CountAsync() > 0)
                            {
                                await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);

                                await ctx.human!.MoveToElementAsync(
                                    ctx.Page!,
                                    ctx.CdpSession!,
                                    locator_detail.First,
                                    maxSwipes: 10,
                                    cancellationToken: token);

                                if (!await IsElementPartiallyVisibleAsync(locator_detail.First))
                                {
                                    await SmAdTouch.ScrollFallbackAsync(ctx.Page!, locator_detail.First, token);
                                }




                                var clickRes3 = await ClickAndDetectNavigationAsync(ctx, locator_detail.First, token);
                                if (clickRes3.Navigated)
                                {
                                    await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                                    await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                                    await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                                    await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                                    await BrowseForAsync(ctx, 3, 8, token);

                                }

                            }
                        }

                    }
                    else
                    {
                        locator_detail = ctx.Page
                            .Locator("a[href]:visible")
                            .Filter(new() { Visible = true })
                            .First;

                        if (await locator_detail.CountAsync() > 0)
                        {
                            var clickRes3 = await ClickAndDetectNavigationAsync(ctx, locator_detail.First, token);
                            if (clickRes3.Navigated)
                            {
                                await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                                await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                                await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
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

                bool canClick = _appSettings.Rfq1688 && (_appSettings.Rfq1688Rate == 100
                    || metrics["dsp_rfq1688_click"] == 0
                    || ((metrics["dsp_rfq1688_click"] / (double)metrics["dsp_rfq1688"]) * 100 < _appSettings.Rfq1688Rate));

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
                        await SmAdTouch.TapAsync(ctx.Page, ctx.CdpSession!, queryBtn.First, timeout: 2000);
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

                await SmAdTouch.ReplaceTextAsync(ctx.Page!, el.First, phone, token);
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                var answerContents = ctx.Page.Locator("div.new_answer_content span,div.answer_content span");
                if (await answerContents.CountAsync() > 0)
                {
                    int count = await answerContents.CountAsync();
                    var answer = answerContents.Nth(CommonHelper.RandomRange(0, count));
                    await SmAdTouch.TapAsync(ctx.Page, ctx.CdpSession!, answer.First, timeout: 2000);
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                }
                else
                {
                    var chatText = ChatTextHelper.GetChatText();
                    el = ctx.Page.Locator("textarea#new_od_xst_msg_input_val_new_message,textarea#od_xst_msg_input_val_new_message");
                    if (await el.CountAsync() > 0)
                    {
                        await SmAdTouch.ReplaceTextAsync(ctx.Page!, el.First, chatText, token);
                        await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                    }
                }

                el = ctx.Page.Locator(".new_successTipNew_wangwang_new,.successTipNew_call_new");
                if (await el.CountAsync() > 0)
                {
                    try
                    {
                        await SmAdTouch.TapAsync(ctx.Page, ctx.CdpSession!, el.First, timeout: 2000);
                        await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                        var sms = ctx.Page.GetByText("获取验证码");
                        if (await sms.CountAsync() > 0)
                        {
                            var close1 = ctx.Page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new");
                            if (await close1.CountAsync() > 0)
                                await SmAdTouch.TapAsync(ctx.Page, ctx.CdpSession!, close1.First, timeout: 2000);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch { }

                    var close2 = ctx.Page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new,.newCloseIcon_content");
                    if (await close2.CountAsync() > 0)
                        await SmAdTouch.TapAsync(ctx.Page, ctx.CdpSession!, close2.First, timeout: 2000);
                }
                await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                await BrowseForAsync(ctx, 3, 8, token);

                if (!_appSettings.NoTrigger1688Shop || CommonHelper.Chance(0.25))
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

                        await ctx.human!.MoveToElementAsync(
                            ctx.Page!,
                            ctx.CdpSession!,
                            locator.First,
                            maxSwipes: 10,
                            cancellationToken: token);

                        if (!await IsElementPartiallyVisibleAsync(locator.First))
                        {
                            await SmAdTouch.ScrollFallbackAsync(ctx.Page!, locator.First, token);

                        }

                        await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);
                        var clickRes2 = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                        if (clickRes2.Navigated)
                        {
                            await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                            await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                            await BrowseForAsync(ctx, 3, 8, token);


                            locator = ctx.Page
                                .Locator("a[href]:visible")
                                .Filter(new() { Visible = true })
                                .First;

                            if (await locator.CountAsync() > 0)
                            {
                                var clickRes3 = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                                if (clickRes3.Navigated)
                                {
                                    await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                                    await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                    await BrowseForAsync(ctx, 3, 8, token);
                                }

                            }
                        }

                    }
                    else
                    {
                        locator = ctx.Page
                            .Locator("a[href]:visible")
                            .Filter(new() { Visible = true })
                            .First;

                        if (await locator.CountAsync() > 0)
                        {
                            var clickRes3 = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                            if (clickRes3.Navigated)
                            {
                                await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                                await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
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




        private async Task TryHandleQianhuFormAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!ctx.Page!.Url.StartsWith("https://qianhu.wejianzhan.com/"))
                return;

            try
            {
                var phone = await _adeHelper.GetPhoneNumberAsync();
                if (string.IsNullOrWhiteSpace(phone))
                    return;

                var surname = _nameGenerator.GetDisplayName(phone);

                var inputName = ctx.Page.Locator("input[placeholder='请输入您的称呼']").First;
                if (await inputName.CountAsync() > 0)
                {
                    await SmAdTouch.ReplaceTextAsync(ctx.Page!, inputName, surname, token);
                }

                await Task.Delay(CommonHelper.RandomRange(500, 800), token);

                var inputPhone = ctx.Page.Locator("input[placeholder='请输入手机号']").First;
                if (await inputPhone.CountAsync() > 0)
                {
                    await SmAdTouch.ReplaceTextAsync(ctx.Page!, inputPhone, phone, token);
                }

                var radio = ctx.Page.Locator(".phone-agrement-container .phone-agrement-radio");
                if (await radio.CountAsync() > 0)
                    await SmAdTouch.TapAsync(ctx.Page, ctx.CdpSession!, radio.First);

                var btnSubmit = ctx.Page.Locator("div:has-text('免费领票')").First;
                if (await btnSubmit.CountAsync() > 0)
                {
                    await SmAdTouch.TapAsync(ctx.Page, ctx.CdpSession!, btnSubmit);
                    await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }

        private async Task TryHandleLouisvuittonAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!ctx.Page!.Url.StartsWith("https://www.louisvuitton.cn"))
                return;

            try
            {
                await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                var cookieBtn = await SMAdHelper.WaitVisibleLocatorAsync(new[]
                {
                    ctx.Page.GetByText("同意全部第三方Cookie", new() { Exact = true }),
                    ctx.Page.GetByRole(AriaRole.Button, new() { Name = "同意全部第三方Cookie" }),
                    ctx.Page.Locator("button").Filter(new() { HasTextString = "同意全部第三方Cookie" }),
                }, token, timeoutMs: 10000);
                if (cookieBtn != null)
                {
                    await Task.Delay(CommonHelper.RandomRange(300, 600), token);
                    await SmAdTouch.TapAsync(ctx.Page, ctx.CdpSession!, cookieBtn);
                }


                await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);
                await BrowseForAsync(ctx, 3, 8, token);

                ClickResult? clickResult = null;
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

                if (clickResult != null && clickResult.Navigated)
                {
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                    await BrowseForAsync(ctx, 3, 8, token);

                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                    nodes = await PlaywrightClickableHelper.GetClickableNodesAsync(ctx.Page, options);
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



        private async Task TryHandleAllAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var b = ctx.ActivePage;
            if (b == null || b.Page.IsClosed) return;
            await ctx.human.SwipeByIntentAsync(b.Page, b.Session, SwipeIntent.Reading, token);
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
            LogWriteLine("huadong");
            //await Task.Delay(2000, token);
            ////var traces =  await ctx.human.BrowseTimesAsync(ctx.Page!, ctx.CdpSession!, minTimes: 3, maxTimes: 5);

            //// HumanSwipeGifExporter.ExportAll(
            //// traces,
            //// @"./traces");

            //await ctx.Page!.ScreenshotAsync(new PageScreenshotOptions
            //{
            //    Path = "screenshot.png",
            //    FullPage = false
            //});
            //LogWriteLine("jieping");
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
        public Task<ClickResult> ClickAndDetectNavigationAsync(WorkerRunContext ctx, ILocator element, CancellationToken token)
            => GetActions(ctx).ExecuteAsync("Navigate", (b, ct) => ctx.human.Engine.TapAsync(b.Page, b.Session, element,
                cancellationToken: ct), (p, ct) => ActivatePageAsync(ctx, p, ct), token);

        public Task<ClickResult> ClickAndDetectNavigationAsync(WorkerRunContext ctx, IElementHandle element, CancellationToken token)
            => GetActions(ctx).ExecuteAsync("Navigate", (b, ct) => ctx.human.Engine.TapAsync(b.Page, b.Session, element,
                cancellationToken: ct), (p, ct) => ActivatePageAsync(ctx, p, ct), token);

        private SMAd.PageActions.PageActionExecutor GetActions(WorkerRunContext ctx)
            => ctx.Actions ?? Interlocked.CompareExchange(ref ctx.Actions, new(ctx, message => LogWriteLine(message)), null) ?? ctx.Actions!;

        public async Task<ClickResult> TryRandomViewportClickableClickAsync(WorkerRunContext ctx, CancellationToken token)
            => await TryRandomLinkClickAsync(ctx, "a[href]:visible", token);

        public async Task<ClickResult> TryRandomLinkClickAsync(WorkerRunContext ctx, string selector, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var locators = ctx.Page!.Locator(selector);
            int count = await locators.CountAsync();

            var clickable = new List<ILocator>();
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();

                var link = locators.Nth(i);
                if (await link.IsVisibleAsync() && await link.IsEnabledAsync())
                    clickable.Add(link);
            }

            foreach (var link in clickable.OrderBy(_ => Guid.NewGuid()))
            {
                token.ThrowIfCancellationRequested();

                await ctx.human!.MoveToElementAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    link,
                    maxSwipes: 10,
                    cancellationToken: token);

                if (!await IsElementPartiallyVisibleAsync(link))
                {
                    await SmAdTouch.ScrollFallbackAsync(ctx.Page!, link, token);
                }

                await Task.Delay(CommonHelper.RandomRange(800, 1400), token);
                var result = await ClickAndDetectNavigationAsync(ctx, link.First, token);
                if (result.Navigated)
                    return result;
            }
            return ClickResult.Fail("No actionable link target");
        }


        #endregion

    }
}
