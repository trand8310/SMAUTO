using Microsoft.Playwright;
using QTP.Common;
using System.Numerics;

namespace SMAd.Swiperv2
{
    public enum ScrollDirection
    {
        Up,
        Down,
        Random
    }

    public sealed class SwipeArea
    {
        public float MinXRatio { get; set; } = 0.35f;
        public float MaxXRatio { get; set; } = 0.65f;
        public float MinYRatio { get; set; } = 0.18f;
        public float MaxYRatio { get; set; } = 0.82f;

        public static SwipeArea Normal => new()
        {
            MinXRatio = 0.35f,
            MaxXRatio = 0.65f,
            MinYRatio = 0.18f,
            MaxYRatio = 0.82f
        };

        public static SwipeArea Micro => new()
        {
            MinXRatio = 0.42f,
            MaxXRatio = 0.60f,
            MinYRatio = 0.40f,
            MaxYRatio = 0.68f
        };

        public static SwipeArea Wide => new()
        {
            MinXRatio = 0.22f,
            MaxXRatio = 0.78f,
            MinYRatio = 0.18f,
            MaxYRatio = 0.82f
        };
    }


    public enum SwipeMotionCurve
    {
        Normal = 0,
        Snappy = 1,
        Lazy = 2,
        Uneven = 3
    }

    /// <summary>
    /// 滑动风格参数。用于把百万级滑动任务拆成多套“手感”相近但细节不同的动作族。
    /// </summary>
    public sealed class SwipeStyleOptions
    {
        public string? StyleKey { get; set; }
        public long? StyleNumber { get; set; }
        public long? VariationNumber { get; set; }
        public double DistanceMultiplier { get; set; } = 1.0;
        public double TimingMultiplier { get; set; } = 1.0;
        public double HoldMultiplier { get; set; } = 1.0;
        public double DriftMultiplier { get; set; } = 1.0;
        public double JitterMultiplier { get; set; } = 1.0;
        public double HorizontalEndMultiplier { get; set; } = 1.0;
        public double ForceMultiplier { get; set; } = 1.0;
        public double RadiusMultiplier { get; set; } = 1.0;
        public double TinyBackChanceMultiplier { get; set; } = 1.0;
        public int StepOffset { get; set; }
        public double PauseMultiplier { get; set; } = 1.0;
        public float StartXBiasRatio { get; set; }
        public float StartYBiasRatio { get; set; }
        public SwipeMotionCurve Curve { get; set; } = SwipeMotionCurve.Normal;

        public static SwipeStyleOptions Default => new();

        /// <summary>
        /// 只传一个数字即可生成一套稳定风格。不同数字对应不同“手感”。
        /// </summary>
        public static SwipeStyleOptions FromNumber(long styleNumber)
        {
            return FromNumber(styleNumber, null);
        }

        /// <summary>
        /// 单个动作数字：高位自动作为套号，低位作为同套内微量变化号。默认每 1000000 个数字为一套。
        /// </summary>
        public static SwipeStyleOptions FromActionNumber(
            long actionNumber,
            long suiteSize = 1_000_000,
            double microVariationStrength = 1.0)
        {
            suiteSize = Math.Max(1, suiteSize);
            long suiteNumber = Math.DivRem(actionNumber, suiteSize, out long variationNumber);

            if (variationNumber < 0)
                variationNumber = -variationNumber;

            var style = FromNumber(suiteNumber, variationNumber, microVariationStrength);
            style.StyleKey = $"action:{actionNumber}";
            return style;
        }

        /// <summary>
        /// styleNumber 控制大风格，variationNumber 控制同一套内的微量变化，适合百万级动作按序号传入。
        /// </summary>
        public static SwipeStyleOptions FromNumber(
            long styleNumber,
            long? variationNumber,
            double microVariationStrength = 1.0)
        {
            ulong seed = ToSeed(styleNumber);

            double Pick(ulong salt, double min, double max)
            {
                double ratio = Unit(Mix(seed + salt));
                return min + (max - min) * ratio;
            }

            var style = new SwipeStyleOptions
            {
                StyleKey = $"number:{styleNumber}",
                StyleNumber = styleNumber,
                VariationNumber = variationNumber,
                DistanceMultiplier = Pick(0x101, 0.88, 1.14),
                TimingMultiplier = Pick(0x102, 0.86, 1.18),
                HoldMultiplier = Pick(0x103, 0.82, 1.22),
                DriftMultiplier = Pick(0x104, 0.72, 1.38),
                JitterMultiplier = Pick(0x105, 0.70, 1.32),
                HorizontalEndMultiplier = Pick(0x106, 0.72, 1.35),
                ForceMultiplier = Pick(0x107, 0.92, 1.06),
                RadiusMultiplier = Pick(0x108, 0.86, 1.18),
                TinyBackChanceMultiplier = Pick(0x109, 0.45, 1.65),
                StepOffset = (int)Math.Round(Pick(0x10A, -3, 4)),
                PauseMultiplier = Pick(0x10B, 0.82, 1.25),
                StartXBiasRatio = (float)Pick(0x10C, -0.12, 0.12),
                StartYBiasRatio = (float)Pick(0x10D, -0.08, 0.08),
                Curve = (SwipeMotionCurve)(Mix(seed + 0x10E) % 4)
            };

            if (variationNumber.HasValue)
                style.ApplyMicroVariation(variationNumber.Value, microVariationStrength);

            return style.Clamp();
        }

        public static SwipeStyleOptions FromSuite(string suiteKey)
        {
            var style = FromNumber(StableHash64(string.IsNullOrWhiteSpace(suiteKey) ? "default" : suiteKey));
            style.StyleKey = suiteKey;
            return style;
        }

        public SwipeStyleOptions Clamp()
        {
            DistanceMultiplier = Math.Clamp(DistanceMultiplier, 0.55, 1.45);
            TimingMultiplier = Math.Clamp(TimingMultiplier, 0.55, 1.80);
            HoldMultiplier = Math.Clamp(HoldMultiplier, 0.50, 1.80);
            DriftMultiplier = Math.Clamp(DriftMultiplier, 0.35, 2.20);
            JitterMultiplier = Math.Clamp(JitterMultiplier, 0.20, 2.20);
            HorizontalEndMultiplier = Math.Clamp(HorizontalEndMultiplier, 0.30, 2.10);
            ForceMultiplier = Math.Clamp(ForceMultiplier, 0.70, 1.20);
            RadiusMultiplier = Math.Clamp(RadiusMultiplier, 0.60, 1.70);
            TinyBackChanceMultiplier = Math.Clamp(TinyBackChanceMultiplier, 0.0, 2.50);
            StepOffset = Math.Clamp(StepOffset, -6, 8);
            PauseMultiplier = Math.Clamp(PauseMultiplier, 0.50, 2.00);
            StartXBiasRatio = Math.Clamp(StartXBiasRatio, -0.25f, 0.25f);
            StartYBiasRatio = Math.Clamp(StartYBiasRatio, -0.20f, 0.20f);
            return this;
        }

