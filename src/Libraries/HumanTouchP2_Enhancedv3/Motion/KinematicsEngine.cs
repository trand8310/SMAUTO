using System;
using System.Collections.Generic;

namespace PlaywrightHumanInput
{
    public sealed class KinematicsEngine
    {
        public List<(double timeMs, PointD point, double velocity)> GenerateBaseTrajectory(
            HumanTouchSession session,
            GesturePlan plan)
        {
            var random = session.Random;
            var device = session.DeviceProfile;

            double hz = RandomMath.NextDouble(
                random,
                device.MinSamplingHz,
                device.MaxSamplingHz);

            double nominalInterval = 1000.0 / Math.Max(30, hz);
            var times = BuildSampleTimes(
                random,
                device,
                plan.DurationMs,
                nominalInterval,
                plan.RequestedStepsHint);

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

            // 末端回拉作为独立短动作，不破坏主轨迹速度模型。
            if (plan.HasPullBack && output.Count > 2)
            {
                var last = output[^1];
                var pull = PullBack(plan, last.point, plan.PullBackPx);
                double t = last.timeMs + RandomMath.NextDouble(random, 22, 46);
                double v = Distance(last.point, pull) /
                           Math.Max(0.001, (t - last.timeMs) / 1000.0);
                output.Add((t, pull, v));
            }

            return output;
        }

        private static List<double> BuildSampleTimes(
            Random r,
            TouchDeviceProfile device,
            double durationMs,
            double nominalInterval,
            int requestedSteps)
        {
            var times = new List<double> { 0 };

            if (requestedSteps > 5)
            {
                nominalInterval = Math.Clamp(
                    durationMs / requestedSteps,
                    4.0,
                    28.0);
            }

            // 采样周期抖动使用相关过程，而不是每一帧独立白噪声。
            double jitterState = RandomMath.Normal(
                r,
                0,
                nominalInterval * device.SamplingJitterRatio * 0.35);

            double t = 0;
            while (t < durationMs)
            {
                double innovation = RandomMath.Normal(
                    r,
                    0,
                    nominalInterval * device.SamplingJitterRatio);

                double rho = Math.Clamp(device.SamplingAutocorrelation, 0.0, 0.95);
                double innovationScale = Math.Sqrt(Math.Max(0.01, 1.0 - rho * rho));
                jitterState = rho * jitterState + innovationScale * innovation;

                double interval = Math.Max(
                    3.5,
                    nominalInterval +
                    jitterState +
                    RandomMath.Normal(r, 0, device.TimingNoiseMs));

                if (RandomMath.Chance(r, device.CoalescedSampleChance))
                {
                    interval *= RandomMath.NextDouble(r, 1.65, 2.15);
                }

                t += interval;
                if (t >= durationMs)
                    break;

                times.Add(t);
            }

            times.Add(durationMs);
            return times;
        }

