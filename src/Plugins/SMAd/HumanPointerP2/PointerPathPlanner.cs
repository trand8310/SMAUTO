namespace SMAd.HumanPointerP2
{
    public sealed class PointerPathPlanner
    {
        public PointerTrace Plan(
            HumanPointerSession session,
            PointerPosition start,
            PointerPosition end,
            double targetWidth,
            double viewportWidth = double.PositiveInfinity,
            double viewportHeight = double.PositiveInfinity)
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
            double duration = (92 + (118 * indexOfDifficulty)) / profile.SpeedBias;
            duration *= Math.Exp(Normal(random) * 0.075);
            if (distance < 18)
                duration *= 0.78;
            duration = Math.Clamp(duration, 95, 1050);

            var points = new List<PointerTracePoint>();
            double overshootProbability = profile.OvershootChance *
                Math.Clamp((distance - effectiveWidth) / 180.0, 0.20, 1.0);
            if (distance > 70 && random.NextDouble() < overshootProbability)
            {
                double overshootDistance = Math.Clamp(distance * Next(random, 0.012, 0.038), 2.5, 16);
                var direction = Normalize(new PointerPosition(end.X - start.X, end.Y - start.Y));
                var perpendicular = new PointerPosition(-direction.Y, direction.X);
                double sideCorrection = Normal(random) * Math.Min(3.5, overshootDistance * 0.22);
                var overshoot = new PointerPosition(
                    end.X + (direction.X * overshootDistance) + (perpendicular.X * sideCorrection),
                    end.Y + (direction.Y * overshootDistance) + (perpendicular.Y * sideCorrection));
                overshoot = ClampToViewport(overshoot, viewportWidth, viewportHeight);

                AddSegment(session, points, start, overshoot, duration * Next(random, 0.78, 0.86), includeStart: false, viewportWidth, viewportHeight);
                double usedDuration = points.Sum(point => point.DelayMs);
                AddSegment(session, points, overshoot, end, Math.Max(35, duration - usedDuration), includeStart: false, viewportWidth, viewportHeight);
            }
            else
            {
                AddSegment(session, points, start, end, duration, includeStart: false, viewportWidth, viewportHeight);
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
            bool includeStart,
            double viewportWidth,
            double viewportHeight)
        {
            var random = session.Random;
            var profile = session.Profile;
            double distance = Distance(start, end);
            int samplingHz = (int)Math.Round(84 + (((random.NextDouble() + random.NextDouble()) * 0.5) * 62));
            int steps = Math.Clamp((int)Math.Ceiling(durationMs / (1000.0 / samplingHz)), 5, 96);
            double baseDelay = durationMs / steps;

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Max(1, Math.Sqrt((dx * dx) + (dy * dy)));
            double px = -dy / length;
            double py = dx / length;
            double bend = Math.Clamp(Math.Sqrt(distance) * Next(random, 0.75, 2.15), 1.5, 42);
            bend *= profile.CurveBias * (random.NextDouble() < 0.5 ? -1 : 1);

            double c1Progress = Next(random, 0.20, 0.36);
            double c2Progress = Next(random, 0.64, 0.82);
            double c2BendScale = Next(random, 0.18, 0.55);
            var c1 = ClampToViewport(new PointerPosition(
                start.X + (dx * c1Progress) + (px * bend),
                start.Y + (dy * c1Progress) + (py * bend)), viewportWidth, viewportHeight);
            var c2 = ClampToViewport(new PointerPosition(
                start.X + (dx * c2Progress) + (px * bend * c2BendScale),
                start.Y + (dy * c2Progress) + (py * bend * c2BendScale)), viewportWidth, viewportHeight);

            double noiseX = 0;
            double noiseY = 0;
            double timingNoise = 0;
            double noiseScale = Math.Clamp(Math.Sqrt(distance) / 17.0, 0.20, 1.35) *
                profile.TremorBias / profile.PrecisionBias;
            int first = includeStart ? 0 : 1;
            double elapsed = output.Count == 0 ? 0 : output[^1].TimeMs;

            for (int i = first; i <= steps; i++)
            {
                double linearT = i / (double)steps;
                double t = MinimumJerk(linearT);
                var point = CubicBezier(start, c1, c2, end, t);

                // 相关噪声只作用在轨迹中段，确保起点和终点准确。
                noiseX = (noiseX * 0.76) + (Normal(random) * 0.18 * noiseScale);
                noiseY = (noiseY * 0.76) + (Normal(random) * 0.18 * noiseScale);
                double envelope = Math.Sin(Math.PI * linearT);
                point = ClampToViewport(new PointerPosition(
                    point.X + (noiseX * envelope),
                    point.Y + (noiseY * envelope)), viewportWidth, viewportHeight);

                timingNoise = (timingNoise * 0.66) + (Normal(random) * 0.055);
                int delay = i == first && includeStart
                    ? 0
                    : Math.Max(1, (int)Math.Round(baseDelay * Math.Exp(timingNoise)));
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

        private static PointerPosition ClampToViewport(
            PointerPosition point,
            double viewportWidth,
            double viewportHeight)
        {
            double x = double.IsFinite(viewportWidth)
                ? Math.Clamp(point.X, 1, Math.Max(1, viewportWidth - 1))
                : point.X;
            double y = double.IsFinite(viewportHeight)
                ? Math.Clamp(point.Y, 1, Math.Max(1, viewportHeight - 1))
                : point.Y;
            return new PointerPosition(x, y);
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
