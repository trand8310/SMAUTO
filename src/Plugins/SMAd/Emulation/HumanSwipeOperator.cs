using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
    /// HumanSwipeOperator 的高层行为配置。
    ///
    /// 这个类负责“连续行为”的拟人化；HumanSwipeEmulator 负责“单次轨迹”的拟人化。
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
        public double BackReviewChance => Math.Max(
            0,
            1.0
            - ReadingChance
            - PreviewChance
            - FlingNormalChance
            - FlingStrongChance
            - FlingVeryStrongChance
            - MicroChance);

        public int DefaultViewportWidth { get; set; } = 390;
        public int DefaultViewportHeight { get; set; } = 800;

        /// <summary>
        /// 同一个浏览会话的行为状态。
        /// 建议一个账号/一个页面浏览任务复用同一个 Session，这样起点、速度、停顿会更像同一个人。
        /// </summary>
        public HumanBrowseSessionState? Session { get; set; }

        /// <summary>
        /// true 时，Operator 会把 Session 的 StyleProfile 写入每次 HumanSwipeOptions，保持同一用户的手势习惯。
        /// </summary>
        public bool UseSessionBrowseModel { get; set; } = true;

        /// <summary>
        /// 动作后停顿倍率。0.5 更快，1.5 更慢。
        /// </summary>
        public double DelayFactor { get; set; } = 1.0;

        /// <summary>
        /// 滑动失败后是否允许 HumanSwipeEmulator 使用原生 ScrollIntoView 兜底。
        /// 需要全链路触摸拟人时保持 false。
        /// </summary>
        public bool AllowNativeScrollFallback { get; set; } = false;

        /// <summary>
        /// 连续浏览时，随着 Fatigue 上升，是否自动放慢动作并增加停顿。
        /// </summary>
        public bool EnableFatigueDelay { get; set; } = true;

        /// <summary>
        /// 是否在随机浏览中允许向下回看。
        /// </summary>
        public bool AllowBackReview { get; set; } = true;

        /// <summary>
        /// 长观察停顿概率。
        /// </summary>
        public double LongObserveChance { get; set; } = 0.10;

        /// <summary>
        /// 日志回调。
        /// </summary>
        public Action<string>? Log { get; set; }
    }

    /// <summary>
    /// 时间范围配置，支持毫秒、秒、分钟。
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
    /// HumanSwipeEmulator 的高层操作器。
    ///
    /// 设计原则：
    /// 1. HumanSwipeEmulator 负责单次轨迹真实：坐标、速度、触点、滚动验证。
    /// 2. HumanSwipeOperator 负责连续行为真实：意图、概率、停顿、回看、限时浏览、找目标。
    /// 3. 通过 HumanBrowseSessionState 让连续动作保持同一个人的习惯。
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

            var action = PickBrowseAction(options);
            options.Log?.Invoke($"BrowseOnce action={action.Intent}, direction={action.Direction}, strength={action.Strength}");

            return RunActionAsync(page, cdp, action, options, cancellationToken);
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
            return SwipeByIntentAsync(page, cdp, intent, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        /// <summary>
        /// 按指定意图执行一次动作，并使用指定 Operator 配置/Session。
        /// </summary>
        public static Task<HumanSwipeTrace?> SwipeByIntentAsync(
            IPage page,
            ICDPSession cdp,
            SwipeIntent intent,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            ValidatePageAndCdp(page, cdp);

            var action = intent switch
            {
                SwipeIntent.Reading => new SwipeActionPlan(SwipeIntent.Reading, HumanSwipeDirection.Up, null),
                SwipeIntent.Preview => new SwipeActionPlan(SwipeIntent.Preview, HumanSwipeDirection.Up, null),
                SwipeIntent.Fling => new SwipeActionPlan(SwipeIntent.Fling, HumanSwipeDirection.Up, PickRandomFlingStrength()),
                SwipeIntent.MicroAdjust => new SwipeActionPlan(SwipeIntent.MicroAdjust, HumanSwipeDirection.Up, null),
                SwipeIntent.BackReview => new SwipeActionPlan(SwipeIntent.BackReview, HumanSwipeDirection.Down, null),
                SwipeIntent.FastScan => new SwipeActionPlan(SwipeIntent.FastScan, HumanSwipeDirection.Up, null),
                _ => new SwipeActionPlan(SwipeIntent.Preview, HumanSwipeDirection.Up, null)
            };

            return RunActionAsync(page, cdp, action, options, cancellationToken);
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
            return RandomFromIntentsAsync(page, cdp, intents, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        /// <summary>
        /// 从多个意图中随机选择一个执行，并使用指定 Operator 配置/Session。
        /// </summary>
        public static Task<HumanSwipeTrace?> RandomFromIntentsAsync(
            IPage page,
            ICDPSession cdp,
            IReadOnlyList<SwipeIntent> intents,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (intents == null || intents.Count == 0)
                return BrowseOnceAsync(page, cdp, options, cancellationToken);

            var intent = intents[NextInt(0, intents.Count - 1)];
            return SwipeByIntentAsync(page, cdp, intent, options, cancellationToken);
        }

        #endregion

        #region 连续动作

        /// <summary>
        /// 连续随机浏览多次。
        /// 每次动作和动作后停顿都会随机，并复用同一个 Session。
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
            NormalizeRange(ref minTimes, ref maxTimes, 0);

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
            NormalizeRange(ref minTimes, ref maxTimes, 0);

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
                    trace = await ReadingUpAsync(page, cdp, options, cancellationToken);
                else if (r < 0.88)
                    trace = await MicroUpAsync(page, cdp, options, cancellationToken);
                else
                    trace = await PreviewUpAsync(page, cdp, options, cancellationToken);

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
            NormalizeRange(ref minTimes, ref maxTimes, 0);

            var traces = new List<HumanSwipeTrace>();
            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await FastScanOnceAsync(page, cdp, options, cancellationToken);

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
            NormalizeRange(ref minTimes, ref maxTimes, 0);

            var traces = new List<HumanSwipeTrace>();
            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                HumanSwipeTrace? trace = NextDouble() < 0.75
                    ? await PreviewDownAsync(page, cdp, options, cancellationToken)
                    : await ReadingDownAsync(page, cdp, options, cancellationToken);

                if (trace != null)
                    traces.Add(trace);

                await DelayAfterTraceAsync(trace, options, cancellationToken);
            }

            return traces;
        }

        /// <summary>
        /// 连续随机向上滑动。某一次滑动失败、页面滑不动、到底、或者返回 null，就立即停止。
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
            NormalizeRange(ref minTimes, ref maxTimes, 0);

            var traces = new List<HumanSwipeTrace>();
            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await RandomUpOnceAsync(page, cdp, options, cancellationToken);

                if (trace == null)
                    break;

                traces.Add(trace);
                await DelayAfterTraceAsync(trace, options, cancellationToken);
            }

            return traces;
        }

        /// <summary>
        /// 连续随机向上滑动指定最大次数。滑不动就停止。
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
        /// 单次随机向上动作。只做向上，不做向下回看。
        /// </summary>
        public static Task<HumanSwipeTrace?> RandomUpOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return RandomUpOnceAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        /// <summary>
        /// 单次随机向上动作。只做向上，不做向下回看。
        /// </summary>
        public static Task<HumanSwipeTrace?> RandomUpOnceAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            double r = NextDouble();

            if (r < 0.08)
                return ReadingUpAsync(page, cdp, options, cancellationToken);

            if (r < 0.42)
                return PreviewUpAsync(page, cdp, options, cancellationToken);

            if (r < 0.72)
                return FlingUpAsync(page, cdp, FlingStrength.Normal, options, cancellationToken);

            if (r < 0.90)
                return FlingUpAsync(page, cdp, FlingStrength.Strong, options, cancellationToken);

            if (r < 0.96)
                return FlingUpAsync(page, cdp, FlingStrength.VeryStrong, options, cancellationToken);

            if (r < 0.99)
                return LongFlingUpAsync(page, cdp, options, cancellationToken);

            return MicroUpAsync(page, cdp, options, cancellationToken);
        }

        /// <summary>
        /// 单次随机向下动作。只做向下，不做向上回看。
        /// </summary>
        public static Task<HumanSwipeTrace?> RandomDownOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return RandomDownOnceAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        /// <summary>
        /// 单次随机向下动作。只做向下，不做向上回看。
        /// </summary>
        public static Task<HumanSwipeTrace?> RandomDownOnceAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            double r = NextDouble();

            if (r < 0.08)
                return ReadingDownAsync(page, cdp, options, cancellationToken);

            if (r < 0.42)
                return PreviewDownAsync(page, cdp, options, cancellationToken);

            if (r < 0.72)
                return FlingDownAsync(page, cdp, FlingStrength.Normal, options, cancellationToken);

            if (r < 0.90)
                return FlingDownAsync(page, cdp, FlingStrength.Strong, options, cancellationToken);

            if (r < 0.96)
                return FlingDownAsync(page, cdp, FlingStrength.VeryStrong, options, cancellationToken);

            if (r < 0.99)
                return LongFlingDownAsync(page, cdp, options, cancellationToken);

            return MicroDownAsync(page, cdp, options, cancellationToken);
        }

        /// <summary>
        /// 连续随机向上滑动，并支持自定义停止验证回调。
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

            NormalizeRange(ref minTimes, ref maxTimes, 0);
            var traces = new List<HumanSwipeTrace>();
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

                var trace = await RandomUpOnceAsync(page, cdp, options, cancellationToken);

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
        /// </summary>
        public static Task<List<HumanSwipeTrace>> RandomUpUntilAsync(
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

            return RandomUpUntilAsync(
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
        /// </summary>
        public static Task<List<HumanSwipeTrace>> RandomUpUntilAsync(
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

            return RandomUpUntilAsync(
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
        /// 连续随机向上滑动，并支持自定义停止验证回调。回调包含当前已完成滑动次数。
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

            NormalizeRange(ref minTimes, ref maxTimes, 0);
            var traces = new List<HumanSwipeTrace>();
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

                var trace = await RandomUpOnceAsync(page, cdp, options, cancellationToken);

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

        #endregion

        #region 无规则浏览/限时浏览

        /// <summary>
        /// 随机无规则乱序浏览一次。
        /// 模拟“反复查找某个东西”的浏览行为：可能向上、向下、微调、快速扫描、强甩、短暂停顿。
        /// </summary>
        public static async Task<HumanSwipeTrace?> ChaoticBrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return await ChaoticBrowseOnceAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        /// <summary>
        /// 随机无规则乱序浏览一次，并使用指定 Operator 配置/Session。
        /// </summary>
        public static async Task<HumanSwipeTrace?> ChaoticBrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            double r = NextDouble();

            if (r < 0.24)
                return await PreviewUpAsync(page, cdp, options, cancellationToken);

            if (r < 0.42)
            {
                var strength = NextDouble() < 0.72 ? FlingStrength.Normal : FlingStrength.Strong;
                return await FlingUpAsync(page, cdp, strength, options, cancellationToken);
            }

            if (r < 0.54)
                return await ReadingUpAsync(page, cdp, options, cancellationToken);

            if (r < 0.66)
                return await PreviewDownAsync(page, cdp, options, cancellationToken);

            if (r < 0.74)
            {
                var strength = NextDouble() < 0.80 ? FlingStrength.Normal : FlingStrength.Strong;
                return await FlingDownAsync(page, cdp, strength, options, cancellationToken);
            }

            if (r < 0.84)
            {
                return NextDouble() < 0.62
                    ? await MicroUpAsync(page, cdp, options, cancellationToken)
                    : await MicroDownAsync(page, cdp, options, cancellationToken);
            }

            if (r < 0.93)
                return await FastScanOnceAsync(page, cdp, options, cancellationToken);

            if (r < 0.98)
                return await LongFlingUpAsync(page, cdp, options, cancellationToken);

            // 偶尔停顿，像人在观察页面。
            EnsureSession(options).RecordSwipe(null);
            await Task.Delay(NextInt(600, 1800), cancellationToken);
            return null;
        }

        /// <summary>
        /// 随机无规则乱序浏览多次。
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
            NormalizeRange(ref minTimes, ref maxTimes, 0);

            var traces = new List<HumanSwipeTrace>();
            int times = NextInt(minTimes, maxTimes);

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                var trace = await ChaoticBrowseOnceAsync(page, cdp, options, cancellationToken);

                if (trace != null)
                    traces.Add(trace);

                await DelayAfterChaoticTraceAsync(trace, options, cancellationToken);
            }

            return traces;
        }

        /// <summary>
        /// 随机无规则乱序查找。stopVerifier 返回 true 时停止。
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

            NormalizeRange(ref minTimes, ref maxTimes, 0);
            maxContinuousNoMove = Math.Max(1, maxContinuousNoMove);

            var traces = new List<HumanSwipeTrace>();
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

                var trace = await ChaoticBrowseOnceAsync(page, cdp, options, cancellationToken);

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

                if (continuousNoMove >= maxContinuousNoMove)
                    break;
            }

            return traces;
        }

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

            NormalizeRange(ref minTimes, ref maxTimes, 0);
            maxContinuousNoMove = Math.Max(1, maxContinuousNoMove);

            var traces = new List<HumanSwipeTrace>();
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

                var trace = await ChaoticBrowseOnceAsync(page, cdp, options, cancellationToken);

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
                durationRange,
                _ => stopVerifier(),
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

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
                durationRange,
                p => Task.FromResult(stopVerifier(p)),
                options,
                maxContinuousNoMove,
                checkBeforeEachAction,
                checkAfterEachAction,
                cancellationToken);
        }

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

        /// <summary>
        /// 按指定时间进行无规则浏览。
        /// </summary>
        public static Task<List<HumanSwipeTrace>> TimedChaoticBrowseAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            HumanSwipeOperatorOptions? options = null,
            int maxContinuousNoMove = 4,
            CancellationToken cancellationToken = default)
        {
            return TimedChaoticBrowseUntilAsync(
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

            maxContinuousNoMove = Math.Max(1, maxContinuousNoMove);

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

                var trace = await TimedChaoticBrowseOnceAsync(page, cdp, remaining, options, cancellationToken);

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

                await DelayAfterTimedChaoticTraceAsync(trace, options, remainAfterAction, cancellationToken);

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

            maxContinuousNoMove = Math.Max(1, maxContinuousNoMove);

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

                var trace = await TimedChaoticBrowseOnceAsync(page, cdp, remaining, options, cancellationToken);

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

                await DelayAfterTimedChaoticTraceAsync(trace, options, remainAfterAction, cancellationToken);

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

        private static async Task<HumanSwipeTrace?> TimedChaoticBrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan remaining,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken)
        {
            ValidatePageAndCdp(page, cdp);

            double secondsLeft = remaining.TotalSeconds;
            double r = NextDouble();

            if (secondsLeft <= 1.2)
            {
                EnsureSession(options).RecordSwipe(null);
                await Task.Delay(
                    Math.Max(80, Math.Min(600, (int)(remaining.TotalMilliseconds * 0.60))),
                    cancellationToken);

                return null;
            }

            if (secondsLeft <= 3.0)
            {
                if (r < 0.35)
                    return await MicroUpAsync(page, cdp, options, cancellationToken);

                if (r < 0.55)
                    return await MicroDownAsync(page, cdp, options, cancellationToken);

                if (r < 0.80)
                    return await PreviewUpAsync(page, cdp, options, cancellationToken);

                if (r < 0.92)
                    return await PreviewDownAsync(page, cdp, options, cancellationToken);

                EnsureSession(options).RecordSwipe(null);
                await Task.Delay(NextInt(200, 800), cancellationToken);
                return null;
            }

            if (secondsLeft <= 8.0)
            {
                if (r < 0.24)
                    return await PreviewUpAsync(page, cdp, options, cancellationToken);

                if (r < 0.40)
                    return await FlingUpAsync(page, cdp, FlingStrength.Normal, options, cancellationToken);

                if (r < 0.52)
                    return await ReadingUpAsync(page, cdp, options, cancellationToken);

                if (r < 0.66)
                    return await PreviewDownAsync(page, cdp, options, cancellationToken);

                if (r < 0.75)
                    return await FlingDownAsync(page, cdp, FlingStrength.Normal, options, cancellationToken);

                if (r < 0.88)
                {
                    return NextDouble() < 0.58
                        ? await MicroUpAsync(page, cdp, options, cancellationToken)
                        : await MicroDownAsync(page, cdp, options, cancellationToken);
                }

                if (r < 0.96)
                    return await FastScanOnceAsync(page, cdp, options, cancellationToken);

                EnsureSession(options).RecordSwipe(null);
                await Task.Delay(NextInt(400, 1200), cancellationToken);
                return null;
            }

            return await ChaoticBrowseOnceAsync(page, cdp, options, cancellationToken);
        }

        #endregion

        #region 垂直滑动：阅读/预览/甩动/微调

        public static Task<HumanSwipeTrace?> ReadingUpAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return ReadingUpAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> ReadingUpAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Up, HumanSwipeMode.Reading);
            swipe.SpeedFactor = NextDouble(0.70, 0.95);
            swipe.DistancePx = NextInt((int)(vh * 0.18), (int)(vh * 0.32));
            swipe.Steps = NextInt(46, 72);
            swipe.HoldBeforeMove = true;
            swipe.HoldBeforeEnd = true;
            swipe.MaxJitter = NextDouble(0.8, 1.6);
            swipe.ScrollChangedMinDelta = 5;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> ReadingDownAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return ReadingDownAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> ReadingDownAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Down, HumanSwipeMode.Reading);
            swipe.SpeedFactor = NextDouble(0.70, 0.95);
            swipe.DistancePx = NextInt((int)(vh * 0.16), (int)(vh * 0.30));
            swipe.Steps = NextInt(46, 72);
            swipe.HoldBeforeMove = true;
            swipe.HoldBeforeEnd = true;
            swipe.MaxJitter = NextDouble(0.8, 1.6);
            swipe.ScrollChangedMinDelta = 5;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> PreviewUpAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return PreviewUpAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> PreviewUpAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Up, HumanSwipeMode.Preview);
            swipe.SpeedFactor = NextDouble(1.0, 1.35);
            swipe.DistancePx = NextInt((int)(vh * 0.30), (int)(vh * 0.48));
            swipe.Steps = NextInt(24, 40);
            swipe.HoldBeforeMove = true;
            swipe.HoldBeforeEnd = false;
            swipe.MaxJitter = NextDouble(1.2, 2.0);
            swipe.ScrollChangedMinDelta = 8;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> PreviewDownAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return PreviewDownAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> PreviewDownAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Down, HumanSwipeMode.Preview);
            swipe.SpeedFactor = NextDouble(1.0, 1.35);
            swipe.DistancePx = NextInt((int)(vh * 0.26), (int)(vh * 0.44));
            swipe.Steps = NextInt(24, 40);
            swipe.HoldBeforeMove = true;
            swipe.HoldBeforeEnd = false;
            swipe.MaxJitter = NextDouble(1.2, 2.0);
            swipe.ScrollChangedMinDelta = 8;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> FlingUpAsync(
            IPage page,
            ICDPSession cdp,
            FlingStrength strength = FlingStrength.Normal,
            CancellationToken cancellationToken = default)
        {
            return FlingUpAsync(page, cdp, strength, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> FlingUpAsync(
            IPage page,
            ICDPSession cdp,
            FlingStrength strength,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);
            var cfg = GetFlingConfig(strength);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Up, HumanSwipeMode.Fling);
            swipe.SpeedFactor = NextDouble(cfg.MinSpeed, cfg.MaxSpeed);
            swipe.DistancePx = NextInt((int)(vh * cfg.MinDistanceRatio), (int)(vh * cfg.MaxDistanceRatio));
            swipe.Steps = NextInt(cfg.MinSteps, cfg.MaxSteps);
            swipe.HoldBeforeMove = false;
            swipe.HoldBeforeEnd = false;
            swipe.MaxJitter = NextDouble(cfg.MinJitter, cfg.MaxJitter);
            swipe.ScrollChangedMinDelta = 12;
            swipe.EndPullBackChance = 0.0;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> FlingDownAsync(
            IPage page,
            ICDPSession cdp,
            FlingStrength strength = FlingStrength.Normal,
            CancellationToken cancellationToken = default)
        {
            return FlingDownAsync(page, cdp, strength, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> FlingDownAsync(
            IPage page,
            ICDPSession cdp,
            FlingStrength strength,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);
            var cfg = GetFlingConfig(strength);

            double minDistance = Math.Max(0.35, cfg.MinDistanceRatio - 0.06);
            double maxDistance = Math.Max(minDistance + 0.05, cfg.MaxDistanceRatio - 0.08);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Down, HumanSwipeMode.Fling);
            swipe.SpeedFactor = NextDouble(cfg.MinSpeed, cfg.MaxSpeed);
            swipe.DistancePx = NextInt((int)(vh * minDistance), (int)(vh * maxDistance));
            swipe.Steps = NextInt(cfg.MinSteps, cfg.MaxSteps);
            swipe.HoldBeforeMove = false;
            swipe.HoldBeforeEnd = false;
            swipe.MaxJitter = NextDouble(cfg.MinJitter, cfg.MaxJitter);
            swipe.ScrollChangedMinDelta = 12;
            swipe.EndPullBackChance = 0.0;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> LongFlingUpAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return LongFlingUpAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> LongFlingUpAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Up, HumanSwipeMode.Fling);
            swipe.SpeedFactor = NextDouble(2.7, 3.0);
            swipe.DistancePx = NextInt((int)(vh * 0.90), (int)(vh * 1.20));
            swipe.Steps = NextInt(5, 8);
            swipe.HoldBeforeMove = false;
            swipe.HoldBeforeEnd = false;
            swipe.MaxJitter = NextDouble(0.2, 0.7);
            swipe.ScrollChangedMinDelta = 20;
            swipe.EndPullBackChance = 0.0;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> LongFlingDownAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return LongFlingDownAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> LongFlingDownAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Down, HumanSwipeMode.Fling);
            swipe.SpeedFactor = NextDouble(2.5, 2.9);
            swipe.DistancePx = NextInt((int)(vh * 0.72), (int)(vh * 1.00));
            swipe.Steps = NextInt(6, 9);
            swipe.HoldBeforeMove = false;
            swipe.HoldBeforeEnd = false;
            swipe.MaxJitter = NextDouble(0.2, 0.8);
            swipe.ScrollChangedMinDelta = 16;
            swipe.EndPullBackChance = 0.0;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> MicroUpAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Up, HumanSwipeMode.Micro);
            swipe.SpeedFactor = NextDouble(0.78, 1.15);
            swipe.DistancePx = NextInt((int)(vh * 0.06), (int)(vh * 0.16));
            swipe.Steps = NextInt(18, 32);
            swipe.HoldBeforeMove = true;
            swipe.HoldBeforeEnd = true;
            swipe.MaxJitter = NextDouble(0.35, 0.9);
            swipe.ScrollChangedMinDelta = 3;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> MicroDownAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return MicroDownAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> MicroDownAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vh = ViewportHeight(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Down, HumanSwipeMode.Micro);
            swipe.SpeedFactor = NextDouble(0.78, 1.15);
            swipe.DistancePx = NextInt((int)(vh * 0.05), (int)(vh * 0.14));
            swipe.Steps = NextInt(18, 32);
            swipe.HoldBeforeMove = true;
            swipe.HoldBeforeEnd = true;
            swipe.MaxJitter = NextDouble(0.35, 0.9);
            swipe.ScrollChangedMinDelta = 3;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> FastScanOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return FastScanOnceAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> FastScanOnceAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            return NextDouble() < 0.72
                ? FlingUpAsync(page, cdp, FlingStrength.Strong, options, cancellationToken)
                : LongFlingUpAsync(page, cdp, options, cancellationToken);
        }

        #endregion

        #region 横向滑动/元素动作

        public static Task<HumanSwipeTrace?> SwipeLeftAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return SwipeLeftAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeLeftAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vw = ViewportWidth(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Left, HumanSwipeMode.Preview);
            swipe.SpeedFactor = NextDouble(1.0, 1.35);
            swipe.DistancePx = NextInt((int)(vw * 0.38), (int)(vw * 0.62));
            swipe.Steps = NextInt(20, 34);
            swipe.HoldBeforeMove = true;
            swipe.HoldBeforeEnd = false;
            swipe.MaxJitter = NextDouble(0.8, 1.6);
            swipe.ScrollChangedMinDelta = 8;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeRightAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            return SwipeRightAsync(page, cdp, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeRightAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);
            int vw = ViewportWidth(page, options);

            var swipe = CreateBaseOptions(options, HumanSwipeDirection.Right, HumanSwipeMode.Preview);
            swipe.SpeedFactor = NextDouble(1.0, 1.35);
            swipe.DistancePx = NextInt((int)(vw * 0.34), (int)(vw * 0.58));
            swipe.Steps = NextInt(20, 34);
            swipe.HoldBeforeMove = true;
            swipe.HoldBeforeEnd = false;
            swipe.MaxJitter = NextDouble(0.8, 1.6);
            swipe.ScrollChangedMinDelta = 8;

            return RunPageSwipeAsync(page, cdp, swipe, options, cancellationToken);
        }

        public static Task<List<HumanSwipeTrace>> MoveToElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            float comfortTopRatio = 0.22f,
            float comfortBottomRatio = 0.72f,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (locator == null)
                throw new ArgumentNullException(nameof(locator));

            options ??= new HumanSwipeOperatorOptions();
            var session = options.Session ??= new HumanBrowseSessionState();

            using var scope = HumanSwipeEmulator.BeginStyleScope(session.StyleProfile);

            return HumanSwipeEmulator.SwipeToElementAsync(
                page,
                cdp,
                locator,
                maxSwipes,
                comfortTopRatio,
                comfortBottomRatio,
                cancellationToken);
        }
        public static Task<List<HumanSwipeTrace>> MoveToElementAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            int maxSwipes = 10,
            float comfortTopRatio = 0.22f,
            float comfortBottomRatio = 0.72f,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (element == null)
                throw new ArgumentNullException(nameof(element));

            options ??= new HumanSwipeOperatorOptions();
            var session = options.Session ??= new HumanBrowseSessionState();

            using var scope = HumanSwipeEmulator.BeginStyleScope(session.StyleProfile);

            return HumanSwipeEmulator.SwipeToElementAsync(
                page,
                cdp,
                element,
                maxSwipes,
                comfortTopRatio,
                comfortBottomRatio,
                cancellationToken);
        }






        public static Task<List<HumanSwipeTrace>> MoveToElementVisibleAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 8,
            float visibleMarginPx = 8f,
            HumanSwipeOperatorOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (locator == null)
                throw new ArgumentNullException(nameof(locator));

            options ??= new HumanSwipeOperatorOptions();
            var session = options.Session ??= new HumanBrowseSessionState();

            using var scope = HumanSwipeEmulator.BeginStyleScope(session.StyleProfile);

            return HumanSwipeEmulator.SwipeToElementVisibleAsync(
                page,
                cdp,
                locator,
                maxSwipes,
                visibleMarginPx,
                cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeElementLeftAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default)
        {
            return SwipeElementLeftAsync(page, cdp, locator, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeElementLeftAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (locator == null)
                throw new ArgumentNullException(nameof(locator));

            int vw = ViewportWidth(page, options);
            var swipe = CreateElementSwipeOptions(options, HumanSwipeDirection.Left, vw);

            return RunElementSwipeAsync(page, cdp, locator, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeElementRightAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default)
        {
            return SwipeElementRightAsync(page, cdp, locator, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeElementRightAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (locator == null)
                throw new ArgumentNullException(nameof(locator));

            int vw = ViewportWidth(page, options);
            var swipe = CreateElementSwipeOptions(options, HumanSwipeDirection.Right, vw);

            return RunElementSwipeAsync(page, cdp, locator, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeElementLeftAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            CancellationToken cancellationToken = default)
        {
            return SwipeElementLeftAsync(page, cdp, element, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeElementLeftAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (element == null)
                throw new ArgumentNullException(nameof(element));

            int vw = ViewportWidth(page, options);
            var swipe = CreateElementSwipeOptions(options, HumanSwipeDirection.Left, vw);

            return RunElementSwipeAsync(page, cdp, element, swipe, options, cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeElementRightAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            CancellationToken cancellationToken = default)
        {
            return SwipeElementRightAsync(page, cdp, element, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> SwipeElementRightAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (element == null)
                throw new ArgumentNullException(nameof(element));

            int vw = ViewportWidth(page, options);
            var swipe = CreateElementSwipeOptions(options, HumanSwipeDirection.Right, vw);

            return RunElementSwipeAsync(page, cdp, element, swipe, options, cancellationToken);
        }

        #endregion

        #region 自定义动作

        /// <summary>
        /// 直接传入自定义参数，仍然通过 HumanSwipeOperator 执行，并接入 Session。
        /// </summary>
        public static Task<HumanSwipeTrace?> CustomAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOptions options,
            CancellationToken cancellationToken = default)
        {
            return CustomAsync(page, cdp, options, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        /// <summary>
        /// 直接传入自定义参数，仍然通过 HumanSwipeOperator 执行，并接入指定 Session。
        /// </summary>
        public static Task<HumanSwipeTrace?> CustomAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOptions swipeOptions,
            HumanSwipeOperatorOptions operatorOptions,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (swipeOptions == null)
                throw new ArgumentNullException(nameof(swipeOptions));

            return RunPageSwipeAsync(page, cdp, swipeOptions, operatorOptions, cancellationToken);
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
            return MicroAdjustToComfortAsync(page, cdp, centerY, comfortTop, comfortBottom, new HumanSwipeOperatorOptions(), cancellationToken);
        }

        public static Task<HumanSwipeTrace?> MicroAdjustToComfortAsync(
            IPage page,
            ICDPSession cdp,
            double centerY,
            double comfortTop,
            double comfortBottom,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken = default)
        {
            ValidatePageAndCdp(page, cdp);

            if (centerY < comfortTop)
                return MicroDownAsync(page, cdp, options, cancellationToken);

            if (centerY > comfortBottom)
                return MicroUpAsync(page, cdp, options, cancellationToken);

            EnsureSession(options).RecordSwipe(null);
            return Task.FromResult<HumanSwipeTrace?>(null);
        }

        #endregion

        #region 内部动作构造

        private readonly struct SwipeActionPlan
        {
            public SwipeActionPlan(SwipeIntent intent, HumanSwipeDirection direction, FlingStrength? strength)
            {
                Intent = intent;
                Direction = direction;
                Strength = strength;
            }

            public SwipeIntent Intent { get; }
            public HumanSwipeDirection Direction { get; }
            public FlingStrength? Strength { get; }
        }

        private static SwipeActionPlan PickBrowseAction(HumanSwipeOperatorOptions options)
        {
            var weights = new List<(SwipeActionPlan plan, double weight)>
            {
                (new SwipeActionPlan(SwipeIntent.Reading, HumanSwipeDirection.Up, null), options.ReadingChance),
                (new SwipeActionPlan(SwipeIntent.Preview, HumanSwipeDirection.Up, null), options.PreviewChance),
                (new SwipeActionPlan(SwipeIntent.Fling, HumanSwipeDirection.Up, FlingStrength.Normal), options.FlingNormalChance),
                (new SwipeActionPlan(SwipeIntent.Fling, HumanSwipeDirection.Up, FlingStrength.Strong), options.FlingStrongChance),
                (new SwipeActionPlan(SwipeIntent.Fling, HumanSwipeDirection.Up, FlingStrength.VeryStrong), options.FlingVeryStrongChance),
                (new SwipeActionPlan(SwipeIntent.MicroAdjust, HumanSwipeDirection.Up, null), options.MicroChance)
            };

            if (options.AllowBackReview)
                weights.Add((new SwipeActionPlan(SwipeIntent.BackReview, HumanSwipeDirection.Down, null), options.BackReviewChance));

            double total = weights.Sum(x => Math.Max(0, x.weight));
            if (total <= 0.000001)
                return new SwipeActionPlan(SwipeIntent.Preview, HumanSwipeDirection.Up, null);

            double roll = NextDouble(0, total);
            double acc = 0;

            foreach (var item in weights)
            {
                double weight = Math.Max(0, item.weight);
                acc += weight;

                if (roll <= acc)
                    return item.plan;
            }

            return weights.Last().plan;
        }

        private static Task<HumanSwipeTrace?> RunActionAsync(
            IPage page,
            ICDPSession cdp,
            SwipeActionPlan action,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken)
        {
            return action.Intent switch
            {
                SwipeIntent.Reading when action.Direction == HumanSwipeDirection.Down => ReadingDownAsync(page, cdp, options, cancellationToken),
                SwipeIntent.Reading => ReadingUpAsync(page, cdp, options, cancellationToken),

                SwipeIntent.Preview when action.Direction == HumanSwipeDirection.Down => PreviewDownAsync(page, cdp, options, cancellationToken),
                SwipeIntent.Preview => PreviewUpAsync(page, cdp, options, cancellationToken),

                SwipeIntent.Fling when action.Direction == HumanSwipeDirection.Down => FlingDownAsync(page, cdp, action.Strength ?? FlingStrength.Normal, options, cancellationToken),
                SwipeIntent.Fling => FlingUpAsync(page, cdp, action.Strength ?? FlingStrength.Normal, options, cancellationToken),

                SwipeIntent.MicroAdjust when action.Direction == HumanSwipeDirection.Down => MicroDownAsync(page, cdp, options, cancellationToken),
                SwipeIntent.MicroAdjust => MicroUpAsync(page, cdp, options, cancellationToken),

                SwipeIntent.BackReview => PreviewDownAsync(page, cdp, options, cancellationToken),
                SwipeIntent.FastScan => FastScanOnceAsync(page, cdp, options, cancellationToken),

                _ => PreviewUpAsync(page, cdp, options, cancellationToken)
            };
        }

        private static HumanSwipeOptions CreateBaseOptions(
            HumanSwipeOperatorOptions operatorOptions,
            HumanSwipeDirection direction,
            HumanSwipeMode mode)
        {
            var session = EnsureSession(operatorOptions);

            return new HumanSwipeOptions
            {
                Direction = direction,
                Mode = mode,
                StyleProfile = operatorOptions.UseSessionBrowseModel ? session.StyleProfile : null,
                VerifyScrollChanged = true,
                CheckScrollableBeforeSwipe = true,
                UseBezierCurve = true,
                UseJitter = true,
                UseSmoothJitter = true,
                EnableCrossAxisDrift = true,
                EnableForceCurve = true,
                EnableTouchAreaCurve = true,
                EnableHesitationPause = true,
                EnableEndPullBack = true,
                EnableVisualConfirmPause = true,
                AllowNativeScrollFallback = operatorOptions.AllowNativeScrollFallback,
                Log = operatorOptions.Log
            };
        }

        private static HumanSwipeOptions CreateElementSwipeOptions(
            HumanSwipeOperatorOptions operatorOptions,
            HumanSwipeDirection direction,
            int viewportWidth)
        {
            var swipe = CreateBaseOptions(operatorOptions, direction, HumanSwipeMode.Preview);
            swipe.SpeedFactor = NextDouble(1.0, 1.35);
            swipe.DistancePx = direction == HumanSwipeDirection.Left
                ? NextInt((int)(viewportWidth * 0.42), (int)(viewportWidth * 0.66))
                : NextInt((int)(viewportWidth * 0.38), (int)(viewportWidth * 0.62));
            swipe.Steps = NextInt(20, 34);
            swipe.HoldBeforeMove = true;
            swipe.HoldBeforeEnd = false;
            swipe.MaxJitter = NextDouble(0.8, 1.6);
            swipe.VerifyScrollChanged = false;
            swipe.CheckScrollableBeforeSwipe = false;
            swipe.SafeMargin = 8;

            return swipe;
        }

        private static async Task<HumanSwipeTrace?> RunPageSwipeAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOptions swipeOptions,
            HumanSwipeOperatorOptions operatorOptions,
            CancellationToken cancellationToken)
        {
            ValidatePageAndCdp(page, cdp);

            if (swipeOptions == null)
                throw new ArgumentNullException(nameof(swipeOptions));

            var session = EnsureSession(operatorOptions);
            PrepareSwipeOptionsForSession(swipeOptions, operatorOptions, session);

            var trace = await HumanSwipeEmulator.SwipeAsync(
                page,
                cdp,
                swipeOptions,
                cancellationToken);

            session.RecordSwipe(trace);
            return trace;
        }

        private static async Task<HumanSwipeTrace?> RunElementSwipeAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            HumanSwipeOptions swipeOptions,
            HumanSwipeOperatorOptions operatorOptions,
            CancellationToken cancellationToken)
        {
            var session = EnsureSession(operatorOptions);
            PrepareSwipeOptionsForSession(swipeOptions, operatorOptions, session);

            var trace = await HumanSwipeEmulator.SwipeInsideElementAsync(
                page,
                cdp,
                locator,
                swipeOptions,
                cancellationToken);

            session.RecordSwipe(trace);
            return trace;
        }

        private static async Task<HumanSwipeTrace?> RunElementSwipeAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            HumanSwipeOptions swipeOptions,
            HumanSwipeOperatorOptions operatorOptions,
            CancellationToken cancellationToken)
        {
            var session = EnsureSession(operatorOptions);
            PrepareSwipeOptionsForSession(swipeOptions, operatorOptions, session);

            var trace = await HumanSwipeEmulator.SwipeInsideElementAsync(
                page,
                cdp,
                element,
                swipeOptions,
                cancellationToken);

            session.RecordSwipe(trace);
            return trace;
        }

        private static void PrepareSwipeOptionsForSession(
            HumanSwipeOptions swipeOptions,
            HumanSwipeOperatorOptions operatorOptions,
            HumanBrowseSessionState session)
        {
            if (operatorOptions.UseSessionBrowseModel)
                swipeOptions.StyleProfile ??= session.StyleProfile;

            swipeOptions.AllowNativeScrollFallback = operatorOptions.AllowNativeScrollFallback || swipeOptions.AllowNativeScrollFallback;

            if (operatorOptions.EnableFatigueDelay)
            {
                double fatigue = Math.Clamp(session.Fatigue, 0.0, 1.0);

                swipeOptions.SpeedFactor = Math.Clamp(
                    swipeOptions.SpeedFactor * (1.0 - fatigue * 0.12) * NextDouble(0.96, 1.04),
                    0.30,
                    3.0);

                if (swipeOptions.HesitationChance.HasValue)
                {
                    swipeOptions.HesitationChance = Math.Clamp(
                        swipeOptions.HesitationChance.Value + fatigue * 0.018,
                        0.0,
                        0.25);
                }
            }
        }

        #endregion

        #region 延迟/停止验证

        private static async Task DelayAfterTraceAsync(
            HumanSwipeTrace? trace,
            HumanSwipeOperatorOptions options,
            CancellationToken cancellationToken)
        {
            var session = EnsureSession(options);
            int delay = NextInt(options.MinDelayAfterSwipeMs, options.MaxDelayAfterSwipeMs);

            if (trace == null)
            {
                delay = NextInt(320, 1100);
            }
            else if (trace.Mode == HumanSwipeMode.Reading)
            {
                delay += NextInt(600, 1800);
            }
            else if (trace.Mode == HumanSwipeMode.Fling)
            {
                delay += NextInt(500, 1400);
            }
            else if (trace.Mode == HumanSwipeMode.Micro)
            {
                delay = NextInt(220, 620);
            }

            if (NextDouble() < Math.Clamp(options.LongObserveChance, 0, 0.8))
                delay += NextInt(700, 2200);

            if (options.EnableFatigueDelay)
            {
                double fatigueFactor = 1.0 + Math.Clamp(session.Fatigue, 0.0, 1.0) * 0.35;
                delay = (int)Math.Round(delay * fatigueFactor);
            }

            delay = (int)Math.Round(delay * Math.Clamp(options.DelayFactor, 0.25, 4.0));

            await Task.Delay(Math.Max(50, delay), cancellationToken);
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

            if (NextDouble() < Math.Clamp(options.LongObserveChance + 0.02, 0, 0.8))
                delay += NextInt(800, 2600);

            var session = EnsureSession(options);
            if (options.EnableFatigueDelay)
                delay = (int)Math.Round(delay * (1.0 + Math.Clamp(session.Fatigue, 0.0, 1.0) * 0.30));

            delay = (int)Math.Round(delay * Math.Clamp(options.DelayFactor, 0.25, 4.0));
            await Task.Delay(Math.Max(50, delay), cancellationToken);
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

            if (NextDouble() < Math.Clamp(options.LongObserveChance, 0, 0.8))
                delay += NextInt(700, 2200);

            var session = EnsureSession(options);
            if (options.EnableFatigueDelay)
                delay = (int)Math.Round(delay * (1.0 + Math.Clamp(session.Fatigue, 0.0, 1.0) * 0.25));

            delay = (int)Math.Round(delay * Math.Clamp(options.DelayFactor, 0.25, 4.0));

            int maxAllowed = Math.Max(50, (int)Math.Min(remaining.TotalMilliseconds, delay));
            await Task.Delay(maxAllowed, cancellationToken);
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
                return false;
            }
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
                    MinDistanceRatio = 0.70,
                    MaxDistanceRatio = 0.92,
                    MinSteps = 6,
                    MaxSteps = 10,
                    MinSpeed = 2.3,
                    MaxSpeed = 2.9,
                    MinJitter = 0.4,
                    MaxJitter = 1.0
                },

                FlingStrength.VeryStrong => new FlingConfig
                {
                    MinDistanceRatio = 0.82,
                    MaxDistanceRatio = 1.08,
                    MinSteps = 5,
                    MaxSteps = 8,
                    MinSpeed = 2.6,
                    MaxSpeed = 3.0,
                    MinJitter = 0.2,
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

            if (r < 0.18)
                return FlingStrength.Soft;

            if (r < 0.72)
                return FlingStrength.Normal;

            if (r < 0.94)
                return FlingStrength.Strong;

            return FlingStrength.VeryStrong;
        }

        private static HumanBrowseSessionState EnsureSession(HumanSwipeOperatorOptions options)
        {
            options.Session ??= new HumanBrowseSessionState();
            return options.Session;
        }

        private static int ViewportHeight(IPage page, HumanSwipeOperatorOptions? options = null)
        {
            return page.ViewportSize?.Height
                ?? options?.DefaultViewportHeight
                ?? 800;
        }

        private static int ViewportWidth(IPage page, HumanSwipeOperatorOptions? options = null)
        {
            return page.ViewportSize?.Width
                ?? options?.DefaultViewportWidth
                ?? 390;
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

        private static void NormalizeRange(ref int min, ref int max, int lowerBound)
        {
            if (min < lowerBound)
                min = lowerBound;

            if (max < min)
                max = min;
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

        private static int NextInt(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive)
                maxInclusive = minInclusive;

            return RandomLocal.Value!.Next(minInclusive, maxInclusive + 1);
        }

        private static double NextDouble()
        {
            return RandomLocal.Value!.NextDouble();
        }

        private static double NextDouble(double minInclusive, double maxInclusive)
        {
            if (maxInclusive < minInclusive)
                maxInclusive = minInclusive;

            return minInclusive + RandomLocal.Value!.NextDouble() * (maxInclusive - minInclusive);
        }

        #endregion
    }
}
