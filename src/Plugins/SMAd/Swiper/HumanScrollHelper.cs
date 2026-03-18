

using Microsoft.Playwright;

namespace SMAd.Swiper
{
    public enum HumanScrollMode
    {
        Auto = 0,
        Short = 1,
        Long = 2,
        Probe = 3,
        FineTune = 4
    }

    public static class HumanScrollHelper
    {
        private sealed class HumanScrollProfile
        {
            public HumanScrollMode Mode { get; set; }
            public int DistancePx { get; set; }
            public int PointCount { get; set; }
            public int DelayMs { get; set; }
            public float Jitter { get; set; }
            public int PauseMs { get; set; }
        }

        /// <summary>
        /// 真人级页面滚动。支持短滑 / 长滑 / 探测滑 / 微调滑 / 自动混合策略。
        /// </summary>
        public static async Task TouchPageScrollAsync(
            IPage page,
            ICDPSession client,
            int scrollCount,
            PageScrollDirection direction,
            Func<IPage, Task<bool>>? predexp = null,
            int timeDelay = 0,
            ScrollOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null || scrollCount <= 0)
                return;

            options ??= new ScrollOptions();

            try
            {
                int noMoveCount = 0;

                for (int i = 0; i < scrollCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (page.IsClosed)
                        break;

                    if (predexp != null)
                    {
                        try
                        {
                            if (await predexp(page))
                                break;
                        }
                        catch
                        {
                        }
                    }

                    var viewport = page.ViewportSize;
                    if (viewport == null || viewport.Width <= 0 || viewport.Height <= 0)
                        break;

                    int vh = viewport.Height;

                    if (direction == PageScrollDirection.Down && options.EnableTopProtection)
                    {
                        bool nearTop = await IsNearTopAsync(page, options.NearTopThresholdPx);
                        if (nearTop)
                            break;
                    }

                    double beforeScrollY = await GetPageScrollYSafeAsync(page);

                    var resolved = ResolveHumanScrollProfile(
                        viewportHeight: vh,
                        direction: direction,
                        index: i,
                        scrollCount: scrollCount,
                        noMoveCount: noMoveCount,
                        options: options);

                    await SwipeEmulator.SwipeMultipleAsync(
                        page: page,
                        client: client,
                        times: 1,
                        distancePx: resolved.DistancePx,
                        pointCount: resolved.PointCount,
                        delayMs: resolved.DelayMs,
                        jitter: resolved.Jitter,
                        direction: direction,
                        cancellationToken: cancellationToken);

                    int pause = timeDelay > 0 ? timeDelay : resolved.PauseMs;
                    await Task.Delay(pause, cancellationToken);

                    double afterScrollY = await GetPageScrollYSafeAsync(page);
                    double deltaScroll = Math.Abs(afterScrollY - beforeScrollY);

                    if (deltaScroll < 3)
                        noMoveCount++;
                    else
                        noMoveCount = 0;

                    if (predexp != null)
                    {
                        try
                        {
                            if (await predexp(page))
                                break;
                        }
                        catch
                        {
                        }
                    }

                    if (noMoveCount >= 3)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }

        /// <summary>
        /// 单次真人级滚动。
        /// </summary>
        public static Task TouchPageScrollOnceAsync(
            IPage page,
            ICDPSession client,
            PageScrollDirection direction,
            Func<IPage, Task<bool>>? predexp = null,
            int timeDelay = 0,
            ScrollOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return TouchPageScrollAsync(
                page: page,
                client: client,
                scrollCount: 1,
                direction: direction,
                predexp: predexp,
                timeDelay: timeDelay,
                options: options,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 长滑为主，适合快速浏览。
        /// </summary>
        public static Task TouchPageLongScrollAsync(
            IPage page,
            ICDPSession client,
            int scrollCount,
            PageScrollDirection direction,
            Func<IPage, Task<bool>>? predexp = null,
            int timeDelay = 0,
            CancellationToken cancellationToken = default)
        {
            return TouchPageScrollAsync(
                page,
                client,
                scrollCount,
                direction,
                predexp,
                timeDelay,
                new ScrollOptions
                {
                    Mode = HumanScrollMode.Long
                },
                cancellationToken);
        }

        /// <summary>
        /// 微调滑动，适合轻微修正位置。
        /// </summary>
        public static Task TouchPageFineTuneScrollAsync(
            IPage page,
            ICDPSession client,
            int scrollCount,
            PageScrollDirection direction,
            Func<IPage, Task<bool>>? predexp = null,
            int timeDelay = 0,
            CancellationToken cancellationToken = default)
        {
            return TouchPageScrollAsync(
                page,
                client,
                scrollCount,
                direction,
                predexp,
                timeDelay,
                new ScrollOptions
                {
                    Mode = HumanScrollMode.FineTune
                },
                cancellationToken);
        }

        /// <summary>
        /// 探测滑，适合试探性找元素或判断页面是否还有内容。
        /// </summary>
        public static Task TouchPageProbeScrollAsync(
            IPage page,
            ICDPSession client,
            int scrollCount,
            PageScrollDirection direction,
            Func<IPage, Task<bool>>? predexp = null,
            int timeDelay = 0,
            CancellationToken cancellationToken = default)
        {
            return TouchPageScrollAsync(
                page,
                client,
                scrollCount,
                direction,
                predexp,
                timeDelay,
                new ScrollOptions
                {
                    Mode = HumanScrollMode.Probe
                },
                cancellationToken);
        }

        /// <summary>
        /// 按屏高比例滚动。
        /// </summary>
        public static Task TouchPageScrollByRatioAsync(
            IPage page,
            ICDPSession client,
            int scrollCount,
            PageScrollDirection direction,
            double heightRatio,
            Func<IPage, Task<bool>>? predexp = null,
            int timeDelay = 0,
            CancellationToken cancellationToken = default)
        {
            return TouchPageScrollAsync(
                page,
                client,
                scrollCount,
                direction,
                predexp,
                timeDelay,
                new ScrollOptions
                {
                    HeightRatio = heightRatio
                },
                cancellationToken);
        }

        /// <summary>
        /// 按固定像素滚动。
        /// </summary>
        public static Task TouchPageScrollByDistanceAsync(
            IPage page,
            ICDPSession client,
            int scrollCount,
            PageScrollDirection direction,
            int distancePx,
            Func<IPage, Task<bool>>? predexp = null,
            int timeDelay = 0,
            CancellationToken cancellationToken = default)
        {
            return TouchPageScrollAsync(
                page,
                client,
                scrollCount,
                direction,
                predexp,
                timeDelay,
                new ScrollOptions
                {
                    DistancePx = distancePx
                },
                cancellationToken);
        }

        private static HumanScrollProfile ResolveHumanScrollProfile(
            int viewportHeight,
            PageScrollDirection direction,
            int index,
            int scrollCount,
            int noMoveCount,
            ScrollOptions options)
        {
            if (options.DistancePx.HasValue || options.HeightRatio.HasValue)
            {
                int distancePx = options.DistancePx
                    ?? (int)(viewportHeight * (options.HeightRatio ?? 0.60));

                int pointCount = options.PointCountRange is { } pcr
                    ? RandomUtil.NextInt(pcr.Min, pcr.Max)
                    : GuessPointCount(viewportHeight, distancePx, direction);

                int delayMs = options.DelayRangeMs is { } drr
                    ? RandomUtil.NextInt(drr.Min, drr.Max)
                    : GuessDelayMs(direction, distancePx);

                float jitter = options.JitterRange is { } jr
                    ? (float)RandomUtil.NextDouble(jr.Min, jr.Max)
                    : GuessJitter(direction, distancePx);

                int pauseMs = options.PauseRangeMs is { } pr
                    ? RandomUtil.NextInt(pr.Min, pr.Max)
                    : GuessPauseMs(direction, distancePx);

                return new HumanScrollProfile
                {
                    Mode = options.Mode,
                    DistancePx = distancePx,
                    PointCount = pointCount,
                    DelayMs = delayMs,
                    Jitter = jitter,
                    PauseMs = pauseMs
                };
            }

            HumanScrollMode mode = options.Mode;

            if (mode == HumanScrollMode.Auto)
            {
                mode = DecideAutoMode(direction, index, scrollCount, noMoveCount, options.EnableAutoMix);
            }

            return BuildProfileByMode(viewportHeight, direction, mode, noMoveCount);
        }

        private static HumanScrollMode DecideAutoMode(
            PageScrollDirection direction,
            int index,
            int scrollCount,
            int noMoveCount,
            bool enableAutoMix)
        {
            if (!enableAutoMix)
            {
                return direction == PageScrollDirection.Up
                    ? HumanScrollMode.Long
                    : HumanScrollMode.Short;
            }

            if (noMoveCount >= 1)
                return HumanScrollMode.Probe;

            if (direction == PageScrollDirection.Down)
            {
                int r1 = RandomUtil.NextInt(0, 100);
                if (r1 < 65) return HumanScrollMode.FineTune;
                if (r1 < 90) return HumanScrollMode.Short;
                return HumanScrollMode.Probe;
            }

            if (index == 0)
            {
                int r0 = RandomUtil.NextInt(0, 100);
                if (r0 < 55) return HumanScrollMode.Long;
                if (r0 < 85) return HumanScrollMode.Short;
                return HumanScrollMode.Probe;
            }

            int r2 = RandomUtil.NextInt(0, 100);
            if (r2 < 55) return HumanScrollMode.Long;
            if (r2 < 78) return HumanScrollMode.Short;
            if (r2 < 92) return HumanScrollMode.Probe;
            return HumanScrollMode.FineTune;
        }

        private static HumanScrollProfile BuildProfileByMode(
            int viewportHeight,
            PageScrollDirection direction,
            HumanScrollMode mode,
            int noMoveCount)
        {
            int distancePx;
            int pointCount;
            int delayMs;
            float jitter;
            int pauseMs;

            switch (mode)
            {
                case HumanScrollMode.Short:
                    distancePx = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt((int)(viewportHeight * 0.22), (int)(viewportHeight * 0.34))
                        : RandomUtil.NextInt((int)(viewportHeight * 0.10), (int)(viewportHeight * 0.16));

                    pointCount = RandomUtil.NextInt(9, 13);
                    delayMs = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt(10, 14)
                        : RandomUtil.NextInt(12, 16);
                    jitter = direction == PageScrollDirection.Up
                        ? (float)RandomUtil.NextDouble(0.40, 0.70)
                        : (float)RandomUtil.NextDouble(0.22, 0.40);
                    pauseMs = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt(450, 800)
                        : RandomUtil.NextInt(300, 550);
                    break;

                case HumanScrollMode.Long:
                    distancePx = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt((int)(viewportHeight * 0.52), (int)(viewportHeight * 0.62))
                        : RandomUtil.NextInt((int)(viewportHeight * 0.14), (int)(viewportHeight * 0.22));

                    pointCount = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt(16, 21)
                        : RandomUtil.NextInt(8, 11);
                    delayMs = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt(10, 13)
                        : RandomUtil.NextInt(12, 16);
                    jitter = direction == PageScrollDirection.Up
                        ? (float)RandomUtil.NextDouble(0.65, 1.00)
                        : (float)RandomUtil.NextDouble(0.25, 0.42);
                    pauseMs = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt(550, 950)
                        : RandomUtil.NextInt(320, 600);
                    break;

                case HumanScrollMode.Probe:
                    distancePx = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt((int)(viewportHeight * 0.14), (int)(viewportHeight * 0.22))
                        : RandomUtil.NextInt((int)(viewportHeight * 0.08), (int)(viewportHeight * 0.12));

                    distancePx += noMoveCount * 18;

                    pointCount = RandomUtil.NextInt(7, 10);
                    delayMs = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt(12, 16)
                        : RandomUtil.NextInt(13, 17);
                    jitter = direction == PageScrollDirection.Up
                        ? (float)RandomUtil.NextDouble(0.28, 0.48)
                        : (float)RandomUtil.NextDouble(0.20, 0.34);
                    pauseMs = RandomUtil.NextInt(220, 420);
                    break;

                case HumanScrollMode.FineTune:
                    distancePx = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt((int)(viewportHeight * 0.08), (int)(viewportHeight * 0.15))
                        : RandomUtil.NextInt((int)(viewportHeight * 0.06), (int)(viewportHeight * 0.10));

                    pointCount = RandomUtil.NextInt(6, 9);
                    delayMs = RandomUtil.NextInt(13, 18);
                    jitter = (float)RandomUtil.NextDouble(0.18, 0.32);
                    pauseMs = RandomUtil.NextInt(180, 350);
                    break;

                case HumanScrollMode.Auto:
                default:
                    distancePx = direction == PageScrollDirection.Up
                        ? RandomUtil.NextInt((int)(viewportHeight * 0.50), (int)(viewportHeight * 0.60))
                        : RandomUtil.NextInt((int)(viewportHeight * 0.12), (int)(viewportHeight * 0.18));

                    pointCount = GuessPointCount(viewportHeight, distancePx, direction);
                    delayMs = GuessDelayMs(direction, distancePx);
                    jitter = GuessJitter(direction, distancePx);
                    pauseMs = GuessPauseMs(direction, distancePx);
                    break;
            }

            return new HumanScrollProfile
            {
                Mode = mode,
                DistancePx = distancePx,
                PointCount = pointCount,
                DelayMs = delayMs,
                Jitter = jitter,
                PauseMs = pauseMs
            };
        }

        private static int GuessPointCount(int viewportHeight, int distancePx, PageScrollDirection direction)
        {
            if (direction == PageScrollDirection.Down)
                return RandomUtil.NextInt(7, 10);

            if (distancePx >= viewportHeight * 0.50) return RandomUtil.NextInt(16, 20);
            if (distancePx >= viewportHeight * 0.30) return RandomUtil.NextInt(12, 16);
            if (distancePx >= viewportHeight * 0.15) return RandomUtil.NextInt(8, 11);
            return RandomUtil.NextInt(6, 9);
        }

        private static int GuessDelayMs(PageScrollDirection direction, int distancePx)
        {
            if (direction == PageScrollDirection.Down)
                return RandomUtil.NextInt(12, 16);

            if (distancePx >= 380) return RandomUtil.NextInt(10, 13);
            if (distancePx >= 220) return RandomUtil.NextInt(11, 14);
            return RandomUtil.NextInt(12, 16);
        }

        private static float GuessJitter(PageScrollDirection direction, int distancePx)
        {
            if (direction == PageScrollDirection.Down)
                return (float)RandomUtil.NextDouble(0.22, 0.40);

            if (distancePx >= 380) return (float)RandomUtil.NextDouble(0.65, 0.95);
            if (distancePx >= 220) return (float)RandomUtil.NextDouble(0.40, 0.70);
            return (float)RandomUtil.NextDouble(0.24, 0.45);
        }

        private static int GuessPauseMs(PageScrollDirection direction, int distancePx)
        {
            if (direction == PageScrollDirection.Down)
                return RandomUtil.NextInt(300, 550);

            if (distancePx >= 380) return RandomUtil.NextInt(550, 950);
            if (distancePx >= 220) return RandomUtil.NextInt(420, 780);
            return RandomUtil.NextInt(220, 450);
        }

        public static async Task<double> GetPageScrollYSafeAsync(IPage page)
        {
            try
            {
                return await page.EvaluateAsync<double>(
                    @"() => {
                        const se = document.scrollingElement || document.documentElement || document.body;
                        return window.scrollY || se.scrollTop || 0;
                    }");
            }
            catch
            {
                return 0;
            }
        }

        public static async Task<bool> IsNearTopAsync(IPage page, int thresholdPx = 10)
        {
            try
            {
                double y = await page.EvaluateAsync<double>(
                    @"() => {
                        const se = document.scrollingElement || document.documentElement || document.body;
                        return window.scrollY || se.scrollTop || 0;
                    }");

                return y <= thresholdPx;
            }
            catch
            {
                return false;
            }
        }
    }
}
