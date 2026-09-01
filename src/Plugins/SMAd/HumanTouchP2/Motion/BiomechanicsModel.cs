using System;
using System.Collections.Generic;

namespace PlaywrightHumanInput
{
    public sealed class BiomechanicsModel
    {
        public List<TouchSample> Apply(HumanTouchSession session, GesturePlan plan, IReadOnlyList<(double timeMs, PointD point, double velocity)> baseTrajectory)
        {
            var r = session.Random;
            var user = session.UserProfile;
            var device = session.DeviceProfile;
            var samples = new List<TouchSample>(baseTrajectory.Count);

            double tremorX = RandomMath.Normal(r, 0, 0.12);
            double tremorY = RandomMath.Normal(r, 0, 0.12);
            double pressureNoise = RandomMath.Normal(r, 0, 0.012);
            double radiusNoiseX = RandomMath.Normal(r, 0, 0.08);
            double radiusNoiseY = RandomMath.Normal(r, 0, 0.08);
            double rotationNoise = RandomMath.Normal(r, 0, 1.2);
            double lateralSeed = 0.72 * session.LastLateralOffsetPx + RandomMath.Normal(r, 0, 1.2);
            double baseRadius = user.PreferredTouchRadiusPx * user.TouchAreaBias * device.RadiusScale * RandomMath.NextDouble(r, 0.92, 1.08);
            double aspect = RandomMath.NextDouble(r, 0.88, 1.14);

            for (int i = 0; i < baseTrajectory.Count; i++)
            {
                var src = baseTrajectory[i];
                double t = baseTrajectory.Count <= 1 ? 1 : i / (double)(baseTrajectory.Count - 1);
                double envelope = Math.Sin(Math.PI * Math.Clamp(t, 0, 1));

                // 低频副轴漂移与高频微颤分开建模，避免每点独立白噪声。
                double lowDrift = (lateralSeed * 0.55 + plan.CurveSide * plan.DistancePx * 0.006 * user.DriftBias) * envelope;
                tremorX = tremorX * 0.78 + RandomMath.Normal(r, 0, 0.24 * user.TremorBias) * 0.22;
                tremorY = tremorY * 0.78 + RandomMath.Normal(r, 0, 0.24 * user.TremorBias) * 0.22;

                double x = src.point.X;
                double y = src.point.Y;
                if (plan.Direction is HumanSwipeDirection.Up or HumanSwipeDirection.Down)
                {
                    x += lowDrift + tremorX * envelope;
                    y += tremorY * 0.42 * envelope;
                }
                else
                {
                    y += lowDrift + tremorY * envelope;
                    x += tremorX * 0.42 * envelope;
                }

                double force = 0;
                if (device.SupportsForce)
                {
                    double pressIn = RandomMath.SmoothStep(0.00, 0.12, t);
                    double liftOut = 1.0 - RandomMath.SmoothStep(plan.Mode == HumanSwipeMode.Fling ? 0.84 : 0.76, 1.0, t);
                    double body = Math.Min(pressIn, liftOut);
                    pressureNoise = pressureNoise * 0.86 + RandomMath.Normal(r, 0, 0.015) * 0.14;
                    double min = plan.Mode == HumanSwipeMode.Fling ? 0.46 : 0.42;
                    double max = plan.Mode == HumanSwipeMode.Reading ? 0.82 : 0.86;
                    force = Math.Clamp((min + (max - min) * body + pressureNoise) * user.ForceBias * device.ForceScale, 0.05, 1.0);
                }

                double rx = 1;
                double ry = 1;
                if (device.SupportsTouchArea)
                {
                    radiusNoiseX = radiusNoiseX * 0.82 + RandomMath.Normal(r, 0, 0.06) * 0.18;
                    radiusNoiseY = radiusNoiseY * 0.82 + RandomMath.Normal(r, 0, 0.06) * 0.18;
                    double pressureExpansion = device.SupportsForce ? (force - 0.55) * 0.75 : 0;
                    double liftShrink = 1.0 - 0.18 * RandomMath.SmoothStep(0.82, 1.0, t);
                    rx = Math.Clamp((baseRadius + pressureExpansion + radiusNoiseX) * liftShrink, 1.8, 7.2);
                    ry = Math.Clamp((baseRadius * aspect + pressureExpansion + radiusNoiseY) * liftShrink, 1.8, 7.5);
                }

                double rotation = 0;
                if (device.SupportsRotationAngle)
                {
                    rotationNoise = rotationNoise * 0.88 + RandomMath.Normal(r, 0, 1.0) * 0.12;
                    double rotationDrift = Math.Sin(Math.PI * t) * plan.CurveSide * 2.2;
                    rotation = NormalizeRotation((user.PreferredRotationDeg + rotationNoise + rotationDrift) * device.RotationScale);
                }

                samples.Add(new TouchSample
                {
                    TimeMs = src.timeMs,
                    Point = new PointD(x, y),
                    RadiusX = rx,
                    RadiusY = ry,
                    Force = force,
                    RotationAngle = rotation,
                    VelocityPxPerSecond = src.velocity
                });
            }

            if (samples.Count > 1)
            {
                double lateral = plan.Direction is HumanSwipeDirection.Up or HumanSwipeDirection.Down
                    ? samples[^1].Point.X - plan.End.X
                    : samples[^1].Point.Y - plan.End.Y;
                session.LastLateralOffsetPx = Math.Clamp(lateral, -16, 16);
            }

            return samples;
        }

        private static double NormalizeRotation(double degrees)
        {
            degrees %= 180.0;
            if (degrees < 0) degrees += 180.0;
            return degrees;
        }
    }
}