        private void ApplyMicroVariation(long variationNumber, double strength)
        {
            strength = Math.Clamp(strength, 0.0, 2.0);
            if (strength <= 0)
                return;

            ulong seed = ToSeed(StyleNumber.GetValueOrDefault()) ^ Mix(ToSeed(variationNumber) + 0x9E3779B97F4A7C15UL);

            double Micro(ulong salt, double width)
            {
                return 1.0 + (Unit(Mix(seed + salt)) * 2.0 - 1.0) * width * strength;
            }

            double Offset(ulong salt, double width)
            {
                return (Unit(Mix(seed + salt)) * 2.0 - 1.0) * width * strength;
            }

            DistanceMultiplier *= Micro(0x201, 0.030);
            TimingMultiplier *= Micro(0x202, 0.040);
            HoldMultiplier *= Micro(0x203, 0.045);
            DriftMultiplier *= Micro(0x204, 0.080);
            JitterMultiplier *= Micro(0x205, 0.100);
            HorizontalEndMultiplier *= Micro(0x206, 0.070);
            ForceMultiplier *= Micro(0x207, 0.020);
            RadiusMultiplier *= Micro(0x208, 0.025);
            TinyBackChanceMultiplier *= Micro(0x209, 0.120);
            PauseMultiplier *= Micro(0x20A, 0.045);
            StepOffset += (int)Math.Round(Offset(0x20B, 1.2));
            StartXBiasRatio += (float)Offset(0x20C, 0.020);
            StartYBiasRatio += (float)Offset(0x20D, 0.016);
        }

        private static ulong ToSeed(long value)
        {
            return unchecked((ulong)value) + 0x9E3779B97F4A7C15UL;
        }

        private static double Unit(ulong value)
        {
            return (value >> 11) * (1.0 / (1UL << 53));
        }

        private static ulong Mix(ulong value)
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        private static long StableHash64(string text)
        {
            unchecked
            {
                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offset;

                foreach (char c in text)
                {
                    hash ^= c;
                    hash *= prime;
                }

                return (long)(hash & 0x7FFFFFFFFFFFFFFFUL);
            }
        }
    }

    public sealed class SwipeTrace
    {
        public Vector2 Start { get; set; }
        public Vector2 End { get; set; }
        public List<Vector2> Points { get; set; } = new();
        public int TotalDelayMs { get; set; }
        public ScrollDirection Direction { get; set; }
        public bool IsMicroSwipe { get; set; }
    }

    public sealed class PageScrollState
    {
        public double ScrollX { get; set; }
        public double ScrollY { get; set; }
        public double ScrollHeight { get; set; }
        public double ClientHeight { get; set; }

        public bool CanScrollVertically => ScrollHeight > ClientHeight + 2;
        public bool IsNearTop => ScrollY <= 6;
        public bool IsNearBottom => ScrollY + ClientHeight >= ScrollHeight - 6;
    }

    public sealed class JsScrollState
    {
        public double ScrollX { get; set; }
        public double ScrollY { get; set; }
        public double ScrollHeight { get; set; }
        public double ClientHeight { get; set; }
    }

    public sealed class ScrollTargetState
    {
        public string Kind { get; set; } = "document";
        public string TargetId { get; set; } = "";

        public string ElementTag { get; set; } = "";
        public string ElementId { get; set; } = "";
        public string ElementClass { get; set; } = "";

        public double ScrollLeft { get; set; }
        public double ScrollTop { get; set; }
        public double ScrollWidth { get; set; }
        public double ScrollHeight { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }

        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }

