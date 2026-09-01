using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaywrightHumanInput
{
    public sealed class GestureTraceMetrics
    {
        public int Samples { get; init; }
        public double DurationMs { get; init; }
        public double DistancePx { get; init; }
        public double MeanSamplingIntervalMs { get; init; }
        public double SamplingIntervalStdMs { get; init; }
        public double MeanVelocityPxPerSecond { get; init; }
        public double PeakVelocityPxPerSecond { get; init; }
        public double ReleaseVelocityPxPerSecond { get; init; }
        public double PeakAccelerationPxPerSecond2 { get; init; }
        public double PeakJerkPxPerSecond3 { get; init; }
        public double CurvatureRatio { get; init; }
    }

    public static class GestureTraceAnalyzer
    {
        public static GestureTraceMetrics Analyze(HumanSwipeTrace trace)
        {
            if (trace?.Points == null || trace.Points.Count < 2)
                return new GestureTraceMetrics();

            var points = trace.Points;
            var intervals = new List<double>();
            var velocities = new List<double>();
            var accelerations = new List<double>();
            var jerks = new List<double>();
            double pathDistance = 0;

            for (int i = 1; i < points.Count; i++)
            {
                double dtMs = Math.Max(0.1, points[i].TimeMs - points[i - 1].TimeMs);
                double dx = points[i].X - points[i - 1].X;
                double dy = points[i].Y - points[i - 1].Y;
                double ds = Math.Sqrt(dx * dx + dy * dy);
                pathDistance += ds;
                intervals.Add(dtMs);
                velocities.Add(ds / (dtMs / 1000.0));
            }

            for (int i = 1; i < velocities.Count; i++)
            {
                double dt = Math.Max(0.0001, intervals[Math.Min(i, intervals.Count - 1)] / 1000.0);
                accelerations.Add((velocities[i] - velocities[i - 1]) / dt);
            }
            for (int i = 1; i < accelerations.Count; i++)
            {
                double dt = Math.Max(0.0001, intervals[Math.Min(i + 1, intervals.Count - 1)] / 1000.0);
                jerks.Add((accelerations[i] - accelerations[i - 1]) / dt);
            }

            double directDx = points[^1].X - points[0].X;
            double directDy = points[^1].Y - points[0].Y;
            double direct = Math.Sqrt(directDx * directDx + directDy * directDy);

            return new GestureTraceMetrics
            {
                Samples = points.Count,
                DurationMs = trace.DurationMs,
                DistancePx = pathDistance,
                MeanSamplingIntervalMs = intervals.Count == 0 ? 0 : intervals.Average(),
                SamplingIntervalStdMs = Std(intervals),
                MeanVelocityPxPerSecond = velocities.Count == 0 ? 0 : velocities.Average(),
                PeakVelocityPxPerSecond = velocities.Count == 0 ? 0 : velocities.Max(),
                ReleaseVelocityPxPerSecond = velocities.Count == 0 ? 0 : velocities[^1],
                PeakAccelerationPxPerSecond2 = accelerations.Count == 0 ? 0 : accelerations.Max(x => Math.Abs(x)),
                PeakJerkPxPerSecond3 = jerks.Count == 0 ? 0 : jerks.Max(x => Math.Abs(x)),
                CurvatureRatio = direct <= 0.001 ? 1.0 : pathDistance / direct
            };
        }

        private static double Std(IReadOnlyList<double> values)
        {
            if (values.Count < 2) return 0;
            double mean = values.Average();
            double sum = 0;
            foreach (double v in values) sum += (v - mean) * (v - mean);
            return Math.Sqrt(sum / (values.Count - 1));
        }
    }
}
