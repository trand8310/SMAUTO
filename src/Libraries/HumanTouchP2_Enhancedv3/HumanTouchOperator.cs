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

        /// <summary>
        /// Session 为空时可显式指定本次会话随机种子。
        /// 生产运行通常保持 null；测试复现时才建议固定。
        /// </summary>
        public int? SessionSeed { get; set; }

        public string? Brand { get; set; }
        public string? Model { get; set; }

        public bool UseDesktopCdpDeviceProfile { get; set; } = true;
        public bool AllowBackReview { get; set; } = true;
        public double DelayFactor { get; set; } = 1.0;

        /// <summary>
        /// 启用低成本 DOM 页面上下文，让 Reading/Fling/Preview 的选择与当前内容略有关联。
        /// </summary>
        public bool EnablePageContextAwareness { get; set; } = true;

        /// <summary>
        /// 每多少次自动浏览重新采集一次页面上下文。1 表示每次都采集。
        /// </summary>
        public int PageContextRefreshEveryGestures { get; set; } = 1;

        public Action<string>? Log { get; set; }
    }

    public sealed class HumanTouchOperator
    {
        private readonly HumanBehaviorModel _behavior;
        private readonly PageContextAnalyzer _pageContextAnalyzer;
        private PageContextSnapshot? _lastPageContext;
        private int _browseDecisionCount;

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
                        desktopCdp: Options.UseDesktopCdpDeviceProfile,
                        sessionSeed: Options.SessionSeed)
                    : new HumanTouchSession(
                        Options.UserProfile,
                        deviceProfile: null,
                        sessionSeed: Options.SessionSeed);
            }

            Engine = new HumanTouchEngine(Options.Session);
            _behavior = new HumanBehaviorModel();
            _pageContextAnalyzer = new PageContextAnalyzer();
        }

        public HumanTouchOperatorOptions Options { get; }
        public HumanTouchEngine Engine { get; }
        public HumanTouchSession Session => Engine.Session;
        public PageContextSnapshot? LastPageContext => _lastPageContext;

        public Task<bool> TapAsync(IPage page, ICDPSession cdp, ILocator target,
            CancellationToken cancellationToken = default)
            => Engine.TapAsync(page, cdp, target, log: Options.Log, cancellationToken: cancellationToken);

        public Task<bool> TapAsync(IPage page, ICDPSession cdp, IElementHandle target,
            CancellationToken cancellationToken = default)
            => Engine.TapAsync(page, cdp, target, log: Options.Log, cancellationToken: cancellationToken);

        public async Task<HumanSwipeTrace?> BrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            var context = await GetPageContextForDecisionAsync(page, cancellationToken);
            var intent = _behavior.DecideNextIntent(
                Session,
                Options.AllowBackReview,
                context);

            return await SwipeByIntentAsync(
                page,
                cdp,
                intent,
                cancellationToken);
        }

        public Task<HumanSwipeTrace?> SwipeByIntentAsync(
            IPage page,
            ICDPSession cdp,
            SwipeIntent intent,
            CancellationToken cancellationToken = default)
        {
            var request = RequestForIntent(intent);
            request.Log = Options.Log;
            return Engine.SwipeAsync(page, cdp, request, cancellationToken);
        }

        public async Task<List<HumanSwipeTrace>> BrowseTimesAsync(
            IPage page,
            ICDPSession cdp,
            int minTimes = 2,
            int maxTimes = 5,
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

                var trace = await BrowseOnceAsync(
                    page,
                    cdp,
                    cancellationToken);

                if (trace != null)
                    traces.Add(trace);

                if (page.IsClosed)
                    break;

                var delay = _behavior.DecideObserveDelay(
                    Session,
                    trace?.Intent ?? SwipeIntent.Preview,
                    Options.DelayFactor,
                    _lastPageContext);

                await Task.Delay(delay, cancellationToken);
                Session.RecordObserve(delay);
            }

            return traces;
        }

        public async Task<List<HumanSwipeTrace>> BrowseForAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            var traces = new List<HumanSwipeTrace>();
            var clock = System.Diagnostics.Stopwatch.StartNew();

            while (clock.Elapsed < duration && !page.IsClosed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var trace = await BrowseOnceAsync(
                    page,
                    cdp,
                    cancellationToken);

                if (trace != null)
                    traces.Add(trace);

                TimeSpan remaining = duration - clock.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    break;

                var delay = _behavior.DecideObserveDelay(
                    Session,
                    trace?.Intent ?? SwipeIntent.Preview,
                    Options.DelayFactor,
                    _lastPageContext);

                if (delay > remaining)
                    delay = remaining;

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

                var context = await GetPageContextForDecisionAsync(page, cancellationToken);

                var intent = _behavior.DecideNextIntent(
                    Session,
                    allowBackReview: false,
                    context);

                if (intent == SwipeIntent.BackReview)
                    intent = SwipeIntent.Preview;

                var trace = await SwipeByIntentAsync(
                    page,
                    cdp,
                    intent,
                    cancellationToken);

                if (trace == null)
                    break;

                traces.Add(trace);

                var delay = _behavior.DecideObserveDelay(
                    Session,
                    trace.Intent,
                    Options.DelayFactor,
                    _lastPageContext);

                await Task.Delay(delay, cancellationToken);
                Session.RecordObserve(delay);
            }

            return traces;
        }

        public Task<List<HumanSwipeTrace>> MoveToElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            CancellationToken cancellationToken = default)
            => MoveToElementCoreAsync(
                page,
                cdp,
                locator,
                maxSwipes,
                cancellationToken: cancellationToken);

        private async Task<List<HumanSwipeTrace>> MoveToElementCoreAsync(IPage page, ICDPSession cdp,
            ILocator locator, int maxSwipes, CancellationToken cancellationToken)
        {
            var result = await Engine.MoveToTargetAsync(page, cdp, locator, maxSwipes, cancellationToken: cancellationToken);
            Options.Log?.Invoke($"Move target: {result.Status}, {result.Reason}");
            return new(result.Traces);
        }

        public Task<TargetMovementResult> MoveToTargetAsync(IPage page, ICDPSession cdp,
            ILocator locator, int maxSwipes = 10, CancellationToken cancellationToken = default)
            => Engine.MoveToTargetAsync(page, cdp, locator, maxSwipes, cancellationToken: cancellationToken);

        public Task<List<HumanSwipeTrace>> MoveToElementVisibleAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            CancellationToken cancellationToken = default)
            => Engine.SwipeToElementVisibleAsync(
                page,
                cdp,
                locator,
                maxSwipes,
                cancellationToken: cancellationToken);

        public Task<HumanSwipeTrace?> SwipeElementLeftAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default)
            => Engine.SwipeInsideElementAsync(
                page,
                cdp,
                locator,
                new HumanTouchRequest
                {
                    Direction = HumanSwipeDirection.Left,
                    Intent = SwipeIntent.Preview
                },
                cancellationToken);

        public Task<HumanSwipeTrace?> SwipeElementRightAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default)
            => Engine.SwipeInsideElementAsync(
                page,
                cdp,
                locator,
                new HumanTouchRequest
                {
                    Direction = HumanSwipeDirection.Right,
                    Intent = SwipeIntent.Preview
                },
                cancellationToken);

        public HumanTouchRequest RequestForIntent(SwipeIntent intent)
        {
            var r = Session.Random;

            return intent switch
            {
                SwipeIntent.Reading => new HumanTouchRequest
                {
                    Direction = HumanSwipeDirection.Up,
                    Intent = intent,
                    SpeedFactor = RandomMath.NextDouble(r, 0.88, 1.08),
                    ScrollChangedMinDelta = 4,
                    EnableSubmovements = true
                },

                SwipeIntent.Preview => new HumanTouchRequest
                {
                    Direction = HumanSwipeDirection.Up,
                    Intent = intent,
                    SpeedFactor = RandomMath.NextDouble(r, 0.95, 1.16),
                    ScrollChangedMinDelta = 7,
                    EnableSubmovements = true
                },

                SwipeIntent.MicroAdjust => new HumanTouchRequest
                {
                    Direction = HumanSwipeDirection.Up,
                    Intent = intent,
                    SpeedFactor = RandomMath.NextDouble(r, 0.85, 1.08),
                    ScrollChangedMinDelta = 2,
                    EnableSubmovements = true
                },

                SwipeIntent.BackReview => new HumanTouchRequest
                {
                    Direction = HumanSwipeDirection.Down,
                    Intent = intent,
                    SpeedFactor = RandomMath.NextDouble(r, 0.90, 1.10),
                    ScrollChangedMinDelta = 5,
                    EnableSubmovements = true
                },

                SwipeIntent.FastScan => new HumanTouchRequest
                {
                    Direction = HumanSwipeDirection.Up,
                    Intent = intent,
                    FlingStrength = RandomMath.Chance(r, 0.32)
                        ? FlingStrength.VeryStrong
                        : FlingStrength.Strong,
                    SpeedFactor = RandomMath.NextDouble(r, 1.00, 1.15),
                    ScrollChangedMinDelta = 12,
                    EnableSubmovements = true
                },

                SwipeIntent.Fling => new HumanTouchRequest
                {
                    Direction = HumanSwipeDirection.Up,
                    Intent = intent,
                    FlingStrength = PickFlingStrength(),
                    SpeedFactor = RandomMath.NextDouble(r, 0.95, 1.10),
                    ScrollChangedMinDelta = 10,
                    EnableSubmovements = true
                },

                _ => new HumanTouchRequest
                {
                    Direction = HumanSwipeDirection.Up,
                    Intent = SwipeIntent.Preview
                }
            };
        }

        private async Task<PageContextSnapshot?> GetPageContextForDecisionAsync(IPage page, CancellationToken token)
        {
            if (!Options.EnablePageContextAwareness)
                return null;

            int refreshEvery = Math.Max(1, Options.PageContextRefreshEveryGestures);
            bool shouldRefresh = _lastPageContext == null ||
                                 (_browseDecisionCount % refreshEvery) == 0;

            _browseDecisionCount++;

            if (shouldRefresh)
            {
                _lastPageContext = await _pageContextAnalyzer.CaptureAsync(page, token);
            }

            return _lastPageContext;
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
