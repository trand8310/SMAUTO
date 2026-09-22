using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using PlaywrightHumanInput;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using QTP.Common.Win32;
using SMAd;
using SMAd.LandingPolicy;
using SMAd.Models;
using SMAd.PlaywrightHumanInput;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace QTP.Plugins
{
    public sealed class SMAdTask : QTPServiceBase
    {
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

        public static QTPPlugin GetInfo()
        {
            return new QTPPlugin()
            {
                ClassName = "QTP.Plugins.SMAdTask",
                Name = "SMAd",
                FileName = "SMAd.dll",
            };
        }
        public override string Title => "VISA";
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

        private static List<string> InitFPArgs(JToken taskArgs, int maxTouchPoints)
        {
            var result = new List<string>();
            uint fingerprint = 0;
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
            }
            var make = taskArgs.SelectToken("dev.make")?.Value<string>().ToLower();

            result.Add("--fingerprint-config-dir=\"" + System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fingerprint") + "\"");



            var full_version = taskArgs.SelectToken("dev.full_version").Value<string>();
            var full_version_values = full_version.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries);

            result.Add($"--platform-version=\"{taskArgs.SelectToken("dev.osv").Value<string>()}\"");
            result.Add($"--full-version={full_version}");

            if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.brand")?.Value<string>()))
            {
                var brand = taskArgs.SelectToken("dev.brand")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(make))
                {
                    result.Add($"--make-name=\"{make}\"");

                    //   "--make-name="huawei" --fingerprint-config-dir="E:\code\fingerprint""
                }
                result.Add($"--brand=\"{brand}\"");
                result.Add($"--brand-name=\"{brand}\"");
                if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.brand_version")?.Value<string>()))
                    result.Add($"--brand-version=\"{taskArgs.SelectToken("dev.brand_version")?.Value<string>()}\"");

                result.Add($"--disable-full-version-list");
                result.Add($"--disable-brand-version-list");


                if (os == 1 || os == 2)
                {

                    if (!string.IsNullOrWhiteSpace(make))
                    {
                        if (make.Contains("xiaomi"))
                        {
                            result.Add($"--def-fontname=\"MiSans\"");
                        }
                        else if (make.ToLower().Contains("vivo"))
                        {
                            result.Add($"--def-fontname=\"vivo Sans\"");
                        }
                        else if (make.ToLower().Contains("oppo"))
                        {
                            result.Add($"--def-fontname=\"OPPO Sans 4.0\"");
                        }
                        else if (make.ToLower().Contains("huawei"))
                        {
                            result.Add($"--def-fontname=\"HarmonyOS Sans\"");
                        }
                    }
                }
                //def-fontname
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

            result.Add($"--fingerprint={fingerprint}");
            var grease_cipher = Math.Abs(string.Join(".", full_version_values.Take(2)).GetHashCode()) % 65535;
            result.Add($"--ssl-grease-cipher={grease_cipher}");
            if (os == 1 || os == 2)
            {
                result.Add($"--netinfo-type={new string[] { "wifi", "cellular" }[CommonHelper.RandomRange(0, 2)]}");
                result.Add($"--netinfo-effective=4g");
                result.Add($"--netinfo-rtt={CommonHelper.RandomRange(0, 500)}");
            }

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
                        result.Add($"--webrtc-ip-handling-policy=default");
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

            var dev_hash = Math.Abs(taskArgs.SelectToken("dev").ToString().GetHashCode());

            #region webgl
            result.Add($"--webgl-vendor=\"{vendor}\"");
            result.Add($"--webgl-renderer=\"{gpu}\"");
            #endregion

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
            result.Add("--enable-image-noise");
            result.Add("--enable-text-noise");
            //result.Add("--enable-font-noise");
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

        public async Task CloseBrowserProcess(string uniqueId)
        {
            await _processManager.CloseAsync(uniqueId);
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
                        new VisaLandingPageStrategy(this),
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


            // 1. 固定 Seed
            int accountSeed = StableSeed.Create(ctx.Config.TaskArgs);
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
                PageContextRefreshEveryGestures = 1
            });


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
                    //entry.FirstPageUrl = "https://adtomall.cn/content/pixelscan/r2/";
                    //entry.FirstPageUrl = "https://t1.publicis-groupe.cn/hat?_t=r&type=clk&v=1&_z=m&_inst=saas&hat_id=aaaaaabbbbb&_ms=0&_dt=PHN&_plt=MBL&hat_iesid=__IESID__&imp_id=__IMPID__&uoo=__UOO__&os=__OS__&meid=__IMEI__&idfa=__IDFA__&oaid=__OAID__&mac=__MAC__&androidid=__ANDROIDID__&openudid=__OPENUDID__&useragent=__UA__&ts=__TS__&ip=__IP__&r=[timestamp]&_rc=ea3c&uid=__CAID__&uid_type=CAID&_ul=https%3A%2F%2Fvisa-h5.offerpluscn.com%2Foffer%2Foffer_1782437725_6a3dd75d9fc21%3Fh0%3D__OS__%26h1%3D__IMEI__%26h2%3D__ANDROIDID__%26h5%3D__MAC__%26h7%3D__IDFA__%26h8%3D__OPENUDID__%26h10%3D__OAID__%26hat_id%3DNDgyMTgmNjIxMjE2MCbFVw%26_inst%3Dsaas";
                    //entry.FirstPageUrl = "https://visa-h5.offerpluscn.com/offer/offer_1782437725_6a3dd75d9fc21?h0=__OS__&h1=__IMEI__&h2=__ANDROIDID__&h5=__MAC__&h7=__IDFA__&h8=__OPENUDID__&h10=__OAID__&hat_id=";
                    //entry.FirstPageUrl = "https://visa-h5.offerpluscn.com/travel?h0=__OS__&h1=__IMEI__&h2=__ANDROIDID__&h5=__MAC__&h7=__IDFA__&h8=__OPENUDID__&h49=__OAID__&hat_id=KKQQQDKDSDFSDFSDFS&_inst=saas";
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
                LogWriteLine($"{this.Title}:ExecuteWorker: {((ctx.Config.PageLoadedDelayMs) / 1000.0):N2}");
                await Task.Delay(ctx.Config.PageLoadedDelayMs, token);
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
                await DecideJumpClickAsync(ctx, token);
                if (ctx.JumpClick)
                {
                    var clickFlow = await TryExecuteJumpClickAsync(ctx, entry.FirstPageUrl!, token);
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


            var scaleX = config.TaskArgs.SelectToken("scaleX")?.Value<float>() ?? 1.0;
            var scaleY = config.TaskArgs.SelectToken("scaleY")?.Value<float>() ?? 1.0;

            var args = new List<string>
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
                "--mouse-as-touch",
                "--touch-events=enabled",
                $"--user-agent=\"{config.UserAgent}\"",
                $"--window-size={(int)Math.Ceiling( config.Sw + scaleX)},{(int)Math.Ceiling( config.Sh + scaleY)}",
                "--window-position=0,0",
                $"--device-pixel-ratio={config.DeviceScale}",
               // $"--screen-size={config.Sw * scaleX},{config.Sh * scaleY}",
               // $"--screen-avail-size={config.Sw},{config.Sh}",
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

            args.AddRange(InitFPArgs(config.TaskArgs, config.MaxTouchPoints));
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

        private async Task InitPageAsync(WorkerRunContext ctx, IPage page, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            //await page.SetViewportSizeAsync(ctx.Config.Sw, ctx.Config.Sh);
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
            var result = new EntryPreparationResult
            {
                Success = true,
                FirstPageUrl = ctx.Config.TaskUrl,
                EndTask = false
            };
            return await Task.FromResult(result);
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
        private async Task<FlowControl> TryExecuteJumpClickAsync(WorkerRunContext ctx, string firstPageUrl, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            //https://visa-h5.offerpluscn.com/travel?h0=__OS__&h1=__IMEI__&h2=__ANDROIDID__&h5=__MAC__&h7=__IDFA__&h8=__OPENUDID__&h49=__OAID__&hat_id=NDgyMTgmNjIxMzU2MSZ8XQ&_inst=saas

            var acceptButton = ctx.Page!.GetByRole(AriaRole.Button, new()
            {
                Name = "接受"
            });
            if (!await CDPHelper.WaitForAsync(acceptButton, 5000))
            {
                return FlowControl.EndTask;
            }
            await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, acceptButton);
            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

            var sectionTitle = ctx.Page!
            .Locator(".section-title-row")
            .Filter(new()
            {
                Has = ctx.Page!
                    .Locator(".section-title-main")
                    .Filter(new()
                    {
                        HasTextRegex = new Regex(@"^机票$")
                    })
            });
            var firstCard = sectionTitle
                .Locator("xpath=following-sibling::*[1]")
                .Locator(".card-half-item")
                .First;
            if ((await firstCard.CountAsync()) > 0)
            {
                var resultClicked = await ClickAndDetectNavigationAsync(ctx, firstCard, token);
                if (resultClicked.Navigated)
                {
                    await Task.Delay(CommonHelper.RandomRange(1200, 2000), token);
                    // 第二步：立即预订
                    var reserveButton = ctx.Page!.GetByText(new Regex(@"^立即预[定订]$"));
                    if (await CDPHelper.WaitForAsync(reserveButton, 5000))
                    {
                        await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, reserveButton);
                        await Task.Delay(CommonHelper.RandomRange(1200, 2000), token);
                        // 第二步：等待二次确认弹窗
                        var continueButton = ctx.Page!.GetByText("我已知悉，继续前往", new()
                        {
                            Exact = true
                        });
                        if (await CDPHelper.WaitForAsync(continueButton, 5000))
                        {
                            var result = await ClickAndDetectNavigationAsync(ctx, continueButton, token);
                            if (result.Navigated)
                            {
                                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                var acceptCookies = ctx.Page!.GetByRole(
                                    AriaRole.Button,
                                    new() { Name = "Accept All Cookies" }
                                );
                                if (await CDPHelper.WaitForAsync(acceptCookies, 10000))
                                {
                                    await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, acceptCookies);
                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                }
                            }
                        }
                    }



                }
            }
            else
            {
                // 第二步：立即预定
                var reserveButton = ctx.Page!.GetByText(new Regex(@"^立即预[定订]$"));

                if (await CDPHelper.WaitForAsync(reserveButton, 5000))
                {
                    await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, reserveButton);
                    await Task.Delay(CommonHelper.RandomRange(1200, 2000), token);
                    // 第二步：等待二次确认弹窗
                    var continueButton = ctx.Page!.GetByText("我已知悉，继续前往", new()
                    {
                        Exact = true
                    });
                    if (await CDPHelper.WaitForAsync(continueButton, 5000))
                    {
                        var result = await ClickAndDetectNavigationAsync(ctx, continueButton, token);
                        if (result.Navigated)
                        {
                            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                            var acceptCookies = ctx.Page!.GetByRole(
                                AriaRole.Button,
                                new() { Name = "Accept All Cookies" }
                            );
                            if (await CDPHelper.WaitForAsync(acceptCookies, 10000))
                            {
                                await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, acceptCookies);
                                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                            }
                        }
                    }
                }

            }

            this.QTPExecuteClickthrough(ctx.Config.TaskId);
            LogWriteLine($"{this.Title}:ExecuteWorker:Clickthrough");
            ctx.PageTriggerClick = true;
            await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
            var closeButton = ctx.Page!.Locator(".van-popup .van-icon-close").First;
            if (await CDPHelper.WaitForAsync(closeButton, 5000))
            {
                await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, closeButton);
            }
            return await HandleLandingPageAsync(ctx, token);
        }

        #endregion

        #region Landing Dispatcher / Strategies

        private async Task<FlowControl> HandleLandingPageAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (ctx.LandingDispatcher == null)
                return FlowControl.Continue;

            return await ctx.LandingDispatcher.DispatchAsync(ctx, token);
        }









        #endregion

        #region Generic Landing Helpers


        #endregion

        #region Task Sleep Phase

        private async Task<FlowControl> ExecuteTaskSleepPhaseAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
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
            DateTime start = DateTime.Now;
            LogWriteLine("延时停留");
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    LogWriteLine("滑动操作");
                    var closeButton = ctx.Page!.Locator(".van-popup .van-icon-close").First;
                    if (await CDPHelper.WaitForAsync(closeButton, 5000))
                    {
                        await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, closeButton);
                    }
                    await ctx.human.BrowseOnceAsync(ctx.Page!, ctx.CdpSession!, token);
                    if ((int)(DateTime.Now - start).TotalMilliseconds >= ctx.Config.SleepMs)
                        break;

                    await Task.Delay(CommonHelper.RandomRange(1500, 2500), token);
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
            await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);

            if (entry.FirstPageUrl!.StartsWith("https://visa-h5.offerpluscn.com/travel?"))
            {
                var acceptButton = ctx.Page!.GetByRole(AriaRole.Button, new()
                {
                    Name = "接受"
                });
                if (await CDPHelper.WaitForAsync(acceptButton, 5000))
                {
                    await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, acceptButton);
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                    var sectionTitle = ctx.Page!
                    .Locator(".section-title-row")
                    .Filter(new()
                    {
                        Has = ctx.Page!
                            .Locator(".section-title-main")
                            .Filter(new()
                            {
                                HasTextRegex = new Regex(@"^机票$")
                            })
                    });

                    var firstCard = sectionTitle
                        .Locator("xpath=following-sibling::*[1]")
                        .Locator(".card-half-item")
                        .First;

                    var resultClicked = await ClickAndDetectNavigationAsync(ctx, firstCard, token);
                    if (resultClicked.Navigated)
                    {
                        await Task.Delay(CommonHelper.RandomRange(1200, 2000), token);
                        // 第二步：立即预订
                        var reserveButton = ctx.Page!.GetByText(new Regex(@"^立即预[定订]$"));
                        if (await CDPHelper.WaitForAsync(reserveButton, 5000))
                        {
                            await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, reserveButton);
                            await Task.Delay(CommonHelper.RandomRange(1200, 2000), token);
                            // 第二步：等待二次确认弹窗
                            var continueButton = ctx.Page!.GetByText("我已知悉，继续前往", new()
                            {
                                Exact = true
                            });
                            if (await CDPHelper.WaitForAsync(continueButton, 5000))
                            {
                                var result = await ClickAndDetectNavigationAsync(ctx, continueButton, token);
                                if (result.Navigated)
                                {
                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                    var acceptCookies = ctx.Page!.GetByRole(
                                        AriaRole.Button,
                                        new() { Name = "Accept All Cookies" }
                                    );
                                    if (await CDPHelper.WaitForAsync(acceptCookies, 10000))
                                    {
                                        await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, acceptCookies);
                                        await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                    }
                                }
                            }
                        }

                    }
                }
            }
            else
            {
                var acceptButton = ctx.Page!.GetByRole(AriaRole.Button, new()
                {
                    Name = "接受"
                });
                if (await CDPHelper.WaitForAsync(acceptButton, 5000))
                {
                    await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, acceptButton);
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                    // 第二步：立即预定
                    var reserveButton = ctx.Page!.GetByText(new Regex(@"^立即预[定订]$"));

                    if (await CDPHelper.WaitForAsync(reserveButton, 5000))
                    {
                        await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, reserveButton);
                        await Task.Delay(CommonHelper.RandomRange(1200, 2000), token);
                        // 第二步：等待二次确认弹窗
                        var continueButton = ctx.Page!.GetByText("我已知悉，继续前往", new()
                        {
                            Exact = true
                        });
                        if (await CDPHelper.WaitForAsync(continueButton, 5000))
                        {
                            var result = await ClickAndDetectNavigationAsync(ctx, continueButton, token);
                            if (result.Navigated)
                            {
                                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                var acceptCookies = ctx.Page!.GetByRole(
                                    AriaRole.Button,
                                    new() { Name = "Accept All Cookies" }
                                );
                                if (await CDPHelper.WaitForAsync(acceptCookies, 10000))
                                {
                                    await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, acceptCookies);
                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                }
                            }
                        }
                    }
                }
            }

            var closeButton = ctx.Page!.Locator(".van-popup .van-icon-close").First;
            if (await CDPHelper.WaitForAsync(closeButton, 5000))
            {
                await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, closeButton);
            }
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
                await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, element);
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
                await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, element);
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