        private static List<double> BuildProgressCurve(
            GesturePlan plan,
            List<double> times)
        {
            int n = times.Count;
            var velocityWeights = new double[n];
            var cumulative = new double[n];

            double duration = Math.Max(1, plan.DurationMs);
            double distance = Math.Max(1, plan.DistancePx);

            // 目标抬手速度换算为归一化速度权重。
            double desiredTerminalDerivative =
                plan.ReleaseVelocityPxPerSecond *
                (duration / 1000.0) /
                distance;

            for (int i = 0; i < n; i++)
            {
                double t = Math.Clamp(times[i] / duration, 0, 1);
                double warped = RandomMath.WarpAroundPeak(t, plan.MotionPeakRatio);

                double baseVelocity = RandomMath.MinimumJerkDerivative(warped);

                // 不同意图拥有不同的“基础运动风格”，但仍使用同一套连续运动学。
                if (plan.Mode == HumanSwipeMode.Reading)
                {
                    baseVelocity = Math.Max(0.01, baseVelocity * 0.94);
                }
                else if (plan.Mode == HumanSwipeMode.Preview)
                {
                    baseVelocity = Math.Max(0.01, baseVelocity * 1.02);
                }
                else if (plan.Mode == HumanSwipeMode.Micro)
                {
                    baseVelocity = Math.Max(0.008, baseVelocity * 0.90);
                }

                // Fling 需要在抬手时保留明显非零速度。
                if (plan.Mode == HumanSwipeMode.Fling)
                {
                    double releaseBlend = Math.Clamp(
                        desiredTerminalDerivative / 2.0,
                        0.18,
                        0.82);

                    double terminalRamp = 2.0 * Math.Pow(t, 1.45);
                    baseVelocity =
                        (1.0 - releaseBlend) * baseVelocity +
                        releaseBlend * terminalRamp;
                }
                else if (desiredTerminalDerivative > 0.02)
                {
                    // Preview/Reading 也允许非常小的末端残余速度，不必强制每次精确停到 0。
                    double terminalBlend = Math.Clamp(
                        desiredTerminalDerivative / 2.0,
                        0.0,
                        0.10);
                    baseVelocity += terminalBlend *
                                    RandomMath.SmoothStep(0.68, 1.0, t) *
                                    1.5;
                }

                // 第二个次级运动单元模拟一次轻微再加速/纠正。
                if (plan.HasSecondarySubmovement)
                {
                    double pulse = RandomMath.Gaussian(
                        t,
                        plan.SecondarySubmovementCenter,
                        plan.SecondarySubmovementWidth);

                    baseVelocity +=
                        plan.SecondarySubmovementStrength *
                        1.875 *
                        pulse;
                }

                if (plan.HasHesitation)
                {
                    double z = (t - plan.HesitationAt) /
                               Math.Max(0.015, plan.HesitationWidth);

                    double slowdown =
                        1.0 -
                        plan.HesitationDepth *
                        Math.Exp(-0.5 * z * z);

                    baseVelocity *= Math.Max(0.035, slowdown);
                }

                velocityWeights[i] = Math.Max(0.001, baseVelocity);
            }

            cumulative[0] = 0;
            for (int i = 1; i < n; i++)
            {
                double dt = Math.Max(
                    0.001,
                    (times[i] - times[i - 1]) / duration);

                cumulative[i] = cumulative[i - 1] +
                                0.5 *
                                (velocityWeights[i - 1] + velocityWeights[i]) *
                                dt;
            }

            double total = Math.Max(1e-9, cumulative[^1]);
            var result = new List<double>(n);

            for (int i = 0; i < n; i++)
            {
                result.Add(Math.Clamp(cumulative[i] / total, 0, 1));
            }

            result[^1] = 1.0;
            return result;
        }

        private static PointD ApplyGeometricCurve(
            GesturePlan plan,
            PointD point,
            double progress)
        {
            double dx = plan.End.X - plan.Start.X;
            double dy = plan.End.Y - plan.Start.Y;
            double distance = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));

            double nx = -dy / distance;
            double ny = dx / distance;

            // 不再固定使用 sin(pi*t)；峰值位置、曲线左右不对称和二次谐波都按手势改变。
            double envelope = RandomMath.SkewedEnvelope(
                progress,
                plan.CurvePeakRatio,
                4.2);

            double harmonic = 1.0 +
                              plan.CurveSecondHarmonic *
                              Math.Sin(2.0 * Math.PI * progress);

            double offset = plan.CurveAmountPx *
                            envelope *
                            harmonic *
                            plan.CurveSide;

            return new PointD(
                point.X + nx * offset,
                point.Y + ny * offset);
        }

        private static PointD PullBack(
            GesturePlan plan,
            PointD point,
            double px)
            => plan.Direction switch
            {
                HumanSwipeDirection.Up => new PointD(point.X, point.Y + px),
                HumanSwipeDirection.Down => new PointD(point.X, point.Y - px),
                HumanSwipeDirection.Left => new PointD(point.X + px, point.Y),
                HumanSwipeDirection.Right => new PointD(point.X - px, point.Y),
                _ => point
            };

        private static double Distance(PointD a, PointD b)
            => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
    }
}
