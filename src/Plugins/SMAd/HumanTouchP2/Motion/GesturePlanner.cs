using System;

namespace PlaywrightHumanInput
{
    public sealed class GesturePlanner
    {
        public GesturePlan Plan(HumanTouchSession session, int viewportWidth, int viewportHeight, HumanTouchRequest request)
        {
            session.RecoverToNow();
            var random = session.Random;
            var user = session.UserProfile;
            var direction = ResolveDirection(random, request.Direction);
            var intent = request.Intent;
            var mode = IntentToMode(intent);
            double fatigue = session.Fatigue;

            int safe = Math.Max(8, request.SafeMargin);
            double speedFactor = Math.Clamp(request.SpeedFactor * user.SpeedBias * (1.0 - fatigue * 0.15), 0.35, 3.0);
            double distanceBias = user.DistanceBias;

            var start = BuildStart(session, viewportWidth, viewportHeight, safe, direction, request);
            var end = BuildEnd(random, start, viewportWidth, viewportHeight, safe, direction, intent, request, distanceBias);

            double distance = Distance(start, end);
            double duration = ComputeDurationMs(random, intent, request.FlingStrength, distance, speedFactor, fatigue);
            double releaseVelocity = ComputeReleaseVelocity(random, intent, request.FlingStrength, distance, duration, speedFactor);

            double curveBase = intent switch
            {
                SwipeIntent.MicroAdjust => 0.012,
                SwipeIntent.Reading => 0.022,
                SwipeIntent.Preview => 0.038,
                SwipeIntent.Fling => 0.048,
                SwipeIntent.FastScan => 0.052,
                SwipeIntent.BackReview => 0.030,
                _ => 0.035
            };
            double curveAmount = distance * curveBase * user.CurveBias * RandomMath.NextDouble(random, 0.70, 1.30);
            double curveSide = user.Handedness == HumanHandedness.Right ? 1.0 : -1.0;
            if (RandomMath.Chance(random, 0.27)) curveSide *= -1;

            bool allowHesitation = request.EnableHesitation && intent != SwipeIntent.Fling && intent != SwipeIntent.FastScan;
            double hesitationChance = request.HesitationChance ?? intent switch
            {
                SwipeIntent.Reading => 0.11,
                SwipeIntent.MicroAdjust => 0.08,
                SwipeIntent.Preview => 0.035,
                SwipeIntent.BackReview => 0.06,
                _ => 0.025
            };
            hesitationChance *= user.HesitationBias * (1.0 + fatigue * 0.38);
            bool hasHesitation = allowHesitation && RandomMath.Chance(random, hesitationChance);

            double pullBackChance = request.PullBackChance ?? intent switch
            {
                SwipeIntent.MicroAdjust => 0.22,
                SwipeIntent.Reading => 0.11,
                SwipeIntent.Preview => 0.035,
                _ => 0.0
            };
            bool pullBack = request.EnablePullBack && intent != SwipeIntent.Fling && intent != SwipeIntent.FastScan && RandomMath.Chance(random, pullBackChance * user.PullBackBias);

            int startHold = request.HoldBeforeMove
                ? (int)Math.Round(RandomMath.LogNormal(random, intent == SwipeIntent.Reading ? 42 : 28, 0.35, 8, 120) * user.ReactionBias)
                : 0;

            bool holdEnd = request.HoldBeforeEnd ?? (intent == SwipeIntent.Reading || intent == SwipeIntent.MicroAdjust);
            int endHold = holdEnd
                ? (int)Math.Round(RandomMath.LogNormal(random, intent == SwipeIntent.Reading ? 58 : 28, 0.35, 6, 140) * user.PauseBias)
                : 0;

            return new GesturePlan
            {
                Intent = intent,
                Mode = mode,
                Direction = direction,
                Start = start,
                End = end,
                DurationMs = duration,
                ReleaseVelocityPxPerSecond = releaseVelocity,
                CurveAmountPx = curveAmount,
                CurveSide = curveSide,
                HasHesitation = hasHesitation,
                HesitationAt = hasHesitation ? RandomMath.NextDouble(random, 0.34, 0.72) : 0,
                HesitationWidth = hasHesitation ? RandomMath.NextDouble(random, 0.045, 0.09) : 0,
                HesitationDepth = hasHesitation ? RandomMath.NextDouble(random, 0.78, 0.94) : 0,
                HasPullBack = pullBack,
                PullBackPx = pullBack ? RandomMath.NextDouble(random, 2.0, intent == SwipeIntent.MicroAdjust ? 6.0 : 4.5) : 0,
                StartHoldMs = startHold,
                EndHoldMs = endHold,
                RequestedStepsHint = request.Steps ?? 0
            };
        }

