using System;
using System.Collections.Generic;

namespace PlaywrightHumanInput
{
    public enum HumanSwipeDirection
    {
        Up,
        Down,
        Left,
        Right,
        RandomVertical,
        RandomAny
    }

    public enum HumanSwipeMode
    {
        Reading,
        Preview,
        Fling,
        Micro
    }

    public enum SwipeIntent
    {
        Reading,
        Preview,
        Fling,
        MicroAdjust,
        BackReview,
        FastScan
    }

    public enum FlingStrength
    {
        Soft,
        Normal,
        Strong,
        VeryStrong
    }

    public enum HumanHandedness
    {
        Left,
        Right
    }

    public enum BrowseBehaviorState
    {
        Observe,
        Read,
        Preview,
        FastScan,
        BackReview,
        Idle
    }

    public sealed class HumanSwipeTracePoint
    {
        public double X { get; init; }
        public double Y { get; init; }
        public int DelayMs { get; init; }
        public double RadiusX { get; init; }
        public double RadiusY { get; init; }
        public double Force { get; init; }
        public double RotationAngle { get; init; }
        public double TimeMs { get; init; }
        public double VelocityPxPerSecond { get; init; }
    }

    public sealed class HumanSwipeTrace
    {
        public double StartX { get; init; }
        public double StartY { get; init; }
        public double EndX { get; init; }
        public double EndY { get; init; }
        public HumanSwipeDirection Direction { get; init; }
        public HumanSwipeMode Mode { get; init; }
        public SwipeIntent Intent { get; init; }
        public int Steps { get; init; }
        public int TotalDelayMs { get; init; }
        public bool ScrollChanged { get; init; }
        public double DurationMs { get; init; }
        public double ReleaseVelocityPxPerSecond { get; init; }
        public List<HumanSwipeTracePoint> Points { get; init; } = new();
    }

    public sealed class ElementRect
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double Left => X;
        public double Right => X + Width;
        public double Top => Y;
        public double Bottom => Y + Height;
        public double CenterX => X + Width / 2.0;
        public double CenterY => Y + Height / 2.0;
    }

    public sealed class ScrollTargetState
    {
        public string Kind { get; set; } = "document";
        public string Key { get; set; } = "document";
        public double ScrollLeft { get; set; }
        public double ScrollTop { get; set; }
        public double ScrollWidth { get; set; }
        public double ScrollHeight { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public bool CanScrollVertically => ScrollHeight > ClientHeight + 2;
        public bool CanScrollHorizontally => ScrollWidth > ClientWidth + 2;
        public bool IsNearTop => ScrollTop <= 6;
        public bool IsNearBottom => ScrollTop + ClientHeight >= ScrollHeight - 6;
        public bool IsNearLeft => ScrollLeft <= 6;
        public bool IsNearRight => ScrollLeft + ClientWidth >= ScrollWidth - 6;
    }

    public readonly struct PointD
    {
        public PointD(double x, double y)
        {
            X = x;
            Y = y;
        }
        public double X { get; }
        public double Y { get; }
        public static PointD Lerp(PointD a, PointD b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    public sealed class TouchSample
    {
        public double TimeMs { get; init; }
        public PointD Point { get; init; }
        public double RadiusX { get; init; }
        public double RadiusY { get; init; }
        public double Force { get; init; }
        public double RotationAngle { get; init; }
        public double VelocityPxPerSecond { get; init; }
    }

    public sealed class GesturePlan
    {
        public SwipeIntent Intent { get; init; }
        public HumanSwipeMode Mode { get; init; }
        public HumanSwipeDirection Direction { get; init; }
        public PointD Start { get; init; }
        public PointD End { get; init; }
        public double DurationMs { get; init; }
        public double ReleaseVelocityPxPerSecond { get; init; }
        public double CurveAmountPx { get; init; }
        public double CurveSide { get; init; }
        public bool HasHesitation { get; init; }
        public double HesitationAt { get; init; }
        public double HesitationWidth { get; init; }
        public double HesitationDepth { get; init; }
        public bool HasPullBack { get; init; }
        public double PullBackPx { get; init; }
        public int StartHoldMs { get; init; }
        public int EndHoldMs { get; init; }
        public int RequestedStepsHint { get; init; }
        public double DistancePx => Math.Sqrt(Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2));
    }

    public sealed class HumanTouchRequest
    {
        public HumanSwipeDirection Direction { get; set; } = HumanSwipeDirection.Up;
        public SwipeIntent Intent { get; set; } = SwipeIntent.Preview;
        public FlingStrength FlingStrength { get; set; } = FlingStrength.Normal;
        public double SpeedFactor { get; set; } = 1.0;
        public int? DistancePx { get; set; }
        public int? Steps { get; set; }
        public int? StartX { get; set; }
        public int? StartY { get; set; }
        public int? EndX { get; set; }
        public int? EndY { get; set; }
        public int SafeMargin { get; set; } = 24;
        public bool CheckScrollableBeforeSwipe { get; set; } = true;
        public bool VerifyScrollChanged { get; set; } = true;
        public double ScrollChangedMinDelta { get; set; } = 8;
        public bool EnableHesitation { get; set; } = true;
        public double? HesitationChance { get; set; }
        public bool EnablePullBack { get; set; } = true;
        public double? PullBackChance { get; set; }
        public bool HoldBeforeMove { get; set; } = true;
        public bool? HoldBeforeEnd { get; set; }
        public Action<string>? Log { get; set; }
    }
}
