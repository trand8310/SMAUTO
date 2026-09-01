namespace SMAd.HumanPointerP2
{
    public sealed class PointerPathPlanner
    {
        public PointerTrace Plan(
            HumanPointerSession session,
            PointerPosition start,
            PointerPosition end,
            double targetWidth)
        {
            var random = session.Random;
            var profile = session.Profile;
            double distance = Distance(start, end);

            if (distance < 0.5)
            {
                return new PointerTrace
                {
                    Start = start,
                    End = end,
                    DurationMs = 0,
                    Points = new[]
                    {
                        new PointerTracePoint { X = end.X, Y = end.Y, DelayMs = 0, TimeMs = 0 }
                    }
                };
            }

            // 使用 Fitts 定律估计移动时间，再叠加个人速度偏差。
            double effectiveWidth = Math.Max(6, targetWidth * profile.PrecisionBias);
            double indexOfDifficulty = Math.Log2((distance / effectiveWidth) + 1.0);
            double duration = (135 + (105 * indexOfDifficulty)) / profile.SpeedBias;
            duration *= Next(random, 0.88, 1.14);
            duration = Math.Clamp(duration, 150, 1150);

            var points = new List<PointerTracePoint>();
            if (distance > 90 && random.NextDouble() < profile.OvershootChance)
            {
                double overshootDistance = Math.Clamp(distance * Next(random, 0.015, 0.045), 3, 18);
                var direction = Normalize(new PointerPosition(end.X - start.X, end.Y - start.Y));
                var overshoot = new PointerPosition(
                    end.X + (direction.X * overshootDistance),
                    end.Y + (direction.Y * overshootDistance));

                AddSegment(session, points, start, overshoot, duration * 0.82, includeStart: false);
                AddSegment(session, points, overshoot, end, duration * 0.18, includeStart: false);
            }
            else
            {
                AddSegment(session, points, start, end, duration, includeStart: false);
            }

            if (points.Count == 0 || points[^1].X != end.X || points[^1].Y != end.Y)
            {
                points.Add(new PointerTracePoint
                {
                    X = end.X,
                    Y = end.Y,
                    DelayMs = 8,
                    TimeMs = duration
                });
            }

            return new PointerTrace
            {
                Start = start,
                End = end,
                DurationMs = points.Sum(x => x.DelayMs),
                Points = points
            };
        }

        private static void AddSegment(
            HumanPointerSession session,
            List<PointerTracePoint> output,
            PointerPosition start,
            PointerPosition end,
            double durationMs,
            bool includeStart)
        {
            var random = session.Random;
            var profile = session.Profile;
            double distance = Distance(start, end);
            int samplingHz = random.Next(72, 121);
            int steps = Math.Clamp((int)Math.Ceiling(durationMs / (1000.0 / samplingHz)), 5, 96);
            double baseDelay = durationMs / steps;

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Max(1, Math.Sqrt((dx * dx) + (dy * dy)));
            double px = -dy / length;
            double py = dx / length;
            double bend = Math.Clamp(distance * Next(random, 0.035, 0.12), 2, 70);
            bend *= profile.CurveBias * (random.NextDouble() < 0.5 ? -1 : 1);

            var c1 = new PointerPosition(
                start.X + (dx * Next(random, 0.20, 0.36)) + (px * bend),
                start.Y + (dy * Next(random, 0.20, 0.36)) + (py * bend));
            var c2 = new PointerPosition(
                start.X + (dx * Next(random, 0.64, 0.82)) - (px * bend * Next(random, 0.20, 0.65)),
                start.Y + (dy * Next(random, 0.64, 0.82)) - (py * bend * Next(random, 0.20, 0.65)));

            double noiseX = 0;
            double noiseY = 0;
            int first = includeStart ? 0 : 1;
            double elapsed = output.Count == 0 ? 0 : output[^1].TimeMs;

            for (int i = first; i <= steps; i++)
            {
                double linearT = i / (double)steps;
                double t = MinimumJerk(linearT);
                var point = CubicBezier(start, c1, c2, end, t);

                // 相关噪声只作用在轨迹中段，确保起点和终点准确。
                noiseX = (noiseX * 0.72) + (Normal(random) * 0.32 * profile.TremorBias);
                noiseY = (noiseY * 0.72) + (Normal(random) * 0.32 * profile.TremorBias);
                double envelope = Math.Sin(Math.PI * linearT);
                point = new PointerPosition(
                    point.X + (noiseX * envelope),
                    point.Y + (noiseY * envelope));

                int delay = i == first && includeStart
                    ? 0
                    : Math.Max(1, (int)Math.Round(baseDelay * Next(random, 0.84, 1.18)));
                elapsed += delay;

                if (i == steps)
                    point = end;

                output.Add(new PointerTracePoint
                {
                    X = point.X,
                    Y = point.Y,
                    DelayMs = delay,
                    TimeMs = elapsed
                });
            }
        }

        private static PointerPosition CubicBezier(
            PointerPosition p0,
            PointerPosition p1,
            PointerPosition p2,
            PointerPosition p3,
            double t)
        {
            double u = 1 - t;
            double tt = t * t;
            double uu = u * u;
            double uuu = uu * u;
            double ttt = tt * t;
            return new PointerPosition(
                (uuu * p0.X) + (3 * uu * t * p1.X) + (3 * u * tt * p2.X) + (ttt * p3.X),
                (uuu * p0.Y) + (3 * uu * t * p1.Y) + (3 * u * tt * p2.Y) + (ttt * p3.Y));
        }

        private static double MinimumJerk(double t) =>
            (10 * t * t * t) - (15 * t * t * t * t) + (6 * t * t * t * t * t);

        private static double Distance(PointerPosition a, PointerPosition b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static PointerPosition Normalize(PointerPosition value)
        {
            double length = Math.Sqrt((value.X * value.X) + (value.Y * value.Y));
            return length < 0.0001
                ? new PointerPosition(1, 0)
                : new PointerPosition(value.X / length, value.Y / length);
        }

        private static double Normal(Random random)
        {
            double u1 = Math.Max(double.Epsilon, random.NextDouble());
            double u2 = random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        private static double Next(Random random, double min, double max) =>
            min + (random.NextDouble() * (max - min));
    }
}
