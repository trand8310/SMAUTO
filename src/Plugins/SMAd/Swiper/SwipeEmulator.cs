
namespace SMAd.Swiper
{
    using Microsoft.Playwright;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.PixelFormats;
    using SixLabors.ImageSharp.Processing;
    using SixLabors.ImageSharp.Drawing.Processing;
    using SixLabors.ImageSharp.Drawing;
    using System.Numerics;

    public static class RandomUtil
    {
        public static int NextInt(int min, int max)
        {
            return Random.Shared.Next(min, max);
        }

        public static long NextInt64(long min, long max)
        {
            return Random.Shared.NextInt64(min, max);
        }

        public static double NextDouble()
        {
            return Random.Shared.NextDouble();
        }

        public static double NextDouble(double min, double max)
        {
            return min + Random.Shared.NextDouble() * (max - min);
        }
    }

    public sealed class SwipeOptions
    {
        /// <summary>
        /// 实际滑动距离（像素）
        /// </summary>
        public int DistancePx { get; set; } = 240;

        /// <summary>
        /// 轨迹点数量，越多越平滑
        /// </summary>
        public int PointCount { get; set; } = 18;

        /// <summary>
        /// 每个 touchMove 的基础延迟
        /// </summary>
        public int DelayMs { get; set; } = 12;

        /// <summary>
        /// 中间轨迹微抖动
        /// </summary>
        public float Jitter { get; set; } = 1.0f;

        /// <summary>
        /// 滑动方向
        /// </summary>
        public PageScrollDirection Direction { get; set; } = PageScrollDirection.Random;

        /// <summary>
        /// 滑动区域横向起始比例
        /// </summary>
        public double XStartRatio { get; set; } = 0.40;

        /// <summary>
        /// 滑动区域横向结束比例
        /// </summary>
        public double XEndRatio { get; set; } = 0.60;

        /// <summary>
        /// 滑动区域纵向中心比例
        /// </summary>
        public double YCenterRatio { get; set; } = 0.50;

        /// <summary>
        /// 每次滑动后额外暂停
        /// </summary>
        public (int Min, int Max) PauseRangeMs { get; set; } = (180, 320);
    }

    public static class SwipeEmulator
    {
        #region Public - Core Swipe

        public static async Task<List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)>> SwipeMultipleAsync(
            IPage page,
            ICDPSession client,
            int times,
            int distancePx = 240,
            int pointCount = 18,
            int delayMs = 12,
            float jitter = 1.0f,
            PageScrollDirection direction = PageScrollDirection.Random,
            CancellationToken cancellationToken = default)
        {
            var options = new SwipeOptions
            {
                DistancePx = distancePx,
                PointCount = pointCount,
                DelayMs = delayMs,
                Jitter = jitter,
                Direction = direction,
                XStartRatio = 0.38,
                XEndRatio = 0.62,
                YCenterRatio = 0.50,
                PauseRangeMs = (160, 280)
            };

            return await SwipeMultipleInternalAsync(page, client, times, options, cancellationToken);
        }

