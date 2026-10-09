using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace PlaywrightHumanInput.Examples
{
    public static class UsageExample
    {
        public static async Task RunAsync(IPage page, ICDPSession cdp)
        {
            // 1) accountSeed 只负责生成“这个人长期是什么样”。
            int accountSeed = 20260819;
            var user = HumanUserProfile.CreateRandom(
                seed: accountSeed,
                handedness: HumanHandedness.Right);

            string brand = "Xiaomi";
            string model = "24129PN74C";

            // 2) 不指定 sessionSeed：每次启动都会生成新的会话随机序列。
            //    因此长期画像一致，但不会重启后重复同一套轨迹。
            var session = new HumanTouchSession(
                user,
                brand,
                model,
                desktopCdp: true);

            var human = new HumanTouchOperator(new HumanTouchOperatorOptions
            {
                Session = session,
                DelayFactor = 1.0,
                AllowBackReview = true,
                EnablePageContextAwareness = true,
                PageContextRefreshEveryGestures = 1
            });

            // 自动连续浏览：行为层会参考页面内容和当前 Session 状态。
            await human.BrowseTimesAsync(page, cdp, minTimes: 3, maxTimes: 7);

            // 显式意图：不会被页面上下文改成别的手势。
            var reading = await human.SwipeByIntentAsync(page, cdp, SwipeIntent.Reading);
            var fling = await human.SwipeByIntentAsync(page, cdp, SwipeIntent.Fling);

            if (reading != null)
            {
                var metrics = GestureTraceAnalyzer.Analyze(reading);
                Console.WriteLine(
                    $"peak@{metrics.PeakVelocityPositionRatio:P0}, " +
                    $"pathEff={metrics.PathEfficiency:0.000}, " +
                    $"latLag1={metrics.LateralResidualLag1Autocorrelation:0.000}");
            }

            var target = page.Locator(".target");
            await human.MoveToElementAsync(page, cdp, target, maxSwipes: 8);

            var carousel = page.Locator(".swiper");
            await human.SwipeElementLeftAsync(page, cdp, carousel);
        }

        /// <summary>
        /// 测试时如果需要完全复现同一条随机序列，可以显式给 sessionSeed。
        /// 生产浏览不建议固定 sessionSeed。
        /// </summary>
        public static HumanTouchOperator CreateReplayableForTest(
            string brand,
            string model,
            int accountSeed,
            int sessionSeed)
        {
            return new HumanTouchOperator(new HumanTouchOperatorOptions
            {
                UserProfile = HumanUserProfile.CreateRandom(
                    seed: accountSeed,
                    handedness: HumanHandedness.Right),
                SessionSeed = sessionSeed,
                Brand = brand,
                Model = model,
                UseDesktopCdpDeviceProfile = true,
                DelayFactor = 1.0,
                AllowBackReview = true,
                EnablePageContextAwareness = true
            });
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
                AllowBackReview = true,
                EnablePageContextAwareness = true
            });
        }
    }
}
