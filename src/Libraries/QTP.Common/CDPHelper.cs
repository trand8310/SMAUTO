using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QTP.Common.Models;
using System.Diagnostics;

namespace QTP.Common
{
    public static class CDPHelper
    {

        public static async Task SynthesizeScrollGesture(ICDPSession cdpSession)
        {
            await cdpSession.SendAsync("Input.synthesizeScrollGesture", new Dictionary<string, object>()
            {


            });
        }
        public static async Task InitCDPSession(ICDPSession cdpSession, int maxTouchPoints)
        {

            await Task.CompletedTask;
        }

        public static async Task SetDeviceMetricsOverride(ICDPSession cdpSession, int width, int height, float deviceScaleFactor, bool mobile = false)
        {
            //try
            //{
            //    await cdpSession.SendAsync("Emulation.setDeviceMetricsOverride", new Dictionary<string, object>() {
            //        {"width",width },
            //        {"height",height },
            //        {"deviceScaleFactor",deviceScaleFactor },
            //        {"mobile",mobile },
            //    });
            //}
            //catch (Exception)
            //{
            //}
            await Task.CompletedTask;

        }

        public static async Task SetUserAgentOverride(ICDPSession cdpSession,
            string userAgent,
            string platform = "Windows",
            string? platformVersion = null,
            JToken? brands = null,
            JToken? fullVersionList = null)
        {
            try
            {

                var userAgentMetadata = new Dictionary<string, object>
                {
                    //["brands"] = brands ?? [],
                    //["fullVersionList"] = fullVersionList ?? [],
                    ["platform"] = "Windows",
                    ["platformVersion"] = platformVersion ?? "",
                    ["architecture"] = "x86",
                    ["bitness"] = "64",
                    ["model"] = "",
                    ["mobile"] = false,
                    ["wow64"] = false
                };
                if (brands != null && brands.Count() > 0)
                {
                    userAgentMetadata["brands"] = brands
                        .Children<JObject>()
                        .Select(item => item.Properties().ToDictionary(
                            property => property.Name,
                            property => (object)property.Value.ToString()))
                        .ToArray();

                }
                if (fullVersionList != null && fullVersionList.Count() > 0)
                {
                    userAgentMetadata["fullVersionList"] = fullVersionList
                        .Children<JObject>()
                        .Select(item => item.Properties().ToDictionary(
                            property => property.Name,
                            property => (object)property.Value.ToString()))
                        .ToArray();
                }

                var parameters = new Dictionary<string, object>
                {
                    ["userAgent"] = userAgent,
                    ["acceptLanguage"] = "zh-CN,en-US",
                    ["platform"] = "Win32",
                    ["userAgentMetadata"] = userAgentMetadata
                };
                await cdpSession.SendAsync("Emulation.setUserAgentOverride", parameters);
            }
            catch (Exception)
            {
            }
        }

        public static async Task SetGeolocationOverride(ICDPSession cdpSession, double latitude, double longitude)
        {
            try
            {
                await cdpSession.SendAsync("Emulation.setGeolocationOverride", new Dictionary<string, object>()
                {
                     {"latitude",latitude },
                     {"longitude",longitude },
                });
            }
            catch (Exception)
            {
            }
        }

        public static async Task SetTimezoneOverride(ICDPSession cdpSession, string timezoneId)
        {
            try
            {
                await cdpSession.SendAsync("Emulation.setTimezoneOverride", new Dictionary<string, object>()
                {
                     {"timezoneId",timezoneId }
                });
            }
            catch (Exception)
            {
            }

        }

