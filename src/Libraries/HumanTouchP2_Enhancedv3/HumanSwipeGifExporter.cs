


namespace PlaywrightHumanInput;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

public static class HumanSwipeGifExporter
{
    public sealed class Options
    {
        public int Width { get; set; } = 480;
        public int Height { get; set; } = 900;
        public int Padding { get; set; } = 28;
        public int InfoAreaHeight { get; set; } = 145;
        public Color BackgroundColor { get; set; } = Color.White;
        public Color ScreenBackgroundColor { get; set; } = Color.ParseHex("#F7F7F7");
        public Color BorderColor { get; set; } = Color.ParseHex("#888888");
        public Color PlannedPathColor { get; set; } = Color.ParseHex("#C8CDD4");
        public Color ActualPathColor { get; set; } = Color.ParseHex("#1976D2");
        public Color StartPointColor { get; set; } = Color.ParseHex("#20A464");
        public Color EndPointColor { get; set; } = Color.ParseHex("#FF9800");
        public Color FingerColor { get; set; } = Color.ParseHex("#E53935");
        public Color FingerBorderColor { get; set; } = Color.ParseHex("#8E1010");
        public Color TextColor { get; set; } = Color.ParseHex("#222222");
        public float BorderThickness { get; set; } = 2f;
        public float PlannedPathThickness { get; set; } = 2f;
        public float ActualPathThickness { get; set; } = 4f;
        public float DefaultFingerRadius { get; set; } = 10f;
        public float ForceRadiusFactor { get; set; } = 0.30f;
        public int DefaultFrameDelayMs { get; set; } = 16;
        public int MinimumFrameDelayMs { get; set; } = 10;
        public bool AutoFit { get; set; } = true;
        public double MaxAutoFitScale { get; set; } = 1.35;
        public bool ShowDebugInfo { get; set; } = true;
        public float FontSize { get; set; } = 15f;
        public string FontFamilyName { get; set; } = "Arial";
        public bool LoopForever { get; set; } = true;
    }

    private sealed class RenderPoint
    {
        public double SourceX { get; init; }
        public double SourceY { get; init; }
        public double X { get; set; }
        public double Y { get; set; }
        public int DelayMs { get; init; }
        public double RadiusX { get; init; }
        public double RadiusY { get; init; }
        public double Force { get; init; }
        public double RotationAngle { get; init; }
        public double TimeMs { get; init; }
        public double VelocityPxPerSecond { get; init; }
    }

