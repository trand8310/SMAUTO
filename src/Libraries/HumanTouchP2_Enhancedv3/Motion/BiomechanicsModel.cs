using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaywrightHumanInput
{
    public sealed class BiomechanicsModel
    {
        public List<TouchSample> Apply(
            HumanTouchSession session,
            GesturePlan plan,
            IReadOnlyList<(double timeMs, PointD point, double velocity)> baseTrajectory)
        {
            var r = session.Random;
            var user = session.UserProfile;
            var device = session.DeviceProfile;
            var samples = new List<TouchSample>(baseTrajectory.Count);

            if (baseTrajectory.Count == 0)
                return samples;

            double peakVelocity = Math.Max(
                1,
                baseTrajectory.Max(x => x.velocity));

            // 两个时间尺度的相关微扰：慢漂移 + 较快微颤。
            double slowX = RandomMath.Normal(r, 0, 0.10);
            double slowY = RandomMath.Normal(r, 0, 0.10);
            double tremorX = RandomMath.Normal(r, 0, 0.08);
            double tremorY = RandomMath.Normal(r, 0, 0.08);

            double pressureNoise = RandomMath.Normal(r, 0, 0.010);
            double radiusNoiseX = RandomMath.Normal(r, 0, 0.06);
            double radiusNoiseY = RandomMath.Normal(r, 0, 0.06);
            double rotationNoise = RandomMath.Normal(r, 0, 0.8);

            double lateralSeed =
                0.72 * session.LastLateralOffsetPx +
                RandomMath.Normal(r, 0, 1.2);

            double forceSessionScale = session.NextForceDrift();

            double baseRadius =
                user.PreferredTouchRadiusPx *
                user.TouchAreaBias *
                device.RadiusScale *
                RandomMath.NextDouble(r, 0.92, 1.08);

            double aspect = RandomMath.NextDouble(r, 0.88, 1.14);
            double lastTimeMs = 0;

            for (int i = 0; i < baseTrajectory.Count; i++)
            {
                var src = baseTrajectory[i];
                double t = baseTrajectory.Count <= 1
                    ? 1
                    : i / (double)(baseTrajectory.Count - 1);

                double dtMs = i == 0
                    ? 12
                    : Math.Max(1, src.timeMs - lastTimeMs);
                lastTimeMs = src.timeMs;

                // 实际时间间隔决定相关过程的衰减，避免 60Hz/120Hz 下频谱完全不同。
                UpdateOuProcess(
                    r,
                    ref slowX,
                    dtMs,
                    tauMs: 95,
                    sigma: 0.20 * user.DriftBias);
                UpdateOuProcess(
                    r,
                    ref slowY,
                    dtMs,
                    tauMs: 95,
                    sigma: 0.20 * user.DriftBias);
                UpdateOuProcess(
                    r,
                    ref tremorX,
                    dtMs,
                    tauMs: 34,
                    sigma: 0.24 * user.TremorBias);
                UpdateOuProcess(
                    r,
                    ref tremorY,
                    dtMs,
                    tauMs: 34,
                    sigma: 0.24 * user.TremorBias);

                double envelope = RandomMath.SkewedEnvelope(
                    t,
                    Math.Clamp(plan.CurvePeakRatio, 0.32, 0.72),
                    3.4);

                double lowDrift =
                    (lateralSeed * 0.48 +
                     slowX * 0.65 +
                     plan.CurveSide * plan.DistancePx * 0.0045 * user.DriftBias) *
                    envelope;

                double x = src.point.X;
                double y = src.point.Y;

                if (plan.Direction is HumanSwipeDirection.Up or HumanSwipeDirection.Down)
                {
                    x += lowDrift + tremorX * envelope;
                    y += (slowY * 0.18 + tremorY * 0.40) * envelope;
                }
                else
                {
                    y += lowDrift + tremorY * envelope;
                    x += (slowX * 0.18 + tremorX * 0.40) * envelope;
                }

                double velocityNorm = Math.Clamp(src.velocity / peakVelocity, 0, 1.5);

                double force = 0;
                if (device.SupportsForce)
                {
                    double pressIn = RandomMath.SmoothStep(0.00, 0.12, t);
                    double liftOut = 1.0 - RandomMath.SmoothStep(
                        plan.Mode == HumanSwipeMode.Fling ? 0.84 : 0.76,
                        1.0,
                        t);

                    double body = Math.Min(pressIn, liftOut);

                    pressureNoise = UpdateCorrelatedNoise(
                        r,
                        pressureNoise,
                        dtMs,
                        tauMs: 70,
                        sigma: 0.018);

                    double min = plan.Mode == HumanSwipeMode.Fling ? 0.46 : 0.42;
                    double max = plan.Mode == HumanSwipeMode.Reading ? 0.82 : 0.86;

                    // 快速移动阶段压力通常略小，避免 pressure 与运动完全无关。
                    double speedRelief = 1.0 - Math.Clamp(velocityNorm * 0.055, 0, 0.075);

                    force = Math.Clamp(
                        (min + (max - min) * body + pressureNoise) *
                        speedRelief *
                        user.ForceBias *
                        forceSessionScale *
                        device.ForceScale,
                        0.05,
                        1.0);
                }

                double rx = 1;
                double ry = 1;

                if (device.SupportsTouchArea)
                {
                    radiusNoiseX = UpdateCorrelatedNoise(
                        r,
                        radiusNoiseX,
                        dtMs,
                        tauMs: 85,
                        sigma: 0.08);

                    radiusNoiseY = UpdateCorrelatedNoise(
                        r,
                        radiusNoiseY,
                        dtMs,
                        tauMs: 85,
                        sigma: 0.08);

                    double pressureExpansion = device.SupportsForce
                        ? (force - 0.55) * 0.78
                        : 0;

                    double liftShrink =
                        1.0 -
                        0.18 * RandomMath.SmoothStep(0.82, 1.0, t);

                    rx = Math.Clamp(
                        (baseRadius + pressureExpansion + radiusNoiseX) * liftShrink,
                        1.8,
                        7.2);

                    ry = Math.Clamp(
                        (baseRadius * aspect + pressureExpansion + radiusNoiseY) * liftShrink,
                        1.8,
                        7.5);
                }

                double rotation = 0;
                if (device.SupportsRotationAngle)
                {
                    rotationNoise = UpdateCorrelatedNoise(
                        r,
                        rotationNoise,
                        dtMs,
                        tauMs: 130,
                        sigma: 1.35);

                    double rotationDrift =
                        RandomMath.SkewedEnvelope(t, 0.55, 3.6) *
                        plan.CurveSide *
                        2.2;

                    rotation = NormalizeRotation(
                        (user.PreferredRotationDeg + rotationNoise + rotationDrift) *
                        device.RotationScale);
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

        private static void UpdateOuProcess(
            Random random,
            ref double state,
            double dtMs,
            double tauMs,
            double sigma)
        {
            state = UpdateCorrelatedNoise(
                random,
                state,
                dtMs,
                tauMs,
                sigma);
        }

        private static double UpdateCorrelatedNoise(
            Random random,
            double state,
            double dtMs,
            double tauMs,
            double sigma)
        {
            double alpha = Math.Exp(-Math.Max(0.1, dtMs) / Math.Max(1.0, tauMs));
            double innovationScale = Math.Sqrt(Math.Max(0.001, 1.0 - alpha * alpha));
            return alpha * state +
                   innovationScale * RandomMath.Normal(random, 0, sigma);
        }

        private static double NormalizeRotation(double degrees)
        {
            degrees %= 180.0;
            if (degrees < 0)
                degrees += 180.0;
            return degrees;
        }
    }
}
