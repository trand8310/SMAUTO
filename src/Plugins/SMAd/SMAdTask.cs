using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using PlaywrightHumanInput;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using QTP.Common.Win32;
using SMAd;
using SMAd.Emulation;
using SMAd.LandingPolicy;
using SMAd.Models;
using SMAd.SMAd;
using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.Intrinsics.Arm;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;




namespace QTP.Plugins
{
    public sealed class SMAdTask : QTPServiceBase
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
                ClassName = "QTP.Plugins.SMAdTask",
                Name = "SMAd",
                FileName = "SMAd.dll",
            };
        }
        public override string Title => "神马搜索";


        private readonly TaskStatsAggregator _aggregator;
        private readonly AdeHelper _adeHelper;
        private ChineseNameGenerator _nameGenerator;
        private readonly IPlaywrightProvider _playwrightProvider;


        public SMAdTask(
            IPlaywrightProvider playwrightProvider,
            TaskStatsAggregator aggregator, AdeHelper adeHelper, ChineseNameGenerator nameGenerator, AppSettings appSettings) : base(appSettings)
        {
            _playwrightProvider = playwrightProvider;
            _aggregator = aggregator;
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
                //var closeBtn = ctx.Page!.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new");
                var closeBtn = page.Locator(AliAppDownloadModalCloseSelector);
                if (await closeBtn.CountAsync() > 0)
                {
                    var target = closeBtn.First;
                    if (await target.IsVisibleAsync())
                    {
                        await CDPHelper.MouseClickAsync(page, cdpSession, target);
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
                var closeBtn = page.Locator($".successTipNew_close_new,.newSuccessTipNew_close_new,{AliAppDownloadModalCloseSelector}");
                if (await closeBtn.CountAsync() > 0)
                {
                    var target = closeBtn.First;
                    if (await target.IsVisibleAsync())
                    {
                        await CDPHelper.MouseClickAsync(page, cdpSession, target);
                    }
                }
            }
            catch (Exception)
            {

            }
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
                result.Add("--platform=iOS");
                result.Add("--screen-color-depth=32");
            }
            else if (os == 7)
            {
                result.Add("--platform=Windows");
            }
            else
            {
                result.Add("--platform=Android");
            }


            var full_version = taskArgs.SelectToken("dev.full_version").Value<string>();
            var full_version_values = full_version.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries);
            result.Add($"--platform-version={taskArgs.SelectToken("dev.osv").Value<string>()}");
            result.Add($"--full-version={full_version}");
            if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.brand")?.Value<string>()))
            {
                var brand = taskArgs.SelectToken("dev.brand")?.Value<string>();

                result.Add($"--brand={brand}");
                result.Add($"--brand-name={brand}");
                if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.brand_version")?.Value<string>()))
                    result.Add($"--brand-version={taskArgs.SelectToken("dev.brand_version")?.Value<string>()}");

                var hash_opt = fingerprint % 3;
                if (fingerprint == 0)
                {
                    result.Add($"--disable-brand-version-list");
                    result.Add($"--disable-full-version-list");
                }
                else if (fingerprint == 1)
                {
                    result.Add($"--disable-full-version-list");
                }



                if (os == 1 || os == 2)
                {
                    var make = taskArgs.SelectToken("dev.make")?.Value<string>().ToLower();
                    if (!string.IsNullOrWhiteSpace(make))
                    {
                        if (make.Contains("xiaomi"))
                        {
                            result.Add($"--def-fontname=MiSans");
                        }
                        else if (make.ToLower().Contains("vivo"))
                        {
                            result.Add($"--def-fontname=vivo Sans");
                        }
                        else if (make.ToLower().Contains("oppo"))
                        {
                            result.Add($"--def-fontname=OPPO Sans 4.0");
                        }
                        else if (make.ToLower().Contains("huawei"))
                        {
                            result.Add($"--def-fontname=HarmonyOS Sans");
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
                        result.Add($"--product-model={taskArgs.SelectToken("dev.model")?.Value<string>()}");
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

            var dev_hash = Math.Abs(taskArgs.SelectToken("dev").ToString().GetHashCode());

            #region webgl
            result.Add($"--webgl-vendor={vendor}");
            result.Add($"--webgl-renderer={gpu}");


            Random rand = new Random(Math.Abs($"{vendor}{gpu}".ToLower().GetHashCode()));

            var webgl_extensions = new string[] {
                "EXT_clip_control|WEBGL_stencil_texturing|WEBGL_provoking_vertex|WEBGL_polygon_mode|WEBGL_multi_draw|WEBGL_lose_context|WEBGL_debug_shaders|WEBGL_debug_renderer_info|WEBGL_compressed_texture_s3tc_srgb|WEBGL_compressed_texture_s3tc|WEBGL_clip_cull_distance|WEBGL_blend_func_extended|OVR_multiview2|OES_texture_float_linear|OES_shader_multisample_interpolation|OES_sample_variables|OES_draw_buffers_indexed|NV_shader_noperspective_interpolation|KHR_parallel_shader_compile|EXT_texture_norm16|EXT_texture_mirror_clamp_to_edge|EXT_texture_filter_anisotropic|EXT_texture_compression_rgtc|EXT_texture_compression_bptc|EXT_render_snorm|EXT_polygon_offset_clamp|EXT_float_blend|EXT_disjoint_timer_query_webgl2|EXT_depth_clamp|EXT_conservative_depth|EXT_color_buffer_half_float|EXT_color_buffer_float",
                "EXT_clip_control|WEBGL_stencil_texturing|WEBGL_polygon_mode|WEBGL_multi_draw|WEBGL_lose_context|WEBGL_debug_shaders|WEBGL_debug_renderer_info|WEBGL_compressed_texture_s3tc_srgb|WEBGL_compressed_texture_s3tc|WEBGL_compressed_texture_etc1|WEBGL_compressed_texture_etc|WEBGL_compressed_texture_astc|WEBGL_clip_cull_distance|OVR_multiview2|OES_texture_float_linear|OES_shader_multisample_interpolation|OES_sample_variables|OES_draw_buffers_indexed|NV_shader_noperspective_interpolation|EXT_texture_mirror_clamp_to_edge|EXT_texture_filter_anisotropic|EXT_texture_compression_rgtc|EXT_texture_compression_bptc|EXT_polygon_offset_clamp|EXT_float_blend|EXT_depth_clamp|EXT_conservative_depth|EXT_color_buffer_half_float|EXT_color_buffer_float",
                "EXT_color_buffer_float|WEBGL_multi_draw|WEBGL_lose_context|WEBGL_debug_shaders|WEBGL_debug_renderer_info|WEBGL_compressed_texture_s3tc_srgb|WEBGL_compressed_texture_s3tc|WEBGL_compressed_texture_etc1|WEBGL_compressed_texture_etc|WEBGL_compressed_texture_astc|OES_texture_float_linear|EXT_texture_norm16|EXT_texture_filter_anisotropic|EXT_texture_compression_rgtc|EXT_texture_compression_bptc|EXT_float_blend|EXT_color_buffer_half_float",
            };
            var webgl_extension_text = string.Join("|", webgl_extensions[rand.Next(0, 3)].Split(new string[] { "|" }, StringSplitOptions.RemoveEmptyEntries).OrderBy(o => Guid.NewGuid()));
            result.Add($"--webgl-extensions={webgl_extension_text}");
            var webgl_vertex_shaders = new string[] {
                "32,256,16,31,1024,14,128,124,128,4,4,1-8,1-1023",
                "32,512,16,31,2048,36,128,124,64,4,4,1-4095.9375,1-1024",
                "32,1024,16,31,4096,48,128,124,64,4,4,1-16.7456,1-1024",
                "32,2048,16,31,8192,36,128,124,64,4,4,1-32.1247,1-1024",
                "32,4096,16,31,16384,48,128,124,64,4,4,1-1024.6631,1-1024",
                "32,8192,16,31,1024,36,128,124,64,4,4,1-2048.4475,1-1024",
            };

            result.Add($"--webgl-vertex-shader={webgl_vertex_shaders[rand.Next(0, 6)]}");
            var webgl_fragment_shaders = new string[] {
                "256,16,1024,14,128,-8,7",
                "512,16,2048,36,124,-8,7",
                "1024,16,4096,36,124,-8,7",
                "2048,16,16384,36,124,-8,7",
                "4096,16,8192,36,124,-8,7",
                "8192,16,16384,36,124,-8,7",
            };

            result.Add($"--webgl-fragment-shader={webgl_fragment_shaders[rand.Next(0, 6)]}");
            var webgl_frame_buffers = new string[] {
                "8,8,4,,16384,16384-16384,8,8,8,8,24,0",
                "8,8,4,,16383,16383-16383,8,8,8,8,24,0",
                "8,8,4,,65535,65535-65535,8,8,8,8,24,0"
            };
            result.Add($"--webgl-frame-buffer={webgl_frame_buffers[rand.Next(0, 3)]}");


            var webgl_textures = new string[] {
                "4096,4096,96,16,2048,2048,16",
                "4096,4096,96,16,16383,4096,256",
                "4096,4096,96,16,8192,4096,128",
                "4096,4096,96,16,4096,4096,64",
                "4096,4096,96,16,16383,4096,128",
                "4096,4096,96,16,2048,4096,128",
                "4096,4096,96,16,2048,4096,256",
            };

            result.Add($"--webgl-textures={webgl_textures[rand.Next(0, 7)]}");
            var webgl_uniform_buffers = new string[] {
                "84,65536,32,84,230400,230400",
                "24,65536,256,24,212988,200704",
                "48,65536,256,24,212988,200704",
                "96,65536,256,24,212988,200704",
                "192,65536,256,24,212988,200704",
                "216,65536,16,216,606208,626028",
                "512,65536,16,216,606208,626028",
            };
            result.Add($"--webgl-uniform-buffer={webgl_uniform_buffers[rand.Next(0, 7)]}");
            #endregion

            result.Add($"--hardware-concurrency={(taskArgs.SelectToken("dev.cpu")?.Value<int>() ?? 8)}");

            var ram = taskArgs.SelectToken("dev.ram").Value<string>().Split(',', StringSplitOptions.RemoveEmptyEntries);
            int deviceMemory = Convert.ToInt32(ram[CommonHelper.RandomRange(0, ram.Length)].Trim());
            if (deviceMemory < 4) deviceMemory = 4;

            result.Add($"--device-memory={(deviceMemory > 8 ? 8 : deviceMemory)}");

            var js_memory_info = new string[] { "10000000|10000000|1136000000", "29400000|31200000|1130000000", "10000000|10000000|1136000000", "29400000|31200000|1130000000", "29400000|31200000|1130000000" };
            result.Add($"--js-memory-info={js_memory_info[(dev_hash % 4)]}");
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


            result.Add($"--storage-quota=0|{(storage - usage_storage)}");

            result.Add("--enable-rects-noise");
            result.Add("--enable-canvas-noise");
            result.Add("--enable-image-noise");
            result.Add("--enable-text-noise");
            result.Add("--enable-font-noise");
            result.Add("--enable-audio-noise");

            if (dev_hash % 2 == 0)
            {
                result.Add("--disable-pdf-viewer");
            }

            if (new bool[] { true, true, false, true, true, false, true, true, false, true }[CommonHelper.RandomRange(0, 10)])
            {
                result.Add("--disable-geolocation");
            }


            if (os == 1 || os == 2)
            {

                {
                    var touch_ix = CommonHelper.NextInt(0, 2);
                    var touch_iy = CommonHelper.NextInt(80, 100);
                    if (new bool[] { true, true, false, true, true, false, true, true, false, true }[CommonHelper.RandomRange(0, 10)])
                    {
                        string combined_x = $"{fingerprint}_touch_offset_x";
                        string combined_y = $"{fingerprint}_touch_offset_y";
                        uint hash_val_x = CommonHelper.GetStableHash(combined_x);
                        uint hash_val_y = CommonHelper.GetStableHash(combined_y);
                        double norm_x = (hash_val_x / 4294967295.0) - 0.5;
                        double norm_y = (hash_val_y / 4294967295.0) - 0.5;
                        double noise_factor_x_ = norm_x * 0.2;
                        double noise_factor_y_ = norm_y * 0.2;
                        var touch_dx = touch_ix * (1 + noise_factor_x_);
                        var touch_dy = touch_iy * (1 + noise_factor_y_);
                        result.Add($"--touch-emulator-point={touch_dx},{touch_dy}");
                    }
                    else
                    {
                        result.Add($"--touch-emulator-point={touch_ix},{touch_iy}");
                    }
                }


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
                        //Channel = "chrome",
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


            var hash = Math.Max(47, Math.Abs(config.TaskArgs.SelectToken("dev")!.GetHashCode() % 145)) + 1;
            return new BrowserNewContextOptions
            {
                UserAgent = config.UserAgent,
                ViewportSize = new ViewportSize
                {
                    Width = config.Sw,
                    Height = config.Sh - CommonHelper.RandomRange(47, hash)
                },
                ScreenSize = new ScreenSize
                {
                    Width = config.Sw,
                    Height = config.Sh
                },
                DeviceScaleFactor = config.DeviceScale,
                IsMobile = config.Os == 1 || config.Os == 2,
                HasTouch = config.MaxTouchPoints > 0,
                IgnoreHTTPSErrors = true,
                Locale = "zh-CN",
                TimezoneId = "Asia/Shanghai",
            };
        }

        private static bool IsHiddenMode(TaskConfig config)
        {
            return config.TaskArgs.SelectToken("isHiddenMode")?.Value<bool>() ?? false;
        }

        private static IReadOnlyList<string> NormalizeChromiumLaunchArgs(IEnumerable<string> args, string userDataDir)
        {
            var normalized = new List<string>
            {
                $"--user-data-dir={userDataDir}"
            };

            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg))
                    continue;

                var item = RemoveArgumentValueQuotes(arg.Trim());
                if (item.Equals("--headless", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.StartsWith("--user-data-dir", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (item.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
                    continue;

                normalized.Add(item);
            }

            return normalized;
        }

        private static string RemoveArgumentValueQuotes(string arg)
        {
            var equalsIndex = arg.IndexOf('=');
            if (equalsIndex < 0 || equalsIndex == arg.Length - 1)
                return arg;

            var key = arg[..(equalsIndex + 1)];
            var value = arg[(equalsIndex + 1)..];

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];

            return key + value;
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

                    new UMobLandingPageStrategy(this),
                    new AliLandingPageStrategy(this),
                    new AiSiteLandingPageStrategy(this),
                    new AiStudyLandingPageStrategy(this),
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
                ctx.Page = null;
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
            using var swipeStyleScope = HumanSwipeEmulator.BeginStyleScope(ctx.SwipeStyleProfile);
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
                    //entry.FirstPageUrl = "https://www.browserscan.net/zh";
                    entry.FirstPageUrl = "https://adtomall.cn/content/pixelscan/r1/";
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
                    var delay_ms = CommonHelper.RandomRange(5000, 8000);
                    await Task.Delay(delay_ms, token);
                    await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    duration: TimeSpan.FromMilliseconds(Math.Abs(ctx.Config.PageLoadedDelayMs - delay_ms)),
                    cancellationToken: token);
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

                await HumanSwipeOperator.RandomUpUntilStopAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    minTimes: 0,
                    maxTimes: 5,
                    cancellationToken: token);

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
                    await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(3000, 8000)),
                    cancellationToken: token);

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
                "--mouse-as-touch",
                "--touch-events=enabled",
                $"--max-touch-points={config.MaxTouchPoints}",
                $"--user-agent={config.UserAgent}",
                $"--window-size={config.Sw},{config.Sh}",
                $"--window-position=0,0",
                //"--virtual-clipboard",
                //$"--device-pixel-ratio={config.DeviceScale}",
                //$"--screen-size=\"{config.Sw},{config.Sh}\"",
               // $"--screen-avail-size=\"{config.Sw},{config.Sh}\"",
            };



            if (config.Os == 1 || config.Os == 2)
            {

            }

            //proxyServer = string.Empty;
            //proxyServer = string.Empty;
            //var isProxyMode = config.TaskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            //if (isProxyMode)
            //{
            //    proxyServer = config.TaskArgs.SelectToken("proxy_server")!.Value<string>();
            //    var protocol = config.TaskArgs.SelectToken("protocol")?.Value<string>();
            //    if (!string.IsNullOrWhiteSpace(protocol) && protocol.Equals("socks5"))
            //    {
            //        args.Add($"--proxy-server=\"socks5://{proxyServer}\"");
            //        //--proxy-server="socks5://127.0.0.1:1080" ^
            //        //--disable-features=DnsOverHttps ^
            //        //--host-resolver-rules="MAP * ~NOTFOUND , EXCLUDE 127.0.0.1"
            //    }
            //    else
            //    {
            //        args.Add($"--proxy-server=\"{proxyServer}\"");
            //    }

            //}

            //if (config.TaskArgs.SelectToken("incognito")?.Value<bool>() ?? false)
            //{
            //    args.Add("--incognito");
            //    args.Add("--enable-incognito-themes");
            //}
            //else
            //{
            //    args.Add($"--disk-cache-dir=\"{config.CacheDir}\"");
            //}

            if (_appSettings.BlockImage || _appSettings.BlockMedia)
            {
                args.Add("--autoplay-policy=user-gesture-required");
            }

            if (_appSettings.BlockImage)
            {
                args.Add("--blink-settings=imagesEnabled=false");
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

            ctx.Page = await ctx.Context.NewPageAsync();
            //await InitPageAsync(ctx, ctx.Page, token);
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
            //var touchManager = new TouchEmulationManager(ctx,page, ctx.CdpManager!, ctx.Config.MaxTouchPoints);
            //await touchManager.StartAsync();
            if (_appSettings.BlockImage && _appSettings.BlockMedia)
            {
                await page.RouteAsync("**/*", async route =>
                {
                    var request = route.Request;
                    var url = request.Url;
                    var type = request.ResourceType;
                    if (type == "media" || SMAdHelper.IsBlockedMediaUrl(url))
                    {
                        await route.AbortAsync();
                        return;
                    }

                    await route.ContinueAsync();
                });
            }
            //await page.SetViewportSizeAsync(ctx.Config.Sw, ctx.Config.Sh);
            var cdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(page);
            //await cdpSession.SendAsync("Page.enable");
            //cdpSession.Event("Page.downloadWillBegin").OnEvent += (_, _) =>
            //{
            //    Interlocked.Increment(ref ctx.TriggerDownloadSign);
            //};

            if (ctx.Config.Os == 1 || ctx.Config.Os == 2)
            {
                //await CDPHelper.InitCDPSession(cdpSession, ctx.Config.MaxTouchPoints);
            }
            else
            {

            }

            //await CDPHelper.SetDeviceMetricsOverride(cdpSession, ctx.Config.Sw, ctx.Config.Sh, ctx.Config.DeviceScale, (ctx.Config.Os == 1 || ctx.Config.Os == 2 ? true : false));
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

            page.Download += async (_, download) =>
            {
                Interlocked.Increment(ref ctx.TriggerDownloadSign);
                await Task.Delay(CommonHelper.RandomRange(800, 2000), ctx.Config.LinkedCts.Token);
                try { await download.CancelAsync(); } catch { }
            };

            if (ctx.Page == page)
                ctx.CdpSession = cdpSession;

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
            ctx.CdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(ctx.Page);
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
                    if (!title.StartsWith("网页搜索") && !title.StartsWith("搜索"))
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

                    var input = ctx.Page!.Locator("textarea#kw");
                    if (await input.CountAsync() == 0)
                    {
                        LogWriteLine($"{this.Title}:输入框不存在");
                        return false;
                    }

                    await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, input);
                    await Task.Delay(CommonHelper.RandomRange(800, 1200), ct);

                    await input.PressSequentiallyAsync(word!, new LocatorPressSequentiallyOptions
                    {
                        Delay = CommonHelper.RandomRange(20, 100)
                    });

                    await Task.Delay(CommonHelper.RandomRange(1500, 2000), ct);

                    var btn = ctx.Page.Locator("div.submit");
                    if (await btn.CountAsync() == 0)
                    {
                        LogWriteLine($"{this.Title}:搜索按钮不存在");
                        return false;
                    }

                    ctx.CurrentPageUrl = ctx.Page.Url;
                    await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, btn.First);

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
                    await Task.Delay(CommonHelper.RandomRange(5000, 8000), ct);
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
                var adDotUrls = ctx.Page.Locator("div[ad_dot_url^='http'],div.ad-wolong-container:has(a[data-url^='http'])");
                ctx.PageAdsCount = await adDotUrls.CountAsync();

                if (ctx.PageAdsCount <= 0)
                {
                    LogWriteLine("没有广告标记,重试");
                    return false;
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

            var candidates = await BuildSponsoredCandidatesAsync(ctx, sponsoreds, sponsoredCount, token);

            foreach (var sponsored in candidates)
            {
                token.ThrowIfCancellationRequested();
                await HumanSwipeOperator.MoveToElementAsync(
                  ctx.Page!,
                  ctx.CdpSession!,
                  sponsored,
                  maxSwipes: 10,
                  cancellationToken: token);

                await sponsored.ScrollIntoViewIfNeededAsync();

                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

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
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
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
                if (dataUrl.Contains("1688.com")) score = 50;
                else if (dataUrl.Contains("jd.com")) score = 60;
                else if (dataUrl.Contains("qq.com")) score = 70;
                else if (dataUrl.Contains("baidu.com")) score = 80;
                else if (dataUrl.Contains("taobao.com")) score = 100;
                else if (dataUrl.Contains("pinduoduo.com")) score = 800;


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

            _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_p4psearch");
            var metrics = _aggregator.GetLocalMetrics(ctx.Config.TaskId, "dsp_p4psearch", "dsp_p4psearch_click");

            if (metrics["dsp_p4psearch"] > 0)
                LogWriteLine($"1688询价比率:{(metrics["dsp_p4psearch_click"] / (double)metrics["dsp_p4psearch"] * 100):N2}%");

            bool canClick = _appSettings.p4psearchRate == 100
                || metrics["dsp_p4psearch_click"] == 0
                || ((metrics["dsp_p4psearch_click"] / (double)metrics["dsp_p4psearch"]) * 100 < _appSettings.p4psearchRate);

            if (!canClick)
                return;

            try
            {
                await HumanSwipeOperator.RandomUpUntilAsync(
                  ctx.Page!,
                  ctx.CdpSession!,
                  stopVerifier: async _ =>
                  {
                      var panel = ctx.Page!.Locator("div[class*='ab-recommend-words']");
                      if (await panel.CountAsync() > 0)
                      {
                          await panel.First.ScrollIntoViewIfNeededAsync();
                          return true;
                      }
                      return false;
                  },
                  minTimes: 3,
                  maxTimes: 10,
                  cancellationToken: token);

                token.ThrowIfCancellationRequested();

                await HumanSwipeOperator.MicroUpAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    cancellationToken: token);

                await Task.Delay(CommonHelper.RandomRange(100, 200), token);

                var container = ctx.Page!.Locator("div[class*='ab-recommend-words']").First;
                if (await container.CountAsync() == 0)
                {
                    return;
                }
                var links = container.Locator("a.word");
                int count = await links.CountAsync();
                if (count == 0)
                {
                    return;
                }
                var containerBox = await container.BoundingBoxAsync();
                var recommends = new List<ILocator>();
                if (containerBox != null)
                {
                    try
                    {
                        double containerTop = containerBox.Y;
                        double containerBottom = containerBox.Y + containerBox.Height;
                        double containerLeft = containerBox.X;
                        double containerRight = containerBox.X + containerBox.Width;

                        for (int i = 0; i < count; i++)
                        {
                            var link = links.Nth(i);
                            var box = await link.BoundingBoxAsync();
                            if (box == null)
                                continue;

                            double linkTop = box.Y;
                            double linkBottom = box.Y + box.Height;
                            double linkLeft = box.X;
                            double linkRight = box.X + box.Width;

                            // 判断是否和容器可视区域有交集
                            bool verticallyVisible = linkBottom > containerTop && linkTop < containerBottom;
                            bool horizontallyVisible = linkRight > containerLeft && linkLeft < containerRight;

                            if (verticallyVisible && horizontallyVisible)
                            {
                                recommends.Add(link);
                            }
                        }
                    }
                    catch (Exception)
                    {

                    }

                }
                if (recommends.Count() == 0)
                    return;
                _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_p4psearch_click");
                foreach (var target_index in Enumerable.Range(0, recommends.Count()).OrderBy(o => Guid.NewGuid()))
                {
                    var recommend = recommends[target_index];
                    var click = await ClickAndDetectNavigationAsync(ctx, recommend, token);
                    if (click.Navigated)
                    {
                        await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                        break;
                    }

                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }

        public async Task<ILocator?> ResolveOfferItemsAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var url = ctx.Page!.Url;
            ILocator? offerItems = null;

            if (url.Contains("m.p4psearch.1688.com"))
            {
                if (_appSettings.Rfq1688 && _appSettings.Rfq1688Rate > 0)
                    _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_rfq1688");

                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                    cancellationToken: token);

                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("//div[starts-with(@class,'offer-item')]");
            }
            else if (url.Contains("m.1688.com"))
            {
                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                    cancellationToken: token);


                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);


                offerItems = ctx.Page.Locator("//div[starts-with(@class,'offer-item')]");
            }
            else if (url.Contains("b2b.baidu.com"))
            {

                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                    cancellationToken: token);

                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                offerItems = ctx.Page.Locator(".img-content,.list-title,.content-without-title");
                if (await offerItems.CountAsync() == 0)
                    offerItems = ctx.Page.Locator("a.product-item-link");
            }
            else if (url.Contains("aden.baidu.com") || url.Contains("ada.baidu.com"))
            {
                //https://ada.baidu.com/site
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
                    await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                        ctx.Page!,
                        ctx.CdpSession!,
                        duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                        cancellationToken: token);

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
                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                    cancellationToken: token);

                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("//a[starts-with(@class,'link')]");
            }
            else if (url.StartsWith("https://pro.m.jd.com/mall/active"))
            {
                //https://pro.m.jd.com/mall/active/6PRJiy2LHsUc6oezS9u5rjfYqmj/index.htm
                await HandleJdActivePageAsync(ctx, token);

                if (!ctx.Page.Url.StartsWith("https://plogin.m.jd.com/"))
                {
                    await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                    offerItems = ctx.Page.Locator(".masonryCard,.commodity-list .commodity-desc,.list-con .product,a.goods,.feed-product-container");
                    //if (await offerItems.CountAsync() == 0)
                    //    offerItems = ctx.Page.Locator(".feed-product-container");
                    //if (await offerItems.CountAsync() == 0)
                    //    offerItems = ctx.Page.Locator(".feed-product-container,a.goods,.list-con .product");
                    //if (await offerItems.CountAsync() == 0)
                    //    offerItems = ctx.Page.Locator("img");
                }
            }
            else if (url.Contains("m.jd.com"))
            {
                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                    cancellationToken: token);

                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                offerItems = ctx.Page.Locator(".commodity-list .commodity-desc,.list-con .product,a.goods,.feed-product-container");
                if (await offerItems.CountAsync() == 0)
                    offerItems = ctx.Page.Locator(".feed-product-container");
                if (await offerItems.CountAsync() == 0)
                    offerItems = ctx.Page.Locator(".feed-product-container,a.goods,.list-con .product");
                if (await offerItems.CountAsync() == 0)
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
                                var result = await ClickAndDetectNavigationAsync(ctx, target, token);
                                if (result.Navigated)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }
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

            await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                ctx.Page!,
                ctx.CdpSession!,
                duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                cancellationToken: token);

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
                await TryHandleRfq1688Async(ctx, token);
                await TryHandleQianhuFormAsync(ctx, token);
                await TryHandleLouisvuittonAsync(ctx, token);
            }
            this.QTPExecuteSuccess(ctx.Config.TaskId);
            LogWriteLine($"{this.Title}:ExecuteWorker:Success");

            if (ctx.Config.TotalPV > 1)
            {

                //if ((ctx.JumpClick && !ctx.PageTriggerClick))
                //{
                //    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                //    return FlowControl.NextPv;
                //}

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


                    await HumanSwipeOperator.ChaoticBrowseOnceAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    cancellationToken: token);

                    token.ThrowIfCancellationRequested();

                    if (await IsPageEnd(ctx.Page))
                        await HumanSwipeOperator.RandomDownOnceAsync(ctx.Page!, ctx.CdpSession!, cancellationToken: token);
                    else if (await IsPageTop(ctx.Page))
                        await HumanSwipeOperator.RandomUpOnceAsync(ctx.Page!, ctx.CdpSession!, cancellationToken: token);

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
                await Task.Delay(CommonHelper.RandomRange(1500, 2000), token);
                await ClearPageCloseBtn(ctx.Page!, ctx.CdpSession!);
                await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                await ClearSuccessTipNewCloseNew(ctx.Page!, ctx.CdpSession!);
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                ctx.Page!,
                ctx.CdpSession!,
                duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                cancellationToken: token);

                if (!_appSettings.NoTrigger1688Shop ||  CommonHelper.Chance(0.25))
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
                        await locator_detail.ScrollIntoViewIfNeededAsync();
                        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                        var clickRes2 = await ClickAndDetectNavigationAsync(ctx, locator_detail.First, token);
                        if (clickRes2.Navigated)
                        {
                            await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                            await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                            await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                            await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                            await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                            ctx.Page!,
                            ctx.CdpSession!,
                            duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                            cancellationToken: token);

                            locator_detail = ctx.Page
                                .Locator("body,iframe")
                                .Filter(new() { Visible = true })
                                .First;

                            if (await locator_detail.CountAsync() > 0)
                            {
                                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                                await locator_detail.First.ScrollIntoViewIfNeededAsync();
                                var clickRes3 = await ClickAndDetectNavigationAsync(ctx, locator_detail.First, token);
                                if (clickRes3.Navigated)
                                {
                                    await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                                    await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                                    await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                                    await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                                    await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                                    ctx.Page!,
                                    ctx.CdpSession!,
                                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                                    cancellationToken: token);

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
                                await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                                await Task.Delay(CommonHelper.RandomRange(200, 300), token);
                                await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                                ctx.Page!,
                                ctx.CdpSession!,
                                duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                                cancellationToken: token);
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


                await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                var el = ctx.Page!.Locator("#od_xst_phone_input_val_new,#new_od_xst_phone_input_val_new");
                if (await el.CountAsync() == 0)
                {
                    var queryBtn = ctx.Page.Locator(".queryBtnTitleTop");
                    if (await queryBtn.CountAsync() == 0)
                        queryBtn = ctx.Page.GetByText("立即询价");

                    if (await queryBtn.CountAsync() > 0)
                    {
                        await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, queryBtn.First, timeout: 1500);
                        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
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
                await Task.Delay(CommonHelper.RandomRange(1500, 2000), token);

                var answerContents = ctx.Page.Locator("div.new_answer_content span,div.answer_content span");
                if (await answerContents.CountAsync() > 0)
                {
                    int count = await answerContents.CountAsync();
                    var answer = answerContents.Nth(CommonHelper.RandomRange(0, count));
                    await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, answer.First, timeout: 1000);
                    await Task.Delay(CommonHelper.RandomRange(1500, 2000), token);
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
                        await Task.Delay(CommonHelper.RandomRange(1500, 2000), token);
                    }
                }

                el = ctx.Page.Locator(".new_successTipNew_wangwang_new,.successTipNew_call_new");
                if (await el.CountAsync() > 0)
                {
                    try
                    {
                        await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, el.First, timeout: 2000);
                        await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                        var sms = ctx.Page.GetByText("获取验证码");
                        if (await sms.CountAsync() > 0)
                        {
                            var close1 = ctx.Page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new");
                            if (await close1.CountAsync() > 0)
                                await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, close1.First, timeout: 1500);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch { }

                    var close2 = ctx.Page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new,.newCloseIcon_content");
                    if (await close2.CountAsync() > 0)
                        await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, close2.First, timeout: 1500);
                }
                await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                ctx.Page!,
                ctx.CdpSession!,
                duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                cancellationToken: token);

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
                        await locator.ScrollIntoViewIfNeededAsync();
                        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                        var clickRes2 = await ClickAndDetectNavigationAsync(ctx, locator.First, token);
                        if (clickRes2.Navigated)
                        {
                            await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                            await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                            await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                            ctx.Page!,
                            ctx.CdpSession!,
                            duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                            cancellationToken: token);


                            locator = ctx.Page
                                .Locator("body,iframe")
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
                                    await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                                    ctx.Page!,
                                    ctx.CdpSession!,
                                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                                    cancellationToken: token);
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
                                await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                                await ClearSuccessTipNewCloseNew(ctx.Page, ctx.CdpSession!);
                                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

                                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                                ctx.Page!,
                                ctx.CdpSession!,
                                duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                                cancellationToken: token);


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
                    await inputName.FillAsync("");
                    await inputName.PressSequentiallyAsync(surname);
                }

                await Task.Delay(CommonHelper.RandomRange(300, 500), token);

                var inputPhone = ctx.Page.Locator("input[placeholder='请输入手机号']").First;
                if (await inputPhone.CountAsync() > 0)
                {
                    await inputPhone.FillAsync("");
                    await inputPhone.PressSequentiallyAsync(phone);
                }

                var radio = ctx.Page.Locator(".phone-agrement-container .phone-agrement-radio");
                if (await radio.CountAsync() > 0)
                    await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, radio.First);

                var btnSubmit = ctx.Page.Locator("div:has-text('免费领票')").First;
                if (await btnSubmit.CountAsync() > 0)
                {
                    await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, btnSubmit);
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
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                var cookieBtn = await SMAdHelper.WaitVisibleLocatorAsync(new[]
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

                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                ctx.Page!,
                ctx.CdpSession!,
                duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                cancellationToken: token);

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

                    await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                    cancellationToken: token);


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

                await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                ctx.Page!,
                ctx.CdpSession!,
                duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                cancellationToken: token);

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

                    await HumanSwipeOperator.TimedChaoticBrowseUntilAsync(
                    ctx.Page!,
                    ctx.CdpSession!,
                    duration: TimeSpan.FromMilliseconds(CommonHelper.RandomRangeDouble(5000, 8000)),
                    cancellationToken: token);


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

            await Task.Delay(CommonHelper.RandomRange(5000, 8000));

            var traces = await HumanSwipeOperator.ChaoticBrowseTimesAsync(
            ctx.Page!,
            ctx.CdpSession!,
            minTimes: 5,
            maxTimes: 12);

            HumanSwipeTraceRenderer.DrawSwipeGif(
                traces,
                filePath: "./swipe_trace.gif",
                width: ctx.Page!.ViewportSize!.Width,
                height: ctx.Page!.ViewportSize!.Height,
                defaultFrameDelayMs: 35,
                useTraceDelay: true,
                highlightCurrent: true,
                tailFade: true);



            //await HumanTouchSwipe.SwipeAsync(
            //    ctx.Page!,
            //    ctx.CdpSession!,
            //    HumanSwipeDirection.Up,
            //    HumanSwipeMode.Fling,
            //    speedFactor: 1.3);

            //await HumanSwipeOperator.RandomUpUntilStopAsync(
            //    ctx.Page!,
            //    ctx.CdpSession!,
            //    minTimes: 3,
            //    maxTimes: 8);


            //await HumanSwipeEmulator.FlingUpAsync(ctx.Page!, ctx.CdpSession!, FlingStrength.VeryStrong);


            //await HumanSwipeEmulator.SwipeAsync(
            // ctx.Page!,
            // ctx.CdpSession!,
            // new HumanSwipeOptions
            // {
            //     Direction = HumanSwipeDirection.Up,
            //     Mode = HumanSwipeMode.Fling,
            //     SpeedFactor = 1.3,
            //     VerifyScrollChanged = true,
            //     CheckScrollableBeforeSwipe = true
            // });

            //var step = await HandleLandingPageAsync(ctx, token);
            //ctx.JumpClick = true;
            //ctx.PageTriggerClick = true;
            //await ExecuteTaskSleepPhaseAsync(ctx, token);
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
