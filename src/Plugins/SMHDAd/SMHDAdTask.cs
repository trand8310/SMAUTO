using Microsoft.Playwright;
using QTP.Common;
using QTP.Common.Models;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using QTP.Common.Infrastructure;
using System.Web;
using System.Text;




namespace QTP.Plugins
{
    public sealed class SMHDAdTask : QTPServiceBase
    {

        public static QTPPlugin GetInfo()
        {
            return new QTPPlugin()
            {
                ClassName = "QTP.Plugins.SMHDAdTask",
                Name = "SMHDAd",
                FileName = "SMHDAd.dll",
            };
        }
        public override string Title => "神马搜索";

        public SMHDAdTask(IWritableOptions<AppSettings> appSettings) : base(appSettings)
        {

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

            double randomValue = new Random(Guid.NewGuid().GetHashCode()).NextDouble();
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

        public async Task<bool> ScrollElementIntoViewAsync(ILocator locator)
        {
            var result = false;
            try
            {
                await locator.WaitForAsync(new LocatorWaitForOptions() { Timeout = 2000 });
                await locator.ScrollIntoViewIfNeededAsync(new LocatorScrollIntoViewIfNeededOptions() { Timeout = 2000 });
                result = true; ;
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine(ex.Message);
                await locator.EvaluateAsync(@"(element) => {
                     element.scrollIntoView({
                         behavior: 'smooth',
                         block: '" + new string[] { "center", "nearest", "start", "end", "center", "nearest", "center", "center", "nearest", "start" }[new Random().Next(0, 10)] + @"',
                         inline: 'nearest'
                     });}");
                result = true;
            }
            catch (Exception)
            {

            }
            return result;
        }

        private static async Task<IElementHandle?> FindClosestParentWithClassAsync(ILocator childLocator, string className)
        {
            var currentNode = await childLocator.ElementHandleAsync();
            while (currentNode != null)
            {
                // 检查父节点是否具有目标类
                var hasTargetClass = await currentNode.EvaluateAsync<bool>($"node => node.classList.contains('{className}')");
                if (hasTargetClass)
                {
                    return currentNode; // 返回满足条件的父节点
                }
                var parentHandle = await currentNode.EvaluateHandleAsync("node => node.parentElement");
                currentNode = parentHandle?.AsElement();
            }
            return null;
        }

        private async Task ScrollUpGesture(IPage page, ICDPSession cdpSession, int scrollCount, Func<IPage, Task<bool>> func = null)
        {
            LogWriteLine($"随机向上滑动:{scrollCount}次");
            await CDPHelper.ClearDeviceOrientationOverrideAsync(cdpSession);
            for (int i = 0; i < scrollCount; i++)
            {
                #region 滑动操作
                await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                int startX = (int)(page.ViewportSize.Width * (new Random().Next(35, 55) * 0.01));
                int startY = (int)(page.ViewportSize.Height * (new Random().Next(65, 90) * 0.01));
                int endX = (int)(page.ViewportSize.Width * (new Random().Next(55, 75) * 0.01));
                int endY = -new Random().Next(180, 300);
                LogWriteLine($"开始滑动:[{startX},{startY}],[{endX},{endY}]");
                await cdpSession.SendAsync("Input.synthesizeScrollGesture", new Dictionary<string, object>()
             {
                 { "x",startX},
                 { "y",startY},
                 { "xDistance",endX},
                 { "yDistance",endY},
                 { "yOverscroll",new Random().Next(50,300)},
                 { "gestureSourceType","default"},
             });
                await Task.Delay(new Random().Next(500, 2000));
                if (func != null && await func(page))
                {
                    break;
                }
                #endregion
            }
        }

        private async Task ScrollDownGesture(IPage page, ICDPSession cdpSession, int scrollCount)
        {
            LogWriteLine($"随机向下滑动:{scrollCount}次");
            for (int i = 0; i < scrollCount; i++)
            {
                #region 滑动操作
                await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                int startX = (int)(page.ViewportSize.Width * (new Random().Next(45, 50) * 0.01));
                int startY = (int)(page.ViewportSize.Height * (new Random().Next(15, 30) * 0.01));
                int endX = (int)(page.ViewportSize.Width * (new Random().Next(50, 60) * 0.01));
                int endY = startY + new Random().Next(180, 300);
                LogWriteLine($"开始滑动:[{startX},{startY}],[{endX},{endY}]");
                await cdpSession.SendAsync("Input.synthesizeScrollGesture", new Dictionary<string, object>()
               {
                   { "x",startX},
                   { "y",startY},
                   { "xDistance",endX},
                   { "yDistance",endY},
                   { "yOverscroll",-new Random().Next(50,200)},
                   { "gestureSourceType","default"},
               });
                await Task.Delay(new Random().Next(200, 2000));

                #endregion
            }
        }

        private async Task SynthesizeScrollGesture(IPage page, ICDPSession cdpSession, int gestureOrientation = 1)
        {
            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
            if (page.ViewportSize != null)
            {
                int sw = page.ViewportSize.Width;
                int sh = page.ViewportSize.Height;

                if (gestureOrientation == -1)
                {
                    int startX = (int)(sw * (new Random().Next(45, 50) * 0.01));
                    int startY = (int)(sh * (new Random().Next(15, 30) * 0.01));
                    int endX = (int)(sw * (new Random().Next(50, 60) * 0.01));
                    int endY = startY + new Random().Next(180, 300);
                    LogWriteLine($"向下滑动:[{startX},{startY}],[{endX},{endY}]");
                    await cdpSession.SendAsync("Input.synthesizeScrollGesture",
                        new Dictionary<string, object>()
                       {
                       { "x",startX},
                       { "y",startY},
                       { "xDistance",endX},
                       { "yDistance",endY},
                       { "yOverscroll",-new Random().Next(50,200)},
                       { "gestureSourceType","default"},
                       });
                }
                else
                {

                    int startX = (int)(sw * (new Random().Next(35, 55) * 0.01));
                    int startY = (int)(sh * (new Random().Next(65, 90) * 0.01));
                    int endX = (int)(sw * (new Random().Next(55, 75) * 0.01));
                    int endY = -new Random().Next(180, 300);
                    LogWriteLine($"向上滑动:[{startX},{startY}],[{endX},{endY}]");
                    await cdpSession.SendAsync("Input.synthesizeScrollGesture",
                        new Dictionary<string, object>()
                     {
                     { "x",startX},
                     { "y",startY},
                     { "xDistance",endX},
                     { "yDistance",endY},
                     { "yOverscroll",new Random().Next(50,300)},
                     { "gestureSourceType","default"},
                     });
                }

            }
        }
        private async Task TouchScrollUp(IPage page, ICDPSession cdpSession)
        {

            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
            try
            {
                int startX = (int)(page.ViewportSize.Width * (new Random().Next(35, 55) * 0.01));
                int startY = (int)(page.ViewportSize.Height * (new Random().Next(65, 90) * 0.01));
                int endX = startX + new Random().Next(-10, 50);
                int endY = (int)(page.ViewportSize.Height * (new Random().Next(10, 25) * 0.01));
                int steps = new Random().Next(15, 45);
                LogWriteLine($"TouchScrollUp:[{startX},{startY}],[{endX},{endY}]");
                await cdpSession.SendAsync("Input.dispatchTouchEvent",
                    new Dictionary<string, object>() {
                        { "type","touchStart"},
                        { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                });

                for (int j = 1; j <= steps; j++)
                {
                    var currentX = startX + new Random().Next(-1, 2);
                    var currentY = startY - ((startY - endY) / steps) * j;
                    await cdpSession.SendAsync("Input.dispatchTouchEvent",
                        new Dictionary<string, object>() {
                            { "type","touchMove"},
                            { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                    });
                    SpinWait.SpinUntil(() => false, new Random().Next(5, 15));
                }
                await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                        { "type","touchEnd"},
                        { "touchPoints",new object[] {}},
                    });

            }
            finally
            {
                await CDPHelper.SetEmitTouchEventsForMouse(cdpSession, true);
            }


        }
        private async Task TouchScrollDown(IPage page, ICDPSession cdpSession)
        {

            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
            try
            {
                // 模拟向下滑动
                int startX = (int)(page.ViewportSize.Width * (new Random().Next(35, 55) * 0.01));// 滑动起始点 X 坐标
                int startY = (int)(page.ViewportSize.Height * (new Random().Next(10, 20) * 0.01)); // 滑动起始点 Y 坐标
                int endX = startX + new Random().Next(-10, 50);
                int endY = (int)(page.ViewportSize.Height * (new Random().Next(60, 90) * 0.01));// 滑动终点 Y 坐标
                int steps = new Random().Next(15, 40);   // 滑动的步数（影响平滑度）
                LogWriteLine($"TouchScrollDown:[{startX},{startY}],[{endX},{endY}]");
                await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                    { "type","touchStart"},
                    { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                });
                for (int j = 1; j <= steps; j++)
                {
                    var currentX = startX + new Random().Next(-1, 2);
                    var currentY = startY + ((endY - startY) / steps) * j;
                    await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                        { "type","touchMove"},
                        { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                    });
                    SpinWait.SpinUntil(() => false, new Random().Next(5, 15));
                }
                await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                        { "type","touchEnd"},
                        { "touchPoints",new object[] {}},
                });
            }
            finally
            {
                await CDPHelper.SetEmitTouchEventsForMouse(cdpSession, true);
            }

        }
        /// <summary>
        /// 触摸滑动
        /// </summary>
        /// <param name="page"></param>
        /// <param name="cdpSession"></param>
        /// <param name="scrollCount"></param>
        /// <param name="direction">1:向上滑动,2:向下滑动</param>
        /// <param name="predexp"></param>
        /// <returns></returns>
        private async Task TouchPageScroll(IPage page, ICDPSession cdpSession, int scrollCount, int direction, Func<IPage, Task<bool>>? predexp = null)
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
                    await TouchScrollDown(page, cdpSession);
                    await Task.Delay(new Random().Next(800, 2345));
                    if (predexp != null && await predexp(page))
                    {
                        break;
                    }
                }
                else
                {
                    await TouchScrollUp(page, cdpSession);
                    await Task.Delay(new Random().Next(800, 2345));
                    if (predexp != null && await predexp(page))
                    {
                        break;
                    }
                }
            }
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="page"></param>
        /// <param name="cdpSession"></param>
        /// <param name="locator"></param>
        /// <param name="speed">0:普通,1:快</param>
        /// <returns></returns>
        private async Task TouchEelmentToViewportAsync(IPage page, ICDPSession cdpSession, ILocator locator, int speed = 0)
        {

            try
            {
                int viewport_width = page.ViewportSize.Width;
                int viewport_height = page.ViewportSize.Height;
                var top_boundary = viewport_height * 0.25;
                var bottom_boundary = viewport_height * 0.75;
                var boundingBox = await locator.BoundingBoxAsync();
                if (boundingBox.Y < 0)
                {
                    ///模拟向下滑动
                    do
                    {
                        int startX = (int)(viewport_width * (new Random().Next(35, 55) * 0.01));// 滑动起始点 X 坐标
                        int startY = (int)(viewport_height * (new Random().Next(15, 35) * 0.01)); // 滑动起始点 Y 坐标
                        int endX = startX + new Random().Next(-10, 50);
                        int endY = (int)(page.ViewportSize.Height * (new Random().Next(60, 85) * 0.01));// 滑动终点 Y 坐标
                        LogWriteLine($"EelmentToViewport::TouchScrollDown:[{startX},{startY}],[{endX},{endY}]");
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchStart"},
                            { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                        });
                        int steps = speed == 0 ? new Random().Next(15, 40) : new Random().Next(10, 30);//// 滑动的步数（影响平滑度）
                        for (int j = 1; j <= steps; j++)
                        {
                            var currentX = startX + new Random().Next(-1, 2);
                            var currentY = startY + ((endY - startY) / steps) * j;
                            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                            await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                                { "type","touchMove"},
                                { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                            });
                            SpinWait.SpinUntil(() => false, new Random().Next(10, 25));
                        }
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchEnd"},
                            { "touchPoints",new object[] {}},
                          });
                        if (speed == 0)
                            SpinWait.SpinUntil(() => false, new Random().Next(800, 2000));
                        else
                            SpinWait.SpinUntil(() => false, new Random().Next(500, 1000));


                        boundingBox = await locator.BoundingBoxAsync();
                    } while (boundingBox.Y < 0);


                    if (boundingBox.Y < top_boundary && !await IsPageTop(page))
                    {
                        int startX = (int)(viewport_width * (new Random().Next(35, 55) * 0.01));// 滑动起始点 X 坐标
                        int startY = (int)(viewport_height * (new Random().Next(45, 50) * 0.01)); // 滑动起始点 Y 坐标
                        int endX = startX + new Random().Next(-10, 50);
                        int endY = (int)(page.ViewportSize.Height * (new Random().Next(50, 55) * 0.01));// 滑动终点 Y 坐标
                        LogWriteLine($"TouchScrollDown:[{startX},{startY}],[{endX},{endY}]");
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchStart"},
                            { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                        });
                        int steps = new Random().Next(5, 10);
                        for (int j = 1; j <= steps; j++)
                        {
                            var currentX = startX + new Random().Next(-1, 2);
                            var currentY = startY + ((endY - startY) / steps) * j;
                            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                            await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                                { "type","touchMove"},
                                { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                            });
                            SpinWait.SpinUntil(() => false, new Random().Next(10, 25));
                        }
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchEnd"},
                            { "touchPoints",new object[] {}},
                         });
                    }

                }
                else if (boundingBox.Y > viewport_height)
                {
                    do
                    {
                        // 模拟向上滑动
                        int startX = (int)(viewport_width * (new Random().Next(40, 55) * 0.01));
                        int startY = (int)(viewport_height * (new Random().Next(65, 85) * 0.01));
                        int endX = startX + new Random().Next(-10, 50);
                        int endY = (int)(viewport_height * (new Random().Next(15, 35) * 0.01));
                        LogWriteLine($"EelmentToViewport::TouchScrollUp:[{startX},{startY}],[{endX},{endY}]");
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchStart"},
                            { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                        });
                        int steps = speed == 0 ? new Random().Next(15, 40) : new Random().Next(10, 30);
                        for (int j = 1; j <= steps; j++)
                        {
                            var currentX = startX + new Random().Next(-1, 2);
                            var currentY = startY - ((startY - endY) / steps) * j;
                            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                            await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                                { "type","touchMove"},
                                { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                            });

                            SpinWait.SpinUntil(() => false, new Random().Next(10, 25));
                        }
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchEnd"},
                            { "touchPoints",new object[] {}},
                        });
                        if (speed == 0)
                            SpinWait.SpinUntil(() => false, new Random().Next(800, 2000));
                        else
                            SpinWait.SpinUntil(() => false, new Random().Next(500, 1000));

                        boundingBox = await locator.BoundingBoxAsync();
                    } while (boundingBox.Y > viewport_height);

                    if (boundingBox.Y > bottom_boundary && !await IsPageEnd(page))
                    {
                        int startX = (int)(viewport_width * (new Random().Next(40, 55) * 0.01));
                        int startY = (int)(viewport_height * (new Random().Next(50, 55) * 0.01));
                        int endX = startX + new Random().Next(-10, 50);
                        int endY = (int)(viewport_height * (new Random().Next(45, 50) * 0.01));
                        LogWriteLine($"EelmentToViewport::TouchScrollUp:[{startX},{startY}],[{endX},{endY}]");
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchStart"},
                            { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                        });
                        int steps = new Random().Next(5, 10);
                        for (int j = 1; j <= steps; j++)
                        {
                            var currentX = startX + new Random().Next(-1, 2);
                            var currentY = startY - ((startY - endY) / steps) * j;
                            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                            await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                                { "type","touchMove"},
                                { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                            });
                            SpinWait.SpinUntil(() => false, new Random().Next(10, 20));
                        }
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchEnd"},
                            { "touchPoints",new object[] {}},
                        });
                    }
                }
            }
            catch
            {

            }
            finally
            {
                await CDPHelper.SetEmitTouchEventsForMouse(cdpSession, true);
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
                    await Task.Delay(CommonHelper.RandomRange(2500, 3500));
                    try
                    {

                        if (await page.Locator(".androidOpenModal").CountAsync() > 0)
                        {
                            var closeBtn = page.Locator(".closeBtn");
                            if (await closeBtn.CountAsync() > 0)
                            {
                                await CDPHelper.MouseClickAsync(page, cdpSession, closeBtn);
                                break;
                            }
                        }
                        else if (await page.Locator(".iosOpenModal").CountAsync() > 0)
                        {
                            //closeIcon
                            var closeBtn = page.Locator(".closeIcon");
                            if (await closeBtn.CountAsync() > 0)
                            {
                                LogWriteLine("找到了按钮");
                                await CDPHelper.MouseClickAsync(page, cdpSession, closeBtn);
                                break;
                            }
                        }
                        else if (await page.Locator(".enquiryFormContentnew").CountAsync() > 0 && await page.Locator(".successTipNew_close_new").CountAsync() > 0)
                        {
                            var closeBtn = page.Locator(".successTipNew_close_new");
                            if (await closeBtn.CountAsync() > 0)
                            {
                                await CDPHelper.MouseClickAsync(page, cdpSession, closeBtn);
                                break;
                            }
                        }

                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            });
        }


        private static List<string> InitFPArgs(JObject taskArgs, int maxTouchPoints)
        {
            var result = new List<string>();
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
            result.Add("--platform=Android");
            result.Add($"--platform-version={taskArgs.SelectToken("dev.osv").Value<string>()}");
            result.Add($"--full-version={taskArgs.SelectToken("dev.full_version").Value<string>()}");
            if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.brand")?.Value<string>()))
            {
                result.Add($"--brand={taskArgs.SelectToken("dev.brand")?.Value<string>()}");
                result.Add($"--brand-version={taskArgs.SelectToken("dev.brand_version")?.Value<string>()}");

            }
            if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.model")?.Value<string>()))
            {
                result.Add($"--product-model={taskArgs.SelectToken("dev.model")?.Value<string>()}");
            }
            result.Add($"--fingerprint={CommonHelper.RandomNumber()}");
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



            result.Add($"--webgl-vendor={vendor}");
            result.Add($"--webgl-renderer={gpu}");

            var webgl_extensions = new string[] {
                "EXT_clip_control|WEBGL_stencil_texturing|WEBGL_provoking_vertex|WEBGL_polygon_mode|WEBGL_multi_draw|WEBGL_lose_context|WEBGL_debug_shaders|WEBGL_debug_renderer_info|WEBGL_compressed_texture_s3tc_srgb|WEBGL_compressed_texture_s3tc|WEBGL_clip_cull_distance|WEBGL_blend_func_extended|OVR_multiview2|OES_texture_float_linear|OES_shader_multisample_interpolation|OES_sample_variables|OES_draw_buffers_indexed|NV_shader_noperspective_interpolation|KHR_parallel_shader_compile|EXT_texture_norm16|EXT_texture_mirror_clamp_to_edge|EXT_texture_filter_anisotropic|EXT_texture_compression_rgtc|EXT_texture_compression_bptc|EXT_render_snorm|EXT_polygon_offset_clamp|EXT_float_blend|EXT_disjoint_timer_query_webgl2|EXT_depth_clamp|EXT_conservative_depth|EXT_color_buffer_half_float|EXT_color_buffer_float",
                "EXT_clip_control|WEBGL_stencil_texturing|WEBGL_polygon_mode|WEBGL_multi_draw|WEBGL_lose_context|WEBGL_debug_shaders|WEBGL_debug_renderer_info|WEBGL_compressed_texture_s3tc_srgb|WEBGL_compressed_texture_s3tc|WEBGL_compressed_texture_etc1|WEBGL_compressed_texture_etc|WEBGL_compressed_texture_astc|WEBGL_clip_cull_distance|OVR_multiview2|OES_texture_float_linear|OES_shader_multisample_interpolation|OES_sample_variables|OES_draw_buffers_indexed|NV_shader_noperspective_interpolation|EXT_texture_mirror_clamp_to_edge|EXT_texture_filter_anisotropic|EXT_texture_compression_rgtc|EXT_texture_compression_bptc|EXT_polygon_offset_clamp|EXT_float_blend|EXT_depth_clamp|EXT_conservative_depth|EXT_color_buffer_half_float|EXT_color_buffer_float",
                "EXT_color_buffer_float|WEBGL_multi_draw|WEBGL_lose_context|WEBGL_debug_shaders|WEBGL_debug_renderer_info|WEBGL_compressed_texture_s3tc_srgb|WEBGL_compressed_texture_s3tc|WEBGL_compressed_texture_etc1|WEBGL_compressed_texture_etc|WEBGL_compressed_texture_astc|OES_texture_float_linear|EXT_texture_norm16|EXT_texture_filter_anisotropic|EXT_texture_compression_rgtc|EXT_texture_compression_bptc|EXT_float_blend|EXT_color_buffer_half_float",
            };


            var webgl_extension_text = string.Join("|", webgl_extensions[CommonHelper.RandomRange(0, 3)].Split(new string[] { "|" }, StringSplitOptions.RemoveEmptyEntries).OrderBy(o => Guid.NewGuid()));
            result.Add($"--webgl-extensions={webgl_extension_text}");


            var webgl_vertex_shaders = new string[] {
                "32,256,16,31,1024,14,128,124,128,4,4,1-8,1-1023",
                "32,4096,16,31,16384,36,128,124,64,4,4,1-4095.9375,1-1024",
            };

            result.Add($"--webgl-vertex-shader={webgl_vertex_shaders[CommonHelper.RandomRange(0, 2)]}");


            var webgl_fragment_shaders = new string[] {
                "256,16,1024,14,128,-8,7",
                "4096,16,16384,36,124,-8,7"
            };

            result.Add($"--webgl-fragment-shader={webgl_fragment_shaders[CommonHelper.RandomRange(0, 2)]}");


            var webgl_frame_buffers = new string[] {
                "8,8,4,,16384,16384-16384,8,8,8,8,24,0",
                "8,8,4,,16383,16383-16383,8,8,8,8,24,0"
            };

            result.Add($"--webgl-frame-buffer={webgl_frame_buffers[CommonHelper.RandomRange(0, 2)]}");


            var webgl_textures = new string[] {
                "4096,4096,96,16,2048,2048,16",
                "4096,4096,96,16,16383,4096,256",
            };

            result.Add($"--webgl-textures={webgl_textures[CommonHelper.RandomRange(0, 2)]}");


            var webgl_uniform_buffers = new string[] {
                "84,65536,32,84,230400,230400",
                "24,65536,256,24,212988,200704",
                "216,65536,16,216,606208,626028",
            };
            result.Add($"--webgl-uniform-buffer={webgl_uniform_buffers[CommonHelper.RandomRange(0, 3)]}");


            result.Add($"--hardware-concurrency={(taskArgs.SelectToken("dev.cpu")?.Value<int>() ?? 8)}");
            result.Add("--device-memory=8");
            result.Add("--js-memory-info=10000000|10000000|1136000000");
            result.Add($"--max-touch-points={maxTouchPoints}");

            var quota_info = new string[] { "0|69250036530", "0|138500073060", "0|138500073060", "0|277000146120", "0|277000146120", "0|277000146120", "0|554000292240", "0|554000292240" };
            result.Add($"--quota-info={quota_info[CommonHelper.RandomRange(0, 8)]}");
            result.Add("--enable-rects-noise");
            result.Add("--enable-image-noise");
            result.Add("--enable-text-noise");
            result.Add("--enable-font-noise");
            result.Add("--enable-audio-noise");
            result.Add("--disable-pdf-viewer");
            var hash = Math.Abs(taskArgs["dev"].ToString().GetHashCode());
            result.Add($"--touch-emulator-point={new Random(hash).Next(0, 2)},{new Random(hash).Next(82, 92)}");
            result.Add($"--netinfo-type={new string[] { "wifi", "cellular" }[new Random(Guid.NewGuid().GetHashCode()).Next(0, 2)]}");
            result.Add($"--netinfo-effective=4g");
            result.Add($"--netinfo-rtt={new Random(Guid.NewGuid().GetHashCode()).Next(0, 150)}");

            int level = new Random(Guid.NewGuid().GetHashCode()).Next(5, 101);
            if (new bool[] { false, false, true, false, false, true, false, false, true, false }[new Random(Guid.NewGuid().GetHashCode()).Next(0, 10)])
            {
                result.Add($"--enable-battery-charging");
                result.Add($"--battery-level={Convert.ToDecimal((level * 0.01).ToString("f2"))}");
                result.Add($"--battery-charging-time={new Random(Guid.NewGuid().GetHashCode()).Next(0, 120)}");
                ///--battery-discharging-time=0
            }
            else
            {
                result.Add($"--battery-level={Convert.ToDecimal((level * 0.01).ToString("f2"))}");
                result.Add($"--battery-discharging-time={new Random(Guid.NewGuid().GetHashCode()).Next(21600, 86400) * level}");
            }

            return result;
        }





        private SemaphoreSlim _wordforlocal = new SemaphoreSlim(1);
        private async Task SaveWordForLocal(string name, string q)
        {
            await _wordforlocal.WaitAsync();
            try
            {
                await System.IO.File.AppendAllTextAsync($"./Data/{name}_{System.DateTime.Today.ToString("yyyyMMdd")}.log", $"{q}{System.Environment.NewLine}");
            }
            finally
            {
                _wordforlocal.Release();


            }
        }

        private IBrowserContext BrowserContext;
        public override async Task CloseBrowserContext()
        {
            try
            {
                if (BrowserContext != null)
                {
                    try
                    {
                        await BrowserContext.ClearCookiesAsync();
                        await BrowserContext.CloseAsync();
                    }
                    catch (Exception)
                    {


                    }
                    try
                    {
                        await BrowserContext.DisposeAsync();
                    }
                    catch (Exception)
                    {

                    }
                }
            }
            catch (Exception)
            {


            }
        }


        public override async Task<(bool, bool, int)> ExecuteWorkerAsync(string taskIdentifier, JObject taskArgs)
        {
            #region 任务参数设置
            var st = System.DateTime.Now;
            int taskid = taskArgs.SelectToken("task.id").Value<int>();
            var task_url = taskArgs.SelectToken("task.url").Value<string>();
            string first_page_url = task_url;
            var q = taskArgs.SelectToken("q").Value<string>();
            bool has_input_q = true;
            if (first_page_url.Contains("[QUERY]"))
            {
                has_input_q = false;
                first_page_url = first_page_url.Replace("[QUERY]", q);
            }

            var sleep = new Random().Next(8, 15);
            if (taskArgs.SelectToken("task.sleep") != null)
            {
                var task_sleep = taskArgs.SelectToken("task.sleep").Value<string>();
                if (task_sleep.Contains("-"))
                {
                    var values = task_sleep.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(s => Convert.ToInt32(s)).ToArray();
                    if (values.Length == 2)
                        sleep = new Random().Next(values[0], values[1]);
                }
                else if (int.TryParse(task_sleep, out var _))
                {
                    sleep = Convert.ToInt32(task_sleep);
                }
            }


            this.QTPExecuteStart();
            await this.UpdateTaskStatusAsync(taskid, task_url, "start", 1);
            LogWriteLine($"{this.Title}:ExecuteWorker:Start");
            #endregion

            var useragent = taskArgs.SelectToken("dev.ua").Value<string>();
            var sw = taskArgs.SelectToken("dev.width").Value<int>();
            var sh = taskArgs.SelectToken("dev.height").Value<int>();
            var deviceScale = (taskArgs.SelectToken("dev.sw").Value<double>() - 85) / sw;
            int maxTouchPoints = new Random().Next(4, 6);
            int page_ads_count = 0;
            bool page_trigger_click = false;
            string userDataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "File", "User Data", taskArgs.SelectToken("cacheName").Value<string>());
            var processIndex = taskArgs.SelectToken("processIndex")?.Value<int>();
            if (!processIndex.HasValue)
            {
                processIndex = 1;
            }

            var args = new List<string>()
            {
                "--no-sandbox",
                "--disable-desktop-notifications",
                "--no-first-run",
                "--no-default-browser-check",
                "--show-avatar-button=never",
                "--disable-extensions",
                "--disable-infobars",
                "--disable-logging",
                "--disable-blink-features=AutomationControlled",
                "--disable-gpu",
                "--disable-popup-blocking",
                "--hide-crashed-bubble",
                $"--user-agent={useragent}",
                $"--window-size={sw},{sh}",
                $"--window-position=0,0",
                "--ignore-certificate-errors"
            };
            var fingerArgs = InitFPArgs(taskArgs, maxTouchPoints);
            args.AddRange(fingerArgs);
            LogWriteLine($"args={string.Join(" ", fingerArgs)}");

            using var playwright = await Playwright.CreateAsync();
            var chromium = playwright.Chromium;
            var isProxyMode = taskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            Proxy proxySettings = isProxyMode ? new Proxy { Server = taskArgs.SelectToken("proxy_server").Value<string>() } : null;

            var options = new BrowserTypeLaunchPersistentContextOptions
            {
                Proxy = proxySettings,
                Headless = taskArgs.SelectToken("isHiddenMode")?.Value<bool>() ?? false,
                Channel = "chrome",
                ExecutablePath = "./File/chrome-win/chrome.exe",
                IgnoreDefaultArgs = new List<string>()
                {
                    "--enable-automation",
                },
                Args = args,
                UserAgent = useragent,
                ViewportSize = new ViewportSize() { Width = sw, Height = sh },
                DeviceScaleFactor = (float)deviceScale,
                HasTouch = true,
                IsMobile = true,
                ColorScheme = ColorScheme.Dark,
                IgnoreHTTPSErrors = true,
                TimezoneId = "Asia/Shanghai",
            };
            options.Permissions = new[] { "geolocation" };


            if (new bool[] { false, false, true, false, false }[CommonHelper.RandomRange(0, 5)])
            {
                if (taskArgs.SelectToken("ipInfo.lon") != null)
                {
                    options.Geolocation = new Geolocation() { Longitude = taskArgs.SelectToken("ipInfo.lon").Value<float>(), Latitude = taskArgs.SelectToken("ipInfo.lat").Value<float>() };
                }
                options.TimezoneId = taskArgs.SelectToken("ipInfo.timezone")?.Value<string>() ?? "Asia/Shanghai";
            }



            this.BrowserContext = await chromium.LaunchPersistentContextAsync(userDataDir: userDataDir, options);



            int trigger_download_sign = 0;
            string suggestedFilename = string.Empty;

            this.BrowserContext.Page += (_, page) =>
            {
                page.Dialog += async (_, dialog) =>
                {
                    await dialog.DismissAsync(); // 关闭对话框
                };
                page.Crash += async (_, e) =>
                {
                    await CloseBrowserContext();

                };
                page.PageError += (_, e) =>
                {

                };
                page.RequestFailed += async (_, e) =>
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(e.Failure) &&
                            (e.Failure.Contains("ERR_INVALID_AUTH_CREDENTIALS") ||
                            (e.Failure.Contains("ERR_TUNNEL_CONNECTION_FAILED") && page.Url.Equals(e.Url))))
                        {
                            LogWriteLine($"page.RequestFailed:{e.Failure},{e.Url},{page.Url}");
                            await CloseBrowserContext();
                        }
                    }
                    catch (Exception)
                    {

                    }

                };
                page.Download += async (sender, download) =>
                {
                    Interlocked.Increment(ref trigger_download_sign);
                    suggestedFilename = download.SuggestedFilename;
                    await Task.Delay(new Random().Next(3000, 5000));
                    await download.CancelAsync(); // 取消下载
                };
            };

            try
            {
                IPage page = BrowserContext.Pages.Count > 0 ? BrowserContext.Pages[0] : await BrowserContext.NewPageAsync();
                var cdpSession = await BrowserContext.NewCDPSessionAsync(page);
                await CDPHelper.ClearDataForOriginAsync(cdpSession);
                await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                int pageLoadingTimeout = taskArgs.SelectToken("homePageLoadingTimeout")?.Value<int>() * 1000 ?? 30000;
                int secondJumpRate = taskArgs.SelectToken("secondJumpRate")?.Value<int>() ?? 10;
                int innerJumpRate = taskArgs.SelectToken("innerJumpRate")?.Value<int>() ?? 100;
                int adJumpRate = taskArgs.SelectToken("adJumpRate")?.Value<int>() ?? 0;

                bool priorityNon1688 = taskArgs.SelectToken("priorityNon1688")?.Value<bool>() ?? false;
                bool enableSaveWordForLocal = taskArgs.SelectToken("enableSaveWordForLocal")?.Value<bool>() ?? false;

                int pageloadedDelay = new Random().Next(8000, 15000);
                if (taskArgs.ContainsKey("pageloadedDelay") && !string.IsNullOrWhiteSpace(taskArgs.SelectToken("pageloadedDelay").Value<string>()))
                {
                    var tmpStr = taskArgs["pageloadedDelay"].ToString();
                    if (tmpStr.Contains("-"))
                    {
                        var values = tmpStr.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(s => Convert.ToInt32(s)).ToArray();
                        if (values.Length == 2)
                            pageloadedDelay = new Random().Next(values[0] * 1000, values[1] * 1000);
                    }
                    else
                    {
                        pageloadedDelay = Convert.ToInt32(tmpStr) * 1000;
                    }
                }


                try
                {

                    await page.GotoAsync(first_page_url, new PageGotoOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = pageLoadingTimeout });
                }
                catch (TimeoutException ex)
                {
                    LogWriteLine($"加载超时:{ex.Message}");
                    goto task_end;
                }

                this.QTPExecuteDSP();
                var json_res = await this.UpdateTaskStatusAsync(taskid, task_url, "dsp", 1);
                LogWriteLine($"{this.Title}:ExecuteWorker:曝光进入页面停留{(pageloadedDelay / 1e3):N2}秒");
                await Task.Delay(pageloadedDelay);

                string current_page_url = page.Url;
                int pagesCount = BrowserContext.Pages.Count;

                if (has_input_q)
                {
                    //输入词条的模式
                    try
                    {
                        LogWriteLine($"{this.Title}:输入搜索词条{q}");
                        var input = page.Locator("textarea#kw");
                        if (await input.CountAsync() == 0)
                        {
                            LogWriteLine($"{this.Title}:输入框不存在");
                            goto task_end;
                        }
                        await CDPHelper.MouseClickAsync(page, cdpSession, input);
                        await Task.Delay(new Random().Next(800, 1200));
                        await input.PressSequentiallyAsync(q, new LocatorPressSequentiallyOptions() { Delay = new Random().Next(20, 100) });
                        await Task.Delay(new Random().Next(1500, 2000));
                        var search_button = page.Locator("//div[contains(@class,'submit')]");
                        if (await search_button.CountAsync() == 0)
                        {
                            LogWriteLine($"{this.Title}:搜索按钮不存在");
                            goto task_end;
                        }
                        await CDPHelper.TapAsync(page, cdpSession, search_button);
                        await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                        LogWriteLine($"{this.Title}:搜索完成");
                        await Task.Delay(new Random().Next(5000, 8000));
                    }
                    catch (TimeoutException)
                    {

                    }
                    catch (Exception ex)
                    {
                        LogWriteLine($"{this.Title}:搜索操作失败,{ex.Message}");
                        goto task_end;
                    }
                }



                int relatedAdCount = taskArgs.SelectToken("relatedAdCount")?.Value<int>() ?? 0;
                if (relatedAdCount < 1)
                    relatedAdCount = 1;
                var relatedAd = taskArgs.SelectToken("relatedAd")?.Value<bool>() ?? false;
                if (relatedAd)
                {
                    #region 相关
                    int tryRelatedAdCount = 0;
                tryRelatedAd:
                    tryRelatedAdCount++;
                    if (tryRelatedAdCount > relatedAdCount)
                    {
                        goto task_sleep;
                    }
                    var ad_dot_urls = page.Locator("//div[starts-with(@ad_dot_url,'http')]");
                    if (await ad_dot_urls.CountAsync() == 0)
                    {
                        await TouchPageScroll(page, cdpSession, 1, new Random().Next(1, 3));
                        LogWriteLine($"{this.Title}:广告位为空,继续查找推荐的广告标记");
                        var related = page.Locator("//*[contains(text(),'相关搜索')]");
                        var related_count = await related.CountAsync();
                        if (related_count > 0)
                        {
                            var related_link = related.Locator("..").Locator("a");
                            var related_link_count = await related_link.CountAsync();
                            foreach (var link in (await related_link.AllAsync()).OrderBy(o => Guid.NewGuid()))
                            {
                                await ScrollElementIntoViewAsync(link);
                                var href = await link.GetAttributeAsync("href");
                                var link_text = await link.InnerTextAsync();
                                if (string.IsNullOrWhiteSpace(href) || href.Contains("tel:") || string.IsNullOrWhiteSpace(link_text))
                                {
                                    continue;
                                }
                                LogWriteLine($"{this.Title}:相关搜索：{link_text}");
                                try
                                {
                                    pagesCount = BrowserContext.Pages.Count;
                                    current_page_url = page.Url;
                                    await CDPHelper.MouseClickAsync(page, cdpSession, link);
                                    await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                    await Task.Delay(new Random().Next(5000, 8000));
                                }
                                catch (TimeoutException)
                                {

                                }

                                if ((BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                {
                                    //多增多一次曝光数量
                                    json_res = await this.UpdateTaskStatusAsync(taskid, task_url, "dsp", 1);
                                    q = link_text;
                                    goto related_end;
                                }
                            }
                        }

                    }

                related_end:
                    {
                        ad_dot_urls = page.Locator("//div[starts-with(@ad_dot_url,'http')]");
                        page_ads_count = await ad_dot_urls.CountAsync();
                        if (page_ads_count > 0 && !string.IsNullOrWhiteSpace(q))
                        {
                            await this.AddHotKWAsync(q);
                        }
                        else
                        {
                            LogWriteLine("没有广告标记,重试");
                            goto tryRelatedAd;
                        }
                    }
                    #endregion
                }
                else
                {
                    var ad_dot_urls = page.Locator("//div[starts-with(@ad_dot_url,'http')]");
                    page_ads_count = await ad_dot_urls.CountAsync();
                    if (page_ads_count > 0 && !string.IsNullOrWhiteSpace(q))
                    {

                    }
                    else
                    {
                        LogWriteLine("没有广告标记,重试");
                        goto task_sleep;
                    }
                }


                var queryParams = HttpUtility.ParseQueryString(new Uri(page.Url).Query);
                if (queryParams["q"] != null)
                {
                    q = queryParams["q"];
                }

                int ad_1688_count = 0;
                int ad_other_count = 0;
                var ad_urls = page.Locator("//div[starts-with(@ad_dot_url,'http')]");
                var ad_urls_count = await ad_urls.CountAsync();
                if (ad_urls_count > 0)
                {
                    var ad_urls_range = Enumerable.Range(0, ad_urls_count);
                    foreach (var ad_url_index in ad_urls_range)
                    {
                        var ad_url_item = ad_urls.Nth(ad_url_index);
                        if (!await ad_url_item.IsVisibleAsync())
                        {
                            continue;
                        }
                        await ScrollElementIntoViewAsync(ad_url_item);
                        //await TouchEelmentToViewportAsync(page, cdpSession, ad_url_item);
                        var alis = ad_url_item.Locator("a.c-title").Or(ad_url_item.Locator("a.ad-desc")).Or(ad_url_item.Locator("a.img-item"));
                        var alis_count = await alis.CountAsync();
                        if (alis_count > 0)
                        {
                            var ad_text = await alis.First.InnerTextAsync();
                            var ad_href = await alis.First.GetAttributeAsync("href");
                            var data_url = await alis.First.GetAttributeAsync("data-url");
                            if (!string.IsNullOrWhiteSpace(data_url) && !data_url.Contains("qq.com"))
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
                }
                else
                {
                    await this.AddHotKWAsync("ad-none-kw", q);
                    goto task_end;
                }

                if (ad_other_count > 0 || ad_1688_count > 0)
                {
                    if (ad_other_count > 0)
                    {
                        await SaveWordForLocal("ad_other", q);
                        await this.AddHotKWAsync("ad-other-kw", q);

                    }
                    if (ad_1688_count > 0)
                    {
                        await SaveWordForLocal("ad_1688", q);
                        await this.AddHotKWAsync("ad-1688-kw", q);
                    }
                }
                else
                {
                    await this.AddHotKWAsync("ad-none-kw", q);
                }

                if (ad_other_count > 0 && ad_1688_count == 0)
                {
                    await SaveWordForLocal("ad_no1688", q);
                    //处理相关搜索的词语
                    var related = page.Locator("//*[contains(text(),'相关搜索')]");
                    var related_count = await related.CountAsync();
                    if (related_count > 0)
                    {
                        var link_texts = new List<string>();
                        await ScrollElementIntoViewAsync(related);
                        //await TouchEelmentToViewportAsync(page, cdpSession, related);
                        var related_link = related.Locator("..").Locator("a");
                        var related_link_count = await related_link.CountAsync();
                        foreach (var link in (await related_link.AllAsync()))
                        {
                            if (await link.IsVisibleAsync())
                            {
                                var href = await link.GetAttributeAsync("href");
                                var link_text = await link.InnerTextAsync();
                                if (!string.IsNullOrWhiteSpace(link_text))
                                {
                                    link_texts.Add(link_text);
                                }
                            }
                        }
                        if (link_texts.Count > 0)
                        {
                            await this.AddHotKWAsync("ad-related-kw", string.Join(System.Environment.NewLine, link_texts));
                        }
                    }

                }


                if ((taskArgs.SelectToken("onlyNon1688")?.Value<bool>() ?? false) && ad_other_count == 0)
                {
                    LogWriteLine("没有非1688广告,重试");
                    goto task_sleep;
                }

                if (IsEventOccurring((secondJumpRate * 0.01)))
                {
                    int next_page_count = new Random().Next(0, 2);
                    for (int i = 0; i < next_page_count; i++)
                    {
                        await TouchPageScroll(page, cdpSession, new Random().Next(0, 3), 1, async (page) =>
                        {
                            try
                            {
                                var n = page.Locator("//a[@id='pager']/span[@class='p-next']");
                                if (n != null && await n.CountAsync() > 0)
                                    return await IsElementInViewportAsync(n);
                            }
                            catch (TimeoutException)
                            {

                            }
                            return false;
                        });

                        var next_page_ele = page.Locator("//a[@id='pager']/span[@class='p-next']");
                        if (await next_page_ele.CountAsync() > 0)
                        {
                            LogWriteLine($"翻页:{i + 1},次");
                            try
                            {
                                await TouchEelmentToViewportAsync(page, cdpSession, next_page_ele);
                                await Task.Delay(new Random().Next(500, 1000));
                                await CDPHelper.MouseClickAsync(page, cdpSession, next_page_ele);
                                await Task.Delay(new Random().Next(50, 100));
                                await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                await Task.Delay(new Random().Next(5000, 8000));
                            }
                            catch (TimeoutException)
                            {

                            }
                        }
                    }
                }
                else
                {
                    await TouchPageScroll(page, cdpSession, new Random().Next(0, 2), 1, async (page) =>
                    {
                        try
                        {
                            var n = page.Locator("//a[@id='pager']/span[@class='p-next']");
                            if (n != null && await n.CountAsync() > 0)
                                return await IsElementInViewportAsync(n);
                        }
                        catch (TimeoutException)
                        {

                        }
                        return false;
                    });
                    await Task.Delay(new Random().Next(800, 2000));
                }


                #region jumpClick
                var jumpClick = false;
                current_page_url = page.Url;

                int click_rate = taskArgs.SelectToken("task.click_rate").Value<int>();
                if (click_rate > 0)
                {
                    json_res = await this.GetTaskStatusAsync(taskid, task_url);
                    int dsp_count = json_res.SelectToken("data.dsp").Value<int>();
                    int click_count = 0;
                    if (json_res.SelectToken("data.click") != null)
                    {
                        click_count = json_res.SelectToken("data.click").Value<int>();
                    }
                    if (click_rate == 100 || click_count == 0 || ((click_count / (double)dsp_count) * 100 < click_rate))
                    {
                        jumpClick = true;

                    }
                    LogWriteLine($"点击比率:{(click_count / (double)dsp_count * 100):N2}%");
                }

                if (jumpClick)
                {

                    var sponsoreds = page.Locator("//div[starts-with(@ad_dot_url,'http')]");
                    var sponsored_count = await sponsoreds.CountAsync();
                    if (sponsored_count > 0)
                    {
                        var sortedList = new SortedList<int, ILocator>();
                        int sort_index = 0;
                        List<int> sponsored_range = new List<int>();
                        if (priorityNon1688)
                        {
                            sponsored_range = Enumerable.Range(0, sponsored_count).ToList();
                            foreach (var sponsored_index in sponsored_range)
                            {
                                var sponsored = sponsoreds.Nth(sponsored_index);
                                var alis = sponsored.Locator("a.c-title").Or(sponsored.Locator("a.ad-desc")).Or(sponsored.Locator("a.img-item"));
                                var alis_count = await alis.CountAsync();
                                if (alis_count > 0)
                                {
                                    var ad_text = await alis.First.InnerTextAsync();
                                    var ad_href = await alis.First.GetAttributeAsync("href");
                                    var data_url = await alis.First.GetAttributeAsync("data-url");
                                    if (!string.IsNullOrWhiteSpace(data_url))
                                    {
                                        if (data_url.Contains(".1688."))
                                        {
                                            sortedList.Add(100 + sort_index++, sponsored);
                                        }
                                        else if (data_url.Contains("qq.com"))
                                        {
                                            sortedList.Add(200 + sort_index++, sponsored);
                                        }
                                        else
                                        {
                                            sortedList.Add(sort_index++, sponsored);
                                        }
                                    }
                                }

                            }

                        }



                        if (priorityNon1688)
                            sponsored_range = Enumerable.Range(0, sponsored_count).ToList();
                        else
                            sponsored_range = Enumerable.Range(0, sponsored_count).OrderBy(o => Guid.NewGuid()).ToList();

                        foreach (var sponsored_index in sponsored_range)
                        {
                            var sponsored = priorityNon1688 ? sortedList.Values[sponsored_index] : sponsoreds.Nth(sponsored_index);

                            await TouchEelmentToViewportAsync(page, cdpSession, sponsored, 1);
                            await Task.Delay(CommonHelper.RandomRange(800, 2000));
                            try
                            {
                                pagesCount = BrowserContext.Pages.Count;
                                current_page_url = page.Url;
                                var sponsored_text = await sponsored.InnerTextAsync();

                                StringBuilder buff = new StringBuilder("a.c-title,a.c-text");
                                if (CommonHelper.RandomRange(0, 10) % 2 == 0)
                                {
                                    buff.Append(",.img-item");
                                }

                                if (sponsored_text.Contains("下载"))
                                {
                                    buff.Clear();
                                    buff.Append(".com-swipe-container a.cpc-a img");
                                }

                                var cpc_list = sponsored.Locator(buff.ToString());
                                var cpc_count = await cpc_list.CountAsync();
                                if (cpc_count == 0)
                                {
                                    cpc_list = sponsored.Locator(".cpc-a");
                                    cpc_count = await cpc_list.CountAsync();
                                }

                                if (cpc_count > 0)
                                {
                                    if (cpc_count > 3)
                                    {
                                        var cpc_range = Enumerable.Range(0, 4).OrderBy(o => Guid.NewGuid()).ToList();
                                        foreach (var cpc_index in cpc_range)
                                        {
                                            var cpc = cpc_list.Nth(cpc_index);
                                            if (await cpc.IsVisibleAsync())
                                            {
                                                var tagName = await cpc.EvaluateAsync<string>("el => el.nodeName.toLowerCase()");
                                                LogWriteLine($"触发广告位[title_and_text]:{tagName}:{await cpc.InnerTextAsync()}");
                                                await CDPHelper.MouseClickAsync(page, cdpSession, cpc);
                                                break;
                                            }
                                        }
                                    }
                                    else
                                    {
                                        var cpc_range = await cpc_list.AllAsync();
                                        var cpc = cpc_range[CommonHelper.RandomRange(0, cpc_count)];
                                        var tagName = await cpc.EvaluateAsync<string>("el => el.nodeName.toLowerCase()");
                                        LogWriteLine($"触发广告位[title_and_text]:{tagName}:{await cpc.InnerTextAsync()}");
                                        await CDPHelper.MouseClickAsync(page, cdpSession, cpc);
                                    }

                                }
                                else
                                {
                                    LogWriteLine($"触发广告位:[bounding_box]:{sponsored_text}");
                                    await CDPHelper.MouseClickAsync(page, cdpSession, sponsored, 1);
                                }
                                await Task.Delay(CommonHelper.RandomRange(50, 100));
                                await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                            }
                            catch (TimeoutException)
                            {


                            }

                            //判断是否出发了下载
                            if (trigger_download_sign > 0)
                            {
                                LogWriteLine($"触发下载:{suggestedFilename}");
                                await this.UpdateTaskStatusAsync(taskid, task_url, "click", 1);
                                this.QTPExecuteClickthrough();
                                LogWriteLine($"{this.Title}:ExecuteWorker:Clickthrough");
                                page_trigger_click = true;
                                await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(1, 3), 1);
                                await Task.Delay(CommonHelper.RandomRange(1000, 2000));
                                goto task_end;
                            }


                            if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                            {
                                if (this.BrowserContext.Pages.Count > pagesCount)
                                {
                                    page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                    cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                    await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                }
                                await this.UpdateTaskStatusAsync(taskid, task_url, "click", 1);
                                this.QTPExecuteClickthrough();
                                LogWriteLine($"{this.Title}:ExecuteWorker:Clickthrough");
                                page_trigger_click = true;


                                if (IsEventOccurring(innerJumpRate * 0.01))
                                {
                                    await Task.Delay(CommonHelper.RandomRange(2000, 3000));
                                    try
                                    {
                                        if (page.Url.Contains("m.p4psearch.1688.com"))
                                        {
                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 4), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 2000));

                                            var page_sponsoreds = page.Locator("div:text('广告')").Or(page.Locator("span:text('广告')"));
                                            var page_sponsoreds_count = await page_sponsoreds.CountAsync();
                                            if (page_sponsoreds_count > 0)
                                            {
                                                var page_sponsored_range = Enumerable.Range(0, page_sponsoreds_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                                                foreach (var page_sponsored_index in page_sponsored_range)
                                                {
                                                    var page_sponsored = sponsoreds.Nth(page_sponsored_index);

                                                    await TouchEelmentToViewportAsync(page, cdpSession, page_sponsored);
                                                    await Task.Delay(CommonHelper.RandomRange(800, 2000));

                                                    var tagNode = await sponsored.EvaluateHandleAsync(@"element => { const anchor = element.closest('div.offer-item'); return anchor ? anchor : null;}");
                                                    if (tagNode != null && tagNode.AsElement() != null)
                                                    {
                                                        var tagEle = tagNode.AsElement();
                                                        if (tagEle != null)
                                                        {
                                                            try
                                                            {
                                                                pagesCount = this.BrowserContext.Pages.Count;
                                                                current_page_url = page.Url;
                                                                await CDPHelper.MouseClickAsync(page, cdpSession, tagEle);
                                                                await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = pageLoadingTimeout });
                                                                await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                                                            }
                                                            catch (TimeoutException)
                                                            {

                                                            }

                                                            if (this.BrowserContext.Pages.Count > pagesCount || !page.Url.Equals(current_page_url))
                                                            {
                                                                if (this.BrowserContext.Pages.Count > pagesCount)
                                                                {
                                                                    page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                                                    cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                                                    await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                                }
                                                                ProcessingPageElementTask(page, cdpSession);

                                                            }
                                                            goto task_sleep;
                                                        }

                                                    }
                                                }
                                            }
                                            else
                                            {
                                                var fusion_items = page.Locator("div[component-tech='fusion'] iframe");
                                                int fusion_items_count = await fusion_items.CountAsync();
                                                if (fusion_items_count > 0)
                                                {
                                                    var fusion_items_range = Enumerable.Range(0, fusion_items_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                                                    foreach (var fusion_item_index in fusion_items_range)
                                                    {
                                                        var fusion_item = fusion_items.Nth(fusion_item_index);
                                                        await TouchEelmentToViewportAsync(page, cdpSession, fusion_item);
                                                        await Task.Delay(CommonHelper.RandomRange(800, 2000));

                                                        try
                                                        {
                                                            pagesCount = this.BrowserContext.Pages.Count;
                                                            current_page_url = page.Url;
                                                            await CDPHelper.MouseClickAsync(page, cdpSession, fusion_item);
                                                            await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = pageLoadingTimeout });
                                                            await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                                                        }
                                                        catch (TimeoutException)
                                                        {

                                                        }
                                                        if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                        {
                                                            if (this.BrowserContext.Pages.Count > pagesCount)
                                                            {
                                                                page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                                                cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                                                await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                            }

                                                            ProcessingPageElementTask(page, cdpSession);
                                                        }
                                                        goto task_sleep;
                                                    }


                                                }
                                                else
                                                {
                                                    var offer_items = page.Locator("//div[starts-with(@class,'offer-item')]");
                                                    int offer_items_count = await offer_items.CountAsync();
                                                    if (offer_items_count > 0)
                                                    {
                                                        var offer_items_range = Enumerable.Range(0, offer_items_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                                                        foreach (var offer_item_index in offer_items_range)
                                                        {
                                                            var offer_item = offer_items.Nth(offer_item_index);
                                                            await TouchEelmentToViewportAsync(page, cdpSession, offer_item);
                                                            await Task.Delay(CommonHelper.RandomRange(800, 2000));

                                                            try
                                                            {
                                                                pagesCount = this.BrowserContext.Pages.Count;
                                                                current_page_url = page.Url;
                                                                await CDPHelper.MouseClickAsync(page, cdpSession, offer_item);
                                                                await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = pageLoadingTimeout });
                                                                await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                                                            }
                                                            catch (TimeoutException)
                                                            {

                                                            }

                                                            if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                            {
                                                                if (this.BrowserContext.Pages.Count > pagesCount)
                                                                {
                                                                    page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                                                    cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                                                     await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                                }
                                                                ProcessingPageElementTask(page, cdpSession);
                                                            }
                                                            goto task_sleep;
                                                        }
                                                    }
                                                }
                                            }

                                        }
                                        else if (page.Url.Contains("m.1688.com"))
                                        {
                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 4), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 2000));
                                            var page_sponsoreds = page.Locator("div:text('广告')").Or(page.Locator("span:text('广告')"));
                                            var page_sponsoreds_count = await page_sponsoreds.CountAsync();
                                            if (page_sponsoreds_count > 0)
                                            {
                                                var page_sponsored_range = Enumerable.Range(0, page_sponsoreds_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                                                foreach (var page_sponsored_index in page_sponsored_range)
                                                {
                                                    var page_sponsored = page_sponsoreds.Nth(page_sponsored_index);
                                                    await TouchEelmentToViewportAsync(page, cdpSession, page_sponsored);
                                                    await Task.Delay(CommonHelper.RandomRange(800, 2000));

                                                    var tagNode = await sponsored.EvaluateHandleAsync(@"element => { const anchor = element.closest('div.offer-item'); return anchor ? anchor : null;}");
                                                    if (tagNode != null && tagNode.AsElement() != null)
                                                    {
                                                        var tagEle = tagNode.AsElement();
                                                        if (tagEle != null)
                                                        {
                                                            try
                                                            {
                                                                pagesCount = this.BrowserContext.Pages.Count;
                                                                current_page_url = page.Url;
                                                                await CDPHelper.MouseClickAsync(page, cdpSession, tagEle);
                                                                await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                                                await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                                                            }
                                                            catch (TimeoutException)
                                                            {

                                                            }

                                                            if (this.BrowserContext.Pages.Count > pagesCount || !page.Url.Equals(current_page_url))
                                                            {
                                                                if (this.BrowserContext.Pages.Count > pagesCount)
                                                                {
                                                                    page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                                                    cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                                                    await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                                }
                                                                ProcessingPageElementTask(page, cdpSession);

                                                            }
                                                            goto task_sleep;
                                                        }



                                                    }

                                                }
                                            }
                                            else
                                            {
                                                var fusion_items = page.Locator("div[component-tech='fusion'] iframe");
                                                int fusion_items_count = await fusion_items.CountAsync();
                                                if (fusion_items_count > 0)
                                                {
                                                    var fusion_items_range = Enumerable.Range(0, fusion_items_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                                                    foreach (var fusion_item_index in fusion_items_range)
                                                    {
                                                        var fusion_item = fusion_items.Nth(fusion_item_index);
                                                        await TouchEelmentToViewportAsync(page, cdpSession, fusion_item);
                                                        await Task.Delay(CommonHelper.RandomRange(800, 2000));

                                                        try
                                                        {
                                                            pagesCount = this.BrowserContext.Pages.Count;
                                                            current_page_url = page.Url;
                                                            await CDPHelper.MouseClickAsync(page, cdpSession, fusion_item);
                                                            await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                                            await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                                                        }
                                                        catch (TimeoutException)
                                                        {

                                                        }
                                                        if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                        {
                                                            if (this.BrowserContext.Pages.Count > pagesCount)
                                                            {
                                                                page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                                                cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                                                 await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                            }
                                                            ProcessingPageElementTask(page, cdpSession);
                                                        }
                                                        goto task_sleep;
                                                    }


                                                }
                                                else
                                                {
                                                    var offer_items = page.Locator("//div[starts-with(@class,'offer-item')]");

                                                    int offer_items_count = await offer_items.CountAsync();
                                                    if (offer_items_count > 0)
                                                    {
                                                        var offer_items_range = Enumerable.Range(0, offer_items_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                                                        foreach (var offer_item_index in offer_items_range)
                                                        {
                                                            var offer_item = offer_items.Nth(offer_item_index);
                                                            await TouchEelmentToViewportAsync(page, cdpSession, offer_item);
                                                            await Task.Delay(CommonHelper.RandomRange(800, 2000));
                                                            try
                                                            {
                                                                pagesCount = this.BrowserContext.Pages.Count;
                                                                current_page_url = page.Url;
                                                                await CDPHelper.MouseClickAsync(page, cdpSession, offer_item);
                                                                await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                                                await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                                                            }
                                                            catch (TimeoutException)
                                                            {

                                                            }
                                                            if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                            {
                                                                if (this.BrowserContext.Pages.Count > pagesCount)
                                                                {
                                                                    page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                                                    cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                                                     await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                                }
                                                                ProcessingPageElementTask(page, cdpSession);
                                                            }
                                                            goto task_sleep;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        else if (page.Url.Contains("b2b.baidu.com"))
                                        {
                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 4), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 2000));

                                            var offer_items = page.Locator("a.product-item-link");
                                            var offer_items_count = await offer_items.CountAsync();
                                            if (offer_items_count > 0)
                                            {
                                                var offer_items_range = Enumerable.Range(0, offer_items_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                                                foreach (var offer_item_index in offer_items_range)
                                                {
                                                    var offer_item = offer_items.Nth(offer_item_index);
                                                    await TouchEelmentToViewportAsync(page, cdpSession, offer_item);
                                                    await Task.Delay(CommonHelper.RandomRange(800, 2000));

                                                    try
                                                    {
                                                        pagesCount = this.BrowserContext.Pages.Count;
                                                        current_page_url = page.Url;
                                                        await CDPHelper.MouseClickAsync(page, cdpSession, offer_item);
                                                        await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                                        await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                                                    }
                                                    catch (TimeoutException)
                                                    {

                                                    }

                                                    if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                    {
                                                        if (this.BrowserContext.Pages.Count > pagesCount)
                                                        {
                                                            page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                                            cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                                            await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                        }
                                                    }
                                                    goto task_sleep;
                                                }
                                            }
                                        }
                                        else if (page.Url.Contains("uland.taobao.com"))
                                        {
                                            await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                            await Task.Delay(CommonHelper.RandomRange(800, 2000));
                                            var offer_items = page.Locator("//a[starts-with(@class,'link')]");
                                            var offer_items_count = await offer_items.CountAsync();
                                            if (offer_items_count > 0)
                                            {
                                                var offer_items_range = Enumerable.Range(0, offer_items_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                                                foreach (var offer_item_index in offer_items_range)
                                                {
                                                    var offer_item = offer_items.Nth(offer_item_index);
                                                    await TouchEelmentToViewportAsync(page, cdpSession, offer_item);
                                                    await Task.Delay(CommonHelper.RandomRange(800, 2000));
                                                    try
                                                    {
                                                        pagesCount = this.BrowserContext.Pages.Count;
                                                        current_page_url = page.Url;
                                                        await CDPHelper.MouseClickAsync(page, cdpSession, offer_item);
                                                        await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                                        await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                                                    }
                                                    catch (TimeoutException)
                                                    {

                                                    }
                                                    if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                    {
                                                        if (this.BrowserContext.Pages.Count > pagesCount)
                                                        {
                                                            page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                                            cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                                            //await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                                        }
                                                    }
                                                    goto task_sleep;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (IsEventOccurring(adJumpRate * 0.01))
                                            {
                                                await TouchPageScroll(page, cdpSession, CommonHelper.RandomRange(0, 3), 1);
                                                await Task.Delay(CommonHelper.RandomRange(800, 2000));
                                                var offer_items = page.Locator("a");
                                                var offer_items_count = await offer_items.CountAsync();
                                                if (offer_items_count > 0)
                                                {
                                                    var offer_items_range = Enumerable.Range(0, offer_items_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                                                    foreach (var offer_item_index in offer_items_range)
                                                    {
                                                        var offer_item = offer_items.Nth(offer_item_index);
                                                        await TouchEelmentToViewportAsync(page, cdpSession, offer_item);
                                                        await Task.Delay(CommonHelper.RandomRange(800, 2000));
                                                        try
                                                        {
                                                            pagesCount = this.BrowserContext.Pages.Count;
                                                            current_page_url = page.Url;
                                                            await CDPHelper.MouseClickAsync(page, cdpSession, offer_item);
                                                            await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                                            await Task.Delay(CommonHelper.RandomRange(5000, 8000));
                                                        }
                                                        catch (TimeoutException)
                                                        {

                                                        }
                                                        if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                                        {
                                                            if (this.BrowserContext.Pages.Count > pagesCount)
                                                            {
                                                                page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                                                cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                                            }
                                                        }
                                                        goto task_sleep;
                                                    }
                                                }
                                            }

                                        }
                                    }
                                    catch
                                    {
                                    }
                                }
                                goto task_sleep;
                            }
                        }
                    }

                }
                else
                {
                    if (IsEventOccurring(secondJumpRate * 0.01))
                    {
                        var sc_query = new int[] { 1, 1, 2, 1, 1, 2, 1, 1, 2, 1 }[new Random().Next(1, 10)] == 1 ? page.Locator("//div[contains(@class,'sc_structure_template_normal')]") : page.Locator("//div[contains(@class,'nature-result')]");
                        int sc_query_count = await sc_query.CountAsync();

                        if (sc_query_count > 0)
                        {
                            var sc_result_range = Enumerable.Range(0, sc_query_count).ToArray().OrderBy(o => Guid.NewGuid()).ToList();
                            foreach (var sc_result_item_index in sc_result_range)
                            {
                                var sc_result_item = sc_query.Nth(sc_result_item_index);

                                await TouchEelmentToViewportAsync(page, cdpSession, sc_result_item);
                                await Task.Delay(new Random().Next(1500, 2500));

                                try
                                {
                                    pagesCount = this.BrowserContext.Pages.Count;
                                    current_page_url = page.Url;
                                    await CDPHelper.MouseClickAsync(page, cdpSession, sc_result_item);
                                    await page.WaitForURLAsync("**", new PageWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 15000 });
                                    await Task.Delay(new Random().Next(5000, 8000));
                                }
                                catch (TimeoutException)
                                {

                                }
                                if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                {
                                    if (this.BrowserContext.Pages.Count > pagesCount)
                                    {
                                        page = this.BrowserContext.Pages[this.BrowserContext.Pages.Count - 1];
                                        cdpSession = await this.BrowserContext.NewCDPSessionAsync(page);
                                        await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                                    }
                                    goto task_sleep;
                                }
                            }
                        }
                    }
                    else
                    {
                        await TouchPageScroll(page, cdpSession, new Random().Next(3, 5), 1);
                        await Task.Delay(new Random().Next(3000, 5000));
                    }
                }
            #endregion

            task_sleep:
                {
                    this.QTPExecuteSuccess();
                    LogWriteLine($"{this.Title}:ExecuteWorker:Success");
                    LogWriteLine($"延时停留");
                    DateTime s1 = System.DateTime.Now;
                    await Task.Delay(CommonHelper.RandomRange(3000, 5000));
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

                            int totalSecond = (int)((TimeSpan)(System.DateTime.Now - s1)).TotalSeconds;
                            if (totalSecond >= sleep)
                                break;
                            await Task.Delay(CommonHelper.RandomRange(3000, 5000));
                        }
                        catch (Exception)
                        {
                            break;
                        }
                    } while (true);
                    LogWriteLine("动作完成");
                }
            task_end:
                {
                    this.QTPExecuteComplete();
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
                await CloseBrowserContext();
            }
            return (false, page_trigger_click, page_ads_count);
        }
    }
}