    private readonly struct ViewTransform
    {
        public ViewTransform(double minX, double minY, double scale, double offsetX, double offsetY)
        {
            MinX = minX;
            MinY = minY;
            Scale = scale;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public double MinX { get; }
        public double MinY { get; }
        public double Scale { get; }
        public double OffsetX { get; }
        public double OffsetY { get; }

        public PointF Map(double x, double y) => new(
            (float)(OffsetX + (x - MinX) * Scale),
            (float)(OffsetY + (y - MinY) * Scale));
    }

    public static void ExportGif(HumanSwipeTrace trace, string outputPath, Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (trace.Points == null || trace.Points.Count == 0)
            throw new InvalidOperationException("HumanSwipeTrace.Points 为空，无法生成 GIF。");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("outputPath 不能为空。", nameof(outputPath));

        options ??= new Options();

        var points = trace.Points.Select(p => new RenderPoint
        {
            SourceX = p.X,
            SourceY = p.Y,
            X = p.X,
            Y = p.Y,
            DelayMs = p.DelayMs,
            RadiusX = p.RadiusX,
            RadiusY = p.RadiusY,
            Force = p.Force,
            RotationAngle = p.RotationAngle,
            TimeMs = p.TimeMs,
            VelocityPxPerSecond = p.VelocityPxPerSecond
        }).ToList();

        ViewTransform transform = CreateTransform(trace, points, options);
        foreach (RenderPoint p in points)
        {
            PointF mapped = transform.Map(p.SourceX, p.SourceY);
            p.X = mapped.X;
            p.Y = mapped.Y;
        }

        using Image<Rgba32> gif = BuildGif(trace, points, transform, options);
        string? dir = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        if (options.LoopForever) gif.Metadata.GetGifMetadata().RepeatCount = 0;
        gif.Save(outputPath, new GifEncoder());
    }

    public static void ExportAll(IReadOnlyList<HumanSwipeTrace> traces, string directory, Options? options = null)
    {
        ArgumentNullException.ThrowIfNull(traces);
        Directory.CreateDirectory(directory);
        for (int i = 0; i < traces.Count; i++)
            ExportGif(traces[i], System.IO.Path.Combine(directory, $"swipe_{i + 1:000}.gif"), options);
    }

    private static Image<Rgba32> BuildGif(HumanSwipeTrace trace, List<RenderPoint> points, ViewTransform transform, Options options)
    {
        Image<Rgba32>? result = null;

        if (trace.StartHoldMs > 0)
        {
            using var frame = RenderFrame(trace, points, transform, 0, options, FramePhase.StartHold);
            SetFrameDelay(frame.Frames.RootFrame, trace.StartHoldMs, options);
            AppendFrame(ref result, frame);
        }

        for (int i = 0; i < points.Count; i++)
        {
            using var frame = RenderFrame(trace, points, transform, i, options, FramePhase.Moving);
            int delay = points[i].DelayMs > 0 ? points[i].DelayMs : options.DefaultFrameDelayMs;
            SetFrameDelay(frame.Frames.RootFrame, delay, options);
            AppendFrame(ref result, frame);
        }

        if (trace.EndHoldMs > 0)
        {
            using var frame = RenderFrame(trace, points, transform, points.Count - 1, options, FramePhase.EndHold);
            SetFrameDelay(frame.Frames.RootFrame, trace.EndHoldMs, options);
            AppendFrame(ref result, frame);
        }

        return result ?? throw new InvalidOperationException("没有生成任何 GIF 帧。");
    }

    private static Image<Rgba32> RenderFrame(HumanSwipeTrace trace, List<RenderPoint> points, ViewTransform transform, int currentIndex, Options options, FramePhase phase)
    {
        var image = new Image<Rgba32>(
            options.Width,
            options.Height,
            options.BackgroundColor.ToPixel<Rgba32>());

        RenderPoint current = points[currentIndex];

        image.Mutate(context => context.Paint(canvas =>
        {
            DrawScreen(canvas, options);
            DrawPlannedPath(canvas, points, options);
            DrawActualPath(canvas, points, currentIndex, options);
            DrawStartEndPoints(canvas, points, options);
            DrawPlannedEndPoint(canvas, trace, transform);
            DrawCurrentFinger(canvas, current, transform.Scale, options);
            if (options.ShowDebugInfo)
                DrawDebugInfo(canvas, trace, current, currentIndex, points.Count, phase, options);
        }));

        return image;
    }

    private static void DrawScreen(DrawingCanvas canvas, Options options)
    {
        int top = options.InfoAreaHeight + options.Padding;
        int width = options.Width - options.Padding * 2;
        int height = options.Height - top - options.Padding;
        if (width <= 0 || height <= 0) return;

        var rect = new Rectangle(options.Padding, top, width, height);
        canvas.Fill(Brushes.Solid(options.ScreenBackgroundColor), rect);
        canvas.Draw(Pens.Solid(options.BorderColor, options.BorderThickness), rect);
    }

    private static void DrawPlannedPath(DrawingCanvas canvas, List<RenderPoint> points, Options options)
    {
        if (points.Count < 2) return;
        canvas.Draw(Pens.Solid(options.PlannedPathColor, options.PlannedPathThickness), BuildPath(points, points.Count - 1));
    }

    private static void DrawActualPath(DrawingCanvas canvas, List<RenderPoint> points, int currentIndex, Options options)
    {
        if (currentIndex < 1) return;
        canvas.Draw(Pens.Solid(options.ActualPathColor, options.ActualPathThickness), BuildPath(points, currentIndex));
    }

    private static IPath BuildPath(List<RenderPoint> points, int lastIndex)
    {
        var builder = new PathBuilder();
        builder.MoveTo(new PointF((float)points[0].X, (float)points[0].Y));
        for (int i = 1; i <= lastIndex; i++)
            builder.LineTo(new PointF((float)points[i].X, (float)points[i].Y));
        return builder.Build();
    }

    private static void DrawStartEndPoints(DrawingCanvas canvas, List<RenderPoint> points, Options options)
    {
        RenderPoint start = points[0];
        RenderPoint end = points[^1];

        canvas.Fill(Brushes.Solid(options.StartPointColor),
            new EllipsePolygon((float)start.X, (float)start.Y, 7f, 7f));
        canvas.Fill(Brushes.Solid(options.EndPointColor),
            new EllipsePolygon((float)end.X, (float)end.Y, 7f, 7f));
    }

    private static void DrawPlannedEndPoint(DrawingCanvas canvas, HumanSwipeTrace trace, ViewTransform transform)
    {
        PointF p = transform.Map(trace.PlannedEndX, trace.PlannedEndY);
        var ellipse = new EllipsePolygon(p.X, p.Y, 5f, 5f);
        canvas.Draw(Pens.Solid(Color.ParseHex("#9C27B0"), 2f), ellipse);
    }

    private static void DrawCurrentFinger(DrawingCanvas canvas, RenderPoint point, double transformScale, Options options)
    {
        double rx = point.RadiusX > 0 ? point.RadiusX * transformScale : options.DefaultFingerRadius;
        double ry = point.RadiusY > 0 ? point.RadiusY * transformScale : options.DefaultFingerRadius;

        rx = Math.Clamp(rx, options.DefaultFingerRadius * 0.65, options.DefaultFingerRadius * 3.0);
        ry = Math.Clamp(ry, options.DefaultFingerRadius * 0.65, options.DefaultFingerRadius * 3.0);

        double force = Math.Clamp(point.Force, 0.0, 1.0);
        double forceScale = 1.0 + force * options.ForceRadiusFactor;
        rx *= forceScale;
        ry *= forceScale;

        var ellipse = new EllipsePolygon((float)point.X, (float)point.Y, (float)rx, (float)ry);
        canvas.Fill(Brushes.Solid(options.FingerColor.WithAlpha(0.72f)), ellipse);
        canvas.Draw(Pens.Solid(options.FingerBorderColor, 2f), ellipse);
    }

    private static void DrawDebugInfo(DrawingCanvas canvas, HumanSwipeTrace trace, RenderPoint point, int currentIndex, int totalPoints, FramePhase phase, Options options)
    {
        Font font = CreateFont(options);
        string text =
            $"Phase: {phase}    Step: {currentIndex + 1}/{totalPoints}\n" +
            $"X,Y: {point.SourceX:F1}, {point.SourceY:F1}    Time: {point.TimeMs:F1}ms    Delay: {point.DelayMs}ms\n" +
            $"Velocity: {point.VelocityPxPerSecond:F0}px/s    Force: {point.Force:F3}    Radius: {point.RadiusX:F1}x{point.RadiusY:F1}\n" +
            $"Rotation: {point.RotationAngle:F1}°    Duration: {trace.DurationMs:F0}ms    ReleaseV: {trace.ReleaseVelocityPxPerSecond:F0}px/s";

        var textOptions = new RichTextOptions(font)
        {
            Origin = new PointF(options.Padding, 12),
            WrappingLength = options.Width - options.Padding * 2
        };

        canvas.DrawText(textOptions, text, Brushes.Solid(options.TextColor), pen: null);
    }

    private static Font CreateFont(Options options)
    {
        try
        {
            return SystemFonts.CreateFont(options.FontFamilyName, options.FontSize);
        }
        catch
        {
            FontFamily family = SystemFonts.Collection.Families.FirstOrDefault();
            if (family == default)
                throw new InvalidOperationException("当前系统没有可用字体；可以把 ShowDebugInfo=false 后继续生成 GIF。");
            return family.CreateFont(options.FontSize);
        }
    }

    private static ViewTransform CreateTransform(HumanSwipeTrace trace, List<RenderPoint> points, Options options)
    {
        double minX = points.Select(p => p.SourceX).Append(trace.StartX).Append(trace.EndX).Append(trace.PlannedEndX).Min();
        double maxX = points.Select(p => p.SourceX).Append(trace.StartX).Append(trace.EndX).Append(trace.PlannedEndX).Max();
        double minY = points.Select(p => p.SourceY).Append(trace.StartY).Append(trace.EndY).Append(trace.PlannedEndY).Min();
        double maxY = points.Select(p => p.SourceY).Append(trace.StartY).Append(trace.EndY).Append(trace.PlannedEndY).Max();

        double sourceWidth = Math.Max(1.0, maxX - minX);
        double sourceHeight = Math.Max(1.0, maxY - minY);
        double top = options.InfoAreaHeight + options.Padding;
        double usableWidth = Math.Max(1.0, options.Width - options.Padding * 2.0);
        double usableHeight = Math.Max(1.0, options.Height - top - options.Padding);

        if (!options.AutoFit)
            return new ViewTransform(0, 0, 1.0, options.Padding, top);

        double scale = Math.Min(usableWidth / sourceWidth, usableHeight / sourceHeight);
        scale = Math.Min(scale, options.MaxAutoFitScale);

        double offsetX = options.Padding + (usableWidth - sourceWidth * scale) / 2.0;
        double offsetY = top + (usableHeight - sourceHeight * scale) / 2.0;

        return new ViewTransform(minX, minY, scale, offsetX, offsetY);
    }

    private static void SetFrameDelay(ImageFrame<Rgba32> frame, int delayMs, Options options)
    {
        int ms = Math.Max(options.MinimumFrameDelayMs, delayMs);
        int hundredths = Math.Max(1, (int)Math.Round(ms / 10.0, MidpointRounding.AwayFromZero));
        frame.Metadata.GetGifMetadata().FrameDelay = hundredths;
    }

    private static void AppendFrame(ref Image<Rgba32>? gif, Image<Rgba32> frame)
    {
        if (gif == null)
        {
            gif = frame.Clone();
            return;
        }
        gif.Frames.AddFrame(frame.Frames.RootFrame);
    }

    private enum FramePhase
    {
        StartHold,
        Moving,
        EndHold
    }
}

