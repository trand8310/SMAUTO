using Microsoft.Playwright;
using Newtonsoft.Json.Linq;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using SMAd;
using SMAd.Swiper;



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
        /// 触摸滑动
        /// </summary>
        /// <param name="page"></param>
        /// <param name="client"></param>
        /// <param name="scrollCount"></param>
        /// <param name="direction"></param>
        /// <param name="predexp"></param>
        /// <param name="timeDelay"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task TouchPageScrollAsync(
            IPage page,
            ICDPSession client,
            int scrollCount,
            int direction,
            Func<IPage, Task<bool>>? predexp = null,
            int timeDelay = 0,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null || scrollCount <= 0)
                return;

            try
            {
                var scrollDirection = direction >= 0
                    ? ScrollDirection.Up
                    : ScrollDirection.Down;

                for (int i = 0; i < scrollCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (page.IsClosed)
                        break;

                    if (predexp != null)
                    {
                        try
                        {
                            if (await predexp(page))
                                break;
                        }
                        catch
                        {
                        }
                    }

                    // 向下滑前，先判断是否已接近顶部
                    if (direction < 0)
                    {
                        bool nearTop = await IsNearTopAsync(page, 10);
                        if (nearTop)
                            break;
                    }

                    int distancePx = direction >= 0
                        ? RandomUtil.NextInt(78, 118)
                        : RandomUtil.NextInt(55, 82);

                    int pointCount = distancePx <= 70 ? 7
                        : distancePx <= 95 ? 8
                        : 9;

                    int delayMs = RandomUtil.NextInt(11, 15);
                    float jitter = (float)RandomUtil.NextDouble(0.28, 0.42);

                    await SwipeEmulator.SwipeMultipleMicroAsync(
                        page: page,
                        client: client,
                        times: 1,
                        distancePx: distancePx,
                        pointCount: pointCount,
                        delayMs: delayMs,
                        jitter: jitter,
                        direction: scrollDirection,
                        cancellationToken: cancellationToken);

                    int pause = timeDelay > 0
                        ? timeDelay
                        : RandomUtil.NextInt(450, 850);

                    await Task.Delay(pause, cancellationToken);

                    if (predexp != null)
                    {
                        try
                        {
                            if (await predexp(page))
                                break;
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }



        private async Task TouchPageScroll(
            IPage page,
            ICDPSession client,
            int scrollCount,
            int direction,
            Func<IPage, Task<bool>>? predexp = null,
            int time_delay = 0)
        {
            await TouchPageScrollAsync(
                page: page,
                client: client,
                scrollCount: scrollCount,
                direction: direction,
                predexp: predexp,
                timeDelay: time_delay);
        }




        /// <summary>
        /// 页面向上滑动一点
        /// </summary>
        /// <param name="page"></param>
        /// <param name="client"></param>
        /// <param name="distancePx"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>

        private async Task TouchPageScrollUpAsync(
           IPage page,
           ICDPSession client,
           int distancePx = 80,
           CancellationToken cancellationToken = default)
        {
            try
            {
                await SwipeEmulator.SwipeMultipleMicroAsync(
                    page: page,
                    client: client,
                    times: 1,
                    distancePx: distancePx,
                    pointCount: distancePx <= 80 ? 5 : 6,
                    delayMs: 6,
                    jitter: 0.2f,
                    direction: ScrollDirection.Up,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        /// <summary>
        /// 页面向下滑动一点
        /// </summary>
        /// <param name="page"></param>
        /// <param name="client"></param>
        /// <param name="distancePx"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>

        private async Task TouchPageScrollDownAsync(
           IPage page,
           ICDPSession client,
           int distancePx = 90,
           CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null)
                return;

            try
            {
                distancePx = Math.Clamp(distancePx, 60, 180);

                int finalDistance = Math.Clamp(
                    distancePx + RandomUtil.NextInt(-8, 9),
                    60,
                    180);

                int pointCount;
                if (finalDistance <= 70)
                    pointCount = 7;
                else if (finalDistance <= 100)
                    pointCount = 8;
                else if (finalDistance <= 140)
                    pointCount = 9;
                else
                    pointCount = 10;

                await SwipeEmulator.SwipeMultipleMicroAsync(
                    page: page,
                    client: client,
                    times: 1,
                    distancePx: finalDistance,
                    pointCount: pointCount,
                    delayMs: RandomUtil.NextInt(11, 15),
                    jitter: (float)RandomUtil.NextDouble(0.28, 0.42),
                    direction: ScrollDirection.Down,
                    cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }



        private async Task SynthesizeScrollGestureAsync(
        IPage page,
        ICDPSession client,
        int scrollCount,
        int direction,
        Func<IPage, Task<bool>>? predexp = null,
        int timeDelay = 0,
        CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null || scrollCount <= 0)
                return;

            try
            {
                LogWriteLine(direction == -1
                    ? $"TouchScrollDown:{scrollCount}次"
                    : $"TouchScrollUp:{scrollCount}次");

                int delayMsAfterScroll = timeDelay > 0
                    ? timeDelay
                    : CommonHelper.RandomRange(500, 2000);

                int vw = page.ViewportSize?.Width ?? 400;
                int vh = page.ViewportSize?.Height ?? 800;

                for (int i = 0; i < scrollCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (page.IsClosed)
                        break;

                    int x = RandomUtil.NextInt((int)(vw * 0.35), (int)(vw * 0.65));
                    int y = RandomUtil.NextInt((int)(vh * 0.42), (int)(vh * 0.58));

                    int distancePx = Math.Clamp(
                        (int)(vh * RandomUtil.NextDouble(0.18, 0.32)),
                        100,
                        260);

                    // 这里正负方向需要按你页面实际测试一次
                    int yDistance = direction >= 0 ? distancePx : -distancePx;

                    await client.SendAsync("Input.synthesizeScrollGesture", new Dictionary<string, object>
                    {
                        ["x"] = x,
                        ["y"] = y,
                        ["xDistance"] = 0,
                        ["yDistance"] = yDistance,
                        ["speed"] = RandomUtil.NextInt(650, 1050),
                        ["gestureSourceType"] = "touch",
                        ["repeatCount"] = 0,
                        ["repeatDelayMs"] = 0
                    });

                    await Task.Delay(delayMsAfterScroll, cancellationToken);

                    if (predexp != null && await predexp(page))
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogWriteLine($"TouchPageScrollAsync error: {ex.Message}");
            }
        }


        private async Task GestureScrollUp(IPage page, ICDPSession client)
        {
            if (page == null || page.IsClosed || client == null)
                return;
            if (page.ViewportSize != null)
            {
                int vw = page.ViewportSize?.Width ?? 360;
                int vh = page.ViewportSize?.Height ?? 740;
                int startX = RandomUtil.NextInt((int)(vw * 0.35), (int)(vw * 0.65));
                int startY = RandomUtil.NextInt((int)(vh * 0.42), (int)(vh * 0.58));


                int xDistance = (int)(vw * (CommonHelper.RandomRange(55, 75) * 0.01));
                int yDistance = Math.Clamp(
                    (int)(vh * RandomUtil.NextDouble(0.18, 0.32)),
                    100,
                    260);

                await client.SendAsync("Input.synthesizeScrollGesture",
                    new Dictionary<string, object>()
                     {
                         { "x",startX},
                         { "y",startY},
                         { "xDistance",xDistance},
                         { "yDistance",-yDistance},
                         { "speed",RandomUtil.NextInt(650, 1050)},
                         { "repeatCount",0},
                         { "repeatDelayMs",0},
                         { "yOverscroll",CommonHelper.RandomRange(50,150)},
                         { "gestureSourceType","default"},
                     });
            }
        }
        private async Task GestureScrollDown(IPage page, ICDPSession client)
        {
            if (page == null || page.IsClosed || client == null)
                return;
            if (page.ViewportSize != null)
            {
                int vw = page.ViewportSize?.Width ?? 360;
                int vh = page.ViewportSize?.Height ?? 740;
                int startX = RandomUtil.NextInt((int)(vw * 0.45), (int)(vw * 0.50));
                int startY = RandomUtil.NextInt((int)(vh * 0.15), (int)(vh * 0.35));


                int xDistance = (int)(vw * (CommonHelper.RandomRange(50, 60) * 0.01));
                int yDistance = startY + Math.Clamp(
                    (int)(vh * RandomUtil.NextDouble(0.18, 0.32)),
                    100,
                    260);

                await client.SendAsync("Input.synthesizeScrollGesture",
                    new Dictionary<string, object>()
                     {
                         { "x",startX},
                         { "y",startY},
                         { "xDistance",xDistance},
                         { "yDistance",yDistance},
                         { "speed",RandomUtil.NextInt(650, 1050)},
                         { "repeatCount",0},
                         { "repeatDelayMs",0},
                         { "yOverscroll",-CommonHelper.RandomRange(50,150)},
                         { "gestureSourceType","touch"},
                     });
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




        public async Task ScrollWithTimeoutAsync(
            IPage page,
            CDPSessionManager cdpManager,
            int durationMs,
            CancellationToken cancellationToken = default)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (cdpManager == null) throw new ArgumentNullException(nameof(cdpManager));
            if (durationMs <= 0) return;
            var cdpSession = await cdpManager.GetOrCreateSessionAsync(page);
            if (!await CanPageScrollAsync(page))
            {

                await Task.Delay(CommonHelper.RandomRange(2000, 3000), cancellationToken);
                return;
            }

            int[] scrollSteps = [0, 0, 0, -1, 0, 0, 0, -1, 0, -1];
            var endTime = Environment.TickCount64 + durationMs;
            while (Environment.TickCount64 < endTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int step = scrollSteps[CommonHelper.RandomRange(0, scrollSteps.Length)];
                int timeDelay = CommonHelper.RandomRange(10, 20);

                await TouchPageScroll(page, cdpSession, 1, step, time_delay: timeDelay);

                int remainMs = (int)Math.Max(0, endTime - Environment.TickCount64);
                if (remainMs <= 0)
                    break;

                int delayMs = Math.Min(CommonHelper.RandomRange(1000, 2000), remainMs);
                await Task.Delay(delayMs, cancellationToken);
            }
        }




        private async Task<IBrowser?> ConnectOverCDPWithRetryAsync(
        IPlaywright playwright,
        string endpoint,
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
                    LogWriteLine($"CDP连接尝试 {attempt}/{maxAttempts}: {endpoint}");

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

                    LogWriteLine($"CDP连接成功: {endpoint}");
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

                    LogWriteLine($"CDP连接失败 {attempt}/{maxAttempts}: {ex.Message}");

                    if (attempt >= maxAttempts)
                        break;

                    await Task.Delay(delayMs, token);
                }
            }

            if (lastException != null)
            {
                LogWriteLine($"CDP连接最终失败: {lastException}");
            }

            return null;
        }




        public async Task<(bool, bool, int)> ExecuteWorker2Async(string uniqueId, JObject taskArgs, CancellationToken token)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
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
            var kernelVersion = taskArgs.SelectToken("kernelVersion")?.Value<string>() ?? _appSettings.KernelVersion;
            int maxTouchPoints = CommonHelper.RandomRange(4, 6);
            int page_ads_count = 0;
            bool page_trigger_click = false;
            var processIndex = taskArgs.SelectToken("processIndex")?.Value<int>() ?? 1;
            string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", "Chrome", kernelVersion, "User_Cache", $"{taskArgs.SelectToken("cacheName").Value<string>()}");
            string userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", "Chrome", kernelVersion, "User_Data", $"{processIndex}_{Guid.NewGuid().ToString("n")}");

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

            args.AddRange(InitFPArgs(taskArgs, maxTouchPoints));

            LogWriteLine($"args={string.Join(" ", args)}");
            using var playwright = await Playwright.CreateAsync();
            var chromium = playwright.Chromium;
            var chromePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "File", "chrome-win", kernelVersion, "chrome.exe");
            var session = await _processManager.StartChromium(uniqueId, chromePath, userDataDir, TimeSpan.FromSeconds(180), $"about:blank  {string.Join(" ", args)}", proxyServer);
            var endpoint = $"http://localhost:{session.DebugPort}";
            await using var browser = await ConnectOverCDPWithRetryAsync(
            playwright,
            endpoint,
            token,
            maxAttempts: 3,
            delayMs: 200,
            requireUsableContext: true);



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

            browser.Disconnected += (sender, e) =>
            {
                try
                {
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
                        first_page_url = "https://pro.m.jd.com/mall/active/32R3r4vG6x3RmoeJCevxY7BXjecP/index.html?babelChannel=ttt4&hy_entry=Outside_UC";

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

                                var locator_list = page.Locator("text=/剩.*个名额|图文.*起|电话.*起/").Filter(new() { Visible = true });
                                var locator_count = await locator_list.CountAsync();
                                if (locator_count > 0)
                                {
                                    foreach (var target_index in Enumerable.Range(0, locator_count).OrderBy(o => Guid.NewGuid()))
                                    {
                                        var target = locator_list.Nth(target_index);


                                    }
                                }









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
                    LogWriteLine($"{this.Title}:ExecuteWorker:曝光进入页面停留{((pageloadedDelay) / 1e3):N2}秒");
                    await ScrollWithTimeoutAsync(page, cdpManager, Math.Abs(pageloadedDelay));
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
                                                    await TouchPageScrollUpAsync(page, cdpSession);
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
                        new UMobLandingPageStrategy(this),
                        new AiSiteLandingPageStrategy(this),
                        new AiStudyLandingPageStrategy(this),
                        new GenericLandingPageStrategy(this),
                    })
                };

                this.QTPExecuteStart(config.TaskId);
                LogWriteLine($"{this.Title}:ExecuteWorker:Start");

                using var playwright = await Playwright.CreateAsync();
                ctx.Playwright = playwright;

                linkedCts.Token.ThrowIfCancellationRequested();

                var browser = await StartAndConnectBrowserAsync(ctx, linkedCts.Token);
                if (browser == null)
                    return (false, false, 0);

                ctx.Browser = browser;
                ctx.Context = browser.Contexts[0];
                ctx.CdpManager = new CDPSessionManager(ctx.Context);

                await ConfigureContextAsync(ctx, linkedCts.Token);

                await AttachLifecycleEventsAsync(ctx, linkedCts.Token);

                var ok = await RunMainFlowAsync(ctx, linkedCts.Token);

                return (ok, ctx.PageTriggerClick, ctx.PageAdsCount);
            }
            catch (OperationCanceledException)
            {
                LogWriteLine($"{this.Title}:ExecuteWorker:Canceled");
                return (false, ctx?.PageTriggerClick ?? false, ctx?.PageAdsCount ?? 0);
            }
            catch (Exception ex)
            {
                LogWriteLine(ex.ToString());
                return (false, ctx?.PageTriggerClick ?? false, ctx?.PageAdsCount ?? 0);
            }
            finally
            {
                if (ctx != null)
                {
                    try
                    {
                        if (ctx.Browser != null)
                            await ctx.Browser.CloseAsync();
                    }
                    catch { }

                    try
                    {
                        await CloseBrowserProcess(uniqueId);
                    }
                    catch { }
                }
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

                var entry = await PrepareEntryAsync(ctx, token);
                if (!entry.Success)
                {
                    if (entry.EndTask)
                        return CompleteSuccess(ctx);

                    continue;
                }
                if (ctx.Config.IsTest)
                {
                    //entry.FirstPageUrl = "https://wm.m.sm.cn/s?from=10000&q=%E6%BF%80%E7%B4%A0%E4%BE%9D%E8%B5%96%E6%80%A7%E7%9A%AE%E7%82%8E";
                    entry.FirstPageUrl = "https://pro.m.jd.com/mall/active/KtpmHjYN5sC8vyEfvBSesVjwn9Z/index.html?babelChannel=ttt12";
                }

                var gotoOk = await NavigateToEntryAsync(ctx, entry.FirstPageUrl!, token);
                if (!gotoOk)
                    continue;

                if (ctx.Config.IsTest)
                {
                    await RunTestBranchAsync(ctx, entry, token);
                    return CompleteSuccess(ctx);
                }

                ctx.ResetPerPvState();

                if (ctx.Page!.Url.Contains("punish?x5secdata"))
                {
                    this.X5Secdata(ctx.Config.TaskId, 1, ctx.Page.Url);
                    return CompleteSuccess(ctx);
                }

                if (entry.IsHomepageTrigger)
                {
                    var homepageOk = await ExecuteHomepageTriggerAsync(ctx, entry.QueryWord, token);
                    if (!homepageOk)
                        continue;
                }
                else
                {
                    LogWriteLine($"{this.Title}:ExecuteWorker:曝光进入页面停留{((ctx.Config.PageLoadedDelayMs) / 1000.0):N2}秒");
                    token.ThrowIfCancellationRequested();
                    await ScrollWithTimeoutAsync(ctx.Page!, ctx.CdpManager!, Math.Abs(ctx.Config.PageLoadedDelayMs));
                }

                var adsOk = await DetectAndUploadAdWordsAsync(ctx, entry.QueryWord, token);
                if (!adsOk)
                    continue;

                await DecideJumpClickAsync(ctx, token);

                if (ctx.JumpClick)
                {
                    var clickFlow = await TryExecuteJumpClickAsync(ctx, token);
                    if (clickFlow == FlowControl.EndTask)
                        return CompleteSuccess(ctx);
                }

                var sleepFlow = await ExecuteTaskSleepPhaseAsync(ctx, token);
                if (sleepFlow == FlowControl.EndTask)
                    return CompleteSuccess(ctx);

                if (sleepFlow == FlowControl.NextPv)
                    continue;

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
            var deviceScale = taskArgs.SelectToken("dev.pixelRatio")?.Value<float>() ?? 0;
            if (deviceScale == 0)
                deviceScale = (float)(CommonHelper.RandomRange(250, 270) / (1e2 * 1.0123456));

            var os = taskArgs.SelectToken("os")!.Value<int>();
            var devSw = taskArgs.SelectToken("dev.sw")?.Value<int>();
            var sw = (int)(taskArgs.SelectToken("dev.sw")!.Value<int>() / deviceScale);
            var sh = (int)(taskArgs.SelectToken("dev.sh")!.Value<int>() / deviceScale);

            if (os == 2)
            {
                sw = 428;
                sh = 926;
                deviceScale = (float)devSw! / sw;
            }

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
                DevSw = devSw,
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
                MaxTouchPoints = CommonHelper.RandomRange(4, 6),
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

        #region RetryPolicy

        private static class RetryPolicy
        {
            public static async Task<RetryResult<T>> ExecuteAsync<T>(
                Func<CancellationToken, Task<T>> action,
                int maxAttempts,
                Func<T, bool>? successPredicate = null,
                Func<Exception, bool>? shouldRetryOnException = null,
                Func<int, int>? delayMsFactory = null,
                Action<int, Exception?>? onRetry = null,
                CancellationToken token = default)
            {
                if (maxAttempts <= 0)
                    throw new ArgumentOutOfRangeException(nameof(maxAttempts));

                successPredicate ??= (_ => true);
                shouldRetryOnException ??= (_ => true);
                delayMsFactory ??= (attempt => Math.Min(300 * attempt, 1500));

                Exception? lastException = null;
                T? lastValue = default;

                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        lastValue = await action(token);
                        if (successPredicate(lastValue))
                            return RetryResult<T>.Success(lastValue, attempt);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        if (!shouldRetryOnException(ex) || attempt >= maxAttempts)
                            break;

                        onRetry?.Invoke(attempt, ex);
                        await Task.Delay(delayMsFactory(attempt), token);
                        continue;
                    }

                    if (attempt < maxAttempts)
                    {
                        onRetry?.Invoke(attempt, null);
                        await Task.Delay(delayMsFactory(attempt), token);
                    }
                }

                return RetryResult<T>.Fail(lastValue, lastException, maxAttempts);
            }

            public static async Task<RetryResult<bool>> ExecuteBoolAsync(
                Func<CancellationToken, Task<bool>> action,
                int maxAttempts,
                Func<Exception, bool>? shouldRetryOnException = null,
                Func<int, int>? delayMsFactory = null,
                Action<int, Exception?>? onRetry = null,
                CancellationToken token = default)
            {
                return await ExecuteAsync(
                    action,
                    maxAttempts,
                    successPredicate: v => v,
                    shouldRetryOnException: shouldRetryOnException,
                    delayMsFactory: delayMsFactory,
                    onRetry: onRetry,
                    token: token);
            }
        }

        #endregion

        #region Browser Boot / Events

        private async Task<IBrowser?> StartAndConnectBrowserAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var args = BuildChromiumArgs(ctx.Config, out var proxyServer);
            LogWriteLine($"args={string.Join(" ", args)}");

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
                   token,
                   maxAttempts: 3,
                   delayMs: 200,
                   requireUsableContext: true);
        }

        private List<string> BuildChromiumArgs(TaskConfig config, out string proxyServer)
        {
            var args = new List<string>
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
                "--hide-bad-flags",
                "--hide-crashed-bubble",
                $"--user-agent=\"{config.UserAgent}\"",
                $"--window-size=\"{config.Sw + 20},{config.Sh + 48}\"",
                "--window-position=0,0",
                $"--device-pixel-ratio={config.DeviceScale}",
                $"--screen-size=\"{config.Sw},{config.Sh}\"",
                $"--screen-avail-size=\"{config.Sw},{config.Sh}\"",
            };

            proxyServer = string.Empty;
            var isProxyMode = config.TaskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            if (isProxyMode)
            {
                proxyServer = config.TaskArgs.SelectToken("proxy_server")!.Value<string>();
                args.Add($"--proxy-server=\"{proxyServer}\"");
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

        private async Task AttachLifecycleEventsAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (ctx.Browser == null || ctx.Context == null)
                return;

            ctx.Browser.Disconnected += (_, _) =>
            {
                try
                {
                    LogWriteLine("浏览器已关闭或断开连接！");
                    if (!ctx.Config.LinkedCts.IsCancellationRequested)
                        ctx.Config.LinkedCts.Cancel();
                }
                catch { }
            };

            ctx.Context.Page += async (_, newPage) =>
            {
                try
                {
                    if (!ctx.Config.LinkedCts.IsCancellationRequested)
                        await InitPageAsync(ctx, newPage, ctx.Config.LinkedCts.Token);
                }
                catch (OperationCanceledException) { }
                catch { }
            };
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

            await CDPHelper.InitCDPSession(cdpSession, ctx.Config.MaxTouchPoints);
            await CDPHelper.SetDeviceMetricsOverride(cdpSession, ctx.Config.Sw, ctx.Config.Sh, ctx.Config.DeviceScale, true);
            await CDPHelper.SetBrowserPermission(cdpSession);

            page.Dialog += async (_, dialog) =>
            {
                try { await dialog.DismissAsync(); } catch { }
            };

            page.Crash += (_, _) =>
            {
                try
                {
                    LogWriteLine("Crash！");
                    if (!ctx.Config.LinkedCts.IsCancellationRequested)
                        ctx.Config.LinkedCts.Cancel();
                }
                catch { }
            };

            page.RequestFailed += (_, e) =>
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(e.Failure) &&
                        (e.Failure.Contains("ERR_INVALID_AUTH_CREDENTIALS") ||
                         (e.Failure.Contains("ERR_TUNNEL_CONNECTION_FAILED") && page.Url.Equals(e.Url))))
                    {
                        LogWriteLine($"page.RequestFailed:{e.Failure},{e.Url},{page.Url}");
                        if (!ctx.Config.LinkedCts.IsCancellationRequested)
                            ctx.Config.LinkedCts.Cancel();
                    }
                }
                catch { }
            };

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
            ctx.CdpSession = await ctx.CdpManager!.GetOrCreateSessionAsync(ctx.Page);
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
                var title = await ctx.Page!.TitleAsync();
                if (!title.StartsWith("网页搜索") && !title.StartsWith("搜索"))
                    return false;
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

            var adDotUrls = ctx.Page!.Locator("div[ad_dot_url^='http'],div.ad-wolong-container:has(a[data-url^='http'])");
            ctx.PageAdsCount = await adDotUrls.CountAsync();

            if (ctx.PageAdsCount <= 0)
            {
                LogWriteLine("没有广告标记,重试");
                return false;
            }

            if (string.IsNullOrWhiteSpace(q))
                return true;

            int ad1688 = 0;
            int adOther = 0;

            foreach (var i in Enumerable.Range(0, ctx.PageAdsCount))
            {
                token.ThrowIfCancellationRequested();

                var item = adDotUrls.Nth(i);
                var links = item.Locator("a[data-url]");
                if (await links.CountAsync() == 0)
                    continue;

                var dataUrl = await links.First.GetAttributeAsync("data-url");
                if (string.IsNullOrWhiteSpace(dataUrl))
                    continue;

                if (dataUrl.Contains(".1688."))
                    ad1688++;
                else
                    adOther++;
            }

            if (adOther > 0 && ad1688 == 0)
                QTPUploadAdWord("no1688", q);
            if (adOther > 0)
                QTPUploadAdWord("other", q);
            if (ad1688 > 0)
                QTPUploadAdWord("1688", q);

            if (ctx.Config.NoTrigger1688 && adOther == 0)
            {
                LogWriteLine("只有1688广告标记,重试");
                return false;
            }

            return true;
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
                return;

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
                return FlowControl.Continue;

            var candidates = await BuildSponsoredCandidatesAsync(ctx, sponsoreds, sponsoredCount, token);

            foreach (var sponsored in candidates)
            {
                token.ThrowIfCancellationRequested();

                await SwipeEmulator.SwipeToElementAsync(ctx.Page, ctx.CdpSession!, sponsored);
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
        /// 测试_触发广告
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        private async Task<FlowControl> TryTestExecuteJumpClickAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var sponsoreds = ctx.Page!.Locator("div[ad_dot_url^='http'],div.ad-wolong-container:has(a[data-url^='http'])");
            var sponsoredCount = await sponsoreds.CountAsync();
            if (sponsoredCount <= 0)
                return FlowControl.Continue;

            var candidates = await BuildSponsoredCandidatesAsync(ctx, sponsoreds, sponsoredCount, token);

            foreach (var sponsored in candidates)
            {
                token.ThrowIfCancellationRequested();

                await SwipeEmulator.SwipeToElementAsync(ctx.Page, ctx.CdpSession!, sponsored);
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
                    return FlowControl.Continue;
                }
            }
            return FlowControl.Continue;
        }



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
                if (dataUrl.Contains("1688.com")) score = 100;
                else if (dataUrl.Contains("taobao.com")) score = 90;
                else if (dataUrl.Contains("baidu.com")) score = 80;
                else if (dataUrl.Contains("pinduoduo.com")) score = 800;
                else if (dataUrl.Contains("qq.com")) score = 900;

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
                var uMob = filtered.Where(x => x.Url.Contains(".u-mob.", StringComparison.OrdinalIgnoreCase)).ToList();
                if (uMob.Count > 0)
                    return uMob[Random.Shared.Next(0, uMob.Count)].Locator;

                return filtered[Random.Shared.Next(0, filtered.Count)].Locator;
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

        private interface ILandingPageStrategy
        {
            bool CanHandle(string url);
            Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token);
        }

        private sealed class LandingPageStrategyDispatcher
        {
            private readonly List<ILandingPageStrategy> _strategies;

            public LandingPageStrategyDispatcher(IEnumerable<ILandingPageStrategy> strategies)
            {
                _strategies = strategies.ToList();
            }

            public async Task<FlowControl> DispatchAsync(WorkerRunContext ctx, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();

                var url = ctx.Page?.Url ?? string.Empty;
                foreach (var strategy in _strategies)
                {
                    if (strategy.CanHandle(url))
                        return await strategy.HandleAsync(ctx, token);
                }

                return FlowControl.Continue;
            }
        }

        private sealed class UMobLandingPageStrategy : ILandingPageStrategy
        {
            private readonly SMAdTask _owner;

            public UMobLandingPageStrategy(SMAdTask owner)
            {
                _owner = owner;
            }

            public bool CanHandle(string url) => url.StartsWith("https://site.u-mob.cn/", StringComparison.OrdinalIgnoreCase);

            public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();

                await _owner.TouchPageScroll(ctx.Page!, ctx.CdpSession!, CommonHelper.RandomRange(0, 3), 1);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                var tagItems = ctx.Page!.Locator(".tag-panel .tag-item");
                var count = await tagItems.CountAsync();
                if (count > 0)
                {
                    var clickCount = CommonHelper.RandomRange(1, count);
                    var indices = Enumerable.Range(0, count)
                        .OrderBy(_ => Guid.NewGuid())
                        .Take(clickCount)
                        .ToList();

                    foreach (var i in indices)
                    {
                        token.ThrowIfCancellationRequested();
                        await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, tagItems.Nth(i));
                        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                    }
                }

                if (!ctx.Config.NotTriggerDownload)
                {
                    var button = ctx.Page.Locator("button:has-text('下载')");
                    if (await button.CountAsync() > 0)
                    {
                        if (new[] { 3, 5, 7 }.Contains(CommonHelper.RandomRange(1, 10)))
                        {
                            await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, button.First);
                            await Task.Delay(CommonHelper.RandomRange(1500, 2500), token);
                        }
                    }
                }

                return FlowControl.Continue;
            }
        }

        private sealed class AiSiteLandingPageStrategy : ILandingPageStrategy
        {
            private readonly SMAdTask _owner;

            public AiSiteLandingPageStrategy(SMAdTask owner)
            {
                _owner = owner;
            }

            public bool CanHandle(string url) => url.StartsWith("https://aisite.wejianzhan.com", StringComparison.OrdinalIgnoreCase);

            public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                var openBtn = ctx.Page!.Locator(".animate-container svg image");
                if (await openBtn.CountAsync() > 0)
                {
                    int imageCount = await openBtn.CountAsync();
                    await _owner.ClickAndDetectNavigationAsync(ctx, openBtn.Nth(imageCount - 1), token);
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                }

                openBtn = ctx.Page.Locator(".welcome-popup-open-button");
                if (await openBtn.CountAsync() > 0)
                {
                    if (new[] { 1, 2, 3, 7, 8, 9 }.Contains(CommonHelper.RandomRange(1, 10)))
                    {
                        var clicked = await _owner.ClickAndDetectNavigationAsync(ctx, openBtn.First, token);
                        if (clicked.Navigated)
                            return FlowControl.Continue;
                    }
                }

                var closeBtn = ctx.Page.Locator(".close-btn,.close-area .close-icon");
                if (await closeBtn.CountAsync() > 0)
                {
                    await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, closeBtn.First);
                    await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                }

                await _owner.TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(0, 3), 1);

                var offerItems = ctx.Page.Locator(".ad-card-title,.ad-card-image,.ad-card-conv-btn");
                if (await offerItems.CountAsync() > 0)
                {
                    int count = await offerItems.CountAsync();
                    var offer = offerItems.Nth(CommonHelper.RandomRange(0, count));
                    await SwipeEmulator.SwipeToElementAsync(ctx.Page, ctx.CdpSession!, offer);
                    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                    await _owner.ClickAndDetectNavigationAsync(ctx, offer, token);
                    return FlowControl.Continue;
                }

                var jsClick = await _owner.TryRandomViewportClickableClickAsync(ctx, token);
                if (!jsClick.Navigated)
                    await _owner.TryRandomLinkClickAsync(ctx, "a,img", token);

                return FlowControl.Continue;
            }
        }

        private sealed class AiStudyLandingPageStrategy : ILandingPageStrategy
        {
            private readonly SMAdTask _owner;

            public AiStudyLandingPageStrategy(SMAdTask owner)
            {
                _owner = owner;
            }

            public bool CanHandle(string url) => url.StartsWith("https://aistudy.baidu.com/", StringComparison.OrdinalIgnoreCase);

            public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                var recommend = ctx.Page!.Locator(".recommend-adlist .waterfall-column");
                var count = await recommend.CountAsync();

                if (count == 0)
                {
                    var ok1 = await CDPHelper.FindItemAndClickAsync(ctx.Page, ctx.CdpSession!, ".search-page-container input");
                    var ok2 = ok1 && await CDPHelper.FindItemAndClickAsync(ctx.Page, ctx.CdpSession!, ".search-page-container .search");

                    if (ok2)
                    {
                        var retry = await RetryPolicy.ExecuteBoolAsync(
                            async ct =>
                            {
                                ct.ThrowIfCancellationRequested();

                                recommend = ctx.Page.Locator(".recommend-adlist .waterfall-column");
                                count = await recommend.CountAsync();
                                if (count > 0)
                                    return true;

                                if (await CDPHelper.FindItemAndClickAsync(ctx.Page, ctx.CdpSession!, ".no-result-btn"))
                                    await Task.Delay(1500, ct);

                                return false;
                            },
                            maxAttempts: 5,
                            token: token);

                        if (retry.IsSuccess)
                        {
                            recommend = ctx.Page.Locator(".recommend-adlist .waterfall-column");
                            count = await recommend.CountAsync();
                        }
                    }
                }

                if (count > 0)
                {
                    var item = recommend.Nth(CommonHelper.RandomRange(0, count));
                    await SwipeEmulator.SwipeToElementAsync(ctx.Page, ctx.CdpSession!, item);
                    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                    await _owner.ClickAndDetectNavigationAsync(ctx, item, token);
                }

                return FlowControl.Continue;
            }
        }

        private sealed class GenericLandingPageStrategy : ILandingPageStrategy
        {
            private readonly SMAdTask _owner;

            public GenericLandingPageStrategy(SMAdTask owner)
            {
                _owner = owner;
            }

            public bool CanHandle(string url) => true;

            public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                if (_owner._appSettings.p4psearch && _owner._appSettings.p4psearchRate > 0 && ctx.Page!.Url.Contains("m.1688.com"))
                {
                    await _owner.TryHandle1688RecommendWordsAsync(ctx, token);
                }

                var offerItems = await _owner.ResolveOfferItemsAsync(ctx, token);

                if (!ctx.Page!.Url.StartsWith("https://plogin.m.jd.com/"))
                {

                    if (offerItems != null && await offerItems.CountAsync() > 0)
                    {
                        int count = await offerItems.CountAsync();
                        var item = offerItems.Nth(CommonHelper.RandomRange(0, count));
                        await SwipeEmulator.SwipeToElementAsync(ctx.Page!, ctx.CdpSession!, item);
                        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                        var click = await _owner.ClickAndDetectNavigationAsync(ctx, item, token);
                        if (click.Navigated)
                        {
                            _owner.ProcessingPageElementTask(ctx.Page!, ctx.CdpSession!);
                            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                        }
                    }
                    else
                    {
                        await _owner.TryRandomLinkClickAsync(ctx, "a:visible", token);
                    }
                }



                return FlowControl.Continue;
            }
        }

        #endregion

        #region Generic Landing Helpers

        private async Task TryHandle1688RecommendWordsAsync(WorkerRunContext ctx, CancellationToken token)
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
                await TouchPageScroll(ctx.Page!, ctx.CdpSession!, CommonHelper.RandomRange(5, 8), 1, predexp: async _ =>
                {
                    var panel = ctx.Page.Locator("div[class*='ab-recommend-words']");
                    if (await panel.CountAsync() > 0)
                    {
                        await panel.First.ScrollIntoViewIfNeededAsync();
                        return true;
                    }
                    return false;
                });

                token.ThrowIfCancellationRequested();

                await TouchPageScrollUpAsync(ctx.Page!, ctx.CdpSession!);
                await Task.Delay(CommonHelper.RandomRange(100, 200), token);

                var recommends = await ctx.Page!.QuerySelectorAllAsync("div[class*='ab-recommend-words'] a.word");
                if (recommends.Count == 0)
                    return;

                _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_p4psearch_click");

                var recommend = recommends[CommonHelper.RandomRange(0, recommends.Count)];
                await recommend.ScrollIntoViewIfNeededAsync();
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, recommend, timeout: 2000);
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch { }
        }

        private async Task<ILocator?> ResolveOfferItemsAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var url = ctx.Page!.Url;
            ILocator? offerItems = null;

            if (url.Contains("m.p4psearch.1688.com"))
            {
                if (_appSettings.Rfq1688 && _appSettings.Rfq1688Rate > 0)
                    _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_rfq1688");

                await TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(0, 3), 1);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("//div[starts-with(@class,'offer-item')]");
            }
            else if (url.Contains("m.1688.com"))
            {
                await TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(0, 3), 1);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("//div[starts-with(@class,'offer-item')]");
            }
            else if (url.Contains("b2b.baidu.com"))
            {
                await TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(0, 3), 1);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                offerItems = ctx.Page.Locator(".img-content,.list-title,.content-without-title");
                if (await offerItems.CountAsync() == 0)
                    offerItems = ctx.Page.Locator("a.product-item-link");
            }
            else if (url.Contains("aden.baidu.com") || url.Contains("ada.baidu.com"))
            {
                await TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(0, 3), 1);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("//div[contains(@class,'ec_content')]");
            }
            else if (url.Contains("uland.taobao.com"))
            {
                await TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(0, 3), 1);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                offerItems = ctx.Page.Locator("//a[starts-with(@class,'link')]");
            }
            else if (url.StartsWith("https://pro.m.jd.com/mall/active"))
            {
                await HandleJdActivePageAsync(ctx, token);


                if (!ctx.Page.Url.StartsWith("https://plogin.m.jd.com/"))
                {
                    await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                    offerItems = ctx.Page.Locator(".masonryCard,.commodity-list .commodity-desc,.list-con .product,a.goods,.feed-product-container");
                    if (await offerItems.CountAsync() == 0)
                        offerItems = ctx.Page.Locator(".feed-product-container");
                    if (await offerItems.CountAsync() == 0)
                        offerItems = ctx.Page.Locator(".feed-product-container,a.goods,.list-con .product");
                    if (await offerItems.CountAsync() == 0)
                        offerItems = ctx.Page.Locator("img");
                }
            }
            else if (url.Contains("m.jd.com"))
            {
                await TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(0, 3), 1);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                offerItems = ctx.Page.Locator(".commodity-list .commodity-desc,.list-con .product,a.goods,.feed-product-container");
                if (await offerItems.CountAsync() == 0)
                    offerItems = ctx.Page.Locator(".feed-product-container");
                if (await offerItems.CountAsync() == 0)
                    offerItems = ctx.Page.Locator(".feed-product-container,a.goods,.list-con .product");
                if (await offerItems.CountAsync() == 0)
                    offerItems = ctx.Page.Locator("img");
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
            if (medical)
            {
                await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                await SynthesizeScrollGestureAsync(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(1, 3), 1);

                //await TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(1, 3), 1);
                //|图文.*起|电话.*起
                var locator_list = ctx.Page.Locator("text=/剩.*个名额|图文.*起|电话.*起/").Filter(new() { Visible = true });
                var locator_count = await locator_list.CountAsync();
                if (locator_count > 0)
                {
                    foreach (var target_index in Enumerable.Range(0, locator_count).OrderBy(o => Guid.NewGuid()))
                    {
                        var target = locator_list.Nth(target_index);

                        //await SwipeEmulator.SwipeToElementAsync(ctx.Page, ctx.CdpSession!, target);
                        await target.ScrollIntoViewIfNeededAsync();
                        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                        var target_text = await target.InnerTextAsync();
                        if (!string.IsNullOrWhiteSpace(target_text))
                            LogWriteLine(target_text);
                        result = await ClickElementHandleAndDetectNavigationAsync(ctx, target, token);
                        if (result.Navigated)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    locator_list = ctx.Page.Locator("img[data-type='image']").Locator("..").Filter(new()
                    {
                        Has = ctx.Page.Locator("div[data-type='price']")
                    });

                    locator_count = await locator_list.CountAsync();
                    if (locator_count > 0)
                    {
                        foreach (var target_index in Enumerable.Range(0, locator_count).OrderBy(o => Guid.NewGuid()))
                        {
                            var target = locator_list.Nth(target_index);

                            //await SwipeEmulator.SwipeToElementAsync(ctx.Page, ctx.CdpSession!, target);
                            await target.ScrollIntoViewIfNeededAsync();
                            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                            var target_text = await target.InnerTextAsync();
                            if (!string.IsNullOrWhiteSpace(target_text))
                                LogWriteLine(target_text);
                            result = await ClickElementHandleAndDetectNavigationAsync(ctx, target, token);
                            if (result.Navigated)
                            {
                                break;
                            }
                        }
                    }
                    else
                    {

                    }

                    await TouchPageScroll(ctx.Page, ctx.CdpSession!, CommonHelper.RandomRange(0, 2), 1);
                    result = await TryRandomViewportClickableClickAsync(ctx, token);
                }
            }
            else
            {
              
                await GestureScrollUp(ctx.Page, ctx.CdpSession!);
                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                int count = await CenterClickableFinder.MarkCandidatesAsync(ctx.Page);

                var loc = CenterClickableFinder.GetMarkedLocator(ctx.Page);
                int markedCount = await loc.CountAsync();



                bool ok = await CenterClickableFinder.ClickBestByTouchAsync(ctx.Page, ctx.CdpSession);
                //var candidates = await CenterClickableFinder.GetCandidatesAsync(ctx.Page);

                //foreach (var item in candidates)
                //{
                //    Console.WriteLine($"{item.TagName} | {item.SelectorHint} | ({item.CenterX}, {item.CenterY}) | score={item.Score}");
                //}


                //var locator_list = ctx.Page.Locator("img[data-type='image']").Locator("..").Filter(new()
                //{
                //    Has = ctx.Page.Locator("div[data-type='price']")
                //});

                //var locator_count = await locator_list.CountAsync();
                //if (locator_count > 0)
                //{
                //    foreach (var target_index in Enumerable.Range(0, locator_count).OrderBy(o => Guid.NewGuid()))
                //    {
                //        var target = locator_list.Nth(target_index);

                //        //await SwipeEmulator.SwipeToElementAsync(ctx.Page, ctx.CdpSession!, target);
                //        await target.ScrollIntoViewIfNeededAsync();
                //        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                //        var target_text = await target.InnerTextAsync();
                //        if (!string.IsNullOrWhiteSpace(target_text))
                //            LogWriteLine(target_text);
                //        result = await ClickElementHandleAndDetectNavigationAsync(ctx, target, token);
                //        if (result.Navigated)
                //        {
                //            break;
                //        }
                //    }
                //}
                //else
                //{

                //}
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

            await TryHandleRfq1688Async(ctx, token);
            await TryHandleQianhuFormAsync(ctx, token);

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
                await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                return FlowControl.EndTask;
            }

            LogWriteLine("延时停留");
            LogWriteLine("准备滑动");

            DateTime start = DateTime.Now;
            int gestureOrientation = 1;

            while (true)
            {
                token.ThrowIfCancellationRequested();

                try
                {
                    LogWriteLine("滑动操作");
                    await TouchPageScroll(ctx.Page, ctx.CdpSession!, 1, gestureOrientation);

                    token.ThrowIfCancellationRequested();

                    if (await IsPageEnd(ctx.Page))
                        gestureOrientation = -1;
                    else if (await IsPageTop(ctx.Page))
                        gestureOrientation = 1;

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

        private async Task TryHandleRfq1688Async(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            if (!(_appSettings.Rfq1688 && _appSettings.Rfq1688Rate > 0 && ctx.Page!.Url.Contains("m.p4psearch.1688.com")))
                return;

            try
            {
                var metrics = _aggregator.GetLocalMetrics(ctx.Config.TaskId, "dsp_rfq1688", "dsp_rfq1688_click");
                if (metrics["dsp_rfq1688"] > 0)
                    LogWriteLine($"1688询价比率:{(metrics["dsp_rfq1688_click"] / (double)metrics["dsp_rfq1688"] * 100):N2}%");

                bool canClick = _appSettings.Rfq1688Rate == 100
                    || metrics["dsp_rfq1688_click"] == 0
                    || ((metrics["dsp_rfq1688_click"] / (double)metrics["dsp_rfq1688"]) * 100 < _appSettings.Rfq1688Rate);

                if (!canClick)
                    return;

                await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);

                var el = ctx.Page.Locator("#od_xst_phone_input_val_new,#new_od_xst_phone_input_val_new");
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
                    return;

                _aggregator.AddLocalMetric(ctx.Config.TaskId, "dsp_rfq1688_click");

                var phone = await _adeHelper.GetPhoneNumberAsync();
                if (string.IsNullOrWhiteSpace(phone))
                    return;

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
                    var texts = new[]
                    {
                        "有没有现货","价格还有空间吗","什么时间发货","有活动吗","工厂在哪里","实物图是否一致",
                        "能否提供质检","可以寄样品给我吗","批发价是多少","可以开发票吧","这款支持一件代发吗","包邮吗"
                    };

                    el = ctx.Page.Locator("textarea#new_od_xst_msg_input_val_new_message,textarea#od_xst_msg_input_val_new_message");
                    if (await el.CountAsync() > 0)
                    {
                        await el.First.FillAsync("");
                        await Task.Delay(CommonHelper.RandomRange(50, 100), token);
                        await el.First.PressSequentiallyAsync(texts[CommonHelper.RandomRange(0, texts.Length)]);
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

                var btnGoShop = ctx.Page.Locator(".new_offer_card-title div:text-is('进店看看')");
                if (await btnGoShop.CountAsync() > 0)
                {
                    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                    await btnGoShop.First.ScrollIntoViewIfNeededAsync();
                    await Task.Delay(CommonHelper.RandomRange(100, 200), token);

                    await ClickAndDetectNavigationAsync(ctx, btnGoShop.First, token);
                    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                    var leafDivs = ctx.Page
                        .FrameLocator("iframe[src*='getPageModuleResourceRax1']")
                        .Locator("div#moreOffer + div div:not(:has(*))");

                    int count = await leafDivs.CountAsync();
                    await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);

                    for (int i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested();

                        var item = leafDivs.Nth(i);
                        if (!await item.IsVisibleAsync())
                            continue;

                        await ClearPageCloseBtn(ctx.Page, ctx.CdpSession!);
                        var click = await ClickAndDetectNavigationAsync(ctx, item, token);
                        if (click.Navigated)
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

        #endregion

        #region Test Branch

        private async Task RunTestBranchAsync(WorkerRunContext ctx, EntryPreparationResult entry, CancellationToken token)
        {

            //token.ThrowIfCancellationRequested();

            //var adsOk = await DetectAndUploadAdWordsAsync(ctx, entry.QueryWord, token);
            //if (!adsOk)
            //    return;

            //await DecideJumpClickAsync(ctx, token);

            //if (ctx.JumpClick)
            //{
            //    var clickFlow = await TryTestExecuteJumpClickAsync(ctx, token);
            //    if (clickFlow == FlowControl.EndTask)
            //        return;
            //}


            token.ThrowIfCancellationRequested();

            var offerItems = await ResolveOfferItemsAsync(ctx, token);

            await Task.Delay(TimeSpan.FromSeconds(150), token);
        }

        #endregion

        #region Click Helpers

        private async Task<ClickResult> ClickAndDetectNavigationAsync(WorkerRunContext ctx, ILocator locator, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                ctx.PagesCount = ctx.Context!.Pages.Count;
                ctx.CurrentPageUrl = ctx.Page!.Url;

                await CDPHelper.MouseClickAsync(ctx.Page, ctx.CdpSession!, locator);
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

        private async Task<ClickResult> TryRandomViewportClickableClickAsync(WorkerRunContext ctx, CancellationToken token)
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
                    var result = await ClickElementHandleAndDetectNavigationAsync(ctx, target, token);
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

        private async Task<ClickResult> ClickElementHandleAndDetectNavigationAsync(WorkerRunContext ctx, IElementHandle handle, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                ctx.PagesCount = ctx.Context!.Pages.Count;
                ctx.CurrentPageUrl = ctx.Page!.Url;

                await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, handle);
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

        private async Task<ClickResult> ClickElementHandleAndDetectNavigationAsync(WorkerRunContext ctx, ILocator handle, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                ctx.PagesCount = ctx.Context!.Pages.Count;
                ctx.CurrentPageUrl = ctx.Page!.Url;

                await CDPHelper.MouseClickAsync(ctx.Page!, ctx.CdpSession!, handle);
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

        private async Task<ClickResult> TryRandomLinkClickAsync(WorkerRunContext ctx, string selector, CancellationToken token)
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

        private async Task<List<IElementHandle>> GetCurrentViewportClickableElementsAsync(IPage page, CancellationToken token)
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

        #region Internal Types

        private sealed class TaskConfig
        {
            public string UniqueId { get; set; } = "";
            public JObject TaskArgs { get; set; } = default!;
            public CancellationTokenSource LinkedCts { get; set; } = default!;

            public int TaskId { get; set; }
            public string TaskUrl { get; set; } = "";
            public int SleepMs { get; set; }
            public bool IsLocalAdWord { get; set; }
            public int PageLoadingTimeoutMs { get; set; }
            public int PageLoadedDelayMs { get; set; }

            public int HomepageTrigger { get; set; }
            public bool PriorityNon1688 { get; set; }

            public string UserAgent { get; set; } = "";
            public int Os { get; set; }
            public int? DevSw { get; set; }
            public float DeviceScale { get; set; }
            public int Sw { get; set; }
            public int Sh { get; set; }

            public string WordName { get; set; } = "";
            public bool NoTrigger1688 { get; set; }
            public bool CleaningWords { get; set; }
            public bool NotTriggerDownload { get; set; }
            public bool PvsTriggerOne { get; set; }
            public int CurrentUV { get; set; }

            public string KernelVersion { get; set; } = "132";
            public int MaxTouchPoints { get; set; }
            public int ProcessIndex { get; set; }

            public bool IsTest { get; set; }
            public int TotalPV { get; set; }

            public string CacheDir { get; set; } = "";
            public string UserDataDir { get; set; } = "";
        }

        private sealed class WorkerRunContext
        {
            public WorkerRunContext(TaskConfig config)
            {
                Config = config;
                StartTime = DateTime.Now;
            }

            public TaskConfig Config { get; }
            public DateTime StartTime { get; }

            public IPlaywright? Playwright { get; set; }
            public IBrowser? Browser { get; set; }
            public IBrowserContext? Context { get; set; }
            public IPage? Page { get; set; }
            public ICDPSession? CdpSession { get; set; }
            public CDPSessionManager? CdpManager { get; set; }

            public LandingPageStrategyDispatcher? LandingDispatcher { get; set; }

            public int DebugPort { get; set; }

            public int TriggerDownloadSign;
            public int PageAdsCount { get; set; }
            public bool PageTriggerClick { get; set; }
            public bool JumpClick { get; set; }

            public int PagesCount { get; set; }
            public string CurrentPageUrl { get; set; } = "";
            public int PvIndex { get; set; }

            public void ResetPerPvState()
            {

                TriggerDownloadSign = 0;
                PageTriggerClick = false;
                JumpClick = false;
                PagesCount = 0;
                CurrentPageUrl = string.Empty;
            }
        }

        private sealed class EntryPreparationResult
        {
            public bool Success { get; set; }
            public bool EndTask { get; set; }
            public bool IsHomepageTrigger { get; set; }
            public string? QueryWord { get; set; }
            public string? FirstPageUrl { get; set; }
        }

        private sealed class ClickResult
        {
            public bool Attempted { get; private set; }
            public bool Navigated { get; private set; }
            public bool OpenedNewPage { get; private set; }

            public static ClickResult Fail() => new() { Attempted = false, Navigated = false, OpenedNewPage = false };
            public static ClickResult NoNavigation() => new() { Attempted = true, Navigated = false, OpenedNewPage = false };
            public static ClickResult SuccessSamePage() => new() { Attempted = true, Navigated = true, OpenedNewPage = false };
            public static ClickResult SuccessNewPage() => new() { Attempted = true, Navigated = true, OpenedNewPage = true };
        }

        private sealed class RetryResult<T>
        {
            public bool IsSuccess { get; private set; }
            public T? Value { get; private set; }
            public Exception? Exception { get; private set; }
            public int Attempts { get; private set; }

            public static RetryResult<T> Success(T? value, int attempts) =>
                new RetryResult<T> { IsSuccess = true, Value = value, Attempts = attempts };

            public static RetryResult<T> Fail(T? value, Exception? exception, int attempts) =>
                new RetryResult<T> { IsSuccess = false, Value = value, Exception = exception, Attempts = attempts };
        }

        private enum FlowControl
        {
            Continue = 0,
            NextPv = 1,
            EndTask = 2
        }

        #endregion


    }
}
