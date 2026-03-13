using Microsoft.Playwright;


namespace SMAd.Human
{
     public enum TouchScrollDirection
    {
        Up,
        Down
    }
    public sealed class TouchScrollOptions
    {
        /// <summary>
        /// 总滑动距离（像素）。<=0 时自动按视口高度估算。
        /// </summary>
        public int DistancePx { get; set; } = 0;

        /// <summary>
        /// 分段数量。1=一次性滑动；2/3=更像人手分段带节奏。
        /// </summary>
        public int Segments { get; set; } = 2;

        /// <summary>
        /// 起点横向区域比例最小值。
        /// </summary>
        public double XStartRatio { get; set; } = 0.35;

        /// <summary>
        /// 起点横向区域比例最大值。
        /// </summary>
        public double XEndRatio { get; set; } = 0.65;

        /// <summary>
        /// 起点纵向区域比例最小值。
        /// </summary>
        public double YStartRatio { get; set; } = 0.42;

        /// <summary>
        /// 起点纵向区域比例最大值。
        /// </summary>
        public double YEndRatio { get; set; } = 0.58;

        /// <summary>
        /// 速度范围（越大越快）。
        /// </summary>
        public (int Min, int Max) SpeedRange { get; set; } = (700, 1150);

        /// <summary>
        /// 每段滑动后停顿范围。
        /// </summary>
        public (int Min, int Max) PauseRangeMs { get; set; } = (120, 280);

        /// <summary>
        /// 是否允许轻微横向漂移。
        /// </summary>
        public bool AllowXDrift { get; set; } = true;

        /// <summary>
        /// 横向漂移最大值。
        /// </summary>
        public int MaxXDriftPx { get; set; } = 12;

        /// <summary>
        /// 是否尽量避开点击热点。
        /// </summary>
        public bool AvoidClickableHotspots { get; set; } = true;

        /// <summary>
        /// 是否校验滚动是否真的发生。
        /// </summary>
        public bool VerifyScrollMoved { get; set; } = true;

        /// <summary>
        /// 单段最小距离。
        /// </summary>
        public int MinSegmentDistancePx { get; set; } = 40;
    }