        public static async Task SetAutoDarkModeOverride(ICDPSession cdpSession, bool enabled)
        {
            try
            {
                await cdpSession.SendAsync("Emulation.setAutoDarkModeOverride", new Dictionary<string, object>()
                {
                     {"enabled",enabled }
                });
            }
            catch (Exception)
            {
            }

        }
        public static async Task SetBrowserPermission(ICDPSession cdpSession)
        {
            try
            {
                //var originUri = new Uri(url);
                //var origin = $"{originUri.Scheme}://{originUri.Host}";
                await cdpSession.SendAsync("Browser.setPermission", new Dictionary<string, object>
                {
                    ["permission"] = new Dictionary<string, object>
                    {
                        ["name"] = "geolocation"
                    },
                    ["setting"] = "granted"
                });
            }
            catch (Exception)
            {
            }

        }
        public static async Task SetScrollbarsHidden(ICDPSession cdpSession, bool hidden = true)
        {
            try
            {
                await cdpSession.SendAsync("Emulation.setScrollbarsHidden", new Dictionary<string, object>() {
                    {"hidden",hidden },
                });
            }
            catch (Exception)
            {
            }

        }

        public static async Task SetTouchEmulationEnabled(ICDPSession cdpSession, bool enabled = true, int maxTouchPoints = 1)
        {
            try
            {
                await cdpSession.SendAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object>() {
                {"enabled",enabled },
                {"maxTouchPoints",maxTouchPoints },
            });
            }
            catch (Exception)
            {

            }

        }

        public static async Task SetEmitTouchEventsForMouse(ICDPSession cdpSession, bool enabled = true, string configuration = "mobile")
        {
            try
            {
                await cdpSession.SendAsync("Emulation.setEmitTouchEventsForMouse", new Dictionary<string, object>() {
                {"enabled",enabled },
                {"configuration",configuration},
            });
            }
            catch (Exception)
            {

            }
        }

        public static async Task ClearDataForOriginAsync(ICDPSession cdpSession, string origin = "*", string storageTypes = "cache_storage,cookies,local_storage")
        {
            try
            {
                var dict = new System.Collections.Generic.Dictionary<string, object>();
                dict.Add("origin", origin);
                dict.Add("storageTypes", storageTypes);
                await cdpSession.SendAsync("Storage.clearDataForOrigin", dict);
            }
            catch (Exception)
            {

            }
        }

