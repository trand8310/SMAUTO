using Microsoft.Playwright;


namespace PlaywrightHumanInput
{
    /// <summary>
    /// Fling 力度。
    /// </summary>
    public enum FlingStrength
    {
        Soft,
        Normal,
        Strong,
        VeryStrong
    }

    /// <summary>
    /// 滑动意图。
    /// </summary>
    public enum SwipeIntent
    {
        Reading,
        Preview,
        Fling,
        MicroAdjust,
        BackReview,
        FastScan
    }

    /// <summary>
    /// HumanSwipeOperator 的概率和停顿配置。
    /// </summary>
    public sealed class HumanSwipeOperatorOptions
    {
        public int MinDelayAfterSwipeMs { get; set; } = 450;
        public int MaxDelayAfterSwipeMs { get; set; } = 1600;

        /// <summary>慢速阅读概率。</summary>
        public double ReadingChance { get; set; } = 0.10;

        /// <summary>正常预览概率。</summary>
        public double PreviewChance { get; set; } = 0.38;

        /// <summary>普通甩动概率。</summary>
        public double FlingNormalChance { get; set; } = 0.28;

        /// <summary>强力甩动概率。</summary>
        public double FlingStrongChance { get; set; } = 0.14;

        /// <summary>超强甩动概率。</summary>
        public double FlingVeryStrongChance { get; set; } = 0.05;

        /// <summary>微调概率。</summary>
        public double MicroChance { get; set; } = 0.03;

        /// <summary>
        /// 剩余概率自动作为向下回看概率。
        /// 默认：1 - 上面所有概率 = 0.02。
        /// </summary>
        public double BackReviewChance
        {
            get
            {
                return Math.Max(
                    0,
                    1.0
                    - ReadingChance
                    - PreviewChance
                    - FlingNormalChance
                    - FlingStrongChance
                    - FlingVeryStrongChance
                    - MicroChance);
            }
        }

        public int DefaultViewportWidth { get; set; } = 390;
        public int DefaultViewportHeight { get; set; } = 800;
    }


    /// <summary>
    /// 时间范围配置，支持毫秒、秒、分钟。
    /// 
    /// 示例：
    /// HumanBrowseDurationRange.FromMilliseconds(30000, 90000)
    /// HumanBrowseDurationRange.FromSeconds(60, 120)
    /// HumanBrowseDurationRange.FromMinutes(1, 2)
    /// </summary>
    public readonly struct HumanBrowseDurationRange
    {
        public HumanBrowseDurationRange(TimeSpan minDuration, TimeSpan maxDuration)
        {
            if (minDuration < TimeSpan.Zero)
                minDuration = TimeSpan.Zero;

            if (maxDuration < minDuration)
                maxDuration = minDuration;

            MinDuration = minDuration;
            MaxDuration = maxDuration;
        }

        public TimeSpan MinDuration { get; }
        public TimeSpan MaxDuration { get; }

        public static HumanBrowseDurationRange FromMilliseconds(int minMilliseconds, int maxMilliseconds)
        {
            if (minMilliseconds < 0)
                minMilliseconds = 0;

            if (maxMilliseconds < minMilliseconds)
                maxMilliseconds = minMilliseconds;

            return new HumanBrowseDurationRange(
                TimeSpan.FromMilliseconds(minMilliseconds),
                TimeSpan.FromMilliseconds(maxMilliseconds));
        }

        public static HumanBrowseDurationRange FromSeconds(int minSeconds, int maxSeconds)
        {
            if (minSeconds < 0)
                minSeconds = 0;

            if (maxSeconds < minSeconds)
                maxSeconds = minSeconds;

            return new HumanBrowseDurationRange(
                TimeSpan.FromSeconds(minSeconds),
                TimeSpan.FromSeconds(maxSeconds));
        }

        public static HumanBrowseDurationRange FromMinutes(double minMinutes, double maxMinutes)
        {
            if (minMinutes < 0)
                minMinutes = 0;

            if (maxMinutes < minMinutes)
                maxMinutes = minMinutes;

            return new HumanBrowseDurationRange(
                TimeSpan.FromMinutes(minMinutes),
                TimeSpan.FromMinutes(maxMinutes));
        }

        public static HumanBrowseDurationRange Fixed(TimeSpan duration)
        {
            return new HumanBrowseDurationRange(duration, duration);
        }
    }

    /// <summary>
    /// HumanSwipeEmulator 的静态操作器。
    /// 
    /// 特点：
    /// 1. 不需要 new。
    /// 2. 不改 HumanSwipeEmulator 主类。
    /// 3. 统一封装常用滑动行为：阅读、预览、快速甩动、强力甩动、微调、回看、连续随机滑动。
    /// 4. 页面级滑动调用 HumanSwipeEmulator.SwipeAsync。
    /// 5. 元素相关调用 HumanSwipeEmulator.SwipeToElementAsync / SwipeInsideElementAsync。
    /// 
    /// 示例：
    /// await HumanSwipeOperator.BrowseOnceAsync(page, cdp);
    /// await HumanSwipeOperator.BrowseTimesAsync(page, cdp, 3, 7);
    /// await HumanSwipeOperator.FlingUpAsync(page, cdp, FlingStrength.Strong);
    /// await HumanSwipeOperator.SwipeElementLeftAsync(page, cdp, locator);
    /// </summary>
    public static class HumanSwipeOperator
    {
        private static readonly ThreadLocal<Random> RandomLocal =
            new(() => new Random(Guid.NewGuid().GetHashCode()));

        #region 单次随机/意图动作

        /// <summary>
        /// 一次随机浏览滑动。
        /// 内部按概率随机选择 Reading / Preview / Fling / Micro / BackReview。
        /// </summary>
        public static Task<HumanSwipeTrace?> BrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            double r = NextDouble();

            double p1 = options.ReadingChance;
            double p2 = p1 + options.PreviewChance;
            double p3 = p2 + options.FlingNormalChance;
            double p4 = p3 + options.FlingStrongChance;
            double p5 = p4 + options.FlingVeryStrongChance;
            double p6 = p5 + options.MicroChance;

            if (r < p1)
                return ReadingUpAsync(page, cdp, cancellationToken);

            if (r < p2)
                return PreviewUpAsync(page, cdp, cancellationToken);

            if (r < p3)
                return FlingUpAsync(page, cdp, FlingStrength.Normal, cancellationToken);

            if (r < p4)
                return FlingUpAsync(page, cdp, FlingStrength.Strong, cancellationToken);

            if (r < p5)
                return FlingUpAsync(page, cdp, FlingStrength.VeryStrong, cancellationToken);

            if (r < p6)
                return MicroUpAsync(page, cdp, cancellationToken);

            return PreviewDownAsync(page, cdp, cancellationToken);
        }

        /// <summary>
        /// 按指定意图执行一次动作。
        /// </summary>
        public static Task<HumanSwipeTrace?> SwipeByIntentAsync(
            IPage page,
            ICDPSession cdp,
            SwipeIntent intent,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            return intent switch
            {
                SwipeIntent.Reading =>
                    ReadingUpAsync(page, cdp, cancellationToken),

                SwipeIntent.Preview =>
                    PreviewUpAsync(page, cdp, cancellationToken),

                SwipeIntent.Fling =>
                    FlingUpAsync(page, cdp, PickRandomFlingStrength(), cancellationToken),

                SwipeIntent.MicroAdjust =>
                    MicroUpAsync(page, cdp, cancellationToken),

                SwipeIntent.BackReview =>
                    PreviewDownAsync(page, cdp, cancellationToken),

                SwipeIntent.FastScan =>
                    FastScanOnceAsync(page, cdp, cancellationToken),

                _ =>
                    PreviewUpAsync(page, cdp, cancellationToken)
            };
        }

        /// <summary>
        /// 从多个意图中随机选择一个执行。
        /// </summary>
        public static Task<HumanSwipeTrace?> RandomFromIntentsAsync(
            IPage page,
            ICDPSession cdp,
            IReadOnlyList<SwipeIntent> intents,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (intents == null || intents.Count == 0)
                return BrowseOnceAsync(page, cdp, cancellationToken: cancellationToken);

            var intent = intents[NextInt(0, intents.Count - 1)];
            return SwipeByIntentAsync(page, cdp, intent, cancellationToken);
        }

        #endregion

        #region 连续动作

        /// <summary>
        /// 连续随机浏览多次。
        /// 每次动作和动作后停顿都会随机。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> BrowseTimesAsync(
            IPage page,
            ICDPSession cdp,
            int minTimes = 2,
            int maxTimes = 5,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            var traces = new List<HumanSwipeTrace>();
            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await BrowseOnceAsync(page, cdp, options, cancellationToken);

