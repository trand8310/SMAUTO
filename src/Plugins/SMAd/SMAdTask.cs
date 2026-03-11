using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;


namespace QTP.Plugins
{
    public sealed class SMAdTask : QTPServiceBase
    {
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
        public SMAdTask(TaskStatsAggregator aggregator, ChromiumSessionManager manager, AdeHelper adeHelper, ChineseNameGenerator nameGenerator, AppSettings appSettings) : base(appSettings)
        {
            _aggregator = aggregator;
            _processManager = manager;
            _adeHelper = adeHelper;
            _nameGenerator = nameGenerator;
        }

        /// <summary>
        /// 随机生成一个满足百分比的数字
        /// </summary>
        /// <param name="probability"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static bool IsEventOccurring(double probability)
        {
            if (probability < 0 || probability > 1)
                throw new ArgumentOutOfRangeException(nameof(probability), "Probability must be between 0 and 1");
            double randomValue = Random.Shared.NextDouble();
            return randomValue < probability;
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
        /// 触摸滑动
        /// </summary>
        /// <param name="page"></param>
        /// <param name="client"></param>
        /// <param name="scrollCount"></param>
        /// <param name="direction">1:向上滑动,2:向下滑动</param>
        /// <param name="predexp"></param>
        /// <returns></returns>
        private async Task TouchPageScroll(IPage page, ICDPSession client, int scrollCount, int direction, Func<IPage, Task<bool>>? predexp = null, int time_delay = 0)
        {
            try
            {
                if (direction == -1)
                {
                    LogWriteLine($"TouchScrollDown:{scrollCount}次");
                }
                else
                {
                    LogWriteLine($"TouchScrollUp:{scrollCount}次");
                }

                for (int i = 0; i < scrollCount; i++)
                {
                    if (direction == -1)
                    {
                        await SwipeEmulator.SwipeMultipleAsync(
                                    page, client,
                                    1,
                                    direction: ScrollDirection.Down,
                                    steps: RandomUtil.NextInt(15, 30),
                                    delayMs: RandomUtil.NextInt(10, 18),
                                    jitter: (float)RandomUtil.NextDouble(1, 3));

                        if (time_delay == 0)
                            time_delay = CommonHelper.RandomRange(500, 2000);
                        await Task.Delay(time_delay);
                        if (predexp != null && await predexp(page))
                        {
                            break;
                        }
                    }
                    else
                    {
                        await SwipeEmulator.SwipeMultipleAsync(
                                    page, client,
                                    1,
                                    direction: ScrollDirection.Up,
                                    steps: RandomUtil.NextInt(15, 30),
                                    delayMs: RandomUtil.NextInt(10, 18),
                                    jitter: (float)RandomUtil.NextDouble(1, 3));
                        if (time_delay == 0)
                            time_delay = CommonHelper.RandomRange(500, 2000);
                        await Task.Delay(time_delay);

                        if (predexp != null && await predexp(page))
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {


            }


        }

        private async Task TouchPageScrollUp(IPage page, ICDPSession client)
        {
            try
            {
                await SwipeEmulator.SwipeMultipleMicroAsync(
                          page, client,
                          1,
                          direction: ScrollDirection.Up,
                          steps: RandomUtil.NextInt(10, 20),
                          delayMs: RandomUtil.NextInt(5, 10),
                          jitter: (float)RandomUtil.NextDouble(1, 3));
            }
            catch (Exception)
            {


            }


        }
        /// <summary>
        /// 处理页面元素
        /// </summary>
        /// <param name="page"></param>
        /// <param name="cdpSession"></param>
        private void ProcessingPageElementTask(IPage page, ICDPSession cdpSession)
        {
            _ = Task.Run(async () =>
            {
                int redo_try_count = 0;
                while (redo_try_count++ < 10)
                {
                    await Task.Delay(CommonHelper.RandomRange(800, 1200));
                    try
                    {
                        var closeBtn = page.Locator(".androidOpenModal .closeBtn, .iosOpenModal .closeIcon");
                        if (await closeBtn.CountAsync() > 0)
                        {
                            var target = closeBtn.First;
                            if (await target.IsVisibleAsync())
                            {
                                await CDPHelper.MouseClickAsync(page, cdpSession, target);
                                break;
                            }
                        }
                        //else if (await page.Locator(".enquiryFormContentnew").CountAsync() > 0 && await page.Locator(".successTipNew_close_new").CountAsync() > 0)
                        //{
                        //    var closeBtn = page.Locator(".successTipNew_close_new");
                        //    if (await closeBtn.CountAsync() > 0)
                        //    {
                        //        await CDPHelper.MouseClickAsync(page, cdpSession, closeBtn);
                        //        break;
                        //    }
                        //}

                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            });
        }


        private async Task ClearPageCloseBtn(IPage page, ICDPSession cdpSession)
        {
            try
            {
                var closeBtn = page.Locator(".androidOpenModal .closeBtn, .iosOpenModal .closeIcon");
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



        private static List<string> InitFPArgs(JObject taskArgs, int maxTouchPoints)
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
            else
            {
                result.Add("--platform=\"Android\"");
            }


            var full_version = taskArgs.SelectToken("dev.full_version").Value<string>();
            var full_version_values = full_version.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries);

            result.Add($"--platform-version=\"{taskArgs.SelectToken("dev.osv").Value<string>()}\"");
            result.Add($"--full-version={full_version}");

            if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.brand")?.Value<string>()))
            {
                var brand = taskArgs.SelectToken("dev.brand")?.Value<string>();
                result.Add($"--brand=\"{brand}\"");
                result.Add($"--brand-name=\"{brand}\"");
                result.Add($"--brand-version=\"{taskArgs.SelectToken("dev.brand_version")?.Value<string>()}\"");

                if (new bool[] { false, false, true, false, false, true, false, false, true, false }[CommonHelper.RandomRange(0, 10)])
                {
                    result.Add($"--disable-full-version-list");
                }


                var make = taskArgs.SelectToken("dev.make")?.Value<string>().ToLower();
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

                //def-fontname

            }

            if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.model")?.Value<string>()))
            {
                if (os == 1)
                {
                    result.Add($"--product-model=\"{taskArgs.SelectToken("dev.model")?.Value<string>()}\"");
                }

            }

            result.Add($"--fingerprint={fingerprint}");
            var grease_cipher = Math.Abs(string.Join(".", full_version_values.Take(2)).GetHashCode()) % 65535;
            result.Add($"--ssl-grease-cipher={grease_cipher}");
            result.Add($"--netinfo-type={new string[] { "wifi", "cellular" }[CommonHelper.RandomRange(0, 2)]}");
            result.Add($"--netinfo-effective=4g");
            result.Add($"--netinfo-rtt={CommonHelper.RandomRange(0, 400)}");

            result.Add($"--force-webrtc-ip-handling-policy");
            var isProxyMode = taskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            if (isProxyMode)
            {
                var realIp = taskArgs.SelectToken("realIp")?.Value<string>() ?? taskArgs.SelectToken("ipInfo.query")?.Value<string>();
                if (!string.IsNullOrWhiteSpace(realIp))
                {
                    result.Add($"--webrtc-ip={realIp}");
                    result.Add($"--webrtc-ip-handling-policy=default_public_interface_only");
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
            Random rand = new Random(Math.Abs($"{vendor}{gpu}".ToLower().GetHashCode()));

            var webgl_extensions = new string[] {
                "EXT_clip_control|WEBGL_stencil_texturing|WEBGL_provoking_vertex|WEBGL_polygon_mode|WEBGL_multi_draw|WEBGL_lose_context|WEBGL_debug_shaders|WEBGL_debug_renderer_info|WEBGL_compressed_texture_s3tc_srgb|WEBGL_compressed_texture_s3tc|WEBGL_clip_cull_distance|WEBGL_blend_func_extended|OVR_multiview2|OES_texture_float_linear|OES_shader_multisample_interpolation|OES_sample_variables|OES_draw_buffers_indexed|NV_shader_noperspective_interpolation|KHR_parallel_shader_compile|EXT_texture_norm16|EXT_texture_mirror_clamp_to_edge|EXT_texture_filter_anisotropic|EXT_texture_compression_rgtc|EXT_texture_compression_bptc|EXT_render_snorm|EXT_polygon_offset_clamp|EXT_float_blend|EXT_disjoint_timer_query_webgl2|EXT_depth_clamp|EXT_conservative_depth|EXT_color_buffer_half_float|EXT_color_buffer_float",
                "EXT_clip_control|WEBGL_stencil_texturing|WEBGL_polygon_mode|WEBGL_multi_draw|WEBGL_lose_context|WEBGL_debug_shaders|WEBGL_debug_renderer_info|WEBGL_compressed_texture_s3tc_srgb|WEBGL_compressed_texture_s3tc|WEBGL_compressed_texture_etc1|WEBGL_compressed_texture_etc|WEBGL_compressed_texture_astc|WEBGL_clip_cull_distance|OVR_multiview2|OES_texture_float_linear|OES_shader_multisample_interpolation|OES_sample_variables|OES_draw_buffers_indexed|NV_shader_noperspective_interpolation|EXT_texture_mirror_clamp_to_edge|EXT_texture_filter_anisotropic|EXT_texture_compression_rgtc|EXT_texture_compression_bptc|EXT_polygon_offset_clamp|EXT_float_blend|EXT_depth_clamp|EXT_conservative_depth|EXT_color_buffer_half_float|EXT_color_buffer_float",
                "EXT_color_buffer_float|WEBGL_multi_draw|WEBGL_lose_context|WEBGL_debug_shaders|WEBGL_debug_renderer_info|WEBGL_compressed_texture_s3tc_srgb|WEBGL_compressed_texture_s3tc|WEBGL_compressed_texture_etc1|WEBGL_compressed_texture_etc|WEBGL_compressed_texture_astc|OES_texture_float_linear|EXT_texture_norm16|EXT_texture_filter_anisotropic|EXT_texture_compression_rgtc|EXT_texture_compression_bptc|EXT_float_blend|EXT_color_buffer_half_float",
            };
            var webgl_extension_text = string.Join("|", webgl_extensions[rand.Next(0, 3)].Split(new string[] { "|" }, StringSplitOptions.RemoveEmptyEntries).OrderBy(o => Guid.NewGuid()));
            result.Add($"--webgl-extensions=\"{webgl_extension_text}\"");
            var webgl_vertex_shaders = new string[] {
                "32,256,16,31,1024,14,128,124,128,4,4,1-8,1-1023",
                "32,512,16,31,2048,36,128,124,64,4,4,1-4095.9375,1-1024",
                "32,1024,16,31,4096,48,128,124,64,4,4,1-16.7456,1-1024",
                "32,2048,16,31,8192,36,128,124,64,4,4,1-32.1247,1-1024",
                "32,4096,16,31,16384,48,128,124,64,4,4,1-1024.6631,1-1024",
                "32,8192,16,31,1024,36,128,124,64,4,4,1-2048.4475,1-1024",
            };

            result.Add($"--webgl-vertex-shader=\"{webgl_vertex_shaders[rand.Next(0, 6)]}\"");
            var webgl_fragment_shaders = new string[] {
                "256,16,1024,14,128,-8,7",
                "512,16,2048,36,124,-8,7",
                "1024,16,4096,36,124,-8,7",
                "2048,16,16384,36,124,-8,7",
                "4096,16,8192,36,124,-8,7",
                "8192,16,16384,36,124,-8,7",
            };

            result.Add($"--webgl-fragment-shader=\"{webgl_fragment_shaders[rand.Next(0, 6)]}\"");
            var webgl_frame_buffers = new string[] {
                "8,8,4,,16384,16384-16384,8,8,8,8,24,0",
                "8,8,4,,16383,16383-16383,8,8,8,8,24,0",
                "8,8,4,,65535,65535-65535,8,8,8,8,24,0"
            };
            result.Add($"--webgl-frame-buffer=\"{webgl_frame_buffers[rand.Next(0, 3)]}\"");


            var webgl_textures = new string[] {
                "4096,4096,96,16,2048,2048,16",
                "4096,4096,96,16,16383,4096,256",
                "4096,4096,96,16,8192,4096,128",
                "4096,4096,96,16,4096,4096,64",
                "4096,4096,96,16,16383,4096,128",
                "4096,4096,96,16,2048,4096,128",
                "4096,4096,96,16,2048,4096,256",
            };

            result.Add($"--webgl-textures=\"{webgl_textures[rand.Next(0, 7)]}\"");
            var webgl_uniform_buffers = new string[] {
                "84,65536,32,84,230400,230400",
                "24,65536,256,24,212988,200704",
                "48,65536,256,24,212988,200704",
                "96,65536,256,24,212988,200704",
                "192,65536,256,24,212988,200704",
                "216,65536,16,216,606208,626028",
                "512,65536,16,216,606208,626028",
            };
            result.Add($"--webgl-uniform-buffer=\"{webgl_uniform_buffers[rand.Next(0, 7)]}\"");
            #endregion

            result.Add($"--hardware-concurrency={(taskArgs.SelectToken("dev.cpu")?.Value<int>() ?? 8)}");

            var ram = taskArgs.SelectToken("dev.ram").Value<string>().Split(',', StringSplitOptions.RemoveEmptyEntries);
            int deviceMemory = Convert.ToInt32(ram[CommonHelper.RandomRange(0, ram.Length)].Trim());
            result.Add($"--device-memory={(deviceMemory > 8 ? 8 : deviceMemory)}");

            var js_memory_info = new string[] { "10000000|10000000|1136000000", "29400000|31200000|1130000000", "10000000|10000000|1136000000", "29400000|31200000|1130000000", "29400000|31200000|1130000000" };
            result.Add($"--js-memory-info=\"{js_memory_info[(dev_hash % 4)]}\"");
            result.Add($"--max-touch-points={maxTouchPoints}");
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
            result.Add("--enable-font-noise");
            result.Add("--enable-audio-noise");

            if (new bool[] { true, true, false, true, true, false, true, true, false, true }[CommonHelper.RandomRange(0, 10)])
            {
                result.Add("--disable-pdf-viewer");
            }

            if (new bool[] { true, true, false, true, true, false, true, true, false, true }[CommonHelper.RandomRange(0, 10)])
            {
                result.Add("--disable-geolocation");
            }


            {
                var touch_ix = RandomUtil.NextInt(0, 2);
                var touch_iy = RandomUtil.NextInt(80, 100);
                if (new bool[] { true, true, false, true, true, false, true, true, false, true }[CommonHelper.RandomRange(0, 10)])
                {
                    string combined_x = $"{fingerprint}_touch_offset_x";
                    string combined_y = $"{fingerprint}_touch_offset_y";
                    uint hash_val_x = GetStableHash(combined_x);
                    uint hash_val_y = GetStableHash(combined_y);
                    double norm_x = (hash_val_x / 4294967295.0) - 0.5;
                    double norm_y = (hash_val_y / 4294967295.0) - 0.5;
                    double noise_factor_x_ = norm_x * 0.2;
                    double noise_factor_y_ = norm_y * 0.2;
                    var touch_dx = touch_ix * (1 + noise_factor_x_);
                    var touch_dy = touch_iy * (1 + noise_factor_y_);
                    result.Add($"--touch-emulator-point=\"{touch_dx},{touch_dy}\"");
                }
                else
                {
                    result.Add($"--touch-emulator-point=\"{touch_ix},{touch_iy}\"");
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
            return result;
        }


        //private IBrowserContext context;
        //private IBrowser Browser;

        //private IntPtr BrowserMainWindow = IntPtr.Zero;
        //private PROCESS_INFORMATION pi;
        //private Process BrowserProcess;


        public override async Task<bool> CloseBrowserProcess(string uniqueId)
        {
            await _processManager.CloseAsync(uniqueId);
            return true;
        }


        public async Task ScrollWithTimeoutAsync(IPage page, CDPSessionManager cdpManager, int durationMs)
        {
            var cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
            //1:创建 CancellationTokenSource,durationMs 到期自动取消
            using var cts = new CancellationTokenSource(durationMs);
            var token = cts.Token;
            try
            {
                //2:循环滑动
                while (!token.IsCancellationRequested)
                {
                    //执行滑动操作
                    await TouchPageScroll(page, cdpSession, 1, new int[] { 0, 0, 0, -1, 0, 0, 0, -1, 0, -1 }[CommonHelper.RandomRange(0, 10)], time_delay: CommonHelper.RandomRange(10, 20));
                    //等待短时间再滑下一次
                    await Task.Delay(CommonHelper.RandomRange(1000, 2000), token);

                }
            }
            catch (TaskCanceledException)
            {
                // 超时到，停止滑动
            }
            catch (Exception)
            {

            }
        }

        public static void StartAutoCloseQuarkModal(
            IPage page,
            CDPSessionManager cdpManager)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!page.IsClosed)
                    {
                        try
                        {
                            var modals = page.Locator("div.quark-download-modal");
                            var count = await modals.CountAsync();
                            if (count > 0)
                            {
                                for (int i = 0; i < count; i++)
                                {
                                    var modal = modals.Nth(i);
                                    var closeBtn = modal.Locator("img.close");
                                    if (await closeBtn.CountAsync() == 0)
                                        continue;
                                    var btn = closeBtn.First;
                                    if (!await btn.IsVisibleAsync())
                                        continue;
                                    var cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                    await CDPHelper.MouseClickAsync(page, cdpSession, btn);
                                    break;
                                }
                            }
                        }
                        catch
                        {
                        }
                        await Task.Delay(2000);
                    }
                }
                catch (Exception)
                {


                }

            });
        }




        private static async Task<IBrowser?> ConnectOverCDPWithRetryAsync(
        IPlaywright playwright,
        string endpoint,
        int retry = 20,
        int delayMs = 300,
        CancellationToken token = default)
        {
            var chromium = playwright.Chromium;
            for (int i = 0; i < retry; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {

                    var browser = await chromium.ConnectOverCDPAsync(endpoint);
                    return browser;
                }
                catch (Exception)
                {
                    if (i == retry - 1)
                        return null;
                    await Task.Delay(delayMs, token);
                }
            }
            return null;
        }


        public override async Task<(bool, bool, int)> ExecuteWorkerAsync(string uniqueId, JObject taskArgs, CancellationTokenSource linkedCts)
        {

            #region 任务参数设置
            var st = System.DateTime.Now;
            int taskid = taskArgs.SelectToken("task.id").Value<int>();
            var task_url = taskArgs.SelectToken("task.url").Value<string>();
            var sleep = CommonHelper.RandomRange(8, 15);
            if (taskArgs.SelectToken("task.sleep") != null)
            {
                var task_sleep = taskArgs.SelectToken("task.sleep").Value<string>();
                if (task_sleep.Contains("-"))
                {
                    var values = task_sleep.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(s => Convert.ToInt32(s)).ToArray();
                    if (values.Length == 2)
                        sleep = CommonHelper.RandomRange(values[0], values[1]);
                }
                else if (int.TryParse(task_sleep, out var _))
                {
                    sleep = Convert.ToInt32(task_sleep);
                }
            }
            var isLocalAdWord = taskArgs.SelectToken("isLocalAdWord")?.Value<bool>() ?? false;
            int pageLoadingTimeout = taskArgs.SelectToken("pageLoadingTimeout")?.Value<int>() * 1000 ?? 30000;
            int pageloadedDelay = CommonHelper.RandomRange(8000, 15000);
            if (taskArgs.ContainsKey("pageloadedDelay") && !string.IsNullOrWhiteSpace(taskArgs.SelectToken("pageloadedDelay").Value<string>()))
            {
                var tmpStr = taskArgs["pageloadedDelay"].ToString();
                if (tmpStr.Contains("-"))
                {
                    var values = tmpStr.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(s => Convert.ToInt32(s)).ToArray();
                    if (values.Length == 2)
                        pageloadedDelay = CommonHelper.RandomRange(values[0] * 1000, values[1] * 1000);
                }
                else
                {
                    pageloadedDelay = Convert.ToInt32(tmpStr) * 1000;
                }
            }
            int hompageTrigger = taskArgs.SelectToken("hompageTrigger")?.Value<int>() ?? 0;
            //非1688优先
            bool priorityNon1688 = taskArgs.SelectToken("priorityNon1688")?.Value<bool>() ?? false;
            this.QTPExecuteStart(taskid);
            LogWriteLine($"{this.Title}:ExecuteWorker:Start");
            #endregion


            var useragent = taskArgs.SelectToken("dev.ua").Value<string>();
            var os = taskArgs.SelectToken("os").Value<int>();
            var dev_sw = taskArgs.SelectToken("dev.sw")?.Value<int>();
            var deviceScale = taskArgs.SelectToken("dev.pixelRatio")?.Value<float>() ?? 0;
            if (deviceScale == 0)
                deviceScale = (float)(CommonHelper.RandomRange(250, 270) / (1e2 * 1.0123456));
            var sw = (int)(taskArgs.SelectToken("dev.sw").Value<int>() / deviceScale);
            var sh = (int)(taskArgs.SelectToken("dev.sh").Value<int>() / deviceScale);
            if (os == 2)
            {
                sw = 428;
                sh = 926;
                deviceScale = (float)dev_sw / (float)sw;
            }

            var wordname = taskArgs.SelectToken("wordname")?.Value<string>() ?? "default";
            var noTrigger1688 = taskArgs.SelectToken("noTrigger1688")?.Value<bool>() ?? false;
            var cleaningWords = taskArgs.SelectToken("cleaningWords")?.Value<bool>() ?? false;
            var notTriggerDownload = taskArgs.SelectToken("notTriggerDownload")?.Value<bool>() ?? false;


            var pvsTriggerOne = taskArgs.SelectToken("pvsTriggerOne")?.Value<bool>() ?? true;
            var currentUV = taskArgs.SelectToken("currentUV")?.Value<int>() ?? 0;
            var kernelVersion = taskArgs.SelectToken("kernelVersion")?.Value<string>() ?? "132";
            int maxTouchPoints = CommonHelper.RandomRange(4, 6);
            int page_ads_count = 0;
            bool page_trigger_click = false;
            var processIndex = taskArgs.SelectToken("processIndex")?.Value<int>() ?? 1;
            string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", "Chrome", kernelVersion, "User_Cache", $"{taskArgs.SelectToken("cacheName").Value<string>()}");
            string userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", "Chrome", kernelVersion, "User_Data", $"{processIndex}_{Guid.NewGuid().ToString("n")}");

            //--enable-logging --v=1  --log-file="%~dp0\126.log"
            //"--enable-logging",
            //"--v=1",
            //"--log-file=E:\\workhome\\SVNRoot\\WUQIXIU_PROJECT\\SM-MUV\\126\\Build\\File\\126.log",
            //$"--disk-cache-dir={cachePath}",
            //"--no-startup-window",

            var args = new List<string>()
            {
                "--disable-extensions",
                "--disable-default-apps",
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-sync",
                "--disable-component-update",
                "--disable-background-networking",
                "--metrics-recording-only",
                "--disable-client-side-phishing-detection",
                "--disable-popup-blocking",
                "--disable-infobars",
                "--password-store=basic",
                "--use-mock-keychain",
                "--no-service-autorun",
                "--force-color-profile=srgb",
                "--disable-features=LensOverlay,Translate",
                "--disable-logging",
                "--virtual-clipboard",
                "--touch-events=enabled",
                "--use-fake-ui-for-media-stream",
                "--use-fake-device-for-media-stream",
                "--show-avatar-button=never",
                "--disable-http2-grease-settings",
                //"--disk-cache-size=262144000",
                //"--media-cache-size=262144000",
                "--hide-bad-flags",
                "--hide-crashed-bubble",
                $"--user-agent=\"{useragent}\"",
                $"--window-size=\"{sw + 20},{sh + 48}\"",
                $"--window-position=0,0",
            };


            args.Add($"--device-pixel-ratio={deviceScale}");
            args.Add($"--screen-size=\"{sw},{sh}\"");
            args.Add($"--screen-avail-size=\"{sw},{sh}\"");
            string proxyServer = string.Empty;
            var isProxyMode = taskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            if (isProxyMode)
            {
                proxyServer = taskArgs.SelectToken("proxy_server").Value<string>();
                args.Add($"--proxy-server=\"{proxyServer}\"");
            }
            if (taskArgs.SelectToken("isHiddenMode")?.Value<bool>() ?? false)
            {
                args.Add($"--headless");
            }
            if (taskArgs.SelectToken("incognito")?.Value<bool>() ?? false)
            {
                args.Add($"--incognito");
                args.Add($"--enable-incognito-themes");
            }
            else
            {
                args.Add($"--disk-cache-dir=\"{cacheDir}\"");
            }

            //disable-geolocation

            args.AddRange(InitFPArgs(taskArgs, maxTouchPoints));

            LogWriteLine($"args={string.Join(" ", args)}");
            using var playwright = await Playwright.CreateAsync();
            var chromium = playwright.Chromium;
            var chromePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "File", "chrome-win", kernelVersion, "chrome.exe");
            var session = await _processManager.StartChromium(uniqueId, chromePath, userDataDir, TimeSpan.FromSeconds(180), $"about:blank  {string.Join(" ", args)}", proxyServer);
            var endpoint = $"http://localhost:{session.DebugPort}";
            await using var browser = await ConnectOverCDPWithRetryAsync(playwright, endpoint);
            if (browser == null)
            {
                return (false, false, 0);
            }
            var context = browser.Contexts[0];
            if (taskArgs.SelectToken("ipInfo.lon") != null && taskArgs.SelectToken("ipInfo.lat") != null)
            {
                await context.SetGeolocationAsync(new Geolocation
                {
                    Latitude = taskArgs.SelectToken("ipInfo.lat").Value<float>(),
                    Longitude = taskArgs.SelectToken("ipInfo.lon").Value<float>()
                });
            }
            //await context.GrantPermissionsAsync(new[] { "geolocation" });
            browser.Disconnected += (sender, e) =>
            {
                try
                {
                    LogWriteLine("浏览器已关闭或断开连接！");
                    if (!linkedCts.IsCancellationRequested)
                        linkedCts.Cancel();
                }
                catch (Exception)
                {


                }
            };


            try
            {
                var cdpManager = new CDPSessionManager(context);
                int trigger_download_sign = 0;
                context.Page += async (_, newPage) =>
                {
                    await newPage.SetViewportSizeAsync(sw, sh);
                    var cdpSession = await cdpManager.GetOrCreateSessionAsync(newPage);
                    await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                    await CDPHelper.SetDeviceMetricsOverride(cdpSession, sw, sh, (float)deviceScale, true);
                    await CDPHelper.SetBrowserPermission(cdpSession);
                    newPage.Dialog += async (_, dialog) =>
                    {
                        await dialog.DismissAsync(); // 关闭对话框
                    };
                    newPage.Crash += (_, e) =>
                    {
                        try
                        {
                            LogWriteLine("Crash！");
                            if (!linkedCts.IsCancellationRequested)
                                linkedCts.Cancel();
                        }
                        catch (Exception)
                        {
                        }
                        //await CloseBrowserProcess(uniqueId);

                    };
                    newPage.PageError += (_, e) =>
                    {

                    };
                    newPage.RequestFailed += (_, e) =>
                    {
                        try
                        {
                            if (!string.IsNullOrWhiteSpace(e.Failure) &&
                                (e.Failure.Contains("ERR_INVALID_AUTH_CREDENTIALS") ||
                                (e.Failure.Contains("ERR_TUNNEL_CONNECTION_FAILED") && newPage.Url.Equals(e.Url))))
                            {
                                LogWriteLine($"page.RequestFailed:{e.Failure},{e.Url},{newPage.Url}");
                                try
                                {
                                    LogWriteLine("Crash！");
                                    if (!linkedCts.IsCancellationRequested)
                                        linkedCts.Cancel();
                                }
                                catch (Exception)
                                {
                                }
                                //await CloseBrowserProcess(uniqueId);
                            }
                        }
                        catch (Exception)
                        {

                        }

                    };
                    newPage.Download += async (sender, download) =>
                    {
                        Interlocked.Increment(ref trigger_download_sign);
                        try
                        {
                            await download.CancelAsync(); // 取消下载
                        }
                        catch (Exception)
                        {


                        }

                    };
                };

                IPage page = context.Pages[0];
                page.Download += async (sender, download) =>
                {
                    Interlocked.Increment(ref trigger_download_sign);
                    try
                    {
                        await download.CancelAsync(); // 取消下载
                    }
                    catch (Exception)
                    {


                    }
                };
                await page.SetViewportSizeAsync(sw, sh);
                var cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                await cdpSession.SendAsync("Page.enable");
                cdpSession.Event("Page.downloadWillBegin").OnEvent += (s, e) =>
                {
                    Interlocked.Increment(ref trigger_download_sign);
                };
                await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                await CDPHelper.SetDeviceMetricsOverride(cdpSession, sw, sh, (float)deviceScale, true);
                await CDPHelper.SetBrowserPermission(cdpSession);
                //await CDPHelper.SetUserAgentOverride(cdpSession, useragent, os == 2 ? "iOS" : "Android");
                var isTest = taskArgs.SelectToken("isTest")?.Value<bool>() ?? false;
                int totalPV = taskArgs.SelectToken("totalPV")?.Value<int>() ?? 1;
                int pvIndex = 0;
            redo_pv:
                pvIndex++;
                if (pvIndex > totalPV)
                {
                    goto task_end;
                }
                LogWriteLine($"{this.Title}:pv：{totalPV}/{pvIndex}");
                if (context.Pages.Count > 1)
                {
                    do
                    {
                        await context.Pages[context.Pages.Count - 1].CloseAsync();

                    } while (context.Pages.Count > 1);
                    page = context.Pages[0];
                }

                bool is_hompageTrigger = false;
                string first_page_url = task_url;

                var q = string.Empty;
                if (_aggregator.CanHomepageTrigger(taskid))
                {
                    first_page_url = first_page_url.Replace("&q=[QUERY]", "");
                    is_hompageTrigger = true;
                }
                else if (first_page_url.Contains("[QUERY]"))
                {
                    int get_q_count = 0;
                redo_get_q:
                    if (get_q_count++ > 5)
                    {
                        LogWriteLine("无法获取词条,请检查服务器");
                        await Task.Delay(TimeSpan.FromSeconds(30));
                        goto task_end;
                    }
                    q = await _adeHelper.GetWordAsync();
                    if (string.IsNullOrWhiteSpace(q))
                    {
                        await Task.Delay(CommonHelper.RandomRange(100, 200));
                        goto redo_get_q;
                    }
                    LogWriteLine($"{this.Title}:搜索词条{q}");
                    first_page_url = first_page_url.Replace("[QUERY]", q);
                }

                try
                {
                    if (isTest)
                    {
                        //first_page_url = "https://b2b.baidu.com/m/aitf/s?q=%E5%AE%9E%E6%9C%A8%E9%A2%97%E7%B2%92%E6%9D%BF%E6%98%AF%E4%BB%80%E4%B9%88%E6%84%8F%E6%80%9D&fid=509070424&styl=b&sid=90311_811002_70000&a_keywordid=71167800984&creativeId=50000002314759426";
                        //first_page_url = "https://b2b.baidu.com/m/aitf/s?q=%E6%8A%95%E8%B5%84%E8%B5%9A%E9%92%B1%E8%BD%AF%E4%BB%B6app";
                        //first_page_url = "https://qianhu.wejianzhan.com/site/wjzu0ez1/35f561fd-ed35-42e6-aeef-b7d29088a9ee?bd_vid=nHcsrj6LPH6knWDkPWnznWm3rNtkP1cvg17xnH0sg1wxrHbknHmvnHn4nW6&fid=nHcsrj6LPH6knWDkPWnznWm3rNtkP1cvg1D&ch=4&bd_bxst=EiaK6NMKUBg2k9aSK6DD0rfZbfQ3est000000KBv3QQWzogDEoj-8_vOkTAW_rgDkQWv8_n0000000000006nHmsnbc1nWc3wbFanj0vnWR4PHfdf1fYfRf1n10dfYcyH0YrzGLKg6c0000C6TtH7s0005fUXoMA0000DfK60fW2VTiS_ryzkaz1Gnh1zogDEoj-__iqvW5RsWcLQHc4zPyOJ_ieCl1N8TOl__h48_J9n10-cf0000jOOOOOOOOOOstPPXD";
                        //first_page_url = "https://googlechrome.github.io/samples/async-clipboard/";
                        //first_page_url = "https://www.adtomall.com/page.html";
                        //first_page_url = "https://m.p4psearch.1688.com/page.html?scene=8&q=%E7%8E%AF%E4%BF%9D%E7%9A%AE%E8%8D%89%E5%A5%B3&imgurl=img%2Fibank%2FO1CN014k1XW01LMa13eBYoI_!!2207873421285-0-cib.jpg&cosite=smjj&keywordid=74320369958&trackid=88585857717827007619670&format=shandian&bd_vid=11084568593119754510&creative=50000002313693958&clickid=11084568593119754510&uctrackid=czoxMTY5NjMwNTUyNjMzNDM1MDE2MTtjOjUwMDAwMDAyMzEzNjkzOTU4O2Q6ZG1wXy01NjI5MzQyMTI1NDM3MjIyOTQ4O3A6d2w%3D&flowfrom=shenma&hpageId=wx-list-v3&p_rs=true&spm=a3c0f.semlist-v3.0.0&p4pid=b3d16fc102125&exp=wxWangwangShowFloatExp%3AC%3Bqztf%3AA%3Bcpx%3AA%3Bpz%3AB%3Bai%3AB&ptid=0177000000088ef7901f1a69c0162a1a";
                        //first_page_url = "https://m.1688.com/zw/hamlet.html?scene=8&q=%E7%AF%AE%E7%90%83%E8%B6%B3%E7%90%83&imgurl=img/ibank/O1CN014k1XW01LMa13eBYoI_!!2207873421285-0-cib.jpg&cosite=smjj&keywordid=74320369958&trackid={}&format=shandian&bd_vid=11084568593119754510&outerId=618324461983&creative=50000002313693958&trackid=88585857717827007619670&clickid=11084568593119754510&uctrackid=czoxMTY5NjMwNTUyNjMzNDM1MDE2MTtjOjUwMDAwMDAyMzEzNjkzOTU4O2Q6ZG1wXy01NjI5MzQyMTI1NDM3MjIyOTQ4O3A6d2w=&flowfrom=shenma";
                        //first_page_url = "https://m.p4psearch.1688.com/page.html?hpageId=krump-100235&offerid=779147384802&memberId=b2b-2211812440340365e4&ptid=&pid=408015_0000&exp=wxShowleadsCardExp%3AB%3BwxShowWangWangExp%3AB%3BwxShowOrderQuestion%3AC%3BwxShowFromExp%3AC&_force_exp_buckets_=202508112%2C202508052%2C202507233%2C202512183&spm=a3c0f.sem-video.9cdbd3c6.i6.2fdb2996WLpj3b&cosite=smjj&tracelog=p4p&_p_isad=1&clickid=0ea438363d5546f087f3ae29d42ec450&sessionid=6a644981e17c868919b975deb7b03398&a=1245&e=fGUK2bJ4tBJDTWjC-T6pUPW441UN6DKYnP1iqM1VJk6BdC-0V4gN7kxa4CFKJAPoGf0SzxQMxxG.iTwp.QlobnDE-7Sr1kTMWbQ9VlF1iDrP5nn-V-DpcPgtodYxVAvbKI1xlrbUzNsaOE4VIesEI44cxPyqKhUPUbzezgsDmC5IDIBmhMvsK9S.-t0WMwpUqBBUuYKk-lbXKsZu7chhuiF31WKtXN2d-ERBN4SA2bNxGloQIK2u7Z3c7Yv8zwTh&sk=sem&style=1";
                        //first_page_url = "https://wm.m.sm.cn/s?from=10000&q=%E6%B5%81%E6%84%9F%E5%90%83%E4%BB%80%E4%B9%88%E8%8D%AF";
                        //first_page_url = "https://pro.m.jd.com/mall/active/32R3r4vG6x3RmoeJCevxY7BXjecP/index.html?babelChannel=ttt4&hy_entry=Outside_UC";

                    }


                    await page.GotoAsync(first_page_url, new PageGotoOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = pageLoadingTimeout });
                }
                catch (TimeoutException ex)
                {
                    LogWriteLine($"加载超时:{ex.Message}");
                    string title = await page.TitleAsync();
                    if (!title.StartsWith("网页搜索") && !title.StartsWith("搜索"))
                    {
                        goto redo_pv;
                    }
                }
                var delayMs = CommonHelper.RandomRange(3000, 5000);
                await Task.Delay(delayMs);
                string current_page_url = page.Url;
                int pagesCount = context.Pages.Count;
                var jumpClick = false;
                this.QTPExecuteDSP(taskid);


                if (isTest)
                {
                    //await Task.Delay(TimeSpan.FromSeconds(5));
                    //https://pro.m.jd.com/mall/active/32R3r4vG6x3RmoeJCevxY7BXjecP/index.html?babelChannel=ttt4&hy_entry=Outside_UC
                    //            var button = page.Locator("button:has-text('下载')");



                    var locator = page.Locator("*:has-text('医院')");
                    var locator2 = page.Locator("*:has-text('问诊')");
                    if (await locator.CountAsync() > 0 || await locator2.CountAsync() > 0)
                    {

                        try
                        {
                            int jd_redo_count = 0;
                        jd_redo:
                            if (jd_redo_count++ < 3)
                            {
                                await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(1, 5), 1);
                                await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                var clickableHandles = await page.EvaluateHandleAsync(@"() => {
                                    const all = Array.from(document.querySelectorAll('*'));
                                    const visible = all.filter(el => {
                                        const style = window.getComputedStyle(el);
                                        const rect = el.getBoundingClientRect();
                                        return style.visibility !== 'hidden' &&
                                               style.display !== 'none' &&
                                               rect.width > 0 && rect.height > 0 &&
                                               rect.top >= 0 && rect.left >= 0 &&
                                               rect.bottom <= window.innerHeight &&
                                               rect.right <= window.innerWidth;
                                    });

                                    // 判断是否可点击且不被覆盖
                                    return visible.filter(el => {
                                        const rect = el.getBoundingClientRect();
                                        const x = rect.left + rect.width / 2;
                                        const y = rect.top + rect.height / 2;

                                        const topEl = document.elementFromPoint(x, y);
                                        // 检查元素绑定点击事件
                                        const hasClick = el.onclick || (typeof getEventListeners !== 'undefined' && getEventListeners(el).click?.length > 0);

                                        // topEl 可能是子节点，判断 el 是否包含 topEl
                                        const notCovered = topEl && (el === topEl || el.contains(topEl));

                                        return hasClick && notCovered;
                                    });
                                }");

                                // 转换成 Locator
                                var props = await clickableHandles.GetPropertiesAsync();
                                var elements = new List<IElementHandle>();
                                foreach (var prop in props.Values)
                                {
                                    var handle = prop.AsElement();
                                    if (handle != null)
                                        elements.Add(handle);
                                }

                                // 随机点击一个
                                if (elements.Count > 0)
                                {
                                    List<int> elements_range = Enumerable.Range(0, elements.Count).OrderBy(o => Guid.NewGuid()).ToList();

                                    foreach (var target_index in elements_range)
                                    {
                                        var target = elements[target_index];

                                        pagesCount = context.Pages.Count;
                                        current_page_url = page.Url;
                                        await CDPHelper.MouseClickAsync(page, cdpSession, target);
                                        await Task.Delay(CommonHelper.RandomRange(50, 100));
                                        try
                                        {
                                            await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });

                                        }
                                        catch (TimeoutException)
                                        {


                                        }

                                        if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                        {
                                            if (context.Pages.Count > pagesCount)
                                            {
                                                page = context.Pages[context.Pages.Count - 1];
                                                cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                            }

                                            if (page.Url.StartsWith("https://pro.m.jd.com/mall/active"))
                                            {
                                                goto jd_redo;
                                            }

                                            if (page.Url.StartsWith("https://laputa.healthjd.com/doctor_home"))
                                                break;

                                            break;
                                        }
                                    }


                                }
                            }
                        }
                        catch (Exception)
                        {

                        }

                    }




                    await Task.Delay(TimeSpan.FromSeconds(150));
                    goto task_end;
                }



                trigger_download_sign = 0;
                if (page.Url.Contains("punish?x5secdata"))
                {
                    this.X5Secdata(taskid, 1, page.Url);
                    goto task_end;
                }
                if (is_hompageTrigger)
                {
                    //输入词条的模式
                    try
                    {
                        if (string.IsNullOrWhiteSpace(q))
                        {
                            q = await _adeHelper.GetWordAsync();
                            LogWriteLine($"{this.Title}:搜索词条{q}");
                        }
                        var input = page.Locator("textarea#kw");
                        if (await input.CountAsync() == 0)
                        {
                            LogWriteLine($"{this.Title}:输入框不存在");
                            goto redo_pv;
                        }
                        await CDPHelper.MouseClickAsync(page, cdpSession, input);
                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                        await input.PressSequentiallyAsync(q, new LocatorPressSequentiallyOptions() { Delay = CommonHelper.RandomRange(20, 100) });
                        await Task.Delay(CommonHelper.RandomRange(1500, 2000));
                        var search_button = page.Locator("div.submit");
                        if (await search_button.CountAsync() == 0)
                        {
                            LogWriteLine($"{this.Title}:搜索按钮不存在");
                            goto redo_pv;
                        }
                        await CDPHelper.MouseClickAsync(page, cdpSession, search_button.First);
                        await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
                        LogWriteLine($"{this.Title}:搜索完成");
                        await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                    }
                    catch (TimeoutException)
                    {

                    }
                    catch (Exception ex)
                    {
                        LogWriteLine($"{this.Title}:搜索操作失败,{ex.Message}");
                        goto redo_pv;
                    }
                }
                else
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:曝光进入页面停留{((pageloadedDelay - delayMs) / 1e3):N2}秒");
                    await ScrollWithTimeoutAsync(page, cdpManager, Math.Abs(pageloadedDelay - delayMs));
                }

                var ad_dot_urls = page.Locator("div[ad_dot_url^='http'],div.ad-wolong-container:has(a[data-url^='http'])");
                page_ads_count = await ad_dot_urls.CountAsync();
                if (page_ads_count > 0 && !string.IsNullOrWhiteSpace(q))
                {
                    int ad_1688_count = 0;
                    int ad_other_count = 0;
                    foreach (var ad_url_index in Enumerable.Range(0, page_ads_count))
                    {
                        var ad_item = ad_dot_urls.Nth(ad_url_index);
                        var ad_alis = ad_item.Locator("a[data-url]");
                        var ad_alis_count = await ad_alis.CountAsync();
                        if (ad_alis_count > 0)
                        {
                            var data_url = await ad_alis.First.GetAttributeAsync("data-url");
                            if (!string.IsNullOrWhiteSpace(data_url))
                            {
                                if (data_url.Contains(".1688."))
                                {
                                    ad_1688_count++;
                                }
                                else
                                {
                                    ad_other_count++;
                                }
                            }
                        }
                    }
                    if (ad_other_count > 0 && ad_1688_count == 0)
                    {
                        QTPUploadAdWord("no1688", q);
                    }
                    if (ad_other_count > 0)
                    {
                        QTPUploadAdWord("other", q);
                    }
                    if (ad_1688_count > 0)
                    {
                        QTPUploadAdWord("1688", q);
                    }
                    if (noTrigger1688 && ad_other_count == 0)
                    {
                        LogWriteLine("只有1688广告标记,重试");
                        goto redo_pv;
                    }
                }
                else
                {
                    LogWriteLine("没有广告标记,重试");
                    goto redo_pv;
                }


                #region jumpClick

                current_page_url = page.Url;
                int click_rate = taskArgs.SelectToken("task.click_rate").Value<int>();
                if (click_rate > 0)
                {
                    var ctr = await _aggregator.GetClickRatioAsync(taskid, click_rate);
                    LogWriteLine($"点击比率:{(ctr * 100):N2}%");
                    jumpClick = await _aggregator.CanClickthroughAsync(taskid, click_rate);
                }
                page_trigger_click = false;
                if (jumpClick)
                {

                    var sponsoreds = page.Locator("div[ad_dot_url^='http'],div.ad-wolong-container:has(a[data-url^='http'])");
                    var sponsored_count = await sponsoreds.CountAsync();
                    if (sponsored_count > 0)
                    {

                        var sortedList = new SortedList<int, ILocator>();
                        int sort_index = 0;
                        List<int> sponsored_range = Enumerable.Range(0, sponsored_count).OrderBy(o => Guid.NewGuid()).ToList();
                        if (priorityNon1688)
                        {
                            sponsored_range = Enumerable.Range(0, sponsored_count).ToList();
                            foreach (var sponsored_index in sponsored_range)
                            {
                                var sponsored = sponsoreds.Nth(sponsored_index);
                                var alis = sponsored.Locator("a.c-title,a.ad-desc,a.img-item,a[data-url^='http']");//.Or(sponsored.Locator("a.ad-desc")).Or(sponsored.Locator("a.img-item"));
                                var alis_count = await alis.CountAsync();
                                if (alis_count > 0)
                                {
                                    var ad_text = await alis.First.InnerTextAsync();
                                    var ad_href = await alis.First.GetAttributeAsync("href");
                                    var data_url = await alis.First.GetAttributeAsync("data-url");
                                    if (!string.IsNullOrWhiteSpace(data_url))
                                    {
                                        if (data_url.Contains("1688.com"))
                                        {
                                            sortedList.Add(100 + sort_index++, sponsored);
                                        }
                                        else if (data_url.Contains("taobao.com"))
                                        {
                                            sortedList.Add(90 + sort_index++, sponsored);
                                        }
                                        else if (data_url.Contains("baidu.com"))
                                        {
                                            sortedList.Add(80 + sort_index++, sponsored);
                                        }
                                        else if (data_url.Contains("pinduoduo.com"))
                                        {
                                            sortedList.Add(800 + sort_index++, sponsored);
                                        }
                                        else if (data_url.Contains("qq.com"))
                                        {
                                            sortedList.Add(900 + sort_index++, sponsored);
                                        }
                                        else
                                        {
                                            sortedList.Add(sort_index++, sponsored);
                                        }
                                    }
                                }
                                else
                                {
                                    sortedList.Add(sort_index++, sponsored);
                                }
                            }
                        }

                        foreach (var sponsored_index in sponsored_range)
                        {
                            var sponsored = priorityNon1688 ? sortedList.Values[sponsored_index] : sponsoreds.Nth(sponsored_index);
                            await SwipeEmulator.SwipeToElementAsync(page, cdpSession, sponsored);
                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                            try
                            {
                                pagesCount = context.Pages.Count;
                                current_page_url = page.Url;

                                var alis = sponsored.Locator("a.c-title,a[data-url^='http']");
                                var alis_elements = await GetVisibleElementsAsync(alis);
                                if (alis_elements.Count == 0)
                                    continue;
                                var urls_dict = new Dictionary<ILocator, string>();

                                foreach (var el in alis_elements)
                                {
                                    var el_data_url = await el.GetAttributeAsync("data-url");
                                    if (!string.IsNullOrWhiteSpace(el_data_url))
                                    {
                                        urls_dict.Add(el, el_data_url);
                                    }
                                }
                                var exts = new string[] { ".apk", ".zip", ".exe", ".7z", ".rar" };

                                var filtered = urls_dict
                                .Where(kv => !exts.Any(ext => kv.Value.Contains(ext, StringComparison.OrdinalIgnoreCase)))
                                .OrderByDescending(kv => kv.Value.Length)
                                .ToList();

                                ILocator? sponsored_el = null;
                                if (filtered.Any())
                                {
                                    var filtered2 = filtered
                                      .Where(kv => kv.Value.Contains(".u-mob.", StringComparison.OrdinalIgnoreCase))
                                      .ToList();
                                    if (filtered2.Any())
                                    {
                                        sponsored_el = filtered2[Random.Shared.Next(0, filtered2.Count)].Key;
                                    }
                                    else
                                    {
                                        sponsored_el = filtered[Random.Shared.Next(0, filtered.Count)].Key;
                                    }

                                    //site.u-mob.cn

                                }
                                else
                                {
                                    var sorted_urls = urls_dict
                                                   .OrderByDescending(kv => kv.Value.Length)
                                                   .ToList();
                                    sponsored_el = sorted_urls.FirstOrDefault().Key;
                                }

                                var data_url = await sponsored_el.GetAttributeAsync("data-url");
                                if (string.IsNullOrWhiteSpace(data_url))
                                {
                                    continue;
                                }
                                var sponsored_text = await sponsored_el.InnerTextAsync();
                                var box = await sponsored_el.BoundingBoxAsync();
                                if (box != null)
                                {
                                    LogWriteLine($"触发广告位:{sponsored_text}:({box.X},{box.Y},{box.Width},{box.Height})");
                                }
                                else
                                {
                                    LogWriteLine($"触发广告位:{sponsored_text}");
                                }
                                await CDPHelper.MouseClickAsync(page, cdpSession, sponsored_el);
                                await Task.Delay(CommonHelper.RandomRange(50, 100));
                                await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
                                await Task.Delay(CommonHelper.RandomRange(2000, 3000));
                            }
                            catch (TimeoutException)
                            {

                            }
                            catch (Exception ex)
                            {
                                LogWriteLine(ex.Message);
                                continue;
                            }

                            if (trigger_download_sign > 0)
                            {
                                this.QTPExecuteClickthrough(taskid);
                                LogWriteLine($"{this.Title}:ExecuteWorker:Clickthrough");
                                page_trigger_click = true;
                                goto task_end;
                            }


                            if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                            {
                                if (context.Pages.Count > pagesCount)
                                {
                                    page = context.Pages[context.Pages.Count - 1];
                                    cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                    await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                }
                                this.QTPExecuteClickthrough(taskid);
                                LogWriteLine($"{this.Title}:ExecuteWorker:Clickthrough");
                                page_trigger_click = true;
                                try
                                {
                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000));

                                    if (page.Url.StartsWith("https://site.u-mob.cn/"))
                                    {
                                        //tag-panel
                                        await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                        var tagItems = page.Locator(".tag-panel .tag-item");
                                        if (await tagItems.CountAsync() > 0)
                                        {
                                            var count = await tagItems.CountAsync();
                                            // 随机决定点击多少个 (1 ~ count)
                                            int clickCount = CommonHelper.RandomRange(1, count);
                                            // 打乱索引顺序
                                            var indices = Enumerable.Range(0, count)
                                                                    .OrderBy(_ => Guid.NewGuid())
                                                                    .Take(clickCount)
                                                                    .ToList();
                                            Console.WriteLine($"本次随机点击 {clickCount} 个 tag-item");
                                            foreach (var i in indices)
                                            {
                                                var tag = tagItems.Nth(i);
                                                var text = await tag.TextContentAsync();
                                                Console.WriteLine($"点击第 {i + 1} 个 tag-item: {text}");
                                                await CDPHelper.MouseClickAsync(page, cdpSession, tag);
                                                await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            }
                                        }
                                        if (!notTriggerDownload)
                                        {
                                            var button = page.Locator("button:has-text('下载')");
                                            if (await button.CountAsync() > 0)
                                            {
                                                if (new int[] { 3, 5, 7 }.Contains(CommonHelper.RandomRange(1, 10)))
                                                {
                                                    await CDPHelper.MouseClickAsync(page, cdpSession, button.First);

                                                    await Task.Delay(CommonHelper.RandomRange(1500, 2500));
                                                    if (trigger_download_sign > 0)
                                                    {
                                                        page_trigger_click = true;
                                                        goto task_sleep;
                                                    }
                                                }

                                            }
                                        }
                                    }
                                    else if (page.Url.StartsWith("https://aisite.wejianzhan.com"))
                                    {
                                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                        var open_btn = page.Locator(".animate-container svg image");
                                        if (await open_btn.CountAsync() > 0)
                                        {
                                            int image_count = await open_btn.CountAsync();
                                            try
                                            {
                                                var pagesCount2 = context.Pages.Count;
                                                var current_page_url2 = page.Url;
                                                await CDPHelper.MouseClickAsync(page, cdpSession, open_btn.Nth(image_count - 1));
                                                await page.WaitForURLAsync(url => !url.Equals(current_page_url2), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
                                            }
                                            catch (TimeoutException)
                                            {

                                            }
                                            if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                            {
                                                if (context.Pages.Count > pagesCount)
                                                {
                                                    page = context.Pages[context.Pages.Count - 1];
                                                    cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                    await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                }
                                                await Task.Delay(CommonHelper.RandomRange(2000, 3000));
                                            }
                                        }

                                        //打开详情
                                        open_btn = page.Locator(".welcome-popup-open-button");
                                        if (await open_btn.CountAsync() > 0)
                                        {
                                            if (new int[] { 1, 2, 3, 7, 8, 9 }.Contains(CommonHelper.RandomRange(1, 10)))
                                            {
                                                try
                                                {
                                                    pagesCount = context.Pages.Count;
                                                    current_page_url = page.Url;
                                                    await CDPHelper.MouseClickAsync(page, cdpSession, open_btn.First);
                                                    await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
                                                }
                                                catch (TimeoutException)
                                                {

                                                }
                                                if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                {
                                                    if (context.Pages.Count > pagesCount)
                                                    {
                                                        page = context.Pages[context.Pages.Count - 1];
                                                        cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                        await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                    }
                                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000));
                                                    goto task_sleep;
                                                }
                                            }
                                        }
                                        //关闭弹窗,再触发细节
                                        var close_btn = page.Locator(".close-btn,.close-area .close-icon");
                                        if (await close_btn.CountAsync() > 0)
                                        {
                                            await CDPHelper.MouseClickAsync(page, cdpSession, close_btn.First);
                                            await Task.Delay(CommonHelper.RandomRange(3000, 5000));
                                        }
                                        await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                        var offer_items = page.Locator(".ad-card-title,.ad-card-image,.ad-card-conv-btn");
                                        if (await offer_items.CountAsync() > 0)
                                        {
                                            var offer_items_count = await offer_items.CountAsync();
                                            var offer_item = offer_items.Nth(CommonHelper.RandomRange(0, offer_items_count));
                                            await SwipeEmulator.SwipeToElementAsync(page, cdpSession, offer_item);
                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            try
                                            {
                                                pagesCount = context.Pages.Count;
                                                current_page_url = page.Url;
                                                await CDPHelper.MouseClickAsync(page, cdpSession, offer_item);
                                                await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
                                            }
                                            catch (TimeoutException)
                                            {

                                            }
                                            if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                            {
                                                if (context.Pages.Count > pagesCount)
                                                {
                                                    page = context.Pages[context.Pages.Count - 1];
                                                    cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                    await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                }
                                                await Task.Delay(CommonHelper.RandomRange(2000, 3000));
                                            }
                                        }
                                        else
                                        {
                                            try
                                            {
                                                var clickableHandles = await page.EvaluateHandleAsync(@"() => {
                                                            const all = Array.from(document.querySelectorAll('*'));
                                                            const visible = all.filter(el => {
                                                                const style = window.getComputedStyle(el);
                                                                const rect = el.getBoundingClientRect();
                                                                return style.visibility !== 'hidden' &&
                                                                       style.display !== 'none' &&
                                                                       rect.width > 0 && rect.height > 0 &&
                                                                       rect.top >= 0 && rect.left >= 0 &&
                                                                       rect.bottom <= window.innerHeight &&
                                                                       rect.right <= window.innerWidth;
                                                            });

                                                            // 判断是否可点击且不被覆盖
                                                            return visible.filter(el => {
                                                                const rect = el.getBoundingClientRect();
                                                                const x = rect.left + rect.width / 2;
                                                                const y = rect.top + rect.height / 2;

                                                                const topEl = document.elementFromPoint(x, y);
                                                                // 检查元素绑定点击事件
                                                                const hasClick = el.onclick || (typeof getEventListeners !== 'undefined' && getEventListeners(el).click?.length > 0);

                                                                // topEl 可能是子节点，判断 el 是否包含 topEl
                                                                const notCovered = topEl && (el === topEl || el.contains(topEl));

                                                                return hasClick && notCovered;
                                                            });
                                                         }");
                                                var props = await clickableHandles.GetPropertiesAsync();
                                                var elements = new List<IElementHandle>();
                                                foreach (var prop in props.Values)
                                                {
                                                    var handle = prop.AsElement();
                                                    if (handle != null)
                                                        elements.Add(handle);
                                                }
                                                // 随机点击一个
                                                if (elements.Count > 0)
                                                {
                                                    List<int> elements_range = Enumerable.Range(0, elements.Count).OrderBy(o => Guid.NewGuid()).ToList();
                                                    foreach (var target_index in elements_range)
                                                    {
                                                        try
                                                        {
                                                            var target = elements[target_index];
                                                            pagesCount = context.Pages.Count;
                                                            current_page_url = page.Url;
                                                            await CDPHelper.MouseClickAsync(page, cdpSession, target);
                                                            await Task.Delay(CommonHelper.RandomRange(50, 100));
                                                            await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });

                                                        }
                                                        catch (TimeoutException)
                                                        {
                                                        }
                                                        if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                        {
                                                            if (context.Pages.Count > pagesCount)
                                                            {
                                                                page = context.Pages[context.Pages.Count - 1];
                                                                cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                                await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                            }
                                                            goto task_sleep;
                                                        }
                                                    }

                                                }
                                                else
                                                {

                                                    var linkLocators = page.Locator("a,img").Filter(new LocatorFilterOptions { Has = page.Locator(":visible") });
                                                    var count = await linkLocators.CountAsync();
                                                    var clickableLinks = new List<ILocator>();
                                                    for (int i = 0; i < count; i++)
                                                    {
                                                        var link = linkLocators.Nth(i);
                                                        if (await link.IsEnabledAsync() && await link.IsVisibleAsync())
                                                        {
                                                            clickableLinks.Add(link);
                                                        }
                                                    }
                                                    if (clickableLinks.Count() > 0)
                                                    {
                                                        foreach (var link in clickableLinks.OrderBy(o => Guid.NewGuid()))
                                                        {
                                                            await link.ScrollIntoViewIfNeededAsync();
                                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));

                                                            try
                                                            {
                                                                pagesCount = context.Pages.Count;
                                                                current_page_url = page.Url;
                                                                await CDPHelper.MouseClickAsync(page, cdpSession, link.First);
                                                                await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
                                                            }
                                                            catch (TimeoutException)
                                                            {

                                                            }
                                                            if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                            {
                                                                if (context.Pages.Count > pagesCount)
                                                                {
                                                                    page = context.Pages[context.Pages.Count - 1];
                                                                    cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                                    await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                                }
                                                                goto task_sleep;
                                                            }

                                                        }

                                                    }


                                                }
                                            }
                                            catch (Exception)
                                            {

                                            }


                                        }

                                    }
                                    else if (page.Url.StartsWith("https://aistudy.baidu.com/"))
                                    {
                                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                        var recommend_adlis = page.Locator(".recommend-adlist .waterfall-column");
                                        var recommend_adlis_count = await recommend_adlis.CountAsync();
                                        if (recommend_adlis_count == 0)
                                        {
                                            if (await CDPHelper.FindItemAndClickAsync(page, cdpSession, ".search-page-container input"))
                                            {
                                                if (await CDPHelper.FindItemAndClickAsync(page, cdpSession, ".search-page-container .search"))
                                                {
                                                    int redo_ad_count = 0;
                                                redo_adlist:
                                                    if (redo_ad_count++ < 5)
                                                    {
                                                        recommend_adlis = page.Locator(".recommend-adlist .waterfall-column");
                                                        recommend_adlis_count = await recommend_adlis.CountAsync();
                                                        if (recommend_adlis_count == 0)
                                                        {
                                                            if (await CDPHelper.FindItemAndClickAsync(page, cdpSession, ".no-result-btn"))
                                                            {
                                                                await Task.Delay(1500);
                                                                goto redo_adlist;
                                                            }

                                                        }

                                                    }
                                                }
                                            }
                                            if (recommend_adlis_count > 0)
                                            {
                                                var offer_items = page.Locator(".recommend-adlist .waterfall-column");
                                                if (await offer_items.CountAsync() > 0)
                                                {
                                                    var offer_items_count = await offer_items.CountAsync();
                                                    var offer_item = offer_items.Nth(CommonHelper.RandomRange(0, offer_items_count));
                                                    await SwipeEmulator.SwipeToElementAsync(page, cdpSession, offer_item);
                                                    await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                                    try
                                                    {
                                                        pagesCount = context.Pages.Count;
                                                        current_page_url = page.Url;
                                                        await CDPHelper.MouseClickAsync(page, cdpSession, offer_item);
                                                        await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
                                                    }
                                                    catch (TimeoutException)
                                                    {

                                                    }
                                                    if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                    {
                                                        if (context.Pages.Count > pagesCount)
                                                        {
                                                            page = context.Pages[context.Pages.Count - 1];
                                                            cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                            await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                        }
                                                        await Task.Delay(CommonHelper.RandomRange(2000, 3000));
                                                    }
                                                }

                                            }
                                        }


                                    }
                                    else
                                    {
                                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                        if (_appSettings.p4psearch && _appSettings.p4psearchRate > 0 && page.Url.Contains("m.1688.com"))
                                        {
                                            _aggregator.AddLocalMetric(taskid, "dsp_p4psearch");
                                            var metrics = _aggregator.GetLocalMetrics(taskid, "dsp_p4psearch", "dsp_p4psearch_click");
                                            if (metrics["dsp_p4psearch"] > 0)
                                            {
                                                LogWriteLine($"1688询价比率:{(metrics["dsp_p4psearch_click"] / (double)metrics["dsp_p4psearch"] * 100):N2}%");
                                            }

                                            if (_appSettings.p4psearchRate == 100 || metrics["dsp_p4psearch_click"] == 0 || ((metrics["dsp_p4psearch_click"] / (double)metrics["dsp_p4psearch"]) * 100 < _appSettings.p4psearchRate))
                                            {
                                                try
                                                {
                                                    await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(5, 8), 1, predexp: async (_p) =>
                                                    {
                                                        var _ab_el = page.Locator("div[class*='ab-recommend-words']");
                                                        if (await _ab_el.CountAsync() > 0)
                                                        {
                                                            await _ab_el.First.ScrollIntoViewIfNeededAsync();
                                                            return true;
                                                        }
                                                        return false;
                                                    });
                                                    await TouchPageScrollUp(page, cdpSession);
                                                    await Task.Delay(CommonHelper.RandomRange(100, 200));
                                                    var _ab_el = page.Locator("div[class*='ab-recommend-words']");
                                                    if (await _ab_el.CountAsync() > 0)
                                                    {
                                                        var recommends = await page.QuerySelectorAllAsync("div[class*='ab-recommend-words'] a.word");
                                                        if (recommends.Count > 0)
                                                        {
                                                            _aggregator.AddLocalMetric(taskid, "dsp_p4psearch_click");
                                                            var recommend = recommends[CommonHelper.RandomRange(0, recommends.Count)];
                                                            await recommend.ScrollIntoViewIfNeededAsync();
                                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                                            await CDPHelper.MouseClickAsync(page, cdpSession, recommend, timeout: 2000);
                                                            await Task.Delay(CommonHelper.RandomRange(2000, 3000));
                                                        }
                                                    }
                                                }
                                                catch (Exception)
                                                {

                                                }
                                            }




                                        }



