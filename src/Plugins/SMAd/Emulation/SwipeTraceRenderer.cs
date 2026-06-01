using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;



namespace PlaywrightHumanInput
{
    /// <summary>
    /// HumanSwipeTrace 的 GIF/PNG 轨迹渲染器。
    /// 
    /// 注意：
    /// 1. 如果 HumanSwipeTrace.Points 有值，会使用真实轨迹点。
    /// 2. 如果 Points 没有值，会根据 Start/End/Steps 生成一条直线兜底轨迹，但这不是原始真实轨迹。
    /// 
    /// 需要 NuGet：
    /// SixLabors.ImageSharp
    /// SixLabors.ImageSharp.Drawing
    /// </summary>
    public static class HumanSwipeTraceRenderer
    {
        private static readonly Color[] TraceColors =
        {
            Color.Red,
            Color.DeepSkyBlue,
            Color.LimeGreen,
            Color.Orange,
            Color.MediumPurple,
            Color.DeepPink,
            Color.Brown,
            Color.Gold,
        };

        public static void DrawTrajectoriesPng(
            IEnumerable<HumanSwipeTrace> traces,
            string filePath,
            int width,
            int height,
            bool drawStartEnd = true,
            bool drawPointDots = true,
            bool transparentBackground = false)
        {
            var list = NormalizeTraces(traces);
            if (list.Count == 0)
                return;

            EnsureDir(filePath);

            using var image = CreateCanvas(width, height, transparentBackground);

            for (int t = 0; t < list.Count; t++)
            {
                var trace = list[t];
                var points = GetRenderablePoints(trace);

                if (points.Count <= 0)
                    continue;

                var color = TraceColors[t % TraceColors.Length];

                image.Mutate(ctx =>
                {
                    if (drawStartEnd)
                    {
                        ctx.Fill(Color.DarkGreen, new EllipsePolygon((float)trace.StartX, (float)trace.StartY, 6));
                        ctx.Fill(Color.DarkRed, new EllipsePolygon((float)trace.EndX, (float)trace.EndY, 6));
                    }

                    for (int i = 1; i < points.Count; i++)
                    {
                        var p1 = points[i - 1];
                        var p2 = points[i];

                        ctx.DrawLine(
                            color,
                            2.4f,
                            new PointF[]
                            {
                                new PointF((float)p1.X, (float)p1.Y),
                                new PointF((float)p2.X, (float)p2.Y)
                            });

                        if (drawPointDots)
                        {
                            ctx.Fill(Color.Black.WithAlpha(0.70f), new EllipsePolygon((float)p2.X, (float)p2.Y, 1.8f));
                        }
                    }
                });
            }

            image.Save(filePath);
        }

        public static void DrawSwipeGif(
            IEnumerable<HumanSwipeTrace> traces,
            string filePath,
            int width,
            int height,
            int defaultFrameDelayMs = 35,
            bool useTraceDelay = true,
            bool drawStartEnd = true,
            bool highlightCurrent = true,
            bool tailFade = true,
            int tailPointCount = 0,
            bool transparentBackground = false,
            bool infiniteLoop = true,
            int lingerLastFrameCount = 8)
        {
            var list = NormalizeTraces(traces);
            if (list.Count == 0)
                return;

            EnsureDir(filePath);

            using var gif = CreateCanvas(width, height, transparentBackground);
            bool hasAnyFrame = false;

            for (int t = 0; t < list.Count; t++)
            {
                var trace = list[t];
                var points = GetRenderablePoints(trace);

                if (points.Count <= 0)
                    continue;

                var color = TraceColors[t % TraceColors.Length];

                for (int i = 0; i < points.Count; i++)
                {
                    using var frame = CreateCanvas(width, height, transparentBackground);

                    DrawTraceFrame(
                        frame,
                        trace,
                        points,
                        color,
                        maxPointIndex: i,
                        drawStartEnd: drawStartEnd,
                        highlightCurrent: highlightCurrent,
                        tailFade: tailFade,
                        tailPointCount: tailPointCount);

                    int delay = defaultFrameDelayMs;

                    if (useTraceDelay && points[i].DelayMs > 0)
                        delay = points[i].DelayMs;

                    SetGifFrameDelay(frame, delay);

                    gif.Frames.AddFrame(frame.Frames.RootFrame);
                    hasAnyFrame = true;
                }

                for (int k = 0; k < lingerLastFrameCount; k++)
                {
                    using var lingerFrame = CreateCanvas(width, height, transparentBackground);

                    DrawTraceFrame(
                        lingerFrame,
                        trace,
                        points,
                        color,
                        maxPointIndex: points.Count - 1,
                        drawStartEnd: drawStartEnd,
                        highlightCurrent: highlightCurrent,
                        tailFade: tailFade,
                        tailPointCount: tailPointCount);

                    SetGifFrameDelay(lingerFrame, defaultFrameDelayMs);
                    gif.Frames.AddFrame(lingerFrame.Frames.RootFrame);
                }
            }

            if (!hasAnyFrame)
                return;

            if (gif.Frames.Count > 1)
                gif.Frames.RemoveFrame(0);

            gif.Metadata.GetGifMetadata().RepeatCount = infiniteLoop ? (ushort)0 : (ushort)1;
            gif.SaveAsGif(filePath);
        }

        public static void DrawPngAndGif(
            IEnumerable<HumanSwipeTrace> traces,
            string pngPath,
            string gifPath,
            int width,
            int height,
            int defaultFrameDelayMs = 35)
        {
            DrawTrajectoriesPng(
                traces,
                pngPath,
                width,
                height);

            DrawSwipeGif(
                traces,
                gifPath,
                width,
                height,
                defaultFrameDelayMs);
        }