        public static async Task<bool> MouseClickAsync(IPage page, ICDPSession cdpSession, ILocator element, int dir = 0, int timeout = 5000, Action<string>? action = null)
        {
            try
            {
                var bounding = await element.BoundingBoxAsync();
                if (bounding != null)
                {
                    var xmin = 20;
                    var xmax = 80;
                    var ymin = 20;
                    var ymax = 80;
                    if (dir == 1)
                    {
                        //靠上
                        ymin = 15;
                        ymax = 45;
                    }
                    else if (dir == 2)
                    {
                        //居中
                        ymin = 45;
                        ymax = 65;
                    }
                    else if (dir == 3)
                    {
                        //靠下
                        ymin = 55;
                        ymax = 85;
                    }

                    var x = bounding.Width * (CommonHelper.RandomRange(xmin, xmax) * 0.01);
                    var y = bounding.Height * (CommonHelper.RandomRange(ymin, ymax) * 0.01);
                    if (x == 0)
                        x = (double)(bounding.Width / 2.0);
                    if (y == 0)
                        y = (double)(bounding.Height / 2.0);

                    action?.Invoke($"MouseClick::bounding={JsonConvert.SerializeObject(bounding)},action:x={x},y={y}");
                    await element.ClickAsync(new LocatorClickOptions() { Position = new Position() { X = (float)x, Y = (float)y }, Force = true, Timeout = timeout });
                    //action?.Invoke($"Tap:bounding={JsonConvert.SerializeObject(bounding)},action:x={x},y={y}");
                    //await element.TapAsync(new LocatorTapOptions() { Position = new Position() { X = (float)x, Y = (float)y }, Force = true, Timeout = 5000 });
                    return true;
                }
            }
            catch (TimeoutException)
            {
                action?.Invoke($"MouseClick:超时");
            }
            catch
            {
                throw;
            }
            finally
            {

            }
            return false;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="page"></param>
        /// <param name="cdpSession"></param>
        /// <param name="element"></param>
        /// <param name="dir"></param>
        /// <returns></returns>
        public static async Task<bool> MouseClickAsync(IPage page, ICDPSession cdpSession, IElementHandle element, int dir = 0, int timeout = 5000, Action<string>? action = null)
        {
            try
            {
                var bounding = await element.BoundingBoxAsync();
                if (bounding != null)
                {

                    var xmin = 15;
                    var xmax = 85;
                    var ymin = 15;
                    var ymax = 85;
                    if (dir == 1)
                    {
                        //靠上
                        ymin = 15;
                        ymax = 45;
                    }
                    else if (dir == 2)
                    {
                        //居中
                        ymin = 45;
                        ymax = 65;
                    }
                    else if (dir == 3)
                    {
                        //靠下
                        ymin = 55;
                        ymax = 85;
                    }

                    var x = bounding.Width * (CommonHelper.RandomRange(xmin, xmax) * 0.01);
                    var y = bounding.Height * (CommonHelper.RandomRange(ymin, ymax) * 0.01);
                    if (x == 0)
                        x = (double)(bounding.Width / 2.0);
                    if (y == 0)
                        y = (double)(bounding.Height / 2.0);

                    action?.Invoke($"MouseClick::bounding={JsonConvert.SerializeObject(bounding)},action:x={x},y={y}");
                    await element.ClickAsync(new ElementHandleClickOptions() { Position = new Position() { X = (float)x, Y = (float)y }, Force = true, Timeout = timeout });

                    //action?.Invoke($"Tap:bounding={JsonConvert.SerializeObject(bounding)},action:x={x},y={y}");
                    //await element.TapAsync(new ElementHandleTapOptions() { Position = new Position() { X = (float)x, Y = (float)y }, Force = true, Timeout = timeout });
                    return true;
                }
            }
            catch (TimeoutException)
            {
                action?.Invoke($"MouseClick:超时");
            }
            catch
            {
                throw;
            }
            finally
            {

            }
            return false;
        }


        #region 陀螺仪操作

        /// <summary>
        /// 修改浏览器的方向(陀螺仪)
        /// </summary>
        /// <param name="driver">Chrome驱动</param>
        /// <param name="alpha">在围绕z轴旋转时（即左右旋转时),y轴的度数差；是一个介于0到360之间的浮点数</param>
        /// <param name="beta">在围绕x轴旋转时（即前后旋转时）,z轴的度数差；是一个介于-180到180之间的浮点数</param>
        /// <param name="gamma">在围绕y轴旋转时（即扭转设备时)，z轴的度数差；是一个介于-90到90之间的浮点数</param>
        public static async Task ChangeDeviceOrientationAsync(ICDPSession cdpSession, int alpha, int beta, int gamma)
        {
            var args = new System.Collections.Generic.Dictionary<string, object>();
            args.Add("alpha", alpha);
            args.Add("beta", beta);
            args.Add("gamma", gamma);
            await cdpSession.SendAsync("DeviceOrientation.setDeviceOrientationOverride", args);
        }

        /// <summary>
        /// 随机修改浏览器的方向(陀螺仪)
        /// </summary>
        /// <param name="driver">Chrome驱动</param>
        public static async Task ChangeDeviceOrientationAsync(ICDPSession cdpSession)
        {
            try
            {

                var args = new System.Collections.Generic.Dictionary<string, object>();
                args.Add("alpha", new Random().Next(0, 10));
                args.Add("beta", new Random().Next(10, 75));
                args.Add("gamma", new Random().Next(-30, 30));
                await cdpSession.SendAsync("DeviceOrientation.setDeviceOrientationOverride", args);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

        }

        public static async Task ClearDeviceOrientationOverrideAsync(ICDPSession cdpSession)
        {
            try
            {

                var args = new System.Collections.Generic.Dictionary<string, object>();
                args.Add("alpha", 0);
                args.Add("beta", 90);
                args.Add("gamma", 0);
                await cdpSession.SendAsync("DeviceOrientation.clearDeviceOrientationOverride", args);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

        }
        #endregion 陀螺仪操作


    }
}