                if (trace != null)
                    traces.Add(trace);

                await DelayAfterTraceAsync(trace, options, cancellationToken);
            }

            return traces;
        }

        /// <summary>
        /// 阅读一段内容：偏慢速，偶尔微调，偶尔正常预览。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> ReadSectionAsync(
            IPage page,
            ICDPSession cdp,
            int minTimes = 2,
            int maxTimes = 4,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            var traces = new List<HumanSwipeTrace>();
            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                HumanSwipeTrace? trace;
                double r = NextDouble();

                if (r < 0.70)
                    trace = await ReadingUpAsync(page, cdp, cancellationToken);
                else if (r < 0.88)
                    trace = await MicroUpAsync(page, cdp, cancellationToken);
                else
                    trace = await PreviewUpAsync(page, cdp, cancellationToken);

                if (trace != null)
                    traces.Add(trace);

                await DelayAfterTraceAsync(trace, options, cancellationToken);
            }

            return traces;
        }

        /// <summary>
        /// 快速扫描多次：适合列表页快速找目标。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> FastScanTimesAsync(
            IPage page,
            ICDPSession cdp,
            int minTimes = 2,
            int maxTimes = 5,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            var traces = new List<HumanSwipeTrace>();
            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await FastScanOnceAsync(page, cdp, cancellationToken);

                if (trace != null)
                    traces.Add(trace);

                await DelayAfterTraceAsync(trace, options, cancellationToken);
            }

            return traces;
        }

        /// <summary>
        /// 向下回看几次。一般不要太多。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> ReviewBackAsync(
            IPage page,
            ICDPSession cdp,
            int minTimes = 1,
            int maxTimes = 2,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            var traces = new List<HumanSwipeTrace>();
            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                HumanSwipeTrace? trace = NextDouble() < 0.75
                    ? await PreviewDownAsync(page, cdp, cancellationToken)
                    : await ReadingDownAsync(page, cdp, cancellationToken);

                if (trace != null)
                    traces.Add(trace);

                await DelayAfterTraceAsync(trace, options, cancellationToken);
            }

            return traces;
        }


        /// <summary>
        /// 连续随机向上滑动。
        /// 如果某一次滑动失败、页面滑不动、到底、或者返回 null，就立即停止。
        /// 
        /// 适合：列表页/搜索结果页一直往下浏览，但不强行死循环。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> RandomUpUntilStopAsync(
            IPage page,
            ICDPSession cdp,
            int minTimes = 2,
            int maxTimes = 8,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            var traces = new List<HumanSwipeTrace>();

            if (minTimes < 0)
                minTimes = 0;

            if (maxTimes < minTimes)
                maxTimes = minTimes;

            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await RandomUpOnceAsync(
                    page,
                    cdp,
                    cancellationToken);

                // HumanSwipeEmulator.SwipeAsync 内部 VerifyScrollChanged=true 时，
                // 如果到底、滑不动、滚动容器不能继续滚，会返回 null。
                if (trace == null)
                    break;

                traces.Add(trace);

                await DelayAfterTraceAsync(trace, options, cancellationToken);
            }

            return traces;
        }

        /// <summary>
        /// 连续随机向上滑动指定最大次数。
        /// 每次以向上为主，包含 Reading / Preview / Fling / Micro / LongFling。
        /// 滑不动就停止。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> RandomUpUntilStopAsync(
            IPage page,
            ICDPSession cdp,
            int maxTimes,
            CancellationToken cancellationToken = default)
        {
            return RandomUpUntilStopAsync(
                page,
                cdp,
                minTimes: maxTimes,
                maxTimes: maxTimes,
                options: null,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 单次随机向上动作。
        /// 只做向上，不做向下回看。
        /// </summary>
        public static Task<HumanSwipeTrace?> RandomUpOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            double r = NextDouble();

            // 只向上：
            // 8%  慢速阅读
            // 34% 正常预览
            // 30% 普通甩动
            // 18% 强力甩动
            // 6%  超强甩动
            // 3%  超长甩动
            // 1%  微调
            if (r < 0.08)
                return ReadingUpAsync(page, cdp, cancellationToken);

            if (r < 0.42)
                return PreviewUpAsync(page, cdp, cancellationToken);

            if (r < 0.72)
                return FlingUpAsync(page, cdp, FlingStrength.Normal, cancellationToken);

            if (r < 0.90)
                return FlingUpAsync(page, cdp, FlingStrength.Strong, cancellationToken);

            if (r < 0.96)
                return FlingUpAsync(page, cdp, FlingStrength.VeryStrong, cancellationToken);

            if (r < 0.99)
                return LongFlingUpAsync(page, cdp, cancellationToken);

            return MicroUpAsync(page, cdp, cancellationToken);
        }


        /// <summary>
        /// 单次随机向下动作。
        /// 只做向下，不做向上回看。
        /// </summary>
        public static Task<HumanSwipeTrace?> RandomDownOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            double r = NextDouble();

            // 只向上：
            // 8%  慢速阅读
            // 34% 正常预览
            // 30% 普通甩动
            // 18% 强力甩动
            // 6%  超强甩动
            // 3%  超长甩动
            // 1%  微调
            if (r < 0.08)
                return ReadingDownAsync(page, cdp, cancellationToken);

            if (r < 0.42)
                return PreviewDownAsync(page, cdp, cancellationToken);

            if (r < 0.72)
                return FlingDownAsync(page, cdp, FlingStrength.Normal, cancellationToken);

            if (r < 0.90)
                return FlingDownAsync(page, cdp, FlingStrength.Strong, cancellationToken);

            if (r < 0.96)
                return FlingDownAsync(page, cdp, FlingStrength.VeryStrong, cancellationToken);

            if (r < 0.99)
                return LongFlingDownAsync(page, cdp, cancellationToken);

            return MicroDownAsync(page, cdp, cancellationToken);
        }


        /// <summary>
        /// 超长距离快速向上甩动。
        /// 更容易产生超过 1 页的惯性滚动。
        /// </summary>
        public static Task<HumanSwipeTrace?> LongFlingUpAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Up,
                    Mode = HumanSwipeMode.Fling,

                    SpeedFactor = NextDouble(2.7, 3.0),
                    DistancePx = NextInt((int)(vh * 0.90), (int)(vh * 1.20)),
                    Steps = NextInt(5, 8),

                    HoldBeforeMove = false,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.2, 0.7),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 20
                },
                cancellationToken);
        }


        /// <summary>
        /// 连续随机向上滑动，并支持自定义停止验证回调。
        /// 
        /// 逻辑：
        /// 1. 每次滑动前先执行 stopVerifier，如果返回 true，直接停止。
        /// 2. 执行一次随机向上滑动。
        /// 3. 如果滑动返回 null，说明滑不动/到底/滚动无变化，停止。
        /// 4. 滑动后等待 delay。
        /// 5. 再执行 stopVerifier，如果返回 true，停止。
        /// 
        /// 适合：滑动列表时，发现指定节点出现就停止。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> RandomUpUntilAsync(
            IPage page,
            ICDPSession cdp,
            Func<IPage, Task<bool>> stopVerifier,
            int minTimes = 1,
            int maxTimes = 10,
            HumanSwipeOperatorOptions? options = null,
            bool checkBeforeFirstSwipe = true,
            bool checkAfterEachSwipe = true,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            var traces = new List<HumanSwipeTrace>();

            if (minTimes < 0)
                minTimes = 0;

            if (maxTimes < minTimes)
                maxTimes = minTimes;

            int times = NextInt(minTimes, maxTimes);

            if (checkBeforeFirstSwipe)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await SafeVerifyStopAsync(page, stopVerifier, cancellationToken))
                    return traces;
            }

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await RandomUpOnceAsync(
                    page,
                    cdp,
                    cancellationToken);

                // 滑不动、到底、没有发生滚动，则停止。
                if (trace == null)
                    break;

                traces.Add(trace);

                await DelayAfterTraceAsync(trace, options, cancellationToken);

                if (checkAfterEachSwipe)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await SafeVerifyStopAsync(page, stopVerifier, cancellationToken))
                        break;
                }
            }

            return traces;
        }

        /// <summary>
        /// 连续随机向上滑动，并支持无参异步停止回调。
        /// 回调返回 true 时停止。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> RandomUpUntilAsync(
            IPage page,
            ICDPSession cdp,
            Func<Task<bool>> stopVerifier,
            int minTimes = 1,
            int maxTimes = 10,
            HumanSwipeOperatorOptions? options = null,
            bool checkBeforeFirstSwipe = true,
            bool checkAfterEachSwipe = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return await RandomUpUntilAsync(
                page,
                cdp,
                _ => stopVerifier(),
                minTimes,
                maxTimes,
                options,
                checkBeforeFirstSwipe,
                checkAfterEachSwipe,
                cancellationToken);
        }

        /// <summary>
        /// 连续随机向上滑动，并支持同步停止回调。
        /// 回调返回 true 时停止。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> RandomUpUntilAsync(
            IPage page,
            ICDPSession cdp,
            Func<IPage, bool> stopVerifier,
            int minTimes = 1,
            int maxTimes = 10,
            HumanSwipeOperatorOptions? options = null,
            bool checkBeforeFirstSwipe = true,
            bool checkAfterEachSwipe = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return await RandomUpUntilAsync(
                page,
                cdp,
                p => Task.FromResult(stopVerifier(p)),
                minTimes,
                maxTimes,
                options,
                checkBeforeFirstSwipe,
                checkAfterEachSwipe,
                cancellationToken);
        }

        /// <summary>
        /// 连续随机向上滑动，并支持自定义停止验证回调。
        /// 回调包含 page 和当前已完成滑动次数。
        /// 返回 true 时停止。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> RandomUpUntilAsync(
            IPage page,
            ICDPSession cdp,
            Func<IPage, int, Task<bool>> stopVerifier,
            int minTimes = 1,
            int maxTimes = 10,
            HumanSwipeOperatorOptions? options = null,
            bool checkBeforeFirstSwipe = true,
            bool checkAfterEachSwipe = true,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            var traces = new List<HumanSwipeTrace>();

            if (minTimes < 0)
                minTimes = 0;

            if (maxTimes < minTimes)
                maxTimes = minTimes;

            int times = NextInt(minTimes, maxTimes);

            if (checkBeforeFirstSwipe)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await SafeVerifyStopAsync(page, 0, stopVerifier, cancellationToken))
                    return traces;
            }

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await RandomUpOnceAsync(
                    page,
                    cdp,
                    cancellationToken);

                if (trace == null)
                    break;

                traces.Add(trace);

                await DelayAfterTraceAsync(trace, options, cancellationToken);

                if (checkAfterEachSwipe)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await SafeVerifyStopAsync(page, traces.Count, stopVerifier, cancellationToken))
                        break;
                }
            }

            return traces;
        }

        private static async Task<bool> SafeVerifyStopAsync(
            IPage page,
            Func<IPage, Task<bool>> stopVerifier,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page == null || page.IsClosed)
                    return true;

                return await stopVerifier(page);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 验证方法异常时，不直接停止，继续滑动。
                return false;
            }
        }

        private static async Task<bool> SafeVerifyStopAsync(
            IPage page,
            int swipeCount,
            Func<IPage, int, Task<bool>> stopVerifier,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page == null || page.IsClosed)
                    return true;

                return await stopVerifier(page, swipeCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }


        /// <summary>
        /// 随机无规则乱序浏览一次。
        /// 模拟“反复查找某个东西”的浏览行为：
        /// 可能向上、向下、微调、快速扫描、强甩、短暂停顿。
        /// </summary>
        public static async Task<HumanSwipeTrace?> ChaoticBrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            double r = NextDouble();

            // 行为分布：
            // 0.00 - 0.24 正常向上预览
            // 0.24 - 0.42 普通/强力向上甩动
            // 0.42 - 0.54 慢速向上阅读
            // 0.54 - 0.66 正常向下回看
            // 0.66 - 0.74 向下甩动
            // 0.74 - 0.84 小幅上下微调
            // 0.84 - 0.93 快速扫描向上
            // 0.93 - 0.98 超长向上甩动
            // 0.98 - 1.00 只停顿，不滑动
            if (r < 0.24)
                return await PreviewUpAsync(page, cdp, cancellationToken);

            if (r < 0.42)
            {
                var strength = NextDouble() < 0.72
                    ? FlingStrength.Normal
                    : FlingStrength.Strong;

                return await FlingUpAsync(page, cdp, strength, cancellationToken);
            }

            if (r < 0.54)
                return await ReadingUpAsync(page, cdp, cancellationToken);

            if (r < 0.66)
                return await PreviewDownAsync(page, cdp, cancellationToken);

            if (r < 0.74)
            {
                var strength = NextDouble() < 0.80
                    ? FlingStrength.Normal
                    : FlingStrength.Strong;

                return await FlingDownAsync(page, cdp, strength, cancellationToken);
            }

            if (r < 0.84)
            {
                if (NextDouble() < 0.62)
                    return await MicroUpAsync(page, cdp, cancellationToken);

                return await MicroDownAsync(page, cdp, cancellationToken);
            }

            if (r < 0.93)
                return await FastScanOnceAsync(page, cdp, cancellationToken);

            if (r < 0.98)
                return await LongFlingUpAsync(page, cdp, cancellationToken);

            // 偶尔停顿，像人在观察页面。
            await Task.Delay(NextInt(600, 1800), cancellationToken);
            return null;
        }

        /// <summary>
        /// 随机无规则乱序浏览多次。
        /// 不带目标条件，只按随机次数执行。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> ChaoticBrowseTimesAsync(
            IPage page,
            ICDPSession cdp,
            int minTimes = 4,
            int maxTimes = 12,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            var traces = new List<HumanSwipeTrace>();

            if (minTimes < 0)
                minTimes = 0;

            if (maxTimes < minTimes)
                maxTimes = minTimes;

            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await ChaoticBrowseOnceAsync(page, cdp, cancellationToken);

                if (trace != null)
                    traces.Add(trace);

                await DelayAfterChaoticTraceAsync(trace, options, cancellationToken);
            }

            return traces;
        }

        /// <summary>
        /// 随机无规则乱序查找。
        /// 
        /// 逻辑：
        /// 1. 每次动作前先执行 stopVerifier，返回 true 则停止。
        /// 2. 执行一次无规则动作：上滑、下滑、微调、强甩、停顿。
        /// 3. 动作后等待随机时间。
        /// 4. 再执行 stopVerifier，返回 true 则停止。
        /// 5. 不会因为某一次滑动返回 null 就立刻停止，因为 Chaotic 模式里 null 可能代表“停顿观察”。
        /// 6. 如果连续多次滑动都失败/无有效 trace，则认为页面可能不可滚动，停止。
        /// 
        /// 适合：像人在页面里反复上下查找某个节点。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> ChaoticBrowseUntilAsync(
            IPage page,
            ICDPSession cdp,
            Func<IPage, Task<bool>> stopVerifier,
            int minTimes = 4,
            int maxTimes = 18,
            int maxContinuousNoMove = 3,
            HumanSwipeOperatorOptions? options = null,
            bool checkBeforeFirstAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            var traces = new List<HumanSwipeTrace>();

            if (minTimes < 0)
                minTimes = 0;

            if (maxTimes < minTimes)
                maxTimes = minTimes;

            if (maxContinuousNoMove < 1)
                maxContinuousNoMove = 1;

            int times = NextInt(minTimes, maxTimes);
            int continuousNoMove = 0;

            if (checkBeforeFirstAction)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await SafeVerifyStopAsync(page, stopVerifier, cancellationToken))
                    return traces;
            }

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await ChaoticBrowseOnceAsync(page, cdp, cancellationToken);

                if (trace != null)
                {
                    traces.Add(trace);
                    continuousNoMove = 0;
                }
                else
                {
                    continuousNoMove++;
                }

                await DelayAfterChaoticTraceAsync(trace, options, cancellationToken);

                if (checkAfterEachAction)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await SafeVerifyStopAsync(page, stopVerifier, cancellationToken))
                        break;
                }

                // 连续几次都没有有效滑动，通常说明已经到边界、页面不可滚动，或动作没有产生滚动。
                if (continuousNoMove >= maxContinuousNoMove)
                    break;
            }

            return traces;
        }

        /// <summary>
        /// 随机无规则乱序查找，无参异步回调。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> ChaoticBrowseUntilAsync(
            IPage page,
            ICDPSession cdp,
            Func<Task<bool>> stopVerifier,
            int minTimes = 4,
            int maxTimes = 18,
            int maxContinuousNoMove = 3,
            HumanSwipeOperatorOptions? options = null,
            bool checkBeforeFirstAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return ChaoticBrowseUntilAsync(
                page,
                cdp,
                _ => stopVerifier(),
                minTimes,
                maxTimes,
                maxContinuousNoMove,
                options,
                checkBeforeFirstAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 随机无规则乱序查找，同步回调。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> ChaoticBrowseUntilAsync(
            IPage page,
            ICDPSession cdp,
            Func<IPage, bool> stopVerifier,
            int minTimes = 4,
            int maxTimes = 18,
            int maxContinuousNoMove = 3,
            HumanSwipeOperatorOptions? options = null,
            bool checkBeforeFirstAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return ChaoticBrowseUntilAsync(
                page,
                cdp,
                p => Task.FromResult(stopVerifier(p)),
                minTimes,
                maxTimes,
                maxContinuousNoMove,
                options,
                checkBeforeFirstAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 随机无规则乱序查找，回调包含当前有效滑动次数。
        /// 注意：swipeCount 只统计产生有效 trace 的滑动，不统计纯停顿。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> ChaoticBrowseUntilAsync(
            IPage page,
            ICDPSession cdp,
            Func<IPage, int, Task<bool>> stopVerifier,
            int minTimes = 4,
            int maxTimes = 18,
            int maxContinuousNoMove = 3,
            HumanSwipeOperatorOptions? options = null,
            bool checkBeforeFirstAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            var traces = new List<HumanSwipeTrace>();

            if (minTimes < 0)
                minTimes = 0;

            if (maxTimes < minTimes)
                maxTimes = minTimes;

            if (maxContinuousNoMove < 1)
                maxContinuousNoMove = 1;

            int times = NextInt(minTimes, maxTimes);
            int continuousNoMove = 0;

            if (checkBeforeFirstAction)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await SafeVerifyStopAsync(page, 0, stopVerifier, cancellationToken))
                    return traces;
            }

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await ChaoticBrowseOnceAsync(page, cdp, cancellationToken);

                if (trace != null)
                {
                    traces.Add(trace);
                    continuousNoMove = 0;
                }
                else
                {
                    continuousNoMove++;
                }

                await DelayAfterChaoticTraceAsync(trace, options, cancellationToken);

                if (checkAfterEachAction)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await SafeVerifyStopAsync(page, traces.Count, stopVerifier, cancellationToken))
                        break;
                }

                if (continuousNoMove >= maxContinuousNoMove)
                    break;
            }

            return traces;
        }

        private static async Task DelayAfterChaoticTraceAsync(
            HumanSwipeTrace? trace,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken)
        {
            int delay;

            if (trace == null)
            {
                delay = NextInt(500, 1600);
            }
            else if (trace.Mode == HumanSwipeMode.Reading)
            {
                delay = NextInt(900, 2600);
            }
            else if (trace.Mode == HumanSwipeMode.Fling)
            {
                delay = NextInt(900, 2300);
            }
            else if (trace.Mode == HumanSwipeMode.Micro)
            {
                delay = NextInt(220, 760);
            }
            else
            {
                delay = NextInt(options.MinDelayAfterSwipeMs, options.MaxDelayAfterSwipeMs);
            }

            // 偶尔加一个长观察停顿。
            if (NextDouble() < 0.12)
                delay += NextInt(800, 2600);

            await Task.Delay(delay, cancellationToken);
        }




        /// <summary>
        /// 使用统一时间范围进行无规则浏览。
        /// 支持毫秒、秒、分钟构造出来的 HumanBrowseDurationRange。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomAsync(
            IPage page,
            ICDPSession cdp,
            HumanBrowseDurationRange durationRange,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            CancellationToken cancellationToken = default)
        {
            return TimedChaoticBrowseRandomAsync(
                page,
                cdp,
                durationRange.MinDuration,
                durationRange.MaxDuration,
                options,
                maxContinuousNoMove,
                cancellationToken);
        }

        /// <summary>
        /// 使用统一时间范围进行无规则浏览，并支持停止条件。
        /// 支持毫秒、秒、分钟构造出来的 HumanBrowseDurationRange。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomUntilAsync(
            IPage page,
            ICDPSession cdp,
            HumanBrowseDurationRange durationRange,
            Func<IPage, Task<bool>>? stopVerifier = null,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            return TimedChaoticBrowseRandomUntilAsync(
                page,
                cdp,
                durationRange.MinDuration,
                durationRange.MaxDuration,
                stopVerifier,
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 使用统一时间范围进行无规则浏览，并支持无参异步停止条件。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomUntilAsync(
            IPage page,
            ICDPSession cdp,
            HumanBrowseDurationRange durationRange,
            Func<Task<bool>> stopVerifier,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return TimedChaoticBrowseRandomUntilAsync(
                page,
                cdp,
                durationRange.MinDuration,
                durationRange.MaxDuration,
                _ => stopVerifier(),
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 使用统一时间范围进行无规则浏览，并支持同步停止条件。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomUntilAsync(
            IPage page,
            ICDPSession cdp,
            HumanBrowseDurationRange durationRange,
            Func<IPage, bool> stopVerifier,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return TimedChaoticBrowseRandomUntilAsync(
                page,
                cdp,
                durationRange.MinDuration,
                durationRange.MaxDuration,
                p => Task.FromResult(stopVerifier(p)),
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 使用统一时间范围进行无规则浏览，并支持带统计信息的停止条件。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomUntilAsync(
            IPage page,
            ICDPSession cdp,
            HumanBrowseDurationRange durationRange,
            Func<IPage, int, TimeSpan, TimeSpan, Task<bool>> stopVerifier,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return TimedChaoticBrowseRandomUntilAsync(
                page,
                cdp,
                durationRange.MinDuration,
                durationRange.MaxDuration,
                stopVerifier,
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 毫秒范围：在 minMilliseconds ~ maxMilliseconds 之间随机取一个时长进行浏览。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomMillisecondsAsync(
            IPage page,
            ICDPSession cdp,
            int minMilliseconds,
            int maxMilliseconds,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            CancellationToken cancellationToken = default)
        {
            return TimedChaoticBrowseRandomAsync(
                page,
                cdp,
                HumanBrowseDurationRange.FromMilliseconds(minMilliseconds, maxMilliseconds),
                options,
                maxContinuousNoMove,
                cancellationToken);
        }

        /// <summary>
        /// 秒范围：在 minSeconds ~ maxSeconds 之间随机取一个时长进行浏览。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomSecondsAsync(
            IPage page,
            ICDPSession cdp,
            int minSeconds,
            int maxSeconds,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            CancellationToken cancellationToken = default)
        {
            return TimedChaoticBrowseRandomAsync(
                page,
                cdp,
                HumanBrowseDurationRange.FromSeconds(minSeconds, maxSeconds),
                options,
                maxContinuousNoMove,
                cancellationToken);
        }

        /// <summary>
        /// 分钟范围：在 minMinutes ~ maxMinutes 之间随机取一个时长进行浏览。
        /// 支持小数，例如 0.5 ~ 1.5 分钟。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomMinutesAsync(
            IPage page,
            ICDPSession cdp,
            double minMinutes,
            double maxMinutes,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            CancellationToken cancellationToken = default)
        {
            return TimedChaoticBrowseRandomAsync(
                page,
                cdp,
                HumanBrowseDurationRange.FromMinutes(minMinutes, maxMinutes),
                options,
                maxContinuousNoMove,
                cancellationToken);
        }

        /// <summary>
        /// 毫秒范围 + 条件退出。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomMillisecondsUntilAsync(
            IPage page,
            ICDPSession cdp,
            int minMilliseconds,
            int maxMilliseconds,
            Func<IPage, Task<bool>>? stopVerifier = null,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            return TimedChaoticBrowseRandomUntilAsync(
                page,
                cdp,
                HumanBrowseDurationRange.FromMilliseconds(minMilliseconds, maxMilliseconds),
                stopVerifier,
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 秒范围 + 条件退出。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomSecondsUntilAsync(
            IPage page,
            ICDPSession cdp,
            int minSeconds,
            int maxSeconds,
            Func<IPage, Task<bool>>? stopVerifier = null,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            return TimedChaoticBrowseRandomUntilAsync(
                page,
                cdp,
                HumanBrowseDurationRange.FromSeconds(minSeconds, maxSeconds),
                stopVerifier,
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 分钟范围 + 条件退出。
        /// 支持小数，例如 0.5 ~ 1.5 分钟。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomMinutesUntilAsync(
            IPage page,
            ICDPSession cdp,
            double minMinutes,
            double maxMinutes,
            Func<IPage, Task<bool>>? stopVerifier = null,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            return TimedChaoticBrowseRandomUntilAsync(
                page,
                cdp,
                HumanBrowseDurationRange.FromMinutes(minMinutes, maxMinutes),
                stopVerifier,
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 在 minDuration ~ maxDuration 之间随机取一个时长，进行无规则浏览。
        /// 
        /// 例如：
        /// await HumanSwipeOperator.TimedChaoticBrowseRandomAsync(
        ///     page,
        ///     cdp,
        ///     TimeSpan.FromMinutes(1),
        ///     TimeSpan.FromMinutes(2));
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan minDuration,
            TimeSpan maxDuration,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            CancellationToken cancellationToken = default)
        {
            TimeSpan duration = NextDuration(minDuration, maxDuration);

            return TimedChaoticBrowseAsync(
                page,
                cdp,
                duration,
                options,
                maxContinuousNoMove,
                cancellationToken);
        }

        /// <summary>
        /// 在 minSeconds ~ maxSeconds 之间随机取一个秒数，进行无规则浏览。
        /// 
        /// 例如：
        /// await HumanSwipeOperator.TimedChaoticBrowseRandomAsync(page, cdp, 60, 120);
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomAsync(
            IPage page,
            ICDPSession cdp,
            int minSeconds,
            int maxSeconds,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            CancellationToken cancellationToken = default)
        {
            if (minSeconds < 0)
                minSeconds = 0;

            if (maxSeconds < minSeconds)
                maxSeconds = minSeconds;

            int seconds = NextInt(minSeconds, maxSeconds);

            return TimedChaoticBrowseAsync(
                page,
                cdp,
                TimeSpan.FromSeconds(seconds),
                options,
                maxContinuousNoMove,
                cancellationToken);
        }

        /// <summary>
        /// 在 minDuration ~ maxDuration 之间随机取一个时长，进行无规则浏览，并支持停止条件。
        /// stopVerifier 返回 true 时提前退出。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomUntilAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan minDuration,
            TimeSpan maxDuration,
            Func<IPage, Task<bool>>? stopVerifier = null,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            TimeSpan duration = NextDuration(minDuration, maxDuration);

            return TimedChaoticBrowseUntilAsync(
                page,
                cdp,
                duration,
                stopVerifier,
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 在 minSeconds ~ maxSeconds 之间随机取一个秒数，进行无规则浏览，并支持停止条件。
        /// stopVerifier 返回 true 时提前退出。
        /// 
        /// 例如：
        /// await HumanSwipeOperator.TimedChaoticBrowseRandomUntilAsync(
        ///     page,
        ///     cdp,
        ///     60,
        ///     120,
        ///     async p => await p.Locator(".target").CountAsync() > 0);
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomUntilAsync(
            IPage page,
            ICDPSession cdp,
            int minSeconds,
            int maxSeconds,
            Func<IPage, Task<bool>>? stopVerifier = null,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (minSeconds < 0)
                minSeconds = 0;

            if (maxSeconds < minSeconds)
                maxSeconds = minSeconds;

            int seconds = NextInt(minSeconds, maxSeconds);

            return TimedChaoticBrowseUntilAsync(
                page,
                cdp,
                TimeSpan.FromSeconds(seconds),
                stopVerifier,
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 在 minDuration ~ maxDuration 之间随机取一个时长，进行无规则浏览，并支持无参异步停止条件。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomUntilAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan minDuration,
            TimeSpan maxDuration,
            Func<Task<bool>> stopVerifier,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return TimedChaoticBrowseRandomUntilAsync(
                page,
                cdp,
                minDuration,
                maxDuration,
                _ => stopVerifier(),
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 在 minDuration ~ maxDuration 之间随机取一个时长，进行无规则浏览，并支持同步停止条件。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomUntilAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan minDuration,
            TimeSpan maxDuration,
            Func<IPage, bool> stopVerifier,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return TimedChaoticBrowseRandomUntilAsync(
                page,
                cdp,
                minDuration,
                maxDuration,
                p => Task.FromResult(stopVerifier(p)),
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 在 minDuration ~ maxDuration 之间随机取一个时长，进行无规则浏览，并支持带统计信息的停止条件。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseRandomUntilAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan minDuration,
            TimeSpan maxDuration,
            Func<IPage, int, TimeSpan, TimeSpan, Task<bool>> stopVerifier,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            TimeSpan duration = NextDuration(minDuration, maxDuration);

            return TimedChaoticBrowseUntilAsync(
                page,
                cdp,
                duration,
                stopVerifier,
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        private static TimeSpan NextDuration(TimeSpan minDuration, TimeSpan maxDuration)
        {
            if (minDuration < TimeSpan.Zero)
                minDuration = TimeSpan.Zero;

            if (maxDuration < minDuration)
                maxDuration = minDuration;

            double minMs = minDuration.TotalMilliseconds;
            double maxMs = maxDuration.TotalMilliseconds;

            if (maxMs <= minMs)
                return minDuration;

            double ms = NextDouble(minMs, maxMs);
            return TimeSpan.FromMilliseconds(ms);
        }

        /// <summary>
        /// 按指定时间进行无规则浏览。
        /// 
        /// 例如：
        /// await HumanSwipeOperator.TimedChaoticBrowseAsync(page, cdp, TimeSpan.FromMinutes(1));
        /// 
        /// 特点：
        /// 1. 在指定时间内持续执行无规则动作。
        /// 2. 包含上滑、下滑、微调、快速扫描、长甩、短暂停顿。
        /// 3. 不保证刚好精确到毫秒结束，但会在每轮动作前检查时间。
        /// 4. 连续多次没有有效滑动时，可提前停止，避免到顶/到底后死循环。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> TimedChaoticBrowseAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            CancellationToken cancellationToken = default)
        {
            return await TimedChaoticBrowseUntilAsync(
                page,
                cdp,
                duration,
                stopVerifier: (Func<IPage, Task<bool>>?)null,
                options: options,
                maxContinuousNoMove: maxContinuousNoMove,
                checkBeforeEachAction: false,
                checkAfterEachAction: false,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 按指定秒数进行无规则浏览。
        /// 
        /// 例如：
        /// await HumanSwipeOperator.TimedChaoticBrowseAsync(page, cdp, seconds: 60);
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseAsync(
            IPage page,
            ICDPSession cdp,
            int seconds,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            CancellationToken cancellationToken = default)
        {
            if (seconds < 0)
                seconds = 0;

            return TimedChaoticBrowseAsync(
                page,
                cdp,
                TimeSpan.FromSeconds(seconds),
                options,
                maxContinuousNoMove,
                cancellationToken);
        }

        /// <summary>
        /// 按指定时间进行无规则浏览，并支持停止条件。
        /// 
        /// stopVerifier 返回 true 时提前退出。
        /// 适合：在 1 分钟内反复无规则浏览，遇到目标节点出现就停止。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> TimedChaoticBrowseUntilAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            Func<IPage, Task<bool>>? stopVerifier = null,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            if (duration < TimeSpan.Zero)
                duration = TimeSpan.Zero;

            if (maxContinuousNoMove < 1)
                maxContinuousNoMove = 1;

            var traces = new List<HumanSwipeTrace>();
            DateTimeOffset startTime = DateTimeOffset.UtcNow;
            DateTimeOffset endTime = startTime.Add(duration);

            int continuousNoMove = 0;

            while (DateTimeOffset.UtcNow < endTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                if (checkBeforeEachAction && stopVerifier != null)
                {
                    if (await SafeVerifyStopAsync(page, stopVerifier, cancellationToken))
                        break;
                }

                TimeSpan remaining = endTime - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                var trace = await TimedChaoticBrowseOnceAsync(
                    page,
                    cdp,
                    remaining,
                    cancellationToken);

                if (trace != null)
                {
                    traces.Add(trace);
                    continuousNoMove = 0;
                }
                else
                {
                    continuousNoMove++;
                }

                TimeSpan remainAfterAction = endTime - DateTimeOffset.UtcNow;
                if (remainAfterAction <= TimeSpan.Zero)
                    break;

                await DelayAfterTimedChaoticTraceAsync(
                    trace,
                    options,
                    remainAfterAction,
                    cancellationToken);

                if (checkAfterEachAction && stopVerifier != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (await SafeVerifyStopAsync(page, stopVerifier, cancellationToken))
                        break;
                }

                if (continuousNoMove >= maxContinuousNoMove)
                    break;
            }

            return traces;
        }

        /// <summary>
        /// 按指定时间进行无规则浏览，并支持无参异步停止条件。
        /// stopVerifier 返回 true 时提前退出。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseUntilAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            Func<Task<bool>> stopVerifier,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return TimedChaoticBrowseUntilAsync(
                page,
                cdp,
                duration,
                _ => stopVerifier(),
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 按指定时间进行无规则浏览，并支持同步停止条件。
        /// stopVerifier 返回 true 时提前退出。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseUntilAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            Func<IPage, bool> stopVerifier,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            return TimedChaoticBrowseUntilAsync(
                page,
                cdp,
                duration,
                p => Task.FromResult(stopVerifier(p)),
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 按指定秒数进行无规则浏览，并支持停止条件。
        /// stopVerifier 返回 true 时提前退出。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseUntilAsync(
            IPage page,
            ICDPSession cdp,
            int seconds,
            Func<IPage, Task<bool>>? stopVerifier = null,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            if (seconds < 0)
                seconds = 0;

            return TimedChaoticBrowseUntilAsync(
                page,
                cdp,
                TimeSpan.FromSeconds(seconds),
                stopVerifier,
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

        /// <summary>
        /// 按指定时间进行无规则浏览，并支持带统计信息的停止条件。
        /// 
        /// stopVerifier 参数：
        /// page: 当前页面。
        /// traces.Count: 当前已经产生有效滑动的次数。
        /// elapsed: 已运行时长。
        /// remaining: 剩余时长。
        /// 
        /// 返回 true 时提前退出。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> TimedChaoticBrowseUntilAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            Func<IPage, int, TimeSpan, TimeSpan, Task<bool>> stopVerifier,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            bool checkBeforeEachAction = true,
            bool checkAfterEachAction = true,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOperatorOptions();

            ValidatePageAndCdp(page, cdp);

            if (stopVerifier == null)
                throw new ArgumentNullException(nameof(stopVerifier));

            if (duration < TimeSpan.Zero)
                duration = TimeSpan.Zero;

            if (maxContinuousNoMove < 1)
                maxContinuousNoMove = 1;

            var traces = new List<HumanSwipeTrace>();
            DateTimeOffset startTime = DateTimeOffset.UtcNow;
            DateTimeOffset endTime = startTime.Add(duration);

            int continuousNoMove = 0;

            while (DateTimeOffset.UtcNow < endTime)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                TimeSpan elapsed = DateTimeOffset.UtcNow - startTime;
                TimeSpan remaining = endTime - DateTimeOffset.UtcNow;

                if (checkBeforeEachAction)
                {
                    if (await SafeVerifyTimedStopAsync(
                            page,
                            traces.Count,
                            elapsed,
                            remaining,
                            stopVerifier,
                            cancellationToken))
                    {
                        break;
                    }
                }

                remaining = endTime - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    break;

                var trace = await TimedChaoticBrowseOnceAsync(
                    page,
                    cdp,
                    remaining,
                    cancellationToken);

                if (trace != null)
                {
                    traces.Add(trace);
                    continuousNoMove = 0;
                }
                else
                {
                    continuousNoMove++;
                }

                TimeSpan remainAfterAction = endTime - DateTimeOffset.UtcNow;
                if (remainAfterAction <= TimeSpan.Zero)
                    break;

                await DelayAfterTimedChaoticTraceAsync(
                    trace,
                    options,
                    remainAfterAction,
                    cancellationToken);

                if (checkAfterEachAction)
                {
                    TimeSpan elapsedAfter = DateTimeOffset.UtcNow - startTime;
                    TimeSpan remainingAfter = endTime - DateTimeOffset.UtcNow;

                    if (await SafeVerifyTimedStopAsync(
                            page,
                            traces.Count,
                            elapsedAfter,
                            remainingAfter,
                            stopVerifier,
                            cancellationToken))
                    {
                        break;
                    }
                }

                if (continuousNoMove >= maxContinuousNoMove)
                    break;
            }

            return traces;
        }

        /// <summary>
        /// 时间浏览中的单次无规则动作。
        /// 会根据剩余时间动态选择动作：
        /// 剩余时间短时，更倾向于微调/短滑/短停顿，避免最后一轮动作过长。
        /// </summary>
        private static async Task<HumanSwipeTrace?> TimedChaoticBrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan remaining,
            CancellationToken cancellationToken)
        {
            ValidatePageAndCdp(page, cdp);

            double secondsLeft = remaining.TotalSeconds;
            double r = NextDouble();

            if (secondsLeft <= 1.2)
            {
                await Task.Delay(
                    Math.Max(80, Math.Min(600, (int)(remaining.TotalMilliseconds * 0.60))),
                    cancellationToken);

                return null;
            }

            if (secondsLeft <= 3.0)
            {
                // 时间快结束时，不做长甩。
                if (r < 0.35)
                    return await MicroUpAsync(page, cdp, cancellationToken);

                if (r < 0.55)
                    return await MicroDownAsync(page, cdp, cancellationToken);

                if (r < 0.80)
                    return await PreviewUpAsync(page, cdp, cancellationToken);

                if (r < 0.92)
                    return await PreviewDownAsync(page, cdp, cancellationToken);

                await Task.Delay(NextInt(200, 800), cancellationToken);
                return null;
            }

            if (secondsLeft <= 8.0)
            {
                // 剩余时间中等，减少 LongFling。
                if (r < 0.24)
                    return await PreviewUpAsync(page, cdp, cancellationToken);

                if (r < 0.40)
                    return await FlingUpAsync(page, cdp, FlingStrength.Normal, cancellationToken);

                if (r < 0.52)
                    return await ReadingUpAsync(page, cdp, cancellationToken);

                if (r < 0.66)
                    return await PreviewDownAsync(page, cdp, cancellationToken);

                if (r < 0.75)
                    return await FlingDownAsync(page, cdp, FlingStrength.Normal, cancellationToken);

                if (r < 0.88)
                {
                    return NextDouble() < 0.58
                        ? await MicroUpAsync(page, cdp, cancellationToken)
                        : await MicroDownAsync(page, cdp, cancellationToken);
                }

                if (r < 0.96)
                    return await FastScanOnceAsync(page, cdp, cancellationToken);

                await Task.Delay(NextInt(400, 1200), cancellationToken);
                return null;
            }

            // 时间充足，用完整 Chaotic 分布。
            return await ChaoticBrowseOnceAsync(page, cdp, cancellationToken);
        }

        private static async Task DelayAfterTimedChaoticTraceAsync(
            HumanSwipeTrace? trace,
            HumanSwipeOperatorOptions options,
            TimeSpan remaining,
            CancellationToken cancellationToken)
        {
            int delay;

            if (trace == null)
            {
                delay = NextInt(260, 1300);
            }
            else if (trace.Mode == HumanSwipeMode.Reading)
            {
                delay = NextInt(800, 2400);
            }
            else if (trace.Mode == HumanSwipeMode.Fling)
            {
                delay = NextInt(800, 2100);
            }
            else if (trace.Mode == HumanSwipeMode.Micro)
            {
                delay = NextInt(180, 650);
            }
            else
            {
                delay = NextInt(options.MinDelayAfterSwipeMs, options.MaxDelayAfterSwipeMs);
            }

            // 偶尔长观察。
            if (NextDouble() < 0.10)
                delay += NextInt(700, 2200);

            // 不要超过剩余时间太多。
            int maxAllowed = Math.Max(50, (int)Math.Min(remaining.TotalMilliseconds, delay));
            await Task.Delay(maxAllowed, cancellationToken);
        }

        private static async Task<bool> SafeVerifyTimedStopAsync(
            IPage page,
            int swipeCount,
            TimeSpan elapsed,
            TimeSpan remaining,
            Func<IPage, int, TimeSpan, TimeSpan, Task<bool>> stopVerifier,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page == null || page.IsClosed)
                    return true;

                return await stopVerifier(page, swipeCount, elapsed, remaining);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 回调异常不直接停止，继续浏览。
                return false;
            }
        }

        #endregion

        #region 垂直滑动：阅读/预览/甩动/微调

        /// <summary>
        /// 慢速向上阅读。
        /// </summary>
        public static Task<HumanSwipeTrace?> ReadingUpAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Up,
                    Mode = HumanSwipeMode.Reading,

                    SpeedFactor = NextDouble(0.70, 0.95),
                    DistancePx = NextInt((int)(vh * 0.18), (int)(vh * 0.32)),
                    Steps = NextInt(46, 72),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = true,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.8, 1.6),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 5
                },
                cancellationToken);
        }

        /// <summary>
        /// 慢速向下回看。
        /// </summary>
        public static Task<HumanSwipeTrace?> ReadingDownAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Down,
                    Mode = HumanSwipeMode.Reading,

                    SpeedFactor = NextDouble(0.70, 0.95),
                    DistancePx = NextInt((int)(vh * 0.16), (int)(vh * 0.30)),
                    Steps = NextInt(46, 72),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = true,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.8, 1.6),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 5
                },
                cancellationToken);
        }

        /// <summary>
        /// 正常向上预览。
        /// </summary>
        public static Task<HumanSwipeTrace?> PreviewUpAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Up,
                    Mode = HumanSwipeMode.Preview,

                    SpeedFactor = NextDouble(1.0, 1.35),
                    DistancePx = NextInt((int)(vh * 0.30), (int)(vh * 0.48)),
                    Steps = NextInt(24, 40),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(1.2, 2.0),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 8
                },
                cancellationToken);
        }

        /// <summary>
        /// 正常向下回看。
        /// </summary>
        public static Task<HumanSwipeTrace?> PreviewDownAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Down,
                    Mode = HumanSwipeMode.Preview,

                    SpeedFactor = NextDouble(1.0, 1.35),
                    DistancePx = NextInt((int)(vh * 0.26), (int)(vh * 0.44)),
                    Steps = NextInt(24, 40),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(1.2, 2.0),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 8
                },
                cancellationToken);
        }

        /// <summary>
        /// 快速向上甩动。力度越大，距离越长、步数越少、速度越快。
        /// </summary>
        public static Task<HumanSwipeTrace?> FlingUpAsync(
            IPage page,
            ICDPSession cdp,
            FlingStrength strength = FlingStrength.Normal,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);
            var cfg = GetFlingConfig(strength);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Up,
                    Mode = HumanSwipeMode.Fling,

                    SpeedFactor = NextDouble(cfg.MinSpeed, cfg.MaxSpeed),
                    DistancePx = NextInt(
                        (int)(vh * cfg.MinDistanceRatio),
                        (int)(vh * cfg.MaxDistanceRatio)),

                    Steps = NextInt(cfg.MinSteps, cfg.MaxSteps),

                    HoldBeforeMove = false,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(cfg.MinJitter, cfg.MaxJitter),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 12
                },
                cancellationToken);
        }

        /// <summary>
        /// 快速向下甩动。
        /// </summary>
        public static Task<HumanSwipeTrace?> FlingDownAsync(
            IPage page,
            ICDPSession cdp,
            FlingStrength strength = FlingStrength.Normal,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);
            var cfg = GetFlingConfig(strength);

            // 向下通常略短一点，避免顶部回弹太明显。
            double minDistance = Math.Max(0.35, cfg.MinDistanceRatio - 0.06);
            double maxDistance = Math.Max(minDistance + 0.05, cfg.MaxDistanceRatio - 0.08);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Down,
                    Mode = HumanSwipeMode.Fling,

                    SpeedFactor = NextDouble(cfg.MinSpeed, cfg.MaxSpeed),
                    DistancePx = NextInt(
                        (int)(vh * minDistance),
                        (int)(vh * maxDistance)),

                    Steps = NextInt(cfg.MinSteps, cfg.MaxSteps),

                    HoldBeforeMove = false,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(cfg.MinJitter, cfg.MaxJitter),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 12
                },
                cancellationToken);
        }

        /// <summary>
        /// 超长距离快速向下甩动。
        /// 更容易产生较长距离的向下惯性滚动。
        /// 适合从页面中下部快速回看上方内容。
        /// </summary>
        public static Task<HumanSwipeTrace?> LongFlingDownAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Down,
                    Mode = HumanSwipeMode.Fling,

                    // 向下长甩也要快，但略低于向上，避免顶部回弹太明显
                    SpeedFactor = NextDouble(2.5, 3.0),

                    // 向下距离略短于 LongFlingUp，避免一甩直接顶到顶部
                    DistancePx = NextInt(
                        (int)(vh * 0.78),
                        (int)(vh * 1.05)),

                    // 步数越少，抬手速度越大，惯性越明显
                    Steps = NextInt(5, 9),

                    HoldBeforeMove = false,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,

                    // 强甩不要抖太大
                    MaxJitter = NextDouble(0.2, 0.7),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 20
                },
                cancellationToken);
        }



        /// <summary>
        /// 小幅向上微调。
        /// </summary>
        public static Task<HumanSwipeTrace?> MicroUpAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Up,
                    Mode = HumanSwipeMode.Micro,

                    SpeedFactor = NextDouble(0.85, 1.15),
                    DistancePx = NextInt((int)(vh * 0.07), (int)(vh * 0.16)),
                    Steps = NextInt(16, 32),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = true,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.4, 0.9),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 3
                },
                cancellationToken);
        }

        /// <summary>
        /// 小幅向下微调。
        /// </summary>
        public static Task<HumanSwipeTrace?> MicroDownAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vh = ViewportHeight(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Down,
                    Mode = HumanSwipeMode.Micro,

                    SpeedFactor = NextDouble(0.85, 1.15),
                    DistancePx = NextInt((int)(vh * 0.07), (int)(vh * 0.15)),
                    Steps = NextInt(16, 32),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = true,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.4, 0.9),

                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = 3
                },
                cancellationToken);
        }

        /// <summary>
        /// 快速扫描一次。多数情况下是 Strong Fling，少数是 Preview 或 VeryStrong。
        /// </summary>
        public static Task<HumanSwipeTrace?> FastScanOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            double r = NextDouble();

            if (r < 0.18)
                return PreviewUpAsync(page, cdp, cancellationToken);

            if (r < 0.82)
                return FlingUpAsync(page, cdp, FlingStrength.Strong, cancellationToken);

            return FlingUpAsync(page, cdp, FlingStrength.VeryStrong, cancellationToken);
        }

        #endregion

        #region 横向滑动

        /// <summary>
        /// 页面级横向左滑。如果页面本身不支持横向滚动，建议用 SwipeElementLeftAsync。
        /// </summary>
        public static Task<HumanSwipeTrace?> SwipeLeftAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vw = ViewportWidth(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Left,
                    Mode = HumanSwipeMode.Preview,

                    SpeedFactor = NextDouble(1.0, 1.35),
                    DistancePx = NextInt((int)(vw * 0.45), (int)(vw * 0.68)),
                    Steps = NextInt(20, 34),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.8, 1.6),

                    VerifyScrollChanged = false,
                    CheckScrollableBeforeSwipe = false
                },
                cancellationToken);
        }

        /// <summary>
        /// 页面级横向右滑。如果页面本身不支持横向滚动，建议用 SwipeElementRightAsync。
        /// </summary>
        public static Task<HumanSwipeTrace?> SwipeRightAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            int vw = ViewportWidth(page);

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Right,
                    Mode = HumanSwipeMode.Preview,

                    SpeedFactor = NextDouble(1.0, 1.35),
                    DistancePx = NextInt((int)(vw * 0.42), (int)(vw * 0.64)),
                    Steps = NextInt(20, 34),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.8, 1.6),

                    VerifyScrollChanged = false,
                    CheckScrollableBeforeSwipe = false
                },
                cancellationToken);
        }

        #endregion

        #region 元素相关

        /// <summary>
        /// 把元素滑动到屏幕舒适区。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> MoveToElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            float comfortTopRatio = 0.22f,
            float comfortBottomRatio = 0.72f,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (locator == null)
                throw new ArgumentNullException(nameof(locator));

            return HumanSwipeEmulator.SwipeToElementAsync(
                page,
                cdp,
                locator,
                maxSwipes,
                comfortTopRatio,
                comfortBottomRatio,
                cancellationToken);
        }

        /// <summary>
        /// 把元素滑动到屏幕舒适区。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> MoveToElementAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            int maxSwipes = 10,
            float comfortTopRatio = 0.22f,
            float comfortBottomRatio = 0.72f,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (element == null)
                throw new ArgumentNullException(nameof(element));

            return HumanSwipeEmulator.SwipeToElementAsync(
                page,
                cdp,
                element,
                maxSwipes,
                comfortTopRatio,
                comfortBottomRatio,
                cancellationToken);
        }

        /// <summary>
        /// 把无素滑动到屏幕可见区域
        /// </summary>
        /// <param name="page"></param>
        /// <param name="cdp"></param>
        /// <param name="locator"></param>
        /// <param name="maxSwipes"></param>
        /// <param name="visibleMarginPx"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static Task<List<HumanSwipeTrace>> MoveToElementVisibleAsync(
        IPage page,
        ICDPSession cdp,
        ILocator locator,
        int maxSwipes = 8,
        float visibleMarginPx = 8f,
        CancellationToken cancellationToken = default)
        {
            return HumanSwipeEmulator.SwipeToElementVisibleAsync(
                page,
                cdp,
                locator,
                maxSwipes,
                visibleMarginPx,
                cancellationToken);
        }

        /// <summary>
        /// 元素内部左滑，适合 Banner / Swiper / 横向卡片。
        /// </summary>
        public static Task<HumanSwipeTrace?> SwipeElementLeftAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (locator == null)
                throw new ArgumentNullException(nameof(locator));

            int vw = ViewportWidth(page);

            return HumanSwipeEmulator.SwipeInsideElementAsync(
                page,
                cdp,
                locator,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Left,
                    Mode = HumanSwipeMode.Preview,

                    SpeedFactor = NextDouble(1.0, 1.35),
                    DistancePx = NextInt((int)(vw * 0.42), (int)(vw * 0.66)),
                    Steps = NextInt(20, 34),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.8, 1.6),

                    VerifyScrollChanged = false,
                    CheckScrollableBeforeSwipe = false,
                    SafeMargin = 8
                },
                cancellationToken);
        }

        /// <summary>
        /// 元素内部右滑，适合 Banner / Swiper / 横向卡片。
        /// </summary>
        public static Task<HumanSwipeTrace?> SwipeElementRightAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (locator == null)
                throw new ArgumentNullException(nameof(locator));

            int vw = ViewportWidth(page);

            return HumanSwipeEmulator.SwipeInsideElementAsync(
                page,
                cdp,
                locator,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Right,
                    Mode = HumanSwipeMode.Preview,

                    SpeedFactor = NextDouble(1.0, 1.35),
                    DistancePx = NextInt((int)(vw * 0.38), (int)(vw * 0.62)),
                    Steps = NextInt(20, 34),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.8, 1.6),

                    VerifyScrollChanged = false,
                    CheckScrollableBeforeSwipe = false,
                    SafeMargin = 8
                },
                cancellationToken);
        }

        /// <summary>
        /// 元素内部左滑，适合 Banner / Swiper / 横向卡片。
        /// </summary>
        public static Task<HumanSwipeTrace?> SwipeElementLeftAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (element == null)
                throw new ArgumentNullException(nameof(element));

            int vw = ViewportWidth(page);

            return HumanSwipeEmulator.SwipeInsideElementAsync(
                page,
                cdp,
                element,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Left,
                    Mode = HumanSwipeMode.Preview,

                    SpeedFactor = NextDouble(1.0, 1.35),
                    DistancePx = NextInt((int)(vw * 0.42), (int)(vw * 0.66)),
                    Steps = NextInt(20, 34),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.8, 1.6),

                    VerifyScrollChanged = false,
                    CheckScrollableBeforeSwipe = false,
                    SafeMargin = 8
                },
                cancellationToken);
        }

        /// <summary>
        /// 元素内部右滑，适合 Banner / Swiper / 横向卡片。
        /// </summary>
        public static Task<HumanSwipeTrace?> SwipeElementRightAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (element == null)
                throw new ArgumentNullException(nameof(element));

            int vw = ViewportWidth(page);

            return HumanSwipeEmulator.SwipeInsideElementAsync(
                page,
                cdp,
                element,
                new HumanSwipeOptions
                {
                    Direction = HumanSwipeDirection.Right,
                    Mode = HumanSwipeMode.Preview,

                    SpeedFactor = NextDouble(1.0, 1.35),
                    DistancePx = NextInt((int)(vw * 0.38), (int)(vw * 0.62)),
                    Steps = NextInt(20, 34),

                    HoldBeforeMove = true,
                    HoldBeforeEnd = false,

                    UseBezierCurve = true,
                    UseJitter = true,
                    MaxJitter = NextDouble(0.8, 1.6),

                    VerifyScrollChanged = false,
                    CheckScrollableBeforeSwipe = false,
                    SafeMargin = 8
                },
                cancellationToken);
        }

        #endregion

        #region 自定义动作

        /// <summary>
        /// 直接传入自定义参数，仍然通过 HumanSwipeOperator 执行。
        /// </summary>
        public static Task<HumanSwipeTrace?> CustomAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            return HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                options,
                cancellationToken);
        }

        /// <summary>
        /// 根据元素中心位置自动选择微调方向。
        /// centerY 小于 comfortTop 时向下拉；大于 comfortBottom 时向上推。
        /// </summary>
        public static Task<HumanSwipeTrace?> MicroAdjustToComfortAsync(
            IPage page,
            ICDPSession cdp,
            double centerY,
            double comfortTop,
            double comfortBottom,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (centerY < comfortTop)
                return MicroDownAsync(page, cdp, cancellationToken);

            if (centerY > comfortBottom)
                return MicroUpAsync(page, cdp, cancellationToken);

            return Task.FromResult<HumanSwipeTrace?>(null);
        }

        #endregion

        #region 内部配置/工具

        private sealed class FlingConfig
        {
            public double MinDistanceRatio { get; init; }
            public double MaxDistanceRatio { get; init; }
            public int MinSteps { get; init; }
            public int MaxSteps { get; init; }
            public double MinSpeed { get; init; }
            public double MaxSpeed { get; init; }
            public double MinJitter { get; init; }
            public double MaxJitter { get; init; }
        }

        private static FlingConfig GetFlingConfig(FlingStrength strength)
        {
            return strength switch
            {
                FlingStrength.Soft => new FlingConfig
                {
                    MinDistanceRatio = 0.45,
                    MaxDistanceRatio = 0.62,
                    MinSteps = 10,
                    MaxSteps = 16,
                    MinSpeed = 1.6,
                    MaxSpeed = 2.1,
                    MinJitter = 0.7,
                    MaxJitter = 1.2
                },

                FlingStrength.Normal => new FlingConfig
                {
                    MinDistanceRatio = 0.58,
                    MaxDistanceRatio = 0.78,
                    MinSteps = 8,
                    MaxSteps = 13,
                    MinSpeed = 2.0,
                    MaxSpeed = 2.6,
                    MinJitter = 0.6,
                    MaxJitter = 1.1
                },

                FlingStrength.Strong => new FlingConfig
                {
                    MinDistanceRatio = 0.72,
                    MaxDistanceRatio = 0.92,
                    MinSteps = 7,
                    MaxSteps = 11,
                    MinSpeed = 2.3,
                    MaxSpeed = 2.9,
                    MinJitter = 0.5,
                    MaxJitter = 1.0
                },

                FlingStrength.VeryStrong => new FlingConfig
                {
                    MinDistanceRatio = 0.82,
                    MaxDistanceRatio = 1.05,
                    MinSteps = 6,
                    MaxSteps = 9,
                    MinSpeed = 2.6,
                    MaxSpeed = 3.0,
                    MinJitter = 0.3,
                    MaxJitter = 0.8
                },

                _ => new FlingConfig
                {
                    MinDistanceRatio = 0.58,
                    MaxDistanceRatio = 0.78,
                    MinSteps = 8,
                    MaxSteps = 13,
                    MinSpeed = 2.0,
                    MaxSpeed = 2.6,
                    MinJitter = 0.6,
                    MaxJitter = 1.1
                }
            };
        }

        private static FlingStrength PickRandomFlingStrength()
        {
            double r = NextDouble();

            if (r < 0.15)
                return FlingStrength.Soft;

            if (r < 0.65)
                return FlingStrength.Normal;

            if (r < 0.92)
                return FlingStrength.Strong;

            return FlingStrength.VeryStrong;
        }

        private static async Task DelayAfterTraceAsync(
            HumanSwipeTrace? trace,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken)
        {
            int delay = NextInt(options.MinDelayAfterSwipeMs, options.MaxDelayAfterSwipeMs);

            if (trace?.Mode == HumanSwipeMode.Reading)
                delay += NextInt(600, 1800);

            if (trace?.Mode == HumanSwipeMode.Fling)
                delay += NextInt(500, 1400);

            if (trace?.Mode == HumanSwipeMode.Micro)
                delay = NextInt(220, 620);

            await Task.Delay(delay, cancellationToken);
        }

        private static int ViewportHeight(IPage page)
        {
            return page.ViewportSize?.Height ?? 800;
        }

        private static int ViewportWidth(IPage page)
        {
            return page.ViewportSize?.Width ?? 390;
        }

        private static void ValidatePageAndCdp(IPage page, ICDPSession cdp)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));

            if (cdp == null)
                throw new ArgumentNullException(nameof(cdp));

            if (page.IsClosed)
                throw new InvalidOperationException("Page is closed.");
        }

        private static int NextInt(int min, int max)
        {
            if (max < min)
                (min, max) = (max, min);

            return RandomLocal.Value!.Next(min, max + 1);
        }

        private static double NextDouble()
        {
            return RandomLocal.Value!.NextDouble();
        }

        private static double NextDouble(double min, double max)
        {
            if (max < min)
                (min, max) = (max, min);

            return min + RandomLocal.Value!.NextDouble() * (max - min);
        }

        #endregion
    }
}