        public bool CanScrollVertically => ScrollHeight > ClientHeight + 2;
        public bool IsNearTop => ScrollTop <= 6;
        public bool IsNearBottom => ScrollTop + ClientHeight >= ScrollHeight - 6;
    }

    public static class SwipeEmulator
    {
        #region 对外主方法

        public static async Task EnableTouchInputAsync(IPage page, ICDPSession client)
        {
            if (page == null || page.IsClosed || client == null)
                return;

            try
            {
                await page.BringToFrontAsync();
            }
            catch
            {
            }

            try
            {
                await client.SendAsync("Input.setIgnoreInputEvents", new Dictionary<string, object>
                {
                    ["ignore"] = false
                });
            }
            catch
            {
            }

            try
            {
                await client.SendAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["maxTouchPoints"] = 5
                });
            }
            catch
            {
            }
        }

        /// <summary>
        /// 单次拟真人滑动。
        /// ScrollDirection.Up = 手指从下往上滑，页面内容向上移动，scrollY 增大。
        /// ScrollDirection.Down = 手指从上往下滑，页面内容向下回退，scrollY 减小。
        /// </summary>
        public static async Task<SwipeTrace?> SwipeOnceHumanAsync(
            IPage page,
            ICDPSession client,
            ScrollDirection direction = ScrollDirection.Up,
            SwipeArea? area = null,
            bool microSwipe = false,
            int? steps = null,
            int? totalDistancePx = null,
            bool verifyScrollChanged = true,
            CancellationToken cancellationToken = default,
            SwipeStyleOptions? style = null,
            long? styleActionNumber = null,
            long? styleNumber = null,
            long? styleVariationNumber = null,
            double styleVariationStrength = 1.0)
        {
            if (page == null || page.IsClosed || client == null || page.ViewportSize == null)
                return null;

            try
            {
                await EnableTouchInputAsync(page, client);

                area ??= microSwipe ? SwipeArea.Micro : SwipeArea.Normal;
                style = ResolveSwipeStyle(style, styleActionNumber, styleNumber, styleVariationNumber, styleVariationStrength);

                ScrollDirection actualDirection = PickHumanDirection(direction);

                if (page.IsClosed || page.ViewportSize == null)
                    return null;

                int vw = page.ViewportSize.Width;
                int vh = page.ViewportSize.Height;

                var safePath = await CreateSafeHumanSwipePathAsync(
                    page: page,
                    vw: vw,
                    vh: vh,
                    direction: actualDirection,
                    area: area,
                    microSwipe: microSwipe,
                    totalDistancePx: totalDistancePx,
                    style: style,
                    maxTry: 10);

                if (safePath == null)
                    return null;

                var path = safePath.Value;

                ScrollTargetState? before = null;
                if (verifyScrollChanged)
                {
                    before = await GetScrollTargetStateAsync(page, path.start.X, path.start.Y);
                }

                int actualSteps = steps ?? CalcSteps(
                    Vector2.Distance(path.start, path.end),
                    vh,
                    microSwipe);

                var trace = await DispatchHumanSwipeAsync(
                    client: client,
                    start: path.start,
                    end: path.end,
                    steps: actualSteps,
                    direction: actualDirection,
                    microSwipe: microSwipe,
                    style: style,
                    cancellationToken: cancellationToken);

                if (verifyScrollChanged && before != null)
                {
                    await Task.Delay(CommonHelper.NextInt(80, 180), cancellationToken);

                    if (page.IsClosed)
                        return null;

                    bool moved = await DidScrollTargetAsync(
                        page: page,
                        before: before,
                        hitX: path.start.X,
                        hitY: path.start.Y,
                        minDelta: microSwipe ? 4 : 8);

                    if (!moved)
                        return null;
                }

                return trace;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="page">Playwright 当前页面对象</param>
        /// <param name="client">当前页面的 CDP 会话</param>
        /// <param name="times">连续滑动次数注意是“最多”，不是一定滑 4 次。如果中间检测到滑不动，或者连续失败次数达到 maxConsecutiveNoMove，会提前结束。</param>
        /// <param name="direction">滑动方向</param>
        /// <param name="area">控制滑动起点和终点出现的大致区域:SwipeArea.Normal X: 屏幕 35% ~ 65% Y: 屏幕 18% ~ 82%,SwipeArea.Wide 更宽的区域：X: 屏幕 22% ~ 78% Y: 屏幕 18% ~ 82%,SwipeArea.Micro 微滑区域：X: 屏幕 42% ~ 60%,Y: 屏幕 40% ~ 68%</param>
        /// <param name="microSwipe">是否使用微滑模式</param>
        /// <param name="maxConsecutiveNoMove">连续多少次没滑动成功后停止</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns></returns>
        public static async Task<List<SwipeTrace>> SwipeMultipleHumanAsync(
            IPage page,
            ICDPSession client,
            int times,
            ScrollDirection direction = ScrollDirection.Random,
            SwipeArea? area = null,
            bool microSwipe = false,
            int maxConsecutiveNoMove = 2,
            CancellationToken cancellationToken = default,
            SwipeStyleOptions? style = null,
            long? styleActionNumber = null,
            long? styleNumber = null,
            long? styleVariationNumber = null,
            double styleVariationStrength = 1.0)
        {
            var list = new List<SwipeTrace>();

            if (page == null || page.IsClosed || client == null || times <= 0)
                return list;

            area ??= microSwipe ? SwipeArea.Micro : SwipeArea.Normal;
            style = styleNumber.HasValue || styleActionNumber.HasValue
                ? null
                : ResolveSwipeStyle(style, null, null, styleVariationNumber, styleVariationStrength);

            int noMoveCount = 0;

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                try
                {
                    ScrollDirection actualDirection = PickHumanDirection(direction);

                    SwipeStyleOptions currentStyle = styleNumber.HasValue || styleActionNumber.HasValue
                        ? ResolveSwipeStyle(null, styleActionNumber.HasValue ? styleActionNumber + i : null, styleNumber, (styleVariationNumber ?? 0) + i, styleVariationStrength)
                        : style!;

                    var trace = await SwipeOnceHumanAsync(
                        page: page,
                        client: client,
                        direction: actualDirection,
                        area: area,
                        microSwipe: microSwipe,
                        verifyScrollChanged: true,
                        style: currentStyle,
                        cancellationToken: cancellationToken);

                    if (trace == null)
                    {
                        noMoveCount++;

                        if (noMoveCount >= maxConsecutiveNoMove)
                            break;

                        await Task.Delay(CommonHelper.NextInt(80, 180), cancellationToken);
                        continue;
                    }

                    noMoveCount = 0;
                    list.Add(trace);

                    await Task.Delay(
                        microSwipe
                            ? CommonHelper.NextInt(180, 480)
                            : CommonHelper.NextInt(320, 980),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    noMoveCount++;

                    if (noMoveCount >= maxConsecutiveNoMove)
                        break;
                }
            }

            return list;
        }

        public static Task<List<SwipeTrace>> SwipeMultipleMicroHumanAsync(
            IPage page,
            ICDPSession client,
            int times,
            ScrollDirection direction = ScrollDirection.Random,
            int maxConsecutiveNoMove = 2,
            CancellationToken cancellationToken = default,
            SwipeStyleOptions? style = null,
            long? styleActionNumber = null,
            long? styleNumber = null,
            long? styleVariationNumber = null,
            double styleVariationStrength = 1.0)
        {
            return SwipeMultipleHumanAsync(
                page: page,
                client: client,
                times: times,
                direction: direction,
                area: SwipeArea.Micro,
                microSwipe: true,
                maxConsecutiveNoMove: maxConsecutiveNoMove,
                style: style,
                styleActionNumber: styleActionNumber,
                styleNumber: styleNumber,
                styleVariationNumber: styleVariationNumber,
                styleVariationStrength: styleVariationStrength,
                cancellationToken: cancellationToken);
        }

        public static async Task<List<SwipeTrace>> SwipeElementIntoComfortZoneAsync(
            IPage page,
            ICDPSession client,
            ILocator element,
            int maxSwipes = 8,
            float comfortTopRatio = 0.22f,
            float comfortBottomRatio = 0.72f,
            CancellationToken cancellationToken = default,
            SwipeStyleOptions? style = null,
            long? styleActionNumber = null,
            long? styleNumber = null,
            long? styleVariationNumber = null,
            double styleVariationStrength = 1.0)
        {
            var all = new List<SwipeTrace>();

            if (page == null || page.IsClosed || client == null || element == null || page.ViewportSize == null)
                return all;

            style = styleNumber.HasValue || styleActionNumber.HasValue
                ? null
                : ResolveSwipeStyle(style, null, null, styleVariationNumber, styleVariationStrength);

            int vh = page.ViewportSize.Height;
            float comfortTop = vh * comfortTopRatio;
            float comfortBottom = vh * comfortBottomRatio;

            for (int i = 0; i < maxSwipes; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    return all;

                try
                {
                    var box = await element.BoundingBoxAsync();

                    if (box == null)
                        return all;

                    float centerY = (float)(box.Y + box.Height / 2.0);

                    if (centerY >= comfortTop && centerY <= comfortBottom)
                        return all;

                    ScrollDirection direction = centerY < comfortTop
                        ? ScrollDirection.Down
                        : ScrollDirection.Up;

                    double distanceToComfort = centerY < comfortTop
                        ? comfortTop - centerY
                        : centerY - comfortBottom;

                    bool useMicro = distanceToComfort < vh * 0.20;

                    int? targetDistance = useMicro
                        ? (int)Math.Clamp(distanceToComfort * 0.88, vh * 0.08, vh * 0.20)
                        : (int)Math.Clamp(distanceToComfort * 0.92, vh * 0.22, vh * 0.58);

                    SwipeStyleOptions currentStyle = styleNumber.HasValue || styleActionNumber.HasValue
                        ? ResolveSwipeStyle(null, styleActionNumber.HasValue ? styleActionNumber + i : null, styleNumber, (styleVariationNumber ?? 0) + i, styleVariationStrength)
                        : style!;

                    var trace = await SwipeOnceHumanAsync(
                        page: page,
                        client: client,
                        direction: direction,
                        area: useMicro ? SwipeArea.Micro : SwipeArea.Normal,
                        microSwipe: useMicro,
                        totalDistancePx: targetDistance,
                        verifyScrollChanged: true,
                        style: currentStyle,
                        cancellationToken: cancellationToken);

                    if (trace == null)
                        return all;

                    all.Add(trace);

                    await Task.Delay(CommonHelper.NextInt(120, 280), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return all;
                }
            }

            return all;
        }

        public static async Task<List<SwipeTrace>> SwipeToElementAsync(
            IPage page,
            ICDPSession client,
            ILocator element,
            int maxSwipes = 10,
            CancellationToken cancellationToken = default,
            SwipeStyleOptions? style = null,
            long? styleActionNumber = null,
            long? styleNumber = null,
            long? styleVariationNumber = null,
            double styleVariationStrength = 1.0)
        {
            var traces = new List<SwipeTrace>();

            if (page == null || page.IsClosed || client == null || element == null || page.ViewportSize == null || maxSwipes <= 0)
                return traces;

            style = styleNumber.HasValue || styleActionNumber.HasValue
                ? null
                : ResolveSwipeStyle(style, null, null, styleVariationNumber, styleVariationStrength);

            int vh = page.ViewportSize.Height;

            float comfortTop = vh * 0.22f;
            float comfortBottom = vh * 0.72f;

            try
            {
                if (await element.CountAsync() <= 0)
                    return traces;
            }
            catch
            {
                return traces;
            }

            for (int i = 0; i < maxSwipes; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    return traces;

                try
                {
                    SwipeStyleOptions currentStyle = styleNumber.HasValue || styleActionNumber.HasValue
                        ? ResolveSwipeStyle(null, styleActionNumber.HasValue ? styleActionNumber + i : null, styleNumber, (styleVariationNumber ?? 0) + i, styleVariationStrength)
                        : style!;

                    var box = await element.BoundingBoxAsync();

                    if (box == null)
                    {
                        var pos = await GetElementViewportPositionAsync(page, element);

                        if (pos == null)
                            return traces;

                        ScrollDirection direction = pos.CenterY < 0
                            ? ScrollDirection.Down
                            : ScrollDirection.Up;

                        int? distance = (int)Math.Clamp(vh * 0.42, vh * 0.24, vh * 0.58);

                        var trace = await SwipeOnceHumanAsync(
                            page: page,
                            client: client,
                            direction: direction,
                            area: SwipeArea.Normal,
                            microSwipe: false,
                            totalDistancePx: distance,
                            verifyScrollChanged: true,
                            style: currentStyle,
                            cancellationToken: cancellationToken);

                        if (trace == null)
                            return traces;

                        traces.Add(trace);

                        await Task.Delay(CommonHelper.NextInt(120, 280), cancellationToken);
                        continue;
                    }

                    float top = (float)box.Y;
                    float bottom = (float)(box.Y + box.Height);
                    float centerY = (float)(box.Y + box.Height / 2.0);

                    if (centerY >= comfortTop && centerY <= comfortBottom)
                        return traces;

                    if (bottom >= 0 && top <= vh)
                    {
                        double distanceToComfort = centerY < comfortTop
                            ? comfortTop - centerY
                            : centerY - comfortBottom;

                        ScrollDirection direction = centerY < comfortTop
                            ? ScrollDirection.Down
                            : ScrollDirection.Up;

                        bool useMicro = distanceToComfort < vh * 0.20;

                        int? targetDistance = useMicro
                            ? (int)Math.Clamp(distanceToComfort * 0.90, vh * 0.08, vh * 0.18)
                            : (int)Math.Clamp(distanceToComfort * 0.94, vh * 0.18, vh * 0.42);

                        var trace = await SwipeOnceHumanAsync(
                            page: page,
                            client: client,
                            direction: direction,
                            area: useMicro ? SwipeArea.Micro : SwipeArea.Normal,
                            microSwipe: useMicro,
                            totalDistancePx: targetDistance,
                            verifyScrollChanged: true,
                            style: currentStyle,
                            cancellationToken: cancellationToken);

                        if (trace == null)
                            return traces;

                        traces.Add(trace);

                        await Task.Delay(CommonHelper.NextInt(100, 240), cancellationToken);
                        continue;
                    }

                    if (top > vh)
                    {
                        double distance = top - comfortBottom;
                        int? targetDistance = (int)Math.Clamp(distance * 0.92, vh * 0.22, vh * 0.58);

                        var trace = await SwipeOnceHumanAsync(
                            page: page,
                            client: client,
                            direction: ScrollDirection.Up,
                            area: SwipeArea.Normal,
                            microSwipe: false,
                            totalDistancePx: targetDistance,
                            verifyScrollChanged: true,
                            style: currentStyle,
                            cancellationToken: cancellationToken);

                        if (trace == null)
                            return traces;

                        traces.Add(trace);

                        await Task.Delay(CommonHelper.NextInt(120, 280), cancellationToken);
                        continue;
                    }

                    if (bottom < 0)
                    {
                        double distance = comfortTop - bottom;
                        int? targetDistance = (int)Math.Clamp(distance * 0.92, vh * 0.18, vh * 0.46);

                        var trace = await SwipeOnceHumanAsync(
                            page: page,
                            client: client,
                            direction: ScrollDirection.Down,
                            area: SwipeArea.Normal,
                            microSwipe: false,
                            totalDistancePx: targetDistance,
                            verifyScrollChanged: true,
                            style: currentStyle,
                            cancellationToken: cancellationToken);

                        if (trace == null)
                            return traces;

                        traces.Add(trace);

                        await Task.Delay(CommonHelper.NextInt(120, 280), cancellationToken);
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return traces;
                }
            }

            return traces;
        }

        #endregion

        #region 页面滚动状态

        private static async Task<PageScrollState> GetPageScrollStateAsync(IPage page)
        {
            if (page == null || page.IsClosed)
                return new PageScrollState();

            try
            {
                var result = await page.EvaluateAsync<JsScrollState>(@"
                () => {
                    try {
                        const el = document.scrollingElement || document.documentElement || document.body;
                        return {
                            ScrollX: Number(window.scrollX || el?.scrollLeft || 0),
                            ScrollY: Number(window.scrollY || el?.scrollTop || 0),
                            ScrollHeight: Number(el?.scrollHeight || 0),
                            ClientHeight: Number(window.innerHeight || el?.clientHeight || 0)
                        };
                    } catch {
                        return {
                            ScrollX: 0,
                            ScrollY: 0,
                            ScrollHeight: 0,
                            ClientHeight: 0
                        };
                    }
                }");

                return new PageScrollState
                {
                    ScrollX = result?.ScrollX ?? 0,
                    ScrollY = result?.ScrollY ?? 0,
                    ScrollHeight = result?.ScrollHeight ?? 0,
                    ClientHeight = result?.ClientHeight ?? 0
                };
            }
            catch
            {
                return new PageScrollState();
            }
        }

        #endregion

        #region 内部滚动容器检测

        private static async Task<ScrollTargetState> GetScrollTargetStateAsync(
            IPage page,
            float hitX,
            float hitY)
        {
            if (page == null || page.IsClosed)
                return new ScrollTargetState();

            try
            {
                var result = await page.EvaluateAsync<ScrollTargetState>(@"
                (arg) => {
                    const x = Number(arg.x || 0);
                    const y = Number(arg.y || 0);

                    function canScrollY(el) {
                        if (!el) return false;

                        const style = getComputedStyle(el);
                        if (!style) return false;
                        if (style.display === 'none') return false;
                        if (style.visibility === 'hidden') return false;

                        const overflowY = style.overflowY;
                        const scrollable =
                            overflowY === 'auto' ||
                            overflowY === 'scroll' ||
                            overflowY === 'overlay';

                        return scrollable && el.scrollHeight > el.clientHeight + 2;
                    }

                    function pickScrollable(startEl) {
                        let el = startEl;

                        while (el && el !== document.body && el !== document.documentElement) {
                            if (canScrollY(el)) return el;
                            el = el.parentElement;
                        }

                        return document.scrollingElement || document.documentElement || document.body;
                    }

                    function ensureTargetId(target, isDoc) {
                        if (isDoc) return '__document__';

                        if (!target.getAttribute('data-smad-swipe-id')) {
                            const id = 'swipe_' + Date.now() + '_' + Math.random().toString(16).slice(2);
                            target.setAttribute('data-smad-swipe-id', id);
                        }

                        return target.getAttribute('data-smad-swipe-id') || '';
                    }

                    try {
                        const hitEl = document.elementFromPoint(x, y);
                        const target = pickScrollable(hitEl);

                        const docTarget = document.scrollingElement || document.documentElement || document.body;
                        const isDoc = target === docTarget;

                        const targetId = ensureTargetId(target, isDoc);

                        return {
                            Kind: isDoc ? 'document' : 'element',
                            TargetId: targetId,
                            ElementTag: (target?.tagName || '').toLowerCase(),
                            ElementId: target?.id || '',
                            ElementClass: typeof target?.className === 'string' ? target.className : '',
                            ScrollLeft: Number(target?.scrollLeft || 0),
                            ScrollTop: Number(target?.scrollTop || 0),
                            ScrollWidth: Number(target?.scrollWidth || 0),
                            ScrollHeight: Number(target?.scrollHeight || 0),
                            ClientWidth: Number(target?.clientWidth || window.innerWidth || 0),
                            ClientHeight: Number(target?.clientHeight || window.innerHeight || 0),
                            ViewportWidth: Number(window.innerWidth || 0),
                            ViewportHeight: Number(window.innerHeight || 0)
                        };
                    } catch {
                        const target = document.scrollingElement || document.documentElement || document.body;

                        return {
                            Kind: 'document',
                            TargetId: '__document__',
                            ElementTag: (target?.tagName || '').toLowerCase(),
                            ElementId: target?.id || '',
                            ElementClass: typeof target?.className === 'string' ? target.className : '',
                            ScrollLeft: Number(target?.scrollLeft || 0),
                            ScrollTop: Number(target?.scrollTop || 0),
                            ScrollWidth: Number(target?.scrollWidth || 0),
                            ScrollHeight: Number(target?.scrollHeight || 0),
                            ClientWidth: Number(target?.clientWidth || window.innerWidth || 0),
                            ClientHeight: Number(target?.clientHeight || window.innerHeight || 0),
                            ViewportWidth: Number(window.innerWidth || 0),
                            ViewportHeight: Number(window.innerHeight || 0)
                        };
                    }
                }", new { x = hitX, y = hitY });

                return result ?? new ScrollTargetState();
            }
            catch
            {
                return new ScrollTargetState();
            }
        }

        private static async Task<ScrollTargetState> GetDocumentScrollTargetStateAsync(IPage page)
        {
            if (page == null || page.IsClosed)
                return new ScrollTargetState();

            try
            {
                var result = await page.EvaluateAsync<ScrollTargetState>(@"
                () => {
                    try {
                        const target = document.scrollingElement || document.documentElement || document.body;

                        return {
                            Kind: 'document',
                            TargetId: '__document__',
                            ElementTag: (target?.tagName || '').toLowerCase(),
                            ElementId: target?.id || '',
                            ElementClass: typeof target?.className === 'string' ? target.className : '',
                            ScrollLeft: Number(target?.scrollLeft || 0),
                            ScrollTop: Number(target?.scrollTop || 0),
                            ScrollWidth: Number(target?.scrollWidth || 0),
                            ScrollHeight: Number(target?.scrollHeight || 0),
                            ClientWidth: Number(target?.clientWidth || window.innerWidth || 0),
                            ClientHeight: Number(target?.clientHeight || window.innerHeight || 0),
                            ViewportWidth: Number(window.innerWidth || 0),
                            ViewportHeight: Number(window.innerHeight || 0)
                        };
                    } catch {
                        return {
                            Kind: 'document',
                            TargetId: '__document__',
                            ElementTag: '',
                            ElementId: '',
                            ElementClass: '',
                            ScrollLeft: 0,
                            ScrollTop: 0,
                            ScrollWidth: 0,
                            ScrollHeight: 0,
                            ClientWidth: 0,
                            ClientHeight: 0,
                            ViewportWidth: 0,
                            ViewportHeight: 0
                        };
                    }
                }");

                return result ?? new ScrollTargetState();
            }
            catch
            {
                return new ScrollTargetState();
            }
        }

        private static async Task<ScrollTargetState?> GetScrollTargetStateByIdAsync(
            IPage page,
            ScrollTargetState before)
        {
            if (page == null || page.IsClosed || before == null)
                return null;

            try
            {
                return await page.EvaluateAsync<ScrollTargetState?>(@"
                (before) => {
                    try {
                        let target = null;

                        if (before.TargetId === '__document__' || before.Kind === 'document') {
                            target = document.scrollingElement || document.documentElement || document.body;
                        } else {
                            const list = document.querySelectorAll('[data-smad-swipe-id]');
                            for (const item of list) {
                                if (item.getAttribute('data-smad-swipe-id') === before.TargetId) {
                                    target = item;
                                    break;
                                }
                            }
                        }

                        if (!target) return null;

                        const docTarget = document.scrollingElement || document.documentElement || document.body;
                        const isDoc = target === docTarget;

                        return {
                            Kind: isDoc ? 'document' : 'element',
                            TargetId: before.TargetId || '',
                            ElementTag: (target?.tagName || '').toLowerCase(),
                            ElementId: target?.id || '',
                            ElementClass: typeof target?.className === 'string' ? target.className : '',
                            ScrollLeft: Number(target?.scrollLeft || 0),
                            ScrollTop: Number(target?.scrollTop || 0),
                            ScrollWidth: Number(target?.scrollWidth || 0),
                            ScrollHeight: Number(target?.scrollHeight || 0),
                            ClientWidth: Number(target?.clientWidth || window.innerWidth || 0),
                            ClientHeight: Number(target?.clientHeight || window.innerHeight || 0),
                            ViewportWidth: Number(window.innerWidth || 0),
                            ViewportHeight: Number(window.innerHeight || 0)
                        };
                    } catch {
                        return null;
                    }
                }", before);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> DidScrollTargetAsync(
            IPage page,
            ScrollTargetState before,
            float hitX,
            float hitY,
            double minDelta = 8)
        {
            if (page == null || page.IsClosed || before == null)
                return false;

            try
            {
                ScrollTargetState? after = await GetScrollTargetStateByIdAsync(page, before);

                if (after == null)
                    after = await GetScrollTargetStateAsync(page, hitX, hitY);

                if (after == null)
                    after = await GetDocumentScrollTargetStateAsync(page);

                return Math.Abs(after.ScrollTop - before.ScrollTop) >= minDelta;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> CanSafelySwipeDirectionOnTargetAsync(
            IPage page,
            ScrollDirection direction,
            float hitX,
            float hitY)
        {
            if (page == null || page.IsClosed)
                return false;

            try
            {
                var state = await GetScrollTargetStateAsync(page, hitX, hitY);

                if (!state.CanScrollVertically)
                {
                    state = await GetDocumentScrollTargetStateAsync(page);

                    if (!state.CanScrollVertically)
                        return false;
                }

                if (direction == ScrollDirection.Down && state.IsNearTop)
                    return false;

                if (direction == ScrollDirection.Up && state.IsNearBottom)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 起点筛选，已放宽

        private static async Task<(Vector2 start, Vector2 end)?> CreateSafeHumanSwipePathAsync(
            IPage page,
            int vw,
            int vh,
            ScrollDirection direction,
            SwipeArea area,
            bool microSwipe,
            int? totalDistancePx,
            SwipeStyleOptions style,
            int maxTry = 10)
        {
            for (int i = 0; i < maxTry; i++)
            {
                if (page == null || page.IsClosed)
                    return null;

                var path = CreateHumanSwipePath(
                    vw: vw,
                    vh: vh,
                    direction: direction,
                    area: area,
                    microSwipe: microSwipe,
                    totalDistancePx: totalDistancePx,
                    style: style);

                if (path.start == path.end)
                    continue;

                bool canSwipe = await CanSafelySwipeDirectionOnTargetAsync(
                    page: page,
                    direction: direction,
                    hitX: path.start.X,
                    hitY: path.start.Y);

                if (!canSwipe)
                    continue;

                return path;
            }

            return null;
        }

        /// <summary>
        /// 判断滑动起点是否适合按下去。
        /// 这个版本已经放宽：
        /// 不再拦截 a / button / role=button / role=link。
        /// 只避开 input / textarea / select / iframe / video / canvas。
        /// </summary>
        private static async Task<bool> IsGoodSwipeStartPointAsync(IPage page, float x, float y)
        {
            if (page == null || page.IsClosed)
                return false;

            try
            {
                return await page.EvaluateAsync<bool>(@"
(arg) => {
    const x = Number(arg.x || 0);
    const y = Number(arg.y || 0);

    try {
        const el = document.elementFromPoint(x, y);
        if (!el) return false;

        let p = el;
        let depth = 0;

        while (p && depth < 4) {
            const tag = (p.tagName || '').toLowerCase();

            // 输入类控件容易弹键盘或者触发聚焦，避开
            if (
                tag === 'input' ||
                tag === 'textarea' ||
                tag === 'select'
            ) {
                return false;
            }

            // 这些元素容易吞掉 touch 或者产生拖拽/媒体交互，避开
            if (
                tag === 'iframe' ||
                tag === 'video' ||
                tag === 'canvas'
            ) {
                return false;
            }

            const style = getComputedStyle(p);
            if (!style) return false;

            if (style.display === 'none' || style.visibility === 'hidden') {
                return false;
            }

            if (Number(style.opacity) < 0.05) {
                return false;
            }

            p = p.parentElement;
            depth++;
        }

        return true;
    } catch {
        return false;
    }
}", new { x, y });
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 兼容旧 document 判断

        private static async Task<bool> DidScrollAsync(
            IPage page,
            PageScrollState before,
            double minDelta = 8)
        {
            if (page == null || page.IsClosed || before == null)
                return false;

            try
            {
                var after = await GetPageScrollStateAsync(page);
                return Math.Abs(after.ScrollY - before.ScrollY) >= minDelta;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> CanSafelySwipeDirectionAsync(
            IPage page,
            ScrollDirection direction)
        {
            if (page == null || page.IsClosed)
                return false;

            try
            {
                var state = await GetPageScrollStateAsync(page);

                if (!state.CanScrollVertically)
                    return false;

                if (direction == ScrollDirection.Down && state.IsNearTop)
                    return false;

                if (direction == ScrollDirection.Up && state.IsNearBottom)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 轨迹生成

        private static (Vector2 start, Vector2 end) CreateHumanSwipePath(
            int vw,
            int vh,
            ScrollDirection direction,
            SwipeArea area,
            bool microSwipe,
            int? totalDistancePx,
            SwipeStyleOptions style)
        {
            float minX = vw * area.MinXRatio;
            float maxX = vw * area.MaxXRatio;
            float minY = vh * area.MinYRatio;
            float maxY = vh * area.MaxYRatio;

            float safeTop = Math.Max(vh * 0.16f, minY);
            float safeBottom = Math.Min(vh * 0.84f, maxY);
            float safeLeft = Math.Max(vw * 0.12f, minX);
            float safeRight = Math.Min(vw * 0.88f, maxX);

            if (safeRight <= safeLeft || safeBottom <= safeTop)
                return (Vector2.Zero, Vector2.Zero);

            float xSpan = safeRight - safeLeft;
            float xBias = xSpan * style.StartXBiasRatio;
            float startX = (float)CommonHelper.NextDouble(safeLeft, safeRight) + xBias;
            startX = Math.Clamp(startX, safeLeft, safeRight);

            double endDrift = (microSwipe
                ? CommonHelper.NextDouble(-10, 10)
                : CommonHelper.NextDouble(-22, 22)) * style.HorizontalEndMultiplier;
            float endX = startX + (float)endDrift;

            endX = Math.Clamp(endX, safeLeft, safeRight);

            float distance;

            if (totalDistancePx.HasValue && totalDistancePx.Value > 0)
            {
                distance = (float)(totalDistancePx.Value * style.DistanceMultiplier);
            }
            else
            {
                double r = CommonHelper.NextDouble();

                if (microSwipe)
                {
                    distance = r < 0.70
                        ? (float)CommonHelper.NextDouble(vh * 0.08, vh * 0.16)
                        : (float)CommonHelper.NextDouble(vh * 0.16, vh * 0.24);
                    distance *= (float)style.DistanceMultiplier;
                }
                else
                {
                    distance = r < 0.18
                        ? (float)CommonHelper.NextDouble(vh * 0.18, vh * 0.28)
                        : r < 0.76
                            ? (float)CommonHelper.NextDouble(vh * 0.30, vh * 0.48)
                            : (float)CommonHelper.NextDouble(vh * 0.50, vh * 0.66);
                    distance *= (float)style.DistanceMultiplier;
                }
            }

            distance = Math.Clamp(distance, vh * 0.06f, vh * 0.72f);

            Vector2 start;
            Vector2 end;

            switch (direction)
            {
                case ScrollDirection.Down:
                    {
                        float startY = (float)CommonHelper.NextDouble(vh * 0.30f, vh * 0.46f) + vh * style.StartYBiasRatio;
                        startY = Math.Clamp(startY, safeTop, safeBottom);

                        float endY = startY + distance;

                        if (endY > safeBottom)
                        {
                            endY = safeBottom;
                            startY = Math.Max(vh * 0.24f, endY - distance);
                            startY = Math.Clamp(startY, safeTop, safeBottom);
                        }

                        start = new Vector2(startX, startY);
                        end = new Vector2(endX, endY);
                        break;
                    }

                case ScrollDirection.Up:
                default:
                    {
                        float startY = (float)CommonHelper.NextDouble(vh * 0.58f, safeBottom) + vh * style.StartYBiasRatio;
                        startY = Math.Clamp(startY, safeTop, safeBottom);

                        float endY = startY - distance;

                        if (endY < safeTop)
                        {
                            endY = safeTop;
                            startY = Math.Min(safeBottom, endY + distance);
                            startY = Math.Clamp(startY, safeTop, safeBottom);
                        }

                        start = new Vector2(startX, startY);
                        end = new Vector2(endX, endY);
                        break;
                    }
            }

            return (start, end);
        }

        private static List<Vector2> GetHumanLikeSwipePoints(
            Vector2 start,
            Vector2 end,
            int steps,
            bool microSwipe,
            SwipeStyleOptions style)
        {
            steps = Math.Max(steps + style.StepOffset, microSwipe ? 8 : 14);

            var points = new List<Vector2>(steps + 1);

            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float distance = Vector2.Distance(start, end);

            if (distance < 1)
            {
                points.Add(start);
                points.Add(end);
                return points;
            }

            float nx = -dy / distance;
            float ny = dx / distance;

            float sideDriftBase = microSwipe
                ? (float)CommonHelper.NextDouble(1.0, 2.4)
                : (float)CommonHelper.NextDouble(2.2, 5.2);
            sideDriftBase *= (float)style.DriftMultiplier;

            float phase1 = (float)CommonHelper.NextDouble(0, Math.PI * 2);
            float phase2 = (float)CommonHelper.NextDouble(0, Math.PI * 2);

            float amp1 = (float)CommonHelper.NextDouble(sideDriftBase * 0.35, sideDriftBase);
            float amp2 = (float)CommonHelper.NextDouble(sideDriftBase * 0.12, sideDriftBase * 0.42);

            bool addTinyBack = !microSwipe && CommonHelper.Chance(0.24 * style.TinyBackChanceMultiplier);

            for (int i = 0; i <= steps; i++)
            {
                float tRaw = i / (float)steps;
                float t = ApplyCurve(tRaw, style.Curve);

                float x = start.X + dx * t;
                float y = start.Y + dy * t;

                float drift =
                    MathF.Sin(tRaw * MathF.PI * 1.05f + phase1) * amp1 +
                    MathF.Sin(tRaw * MathF.PI * 2.10f + phase2) * amp2;

                float fade = MathF.Sin(tRaw * MathF.PI);
                drift *= fade;

                x += nx * drift;
                y += ny * drift;

                if (tRaw > 0.78f)
                {
                    float tiny = microSwipe
                        ? (float)CommonHelper.NextDouble(0.10, 0.70)
                        : (float)CommonHelper.NextDouble(0.25, 1.20);
                    tiny *= (float)style.JitterMultiplier;

                    x += (float)CommonHelper.NextDouble(-tiny, tiny);
                    y += (float)CommonHelper.NextDouble(-tiny, tiny);
                }

                if (addTinyBack && tRaw > 0.88f)
                {
                    float backRatio = (float)CommonHelper.NextDouble(0.002, 0.008);
                    x -= dx * backRatio;
                    y -= dy * backRatio;
                }

                points.Add(new Vector2(x, y));
            }

            return points;
        }

        #endregion

        #region 事件派发

        private static async Task<SwipeTrace> DispatchHumanSwipeAsync(
            ICDPSession client,
            Vector2 start,
            Vector2 end,
            int steps,
            ScrollDirection direction,
            bool microSwipe,
            SwipeStyleOptions style,
            CancellationToken cancellationToken)
        {
            var points = GetHumanLikeSwipePoints(start, end, steps, microSwipe, style);

            int totalDelay = 0;
            bool touchStarted = false;

            try
            {
                double startForce = microSwipe
                    ? CommonHelper.NextDouble(0.72, 0.92)
                    : CommonHelper.NextDouble(0.78, 0.98);
                startForce = Math.Clamp(startForce * style.ForceMultiplier, 0.45, 1.0);

                int radius = microSwipe
                    ? CommonHelper.NextInt(2, 4)
                    : CommonHelper.NextInt(3, 7);
                radius = Math.Max(1, (int)Math.Round(radius * style.RadiusMultiplier));

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchStart",
                    ["touchPoints"] = new object[]
                    {
                        new
                        {
                            x = MathF.Round(points[0].X, 2),
                            y = MathF.Round(points[0].Y, 2),
                            radiusX = radius,
                            radiusY = radius,
                            force = startForce,
                            id = 0
                        }
                    },
                    ["modifiers"] = 0
                });

                touchStarted = true;

                int holdBeforeMove = microSwipe
                    ? CommonHelper.NextInt(18, 55)
                    : CommonHelper.NextInt(35, 120);

                holdBeforeMove = ScaleDelay(holdBeforeMove, style.HoldMultiplier);
                await Task.Delay(holdBeforeMove, cancellationToken);
                totalDelay += holdBeforeMove;

                for (int i = 1; i < points.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    float progress = i / (float)(points.Count - 1);

                    int delay = ScaleDelay(GetHumanMoveDelay(progress, microSwipe), style.TimingMultiplier);

                    if (CommonHelper.Chance(microSwipe ? 0.04 : 0.08))
                        delay += CommonHelper.NextInt(8, 28);

                    double force = GetHumanForce(progress, microSwipe, style);

                    int moveRadius = microSwipe
                        ? CommonHelper.NextInt(2, 4)
                        : CommonHelper.NextInt(3, 6);
                    moveRadius = Math.Max(1, (int)Math.Round(moveRadius * style.RadiusMultiplier));

                    await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                    {
                        ["type"] = "touchMove",
                        ["touchPoints"] = new object[]
                        {
                            new
                            {
                                x = MathF.Round(points[i].X, 2),
                                y = MathF.Round(points[i].Y, 2),
                                radiusX = moveRadius,
                                radiusY = moveRadius,
                                force = force,
                                id = 0
                            }
                        },
                        ["modifiers"] = 0
                    });

                    await Task.Delay(delay, cancellationToken);
                    totalDelay += delay;
                }

                int holdAfterMove = microSwipe
                    ? CommonHelper.NextInt(8, 35)
                    : CommonHelper.NextInt(18, 70);

                holdAfterMove = ScaleDelay(holdAfterMove, style.HoldMultiplier);
                await Task.Delay(holdAfterMove, cancellationToken);
                totalDelay += holdAfterMove;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
            finally
            {
                if (touchStarted)
                {
                    try
                    {
                        await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                        {
                            ["type"] = "touchEnd",
                            ["touchPoints"] = Array.Empty<object>(),
                            ["modifiers"] = 0
                        });
                    }
                    catch
                    {
                    }
                }
            }

            return new SwipeTrace
            {
                Start = start,
                End = end,
                Points = points,
                TotalDelayMs = totalDelay,
                Direction = direction,
                IsMicroSwipe = microSwipe
            };
        }

        private static int GetHumanMoveDelay(float progress, bool microSwipe)
        {
            int delay;

            if (progress < 0.08f)
            {
                delay = microSwipe
                    ? CommonHelper.NextInt(12, 24)
                    : CommonHelper.NextInt(18, 35);
            }
            else if (progress < 0.22f)
            {
                delay = microSwipe
                    ? CommonHelper.NextInt(7, 16)
                    : CommonHelper.NextInt(9, 22);
            }
            else if (progress < 0.72f)
            {
                delay = microSwipe
                    ? CommonHelper.NextInt(4, 11)
                    : CommonHelper.NextInt(5, 15);
            }
            else if (progress < 0.90f)
            {
                delay = microSwipe
                    ? CommonHelper.NextInt(7, 16)
                    : CommonHelper.NextInt(9, 22);
            }
            else
            {
                delay = microSwipe
                    ? CommonHelper.NextInt(12, 26)
                    : CommonHelper.NextInt(15, 38);
            }

            return delay;
        }

        private static double GetHumanForce(float progress, bool microSwipe, SwipeStyleOptions style)
        {
            double baseForce;

            if (progress < 0.12f)
            {
                baseForce = microSwipe
                    ? CommonHelper.NextDouble(0.65, 0.84)
                    : CommonHelper.NextDouble(0.70, 0.90);
            }
            else if (progress < 0.80f)
            {
                baseForce = microSwipe
                    ? CommonHelper.NextDouble(0.72, 0.92)
                    : CommonHelper.NextDouble(0.78, 0.98);
            }
            else
            {
                baseForce = microSwipe
                    ? CommonHelper.NextDouble(0.58, 0.80)
                    : CommonHelper.NextDouble(0.62, 0.86);
            }

            return Math.Clamp(baseForce * style.ForceMultiplier, 0.45, 1.0);
        }

        #endregion

        #region 工具方法

        private static SwipeStyleOptions ResolveSwipeStyle(
            SwipeStyleOptions? style,
            long? styleActionNumber,
            long? styleNumber,
            long? styleVariationNumber = null,
            double styleVariationStrength = 1.0)
        {
            if (styleActionNumber.HasValue)
                return SwipeStyleOptions.FromActionNumber(styleActionNumber.Value, microVariationStrength: styleVariationStrength);

            if (styleNumber.HasValue)
                return SwipeStyleOptions.FromNumber(styleNumber.Value, styleVariationNumber, styleVariationStrength);

            return (style ?? SwipeStyleOptions.Default).Clamp();
        }

        private static ScrollDirection PickHumanDirection(ScrollDirection direction)
        {
            if (direction != ScrollDirection.Random)
                return direction;

            double r = CommonHelper.NextDouble();

            if (r < 0.88)
                return ScrollDirection.Up;

            return ScrollDirection.Down;
        }

        private static int ScaleDelay(int value, double multiplier)
        {
            return Math.Max(1, (int)Math.Round(value * Math.Clamp(multiplier, 0.50, 2.00)));
        }

        private static int CalcSteps(double distance, int viewportHeight, bool microSwipe)
        {
            if (distance <= 0)
                return microSwipe ? 8 : 14;

            int minSteps = microSwipe ? 8 : 14;
            int maxSteps = microSwipe ? 18 : 34;

            double ratio = Math.Min(distance / (viewportHeight * 0.75), 1.0);

            int steps = (int)(minSteps + (maxSteps - minSteps) * ratio);

            steps += CommonHelper.NextInt(-2, 3);

            return Math.Clamp(steps, minSteps, maxSteps);
        }

        private static float ApplyCurve(float t, SwipeMotionCurve curve)
        {
            return curve switch
            {
                SwipeMotionCurve.Snappy => EaseOutCubic(t),
                SwipeMotionCurve.Lazy => EaseInOutSine(t),
                SwipeMotionCurve.Uneven => Math.Clamp(EaseInOutCubic(t) + MathF.Sin(t * MathF.PI * 3.0f) * 0.018f, 0, 1),
                _ => EaseInOutCubic(t)
            };
        }

        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f
                ? 4 * t * t * t
                : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
        }

        private static float EaseOutCubic(float t)
        {
            return 1 - MathF.Pow(1 - t, 3);
        }

        private static float EaseInOutSine(float t)
        {
            return -(MathF.Cos(MathF.PI * t) - 1) / 2;
        }

        private sealed class ElementViewportPosition
        {
            public double Top { get; set; }
            public double Bottom { get; set; }
            public double CenterY { get; set; }
            public double ViewportHeight { get; set; }
        }

        private static async Task<ElementViewportPosition?> GetElementViewportPositionAsync(
            IPage page,
            ILocator element)
        {
            if (page == null || page.IsClosed || element == null)
                return null;

            try
            {
                var handle = await element.ElementHandleAsync();

                if (handle == null)
                    return null;

                return await page.EvaluateAsync<ElementViewportPosition?>(@"
                (el) => {
                    try {
                        if (!el) return null;

                        const r = el.getBoundingClientRect();

                        return {
                            Top: Number(r.top || 0),
                            Bottom: Number(r.bottom || 0),
                            CenterY: Number((r.top + r.bottom) / 2 || 0),
                            ViewportHeight: Number(window.innerHeight || document.documentElement.clientHeight || 0)
                        };
                    } catch {
                        return null;
                    }
                }", handle);
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}