        private static PointD BuildStart(HumanTouchSession session, int width, int height, int safe, HumanSwipeDirection direction, HumanTouchRequest request)
        {
            var r = session.Random;
            var user = session.UserProfile;
            double x;
            double y;
            if (direction == HumanSwipeDirection.Up || direction == HumanSwipeDirection.Down)
            {
                double centerX = session.NextPreferredVerticalXRatio();
                x = RandomMath.TruncatedNormal(r, width * centerX, width * user.StartPositionStdRatio, safe, width - safe);
                double meanY = direction == HumanSwipeDirection.Up ? height * 0.75 : height * 0.29;
                y = RandomMath.TruncatedNormal(r, meanY, height * 0.075, safe, height - safe);
            }
            else
            {
                double centerY = session.NextPreferredHorizontalYRatio();
                y = RandomMath.TruncatedNormal(r, height * centerY, height * user.StartPositionStdRatio, safe, height - safe);
                double meanX = direction == HumanSwipeDirection.Left ? width * 0.78 : width * 0.22;
                x = RandomMath.TruncatedNormal(r, meanX, width * 0.065, safe, width - safe);
            }

            if (request.StartX.HasValue) x = Math.Clamp(request.StartX.Value, safe, width - safe);
            if (request.StartY.HasValue) y = Math.Clamp(request.StartY.Value, safe, height - safe);
            return new PointD(x, y);
        }

        private static PointD BuildEnd(Random r, PointD start, int width, int height, int safe, HumanSwipeDirection direction, SwipeIntent intent, HumanTouchRequest request, double distanceBias)
        {
            double axisSize = direction is HumanSwipeDirection.Up or HumanSwipeDirection.Down ? height : width;
            double distance = request.DistancePx ?? (axisSize * DistanceRatio(r, intent, request.FlingStrength) * distanceBias);

            // Fling 的手指位移被限制在合理释放区；页面后续距离由 release velocity 触发的惯性决定。
            if (intent == SwipeIntent.Fling || intent == SwipeIntent.FastScan)
                distance = Math.Min(distance, axisSize * (request.FlingStrength == FlingStrength.VeryStrong ? 0.62 : 0.54));

            double cross = RandomMath.Normal(r, 0, Math.Max(1.2, axisSize * 0.006));
            double x = start.X;
            double y = start.Y;

            switch (direction)
            {
                case HumanSwipeDirection.Up:
                    y = Math.Max(safe, start.Y - distance);
                    x += cross;
                    break;
                case HumanSwipeDirection.Down:
                    y = Math.Min(height - safe, start.Y + distance);
                    x += cross;
                    break;
                case HumanSwipeDirection.Left:
                    x = Math.Max(safe, start.X - distance);
                    y += cross;
                    break;
                case HumanSwipeDirection.Right:
                    x = Math.Min(width - safe, start.X + distance);
                    y += cross;
                    break;
            }

            if (request.EndX.HasValue) x = Math.Clamp(request.EndX.Value, safe, width - safe);
            if (request.EndY.HasValue) y = Math.Clamp(request.EndY.Value, safe, height - safe);
            return new PointD(x, y);
        }

