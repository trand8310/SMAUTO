using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput
{
    public sealed class HumanTouchOperatorOptions
    {
        /// <summary>
        /// 优先级最高。显式提供 Session 后，Brand/Model/UserProfile 不再用于创建 Session。
        /// </summary>
        public HumanTouchSession? Session { get; set; }

        /// <summary>Session 为空时可直接提供用户 Profile。</summary>
        public HumanUserProfile? UserProfile { get; set; }

        /// <summary>Session 为空时，可直接传入你设备参数中的 Brand。</summary>
        public string? Brand { get; set; }

        /// <summary>Session 为空时，可直接传入你设备参数中的 Model。</summary>
        public string? Model { get; set; }

        /// <summary>
        /// true：Windows Chromium + Playwright/CDP 模拟 Android，使用保守 CDP 时序包络。
        /// false：按 Android 设备 Profile 的原始采样范围生成。
        /// </summary>
        public bool UseDesktopCdpDeviceProfile { get; set; } = true;

        public bool AllowBackReview { get; set; } = true;
        public double DelayFactor { get; set; } = 1.0;
        public Action<string>? Log { get; set; }
    }

    public sealed class HumanTouchOperator
    {
        private readonly HumanBehaviorModel _behavior;

        public HumanTouchOperator(HumanTouchOperatorOptions? options = null)
        {
            Options = options ?? new HumanTouchOperatorOptions();

            if (Options.Session == null)
            {
                bool hasDeviceIdentity =
                    !string.IsNullOrWhiteSpace(Options.Brand) ||
                    !string.IsNullOrWhiteSpace(Options.Model);

                Options.Session = hasDeviceIdentity
                    ? new HumanTouchSession(
                        Options.UserProfile,
                        Options.Brand,
                        Options.Model,
                        desktopCdp: Options.UseDesktopCdpDeviceProfile)
                    : new HumanTouchSession(Options.UserProfile);
            }

            Engine = new HumanTouchEngine(Options.Session);
            _behavior = new HumanBehaviorModel();
        }

        public HumanTouchOperatorOptions Options { get; }
        public HumanTouchEngine Engine { get; }
        public HumanTouchSession Session => Engine.Session;

        public async Task<HumanSwipeTrace?> BrowseOnceAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default)
        {
            var intent = _behavior.DecideNextIntent(Session, Options.AllowBackReview);
            return await SwipeByIntentAsync(page, cdp, intent, cancellationToken);
        }

        public Task<HumanSwipeTrace?> SwipeByIntentAsync(IPage page, ICDPSession cdp, SwipeIntent intent, CancellationToken cancellationToken = default)
        {
            var request = RequestForIntent(intent);
            request.Log = Options.Log;
            return Engine.SwipeAsync(page, cdp, request, cancellationToken);
        }

        public async Task<List<HumanSwipeTrace>> BrowseTimesAsync(IPage page, ICDPSession cdp, int minTimes = 2, int maxTimes = 5, CancellationToken cancellationToken = default)
        {
            minTimes = Math.Max(0, minTimes);
            maxTimes = Math.Max(minTimes, maxTimes);
            int count = RandomMath.NextInt(Session.Random, minTimes, maxTimes);
            var traces = new List<HumanSwipeTrace>();

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trace = await BrowseOnceAsync(page, cdp, cancellationToken);
                if (trace != null) traces.Add(trace);
                if (page.IsClosed) break;
                var delay = _behavior.DecideObserveDelay(Session, trace?.Intent ?? SwipeIntent.Preview, Options.DelayFactor);
                await Task.Delay(delay, cancellationToken);
                Session.RecordObserve(delay);
            }
            return traces;
        }

        public async Task<List<HumanSwipeTrace>> BrowseForAsync(IPage page, ICDPSession cdp, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            var traces = new List<HumanSwipeTrace>();
            var end = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < end && !page.IsClosed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var trace = await BrowseOnceAsync(page, cdp, cancellationToken);
                if (trace != null) traces.Add(trace);

                TimeSpan remaining = end - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                var delay = _behavior.DecideObserveDelay(Session, trace?.Intent ?? SwipeIntent.Preview, Options.DelayFactor);
                if (delay > remaining) delay = remaining;
                await Task.Delay(delay, cancellationToken);
                Session.RecordObserve(delay);
            }
            return traces;
        }

        public async Task<List<HumanSwipeTrace>> RandomUpUntilStopAsync(
        IPage page,
        ICDPSession cdp,
        int minTimes = 2,
        int maxTimes = 8,
        CancellationToken cancellationToken = default)
        {
            minTimes = Math.Max(0, minTimes);
            maxTimes = Math.Max(minTimes, maxTimes);

            int count = RandomMath.NextInt(
                Session.Random,
                minTimes,
                maxTimes);

            var traces = new List<HumanSwipeTrace>();

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var intent =
                    _behavior.DecideNextIntent(
                        Session,
                        allowBackReview: false);

                // 这个方法必须只向上
                if (intent == SwipeIntent.BackReview)
                    intent = SwipeIntent.Preview;

                var trace = await SwipeByIntentAsync(
                    page,
                    cdp,
                    intent,
                    cancellationToken);

                // 页面到底、滑不动
                if (trace == null)
                    break;

                traces.Add(trace);

                var delay =
                    _behavior.DecideObserveDelay(
                        Session,
                        trace.Intent,
                        Options.DelayFactor);

                await Task.Delay(
                    delay,
                    cancellationToken);

                Session.RecordObserve(delay);
            }

            return traces;
        }





        public Task<List<HumanSwipeTrace>> MoveToElementAsync(IPage page, ICDPSession cdp, ILocator locator, int maxSwipes = 10, CancellationToken cancellationToken = default)
            => Engine.SwipeToElementAsync(page, cdp, locator, maxSwipes, cancellationToken: cancellationToken);

        public Task<List<HumanSwipeTrace>> MoveToElementVisibleAsync(IPage page, ICDPSession cdp, ILocator locator, int maxSwipes = 10, CancellationToken cancellationToken = default)
            => Engine.SwipeToElementVisibleAsync(page, cdp, locator, maxSwipes, cancellationToken: cancellationToken);

        public Task<HumanSwipeTrace?> SwipeElementLeftAsync(IPage page, ICDPSession cdp, ILocator locator, CancellationToken cancellationToken = default)
            => Engine.SwipeInsideElementAsync(page, cdp, locator, new HumanTouchRequest { Direction = HumanSwipeDirection.Left, Intent = SwipeIntent.Preview }, cancellationToken);

        public Task<HumanSwipeTrace?> SwipeElementRightAsync(IPage page, ICDPSession cdp, ILocator locator, CancellationToken cancellationToken = default)
            => Engine.SwipeInsideElementAsync(page, cdp, locator, new HumanTouchRequest { Direction = HumanSwipeDirection.Right, Intent = SwipeIntent.Preview }, cancellationToken);

        public HumanTouchRequest RequestForIntent(SwipeIntent intent)
        {
            var r = Session.Random;
            return intent switch
            {
                SwipeIntent.Reading => new HumanTouchRequest { Direction = HumanSwipeDirection.Up, Intent = intent, SpeedFactor = RandomMath.NextDouble(r, 0.88, 1.08), ScrollChangedMinDelta = 4 },
                SwipeIntent.Preview => new HumanTouchRequest { Direction = HumanSwipeDirection.Up, Intent = intent, SpeedFactor = RandomMath.NextDouble(r, 0.95, 1.16), ScrollChangedMinDelta = 7 },
                SwipeIntent.MicroAdjust => new HumanTouchRequest { Direction = HumanSwipeDirection.Up, Intent = intent, SpeedFactor = RandomMath.NextDouble(r, 0.85, 1.08), ScrollChangedMinDelta = 2 },
                SwipeIntent.BackReview => new HumanTouchRequest { Direction = HumanSwipeDirection.Down, Intent = intent, SpeedFactor = RandomMath.NextDouble(r, 0.90, 1.10), ScrollChangedMinDelta = 5 },
                SwipeIntent.FastScan => new HumanTouchRequest { Direction = HumanSwipeDirection.Up, Intent = intent, FlingStrength = RandomMath.Chance(r, 0.32) ? FlingStrength.VeryStrong : FlingStrength.Strong, SpeedFactor = RandomMath.NextDouble(r, 1.00, 1.15), ScrollChangedMinDelta = 12 },
                SwipeIntent.Fling => new HumanTouchRequest { Direction = HumanSwipeDirection.Up, Intent = intent, FlingStrength = PickFlingStrength(), SpeedFactor = RandomMath.NextDouble(r, 0.95, 1.10), ScrollChangedMinDelta = 10 },
                _ => new HumanTouchRequest { Direction = HumanSwipeDirection.Up, Intent = SwipeIntent.Preview }
            };
        }

        private FlingStrength PickFlingStrength()
        {
            double x = Session.Random.NextDouble();
            if (x < 0.16) return FlingStrength.Soft;
            if (x < 0.72) return FlingStrength.Normal;
            if (x < 0.94) return FlingStrength.Strong;
            return FlingStrength.VeryStrong;
        }
    }
}
