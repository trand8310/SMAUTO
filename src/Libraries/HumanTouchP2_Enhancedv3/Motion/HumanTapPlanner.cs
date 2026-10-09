using System;
using System.Collections.Generic;

namespace PlaywrightHumanInput;

public sealed class HumanTapPlan
{
    public int HoldMs { get; init; }
    public IReadOnlyList<TouchSample> Samples { get; init; } = Array.Empty<TouchSample>();
    public GesturePlan Gesture { get; init; } = new();
}

public sealed class HumanTapTrace
{
    public double X { get; init; }
    public double Y { get; init; }
    public int PlannedHoldMs { get; init; }
    public double ActualDurationMs { get; init; }
    public double PeakForce { get; init; }
    public double RadiusX { get; init; }
    public double RadiusY { get; init; }
}

/// <summary>One stationary contact, with a gradual pressure/area envelope.</summary>
public sealed class HumanTapPlanner
{
    public HumanTapPlan Plan(HumanTouchSession session, double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        session.RecoverToNow();
        var user = session.UserProfile;
        var device = session.DeviceProfile;
        var random = session.Random;
        double median = 75 * Math.Clamp(user.ReactionBias, .6, 1.6)
            / Math.Clamp(user.SpeedBias, .7, 1.4) * (1 + session.Fatigue * .25);
        int hold = (int)Math.Round(RandomMath.LogNormal(random, median, .16, 45, 160));
        double force = Math.Clamp(.58 * user.ForceBias * device.ForceScale * session.NextForceDrift()
            * RandomMath.TruncatedNormal(random, 1, .035, .9, 1.1), .15, .95);
        double radius = Math.Clamp(user.PreferredTouchRadiusPx * user.TouchAreaBias * device.RadiusScale, 2, 8);
        double rotation = Math.Clamp(user.PreferredRotationDeg * device.RotationScale, 0, 180);
        var point = new PointD(x, y);
        TouchSample Sample(double time, double pressure, double area) => new()
        {
            TimeMs = time, Point = point, Force = force * pressure,
            RadiusX = radius * area, RadiusY = radius * 1.08 * area, RotationAngle = rotation
        };
        int peakAt = (int)Math.Round(hold * .4), releaseAt = (int)Math.Round(hold * .85);
        return new HumanTapPlan
        {
            HoldMs = hold,
            Samples = new[] { Sample(0, .65, .90), Sample(peakAt, 1, 1), Sample(releaseAt, .55, .88) },
            Gesture = new GesturePlan { Start = point, End = point, DurationMs = hold, EndHoldMs = hold - releaseAt }
        };
    }
}