        private static double DistanceRatio(Random r, SwipeIntent intent, FlingStrength strength) => intent switch
        {
            SwipeIntent.MicroAdjust => RandomMath.NextDouble(r, 0.055, 0.14),
            SwipeIntent.Reading => RandomMath.NextDouble(r, 0.17, 0.30),
            SwipeIntent.Preview => RandomMath.NextDouble(r, 0.29, 0.47),
            SwipeIntent.BackReview => RandomMath.NextDouble(r, 0.20, 0.36),
            SwipeIntent.FastScan => RandomMath.NextDouble(r, 0.48, 0.66),
            SwipeIntent.Fling => strength switch
            {
                FlingStrength.Soft => RandomMath.NextDouble(r, 0.33, 0.46),
                FlingStrength.Normal => RandomMath.NextDouble(r, 0.40, 0.54),
                FlingStrength.Strong => RandomMath.NextDouble(r, 0.46, 0.60),
                _ => RandomMath.NextDouble(r, 0.50, 0.64)
            },
            _ => RandomMath.NextDouble(r, 0.28, 0.46)
        };

        private static double ComputeDurationMs(Random r, SwipeIntent intent, FlingStrength strength, double distance, double speedFactor, double fatigue)
        {
            double baseDuration = intent switch
            {
                SwipeIntent.MicroAdjust => RandomMath.LogNormal(r, 260, 0.23, 150, 460),
                SwipeIntent.Reading => RandomMath.LogNormal(r, 620, 0.25, 330, 1100),
                SwipeIntent.Preview => RandomMath.LogNormal(r, 310, 0.24, 170, 560),
                SwipeIntent.BackReview => RandomMath.LogNormal(r, 360, 0.25, 190, 680),
                SwipeIntent.FastScan => RandomMath.LogNormal(r, 145, 0.18, 85, 240),
                SwipeIntent.Fling => strength switch
                {
                    FlingStrength.Soft => RandomMath.LogNormal(r, 205, 0.16, 125, 310),
                    FlingStrength.Normal => RandomMath.LogNormal(r, 165, 0.15, 100, 250),
                    FlingStrength.Strong => RandomMath.LogNormal(r, 132, 0.14, 82, 210),
                    _ => RandomMath.LogNormal(r, 112, 0.13, 72, 185)
                },
                _ => 300
            };

            double distanceScale = Math.Clamp(Math.Sqrt(Math.Max(40, distance) / 320.0), 0.70, 1.45);
            return Math.Clamp(baseDuration * distanceScale * (1.0 + fatigue * 0.18) / speedFactor, 65, 1500);
        }

        private static double ComputeReleaseVelocity(Random r, SwipeIntent intent, FlingStrength strength, double distance, double durationMs, double speedFactor)
        {
            if (intent != SwipeIntent.Fling && intent != SwipeIntent.FastScan)
                return intent == SwipeIntent.Preview ? RandomMath.NextDouble(r, 70, 240) : RandomMath.NextDouble(r, 0, 90);

            double target = strength switch
            {
                FlingStrength.Soft => RandomMath.NextDouble(r, 1200, 1900),
                FlingStrength.Normal => RandomMath.NextDouble(r, 1800, 2900),
                FlingStrength.Strong => RandomMath.NextDouble(r, 2600, 3900),
                _ => RandomMath.NextDouble(r, 3400, 5000)
            };
            if (intent == SwipeIntent.FastScan) target *= RandomMath.NextDouble(r, 1.03, 1.18);
            double physicallyReachable = distance / Math.Max(0.06, durationMs / 1000.0) * 2.4;
            return Math.Clamp(Math.Min(target * speedFactor, physicallyReachable), 650, 5200);
        }

        private static double Distance(PointD a, PointD b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

        private static HumanSwipeDirection ResolveDirection(Random r, HumanSwipeDirection direction) => direction switch
        {
            HumanSwipeDirection.RandomVertical => RandomMath.Chance(r, 0.88) ? HumanSwipeDirection.Up : HumanSwipeDirection.Down,
            HumanSwipeDirection.RandomAny => (HumanSwipeDirection)r.Next(0, 4),
            _ => direction
        };

        public static HumanSwipeMode IntentToMode(SwipeIntent intent) => intent switch
        {
            SwipeIntent.Reading => HumanSwipeMode.Reading,
            SwipeIntent.MicroAdjust => HumanSwipeMode.Micro,
            SwipeIntent.Fling => HumanSwipeMode.Fling,
            SwipeIntent.FastScan => HumanSwipeMode.Fling,
            SwipeIntent.BackReview => HumanSwipeMode.Preview,
            _ => HumanSwipeMode.Preview
        };
    }
}