        public static void DrawEachTraceGif(
            IEnumerable<HumanSwipeTrace> traces,
            string outputDir,
            int width,
            int height,
            int defaultFrameDelayMs = 35)
        {
            var list = NormalizeTraces(traces);
            if (list.Count == 0)
                return;

            Directory.CreateDirectory(outputDir);

            for (int i = 0; i < list.Count; i++)
            {
                var one = new List<HumanSwipeTrace> { list[i] };

                string pngPath = System.IO.Path.Combine(outputDir, $"trace_{i + 1:000}.png");
                string gifPath = System.IO.Path.Combine(outputDir, $"trace_{i + 1:000}.gif");

                DrawPngAndGif(
                    one,
                    pngPath,
                    gifPath,
                    width,
                    height,
                    defaultFrameDelayMs);
            }
        }

        private static void DrawTraceFrame(
            Image<Rgba32> image,
            HumanSwipeTrace trace,
            IReadOnlyList<HumanSwipeTracePoint> points,
            Color color,
            int maxPointIndex,
            bool drawStartEnd,
            bool highlightCurrent,
            bool tailFade,
            int tailPointCount)
        {
            if (points.Count == 0)
                return;

            maxPointIndex = Math.Clamp(maxPointIndex, 0, points.Count - 1);

            image.Mutate(ctx =>
            {
                if (drawStartEnd)
                {
                    ctx.Fill(Color.DarkGreen, new EllipsePolygon((float)trace.StartX, (float)trace.StartY, 6));
                    ctx.Fill(Color.DarkRed, new EllipsePolygon((float)trace.EndX, (float)trace.EndY, 6));
                }

                int startIndex = 1;

                if (tailPointCount > 0)
                    startIndex = Math.Max(1, maxPointIndex - tailPointCount + 1);

                for (int i = startIndex; i <= maxPointIndex; i++)
                {
                    var p1 = points[i - 1];
                    var p2 = points[i];

                    float ratio = maxPointIndex <= 0 ? 1f : i / (float)Math.Max(1, maxPointIndex);

                    Color lineColor = color;
                    float lineWidth = 2.5f;

                    if (tailFade)
                    {
                        float alpha;

                        if (tailPointCount > 0)
                        {
                            int localIndex = i - startIndex + 1;
                            int localTotal = Math.Max(1, maxPointIndex - startIndex + 1);
                            alpha = 0.18f + localIndex / (float)localTotal * 0.82f;
                        }
                        else
                        {
                            alpha = 0.16f + ratio * 0.84f;
                        }

                        lineColor = color.WithAlpha(alpha);
                        lineWidth = 1.4f + ratio * 1.8f;
                    }

                    ctx.DrawLine(
                        lineColor,
                        lineWidth,
                        new PointF[]
                        {
                            new PointF((float)p1.X, (float)p1.Y),
                            new PointF((float)p2.X, (float)p2.Y)
                        });
                }

                if (highlightCurrent)
                {
                    var current = points[maxPointIndex];

                    float radius = 4.2f;

                    if (current.RadiusX > 0)
                        radius = (float)Math.Clamp(current.RadiusX, 3.0, 8.0);

                    ctx.Fill(Color.Black, new EllipsePolygon((float)current.X, (float)current.Y, radius));
                    ctx.Draw(Color.White.WithAlpha(0.75f), 1.3f, new EllipsePolygon((float)current.X, (float)current.Y, radius + 3));
                }
            });
        }

        private static IReadOnlyList<HumanSwipeTracePoint> GetRenderablePoints(HumanSwipeTrace trace)
        {
            if (trace.Points != null && trace.Points.Count > 0)
                return trace.Points;

            return BuildFallbackPoints(trace);
        }

        private static List<HumanSwipeTracePoint> BuildFallbackPoints(HumanSwipeTrace trace)
        {
            int steps = Math.Max(2, trace.Steps <= 0 ? 24 : trace.Steps);
            int delay = trace.TotalDelayMs > 0 ? Math.Max(1, trace.TotalDelayMs / steps) : 35;

            var points = new List<HumanSwipeTracePoint>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                double t = i / (double)steps;

                points.Add(new HumanSwipeTracePoint
                {
                    X = trace.StartX + (trace.EndX - trace.StartX) * t,
                    Y = trace.StartY + (trace.EndY - trace.StartY) * t,
                    DelayMs = delay,
                    RadiusX = 4,
                    RadiusY = 4,
                    Force = 0.8,
                    RotationAngle = 0
                });
            }

            return points;
        }

        private static Image<Rgba32> CreateCanvas(
            int width,
            int height,
            bool transparentBackground)
        {
            var image = new Image<Rgba32>(width, height);

            image.Mutate(ctx =>
            {
                ctx.Clear(transparentBackground ? Color.Transparent : Color.White);
            });

            return image;
        }

        private static void SetGifFrameDelay(Image<Rgba32> frame, int delayMs)
        {
            delayMs = Math.Max(10, delayMs);

            var meta = frame.Frames.RootFrame.Metadata.GetGifMetadata();
            meta.FrameDelay = Math.Max(1, delayMs / 10);
            meta.DisposalMethod = GifDisposalMethod.RestoreToBackground;
        }

        private static List<HumanSwipeTrace> NormalizeTraces(IEnumerable<HumanSwipeTrace> traces)
        {
            if (traces == null)
                return new List<HumanSwipeTrace>();

            return traces
                .Where(x => x != null)
                .ToList();
        }

        private static void EnsureDir(string filePath)
        {
            Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(filePath)
                ?? AppContext.BaseDirectory);
        }
    }
}
