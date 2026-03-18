using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Numerics;
using Color = SixLabors.ImageSharp.Color;
using PointF = SixLabors.ImageSharp.PointF;


namespace SMAd.Swiperv2
{

    public static class RandomUtil
    {
        /// <summary>
        /// 返回[min, max)之间的随机整数
        /// </summary>
        public static int NextInt(int min, int max)
        {
            return Random.Shared.Next(min, max);
        }
        public static Int64 NextInt64(Int64 min, Int64 max)
        {
            return Random.Shared.NextInt64(min, max);
        }

        /// <summary>
        /// 返回[0.0, 1.0)之间的随机浮点数
        /// </summary>
        public static double NextDouble()
        {
            return Random.Shared.NextDouble();
        }

        /// <summary>
        /// 返回[min, max)之间的随机浮点数
        /// </summary>
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

    public class SwipeEmulator
    {
        #region 自动滑动
        /// <summary>
        /// 
        /// </summary>
        /// <param name="page"></param>
        /// <param name="client"></param>
        /// <param name="times"></param>
        /// <param name="steps">步数越多，滑动越平滑</param>
        /// <param name="delayMs"></param>
        /// <param name="jitter">jitter 可以调节随机抖动幅度（单位像素）</param>
        /// <param name="direction"></param>
        /// <returns></returns>
        public static async Task<List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)>> SwipeMultipleAsync(
            IPage page,
            ICDPSession client,
            int times, int steps = 25, int delayMs = 16, float jitter = 2f, ScrollDirection direction = ScrollDirection.Random)
        {
            var allTrajectories = new List<(List<Vector2>, Vector2, Vector2, Vector2, Vector2)>();

            int minX = (int)(page.ViewportSize.Width * 0.15);
            int maxX = (int)(page.ViewportSize.Width * 0.85);

            int minY = (int)(page.ViewportSize.Height * 0.15);
            int maxY = (int)(page.ViewportSize.Height * 0.85);


            for (int i = 0; i < times; i++)
            {
                Vector2 start, end;
                bool vertical = true;

                int x = RandomUtil.NextInt(minX, maxX);

                switch (direction)
                {
                    case ScrollDirection.Up:
                        start = new Vector2(x, maxY); // 手指下方
                        end = new Vector2(x, minY);   // 手指上方
                        break;
                    case ScrollDirection.Down:
                        start = new Vector2(x, minY); // 手指上方
                        end = new Vector2(x, maxY);   // 手指下方
                        break;
                    case ScrollDirection.Random:
                        vertical = RandomUtil.NextInt(0, 2) == 0;
                        if (vertical)
                        {
                            int y1 = RandomUtil.NextInt(minY, maxY);
                            int y2 = RandomUtil.NextInt(minY, maxY);
                            start = new Vector2(x, y1);
                            end = new Vector2(x, y2);
                        }
                        else
                        {
                            int y = RandomUtil.NextInt(minY, maxY);
                            int x1 = RandomUtil.NextInt(minX, maxX);
                            int x2 = RandomUtil.NextInt(minX, maxX);
                            start = new Vector2(x1, y);
                            end = new Vector2(x2, y);
                        }
                        break;
                    default:
                        start = new Vector2(x, maxY);
                        end = new Vector2(x, minY);
                        break;
                }

                var trajectory = await SwipeOnceAsync(client, start, end, steps, delayMs, jitter);
                allTrajectories.Add(trajectory);
                await Task.Delay(RandomUtil.NextInt(300, 800));
            }
            return allTrajectories;
        }

