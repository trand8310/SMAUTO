using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using QTP.Plugins.LandingPolicy;
using QTP.Plugins.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace QTP.Plugins
{
    public sealed class Ali1688PCTask : QTPServiceBase
    {
        private const string AliAppDownloadModalCloseSelector = ".androidOpenModal .closeBtn, .iosOpenModal .closeIcon";
        private int _disposeStarted;
        private readonly ConcurrentDictionary<string, WorkerRunContext> _activeContexts = new();
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
                ClassName = "QTP.Plugins.Ali1688PCTask",
                Name = "Ali1688PC",
                FileName = "Ali1688PC.dll",
            };
        }
        public override string Title => "Ali1688";


        private readonly TaskStatsAggregator _aggregator;
        private readonly AdeHelper _adeHelper;
        private ChineseNameGenerator _nameGenerator;
        private readonly IPlaywrightProvider _playwrightProvider;


        public Ali1688PCTask(
            IPlaywrightProvider playwrightProvider,
            TaskStatsAggregator aggregator, AdeHelper adeHelper, ChineseNameGenerator nameGenerator, AppSettings appSettings) : base(appSettings)
        {
            _playwrightProvider = playwrightProvider;
            _aggregator = aggregator;
            _adeHelper = adeHelper;
            _nameGenerator = nameGenerator;
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
                                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
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

     
      
        private static readonly TimeSpan CleanupStepTimeout = TimeSpan.FromSeconds(8);
        private async Task RunCleanupStepAsync(string uniqueId, string stepName, Func<Task> cleanupAction)
        {
            var sw = Stopwatch.StartNew();
            Task cleanupTask;
            try
            {
                cleanupTask = cleanupAction();
            }
            catch (Exception ex)
            {
                LogWriteLine($"{this.Title}:ForceCleanup:{uniqueId}:{stepName} 启动异常: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            var timeoutTask = Task.Delay(CleanupStepTimeout);
            if (await Task.WhenAny(cleanupTask, timeoutTask) != cleanupTask)
            {
                LogWriteLine($"{this.Title}:ForceCleanup:{uniqueId}:{stepName} 超过 {CleanupStepTimeout.TotalSeconds:N0}s 仍未完成，跳过等待，避免清理流程卡死");
                _ = cleanupTask.ContinueWith(t =>
                {
                    LogWriteLine($"{this.Title}:ForceCleanup:{uniqueId}:{stepName} 超时后最终异常: {t.Exception?.GetBaseException().GetType().Name}: {t.Exception?.GetBaseException().Message}");
                }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                return;
            }

            try
            {
                await cleanupTask;
                sw.Stop();
                if (sw.Elapsed > TimeSpan.FromSeconds(1))
                    LogWriteLine($"{this.Title}:ForceCleanup:{uniqueId}:{stepName} 完成，耗时 {sw.Elapsed.TotalSeconds:N2}s");
            }
            catch (Exception ex)
            {
                sw.Stop();
                LogWriteLine($"{this.Title}:ForceCleanup:{uniqueId}:{stepName} 异常，耗时 {sw.Elapsed.TotalSeconds:N2}s: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static List<string> InitFPArgs(TaskConfig config)
        {
            JToken taskArgs = config.TaskArgs;
            int maxTouchPoints = config.MaxTouchPoints;
            var result = new List<string>();

            uint hash_code = (uint)Math.Abs($"{taskArgs.ToString()}".GetHashCode()) % 1048560;
            uint fingerprint = hash_code;
            config.Fingerprint = (int)fingerprint;

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
                result.Add($"--brand-version-list={JsonConvert.SerializeObject(taskArgs.SelectToken("dev.brands"), Formatting.None)}");
            }

            if (taskArgs.SelectToken("dev.fullVersionList") == null || taskArgs.SelectToken("dev.fullVersionList").Count() == 0)
            {
                result.Add($"--disable-full-version-list");
            }
            else
            {
                result.Add($"--full-version-list={JsonConvert.SerializeObject(taskArgs.SelectToken("dev.brands"), Formatting.None)}");
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

        private async Task<IBrowser> LaunchBrowserWithRetryAsync(
            IPlaywright playwright,
            string chromePath,
            IReadOnlyList<string> args,
            TaskConfig config,
            string traceTag,
            CancellationToken token,
            int maxAttempts = 3,
            int delayMs = 200)
        {
            if (playwright == null)
                throw new ArgumentNullException(nameof(playwright));
            if (string.IsNullOrWhiteSpace(chromePath))
                throw new ArgumentException("Chromium executable path cannot be null or empty.", nameof(chromePath));
            if (args == null)
                throw new ArgumentNullException(nameof(args));
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (maxAttempts <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            if (delayMs < 0)
                throw new ArgumentOutOfRangeException(nameof(delayMs));

            Exception? lastException = null;

            var proxyServer = string.Empty;
            var isProxyMode = config.TaskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            if (isProxyMode)
            {
                proxyServer = config.TaskArgs.SelectToken("proxy_server")!.Value<string>();
                var protocol = config.TaskArgs.SelectToken("protocol")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(protocol) && protocol.Equals("socks5"))
                {
                    proxyServer = $"socks5://{proxyServer}";
                    //--proxy-server="socks5://127.0.0.1:1080" ^
                    //--disable-features=DnsOverHttps ^
                    //--host-resolver-rules="MAP * ~NOTFOUND , EXCLUDE 127.0.0.1"
                }
            }

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                IBrowser? browser = null;
                try
                {
                    var launchOptions = new BrowserTypeLaunchOptions
                    {

                        Headless = IsHiddenMode(config),
                        ChromiumSandbox = true,
                        IgnoreDefaultArgs = new List<string>()
                        {
                            "--enable-automation",
                            "--use-fake-ui-for-media-stream",
                            "--use-fake-device-for-media-stream",
                        },
                        ExecutablePath = chromePath,
                        Args = args,
                        Timeout = 15000
                    };
                    if (isProxyMode)
                    {
                        launchOptions.Proxy = new Proxy { Server = proxyServer! };
                    }

                    browser = await playwright.Chromium.LaunchAsync(launchOptions);

                    if (browser == null)
                        throw new InvalidOperationException("LaunchAsync returned null browser.");

                    if (!browser.IsConnected)
                        throw new InvalidOperationException("Browser is not connected after LaunchAsync.");

                    var context = await browser.NewContextAsync(BuildBrowserContextOptions(config));

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

                    LogWriteLine($"{traceTag} LaunchAsync启动失败 {attempt}/{maxAttempts}: {ex.Message}");

                    if (attempt >= maxAttempts)
                        break;

                    await Task.Delay(delayMs, token);
                }
            }

            LogWriteLine($"{traceTag} LaunchAsync启动最终失败: {lastException}");
            throw new InvalidOperationException("Chromium LaunchAsync failed after retries.", lastException);
        }

        private static BrowserNewContextOptions BuildBrowserContextOptions(TaskConfig config)
        {
            return new BrowserNewContextOptions
            {
                UserAgent = config.UserAgent,
                ViewportSize = new ViewportSize
                {
                    Width = config.Sw,
                    Height = config.Sh,
                },
                ScreenSize = new ScreenSize
                {
                    Width = config.Sw,
                    Height = config.Sh
                },
                DeviceScaleFactor = config.DeviceScale,
                IsMobile = false,
                HasTouch = false,
                IgnoreHTTPSErrors = true,
                Locale = "zh-CN",
                TimezoneId = "Asia/Shanghai",
            };
        }
        private static bool IsHiddenMode(TaskConfig config)
        {
            return config.TaskArgs.SelectToken("isHiddenMode")?.Value<bool>() ?? false;
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

        public override async Task<WorkerExecutionResult> ExecuteWorkerAsync(string uniqueId, JObject taskArgs, CancellationToken token)
        {
            WorkerRunContext? ctx = null;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);

            try
            {
                ctx = CreateWorkerRunContext(uniqueId, taskArgs, linkedCts);
                _activeContexts[uniqueId] = ctx;
                this.QTPExecuteStart(ctx.Config.TaskId);
                LogWriteLine($"{this.Title}:ExecuteWorker:Start");
                var initializationFailure = await InitializeWorkerAsync(ctx, linkedCts.Token);
                if (initializationFailure != null)
                    return initializationFailure;

                return await ExecuteMainFlowAndBuildResultAsync(ctx, linkedCts.Token);
            }
            catch (OperationCanceledException)
            {
                var result = BuildFailureResult(ctx, WorkerFailureKind.Canceled, ctx?.LastFailureReason);
                if (ctx?.ProxyFailed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled(代理异常): {ctx.ProxyFailedReason}");
                }
                else if (ctx?.PageCrashed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled(页面崩溃): {ctx.LastFailureReason}");
                }
                else if (ctx != null && !string.IsNullOrWhiteSpace(ctx.LastFailureReason))
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled: {ctx.LastFailureReason}");
                }
                else
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:Canceled");
                }

                return result;
            }
            catch (PlaywrightException ex)
            {
                if (ctx != null)
                    ctx.LastFailureReason = ex.Message;

                var result = BuildFailureResult(ctx, WorkerFailureKind.PlaywrightException, ex.Message);
                if (ctx?.ProxyFailed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:PlaywrightException(代理异常): {ctx.ProxyFailedReason}");
                }
                else if (ctx?.PageCrashed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:PlaywrightException(页面崩溃): {ctx.LastFailureReason}");
                }
                else
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:PlaywrightException: {ex}");
                }

                return result;
            }
            catch (Exception ex)
            {
                if (ctx != null)
                    ctx.LastFailureReason = ex.Message;

                var result = BuildFailureResult(ctx, WorkerFailureKind.UnhandledException, ex.Message);
                if (ctx?.ProxyFailed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:异常(代理异常): {ctx.ProxyFailedReason}");
                }
                else if (ctx?.PageCrashed == true)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:异常(页面崩溃): {ctx.LastFailureReason}");
                }
                else
                {
                    LogWriteLine(ex.ToString());
                }

                return result;
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
                        await ForceCleanupSessionAsync(ctx, uniqueId);
                    }
                    finally
                    {
                        _activeContexts.TryRemove(uniqueId, out _);
                    }
                }

                //await CleanupWorkerAsync(ctx, uniqueId, linkedCts);
            }
        }

        private WorkerRunContext CreateWorkerRunContext(string uniqueId, JObject taskArgs, CancellationTokenSource linkedCts)
        {
            var config = BuildTaskConfig(uniqueId, taskArgs, linkedCts);

            return new WorkerRunContext(config)
            {
                LandingDispatcher = new LandingPageStrategyDispatcher(new ILandingPageStrategy[]
                {
                    new TobaoPageStrategy(this),
                    new DefaultLandingPageStrategy(this),
                })
            };
        }

        private async Task<WorkerExecutionResult?> InitializeWorkerAsync(WorkerRunContext ctx, CancellationToken token)
        {
            ctx.Playwright = await _playwrightProvider.GetAsync();
            token.ThrowIfCancellationRequested();

            var browser = await StartAndLaunchBrowserAsync(ctx, token);
            if (browser == null)
            {
                if (ctx.ProxyFailed)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:浏览器/Context建立失败，疑似代理异常: {ctx.ProxyFailedReason}");
                    return BuildFailureResult(ctx, WorkerFailureKind.ProxyFailed, ctx.ProxyFailedReason);
                }

                LogWriteLine($"{this.Title}:ExecuteWorker:浏览器启动或Context创建失败");
                return BuildFailureResult(ctx, WorkerFailureKind.BrowserStartFailed, ctx.LastFailureReason);
            }

            ctx.Browser = browser;

            if (!ctx.Browser.IsConnected)
            {
                ctx.ProxyFailed = true;
                ctx.ProxyFailedReason ??= "Browser.IsConnected == false";
                LogWriteLine($"{this.Title}:ExecuteWorker:Browser未连接: {ctx.ProxyFailedReason}");
                return BuildFailureResult(ctx, WorkerFailureKind.BrowserDisconnected, ctx.ProxyFailedReason);
            }

            if (ctx.Browser.Contexts == null || ctx.Browser.Contexts.Count == 0)
            {
                ctx.ProxyFailed = true;
                ctx.ProxyFailedReason ??= "Browser.Contexts.Count == 0";
                LogWriteLine($"{this.Title}:ExecuteWorker:Browser无可用Context: {ctx.ProxyFailedReason}");
                return BuildFailureResult(ctx, WorkerFailureKind.NoBrowserContext, ctx.ProxyFailedReason);
            }

            ctx.Context = ctx.Browser.Contexts[0];
            ctx.CdpManager = new CDPSessionManager(ctx.Context);
            await AttachLifecycleEventsAsync(ctx, token);
            await ConfigureContextAsync(ctx, token);



            if (ctx.ProxyFailed)
            {
                LogWriteLine($"{this.Title}:ExecuteWorker:初始化阶段已判定代理异常: {ctx.ProxyFailedReason}");
                return BuildFailureResult(ctx, WorkerFailureKind.ProxyFailed, ctx.ProxyFailedReason);
            }

            if (ctx.PageCrashed)
            {
                LogWriteLine($"{this.Title}:ExecuteWorker:初始化阶段页面崩溃: {ctx.LastFailureReason}");
                return BuildFailureResult(ctx, WorkerFailureKind.PageCrashed, ctx.LastFailureReason);
            }

            return null;
        }

        private async Task<WorkerExecutionResult> ExecuteMainFlowAndBuildResultAsync(WorkerRunContext ctx, CancellationToken token)
        {
            var ok = await RunMainFlowAsync(ctx, token);
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

                return BuildFailureResult(ctx, WorkerFailureKind.MainFlowFailed, ctx.LastFailureReason);
            }

            return WorkerExecutionResult.Success(ctx.PageTriggerClick, ctx.PageAdsCount);
        }

        private static WorkerExecutionResult BuildFailureResult(
            WorkerRunContext? ctx,
            WorkerFailureKind fallbackFailureKind,
            string? fallbackReason)
        {
            if (ctx?.ProxyFailed == true && ShouldPreferContextFailure(fallbackFailureKind))
            {
                return WorkerExecutionResult.Failure(
                    WorkerFailureKind.ProxyFailed,
                    ctx.ProxyFailedReason ?? fallbackReason,
                    ctx.PageTriggerClick,
                    ctx.PageAdsCount);
            }

            if (ctx?.PageCrashed == true && ShouldPreferContextFailure(fallbackFailureKind))
            {
                return WorkerExecutionResult.Failure(
                    WorkerFailureKind.PageCrashed,
                    ctx.LastFailureReason ?? fallbackReason,
                    ctx.PageTriggerClick,
                    ctx.PageAdsCount);
            }

            return WorkerExecutionResult.Failure(
                fallbackFailureKind,
                ctx?.LastFailureReason ?? fallbackReason,
                ctx?.PageTriggerClick ?? false,
                ctx?.PageAdsCount ?? 0);
        }


        private static bool ShouldPreferContextFailure(WorkerFailureKind fallbackFailureKind) =>
            fallbackFailureKind is WorkerFailureKind.Canceled
                or WorkerFailureKind.PlaywrightException
                or WorkerFailureKind.UnhandledException
                or WorkerFailureKind.MainFlowFailed;


        public static async Task SafeClosePageAsync(IPage? page)
        {
            if (page == null) return;

            try
            {
                if (!page.IsClosed)
                {
                    await page.CloseAsync(new PageCloseOptions
                    {
                        RunBeforeUnload = false
                    });
                }
            }
            catch
            {
            }
        }
        public static async Task SafeCloseContextAsync(IBrowserContext? context)
        {
            if (context == null) return;

            try
            {
                await context.CloseAsync();
            }
            catch
            {
            }
        }
        public static async Task SafeCloseBrowserAsync(IBrowser? browser)
        {
            if (browser == null) return;

            try
            {
                if (browser.IsConnected)
                {
                    await browser.CloseAsync();
                }
            }
            catch
            {
                try
                {
                    await browser.DisposeAsync();
                }
                catch
                {
                }
            }
        }

        private async Task CleanupWorkerAsync(WorkerRunContext? ctx, string uniqueId, CancellationTokenSource linkedCts)
        {
            try
            {
                if (!linkedCts.IsCancellationRequested)
                    linkedCts.Cancel();
            }
            catch
            {
            }

            if (ctx == null)
                return;

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
        }

        private async Task ForceCleanupSessionAsync(WorkerRunContext ctx, string uniqueId)
        {
            await ctx.CleanupLock.WaitAsync();
            try
            {
                // Playwright 推荐的释放顺序：
                // 1. 先释放/分离我们额外创建的 CDP session，避免后续关闭页面时还有 CDP 监听或命令挂着。
                // 2. 再关闭显式创建的 BrowserContext，让页面 close 事件、HAR/video 等上下文产物有机会正常落盘。
                // 3. 最后关闭 Browser；Playwright 文档说明 Browser.CloseAsync 更接近强制退出浏览器，且调用后 Browser 不可再用。
                // IPlaywright 由 PlaywrightProvider 单例统一持有，不能在单个 worker 清理时 Dispose，否则会影响其它 worker。

                var cdpManager = ctx.CdpManager;
                if (cdpManager != null)
                    await RunCleanupStepAsync(uniqueId, "CDP.DetachAsync", () => cdpManager.DisposeAsync().AsTask());

                var browserContext = ctx.Context;
                if (browserContext != null)
                    await RunCleanupStepAsync(uniqueId, "Context.CloseAsync", () => browserContext.CloseAsync());

                var browser = ctx.Browser;
                if (browser != null)
                {
                    if (browser.IsConnected)
                    {
                        await RunCleanupStepAsync(uniqueId, "Browser.CloseAsync", () => browser.CloseAsync(new BrowserCloseOptions
                        {
                            Reason = $"ForceCleanupSession:{uniqueId}"
                        }));
                    }
                    else
                    {
                        LogWriteLine($"{this.Title}:ForceCleanup:{uniqueId}:Browser.CloseAsync 跳过，Browser 已断开连接");
                    }
                }

                ctx.CdpManager = null;
                ctx.Context = null;
                ctx.Browser = null;
            }
            finally
            {
                ctx.CleanupLock.Release();
            }
        }

        public override async Task ForceStopWorkerAsync(string uniqueId, string reason, CancellationToken token = default)
        {
            if (!_activeContexts.TryGetValue(uniqueId, out var ctx))
                return;
            LogWriteLine($"{this.Title}:ExecuteWorker:ForceStop: {reason}");
            try
            {
                if (!ctx.Config.LinkedCts.IsCancellationRequested)
                    await ctx.Config.LinkedCts.CancelAsync();
            }
            catch
            {
            }
            token.ThrowIfCancellationRequested();
            await ForceCleanupSessionAsync(ctx, uniqueId);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposeStarted, 1) == 1)
                return;

            try
            {
                foreach (var pair in _activeContexts.ToArray())
                {
                    var activeUniqueId = pair.Key;
                    var activeContext = pair.Value;

                    try
                    {
                        if (!activeContext.Config.LinkedCts.IsCancellationRequested)
                            await activeContext.Config.LinkedCts.CancelAsync();
                    }
                    catch (Exception ex)
                    {
                        LogWriteLine($"{this.Title}:DisposeAsync:{activeUniqueId}:取消任务异常: {ex.GetType().Name}: {ex.Message}");
                    }

                    try
                    {
                        await ForceCleanupSessionAsync(activeContext, activeUniqueId);
                    }
                    finally
                    {
                        _activeContexts.TryRemove(activeUniqueId, out _);
                    }
                }
            }
            finally
            {
                await base.DisposeAsync();
            }
        }


        #endregion

        #region Main Flow

        private async Task<bool> RunMainFlowAsync(WorkerRunContext ctx, CancellationToken token)
        {


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


                if (entry.IsHomepageTrigger)
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker: ExecuteHomepageTriggerAsync");
                    var homepageOk = await ExecuteHomepageTriggerAsync(ctx, entry.QueryWord, token);
                    if (!homepageOk)
                        continue;




                }
                else
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker: {((ctx.Config.PageLoadedDelayMs) / 1000.0):N2}");
                    var delayMs = CommonHelper.RandomRange(5000, 8000);
                    await Task.Delay(delayMs, token);
                    var restMs = Math.Max(0, ctx.Config.PageLoadedDelayMs - delayMs);
                    if (restMs > 300)
                    {
                        await ctx.HumanSession!
                        .For(ctx.Page!)
                        .BrowsePageAsync(1, 2, token);
                    }
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


                await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                var adsOk = await DetectAndUploadAdWordsAsync(ctx, entry.QueryWord, token);
                if (!adsOk)
                    continue;

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
                    await ctx.HumanSession!
                    .For(ctx.Page!)
                    .BrowsePageAsync(2, 6, token);

                    var clickFlow = await TryExecuteJumpClickAsync(ctx, token);
                    if (clickFlow == FlowControl.EndTask)
                        return CompleteSuccess(ctx);
                }
                else
                {
                    if (CommonHelper.Chance(0.2))
                    {
                        await ctx.HumanSession!
                        .For(ctx.Page!)
                        .BrowsePageAsync(1, 3, token);

                        await ClickNoneAdAsync(ctx, token);
                    }
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
            var sw1 = taskArgs.SelectToken("dev.screen.width")?.Value<int>() ?? 1920;
            var sh1 = taskArgs.SelectToken("dev.screen.height")?.Value<int>() ?? 1080;
            var profileResult = WindowsViewportMatcher.Match(sw1, sh1);
            profileResult.DeviceScaleFactor = taskArgs.SelectToken("dev.screen.devicePixelRatio")?.Value<float>() ?? 1.0f;
            profileResult.CssWidth = sw1;
            profileResult.CssHeight = sh1;
            int sw = profileResult.CssWidth;
            int sh = profileResult.CssHeight;
            float deviceScale = profileResult.DeviceScaleFactor;
            var maxTouchPoints = 0;

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
                TaskHomeUrl = taskArgs.SelectToken("task.referer")?.Value<string>(),
                SleepMs = ParseSleepMilliseconds(taskArgs),
                IsLocalAdWord = taskArgs.SelectToken("isLocalAdWord")?.Value<bool>() ?? false,
                PageLoadingTimeoutMs = taskArgs.SelectToken("pageLoadingTimeout")?.Value<int>() * 1000 ?? 30000,
                PageLoadedDelayMs = ParsePageLoadedDelayMilliseconds(taskArgs),
                UserAgent = taskArgs.SelectToken("dev.ua")!.Value<string>(),
                Os = os,
                DeviceScale = deviceScale,
                Sw = sw,
                Sh = sh,
                WordName = taskArgs.SelectToken("wordname")?.Value<string>() ?? "default",
                CleaningWords = taskArgs.SelectToken("cleaningWords")?.Value<bool>() ?? false,
                NotTriggerDownload = taskArgs.SelectToken("notTriggerDownload")?.Value<bool>() ?? false,
                PvsTriggerOne = taskArgs.SelectToken("pvsTriggerOne")?.Value<bool>() ?? true,
                CurrentUV = taskArgs.SelectToken("currentUV")?.Value<int>() ?? 0,
                KernelVersion = kernelVersion,
                MaxTouchPoints = maxTouchPoints,
                ProcessIndex = processIndex,
                IsTest = taskArgs.SelectToken("isTest")?.Value<bool>() ?? false,
                TotalPV = taskArgs.SelectToken("totalPV")?.Value<int>() ?? 1,
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

        private async Task<IBrowser?> StartAndLaunchBrowserAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var args = BuildChromiumArgs(ctx.Config);
            var chromePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "File", "chrome-win", ctx.Config.KernelVersion, "chrome.exe");
            token.ThrowIfCancellationRequested();
            return await LaunchBrowserWithRetryAsync(
                ctx.Playwright!,
                chromePath,
                args,
                ctx.Config,
                BuildTraceTag(ctx),
                token,
                maxAttempts: 3,
                delayMs: 200);
        }

        private List<string> BuildChromiumArgs(TaskConfig config)
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
                $"--user-agent={config.UserAgent}",
                $"--window-position=0,0",
                $"--window-size={sw},{sh}",
                $"--screen-size={sw},{sh}",
                $"--screen-avail-size={availWidth},{availHeight}",
                $"--device-pixel-ratio={config.DeviceScale}",
            };
            if (_appSettings.BlockImage || _appSettings.BlockMedia)
            {
                args.Add("--autoplay-policy=user-gesture-required");
            }

            if (_appSettings.BlockImage)
            {
                args.Add("--blink-settings=imagesEnabled=false");
            }
            args.Add($"--fingerprint-config-dir={System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fingerprint")}");
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
            var initialPage = await ctx.Context.NewPageAsync();
            ctx.InitializeHumanSession();
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
            if (_appSettings.BlockImage && _appSettings.BlockMedia)
            {
                await page.RouteAsync("**/*", async route =>
                {
                    var request = route.Request;
                    var url = request.Url;
                    var type = request.ResourceType;
                    if (type == "media" || BDAdHelper.IsBlockedMediaUrl(url))
                    {
                        await route.AbortAsync();
                        return;
                    }

                    await route.ContinueAsync();
                });
            }
            await page.SetViewportSizeAsync(ctx.Config.Sw, ctx.Config.Sh);

            var cdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(page);
            //var cdpSession2 = ctx.CdpSession!;

            await cdpSession.SendAsync("Page.enable");
            cdpSession.Event("Page.downloadWillBegin").OnEvent += (_, _) =>
            {
                Interlocked.Increment(ref ctx.TriggerDownloadSign);
            };

            //await CDPHelper.InitCDPSession(cdpSession, ctx.Config.MaxTouchPoints);
            //await CDPHelper.SetDeviceMetricsOverride(cdpSession, ctx.Config.Sw, ctx.Config.Sh, ctx.Config.DeviceScale, false);
            //await CDPHelper.SetUserAgentOverride(cdpSession,
            //    ctx.Config.UserAgent,
            //    platformVersion: ctx.Config.TaskArgs.SelectToken("dev.platformVersion")?.Value<string>(),
            //    brands: ctx.Config.TaskArgs.SelectToken("dev.brands"),
            //    fullVersionList: ctx.Config.TaskArgs.SelectToken("dev.fullVersionList"));

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

            page.Download += async (_, download) =>
            {
                Interlocked.Increment(ref ctx.TriggerDownloadSign);
                await Task.Delay(CommonHelper.RandomRange(800, 2000), ctx.Config.LinkedCts.Token);
                try { await download.CancelAsync(); } catch { }
            };

            page.Close += (_, closedPage) =>
            {
                try { ctx.ClearActivePage(closedPage); } catch { }
            };

            ctx.SetActivePage(page, cdpSession);

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
            var initialPage = ctx.Context.Pages[0];
            var cdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(initialPage);
            ctx.SetActivePage(initialPage, cdpSession);
        }

        private string BuildTraceTag(WorkerRunContext ctx)
        {
            return $"{this.Title}[taskId={ctx.Config.TaskId},uniqueId={ctx.Config.UniqueId},uv={ctx.Config.CurrentUV},pv={ctx.PvIndex}]";
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
                //https://www.baidu.com/index.php?tn=02049043_24_pg
                //https://www.baidu.com/s?&tn=02049043_24_pg&wd=[QUERY]
                result.FirstPageUrl = ctx.Config.TaskHomeUrl;
                result.IsHomepageTrigger = true;
                _aggregator.Enqueue(new TaskEvent(ctx.Config.TaskId, StateType.HomepageTrigger, 1));
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
                    if (!title.StartsWith("百度搜索") && !title.StartsWith("搜索"))
                        return false;
                }

            }

            ctx.CurrentPageUrl = ctx.Page!.Url;
            ctx.PagesCount = ctx.Context!.Pages.Count;

            this.QTPExecuteDSP(ctx.Config.TaskId);
            return true;
        }

        private async Task<bool> ExecuteHomepageTriggerAsync(WorkerRunContext ctx, string? q, CancellationToken token)
        {
            var retry = await RetryPolicy.ExecuteBoolAsync(
                async ct =>
                {
                    ct.ThrowIfCancellationRequested();

                    var word = q;
                    if (string.IsNullOrWhiteSpace(word))
                    {
                        word = await _adeHelper.GetWordAsync();
                        if (string.IsNullOrWhiteSpace(word))
                            return false;
                    }

                    var input = ctx.Page!.Locator("textarea#chat-textarea");
                    if (await input.CountAsync() == 0)
                    {
                        LogWriteLine($"{this.Title}:输入框不存在");
                        return false;
                    }

                    await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, input);
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), ct);

                    await input.PressSequentiallyAsync(word!, new LocatorPressSequentiallyOptions
                    {
                        Delay = CommonHelper.RandomRange(50, 200)
                    });

                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), ct);

                    var btn = ctx.Page.Locator("button#chat-submit-button");
                    if (await btn.CountAsync() == 0)
                    {
                        LogWriteLine($"{this.Title}:搜索按钮不存在");
                        return false;
                    }




                    ctx.CurrentPageUrl = ctx.Page.Url;
                    if (CommonHelper.Chance(0.5))
                    {
                        //await input.PressAsync("Enter");
                        await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, btn.First);
                    }
                    else
                    {
                        await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, btn.First);
                    }

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

                    LogWriteLine($"{this.Title}:搜索完成");
                    await Task.Delay(CommonHelper.RandomRange(8000, 12000), ct);
                    return true;
                },
                maxAttempts: 2,
                onRetry: (attempt, ex) =>
                {
                    if (ex != null)
                        LogWriteLine($"{this.Title}:搜索操作重试,{attempt},{ex.Message}");
                    else
                        LogWriteLine($"{this.Title}:搜索操作重试,{attempt}");
                },
                token: token);

            return retry.IsSuccess;
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

                var ads = ctx.Page!.Locator("span.ec-tuiguang:text-is(\"广告\")");
                ctx.PageAdsCount = await ads.CountAsync();
                if (ctx.PageAdsCount <= 0)
                {
                    LogWriteLine("没有广告标记,重试");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(q))
                    return true;

                _aggregator.EnqueueAdWordHit(q);

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

            var sponsoreds = ctx.Page!
            .Locator(".c-container")
            .Filter(new LocatorFilterOptions
            {
                Has = ctx.Page!
                    .Locator("span.ec-tuiguang:text-is(\"广告\")")
            });

            var count = await sponsoreds.CountAsync();
            if (count <= 0)
            {
                return FlowControl.Continue;
            }

            var candidates = await BuildSponsoredCandidatesAsync(ctx, sponsoreds, count, token);

            var human = ctx.HumanSession!.For(ctx.Page!);


            await ctx.HumanSession!
            .For(ctx.Page!)
            .BrowsePageAsync(0, 3, token);

            foreach (var sponsored in candidates)
            {
                token.ThrowIfCancellationRequested();

                await human.ScrollToElementAsync(sponsored,
                maxScrollAttempts: 20,
                targetViewportRatio: 0.48f,
                cancellationToken: token);

                var alis = sponsored.Locator("a");
                var alis_count = await alis.CountAsync();
                if (alis_count == 0)
                {
                    continue;
                }
                var target = alis.Nth(CommonHelper.RandomRange(0, alis_count));
                var text = await target.InnerTextAsync();
                var box = await target.BoundingBoxAsync();
                if (box != null)
                    LogWriteLine($"触发广告位:{text}:({box.X},{box.Y},{box.Width},{box.Height})");
                else
                    continue;



                var click = await ClickAndDetectNavigationAsync(ctx, sponsored, token);
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
                    await Task.Delay(CommonHelper.RandomRange(5000, 8000), token);
                    return await HandleLandingPageAsync(ctx, token);
                }
            }
            return FlowControl.Continue;
        }



        private async Task<FlowControl> ClickNoneAdAsync(WorkerRunContext ctx, CancellationToken token)
        {

            token.ThrowIfCancellationRequested();

            var not_sponsoreds = ctx.Page!
            .Locator(".c-container")
            .Filter(new LocatorFilterOptions
            {
                HasNotText = "广告"
            });

            var count = await not_sponsoreds.CountAsync();
            if (count <= 0)
            {
                return FlowControl.Continue;
            }

            var candidates = Enumerable.Range(0, count)
                .OrderBy(_ => Guid.NewGuid())
                .Select(i => not_sponsoreds.Nth(i))
                .ToList();

            var human = ctx.HumanSession!.For(ctx.Page!);


            foreach (var sponsored in candidates)
            {
                token.ThrowIfCancellationRequested();

                await human.ScrollToElementAsync(sponsored,
                maxScrollAttempts: 20,
                targetViewportRatio: 0.48f,
                cancellationToken: token);

                var alis = sponsored.Locator("a");
                var alis_count = await alis.CountAsync();
                if (alis_count == 0)
                {
                    continue;
                }
                var target = alis.Nth(CommonHelper.RandomRange(0, alis_count));
                var text = await target.InnerTextAsync();
                var box = await target.BoundingBoxAsync();
                if (box != null)
                    LogWriteLine($"触发:{text}:({box.X},{box.Y},{box.Width},{box.Height})");
                else
                    continue;

                var click = await ClickAndDetectNavigationAsync(ctx, sponsored, token);
                if (!click.Attempted)
                    continue;

                if (click.Navigated)
                {
                    await Task.Delay(CommonHelper.RandomRange(5000, 8000), token);
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
            await Task.Delay(1);
            return Enumerable.Range(0, count)
                .OrderBy(_ => Guid.NewGuid())
                .Select(i => sponsoreds.Nth(i))
                .ToList();
        }

        private async Task<ILocator?> PickSponsoredTargetAsync(ILocator sponsored, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var alis = sponsored.Locator(".ec_title");
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

            return await ctx.LandingDispatcher.DispatchAsync(ctx, token);
        }

        #endregion

        #region Generic Landing Helpers

        public async Task<ILocator?> ResolveOfferItemsAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var url = ctx.Page!.Url;
            ILocator? offerItems = null;

            if (url.Contains("s.1688.com"))
            {
                await ctx.HumanSession!
                .For(ctx.Page!)
                .BrowsePageAsync(2, 6, token);

                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator(".page-offerlist a.search-offer-item");
            }
            else if (url.Contains("b2b.baidu.com/"))
            {
                await ctx.HumanSession!
                .For(ctx.Page!)
                .BrowsePageAsync(2, 6, token);

                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                offerItems = ctx.Page.Locator(".product-list a");
                if (await offerItems.CountAsync() == 0)
                    offerItems = ctx.Page.Locator(".p-card-name-title,.p-card-img");
            }
            else if (url.Contains("aden.baidu.com") || url.Contains("ada.baidu.com"))
            {
                await Task.Delay(CommonHelper.RandomRange(3000, 5000));
                var info = ctx.Page!.GetByText(
                    new Regex(@"法律咨询|律师|客服")
                );
                var info_count = await info.CountAsync();
                if (info_count > 0)
                {
                    var input_area = ctx.Page!.Locator(".input-area .fake-input");
                    if (await input_area.CountAsync() > 0)
                    {
                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                        await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, input_area.First);
                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                    }
                    input_area = ctx.Page!.Locator(".input-area textarea");
                    if (await input_area.CountAsync() > 0)
                    {
                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                        await input_area.FillAsync("");
                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                        var info_text = await _adeHelper.GetTalkAsync("law");
                        if (!string.IsNullOrWhiteSpace(info_text))
                        {
                            await input_area.PressSequentiallyAsync(info_text);
                            await Task.Delay(CommonHelper.RandomRange(3000, 5000));
                        }
                        else
                        {
                            await input_area.PressSequentiallyAsync("你好,有事咨询");
                        }
                    }
                    var send_btn = ctx.Page!.Locator(".input-area .send-btn");
                    if (await send_btn.CountAsync() > 0)
                    {
                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                        await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, send_btn.First);
                        await Task.Delay(CommonHelper.RandomRange(3000, 5000));
                    }
                }
                else
                {
                    await ctx.HumanSession!
                    .For(ctx.Page!)
                    .BrowsePageAsync(2, 6, token);

                    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                    offerItems = ctx.Page.Locator("//div[contains(@class,'ec_content')]");
                    if (await offerItems.CountAsync() == 0)
                    {
                        for (int i = 1; i < 4; i++)
                        {
                            var locator = ctx.Page
                                .Locator("body,iframe")
                                .Filter(new() { Visible = true })
                                .First;
                            if (await locator.CountAsync() > 0)
                            {
                                await locator.First.ScrollIntoViewIfNeededAsync();
                                var result = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                                if (result.Navigated)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
            }
            else if (url.Contains("uland.taobao.com"))
            {
                await ctx.HumanSession!
                .For(ctx.Page!)
                .BrowsePageAsync(2, 6, token);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("#PageContentContainer a[id^='item_id_']");
            }
            else if (url.StartsWith("https://cunliangtech.com/"))
            {
                await Task.Delay(CommonHelper.RandomRange(3000, 5000));
                var info_handler = async () =>
                {
                    try
                    {
                        var info = ctx.Page!.GetByText(
                            new Regex(@"法律咨询|律师|客服")
                        );
                        var info_count = await info.CountAsync();
                        if (info_count > 0)
                        {
                            var input_area = ctx.Page!.Locator(".input-area .fake-input");
                            if (await input_area.CountAsync() > 0)
                            {
                                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                                await ClickAndDetectNavigationAsync(ctx, input_area.First, token);
                                await Task.Delay(CommonHelper.RandomRange(800, 1200));
                            }
                            input_area = ctx.Page!.Locator(".input-area textarea");
                            if (await input_area.CountAsync() > 0)
                            {
                                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                                await input_area.FillAsync("");
                                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                                var info_text = await _adeHelper.GetTalkAsync("law", token);
                                if (!string.IsNullOrWhiteSpace(info_text))
                                {
                                    await input_area.PressSequentiallyAsync(info_text);
                                }
                                else
                                {
                                    await input_area.PressSequentiallyAsync("你好,有事咨询");
                                }
                            }
                            var send_btn = ctx.Page!.Locator(".input-area .send-btn");
                            if (await send_btn.CountAsync() > 0)
                            {
                                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                                await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, send_btn.First);
                                await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                            }
                        }
                    }
                    catch (Exception)
                    {


                    }


                };

                var locator_list = ctx.Page!.GetByText(
                    new Regex(@"查看更多")
                );
                var locator_count = await locator_list.CountAsync();
                if (locator_count > 0)
                {
                    foreach (var target_index in Enumerable.Range(0, locator_count).OrderBy(o => Guid.NewGuid()))
                    {
                        var target = locator_list.Nth(target_index);
                        await target.ScrollIntoViewIfNeededAsync();
                        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                        var target_text = await target.InnerTextAsync();
                        if (!string.IsNullOrWhiteSpace(target_text))
                            LogWriteLine(target_text);
                        var result = await ClickAndDetectNavigationAsync(ctx, target, token);
                        if (result.Navigated)
                        {
                            if (ctx.Page.Url.Contains("aden.baidu.com") || ctx.Page.Url.Contains("ada.baidu.com"))
                            {
                                await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                                await info_handler();
                            }
                            break;
                        }
                    }
                }
                else
                {
                    List<ILocator?> elements = new List<ILocator?>(); ;
                    var element_count = 0;
                    foreach (var frame in ctx.Page!.Frames)
                    {
                        try
                        {
                            var loc = frame.GetByText(new Regex(@"查看更多"));
                            var count = await loc.CountAsync();
                            if (count > 0)
                            {
                                elements.Add(loc);
                                element_count++;
                            }
                        }
                        catch
                        {
                            // 某些 frame 可能临时不可用，跳过
                        }
                    }

                    if (elements.Count() > 0 && element_count > 0)
                    {
                        foreach (var target_index in Enumerable.Range(0, element_count).OrderBy(_ => Guid.NewGuid()))
                        {

                            var raw = elements[target_index]!;
                            ILocator target;
                            var clickableAncestor = raw.Locator("xpath=ancestor-or-self::*[self::a or self::button or @role='button' or @onclick][1]");

                            if (await clickableAncestor.CountAsync() > 0)
                                target = clickableAncestor.First;
                            else
                                target = raw.Locator("..");
                            var box = await target.BoundingBoxAsync();
                            var target_text = await target.InnerTextAsync();
                            if (!string.IsNullOrWhiteSpace(target_text))
                                LogWriteLine(target_text);
                            var click = await ClickAndDetectNavigationAsync(ctx, target, token);
                            if (click != null && click.Navigated)
                            {
                                if (ctx.Page.Url.Contains("aden.baidu.com") || ctx.Page.Url.Contains("ada.baidu.com"))
                                {
                                    await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                                    await info_handler();
                                }
                                break;
                            }
                        }
                    }
                    else
                    {
                        for (int i = 1; i < 4; i++)
                        {
                            var locator = ctx.Page
                                .Locator("body,iframe")
                                .Filter(new() { Visible = true })
                                .First;
                            if (await locator.CountAsync() > 0)
                            {
                                await locator.First.ScrollIntoViewIfNeededAsync();
                                var result = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                                if (result.Navigated)
                                {
                                    if (ctx.Page.Url.Contains("aden.baidu.com") || ctx.Page.Url.Contains("ada.baidu.com"))
                                    {
                                        await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                                        await info_handler();
                                    }
                                    break;
                                }
                            }

                        }
                    }
                }
            }
            return offerItems;
        }




        /// <summary>
        /// JD
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<bool> HandleJdActivePageAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var his1 = ctx.Page!.Locator("*:has-text('医院')");
            var his2 = ctx.Page.Locator("*:has-text('问诊')");
            bool medical = await his1.CountAsync() > 0 || await his2.CountAsync() > 0;
            ClickResult? result = null;
            token.ThrowIfCancellationRequested();


            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

            //await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
            //    ctx.Page!,
            //    ctx.CdpSession!,
            //    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
            //    cancellationToken: token);

            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
            if (medical)
            {
                //|图文.*起|电话.*起
                var locator_list = ctx.Page.Locator("text=/剩.*个名额/").Filter(new() { Visible = true });
                var locator_count = await locator_list.CountAsync();
                if (locator_count > 0)
                {
                    foreach (var target_index in Enumerable.Range(0, locator_count).OrderBy(o => Guid.NewGuid()))
                    {
                        var target = locator_list.Nth(target_index);
                        await target.ScrollIntoViewIfNeededAsync();
                        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                        var target_text = await target.InnerTextAsync();
                        if (!string.IsNullOrWhiteSpace(target_text))
                            LogWriteLine(target_text);
                        result = await ClickAndDetectNavigationAsync(ctx, target, token);
                        if (result.Navigated)
                        {
                            break;
                        }
                    }
                }
            }

            if ((result == null || !result.Navigated))
            {
                var count = await CenterClickableFinder.MarkCandidatesAsync(ctx.Page);
                if (count > 0)
                {
                    var locator_list = CenterClickableFinder.GetMarkedLocator(ctx.Page);
                    var locator_count = await locator_list.CountAsync();
                    if (locator_count > 0)
                    {
                        foreach (var target_index in Enumerable.Range(0, locator_count).OrderBy(o => Guid.NewGuid()))
                        {
                            var target = locator_list.Nth(target_index);
                            var target_text = await target.InnerTextAsync();
                            if (!string.IsNullOrWhiteSpace(target_text))
                                LogWriteLine(target_text);
                            result = await ClickAndDetectNavigationAsync(ctx, target, token);
                            if (result.Navigated)
                            {
                                break;
                            }
                        }
                    }
                }
                else
                {
                    var locator = ctx.Page
                    .Locator("body,iframe")
                    .Filter(new() { Visible = true })
                    .First;
                    if (await locator.CountAsync() > 0)
                    {
                        await locator.First.ScrollIntoViewIfNeededAsync();
                        result = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                    }
                }
            }
            //await TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(medical ? 1 : 0, medical ? 5 : 3), 1);
            //if (ctx.Page.Url.StartsWith("https://plogin.m.jd.com/"))
            //{
            //    return true;
            //}
            //// var result = await TryRandomViewportClickableClickAsync(ctx, token);

            //if (ctx.Page.Url.StartsWith("https://pro.m.jd.com/mall/active"))
            //{

            //}
            //if (ctx.Page.Url.StartsWith("https://laputa.healthjd.com/doctor_home"))
            //{
            //    await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
            //    return true;
            //}
            if (result != null && result.Navigated)
            {
                await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                return true;
            }

            return false;
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
                await TryHandleLouisvuittonAsync(ctx, token);
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
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                return FlowControl.EndTask;
            }
            if (ctx.Page!.Url.StartsWith("https://h5.m.taobao.com"))
            {
                if (await ctx.Page.GetByText("获取验证码").CountAsync() > 0)
                {
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
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

                    await ctx.HumanSession!
                    .For(ctx.Page!)
                    .BrowsePageAsync(1, 3, token);

                    token.ThrowIfCancellationRequested();

                    if ((int)(DateTime.Now - start).TotalMilliseconds >= ctx.Config.SleepMs)
                        break;

                    await Task.Delay(CommonHelper.RandomRange(1000, 2000), token);
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
        private async Task TryHandleLouisvuittonAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!ctx.Page!.Url.StartsWith("https://www.louisvuitton.cn"))
                return;

            try
            {
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                var cookieBtn = await BDAdHelper.WaitVisibleLocatorAsync(new[]
                {
                    ctx.Page.GetByText("同意全部第三方Cookie", new() { Exact = true }),
                    ctx.Page.GetByRole(AriaRole.Button, new() { Name = "同意全部第三方Cookie" }),
                    ctx.Page.Locator("button").Filter(new() { HasTextString = "同意全部第三方Cookie" }),
                }, token, timeoutMs: 10000);
                if (cookieBtn != null)
                {
                    await Task.Delay(CommonHelper.RandomRange(300, 600), token);
                    await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, cookieBtn);
                }


                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                //await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                //ctx.Page!,
                //ctx.CdpSession!,
                //duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                //cancellationToken: token);

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

                    //await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    //ctx.Page!,
                    //ctx.CdpSession!,
                    //duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                    //cancellationToken: token);


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
                    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                    await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, acceptBtn.First);
                }
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                //await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                //ctx.Page!,
                //ctx.CdpSession!,
                //duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                //cancellationToken: token);

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
                        await node.ScrollIntoViewIfNeededAsync();
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

                    //await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    //ctx.Page!,
                    //ctx.CdpSession!,
                    //duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                    //cancellationToken: token);


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
            //var ads = ctx.Page!.Locator("span.ec-tuiguang:text-is(\"广告\")");

            //var count = await ads.CountAsync();




            //await ctx.HumanSession!
            //.For(ctx.Page!)
            //.BrowsePageAsync(2, 6, token);

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
                    await ctx.CdpManager!.GetOrCreateSessionAsync(ctx.Page);
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
                    await ctx.CdpManager!.GetOrCreateSessionAsync(ctx.Page);
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
        public async Task<ClickResult> TryRandomViewportClickableClickAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                var elements = await GetCurrentViewportClickableElementsAsync(ctx.Page!, token);
                if (elements.Count == 0)
                    return ClickResult.NoNavigation();

                foreach (var target in elements.OrderBy(_ => Guid.NewGuid()))
                {
                    token.ThrowIfCancellationRequested();
                    var result = await ClickAndDetectNavigationAsync(ctx, target, token);
                    if (result.Navigated)
                        return result;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }

            return ClickResult.NoNavigation();
        }
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
                await link.ScrollIntoViewIfNeededAsync();
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                var result = await ClickAndDetectNavigationAsync(ctx, link.First, token);
                if (result.Navigated)
                    return result;
            }
            return ClickResult.NoNavigation();
        }
        public async Task<List<IElementHandle>> GetCurrentViewportClickableElementsAsync(IPage page, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var clickableHandles = await page.EvaluateHandleAsync(@"() => {
                const all = Array.from(document.querySelectorAll('*'));
                const visible = all.filter(el => {
                    const style = window.getComputedStyle(el);
                    const rect = el.getBoundingClientRect();
                    return style.visibility !== 'hidden' &&
                           style.display !== 'none' &&
                           rect.width > 0 &&
                           rect.height > 0 &&
                           rect.top >= 0 &&
                           rect.left >= 0 &&
                           rect.bottom <= window.innerHeight &&
                           rect.right <= window.innerWidth;
                });

                return visible.filter(el => {
                    const rect = el.getBoundingClientRect();
                    const x = rect.left + rect.width / 2;
                    const y = rect.top + rect.height / 2;
                    const topEl = document.elementFromPoint(x, y);
                    const hasClick = el.onclick || el.tagName === 'A' || el.tagName === 'BUTTON' || el.getAttribute('role') === 'button';
                    const notCovered = topEl && (el === topEl || el.contains(topEl));
                    return hasClick && notCovered;
                });
            }");

            var props = await clickableHandles.GetPropertiesAsync();
            var elements = new List<IElementHandle>();

            foreach (var p in props.Values)
            {
                token.ThrowIfCancellationRequested();

                var el = p.AsElement();
                if (el != null)
                    elements.Add(el);
            }

            return elements;
        }
        #endregion



    }
}
