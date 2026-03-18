using Microsoft.Playwright;

namespace SMAd.General
{

    public sealed class TouchScrollOptions
    {
        /// <summary>
        /// 总滑动距离（像素），<=0 时自动计算
        /// </summary>
        public int DistancePx { get; set; } = 0;

        /// <summary>
        /// 一次滑动拆成几段，2 比较像人
        /// </summary>
        public int Segments { get; set; } = 2;

        /// <summary>
        /// 起点横向比例范围
        /// </summary>
        public double XStartRatio { get; set; } = 0.35;
        public double XEndRatio { get; set; } = 0.65;

        /// <summary>
        /// 起点纵向比例范围
        /// </summary>
        public double YStartRatio { get; set; } = 0.42;
        public double YEndRatio { get; set; } = 0.58;

        /// <summary>
        /// 速度范围，越大越快
        /// </summary>
        public (int Min, int Max) SpeedRange { get; set; } = (720, 1100);

        /// <summary>
        /// 段间停顿
        /// </summary>
        public (int Min, int Max) SegmentPauseRangeMs { get; set; } = (120, 260);

        /// <summary>
        /// 是否允许轻微横向漂移
        /// </summary>
        public bool AllowXDrift { get; set; } = true;

        /// <summary>
        /// 横向漂移最大值
        /// </summary>
        public int MaxXDriftPx { get; set; } = 10;

        /// <summary>
        /// 是否尽量避开点击热点
        /// </summary>
        public bool AvoidClickableHotspots { get; set; } = true;

        /// <summary>
        /// 是否校验滚动是否真的发生
        /// </summary>
        public bool VerifyScrollMoved { get; set; } = true;

        /// <summary>
        /// 单段最小距离
        /// </summary>
        public int MinSegmentDistancePx { get; set; } = 40;
    }

    public sealed class TouchPageScroller
    {
        private readonly Action<string>? _logger;

        public TouchPageScroller(Action<string>? logger = null)
        {
            _logger = logger;
        }

        public void LogWriteLine(string message)
        {
            _logger?.Invoke(message);
        }

        /// <summary>
        /// 与你原先风格一致的封装方法
        /// direction: -1 = Down, 其他 = Up
        /// </summary>
        public async Task TouchPageScroll(
            IPage page,
            ICDPSession client,
            int scrollCount,
            int direction,
            Func<IPage, Task<bool>>? predexp = null,
            int time_delay = 0,
            CancellationToken cancellationToken = default)
        {
            await TouchPageScrollAsync(
                page: page,
                client: client,
                scrollCount: scrollCount,
                direction: direction,
                predexp: predexp,
                timeDelay: time_delay,
                cancellationToken: cancellationToken);
        }

        public async Task TouchPageScrollAsync(
            IPage page,
            ICDPSession client,
            int scrollCount,
            int direction,
            Func<IPage, Task<bool>>? predexp = null,
            int timeDelay = 0,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null || scrollCount <= 0)
                return;

            try
            {
                var scrollDirection = direction == -1
                    ? PageScrollDirection.Down
                    : PageScrollDirection.Up;

                LogWriteLine(scrollDirection == PageScrollDirection.Down
                    ? $"TouchScrollDown:{scrollCount}次"
                    : $"TouchScrollUp:{scrollCount}次");

                int delayMsAfterScroll = timeDelay > 0
                    ? timeDelay
                    : Random.Shared.Next(500, 2000);

                for (int i = 0; i < scrollCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (page.IsClosed)
                        break;

                    var options = CreateHumanLikeOptions(page);

                    bool moved = await ScrollOnceInternalAsync(
                        page,
                        client,
                        scrollDirection,
                        options,
                        cancellationToken);

                    LogWriteLine($"TouchPageScroll step={i + 1}, moved={moved}");

                    await Task.Delay(delayMsAfterScroll, cancellationToken);

                    if (predexp != null && await predexp(page))
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                LogWriteLine("TouchPageScrollAsync canceled");
            }
            catch (Exception ex)
            {
                LogWriteLine($"TouchPageScrollAsync error: {ex.Message}");
            }
        }

        /// <summary>
        /// 微滑，用于轻微修正
        /// </summary>
        public async Task<bool> TouchPageMicroScrollAsync(
            IPage page,
            ICDPSession client,
            int direction,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null)
                return false;

            var scrollDirection = direction == -1
                ? PageScrollDirection.Down
                : PageScrollDirection.Up;

            var options = CreateMicroAdjustOptions(page);

            return await ScrollOnceInternalAsync(
                page,
                client,
                scrollDirection,
                options,
                cancellationToken);
        }

        /// <summary>
        /// 快速翻页型滑动
        /// </summary>
        public async Task<bool> TouchPageFastScrollAsync(
            IPage page,
            ICDPSession client,
            int direction,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null)
                return false;

            var scrollDirection = direction == -1
                ? PageScrollDirection.Down
                : PageScrollDirection.Up;

            var options = CreateFastFlipOptions(page);

