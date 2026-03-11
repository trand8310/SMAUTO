using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using QTP.Common.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;




namespace QTP.Plugins
{
    public sealed class SMAutoAdTask : QTPServiceBase
    {

        public static QTPPlugin GetInfo()
        {
            return new QTPPlugin()
            {
                ClassName = "QTP.Plugins.SMAutoAdTask",
                Name = "SMAutoAd",
                FileName = "SMAutoAd.dll",
            };
        }
        public override string Title => "神马搜索Auto";

        public Func<bool, Task<string>> GetWord;

        public SMAutoAdTask(IWritableOptions<AppSettings> appSettings, Func<bool, Task<string>> action) : base(appSettings)
        {
            GetWord = action;
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
                         block: '" + new string[] { "center", "nearest", "start", "end", "center", "nearest", "center", "center", "nearest", "start" }[CommonHelper.RandomRange(0, 10)] + @"',
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
                int startX = (int)(page.ViewportSize.Width * (CommonHelper.RandomRange(35, 55) * 0.01));
                int startY = (int)(page.ViewportSize.Height * (CommonHelper.RandomRange(65, 90) * 0.01));
                int endX = (int)(page.ViewportSize.Width * (CommonHelper.RandomRange(55, 75) * 0.01));
                int endY = -CommonHelper.RandomRange(180, 300);
                LogWriteLine($"开始滑动:[{startX},{startY}],[{endX},{endY}]");
                await cdpSession.SendAsync("Input.synthesizeScrollGesture", new Dictionary<string, object>()
             {
                 { "x",startX},
                 { "y",startY},
                 { "xDistance",endX},
                 { "yDistance",endY},
                 { "yOverscroll",CommonHelper.RandomRange(50, 300)},
                 { "gestureSourceType","default"},
             });
                await Task.Delay(CommonHelper.RandomRange(500, 2000));
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
                int startX = (int)(page.ViewportSize.Width * (CommonHelper.RandomRange(45, 50) * 0.01));
                int startY = (int)(page.ViewportSize.Height * (CommonHelper.RandomRange(15, 30) * 0.01));
                int endX = (int)(page.ViewportSize.Width * (CommonHelper.RandomRange(50, 60) * 0.01));
                int endY = startY + CommonHelper.RandomRange(180, 300);
                LogWriteLine($"开始滑动:[{startX},{startY}],[{endX},{endY}]");
                await cdpSession.SendAsync("Input.synthesizeScrollGesture", new Dictionary<string, object>()
               {
                   { "x",startX},
                   { "y",startY},
                   { "xDistance",endX},
                   { "yDistance",endY},
                   { "yOverscroll",-CommonHelper.RandomRange(50,200)},
                   { "gestureSourceType","default"},
               });
                await Task.Delay(CommonHelper.RandomRange(200, 2000));

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
                    int startX = (int)(sw * (CommonHelper.RandomRange(45, 50) * 0.01));
                    int startY = (int)(sh * (CommonHelper.RandomRange(15, 30) * 0.01));
                    int endX = (int)(sw * (CommonHelper.RandomRange(50, 60) * 0.01));
                    int endY = startY + CommonHelper.RandomRange(180, 300);
                    LogWriteLine($"向下滑动:[{startX},{startY}],[{endX},{endY}]");
                    await cdpSession.SendAsync("Input.synthesizeScrollGesture",
                        new Dictionary<string, object>()
                       {
                       { "x",startX},
                       { "y",startY},
                       { "xDistance",endX},
                       { "yDistance",endY},
                       { "yOverscroll",-CommonHelper.RandomRange(50,200)},
                       { "gestureSourceType","default"},
                       });
                }
                else
                {

                    int startX = (int)(sw * (CommonHelper.RandomRange(35, 55) * 0.01));
                    int startY = (int)(sh * (CommonHelper.RandomRange(65, 90) * 0.01));
                    int endX = (int)(sw * (CommonHelper.RandomRange(55, 75) * 0.01));
                    int endY = -CommonHelper.RandomRange(180, 300);
                    LogWriteLine($"向上滑动:[{startX},{startY}],[{endX},{endY}]");
                    await cdpSession.SendAsync("Input.synthesizeScrollGesture",
                        new Dictionary<string, object>()
                     {
                     { "x",startX},
                     { "y",startY},
                     { "xDistance",endX},
                     { "yDistance",endY},
                     { "yOverscroll",CommonHelper.RandomRange(50,300)},
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
                int startX = (int)(page.ViewportSize.Width * (CommonHelper.RandomRange(35, 55) * 0.01));
                int startY = (int)(page.ViewportSize.Height * (CommonHelper.RandomRange(65, 90) * 0.01));
                int endX = startX + CommonHelper.RandomRange(-10, 50);
                int endY = (int)(page.ViewportSize.Height * (CommonHelper.RandomRange(10, 25) * 0.01));
                int steps = CommonHelper.RandomRange(15, 45);
                LogWriteLine($"TouchScrollUp:[{startX},{startY}],[{endX},{endY}]");
                await cdpSession.SendAsync("Input.dispatchTouchEvent",
                    new Dictionary<string, object>() {
                        { "type","touchStart"},
                        { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                });

                for (int j = 1; j <= steps; j++)
                {
                    var currentX = startX + CommonHelper.RandomRange(-1, 2);
                    var currentY = startY - ((startY - endY) / steps) * j;
                    await cdpSession.SendAsync("Input.dispatchTouchEvent",
                        new Dictionary<string, object>() {
                            { "type","touchMove"},
                            { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                    });
                    SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(5, 15));
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
                int startX = (int)(page.ViewportSize.Width * (CommonHelper.RandomRange(35, 55) * 0.01));// 滑动起始点 X 坐标
                int startY = (int)(page.ViewportSize.Height * (CommonHelper.RandomRange(10, 20) * 0.01)); // 滑动起始点 Y 坐标
                int endX = startX + CommonHelper.RandomRange(-10, 50);
                int endY = (int)(page.ViewportSize.Height * (CommonHelper.RandomRange(60, 90) * 0.01));// 滑动终点 Y 坐标
                int steps = CommonHelper.RandomRange(15, 40);   // 滑动的步数（影响平滑度）
                LogWriteLine($"TouchScrollDown:[{startX},{startY}],[{endX},{endY}]");
                await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                    { "type","touchStart"},
                    { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                });
                for (int j = 1; j <= steps; j++)
                {
                    var currentX = startX + CommonHelper.RandomRange(-1, 2);
                    var currentY = startY + ((endY - startY) / steps) * j;
                    await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                        { "type","touchMove"},
                        { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                    });
                    SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(5, 15));
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
                    await Task.Delay(CommonHelper.RandomRange(800, 2345));
                    if (predexp != null && await predexp(page))
                    {
                        break;
                    }
                }
                else
                {
                    await TouchScrollUp(page, cdpSession);
                    await Task.Delay(CommonHelper.RandomRange(800, 2345));
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
                        int startX = (int)(viewport_width * (CommonHelper.RandomRange(35, 55) * 0.01));// 滑动起始点 X 坐标
                        int startY = (int)(viewport_height * (CommonHelper.RandomRange(15, 35) * 0.01)); // 滑动起始点 Y 坐标
                        int endX = startX + CommonHelper.RandomRange(-10, 50);
                        int endY = (int)(page.ViewportSize.Height * (CommonHelper.RandomRange(60, 85) * 0.01));// 滑动终点 Y 坐标
                        LogWriteLine($"EelmentToViewport::TouchScrollDown:[{startX},{startY}],[{endX},{endY}]");
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchStart"},
                            { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                        });
                        int steps = speed == 0 ? CommonHelper.RandomRange(15, 40) : CommonHelper.RandomRange(10, 30);//// 滑动的步数（影响平滑度）
                        for (int j = 1; j <= steps; j++)
                        {
                            var currentX = startX + CommonHelper.RandomRange(-1, 2);
                            var currentY = startY + ((endY - startY) / steps) * j;
                            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                            await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                                { "type","touchMove"},
                                { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                            });
                            SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(10, 25));
                        }
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchEnd"},
                            { "touchPoints",new object[] {}},
                          });
                        if (speed == 0)
                            SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(800, 2000));
                        else
                            SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(500, 1000));


                        boundingBox = await locator.BoundingBoxAsync();
                    } while (boundingBox.Y < 0);


                    if (boundingBox.Y < top_boundary && !await IsPageTop(page))
                    {
                        int startX = (int)(viewport_width * (CommonHelper.RandomRange(35, 55) * 0.01));// 滑动起始点 X 坐标
                        int startY = (int)(viewport_height * (CommonHelper.RandomRange(45, 50) * 0.01)); // 滑动起始点 Y 坐标
                        int endX = startX + CommonHelper.RandomRange(-10, 50);
                        int endY = (int)(page.ViewportSize.Height * (CommonHelper.RandomRange(50, 55) * 0.01));// 滑动终点 Y 坐标
                        LogWriteLine($"TouchScrollDown:[{startX},{startY}],[{endX},{endY}]");
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchStart"},
                            { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                        });
                        int steps = CommonHelper.RandomRange(5, 10);
                        for (int j = 1; j <= steps; j++)
                        {
                            var currentX = startX + CommonHelper.RandomRange(-1, 2);
                            var currentY = startY + ((endY - startY) / steps) * j;
                            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                            await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                                { "type","touchMove"},
                                { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                            });
                            SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(10, 25));
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
                        int startX = (int)(viewport_width * (CommonHelper.RandomRange(40, 55) * 0.01));
                        int startY = (int)(viewport_height * (CommonHelper.RandomRange(65, 85) * 0.01));
                        int endX = startX + CommonHelper.RandomRange(-10, 50);
                        int endY = (int)(viewport_height * (CommonHelper.RandomRange(15, 35) * 0.01));
                        LogWriteLine($"EelmentToViewport::TouchScrollUp:[{startX},{startY}],[{endX},{endY}]");
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchStart"},
                            { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                        });
                        int steps = speed == 0 ? CommonHelper.RandomRange(15, 40) : CommonHelper.RandomRange(10, 30);
                        for (int j = 1; j <= steps; j++)
                        {
                            var currentX = startX + CommonHelper.RandomRange(-1, 2);
                            var currentY = startY - ((startY - endY) / steps) * j;
                            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                            await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                                { "type","touchMove"},
                                { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                            });

                            SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(10, 25));
                        }
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchEnd"},
                            { "touchPoints",new object[] {}},
                        });
                        if (speed == 0)
                            SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(800, 2000));
                        else
                            SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(500, 1000));

                        boundingBox = await locator.BoundingBoxAsync();
                    } while (boundingBox.Y > viewport_height);

                    if (boundingBox.Y > bottom_boundary && !await IsPageEnd(page))
                    {
                        int startX = (int)(viewport_width * (CommonHelper.RandomRange(40, 55) * 0.01));
                        int startY = (int)(viewport_height * (CommonHelper.RandomRange(50, 55) * 0.01));
                        int endX = startX + CommonHelper.RandomRange(-10, 50);
                        int endY = (int)(viewport_height * (CommonHelper.RandomRange(45, 50) * 0.01));
                        LogWriteLine($"EelmentToViewport::TouchScrollUp:[{startX},{startY}],[{endX},{endY}]");
                        await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                        await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                            { "type","touchStart"},
                            { "touchPoints",new object[] {new TouchPoint(startX,startY)}},
                        });
                        int steps = CommonHelper.RandomRange(5, 10);
                        for (int j = 1; j <= steps; j++)
                        {
                            var currentX = startX + CommonHelper.RandomRange(-1, 2);
                            var currentY = startY - ((startY - endY) / steps) * j;
                            await CDPHelper.ChangeDeviceOrientationAsync(cdpSession);
                            await cdpSession.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                                { "type","touchMove"},
                                { "touchPoints",new object[] { new TouchPoint(currentX,currentY) }},
                            });
                            SpinWait.SpinUntil(() => false, CommonHelper.RandomRange(10, 20));
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
                    await Task.Delay(CommonHelper.RandomRange(800, 1200));
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


        private void ProcessingPageElementTask(IFrame page, ICDPSession cdpSession)
        {
            _ = Task.Run(async () =>
            {
                int redo_try_count = 0;
                while (redo_try_count++ < 10)
                {
                    await Task.Delay(CommonHelper.RandomRange(300, 500));
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


            var os = taskArgs.SelectToken("os").Value<int>();

            if (os == 2)
            {
                result.Add("--platform=iOS");
                result.Add("--screen-color-depth=32");
            }
            else
            {
                result.Add("--platform=Android");


            }



            result.Add($"--platform-version={taskArgs.SelectToken("dev.osv").Value<string>()}");
            result.Add($"--full-version={taskArgs.SelectToken("dev.full_version").Value<string>()}");

            if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.brand")?.Value<string>()))
            {
                result.Add($"--brand={taskArgs.SelectToken("dev.brand")?.Value<string>()}");
                result.Add($"--brand-version={taskArgs.SelectToken("dev.brand_version")?.Value<string>()}");

            }

            if (!string.IsNullOrWhiteSpace(taskArgs.SelectToken("dev.model")?.Value<string>()))
            {
                if (os == 1)
                {
                    result.Add($"--product-model={taskArgs.SelectToken("dev.model")?.Value<string>()}");

                }

            }
            result.Add($"--fingerprint={CommonHelper.RandomNumber()}");


            result.Add($"--netinfo-type={new string[] { "wifi", "cellular" }[CommonHelper.RandomRange(0, 2)]}");
            result.Add($"--netinfo-effective=4g");
            result.Add($"--netinfo-rtt={CommonHelper.RandomRange(0, 200)}");

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


            #region webgl
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
            #endregion

            result.Add($"--hardware-concurrency={(taskArgs.SelectToken("dev.cpu")?.Value<int>() ?? 8)}");

            var ram = taskArgs.SelectToken("dev.ram").Value<string>().Split(',', StringSplitOptions.RemoveEmptyEntries);
            int deviceMemory = Convert.ToInt32(ram[CommonHelper.RandomRange(0, ram.Length)].Trim());
            result.Add($"--device-memory={(deviceMemory > 8 ? 8 : deviceMemory)}");
            result.Add("--js-memory-info=10000000|10000000|1136000000");
            result.Add($"--max-touch-points={maxTouchPoints}");

            //var quota_info = new string[] { "0|69250036530", "0|138500073060", "0|138500073060", "0|277000146120", "0|277000146120", "0|277000146120", "0|554000292240", "0|554000292240" };

            if (taskArgs.SelectToken("dev.storage") != null)
            {
                var quota_info = new string[] { "0|69250036530", "0|138500073060", "0|138500073060", "0|277000146120", "0|277000146120", "0|277000146120", "0|554000292240", "0|554000292240" };
                result.Add($"--quota-info={quota_info[CommonHelper.RandomRange(0, 8)]}");
            }
            else
            {
                var storage = taskArgs.SelectToken("dev.storage").Value<string>().Split(',', StringSplitOptions.RemoveEmptyEntries);
                long quota_info = long.Parse(storage[CommonHelper.RandomRange(0, storage.Count())]) * 1024 * 1024 * 1024;
                result.Add($"--quota-info={quota_info}");
            }




            result.Add("--enable-rects-noise");
            result.Add("--enable-image-noise");
            result.Add("--enable-text-noise");
            //result.Add("--enable-font-noise");
            result.Add("--enable-audio-noise");
            result.Add("--disable-pdf-viewer");
            result.Add($"--touch-emulator-point={CommonHelper.RandomRange(0, 2)},{CommonHelper.RandomRange(82, 92)}");
            result.Add($"--netinfo-type={new string[] { "wifi", "cellular" }[CommonHelper.RandomRange(0, 2)]}");
            result.Add($"--netinfo-effective=4g");
            result.Add($"--netinfo-rtt={CommonHelper.RandomRange(0, 200)}");

            int level = CommonHelper.RandomRange(5, 101);
            if (new bool[] { false, false, true, false, false, true, false, false, true, false }[CommonHelper.RandomRange(0, 10)])
            {
                result.Add($"--enable-battery-charging");
                result.Add($"--battery-level={Convert.ToDecimal((level * 0.01).ToString("f2"))}");
                result.Add($"--battery-charging-time={CommonHelper.RandomRange(0, 120)}");
                ///--battery-discharging-time=0
            }
            else
            {
                result.Add($"--battery-level={Convert.ToDecimal((level * 0.01).ToString("f2"))}");
                result.Add($"--battery-discharging-time={CommonHelper.RandomRange(21600, 86400) * level}");
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
        private IBrowser Browser;
        public override async Task CloseBrowserContext()
        {
            if (this.IsCloseBrowser)
            {
                return;
            }
            this.IsCloseBrowser = true;
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

            try
            {
                if (Browser != null)
                {
                    try
                    {

                        await Browser.CloseAsync();
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


        private Task ADX_CLICK(int taskid, string task_url, int click_rate, IFrame page, ICDPSession cdpSession, int timeout = 30000)
        {
            return Task.Run(async () =>
            {
                try
                {
                    this.QTPExecuteDSP();
                    var json_res = await this.UpdateTaskStatusAsync(taskid, task_url, "dsp", 1);
                    Console.WriteLine($"Main frame navigated to: {page.Url}");
                    var queryParams = HttpUtility.ParseQueryString(new Uri(page.Url).Query);
                    if (queryParams["q"] != null)
                    {
                        var q = queryParams["q"];
                        await this.AddHotKWAsync($"ad_auo_kw_{System.DateTime.Today.ToString("yyyyMM")}", q);
                    }
                    try
                    {
                        await page.WaitForSelectorAsync("#wraper", new FrameWaitForSelectorOptions() { Timeout = 5000 });
                    }
                    catch (TimeoutException)
                    {


                    }

                    var ad_dot_urls = page.Locator("//div[starts-with(@ad_dot_url,'http')]");
                    int page_ads_count = await ad_dot_urls.CountAsync();
                    LogWriteLine($"广告数量:{page_ads_count}");
                    if (page_ads_count > 0)
                    {
                        var jumpClick = false;
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
                            int pagesCount = 0;
                            string current_page_url = page.Url;
                            var sponsoreds = page.Locator("//div[starts-with(@ad_dot_url,'http')]");
                            var sponsored_count = await sponsoreds.CountAsync();
                            if (sponsored_count > 0)
                            {
                                var sponsored = sponsoreds.Nth(CommonHelper.RandomRange(0, sponsored_count));
                                await sponsored.ScrollIntoViewIfNeededAsync();
                                await Task.Delay(CommonHelper.RandomRange(800, 1200));
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
                                    await page.WaitForURLAsync(url => !url.Equals(current_page_url), new FrameWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeout });
                                }
                                catch (TimeoutException)
                                {


                                }
                                if ((this.BrowserContext.Pages.Count > pagesCount || !page.Url.StartsWith(current_page_url)))
                                {
                                    await this.UpdateTaskStatusAsync(taskid, task_url, "click", 1);
                                    this.QTPExecuteClickthrough();
                                    LogWriteLine($"{this.Title}:ExecuteWorker:Clickthrough");
                                    try
                                    {
                                        ILocator offer_item = null;
                                        if (page.Url.Contains("m.p4psearch.1688.com"))
                                        {
                                            await Task.Delay(CommonHelper.RandomRange(300, 500));
                                            var offer_items = page.Locator("//div[starts-with(@class,'offer-item')]");
                                            int offer_items_count = await offer_items.CountAsync();
                                            if (offer_items_count > 0)
                                            {
                                                offer_item = offer_items.Nth(CommonHelper.RandomRange(0, offer_items_count));
                                            }
                                        }
                                        else if (page.Url.Contains("m.1688.com"))
                                        {
                                            await Task.Delay(CommonHelper.RandomRange(300, 500));
                                            var offer_items = page.Locator("//div[starts-with(@class,'offer-item')]");
                                            int offer_items_count = await offer_items.CountAsync();
                                            if (offer_items_count > 0)
                                            {
                                                offer_item = offer_items.Nth(CommonHelper.RandomRange(0, offer_items_count));
                                            }
                                        }
                                        else if (page.Url.Contains("b2b.baidu.com"))
                                        {
                                            await Task.Delay(CommonHelper.RandomRange(300, 500));
                                            var offer_items = page.Locator("a.product-item-link");
                                            var offer_items_count = await offer_items.CountAsync();
                                            if (offer_items_count > 0)
                                            {
                                                offer_item = offer_items.Nth(CommonHelper.RandomRange(0, offer_items_count));
                                            }
                                        }
                                        else if (page.Url.Contains("uland.taobao.com"))
                                        {
                                            await Task.Delay(CommonHelper.RandomRange(300, 500));
                                            var offer_items = page.Locator("//a[starts-with(@class,'link')]");
                                            var offer_items_count = await offer_items.CountAsync();
                                            if (offer_items_count > 0)
                                            {
                                                offer_item = offer_items.Nth(CommonHelper.RandomRange(0, offer_items_count));
                                            }
                                        }

                                        if (offer_item != null)
                                        {
                                            await offer_item.ScrollIntoViewIfNeededAsync();
                                            try
                                            {
                                                pagesCount = this.BrowserContext.Pages.Count;
                                                current_page_url = page.Url;
                                                await CDPHelper.MouseClickAsync(page, cdpSession, offer_item);
                                                await page.WaitForURLAsync(url => url.Equals(current_page_url), new FrameWaitForURLOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeout });
                                                ProcessingPageElementTask(page, cdpSession);
                                            }
                                            catch (TimeoutException)
                                            {

                                            }
                                        }
                                    }
                                    catch
                                    {

                                    }
                                }

                            }
                        }
                    }
                }
                catch (Exception)
                {


                }


            });

        }
        public override async Task<(bool, bool, int)> ExecuteWorkerAsync(string taskIdentifier, JObject taskArgs)
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

            this.QTPExecuteStart();
            await this.UpdateTaskStatusAsync(taskid, task_url, "start", 1);
            LogWriteLine($"{this.Title}:ExecuteWorker:Start");
            #endregion

            var useragent = taskArgs.SelectToken("dev.ua").Value<string>();
            var os = taskArgs.SelectToken("os").Value<int>();

            var dev_sw = taskArgs.SelectToken("dev.sw").Value<int>();
            var deviceScale = CommonHelper.RandomRange(265, 270) / 1e2;

            if (dev_sw <= 720)
                deviceScale = 2;
            else if (dev_sw > 720 && dev_sw < 1200)
                deviceScale = CommonHelper.RandomRange(265, 270) / 1e2;
            else if (dev_sw >= 1200 && dev_sw < 1400)
                deviceScale = CommonHelper.RandomRange(268, 273) / 1e2;
            else if (dev_sw >= 1400)
                deviceScale = CommonHelper.RandomRange(270, 300) / 1e2;

            var sw = (int)(taskArgs.SelectToken("dev.sw").Value<int>() / deviceScale);
            var sh = (int)(taskArgs.SelectToken("dev.sh").Value<int>() / deviceScale);


            if (sw > 500 && sh < 915)
            {
                sw = 412;
                sh = 915;
            }

            if (os == 2)
            {
                sw = 428;
                sh = 926;
                deviceScale = dev_sw / (double)sw;
            }

            //428x926

            int maxTouchPoints = CommonHelper.RandomRange(4, 6);
            int page_ads_count = 0;
            bool page_trigger_click = false;
            string cachePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "File", "Cache", taskArgs.SelectToken("cacheName").Value<string>());
            var processIndex = taskArgs.SelectToken("processIndex")?.Value<int>();
            if (!processIndex.HasValue)
            {
                processIndex = 1;
            }
            //--enable-logging --v=1  --log-file="%~dp0\126.log"
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
               
                //"--enable-logging",
                //"--v=1",
                //"--log-file=E:\\workhome\\SVNRoot\\WUQIXIU_PROJECT\\SM-MUV\\126\\Build\\File\\126.log",
                 "--disable-infobars",
                "--disable-blink-features=AutomationControlled",
                "--disable-gpu",
                "--disable-popup-blocking",
                "--hide-crashed-bubble",
                //"--disk-cache-size=262144000",
                //"--media-cache-size=262144000",
                $"--disk-cache-dir={cachePath}",
                $"--user-agent={useragent}",
                $"--window-size={sw},{sh}",
                $"--window-position=0,0",
                "--ignore-certificate-errors"
            };
            ///chrome://settings/security
            //--enable-features="DnsOverHttps" --dns-over-https-mode="secure" --dns-over-https-servers="https://doh.opendns.com/dns-query"
            args.AddRange(InitFPArgs(taskArgs, maxTouchPoints));
            LogWriteLine($"args={string.Join(" ", args)}");

            using var playwright = await Playwright.CreateAsync();
            var chromium = playwright.Chromium;

            var launchOption = new BrowserTypeLaunchOptions()
            {
                // Proxy = proxySettings,
                Headless = taskArgs.SelectToken("isHiddenMode")?.Value<bool>() ?? false,
                Channel = "chrome",
                ExecutablePath = "./File/chrome-win/chrome.exe",
                ChromiumSandbox = false,
                IgnoreDefaultArgs = new List<string>()
                {
                    "--enable-automation",
                },
                Args = args,
            };
            var isProxyMode = taskArgs.SelectToken("isProxyMode")?.Value<bool>() ?? false;
            if (isProxyMode)
            {
                Proxy proxySettings = isProxyMode ? new Proxy { Server = taskArgs.SelectToken("proxy_server").Value<string>() } : null;
                launchOption.Proxy = proxySettings;
            }

            this.Browser = await chromium.LaunchAsync(launchOption);



            BrowserNewContextOptions options = new()
            {
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
            this.BrowserContext = await this.Browser.NewContextAsync(options);



            bool is_app_can_closed = false;
            this.IsCloseBrowser = false;
            this.BrowserContext.Page += (_, page) =>
            {
                page.Dialog += async (_, dialog) =>
                {
                    await dialog.DismissAsync(); // 关闭对话框
                };
                page.Crash += (_, e) =>
                {
                    is_app_can_closed = true;
                    // await CloseBrowserContext();

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
                            is_app_can_closed = true;
                             await CloseBrowserContext();
                        }
                    }
                    catch (Exception)
                    {

                    }

                };
                page.Download += async (sender, download) =>
                {
                    await download.CancelAsync(); // 取消下载
                };
            };
            int pageLoadingTimeout = taskArgs.SelectToken("pageLoadingTimeout")?.Value<int>() * 1000 ?? 30000;
            int click_rate = taskArgs.SelectToken("task.click_rate").Value<int>();
            int totalPV = taskArgs.SelectToken("totalPV").Value<int>();
            int pv = 0;

            try
            {
                IPage page = await BrowserContext.NewPageAsync();
                var cdpSession = await BrowserContext.NewCDPSessionAsync(page);
                await CDPHelper.InitCDPSession(cdpSession, maxTouchPoints);
                page.FrameNavigated += (_, frame) =>
                {
                    if (frame != page.MainFrame && frame.Url.StartsWith("https://wm.m.sm.cn"))
                    {
                 
                        if (pv++ < totalPV)
                        {
                            LogWriteLine($"{taskid}=>PV={totalPV}/{pv}");
                            _ = ADX_CLICK(taskid, task_url, click_rate, frame, cdpSession, pageLoadingTimeout);
                        }
                        else
                        {
                            is_app_can_closed = true;
                            //_ = CloseBrowserContext();
                        }

                    }
                };
                try
                {
                    await page.GotoAsync(task_url, new PageGotoOptions() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = pageLoadingTimeout });
                }
                catch (TimeoutException ex)
                {
                    LogWriteLine($"加载超时:{ex.Message}");
                }

                SpinWait.SpinUntil(() => is_app_can_closed, TimeSpan.FromSeconds(180));
                Debug.WriteLine("OK");
                //await Task.Delay(TimeSpan.FromSeconds(180));
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