        public static async Task<List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)>> SwipeMultipleMicroAsync(
        IPage page,
        ICDPSession client,
        int times, int steps = 25, int delayMs = 16, float jitter = 2f, ScrollDirection direction = ScrollDirection.Random)
        {
            var allTrajectories = new List<(List<Vector2>, Vector2, Vector2, Vector2, Vector2)>();

            int minX = (int)(page.ViewportSize.Width * 0.45);
            int maxX = (int)(page.ViewportSize.Width * 0.65);

            int minY = (int)(page.ViewportSize.Height * 0.45);
            int maxY = (int)(page.ViewportSize.Height * 0.65);


            for (int i = 0; i < times; i++)
            {
                Vector2 start, end;
                bool vertical = true;

                int x = RandomUtil.NextInt(minX, maxX);

                switch (direction)
                {
                    case ScrollDirection.Up:
                        start = new Vector2(x, maxY); // 手指下方
                        end = new Vector2(x, minY);   // 手指上方
                        break;
                    case ScrollDirection.Down:
                        start = new Vector2(x, minY); // 手指上方
                        end = new Vector2(x, maxY);   // 手指下方
                        break;
                    case ScrollDirection.Random:
                        vertical = RandomUtil.NextInt(0, 2) == 0;
                        if (vertical)
                        {
                            int y1 = RandomUtil.NextInt(minY, maxY);
                            int y2 = RandomUtil.NextInt(minY, maxY);
                            start = new Vector2(x, y1);
                            end = new Vector2(x, y2);
                        }
                        else
                        {
                            int y = RandomUtil.NextInt(minY, maxY);
                            int x1 = RandomUtil.NextInt(minX, maxX);
                            int x2 = RandomUtil.NextInt(minX, maxX);
                            start = new Vector2(x1, y);
                            end = new Vector2(x2, y);
                        }
                        break;
                    default:
                        start = new Vector2(x, maxY);
                        end = new Vector2(x, minY);
                        break;
                }

                var trajectory = await SwipeOnceAsync(client, start, end, steps, delayMs, jitter);
                allTrajectories.Add(trajectory);
                await Task.Delay(RandomUtil.NextInt(300, 800));
            }
            return allTrajectories;
        }