    public static class HumanTouchScrollEmulator
    {
        public static async Task<bool> ScrollOnceAsync(
            IPage page,
            ICDPSession client,
            TouchScrollDirection direction,
            TouchScrollOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new TouchScrollOptions();

            if (page == null || page.IsClosed || client == null)
                return false;

            var viewport = page.ViewportSize;
            int vw = viewport?.Width ?? 0;
            int vh = viewport?.Height ?? 0;

            if (vw <= 0 || vh <= 0)
                return false;

            int totalDistance = options.DistancePx > 0
                ? options.DistancePx
                : Math.Clamp((int)(vh * NextDouble(0.18, 0.34)), 110, 320);

            int segments = Math.Clamp(options.Segments, 1, 3);

            // 人类手势：一段占大头，剩余小修正
            var distances = SplitDistanceLikeHuman(totalDistance, segments, options.MinSegmentDistancePx);

            // 起点随机，不总在同一个位置
            int startX = NextInt(
                Math.Clamp((int)(vw * options.XStartRatio), 6, Math.Max(6, vw - 6)),
                Math.Clamp((int)(vw * options.XEndRatio), 7, Math.Max(7, vw - 5)) + 1);

            int startY = NextInt(
                Math.Clamp((int)(vh * options.YStartRatio), 8, Math.Max(8, vh - 8)),
                Math.Clamp((int)(vh * options.YEndRatio), 9, Math.Max(9, vh - 7)) + 1);

            if (options.AvoidClickableHotspots)
            {
                var safePoint = await FindSaferStartPointAsync(
                    page,
                    vw,
                    vh,
                    startX,
                    startY,
                    options,
                    cancellationToken);

                startX = safePoint.x;
                startY = safePoint.y;
            }

            int beforeScrollTop = 0;
            if (options.VerifyScrollMoved)
            {
                beforeScrollTop = await GetScrollTopAsync(page);
            }

            int currentX = startX;
            int currentY = startY;

            for (int i = 0; i < distances.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int distance = distances[i];
                int xDistance = 0;

                if (options.AllowXDrift)
                {
                    xDistance = NextInt(-options.MaxXDriftPx, options.MaxXDriftPx + 1);

                    // 首段更稳，后段轻微一点
                    if (i == 0)
                        xDistance = (int)Math.Round(xDistance * 0.6);
                }

                int yDistance = direction == TouchScrollDirection.Down
                    ? distance
                    : -distance;

                int speed = NextInt(options.SpeedRange.Min, options.SpeedRange.Max + 1);

                await client.SendAsync("Input.synthesizeScrollGesture", new Dictionary<string, object>
                {
                    ["x"] = currentX,
                    ["y"] = currentY,
                    ["xDistance"] = xDistance,
                    ["yDistance"] = yDistance,
                    ["speed"] = speed,
                    ["gestureSourceType"] = "touch",
                    ["repeatCount"] = 0,
                    ["repeatDelayMs"] = 0
                });

                // 下一段起点轻微漂移，模拟同一根手指连续二次调整
                currentX = Math.Clamp(
                    currentX + NextInt(-8, 9),
                    6,
                    Math.Max(6, vw - 6));

                currentY = Math.Clamp(
                    currentY + NextInt(-10, 11),
                    8,
                    Math.Max(8, vh - 8));

                // 段间停顿：像人看一眼再继续
                if (i < distances.Count - 1)
                {
                    await Task.Delay(
                        NextInt(options.PauseRangeMs.Min, options.PauseRangeMs.Max + 1),
                        cancellationToken);
                }
            }

            if (!options.VerifyScrollMoved)
                return true;

            int afterScrollTop = await GetScrollTopAsync(page);
            return afterScrollTop != beforeScrollTop;
        }

        public static async Task<int> ScrollMultipleAsync(
            IPage page,
            ICDPSession client,
            int count,
            TouchScrollDirection direction,
            Func<IPage, Task<bool>>? stopPredicate = null,
            TouchScrollOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null || count <= 0)
                return 0;

            int successCount = 0;

            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                bool moved = await ScrollOnceAsync(
                    page,
                    client,
                    direction,
                    options,
                    cancellationToken);

                if (moved)
                    successCount++;

                // 每次完整滑动后再停一下，更像人阅读
                await Task.Delay(NextInt(450, 1400), cancellationToken);

                if (stopPredicate != null && await stopPredicate(page))
                    break;
            }

