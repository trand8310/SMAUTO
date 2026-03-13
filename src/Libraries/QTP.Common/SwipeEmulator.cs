using System.Numerics;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;


namespace QTP.Common
{

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

    public enum ScrollDirection
    {
        Up,
        Down,
        Random
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
        public ScrollDirection Direction { get; set; } = ScrollDirection.Random;

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
            ScrollDirection direction = ScrollDirection.Random,
            CancellationToken cancellationToken = default)
        {
            var options = new SwipeOptions
            {
                DistancePx = distancePx,
                PointCount = pointCount,
                DelayMs = delayMs,
                Jitter = jitter,
                Direction = direction,
                XStartRatio = 0.40,
                XEndRatio = 0.60,
                YCenterRatio = 0.50,
                PauseRangeMs = (180, 320)
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
            ScrollDirection direction = ScrollDirection.Random,
            CancellationToken cancellationToken = default)
        {
            var options = new SwipeOptions
            {
                DistancePx = distancePx,
                PointCount = pointCount,
                DelayMs = delayMs,
                Jitter = jitter,
                Direction = direction,
                XStartRatio = 0.46,
                XEndRatio = 0.54,
                YCenterRatio = 0.50,
                PauseRangeMs = (40, 90)
            };

            return await SwipeMultipleInternalAsync(page, client, times, options, cancellationToken);
        }

        public static async Task<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> SwipeOnceAsync(
            ICDPSession client,
            Vector2 start,
            Vector2 end,
            int pointCount,
            int delayMs,
            float baseJitter,
            CancellationToken cancellationToken = default)
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));

            pointCount = Math.Clamp(pointCount, 4, 40);
            delayMs = Math.Clamp(delayMs, 4, 40);
            baseJitter = Math.Clamp(baseJitter, 0f, 3f);

            float distance = Vector2.Distance(start, end);
            var (cp1, cp2) = GetRandomControlPoints(start, end, distance);
            var points = GetNaturalBezierPoints(start, cp1, cp2, end, pointCount, baseJitter);

            await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchStart",
                ["touchPoints"] = new object[] { new { x = points[0].X, y = points[0].Y } },
                ["modifiers"] = 0
            });

            // 起手按住一小会儿
            await Task.Delay(RandomUtil.NextInt(18, 36), cancellationToken);

            for (int i = 1; i < points.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                float progress = i / (float)(points.Count - 1);

                // 中段快一点，首尾慢一点，但不能过快
                float speedFactor = 0.78f + 0.22f * (1f - Math.Abs(progress - 0.5f) * 2f);
                int dynamicDelay = Math.Max(5, (int)(delayMs / speedFactor));

                int randomPause = RandomUtil.NextInt(0, 100) < 7 ? RandomUtil.NextInt(3, 10) : 0;

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchMove",
                    ["touchPoints"] = new object[] { new { x = points[i].X, y = points[i].Y } },
                    ["modifiers"] = 0
                });

                await Task.Delay(dynamicDelay + randomPause, cancellationToken);
            }

            await Task.Delay(RandomUtil.NextInt(8, 18), cancellationToken);

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
        /// 兼容旧方法名，内部转新版
        /// </summary>
        public static async Task<List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)>> SwipeToElement2Async(
            IPage page,
            ICDPSession client,
            ILocator element,
            int maxSwipes = 10)
        {
            return await SwipeToElementAsync(page, client, element, maxSwipes, CancellationToken.None);
        }

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
                ScrollDirection? lastDirection = null;

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

                    // 已经足够处于可操作区域，直接退出
                    if (centerY >= targetTop && centerY <= targetBottom)
                        break;

                    bool partiallyVisible = IsElementPartiallyVisible(box, vh);

                    // 部分可见时，优先快速探测，避免完整滑动耗时
                    if (partiallyVisible)
                    {
                        double beforeY = box.Y;
                        double beforeCenterY = centerY;

                        ScrollDirection probeDirection = centerY < targetCenter
                            ? ScrollDirection.Down
                            : ScrollDirection.Up;

                        // 接近中心且方向可能反转时，直接退出，避免轻微抖动
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

                        await Task.Delay(RandomUtil.NextInt(45, 90), cancellationToken);

                        var afterBox = await element.BoundingBoxAsync();
                        if (afterBox == null)
                            break;

                        double afterY = afterBox.Y;
                        double afterCenterY = afterBox.Y + afterBox.Height / 2.0;

                        double deltaY = Math.Abs(afterY - beforeY);
                        double deltaCenterY = Math.Abs(afterCenterY - beforeCenterY);

                        // 部分可见情况下，微滑没变化就立即退出
                        if (deltaY < 2 && deltaCenterY < 2)
                            break;

                        // 微滑后已进入目标区，立即退出
                        if (afterCenterY >= targetTop && afterCenterY <= targetBottom)
                            break;

                        swipesCount++;
                        continue;
                    }

                    // 明显不在视口内，正常滑动
                    double beforeFullY = box.Y;
                    double beforeFullCenterY = centerY;

                    ScrollDirection direction;
                    if (centerY < targetTop)
                    {
                        direction = ScrollDirection.Down;
                    }
                    else if (centerY > targetBottom)
                    {
                        direction = ScrollDirection.Up;
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
                            pointCount: pointCount,
                            delayMs: RandomUtil.NextInt(8, 12),
                            jitter: (float)RandomUtil.NextDouble(0.30, 0.70),
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
                            delayMs: RandomUtil.NextInt(9, 14),
                            jitter: (float)RandomUtil.NextDouble(0.55, 1.05),
                            direction: direction,
                            cancellationToken: cancellationToken);
                    }

                    if (trajectory.Count > 0)
                        allTrajectories.AddRange(trajectory);

                    lastDirection = direction;

                    await Task.Delay(RandomUtil.NextInt(90, 160), cancellationToken);

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
                // 正常取消
            }
            catch
            {
                // 这里按你的项目接日志
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

            options.DistancePx = Math.Clamp(options.DistancePx, 30, (int)(vh * 0.60));
            options.PointCount = Math.Clamp(options.PointCount, 4, 40);
            options.DelayMs = Math.Clamp(options.DelayMs, 4, 40);
            options.Jitter = Math.Clamp(options.Jitter, 0f, 3f);

            int minX = (int)(vw * options.XStartRatio);
            int maxX = (int)(vw * options.XEndRatio);
            int centerY = (int)(vh * options.YCenterRatio);

            minX = Math.Clamp(minX, 2, Math.Max(2, vw - 2));
            maxX = Math.Clamp(maxX, minX, Math.Max(minX, vw - 2));
            centerY = Math.Clamp(centerY, 2, Math.Max(2, vh - 2));

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                int x = RandomUtil.NextInt(minX, maxX + 1);
                int yOffset = RandomUtil.NextInt(-14, 15);

                Vector2 start;
                Vector2 end;

                switch (options.Direction)
                {
                    case ScrollDirection.Up:
                        start = new Vector2(x, centerY + yOffset + options.DistancePx / 2f);
                        end = new Vector2(
                            x + RandomUtil.NextInt(-5, 6),
                            centerY + yOffset - options.DistancePx / 2f);
                        break;

                    case ScrollDirection.Down:
                        start = new Vector2(x, centerY + yOffset - options.DistancePx / 2f);
                        end = new Vector2(
                            x + RandomUtil.NextInt(-5, 6),
                            centerY + yOffset + options.DistancePx / 2f);
                        break;

                    case ScrollDirection.Random:
                    default:
                        bool up = RandomUtil.NextInt(0, 2) == 0;
                        if (up)
                        {
                            start = new Vector2(x, centerY + yOffset + options.DistancePx / 2f);
                            end = new Vector2(
                                x + RandomUtil.NextInt(-5, 6),
                                centerY + yOffset - options.DistancePx / 2f);
                        }
                        else
                        {
                            start = new Vector2(x, centerY + yOffset - options.DistancePx / 2f);
                            end = new Vector2(
                                x + RandomUtil.NextInt(-5, 6),
                                centerY + yOffset + options.DistancePx / 2f);
                        }
                        break;
                }

                start = ClampPoint(start, vw, vh);
                end = ClampPoint(end, vw, vh);

                var trajectory = await SwipeOnceAsync(
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
            ScrollDirection direction,
            CancellationToken cancellationToken)
        {
            return await SwipeMultipleMicroAsync(
                page,
                client,
                times: 1,
                distancePx: 60,
                pointCount: 5,
                delayMs: 6,
                jitter: 0.18f,
                direction: direction,
                cancellationToken: cancellationToken);
        }

        #endregion

        #region Private - Curve / Geometry

        private static (Vector2 cp1, Vector2 cp2) GetRandomControlPoints(Vector2 start, Vector2 end, float distance)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;

            // 控制点范围更收敛，减少左右扭动
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

                // 首尾点不抖，中间轻微抖动
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
            if (distancePx >= 240) return 16;
            if (distancePx >= 140) return 12;
            if (distancePx >= 90) return 8;
            return 6;
        }

        #endregion





    }

}