                                        ILocator? offer_items = null;
                                        if (page.Url.Contains("m.p4psearch.1688.com"))
                                        {

                                            if (_appSettings.Rfq1688 && _appSettings.Rfq1688Rate > 0)
                                            {
                                                _aggregator.AddLocalMetric(taskid, "dsp_rfq1688");

                                            }
                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            offer_items = page.Locator("//div[starts-with(@class,'offer-item')]");
                                        }

                                        else if (page.Url.Contains("m.1688.com"))
                                        {
                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            offer_items = page.Locator("//div[starts-with(@class,'offer-item')]");
                                        }
                                        else if (page.Url.Contains("b2b.baidu.com"))
                                        {
                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            offer_items = page.Locator(".img-content,.list-title,.content-without-title");
                                            if (await offer_items.CountAsync() == 0)
                                            {
                                                //product-item product-item-list product-item-small
                                                offer_items = page.Locator("a.product-item-link");
                                            }


                                            //c-touchable-feedback-content,.img-content,.list-title,.content-without-title
                                        }
                                        else if (page.Url.Contains("aden.baidu.com") || page.Url.Contains("ada.baidu.com"))
                                        {
                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            offer_items = page.Locator("//div[contains(@class,'ec_content')]");
                                        }
                                        else if (page.Url.Contains("uland.taobao.com"))
                                        {
                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            offer_items = page.Locator("//a[starts-with(@class,'link')]");
                                        }
                                        else if (page.Url.StartsWith("https://pro.m.jd.com/mall/active"))
                                        {
                                            //https://pro.m.jd.com/mall/active1

                                            var his1 = page.Locator("*:has-text('医院')");
                                            var his2 = page.Locator("*:has-text('问诊')");
                                            if (await his1.CountAsync() > 0 || await his2.CountAsync() > 0)
                                            {

                                                try
                                                {
                                                    int jd_redo_count = 0;
                                                jd_redo:
                                                    if (jd_redo_count++ < 3)
                                                    {
                                                        await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(1, 5), 1);
                                                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                                        var clickableHandles = await page.EvaluateHandleAsync(@"() => {
                                                            const all = Array.from(document.querySelectorAll('*'));
                                                            const visible = all.filter(el => {
                                                                const style = window.getComputedStyle(el);
                                                                const rect = el.getBoundingClientRect();
                                                                return style.visibility !== 'hidden' &&
                                                                       style.display !== 'none' &&
                                                                       rect.width > 0 && rect.height > 0 &&
                                                                       rect.top >= 0 && rect.left >= 0 &&
                                                                       rect.bottom <= window.innerHeight &&
                                                                       rect.right <= window.innerWidth;
                                                            });

                                                            // 判断是否可点击且不被覆盖
                                                            return visible.filter(el => {
                                                                const rect = el.getBoundingClientRect();
                                                                const x = rect.left + rect.width / 2;
                                                                const y = rect.top + rect.height / 2;

                                                                const topEl = document.elementFromPoint(x, y);
                                                                // 检查元素绑定点击事件
                                                                const hasClick = el.onclick || (typeof getEventListeners !== 'undefined' && getEventListeners(el).click?.length > 0);

                                                                // topEl 可能是子节点，判断 el 是否包含 topEl
                                                                const notCovered = topEl && (el === topEl || el.contains(topEl));

                                                                return hasClick && notCovered;
                                                            });
                                                         }");

                                                        // 转换成 Locator
                                                        var props = await clickableHandles.GetPropertiesAsync();
                                                        var elements = new List<IElementHandle>();
                                                        foreach (var prop in props.Values)
                                                        {
                                                            var handle = prop.AsElement();
                                                            if (handle != null)
                                                                elements.Add(handle);
                                                        }

                                                        // 随机点击一个
                                                        if (elements.Count > 0)
                                                        {
                                                            List<int> elements_range = Enumerable.Range(0, elements.Count).OrderBy(o => Guid.NewGuid()).ToList();

                                                            foreach (var target_index in elements_range)
                                                            {
                                                                var target = elements[target_index];

                                                                pagesCount = context.Pages.Count;
                                                                current_page_url = page.Url;
                                                                await CDPHelper.MouseClickAsync(page, cdpSession, target);
                                                                await Task.Delay(CommonHelper.RandomRange(50, 100));
                                                                try
                                                                {
                                                                    await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });

                                                                }
                                                                catch (TimeoutException)
                                                                {


                                                                }

                                                                if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                                {
                                                                    if (context.Pages.Count > pagesCount)
                                                                    {
                                                                        page = context.Pages[context.Pages.Count - 1];
                                                                        cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                                        await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                                    }

                                                                    if (page.Url.StartsWith("https://pro.m.jd.com/mall/active"))
                                                                    {
                                                                        goto jd_redo;
                                                                    }

                                                                    if (page.Url.StartsWith("https://laputa.healthjd.com/doctor_home"))
                                                                        break;

                                                                    goto task_sleep;
                                                                }
                                                            }


                                                        }
                                                    }
                                                }
                                                catch (Exception)
                                                {

                                                }

                                            }
                                            else
                                            {


                                                try
                                                {
                                                    int jd_redo_count = 0;
                                                jd_redo:
                                                    if (jd_redo_count++ < 3)
                                                    {
                                                        await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                                        await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                                        var clickableHandles = await page.EvaluateHandleAsync(@"() => {
                                                            const all = Array.from(document.querySelectorAll('*'));
                                                            const visible = all.filter(el => {
                                                                const style = window.getComputedStyle(el);
                                                                const rect = el.getBoundingClientRect();
                                                                return style.visibility !== 'hidden' &&
                                                                       style.display !== 'none' &&
                                                                       rect.width > 0 && rect.height > 0 &&
                                                                       rect.top >= 0 && rect.left >= 0 &&
                                                                       rect.bottom <= window.innerHeight &&
                                                                       rect.right <= window.innerWidth;
                                                            });

                                                            // 判断是否可点击且不被覆盖
                                                            return visible.filter(el => {
                                                                const rect = el.getBoundingClientRect();
                                                                const x = rect.left + rect.width / 2;
                                                                const y = rect.top + rect.height / 2;

                                                                const topEl = document.elementFromPoint(x, y);
                                                                // 检查元素绑定点击事件
                                                                const hasClick = el.onclick || (typeof getEventListeners !== 'undefined' && getEventListeners(el).click?.length > 0);

                                                                // topEl 可能是子节点，判断 el 是否包含 topEl
                                                                const notCovered = topEl && (el === topEl || el.contains(topEl));

                                                                return hasClick && notCovered;
                                                            });
                                                         }");
                                                        var props = await clickableHandles.GetPropertiesAsync();
                                                        var elements = new List<IElementHandle>();
                                                        foreach (var prop in props.Values)
                                                        {
                                                            var handle = prop.AsElement();
                                                            if (handle != null)
                                                                elements.Add(handle);
                                                        }
                                                        // 随机点击一个
                                                        if (elements.Count > 0)
                                                        {
                                                            List<int> elements_range = Enumerable.Range(0, elements.Count).OrderBy(o => Guid.NewGuid()).ToList();

                                                            foreach (var target_index in elements_range)
                                                            {
                                                                var target = elements[target_index];

                                                                pagesCount = context.Pages.Count;
                                                                current_page_url = page.Url;
                                                                await CDPHelper.MouseClickAsync(page, cdpSession, target);
                                                                await Task.Delay(CommonHelper.RandomRange(50, 100));
                                                                try
                                                                {
                                                                    await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });

                                                                }
                                                                catch (TimeoutException)
                                                                {


                                                                }

                                                                if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                                {
                                                                    if (context.Pages.Count > pagesCount)
                                                                    {
                                                                        page = context.Pages[context.Pages.Count - 1];
                                                                        cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                                        await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                                    }

                                                                    if (page.Url.StartsWith("https://pro.m.jd.com/mall/active"))
                                                                    {
                                                                        goto jd_redo;
                                                                    }

                                                                    if (page.Url.StartsWith("https://laputa.healthjd.com/doctor_home"))
                                                                        break;

                                                                    goto task_sleep;
                                                                }
                                                            }


                                                        }
                                                    }
                                                }
                                                catch (Exception)
                                                {

                                                }
                                            }
                                            offer_items = page.Locator(".masonryCard,.commodity-list .commodity-desc,.list-con .product,a.goods,.feed-product-container");
                                            if (await offer_items.CountAsync() == 0)
                                                offer_items = page.Locator(".feed-product-container");
                                            if (await offer_items.CountAsync() == 0)
                                                offer_items = page.Locator(".feed-product-container,a.goods,.list-con .product");
                                            if (await offer_items.CountAsync() == 0)
                                                offer_items = page.Locator("img");
                                        }
                                        else if (page.Url.Contains("m.jd.com"))
                                        {

                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            //.product,
                                            //a.goods,a.product
                                            //offer_items = page.Locator("//div[contains(@class,'feeds-product-container')]");
                                            offer_items = page.Locator(".commodity-list .commodity-desc,.list-con .product,a.goods,.feed-product-container");
                                            if (await offer_items.CountAsync() == 0)
                                                offer_items = page.Locator(".feed-product-container");
                                            if (await offer_items.CountAsync() == 0)
                                                offer_items = page.Locator(".feed-product-container,a.goods,.list-con .product");
                                            if (await offer_items.CountAsync() == 0)
                                                offer_items = page.Locator("img");
                                        }

                                        if (offer_items != null)
                                        {

                                            int offer_items_count = await offer_items.CountAsync();
                                            if (offer_items_count > 0)
                                            {
                                                var offer_item = offer_items.Nth(CommonHelper.RandomRange(0, offer_items_count));
                                                await SwipeEmulator.SwipeToElementAsync(page, cdpSession, offer_item);
                                                //await offer_item.ScrollIntoViewIfNeededAsync();
                                                await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                                try
                                                {
                                                    pagesCount = context.Pages.Count;
                                                    current_page_url = page.Url;
                                                    await CDPHelper.MouseClickAsync(page, cdpSession, offer_item);
                                                    await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });

                                                }
                                                catch (TimeoutException)
                                                {

                                                }
                                                if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                {
                                                    if (context.Pages.Count > pagesCount)
                                                    {
                                                        page = context.Pages[context.Pages.Count - 1];
                                                        cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                        await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                    }
                                                    ProcessingPageElementTask(page, cdpSession);
                                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000));
                                                }
                                            }

                                        }
                                        else
                                        {
                                            var linkLocators = page.Locator("a:visible");
                                            var count = await linkLocators.CountAsync();
                                            var clickableLinks = new List<ILocator>();
                                            for (int i = 0; i < count; i++)
                                            {
                                                var link = linkLocators.Nth(i);
                                                if (await link.IsEnabledAsync() && await link.IsVisibleAsync())
                                                {
                                                    clickableLinks.Add(link);
                                                }
                                            }
                                            if (clickableLinks.Count() > 0)
                                            {
                                                foreach (var link in clickableLinks)
                                                {
                                                    await link.ScrollIntoViewIfNeededAsync();
                                                    await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                                    try
                                                    {
                                                        pagesCount = context.Pages.Count;
                                                        current_page_url = page.Url;
                                                        await CDPHelper.MouseClickAsync(page, cdpSession, link.First);
                                                        await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });

                                                    }
                                                    catch (TimeoutException)
                                                    {

                                                    }
                                                    if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                    {
                                                        if (context.Pages.Count > pagesCount)
                                                        {
                                                            page = context.Pages[context.Pages.Count - 1];
                                                            cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                            await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                        }
                                                        goto task_sleep;
                                                    }

                                                }

                                            }

                                        }

                                    }
                                }
                                catch
                                {

                                }
                                goto task_sleep;
                            }
                        }
                    }

                }

            #endregion


            task_sleep:
                {

                    if (_appSettings.Rfq1688 && _appSettings.Rfq1688Rate > 0 && page.Url.Contains("m.p4psearch.1688.com"))
                    {
                        try
                        {
                            var metrics = _aggregator.GetLocalMetrics(taskid, "dsp_rfq1688", "dsp_rfq1688_click");
                            if (metrics["dsp_rfq1688"] > 0)
                            {
                                LogWriteLine($"1688询价比率:{(metrics["dsp_rfq1688_click"] / (double)metrics["dsp_rfq1688"] * 100):N2}%");
                            }
                            if (_appSettings.Rfq1688Rate == 100 || metrics["dsp_rfq1688_click"] == 0 || ((metrics["dsp_rfq1688_click"] / (double)metrics["dsp_rfq1688"]) * 100 < _appSettings.Rfq1688Rate))
                            {
                                await Task.Delay(CommonHelper.RandomRange(3000, 5000));


                                var el = page.Locator("#od_xst_phone_input_val_new,#new_od_xst_phone_input_val_new");
                                if (await el.CountAsync() == 0)
                                {
                                    var queryBtn = page.Locator(".queryBtnTitleTop");
                                    if (await queryBtn.CountAsync() == 0)
                                    {
                                        queryBtn = page.GetByText("立即询价");
                                    }
                                    if (await queryBtn.CountAsync() > 0)
                                    {
                                        await CDPHelper.MouseClickAsync(page, cdpSession, queryBtn.First, timeout: 1500);
                                        await Task.Delay(new Random().Next(800, 1200));
                                        el = page.Locator("#od_xst_phone_input_val_new,#new_od_xst_phone_input_val_new");
                                    }
                                }

                                if (await el.CountAsync() > 0)
                                {
                                    _aggregator.AddLocalMetric(taskid, "dsp_rfq1688_click");
                                    var phone = await _adeHelper.GetPhoneNumberAsync();
                                    if (!string.IsNullOrWhiteSpace(phone))
                                    {
                                        await el.First.FillAsync("");
                                        await Task.Delay(new Random().Next(50, 100));
                                        await el.First.PressSequentiallyAsync(phone);
                                        await Task.Delay(new Random().Next(1500, 2000));
                                        var answer_contents = page.Locator("div.new_answer_content span,div.answer_content span");
                                        var answer_contents_count = await answer_contents.CountAsync();
                                        if (answer_contents_count > 0)
                                        {
                                            var answer_content = answer_contents.Nth(CommonHelper.RandomRange(0, answer_contents_count));
                                            await CDPHelper.MouseClickAsync(page, cdpSession, answer_content.First, timeout: 1000);
                                            await Task.Delay(new Random().Next(1500, 2000));
                                        }
                                        else
                                        {
                                            var answer_content_texts = new string[] { "有没有现货", "价格还有空间吗", "什么时间发货", "有活动吗", "工厂在哪里", "实物图是否一致", "能否提供质检", "可以寄样品给我吗", "批发价是多少", "可以开发票吧", "这款支持一件代发吗", "包邮吗" };
                                            el = page.Locator("textarea#new_od_xst_msg_input_val_new_message,textarea#od_xst_msg_input_val_new_message");
                                            if (await el.CountAsync() > 0)
                                            {
                                                await el.First.FillAsync("");
                                                await Task.Delay(new Random().Next(50, 100));
                                                await el.First.PressSequentiallyAsync(answer_content_texts[CommonHelper.RandomRange(0, answer_content_texts.Length)]);
                                                await Task.Delay(new Random().Next(1500, 2000));
                                            }
                                        }
                                        el = page.Locator(".new_successTipNew_wangwang_new,.successTipNew_call_new");
                                        if (await el.CountAsync() > 0)
                                        {
                                            try
                                            {
                                                await CDPHelper.MouseClickAsync(page, cdpSession, el.First, timeout: 2000);
                                                await Task.Delay(new Random().Next(2000, 3000));
                                                var sms_code = page.GetByText("获取验证码");
                                                if (await sms_code.CountAsync() > 0)
                                                {
                                                    el = page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new");
                                                    if (await el.CountAsync() > 0)
                                                    {
                                                        await CDPHelper.MouseClickAsync(page, cdpSession, el.First, timeout: 1500);
                                                    }
                                                }
                                            }
                                            catch (Exception)
                                            {


                                            }

                                            el = page.Locator(".successTipNew_close_new,.newSuccessTipNew_close_new,.newCloseIcon_content");
                                            if (await el.CountAsync() > 0)
                                            {
                                                await CDPHelper.MouseClickAsync(page, cdpSession, el.First, timeout: 1500);
                                            }
                                        }


                                        var btnGoShop = page.Locator(".new_offer_card-title div:text-is('进店看看')");
                                        if (await btnGoShop.CountAsync() > 0)
                                        {

                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            await btnGoShop.First.ScrollIntoViewIfNeededAsync();
                                            await Task.Delay(CommonHelper.RandomRange(100, 200));
                                            current_page_url = page.Url;
                                            await CDPHelper.MouseClickAsync(page, cdpSession, btnGoShop.First);
                                            await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
                                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                            var leafDivs = page
                                            .FrameLocator("iframe[src*='getPageModuleResourceRax1']")
                                            .Locator("div#moreOffer + div div:not(:has(*))");
                                            int count = await leafDivs.CountAsync();

                                            await ClearPageCloseBtn(page, cdpSession);
                                            for (int i = 0; i < count; i++)
                                            {
                                                var item = leafDivs.Nth(i);
                                                if (await item.IsVisibleAsync())
                                                {
                                                    try
                                                    {

                                                        current_page_url = page.Url;
                                                        await ClearPageCloseBtn(page, cdpSession);
                                                        await CDPHelper.MouseClickAsync(page, cdpSession, item);
                                                        await page.WaitForURLAsync(url => !url.Equals(current_page_url), new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 10000 });
                                                    }
                                                    catch (TimeoutException)
                                                    {

                                                    }
                                                    if ((context.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                    {
                                                        if (context.Pages.Count > pagesCount)
                                                        {
                                                            page = context.Pages[context.Pages.Count - 1];
                                                            cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
                                                            await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                        }
                                                        break;
                                                    }


                                                }
                                            }
                                        }

                                    }

                                }
                            }

                        }
                        catch (Exception)
                        {


                        }

                    }


                    if (page.Url.StartsWith("https://qianhu.wejianzhan.com/"))
                    {
                        try
                        {
                            var phoneNumber = await _adeHelper.GetPhoneNumberAsync();
                            if (!string.IsNullOrWhiteSpace(phoneNumber))
                            {
                                var surname = _nameGenerator.GetDisplayName(phoneNumber);
                                var input = page.Locator("input[placeholder='请输入您的称呼']").First;
                                if (await input.CountAsync() > 0)
                                {
                                    await input.FillAsync("");
                                    await input.PressSequentiallyAsync(surname);
                                }
                                await Task.Delay(CommonHelper.RandomRange(300, 500));
                                var input2 = page.Locator("input[placeholder='请输入手机号']").First;
                                if (await input2.CountAsync() > 0)
                                {
                                    await input2.FillAsync("");
                                    await input2.PressSequentiallyAsync(phoneNumber);
                                }
                                var radio1 = page.Locator(".phone-agrement-container .phone-agrement-radio");
                                if (await radio1.CountAsync() > 0)
                                {
                                    await CDPHelper.MouseClickAsync(page, cdpSession, radio1.First);
                                }
                                //phone-agrement-container enhance-dynamic-white-font .phone-agrement-radio
                                //await Task.Delay(CommonHelper.RandomRange(300, 500));
                                //var input3 = page.Locator("input[placeholder='请输入收票地址']").First;
                                //if (await input3.CountAsync() > 0)
                                //{
                                //    await input3.FillAsync("");
                                //    await input3.PressSequentiallyAsync("电联");
                                //}
                                var btnSubmit = page.Locator("div:has-text('免费领票')").First;
                                if (await btnSubmit.CountAsync() > 0)
                                {
                                    await CDPHelper.MouseClickAsync(page, cdpSession, btnSubmit);
                                    await Task.Delay(CommonHelper.RandomRange(3000, 5000));
                                }
                            }
                        }
                        catch (Exception)
                        {


                        }

                    }


                    this.QTPExecuteSuccess(taskid);
                    LogWriteLine($"{this.Title}:ExecuteWorker:Success");
                    if (trigger_download_sign > 0)
                    {
                        goto task_end;
                    }
                    //havanalogin.taobao.com
                    if (page.Url.StartsWith("https://login.m.taobao.com") || page.Url.StartsWith("https://havanalogin.taobao.com") || page.Url.StartsWith("https://plogin.m.jd.com"))
                    {
                        await Task.Delay(new Random().Next(3000, 5000));
                        goto task_end;
                    }

                    if (totalPV > 1)
                    {
                        if (jumpClick && page_trigger_click)
                        {
                            if (!pvsTriggerOne)
                            {
                                await TouchPageScroll(page, cdpSession, 1, 1);
                                await Task.Delay(CommonHelper.RandomRange(800, 1200));
                                LogWriteLine("动作完成");
                                goto redo_pv;
                            }
                        }
                        else
                        {
                            await TouchPageScroll(page, cdpSession, 1, 1);
                            await Task.Delay(CommonHelper.RandomRange(800, 1200));
                            LogWriteLine("动作完成");
                            goto redo_pv;
                        }
                        LogWriteLine($"延时停留");
                        DateTime s1 = System.DateTime.Now;
                        await Task.Delay(new Random().Next(3000, 5000));
                        LogWriteLine($"准备滑动");
                        await TouchPageScroll(page, cdpSession, 1, 0);
                        await Task.Delay(new Random().Next(1000, 2000));
                    }
                    else
                    {
                        LogWriteLine($"延时停留");
                        DateTime s1 = System.DateTime.Now;
                        await Task.Delay(new Random().Next(2000, 3000));
                        sleep -= 2;
                        int gestureOrientation = 1;
                        LogWriteLine($"准备滑动");
                        do
                        {
                            try
                            {
                                LogWriteLine($"滑动操作");
                                await TouchPageScroll(page, cdpSession, 1, gestureOrientation);
                                if (await IsPageEnd(page))
                                {
                                    gestureOrientation = -1;
                                }
                                else if (await IsPageTop(page))
                                {
                                    gestureOrientation = 1;
                                }
                                if ((int)((TimeSpan)(System.DateTime.Now - s1)).TotalSeconds >= sleep)
                                    break;
                                await Task.Delay(new Random().Next(1000, 2000));

                                if (trigger_download_sign > 0)
                                {
                                    goto task_end;
                                }
                            }
                            catch (Exception)
                            {
                                break;
                            }
                        } while (true);
                    }
                    LogWriteLine("动作完成");
                }
            task_end:
                {
                    this.QTPExecuteComplete(taskid);
                    LogWriteLine($"{this.Title}:ExecuteWorker:Complete");
                    return (true, page_trigger_click, page_ads_count);
                }
            }
            catch (Exception ex)
            {
                LogWriteLine(ex.Message);
            }
            finally
            {
                try
                {
                    if (browser != null)
                        await browser.CloseAsync();   // 只断 CDP
                }
                catch { }
                await CloseBrowserProcess(uniqueId);
            }
            return (false, page_trigger_click, page_ads_count);
        }
    }
}