            return await ScrollOnceInternalAsync(
                page,
                client,
                scrollDirection,
                options,
                cancellationToken);
        }

        private TouchScrollOptions CreateHumanLikeOptions(IPage page)
        {
            int vh = page.ViewportSize?.Height ?? 800;
            int autoDistance = Math.Clamp((int)(vh * NextDouble(0.18, 0.34)), 110, 320);

            return new TouchScrollOptions
            {
                DistancePx = autoDistance,
                Segments = Random.Shared.Next(1, 100) < 65 ? 2 : 3,
                XStartRatio = 0.35,
                XEndRatio = 0.65,
                YStartRatio = 0.42,
                YEndRatio = 0.58,
                SpeedRange = (720, 1100),
                SegmentPauseRangeMs = (120, 260),
                AllowXDrift = true,
                MaxXDriftPx = 10,
                AvoidClickableHotspots = true,
                VerifyScrollMoved = true,
                MinSegmentDistancePx = 40
            };
        }

        private TouchScrollOptions CreateMicroAdjustOptions(IPage page)
        {
            int vh = page.ViewportSize?.Height ?? 800;
            int autoDistance = Math.Clamp((int)(vh * NextDouble(0.10, 0.16)), 60, 120);

            return new TouchScrollOptions
            {
                DistancePx = autoDistance,
                Segments = 1,
                XStartRatio = 0.38,
                XEndRatio = 0.62,
                YStartRatio = 0.45,
                YEndRatio = 0.55,
                SpeedRange = (650, 900),
                SegmentPauseRangeMs = (80, 160),
                AllowXDrift = true,
                MaxXDriftPx = 6,
                AvoidClickableHotspots = true,
                VerifyScrollMoved = true,
                MinSegmentDistancePx = 36
            };
        }

        private TouchScrollOptions CreateFastFlipOptions(IPage page)
        {
            int vh = page.ViewportSize?.Height ?? 800;
            int autoDistance = Math.Clamp((int)(vh * NextDouble(0.26, 0.42)), 180, 380);

            return new TouchScrollOptions
            {
                DistancePx = autoDistance,
                Segments = 2,
                XStartRatio = 0.34,
                XEndRatio = 0.66,
                YStartRatio = 0.40,
                YEndRatio = 0.60,
                SpeedRange = (950, 1400),
                SegmentPauseRangeMs = (80, 180),
                AllowXDrift = true,
                MaxXDriftPx = 12,
                AvoidClickableHotspots = true,
                VerifyScrollMoved = true,
                MinSegmentDistancePx = 50
            };
        }

        private async Task<bool> ScrollOnceInternalAsync(
            IPage page,
            ICDPSession client,
            PageScrollDirection direction,
            TouchScrollOptions options,
            CancellationToken cancellationToken)
        {
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
            var distances = SplitDistanceLikeHuman(totalDistance, segments, options.MinSegmentDistancePx);

            int startX = Random.Shared.Next(
                Math.Clamp((int)(vw * options.XStartRatio), 6, Math.Max(6, vw - 6)),
                Math.Clamp((int)(vw * options.XEndRatio), 7, Math.Max(7, vw - 5)) + 1);

            int startY = Random.Shared.Next(
                Math.Clamp((int)(vh * options.YStartRatio), 8, Math.Max(8, vh - 8)),
                Math.Clamp((int)(vh * options.YEndRatio), 9, Math.Max(9, vh - 7)) + 1);

            if (options.AvoidClickableHotspots)
            {
                var saferPoint = await FindSaferStartPointAsync(
                    page,
                    vw,
                    vh,
                    startX,
                    startY,
                    cancellationToken);

                startX = saferPoint.x;
                startY = saferPoint.y;
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
                    xDistance = Random.Shared.Next(-options.MaxXDriftPx, options.MaxXDriftPx + 1);
                    if (i == 0)
                        xDistance = (int)Math.Round(xDistance * 0.6);
                }

                int yDistance = direction == PageScrollDirection.Down
                    ? distance
                    : -distance;

                int speed = Random.Shared.Next(options.SpeedRange.Min, options.SpeedRange.Max + 1);

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

                currentX = Math.Clamp(
                    currentX + Random.Shared.Next(-8, 9),
                    6,
                    Math.Max(6, vw - 6));

                currentY = Math.Clamp(
                    currentY + Random.Shared.Next(-10, 11),
                    8,
                    Math.Max(8, vh - 8));

                if (i < distances.Count - 1)
                {
                    await Task.Delay(
                        Random.Shared.Next(options.SegmentPauseRangeMs.Min, options.SegmentPauseRangeMs.Max + 1),
                        cancellationToken);
                }
            }

            if (!options.VerifyScrollMoved)
                return true;

            int afterScrollTop = await GetScrollTopAsync(page);
            return afterScrollTop != beforeScrollTop;
        }

        private async Task<(int x, int y)> FindSaferStartPointAsync(
            IPage page,
            int vw,
            int vh,
            int fallbackX,
            int fallbackY,
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

        private async Task<bool> IsClickableHotspotAsync(IPage page, int x, int y)
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

        private async Task<int> GetScrollTopAsync(IPage page)
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
                    second -= sum - totalDistance;
                    if (second < minSegment)
                    {
                        second = minSegment;
                        first = totalDistance - second;
                    }
                }

                return new List<int> { first, second };
            }

            int part1 = (int)Math.Round(totalDistance * NextDouble(0.50, 0.64));
            int part2 = (int)Math.Round(totalDistance * NextDouble(0.20, 0.28));
            int part3 = totalDistance - part1 - part2;

            part1 = Math.Max(part1, minSegment);
            part2 = Math.Max(part2, minSegment);
            part3 = Math.Max(part3, minSegment);

            int total = part1 + part2 + part3;
            if (total != totalDistance)
            {
                part3 -= total - totalDistance;

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

        private static double NextDouble(double min, double max)
        {
            return min + Random.Shared.NextDouble() * (max - min);
        }

        private static int ClampInt(int value, int min, int max)
        {
            return Math.Clamp(value, min, Math.Max(min, max));
        }
    }
}
