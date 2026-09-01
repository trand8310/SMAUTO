using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput
{
    // 兼容旧项目的主要参数。内部只映射到新引擎，不保留旧轨迹实现。
    public sealed class HumanSwipeStyleProfile
    {
        public HumanUserProfile UserProfile { get; init; } = HumanUserProfile.CreateRandom();
        public string ProfileId => UserProfile.ProfileId;
        public int Seed => UserProfile.Seed;
        public double SpeedBias => UserProfile.SpeedBias;
        public double CurveBias => UserProfile.CurveBias;
        public double JitterBias => UserProfile.TremorBias;
        public double DriftBias => UserProfile.DriftBias;
        public double ForceBias => UserProfile.ForceBias;
        public double TouchAreaBias => UserProfile.TouchAreaBias;
        public double PauseBias => UserProfile.PauseBias;
        public double HesitationBias => UserProfile.HesitationBias;
        public double PullBackBias => UserProfile.PullBackBias;
        public double DistanceBias => UserProfile.DistanceBias;
        public double VerticalCenterXRatio => UserProfile.VerticalCenterXRatio;
        public double HorizontalCenterYRatio => UserProfile.HorizontalCenterYRatio;
        public double StartHoldBias => UserProfile.ReactionBias;
        public double EndHoldBias => UserProfile.PauseBias;
        public static HumanSwipeStyleProfile CreateRandom() => new();
    }

    public sealed class HumanBrowseSessionState
    {
        public HumanBrowseSessionState()
        {
            StyleProfile = HumanSwipeStyleProfile.CreateRandom();
            Core = new HumanTouchSession(StyleProfile.UserProfile);
        }

        internal HumanTouchSession Core { get; }
        public HumanSwipeStyleProfile StyleProfile { get; }
        public int SwipeCount => Core.SwipeCount;
        public int ConsecutiveUpCount => Core.ConsecutiveUpCount;
        public int ConsecutiveDownCount => Core.ConsecutiveDownCount;
        public double Fatigue => Core.Fatigue;
        public HumanSwipeMode LastMode => GesturePlanner.IntentToMode(Core.LastIntent);
        public HumanSwipeDirection LastDirection => Core.LastDirection;
        public DateTime LastActionTime => Core.LastActionUtc;
    }

    public sealed class HumanSwipeOptions
    {
        public HumanSwipeDirection Direction { get; set; } = HumanSwipeDirection.Up;
        public HumanSwipeMode Mode { get; set; } = HumanSwipeMode.Preview;
        public double SpeedFactor { get; set; } = 1.0;
        public bool UseBezierCurve { get; set; } = true; // 兼容保留；新引擎使用人体曲率模型。
        public bool UseJitter { get; set; } = true;
        public bool HoldBeforeMove { get; set; } = true;
        public bool? HoldBeforeEnd { get; set; }
        public int? StartX { get; set; }
        public int? StartY { get; set; }
        public int? EndX { get; set; }
        public int? EndY { get; set; }
        public int? DistancePx { get; set; }
        public int? Steps { get; set; }
        public int SafeMargin { get; set; } = 24;
        public double MaxJitter { get; set; } = 1.8; // 保留签名；实际幅度由 UserProfile 控制。
        public int CrossAxisJitter { get; set; } = 10;
        public bool UseSmoothJitter { get; set; } = true;
        public bool EnableCrossAxisDrift { get; set; } = true;
        public double? MaxCrossAxisDriftPx { get; set; }
        public bool EnableForceCurve { get; set; } = true;
        public bool EnableTouchAreaCurve { get; set; } = true;
        public bool EnableHesitationPause { get; set; } = true;
        public double? HesitationChance { get; set; }
        public int MinHesitationMs { get; set; } = 80;
        public int MaxHesitationMs { get; set; } = 260;
        public bool EnableEndPullBack { get; set; } = true;
        public double? EndPullBackChance { get; set; }
        public int MinPullBackPx { get; set; } = 2;
        public int MaxPullBackPx { get; set; } = 8;
        public bool EnableVisualConfirmPause { get; set; } = true;
        public int MinVisualConfirmMs { get; set; } = 180;
        public int MaxVisualConfirmMs { get; set; } = 680;
        public int NearTargetExtraPauseMs { get; set; } = 300;
        public bool CheckScrollableBeforeSwipe { get; set; } = true;
        public bool VerifyScrollChanged { get; set; } = true;
        public bool AllowNativeScrollFallback { get; set; } = false;
        public bool UseEventTimestamp { get; set; } = true;
        public double ScrollChangedMinDelta { get; set; } = 8;
        public int MaxPathTry { get; set; } = 10;
        public Action<string>? Log { get; set; }
        public HumanSwipeStyleProfile? StyleProfile { get; set; }
    }

    public sealed class HumanSwipeOperatorOptions
    {
        public int MinDelayAfterSwipeMs { get; set; } = 450;
        public int MaxDelayAfterSwipeMs { get; set; } = 1600;
        public double ReadingChance { get; set; } = 0.10;
        public double PreviewChance { get; set; } = 0.38;
        public double FlingNormalChance { get; set; } = 0.28;
        public double FlingStrongChance { get; set; } = 0.14;
        public double FlingVeryStrongChance { get; set; } = 0.05;
        public double MicroChance { get; set; } = 0.03;
        public double BackReviewChance => Math.Max(0, 1.0 - ReadingChance - PreviewChance - FlingNormalChance - FlingStrongChance - FlingVeryStrongChance - MicroChance);
        public int DefaultViewportWidth { get; set; } = 390;
        public int DefaultViewportHeight { get; set; } = 800;
        public HumanBrowseSessionState? Session { get; set; }
        public bool UseSessionBrowseModel { get; set; } = true;
        public double DelayFactor { get; set; } = 1.0;
        public bool AllowNativeScrollFallback { get; set; } = false;
        public bool EnableFatigueDelay { get; set; } = true;
        public bool AllowBackReview { get; set; } = true;
        public double LongObserveChance { get; set; } = 0.10;
        public Action<string>? Log { get; set; }
    }

    public readonly struct HumanBrowseDurationRange
    {
        public HumanBrowseDurationRange(TimeSpan minDuration, TimeSpan maxDuration)
        {
            if (minDuration < TimeSpan.Zero) minDuration = TimeSpan.Zero;
            if (maxDuration < minDuration) maxDuration = minDuration;
            MinDuration = minDuration;
            MaxDuration = maxDuration;
        }
        public TimeSpan MinDuration { get; }
        public TimeSpan MaxDuration { get; }
        public static HumanBrowseDurationRange FromMilliseconds(int min, int max) => new(TimeSpan.FromMilliseconds(Math.Max(0, min)), TimeSpan.FromMilliseconds(Math.Max(min, max)));
        public static HumanBrowseDurationRange FromSeconds(int min, int max) => new(TimeSpan.FromSeconds(Math.Max(0, min)), TimeSpan.FromSeconds(Math.Max(min, max)));
        public static HumanBrowseDurationRange FromMinutes(double min, double max) => new(TimeSpan.FromMinutes(Math.Max(0, min)), TimeSpan.FromMinutes(Math.Max(min, max)));
        public static HumanBrowseDurationRange Fixed(TimeSpan duration) => new(duration, duration);
    }

    public static class HumanSwipeEmulator
    {
        public static bool AllowNativeScrollFallbackByDefault { get; set; } = false;

        public static Task EnableTouchInputAsync(IPage page, ICDPSession cdp)
            => new CdpTouchDispatcher().EnableAsync(page, cdp, TouchDeviceProfile.GenericAndroid());

        public static Task<HumanSwipeTrace?> BrowseSwipeAsync(IPage page, ICDPSession cdp, HumanBrowseSessionState session, HumanSwipeOptions? baseOptions = null, CancellationToken cancellationToken = default)
            => CreateEngine(session).SwipeAsync(page, cdp, Map(baseOptions ?? new HumanSwipeOptions()), cancellationToken);

        public static Task<HumanSwipeTrace?> SwipeAsync(IPage page, ICDPSession cdp, HumanSwipeOptions? options = null, CancellationToken cancellationToken = default)
            => CreateEngine(null, options?.StyleProfile).SwipeAsync(page, cdp, Map(options ?? new HumanSwipeOptions()), cancellationToken);

        public static Task<HumanSwipeTrace?> SwipeInsideElementAsync(IPage page, ICDPSession cdp, ILocator locator, HumanSwipeOptions options, CancellationToken cancellationToken = default)
            => CreateEngine(null, options.StyleProfile).SwipeInsideElementAsync(page, cdp, locator, Map(options), cancellationToken);

        public static Task<HumanSwipeTrace?> SwipeInsideElementAsync(IPage page, ICDPSession cdp, IElementHandle element, HumanSwipeOptions options, CancellationToken cancellationToken = default)
            => CreateEngine(null, options.StyleProfile).SwipeInsideElementAsync(page, cdp, element, Map(options), cancellationToken);

        public static Task<List<HumanSwipeTrace>> SwipeToElementAsync(IPage page, ICDPSession cdp, ILocator locator, int maxSwipes = 10, float comfortTopRatio = 0.22f, float comfortBottomRatio = 0.72f, CancellationToken cancellationToken = default)
            => CreateEngine().SwipeToElementAsync(page, cdp, locator, maxSwipes, comfortTopRatio, comfortBottomRatio, cancellationToken);

        public static Task<List<HumanSwipeTrace>> SwipeToElementVisibleAsync(IPage page, ICDPSession cdp, ILocator locator, int maxSwipes = 10, float visibleMarginPx = 8f, CancellationToken cancellationToken = default)
            => CreateEngine().SwipeToElementVisibleAsync(page, cdp, locator, maxSwipes, visibleMarginPx, cancellationToken);

        public static Task<ElementRect?> GetElementRectAsync(ILocator locator) => new ScrollTargetResolver().GetElementRectAsync(locator);
        public static Task<ElementRect?> GetElementRectAsync(IElementHandle element) => new ScrollTargetResolver().GetElementRectAsync(element);

        internal static HumanTouchRequest Map(HumanSwipeOptions o)
        {
            SwipeIntent intent = o.Mode switch
            {
                HumanSwipeMode.Reading => SwipeIntent.Reading,
                HumanSwipeMode.Fling => SwipeIntent.Fling,
                HumanSwipeMode.Micro => SwipeIntent.MicroAdjust,
                _ => SwipeIntent.Preview
            };
            return new HumanTouchRequest
            {
                Direction = o.Direction,
                Intent = intent,
                SpeedFactor = o.SpeedFactor,
                DistancePx = o.DistancePx,
                Steps = o.Steps,
                StartX = o.StartX,
                StartY = o.StartY,
                EndX = o.EndX,
                EndY = o.EndY,
                SafeMargin = o.SafeMargin,
                CheckScrollableBeforeSwipe = o.CheckScrollableBeforeSwipe,
                VerifyScrollChanged = o.VerifyScrollChanged,
                ScrollChangedMinDelta = o.ScrollChangedMinDelta,
                EnableHesitation = o.EnableHesitationPause,
                HesitationChance = o.HesitationChance,
                EnablePullBack = o.EnableEndPullBack,
                PullBackChance = o.EndPullBackChance,
                HoldBeforeMove = o.HoldBeforeMove,
                HoldBeforeEnd = o.HoldBeforeEnd,
                Log = o.Log
            };
        }

        private static HumanTouchEngine CreateEngine(HumanBrowseSessionState? session = null, HumanSwipeStyleProfile? style = null)
        {
            if (session != null) return new HumanTouchEngine(session.Core);
            var core = style != null ? new HumanTouchSession(style.UserProfile) : new HumanTouchSession();
            return new HumanTouchEngine(core);
        }
    }

    public static class HumanSwipeOperator
    {
        public static Task<HumanSwipeTrace?> BrowseOnceAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions? options = null, CancellationToken cancellationToken = default)
            => Create(options).BrowseOnceAsync(page, cdp, cancellationToken);

        public static Task<List<HumanSwipeTrace>> BrowseTimesAsync(IPage page, ICDPSession cdp, int minTimes = 2, int maxTimes = 5, HumanSwipeOperatorOptions? options = null, CancellationToken cancellationToken = default)
            => Create(options).BrowseTimesAsync(page, cdp, minTimes, maxTimes, cancellationToken);

        public static Task<HumanSwipeTrace?> SwipeByIntentAsync(IPage page, ICDPSession cdp, SwipeIntent intent, CancellationToken cancellationToken = default)
            => Create(null).SwipeByIntentAsync(page, cdp, intent, cancellationToken);

        public static Task<HumanSwipeTrace?> SwipeByIntentAsync(IPage page, ICDPSession cdp, SwipeIntent intent, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default)
            => Create(options).SwipeByIntentAsync(page, cdp, intent, cancellationToken);

        public static Task<HumanSwipeTrace?> ReadingUpAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => ReadingUpAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        public static Task<HumanSwipeTrace?> ReadingUpAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => Swipe(page, cdp, options, SwipeIntent.Reading, HumanSwipeDirection.Up, null, cancellationToken);
        public static Task<HumanSwipeTrace?> ReadingDownAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => ReadingDownAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        public static Task<HumanSwipeTrace?> ReadingDownAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => Swipe(page, cdp, options, SwipeIntent.Reading, HumanSwipeDirection.Down, null, cancellationToken);
        public static Task<HumanSwipeTrace?> PreviewUpAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => PreviewUpAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        public static Task<HumanSwipeTrace?> PreviewUpAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => Swipe(page, cdp, options, SwipeIntent.Preview, HumanSwipeDirection.Up, null, cancellationToken);
        public static Task<HumanSwipeTrace?> PreviewDownAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => PreviewDownAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        public static Task<HumanSwipeTrace?> PreviewDownAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => Swipe(page, cdp, options, SwipeIntent.Preview, HumanSwipeDirection.Down, null, cancellationToken);
        public static Task<HumanSwipeTrace?> FlingUpAsync(IPage page, ICDPSession cdp, FlingStrength strength = FlingStrength.Normal, CancellationToken cancellationToken = default) => FlingUpAsync(page, cdp, strength, new HumanSwipeOperatorOptions(), cancellationToken);
        public static Task<HumanSwipeTrace?> FlingUpAsync(IPage page, ICDPSession cdp, FlingStrength strength, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => Swipe(page, cdp, options, SwipeIntent.Fling, HumanSwipeDirection.Up, strength, cancellationToken);
        public static Task<HumanSwipeTrace?> FlingDownAsync(IPage page, ICDPSession cdp, FlingStrength strength = FlingStrength.Normal, CancellationToken cancellationToken = default) => FlingDownAsync(page, cdp, strength, new HumanSwipeOperatorOptions(), cancellationToken);
        public static Task<HumanSwipeTrace?> FlingDownAsync(IPage page, ICDPSession cdp, FlingStrength strength, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => Swipe(page, cdp, options, SwipeIntent.Fling, HumanSwipeDirection.Down, strength, cancellationToken);
        public static Task<HumanSwipeTrace?> LongFlingUpAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => FlingUpAsync(page, cdp, FlingStrength.VeryStrong, cancellationToken);
        public static Task<HumanSwipeTrace?> LongFlingUpAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => FlingUpAsync(page, cdp, FlingStrength.VeryStrong, options, cancellationToken);
        public static Task<HumanSwipeTrace?> LongFlingDownAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => FlingDownAsync(page, cdp, FlingStrength.VeryStrong, cancellationToken);
        public static Task<HumanSwipeTrace?> LongFlingDownAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => FlingDownAsync(page, cdp, FlingStrength.VeryStrong, options, cancellationToken);
        public static Task<HumanSwipeTrace?> MicroUpAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => Swipe(page, cdp, options, SwipeIntent.MicroAdjust, HumanSwipeDirection.Up, null, cancellationToken);
        public static Task<HumanSwipeTrace?> MicroDownAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => MicroDownAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        public static Task<HumanSwipeTrace?> MicroDownAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => Swipe(page, cdp, options, SwipeIntent.MicroAdjust, HumanSwipeDirection.Down, null, cancellationToken);
        public static Task<HumanSwipeTrace?> FastScanOnceAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => Create(null).SwipeByIntentAsync(page, cdp, SwipeIntent.FastScan, cancellationToken);
        public static Task<HumanSwipeTrace?> FastScanOnceAsync(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, CancellationToken cancellationToken = default) => Create(options).SwipeByIntentAsync(page, cdp, SwipeIntent.FastScan, cancellationToken);
        public static Task<HumanSwipeTrace?> SwipeLeftAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => Swipe(page, cdp, new HumanSwipeOperatorOptions(), SwipeIntent.Preview, HumanSwipeDirection.Left, null, cancellationToken);
        public static Task<HumanSwipeTrace?> SwipeRightAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) => Swipe(page, cdp, new HumanSwipeOperatorOptions(), SwipeIntent.Preview, HumanSwipeDirection.Right, null, cancellationToken);

        public static Task<List<HumanSwipeTrace>> MoveToElementAsync(IPage page, ICDPSession cdp, ILocator locator, int maxSwipes = 10, float comfortTopRatio = 0.22f, float comfortBottomRatio = 0.72f, CancellationToken cancellationToken = default)
            => Create(null).Engine.SwipeToElementAsync(page, cdp, locator, maxSwipes, comfortTopRatio, comfortBottomRatio, cancellationToken);

        public static Task<List<HumanSwipeTrace>> MoveToElementVisibleAsync(IPage page, ICDPSession cdp, ILocator locator, int maxSwipes = 10, float visibleMarginPx = 8f, CancellationToken cancellationToken = default)
            => Create(null).Engine.SwipeToElementVisibleAsync(page, cdp, locator, maxSwipes, visibleMarginPx, cancellationToken);

        public static Task<HumanSwipeTrace?> SwipeElementLeftAsync(IPage page, ICDPSession cdp, ILocator locator, CancellationToken cancellationToken = default)
            => Create(null).SwipeElementLeftAsync(page, cdp, locator, cancellationToken);

        public static Task<HumanSwipeTrace?> SwipeElementRightAsync(IPage page, ICDPSession cdp, ILocator locator, CancellationToken cancellationToken = default)
            => Create(null).SwipeElementRightAsync(page, cdp, locator, cancellationToken);

        public static Task<HumanSwipeTrace?> CustomAsync(IPage page, ICDPSession cdp, HumanSwipeDirection direction, HumanSwipeMode mode, int? distancePx = null, double speedFactor = 1.0, CancellationToken cancellationToken = default)
            => new HumanTouchEngine().SwipeAsync(page, cdp, new HumanTouchRequest { Direction = direction, Intent = ModeToIntent(mode), DistancePx = distancePx, SpeedFactor = speedFactor }, cancellationToken);

        private static async Task<HumanSwipeTrace?> Swipe(IPage page, ICDPSession cdp, HumanSwipeOperatorOptions options, SwipeIntent intent, HumanSwipeDirection direction, FlingStrength? strength, CancellationToken cancellationToken)
        {
            var op = Create(options);
            var req = op.RequestForIntent(intent);
            req.Direction = direction;
            if (strength.HasValue) req.FlingStrength = strength.Value;
            req.Log = options.Log;
            return await op.Engine.SwipeAsync(page, cdp, req, cancellationToken);
        }

        private static HumanTouchOperator Create(HumanSwipeOperatorOptions? legacy)
        {
            legacy ??= new HumanSwipeOperatorOptions();
            legacy.Session ??= new HumanBrowseSessionState();
            return new HumanTouchOperator(new HumanTouchOperatorOptions
            {
                Session = legacy.Session.Core,
                AllowBackReview = legacy.AllowBackReview,
                DelayFactor = legacy.DelayFactor,
                Log = legacy.Log
            });
        }

        private static SwipeIntent ModeToIntent(HumanSwipeMode mode) => mode switch
        {
            HumanSwipeMode.Reading => SwipeIntent.Reading,
            HumanSwipeMode.Fling => SwipeIntent.Fling,
            HumanSwipeMode.Micro => SwipeIntent.MicroAdjust,
            _ => SwipeIntent.Preview
        };
    }
}