        public static async Task<List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)>> SwipeMultipleMicroAsync(
            IPage page,
            ICDPSession client,
            int times,
            int distancePx = 100,
            int pointCount = 8,
            int delayMs = 8,
            float jitter = 0.35f,
            PageScrollDirection direction = PageScrollDirection.Random,
            CancellationToken cancellationToken = default)
        {
            var options = new SwipeOptions
            {
                DistancePx = distancePx,
                PointCount = pointCount,
                DelayMs = delayMs,
                Jitter = jitter,
                Direction = direction,
                XStartRatio = 0.44,
                XEndRatio = 0.56,
                YCenterRatio = 0.50,
                PauseRangeMs = (70, 130)
            };

            return await SwipeMultipleInternalAsync(page, client, times, options, cancellationToken);
        }

        public static async Task<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> SwipeOnceAsync(
            IPage page,
            ICDPSession client,
            Vector2 start,
            Vector2 end,
            int pointCount,
            int delayMs,
            float baseJitter,
            CancellationToken cancellationToken = default)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (page.ViewportSize == null)
                throw new InvalidOperationException("page.ViewportSize is null.");

            pointCount = Math.Clamp(pointCount, 6, 40);
            delayMs = Math.Clamp(delayMs, 6, 40);
            baseJitter = Math.Clamp(baseJitter, 0f, 3f);

            int vw = page.ViewportSize.Width;
            int vh = page.ViewportSize.Height;

            start = ClampPoint(start, vw, vh);
            end = ClampPoint(end, vw, vh);

            float distance = Vector2.Distance(start, end);

            var (cp1, cp2) = GetRandomControlPoints(start, end, distance);
            var points = GetNaturalBezierPoints(start, cp1, cp2, end, pointCount, baseJitter);

            if (points == null || points.Count < 2)
                return (new List<Vector2>(), cp1, cp2, start, end);

            EnsureMinimumInitialMovement(points, MathF.Min(16f, MathF.Max(10f, distance * 0.16f)));

            ForceEarlyCrossTapSlop(
                points,
                step1: MathF.Min(14f, MathF.Max(10f, distance * 0.10f)),
                step2: MathF.Min(24f, MathF.Max(18f, distance * 0.18f)),
                step3: MathF.Min(36f, MathF.Max(28f, distance * 0.26f)));

            double beforeScrollY = await GetPageScrollYAsync(page);
            bool scrollEstablished = false;

            await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchStart",
                ["touchPoints"] = new object[] { new { x = points[0].X, y = points[0].Y } },
                ["modifiers"] = 0
            });

            // 起手按住一小会，但不要过长
            await Task.Delay(RandomUtil.NextInt(35, 68), cancellationToken);

            for (int i = 1; i < points.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                float progress = i / (float)(points.Count - 1);

                // 中间稍快，首尾稍慢
                float speedFactor = 0.84f + 0.16f * (1f - Math.Abs(progress - 0.5f) * 2f);
                int dynamicDelay = Math.Max(7, (int)(delayMs / speedFactor));

                int randomPause = RandomUtil.NextInt(0, 100) < 8
                    ? RandomUtil.NextInt(4, 10)
                    : 0;

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchMove",
                    ["touchPoints"] = new object[] { new { x = points[i].X, y = points[i].Y } },
                    ["modifiers"] = 0
                });

                await Task.Delay(dynamicDelay + randomPause, cancellationToken);

                // 尽早确认页面是否真的滚动
                if (!scrollEstablished && i >= 2)
                {
                    double nowScrollY = await GetPageScrollYAsync(page);
                    if (Math.Abs(nowScrollY - beforeScrollY) >= 6)
                    {
                        scrollEstablished = true;
                    }
                }

                // 如果走了前半段还没滚动成功，尽量再推动一下，跨过“像点击”的区域
                if (!scrollEstablished && i == Math.Min(points.Count - 1, 3))
                {
                    var pPrev = points[Math.Max(0, i - 1)];
                    var pCur = points[i];
                    var dir = pCur - pPrev;

                    if (dir.LengthSquared() < 0.0001f)
                        dir = end - start;

                    if (dir.LengthSquared() < 0.0001f)
                        dir = new Vector2(0, 1);
                    else
                        dir = Vector2.Normalize(dir);

                    var extraPush = ClampPoint(pCur + dir * MathF.Min(22f, MathF.Max(14f, distance * 0.12f)), vw, vh);

                    await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                    {
                        ["type"] = "touchMove",
                        ["touchPoints"] = new object[] { new { x = extraPush.X, y = extraPush.Y } },
                        ["modifiers"] = 0
                    });

                    await Task.Delay(RandomUtil.NextInt(10, 18), cancellationToken);

                    double nowScrollY = await GetPageScrollYAsync(page);
                    if (Math.Abs(nowScrollY - beforeScrollY) >= 6)
                    {
                        scrollEstablished = true;
                    }
                }
            }

            // 收尾前稍停
            await Task.Delay(RandomUtil.NextInt(18, 34), cancellationToken);

            var last = points[^1];
            var prev = points.Count >= 2 ? points[^2] : start;

            var releaseDir = last - prev;
            if (releaseDir.LengthSquared() < 0.0001f)
                releaseDir = end - start;

            if (releaseDir.LengthSquared() < 0.0001f)
                releaseDir = new Vector2(0, 1);
            else
                releaseDir = Vector2.Normalize(releaseDir);

            // 先构造一个基础 release 点
            float releaseOffset = MathF.Min(12f, MathF.Max(7f, distance * 0.08f));
            var rawReleasePoint = ClampPoint(last + releaseDir * releaseOffset, vw, vh);

            // 再把 release 点尽量挪开热点区域
            var releasePoint = await AdjustReleasePointAsync(page, rawReleasePoint, releaseDir, vw, vh);

            await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchMove",
                ["touchPoints"] = new object[] { new { x = releasePoint.X, y = releasePoint.Y } },
                ["modifiers"] = 0
            });

            await Task.Delay(RandomUtil.NextInt(14, 24), cancellationToken);

            await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchEnd",
                ["touchPoints"] = Array.Empty<object>()
            });

            return (points, cp1, cp2, start, end);
        }

        #endregion

        #region Public - Swipe To Element


        /// <summary>
        /// 将元素尽量滑到视口中部区域。
        /// 策略：
        /// 1. 不可见较多 -> 正常滑动
        /// 2. 部分可见 -> 快速探测微滑
        /// 3. 已经足够可见 -> 立即退出
        /// </summary>
        public static async Task<List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)>> SwipeToElementAsync(
            IPage page,
            ICDPSession client,
            ILocator element,
            int maxSwipes = 10,
            CancellationToken cancellationToken = default)
        {
            var allTrajectories = new List<(List<Vector2>, Vector2, Vector2, Vector2, Vector2)>();

            if (page == null || page.IsClosed || client == null || element == null)
                return allTrajectories;

            try
            {
                var viewport = page.ViewportSize;
                int vw = viewport?.Width ?? 0;
                int vh = viewport?.Height ?? 0;

                if (vw <= 0 || vh <= 0)
                    return allTrajectories;

                int swipesCount = 0;
                int noMoveCount = 0;
                PageScrollDirection? lastDirection = null;

                double targetTop = vh * 0.30;
                double targetBottom = vh * 0.72;
                double targetCenter = vh * 0.50;

                while (swipesCount < maxSwipes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (page.IsClosed)
                        break;

                    var box = await element.BoundingBoxAsync();
                    if (box == null)
                        break;

                    double centerY = box.Y + box.Height / 2.0;

                    if (centerY >= targetTop && centerY <= targetBottom)
                        break;

                    bool partiallyVisible = IsElementPartiallyVisible(box, vh);

                    if (partiallyVisible)
                    {
                        double beforeY = box.Y;
                        double beforeCenterY = centerY;

                        PageScrollDirection probeDirection = centerY < targetCenter
                            ? PageScrollDirection.Down
                            : PageScrollDirection.Up;

                        if (lastDirection.HasValue &&
                            lastDirection.Value != probeDirection &&
                            Math.Abs(centerY - targetCenter) < vh * 0.12)
                        {
                            break;
                        }

                        var probe = await SwipeProbeAsync(page, client, probeDirection, cancellationToken);
                        if (probe.Count > 0)
                            allTrajectories.AddRange(probe);

                        lastDirection = probeDirection;

                        await Task.Delay(RandomUtil.NextInt(55, 100), cancellationToken);

                        var afterBox = await element.BoundingBoxAsync();
                        if (afterBox == null)
                            break;

                        double afterY = afterBox.Y;
                        double afterCenterY = afterBox.Y + afterBox.Height / 2.0;

                        double deltaY = Math.Abs(afterY - beforeY);
                        double deltaCenterY = Math.Abs(afterCenterY - beforeCenterY);

                        if (deltaY < 2 && deltaCenterY < 2)
                            break;

                        if (afterCenterY >= targetTop && afterCenterY <= targetBottom)
                            break;

                        swipesCount++;
                        continue;
                    }

                    double beforeFullY = box.Y;
                    double beforeFullCenterY = centerY;

                    PageScrollDirection direction;
                    if (centerY < targetTop)
                    {
                        direction = PageScrollDirection.Down;
                    }
                    else if (centerY > targetBottom)
                    {
                        direction = PageScrollDirection.Up;
                    }
                    else
                    {
                        break;
                    }

                    if (lastDirection.HasValue &&
                        lastDirection.Value != direction &&
                        Math.Abs(centerY - targetCenter) < vh * 0.10)
                    {
                        break;
                    }

                    int distancePx = CalcSwipeDistanceForTarget(centerY, vh, noMoveCount);
                    int pointCount = CalcPointCount(distancePx);

                    List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> trajectory;

                    if (distancePx <= (int)(vh * 0.16))
                    {
                        trajectory = await SwipeMultipleMicroAsync(
                            page,
                            client,
                            times: 1,
                            distancePx: distancePx,
                            pointCount: Math.Max(7, pointCount),
                            delayMs: RandomUtil.NextInt(12, 16),
                            jitter: (float)RandomUtil.NextDouble(0.28, 0.45),
                            direction: direction,
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        trajectory = await SwipeMultipleAsync(
                            page,
                            client,
                            times: 1,
                            distancePx: distancePx,
                            pointCount: pointCount,
                            delayMs: RandomUtil.NextInt(11, 16),
                            jitter: (float)RandomUtil.NextDouble(0.55, 1.00),
                            direction: direction,
                            cancellationToken: cancellationToken);
                    }

                    if (trajectory.Count > 0)
                        allTrajectories.AddRange(trajectory);

                    lastDirection = direction;

                    await Task.Delay(RandomUtil.NextInt(95, 170), cancellationToken);

                    var afterFullBox = await element.BoundingBoxAsync();
                    if (afterFullBox == null)
                        break;

                    double afterFullY = afterFullBox.Y;
                    double afterFullCenterY = afterFullBox.Y + afterFullBox.Height / 2.0;

                    double deltaFullY = Math.Abs(afterFullY - beforeFullY);
                    double deltaFullCenterY = Math.Abs(afterFullCenterY - beforeFullCenterY);

                    if (deltaFullY < 3 && deltaFullCenterY < 3)
                    {
                        noMoveCount++;
                        if (noMoveCount >= 2)
                            break;
                    }
                    else
                    {
                        noMoveCount = 0;
                    }

                    swipesCount++;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }

            return allTrajectories;
        }

        #endregion

        #region Public - PNG / GIF

        public static void DrawTrajectoriesPng(
            List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> trajectories,
            string filePath,
            int width,
            int height)
        {
            var colors = new[] { Color.Red, Color.Blue, Color.Green, Color.Orange, Color.Purple };

            using var bmp = new Image<Rgba32>(width, height);
            bmp.Mutate(ctx => ctx.Clear(Color.White));

            for (int t = 0; t < trajectories.Count; t++)
            {
                var traj = trajectories[t];
                var color = colors[t % colors.Length];

                bmp.Mutate(ctx => ctx.DrawLine(Color.LightGray, 1, new PointF[]
                {
                new(traj.start.X, traj.start.Y),
                new(traj.cp1.X, traj.cp1.Y),
                new(traj.cp2.X, traj.cp2.Y),
                new(traj.end.X, traj.end.Y)
                }));

                bmp.Mutate(ctx => ctx.Fill(Color.Purple, new EllipsePolygon(traj.cp1.X, traj.cp1.Y, 3)));
                bmp.Mutate(ctx => ctx.Fill(Color.Purple, new EllipsePolygon(traj.cp2.X, traj.cp2.Y, 3)));

                for (int i = 1; i < traj.points.Count; i++)
                {
                    bmp.Mutate(ctx => ctx.DrawLine(color, 2, new PointF[]
                    {
                    new(traj.points[i - 1].X, traj.points[i - 1].Y),
                    new(traj.points[i].X, traj.points[i].Y)
                    }));
                    bmp.Mutate(ctx => ctx.Fill(Color.Black, new EllipsePolygon(traj.points[i].X, traj.points[i].Y, 2)));
                }
            }

            bmp.Save(filePath);
        }

        public static void DrawSwipeGifWithBezier(
            List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> trajectories,
            string filePath,
            int width,
            int height,
            int frameDelayMs = 50)
        {
            var colors = new[] { Color.Red, Color.Blue, Color.Green, Color.Orange, Color.Purple };

            using var gif = new Image<Rgba32>(width, height);

            for (int t = 0; t < trajectories.Count; t++)
            {
                var traj = trajectories[t];
                var color = colors[t % colors.Length];

                for (int i = 0; i < traj.points.Count; i++)
                {
                    using var frame = new Image<Rgba32>(width, height);
                    frame.Mutate(ctx => ctx.Clear(Color.White));

                    frame.Mutate(ctx => ctx.DrawLine(Color.LightGray, 1, new PointF[]
                    {
                    new(traj.start.X, traj.start.Y),
                    new(traj.cp1.X, traj.cp1.Y),
                    new(traj.cp2.X, traj.cp2.Y),
                    new(traj.end.X, traj.end.Y)
                    }));

                    frame.Mutate(ctx => ctx.Fill(Color.Purple, new EllipsePolygon(traj.cp1.X, traj.cp1.Y, 3)));
                    frame.Mutate(ctx => ctx.Fill(Color.Purple, new EllipsePolygon(traj.cp2.X, traj.cp2.Y, 3)));

                    for (int j = 1; j <= i; j++)
                    {
                        frame.Mutate(ctx => ctx.DrawLine(color, 2, new PointF[]
                        {
                        new(traj.points[j - 1].X, traj.points[j - 1].Y),
                        new(traj.points[j].X, traj.points[j].Y)
                        }));
                        frame.Mutate(ctx => ctx.Fill(Color.Black, new EllipsePolygon(traj.points[j].X, traj.points[j].Y, 2)));
                    }

                    frame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = Math.Max(1, frameDelayMs / 10);
                    gif.Frames.AddFrame(frame.Frames.RootFrame);
                }
            }

            gif.SaveAsGif(filePath);
        }

        public static void DrawPngAndGif(
            List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> trajectories,
            string pngPath,
            string gifPath,
            int width,
            int height,
            int frameDelayMs = 50)
        {
            DrawTrajectoriesPng(trajectories, pngPath, width, height);
            DrawSwipeGifWithBezier(trajectories, gifPath, width, height, frameDelayMs);
        }

        #endregion

        #region Private - Internal Swipe

        private static async Task<List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)>> SwipeMultipleInternalAsync(
            IPage page,
            ICDPSession client,
            int times,
            SwipeOptions options,
            CancellationToken cancellationToken)
        {
            var allTrajectories = new List<(List<Vector2>, Vector2, Vector2, Vector2, Vector2)>();

            if (page == null || page.IsClosed || client == null || page.ViewportSize == null)
                return allTrajectories;

            int vw = page.ViewportSize.Width;
            int vh = page.ViewportSize.Height;
            if (vw <= 0 || vh <= 0)
                return allTrajectories;

            options.DistancePx = Math.Clamp(options.DistancePx, 36, (int)(vh * 0.72));
            options.PointCount = Math.Clamp(options.PointCount, 6, 40);
            options.DelayMs = Math.Clamp(options.DelayMs, 6, 40);
            options.Jitter = Math.Clamp(options.Jitter, 0f, 3f);

            int minX = (int)(vw * options.XStartRatio);
            int maxX = (int)(vw * options.XEndRatio);
            int centerY = (int)(vh * options.YCenterRatio);

            minX = Math.Clamp(minX, 6, Math.Max(6, vw - 6));
            maxX = Math.Clamp(maxX, minX, Math.Max(minX, vw - 6));
            centerY = Math.Clamp(centerY, 10, Math.Max(10, vh - 10));

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                int preferredX = RandomUtil.NextInt(minX, maxX + 1);
                int preferredY = centerY + RandomUtil.NextInt(-12, 13);
                int xDrift = RandomUtil.NextInt(-7, 8);

                var safeStartBase = await FindSafeTouchStartAsync(page, preferredX, preferredY, vw, vh);
                int x = (int)safeStartBase.X;
                int safeY = (int)safeStartBase.Y;

                Vector2 start;
                Vector2 end;

                switch (options.Direction)
                {
                    case PageScrollDirection.Up:
                        start = new Vector2(x, safeY + options.DistancePx / 2f);
                        end = new Vector2(x + xDrift, safeY - options.DistancePx / 2f);
                        break;

                    case PageScrollDirection.Down:
                        start = new Vector2(x, safeY - options.DistancePx / 2f);
                        end = new Vector2(x + xDrift, safeY + options.DistancePx / 2f);
                        break;

                    case PageScrollDirection.Random:
                    default:
                        bool up = RandomUtil.NextInt(0, 2) == 0;
                        if (up)
                        {
                            start = new Vector2(x, safeY + options.DistancePx / 2f);
                            end = new Vector2(x + xDrift, safeY - options.DistancePx / 2f);
                        }
                        else
                        {
                            start = new Vector2(x, safeY - options.DistancePx / 2f);
                            end = new Vector2(x + xDrift, safeY + options.DistancePx / 2f);
                        }
                        break;
                }

                start = ClampPoint(start, vw, vh);
                end = ClampPoint(end, vw, vh);

                if (Vector2.Distance(start, end) < 36f)
                {
                    if (options.Direction == PageScrollDirection.Up)
                        end.Y = Math.Max(8, start.Y - 36f);
                    else if (options.Direction == PageScrollDirection.Down)
                        end.Y = Math.Min(vh - 8, start.Y + 36f);
                }

                var trajectory = await SwipeOnceAsync(
                    page,
                    client,
                    start,
                    end,
                    options.PointCount,
                    options.DelayMs,
                    options.Jitter,
                    cancellationToken);

                allTrajectories.Add(trajectory);

                int pauseMin = Math.Max(0, options.PauseRangeMs.Min);
                int pauseMax = Math.Max(pauseMin + 1, options.PauseRangeMs.Max);
                await Task.Delay(RandomUtil.NextInt(pauseMin, pauseMax), cancellationToken);
            }

            return allTrajectories;
        }

        /// <summary>
        /// 极轻量探测微滑，元素部分可见时用来快速判断是否还需要修正。
        /// </summary>
        private static async Task<List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)>> SwipeProbeAsync(
            IPage page,
            ICDPSession client,
            PageScrollDirection direction,
            CancellationToken cancellationToken)
        {
            return await SwipeMultipleMicroAsync(
                page,
                client,
                times: 1,
                distancePx: 72,
                pointCount: 7,
                delayMs: 15,
                jitter: 0.34f,
                direction: direction,
                cancellationToken: cancellationToken);
        }

        #endregion

        #region Private - Safety Helpers

        private static async Task<Vector2> FindSafeTouchStartAsync(
            IPage page,
            int preferredX,
            int preferredY,
            int viewportWidth,
            int viewportHeight)
        {
            var candidates = new List<(int x, int y)>();

            for (int dx = -80; dx <= 80; dx += 20)
            {
                for (int dy = -60; dy <= 60; dy += 20)
                {
                    int x = Math.Clamp(preferredX + dx, 8, viewportWidth - 8);
                    int y = Math.Clamp(preferredY + dy, 8, viewportHeight - 8);
                    candidates.Add((x, y));
                }
            }

            candidates = candidates
                .Distinct()
                .OrderBy(c => Math.Abs(c.x - preferredX) + Math.Abs(c.y - preferredY))
                .ToList();

            foreach (var c in candidates)
            {
                try
                {
                    bool isSafe = await page.EvaluateAsync<bool>(
                        @"([x, y]) => {
                        const el = document.elementFromPoint(x, y);
                        if (!el) return false;

                        const interactive = el.closest(
                            'a,button,input,textarea,select,label,summary,[role=""button""],[onclick]'
                        );
                        if (interactive) return false;

                        const style = getComputedStyle(el);
                        if (style.cursor === 'pointer') return false;

                        return true;
                    }",
                        new[] { c.x, c.y });

                    if (isSafe)
                        return new Vector2(c.x, c.y);
                }
                catch
                {
                }
            }

            return new Vector2(
                Math.Clamp(preferredX, 8, viewportWidth - 8),
                Math.Clamp(preferredY, 8, viewportHeight - 8));
        }

        private static async Task<double> GetPageScrollYAsync(IPage page)
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

        private static async Task<Vector2> AdjustReleasePointAsync(
            IPage page,
            Vector2 point,
            Vector2 direction,
            int viewportWidth,
            int viewportHeight)
        {
            if (direction.LengthSquared() < 0.0001f)
                direction = new Vector2(0, 1);
            else
                direction = Vector2.Normalize(direction);

            var candidates = new List<Vector2>();

            for (int i = 0; i < 6; i++)
            {
                float forward = 6 + i * 3;
                candidates.Add(ClampPoint(point + direction * forward, viewportWidth, viewportHeight));
                candidates.Add(ClampPoint(point + direction * forward + new Vector2(10, 0), viewportWidth, viewportHeight));
                candidates.Add(ClampPoint(point + direction * forward + new Vector2(-10, 0), viewportWidth, viewportHeight));
                candidates.Add(ClampPoint(point + direction * forward + new Vector2(16, 0), viewportWidth, viewportHeight));
                candidates.Add(ClampPoint(point + direction * forward + new Vector2(-16, 0), viewportWidth, viewportHeight));
            }

            foreach (var c in candidates)
            {
                try
                {
                    bool safe = await page.EvaluateAsync<bool>(
                        @"([x, y]) => {
                        const el = document.elementFromPoint(x, y);
                        if (!el) return false;

                        const interactive = el.closest(
                            'a,button,input,textarea,select,label,summary,[role=""button""],[onclick]'
                        );
                        if (interactive) return false;

                        const style = getComputedStyle(el);
                        if (style.cursor === 'pointer') return false;

                        return true;
                    }",
                        new[] { (int)c.X, (int)c.Y });

                    if (safe)
                        return c;
                }
                catch
                {
                }
            }

            return ClampPoint(point + direction * 8, viewportWidth, viewportHeight);
        }

        #endregion

        #region Private - Curve / Geometry

        private static (Vector2 cp1, Vector2 cp2) GetRandomControlPoints(Vector2 start, Vector2 end, float distance)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;

            float lateral = Math.Clamp(distance * 0.06f, 2f, 12f);
            float longitudinal = Math.Clamp(distance * 0.05f, 2f, 10f);

            var cp1 = new Vector2(
                start.X + dx * 0.33f + (float)RandomUtil.NextDouble(-lateral, lateral),
                start.Y + dy * 0.33f + (float)RandomUtil.NextDouble(-longitudinal, longitudinal));

            var cp2 = new Vector2(
                start.X + dx * 0.66f + (float)RandomUtil.NextDouble(-lateral, lateral),
                start.Y + dy * 0.66f + (float)RandomUtil.NextDouble(-longitudinal, longitudinal));

            return (cp1, cp2);
        }

        private static List<Vector2> GetNaturalBezierPoints(
            Vector2 p0,
            Vector2 p1,
            Vector2 p2,
            Vector2 p3,
            int steps,
            float baseJitter = 1.0f)
        {
            steps = Math.Max(steps, 2);

            var points = new List<Vector2>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                float tRaw = i / (float)steps;
                float t = EaseInOutCubic(tRaw);

                float mt = 1 - t;
                float x = mt * mt * mt * p0.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * p3.X;
                float y = mt * mt * mt * p0.Y + 3 * mt * mt * t * p1.Y + 3 * mt * t * t * p2.Y + t * t * t * p3.Y;

                if (i != 0 && i != steps && baseJitter > 0)
                {
                    float midFactor = 1f - Math.Abs(0.5f - tRaw) * 2f;
                    float jitter = baseJitter * midFactor;

                    x += (float)RandomUtil.NextDouble(-jitter, jitter);
                    y += (float)RandomUtil.NextDouble(-jitter * 0.25f, jitter * 0.25f);
                }

                points.Add(new Vector2(x, y));
            }

            return points;
        }

        private static void EnsureMinimumInitialMovement(List<Vector2> points, float minDistance)
        {
            if (points == null || points.Count < 3)
                return;

            var p0 = points[0];
            var p1 = points[1];

            float d01 = Vector2.Distance(p0, p1);
            if (d01 >= minDistance)
                return;

            var dir = p1 - p0;
            if (dir.LengthSquared() < 0.0001f)
                dir = points[^1] - p0;

            if (dir.LengthSquared() < 0.0001f)
                dir = new Vector2(0, 1);
            else
                dir = Vector2.Normalize(dir);

            points[1] = p0 + dir * minDistance;

            if (points.Count >= 4)
            {
                var p2 = points[2];
                var d12 = Vector2.Distance(points[1], p2);
                if (d12 < minDistance * 0.45f)
                {
                    points[2] = points[1] + dir * (minDistance * 0.55f);
                }
            }
        }

        private static void ForceEarlyCrossTapSlop(List<Vector2> points, float step1, float step2, float step3)
        {
            if (points == null || points.Count < 4)
                return;

            var p0 = points[0];
            var dir = points[^1] - p0;

            if (dir.LengthSquared() < 0.0001f)
                dir = new Vector2(0, 1);
            else
                dir = Vector2.Normalize(dir);

            points[1] = p0 + dir * Math.Max(6f, step1);
            points[2] = p0 + dir * Math.Max(step1 + 4f, step2);
            points[3] = p0 + dir * Math.Max(step2 + 4f, step3);
        }

        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f
                ? 4 * t * t * t
                : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
        }

        private static Vector2 ClampPoint(Vector2 p, int width, int height)
        {
            float x = Math.Clamp(p.X, 2, Math.Max(2, width - 2));
            float y = Math.Clamp(p.Y, 2, Math.Max(2, height - 2));
            return new Vector2(x, y);
        }

        private static bool IsElementPartiallyVisible(LocatorBoundingBoxResult box, int viewportHeight)
        {
            double top = box.Y;
            double bottom = box.Y + box.Height;
            return bottom > 0 && top < viewportHeight;
        }

        #endregion

        #region Private - Calculations

        /// <summary>
        /// 根据元素中心与目标中心的距离，计算滑动距离。
        /// </summary>
        private static int CalcSwipeDistanceForTarget(double elementCenterY, int viewportHeight, int noMoveCount)
        {
            double targetCenter = viewportHeight * 0.5;
            double distance = Math.Abs(elementCenterY - targetCenter);

            int distancePx;
            if (distance > viewportHeight * 0.45)
                distancePx = (int)(viewportHeight * 0.40);
            else if (distance > viewportHeight * 0.30)
                distancePx = (int)(viewportHeight * 0.28);
            else if (distance > viewportHeight * 0.18)
                distancePx = (int)(viewportHeight * 0.18);
            else if (distance > viewportHeight * 0.10)
                distancePx = (int)(viewportHeight * 0.12);
            else
                distancePx = (int)(viewportHeight * 0.08);

            distancePx += noMoveCount * 30;

            return Math.Clamp(distancePx, 50, (int)(viewportHeight * 0.50));
        }

        /// <summary>
        /// 根据滑动距离估算轨迹点数量。
        /// </summary>
        private static int CalcPointCount(int distancePx)
        {
            if (distancePx >= 360) return 20;
            if (distancePx >= 240) return 17;
            if (distancePx >= 160) return 13;
            if (distancePx >= 100) return 9;
            if (distancePx >= 70) return 7;
            return 6;
        }

        #endregion
    }

}
