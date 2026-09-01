using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace PlaywrightHumanInput.Examples
{
    public static class UsageExample
    {
        public static async Task RunAsync(IPage page, ICDPSession cdp)
        {
            // 一个浏览任务复用一个 Session：速度、惯用触点区域、疲劳、行为状态会连续。
            var user = HumanUserProfile.CreateRandom(seed: 20260819, handedness: HumanHandedness.Right);
            // 你的设备参数已经有 Brand + Model，直接交给设备解析器。
            string brand = "Xiaomi";
            string model = "24129PN74C";

            // 当前环境是 Windows Chromium + Playwright/CDP，推荐这个入口。
            var device = TouchDeviceProfiles.ResolveForDesktopCdp(brand, model);
            var session = new HumanTouchSession(user, device);

            // 也可以简写为：
            // var session = new HumanTouchSession(user, brand, model, desktopCdp: true);
            var human = new HumanTouchOperator(new HumanTouchOperatorOptions
            {
                Session = session,
                DelayFactor = 1.0,
                AllowBackReview = true
            });

            await human.BrowseTimesAsync(page, cdp, minTimes: 3, maxTimes: 7);
            await human.SwipeByIntentAsync(page, cdp, SwipeIntent.Reading);
            await human.SwipeByIntentAsync(page, cdp, SwipeIntent.Fling);

            var target = page.Locator(".target");
            await human.MoveToElementAsync(page, cdp, target, maxSwipes: 8);

            var carousel = page.Locator(".swiper");
            await human.SwipeElementLeftAsync(page, cdp, carousel);
        }


        public static HumanTouchOperator CreateFromDeviceParameters(
            string brand,
            string model,
            int accountSeed)
        {
            return new HumanTouchOperator(new HumanTouchOperatorOptions
            {
                UserProfile = HumanUserProfile.CreateRandom(
                    seed: accountSeed,
                    handedness: HumanHandedness.Right),
                Brand = brand,
                Model = model,
                UseDesktopCdpDeviceProfile = true,
                DelayFactor = 1.0,
                AllowBackReview = true
            });
        }
    }
}