        public static async Task<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> SwipeOnceAsync(
            ICDPSession client,
            Vector2 start, Vector2 end, int steps, int delayMs, float baseJitter)
        {
            // 计算滑动距离
            float distance = Vector2.Distance(start, end);

            // 控制点随机化，抖动幅度随距离缩放
            var (cp1, cp2) = GetRandomControlPoints(start, end, distance);

            // 获取贝塞尔曲线轨迹点
            var points = GetNaturalBezierPoints(start, cp1, cp2, end, steps, baseJitter);

            // touchStart
            await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                { "type","touchStart"},
                { "touchPoints", new object[] { new { x = points[0].X, y = points[0].Y } } },
                { "modifiers",0},
            });

            // touchMove
            for (int i = 1; i < points.Count; i++)
            {
                // 模拟轻微加速度：t越大，延迟略短
                float speedFactor = 1f - EaseInOutCubic(i / (float)steps);
                int dynamicDelay = (int)(delayMs * speedFactor);

                // 随机微顿挫
                int pause = RandomUtil.NextInt(0, 100) < 10 ? RandomUtil.NextInt(5, 30) : 0;

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                    { "type","touchMove"},
                    { "touchPoints", new object[] { new { x = points[i].X, y = points[i].Y } } },
                    { "modifiers",0},
                });

                await Task.Delay(dynamicDelay + pause);
            }

            // touchEnd
            await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>() {
                { "type","touchEnd"},
                { "touchPoints", new object[] {} }
            });

            return (points, cp1, cp2, start, end);
        }

        #endregion

        #region 贝塞尔曲线 + 自然曲线


        private static (Vector2, Vector2) GetRandomControlPoints(Vector2 start, Vector2 end, float distance)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;

            float jitterScale = Math.Clamp(distance / 500f, 0.5f, 1.5f); // 距离越远，抖动幅度越小

            var cp1 = new Vector2(
                start.X + dx * 0.3f + (float)RandomUtil.NextDouble(-20, 20) * jitterScale,
                start.Y + dy * 0.3f + (float)RandomUtil.NextDouble(-20, 20) * jitterScale
            );

            var cp2 = new Vector2(
                start.X + dx * 0.6f + (float)RandomUtil.NextDouble(-20, 20) * jitterScale,
                start.Y + dy * 0.6f + (float)RandomUtil.NextDouble(-20, 20) * jitterScale
            );

            return (cp1, cp2);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="p0"></param>
        /// <param name="p1"></param>
        /// <param name="p2"></param>
        /// <param name="p3"></param>
        /// <param name="steps"></param>
        /// <param name="jitter">jitter 可以调节随机抖动幅度（单位像素）</param>
        /// <returns></returns>
        private static List<Vector2> GetNaturalBezierPoints(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int steps, float baseJitter = 2f)
        {
            var points = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float tRaw = i / (float)steps;
                float t = EaseInOutCubic(tRaw);

                float mt = 1 - t;
                float x = mt * mt * mt * p0.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * p3.X;
                float y = mt * mt * mt * p0.Y + 3 * mt * mt * t * p1.Y + 3 * mt * t * t * p2.Y + t * t * t * p3.Y;

                // 随机抖动，抖动幅度随滑动距离变化
                float jitter = baseJitter * (1f + (float)RandomUtil.NextDouble(-0.5, 0.5));
                x += (float)(RandomUtil.NextDouble() * 2 - 1) * jitter;
                y += (float)(RandomUtil.NextDouble() * 2 - 1) * jitter;

                points.Add(new Vector2(x, y));
            }
            return points;
        }

        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
        }

        #endregion

        #region PNG + GIF 绘制

        public static void DrawTrajectoriesPng(List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> trajectories,
            string filePath, int width, int height)
        {
            var colors = new Color[] { Color.Red, Color.Blue, Color.Green, Color.Orange, Color.Purple };

            using var bmp = new Image<Rgba32>(width, height);
            bmp.Mutate(ctx => ctx.Clear(Color.White));

            for (int t = 0; t < trajectories.Count; t++)
            {
                var traj = trajectories[t];
                var points = traj.points;
                var cp1 = traj.cp1;
                var cp2 = traj.cp2;
                var start = traj.start;
                var end = traj.end;
                var color = colors[t % colors.Length];

                bmp.Mutate(ctx => ctx.DrawLine(Color.LightGray, 1, new PointF[] {
                new PointF(start.X, start.Y),
                new PointF(cp1.X, cp1.Y),
                new PointF(cp2.X, cp2.Y),
                new PointF(end.X, end.Y)
            }));

                bmp.Mutate(ctx => ctx.Fill(Color.Purple, new EllipsePolygon(cp1.X, cp1.Y, 3)));
                bmp.Mutate(ctx => ctx.Fill(Color.Purple, new EllipsePolygon(cp2.X, cp2.Y, 3)));

                for (int i = 1; i < points.Count; i++)
                {
                    bmp.Mutate(ctx => ctx.DrawLine(color, 2, new PointF[] {
                    new PointF(points[i-1].X, points[i-1].Y),
                    new PointF(points[i].X, points[i].Y)
                }));
                    bmp.Mutate(ctx => ctx.Fill(Color.Black, new EllipsePolygon(points[i].X, points[i].Y, 2)));
                }
            }

            bmp.Save(filePath);
        }

        public static void DrawSwipeGifWithBezier(List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> trajectories,
            string filePath, int width, int height, int frameDelayMs = 50)
        {
            var colors = new Color[] { Color.Red, Color.Blue, Color.Green, Color.Orange, Color.Purple };

            using var gif = new Image<Rgba32>(width, height);
            //gif.Frames.RemoveFrame(0);

            for (int t = 0; t < trajectories.Count; t++)
            {
                var traj = trajectories[t];
                var points = traj.points;
                var cp1 = traj.cp1;
                var cp2 = traj.cp2;
                var start = traj.start;
                var end = traj.end;
                var color = colors[t % colors.Length];

                for (int i = 0; i < points.Count; i++)
                {
                    using var frame = new Image<Rgba32>(width, height);
                    frame.Mutate(ctx => ctx.Clear(Color.White));

                    frame.Mutate(ctx => ctx.DrawLine(Color.LightGray, 1, new PointF[] {
                    new PointF(start.X, start.Y),
                    new PointF(cp1.X, cp1.Y),
                    new PointF(cp2.X, cp2.Y),
                    new PointF(end.X, end.Y)
                }));

                    frame.Mutate(ctx => ctx.Fill(Color.Purple, new EllipsePolygon(cp1.X, cp1.Y, 3)));
                    frame.Mutate(ctx => ctx.Fill(Color.Purple, new EllipsePolygon(cp2.X, cp2.Y, 3)));

                    for (int j = 1; j <= i; j++)
                    {
                        frame.Mutate(ctx => ctx.DrawLine(color, 2, new PointF[] {
                        new PointF(points[j-1].X, points[j-1].Y),
                        new PointF(points[j].X, points[j].Y)
                    }));
                        frame.Mutate(ctx => ctx.Fill(Color.Black, new EllipsePolygon(points[j].X, points[j].Y, 2)));
                    }

                    frame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = frameDelayMs / 10;
                    gif.Frames.AddFrame(frame.Frames.RootFrame);
                }
            }

            gif.SaveAsGif(filePath);
        }

        public static void DrawPngAndGif(List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)> trajectories,
            string pngPath, string gifPath, int width, int height, int frameDelayMs = 50)
        {
            DrawTrajectoriesPng(trajectories, pngPath, width, height);
            DrawSwipeGifWithBezier(trajectories, gifPath, width, height, frameDelayMs);
        }





        #endregion



        /// <summary>
        /// 根据元素距离动态计算 steps
        /// </summary>
        private static int CalcSteps(double distance, int viewportHeight)
        {
            if (distance <= 0) return 0;

            // 基于距离计算步数：距离越远，步数越多
            int minSteps = 10;
            int maxSteps = 40;
            int steps = (int)(minSteps + (maxSteps - minSteps) * Math.Min(distance / (viewportHeight * 2), 1.0));

            // 随机微调步数，增加自然感
            steps += RandomUtil.NextInt(-2, 3);
            return Math.Max(steps, minSteps);
        }

        public static async Task<List<(List<Vector2> points, Vector2 cp1, Vector2 cp2, Vector2 start, Vector2 end)>> SwipeToElementAsync(IPage page, ICDPSession client, ILocator element, int maxSwipes = 10)
        {
            var allTrajectories = new List<(List<Vector2>, Vector2, Vector2, Vector2, Vector2)>();
            try
            {
                int swipesCount = 0;
                int vw = page.ViewportSize.Width;
                int vh = page.ViewportSize.Height;

                var box = await element.BoundingBoxAsync();
                if (box == null) return allTrajectories;

                while (swipesCount < maxSwipes && (box.Y < 0 || box.Y + box.Height > vh))
                {
                    double distance = 0;
                    ScrollDirection direction;

                    if (box.Y < 0)
                    {
                        direction = ScrollDirection.Down;
                        distance = -box.Y;
                    }
                    else if (box.Y + box.Height > vh)
                    {
                        direction = ScrollDirection.Up;
                        distance = box.Y + box.Height - vh;
                    }
                    else
                        break;

                    int steps = CalcSteps(distance, vh);

                    var trajectory = await SwipeMultipleAsync(
                        page, client,
                        1,
                        direction: direction,
                        steps: steps,
                        delayMs: RandomUtil.NextInt(14, 18),
                        jitter: (float)RandomUtil.NextDouble(2, 3));

                    allTrajectories.AddRange(trajectory);

                    box = await element.BoundingBoxAsync();
                    swipesCount++;
                }
            }
            catch
            {
                // 可加日志
            }

            return allTrajectories;
        }





    }

}
