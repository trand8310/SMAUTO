using System;
using System.Collections.Generic;

namespace PlaywrightHumanInput
{
    public sealed class KinematicsEngine
    {
        public List<(double timeMs, PointD point, double velocity)> GenerateBaseTrajectory(HumanTouchSession session, GesturePlan plan)
        {
            var random = session.Random;
            var device = session.DeviceProfile;
            double hz = RandomMath.NextDouble(random, device.MinSamplingHz, device.MaxSamplingHz);
            double nominalInterval = 1000.0 / Math.Max(30, hz);
            var times = BuildSampleTimes(random, device, plan.DurationMs, nominalInterval, plan.RequestedStepsHint);

            var progresses = BuildProgressCurve(plan, times);
            var output = new List<(double timeMs, PointD point, double velocity)>(times.Count);
            PointD previous = plan.Start;
            double previousTime = 0;

            for (int i = 0; i < times.Count; i++)
            {
                double p = progresses[i];
                PointD basePoint = PointD.Lerp(plan.Start, plan.End, p);
                PointD curved = ApplyGeometricCurve(plan, basePoint, p);
                double dt = Math.Max(0.5, times[i] - previousTime) / 1000.0;
                double velocity = i == 0 ? 0 : Distance(previous, curved) / dt;
                output.Add((times[i], curved, velocity));
                previous = curved;
                previousTime = times[i];
            }

            // 微调/阅读偶尔出现很短的末端回拉，它是一个独立的小动作，而不是改变整条主曲线。
            if (plan.HasPullBack && output.Count > 2)
            {
                var last = output[^1];
                var pull = PullBack(plan, last.point, plan.PullBackPx);
                double t = last.timeMs + RandomMath.NextDouble(random, 22, 46);
                double v = Distance(last.point, pull) / Math.Max(0.001, (t - last.timeMs) / 1000.0);
                output.Add((t, pull, v));
            }

            return output;
        }

        private static List<double> BuildSampleTimes(Random r, TouchDeviceProfile device, double durationMs, double nominalInterval, int requestedSteps)
        {
            var times = new List<double> { 0 };
            if (requestedSteps > 5)
                nominalInterval = Math.Clamp(durationMs / requestedSteps, 4.0, 28.0);

            double t = 0;
            while (t < durationMs)
            {
                double jitter = RandomMath.Normal(r, 0, nominalInterval * device.SamplingJitterRatio);
                double interval = Math.Max(3.5, nominalInterval + jitter + RandomMath.Normal(r, 0, device.TimingNoiseMs));
                if (RandomMath.Chance(r, device.CoalescedSampleChance))
                    interval *= RandomMath.NextDouble(r, 1.65, 2.15);
                t += interval;
                if (t >= durationMs) break;
                times.Add(t);
            }
            times.Add(durationMs);
            return times;
        }

        private static List<double> BuildProgressCurve(GesturePlan plan, List<double> times)
        {
            int n = times.Count;
            var velocityWeights = new double[n];
            var cumulative = new double[n];
            double duration = Math.Max(1, plan.DurationMs);
            double distance = Math.Max(1, plan.DistancePx);

            // 对 Fling，混合 minimum-jerk 与 t^2，使起步仍平滑，但抬手时保留非零速度。
            double releaseBlend = 0;
            if (plan.Mode == HumanSwipeMode.Fling)
            {
                double desiredNormalizedDerivative = plan.ReleaseVelocityPxPerSecond * (duration / 1000.0) / distance;
                releaseBlend = Math.Clamp(desiredNormalizedDerivative / 2.0, 0.18, 0.78);
            }

            for (int i = 0; i < n; i++)
            {
                double t = Math.Clamp(times[i] / duration, 0, 1);
                double baseVelocity;
                if (plan.Mode == HumanSwipeMode.Fling)
                    baseVelocity = (1 - releaseBlend) * RandomMath.MinimumJerkDerivative(t) + releaseBlend * 2.0 * t;
                else if (plan.Mode == HumanSwipeMode.Reading)
                    baseVelocity = Math.Max(0.02, RandomMath.MinimumJerkDerivative(Math.Pow(t, 0.88)) * 0.9 + 0.1);
                else
                    baseVelocity = Math.Max(0.02, RandomMath.MinimumJerkDerivative(t) + (plan.Mode == HumanSwipeMode.Preview ? 0.08 : 0.02));

                if (plan.HasHesitation)
                {
                    double z = (t - plan.HesitationAt) / Math.Max(0.015, plan.HesitationWidth);
                    double slowdown = 1.0 - plan.HesitationDepth * Math.Exp(-0.5 * z * z);
                    baseVelocity *= Math.Max(0.035, slowdown);
                }

                velocityWeights[i] = Math.Max(0.001, baseVelocity);
            }

            cumulative[0] = 0;
            for (int i = 1; i < n; i++)
            {
                double dt = Math.Max(0.001, (times[i] - times[i - 1]) / duration);
                cumulative[i] = cumulative[i - 1] + 0.5 * (velocityWeights[i - 1] + velocityWeights[i]) * dt;
            }

            double total = Math.Max(1e-9, cumulative[^1]);
            var result = new List<double>(n);
            for (int i = 0; i < n; i++)
                result.Add(Math.Clamp(cumulative[i] / total, 0, 1));
            result[^1] = 1.0;
            return result;
        }

        private static PointD ApplyGeometricCurve(GesturePlan plan, PointD point, double progress)
        {
            double dx = plan.End.X - plan.Start.X;
            double dy = plan.End.Y - plan.Start.Y;
            double distance = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            double nx = -dy / distance;
            double ny = dx / distance;

            // 非对称低频弯曲：中段最明显，末段有自然回正。
            double envelope = Math.Sin(Math.PI * progress);
            double asymmetry = 0.82 + 0.18 * (1.0 - progress);
            double offset = plan.CurveAmountPx * envelope * asymmetry * plan.CurveSide;
            return new PointD(point.X + nx * offset, point.Y + ny * offset);
        }

        private static PointD PullBack(GesturePlan plan, PointD point, double px) => plan.Direction switch
        {
            HumanSwipeDirection.Up => new PointD(point.X, point.Y + px),
            HumanSwipeDirection.Down => new PointD(point.X, point.Y - px),
            HumanSwipeDirection.Left => new PointD(point.X + px, point.Y),
            HumanSwipeDirection.Right => new PointD(point.X - px, point.Y),
            _ => point
        };

        private static double Distance(PointD a, PointD b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
    }
}