            return successCount;
        }

        public static TouchScrollOptions CreateNormalReadOptions() => new()
        {
            Segments = 2,
            SpeedRange = (720, 1050),
            PauseRangeMs = (120, 260),
            AllowXDrift = true,
            MaxXDriftPx = 10,
            AvoidClickableHotspots = true,
            VerifyScrollMoved = true,
            MinSegmentDistancePx = 40
        };

        public static TouchScrollOptions CreateMicroAdjustOptions() => new()
        {
            DistancePx = 90,
            Segments = 1,
            SpeedRange = (650, 900),
            PauseRangeMs = (80, 160),
            AllowXDrift = true,
            MaxXDriftPx = 6,
            AvoidClickableHotspots = true,
            VerifyScrollMoved = true,
            MinSegmentDistancePx = 36
        };

        public static TouchScrollOptions CreateFastFlipOptions() => new()
        {
            DistancePx = 280,
            Segments = 2,
            SpeedRange = (950, 1400),
            PauseRangeMs = (80, 180),
            AllowXDrift = true,
            MaxXDriftPx = 12,
            AvoidClickableHotspots = true,
            VerifyScrollMoved = true,
            MinSegmentDistancePx = 50
        };

        private static async Task<(int x, int y)> FindSaferStartPointAsync(
            IPage page,
            int vw,
            int vh,
            int fallbackX,
            int fallbackY,
            TouchScrollOptions options,
            CancellationToken cancellationToken)
        {
            var candidates = new List<(int x, int y)>
        {
            (fallbackX, fallbackY),
            (ClampInt((int)(vw * 0.18), 6, vw - 6), ClampInt((int)(vh * 0.50), 8, vh - 8)),
            (ClampInt((int)(vw * 0.82), 6, vw - 6), ClampInt((int)(vh * 0.50), 8, vh - 8)),
            (ClampInt((int)(vw * 0.28), 6, vw - 6), ClampInt((int)(vh * 0.54), 8, vh - 8)),
            (ClampInt((int)(vw * 0.72), 6, vw - 6), ClampInt((int)(vh * 0.46), 8, vh - 8))
        };

            foreach (var p in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool clickable = await IsClickableHotspotAsync(page, p.x, p.y);
                if (!clickable)
                    return p;
            }

            return (fallbackX, fallbackY);
        }

        private static async Task<bool> IsClickableHotspotAsync(IPage page, int x, int y)
        {
            try
            {
                return await page.EvaluateAsync<bool>(
                    @"([x, y]) => {
                    const el = document.elementFromPoint(x, y);
                    if (!el) return false;

                    const clickable = el.closest(
                        'a,button,input,textarea,select,label,[role=""button""],[onclick],[tabindex]'
                    );
                    if (clickable) return true;

                    const style = window.getComputedStyle(el);
                    if (style.cursor === 'pointer') return true;

                    return false;
                }",
                    new[] { x, y });
            }
            catch
            {
                return false;
            }
        }

        private static async Task<int> GetScrollTopAsync(IPage page)
        {
            try
            {
                return await page.EvaluateAsync<int>(
                    @"() => {
                    const el = document.scrollingElement || document.documentElement || document.body;
                    return el ? el.scrollTop : 0;
                }");
            }
            catch
            {
                return 0;
            }
        }

        private static List<int> SplitDistanceLikeHuman(int totalDistance, int segments, int minSegment)
        {
            totalDistance = Math.Max(totalDistance, minSegment);

            if (segments <= 1)
                return new List<int> { totalDistance };

            if (segments == 2)
            {
                int first = (int)Math.Round(totalDistance * NextDouble(0.62, 0.78));
                int second = totalDistance - first;

                first = Math.Max(first, minSegment);
                second = Math.Max(second, minSegment);

                int sum = first + second;
                if (sum != totalDistance)
                {
                    second -= (sum - totalDistance);
                    if (second < minSegment)
                    {
                        second = minSegment;
                        first = totalDistance - second;
                    }
                }

                return new List<int> { first, second };
            }

            // 3 段：大段 + 小修正 + 小收尾
            int part1 = (int)Math.Round(totalDistance * NextDouble(0.50, 0.64));
            int part2 = (int)Math.Round(totalDistance * NextDouble(0.20, 0.28));
            int part3 = totalDistance - part1 - part2;

            part1 = Math.Max(part1, minSegment);
            part2 = Math.Max(part2, minSegment);
            part3 = Math.Max(part3, minSegment);

            int total = part1 + part2 + part3;
            if (total != totalDistance)
            {
                part3 -= (total - totalDistance);

                if (part3 < minSegment)
                {
                    part3 = minSegment;
                    int remain = totalDistance - part3;
                    part1 = (int)Math.Round(remain * 0.68);
                    part2 = remain - part1;

                    part1 = Math.Max(part1, minSegment);
                    part2 = Math.Max(part2, minSegment);
                }
            }

            return new List<int> { part1, part2, part3 };
        }

        private static int NextInt(int min, int max) => Random.Shared.Next(min, max);

        private static double NextDouble(double min, double max)
            => min + Random.Shared.NextDouble() * (max - min);

        private static int ClampInt(int value, int min, int max)
            => Math.Clamp(value, min, Math.Max(min, max));
    }
}
