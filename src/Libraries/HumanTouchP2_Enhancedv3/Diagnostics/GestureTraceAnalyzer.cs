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
        public double DirectDistancePx { get; init; }
        public double PathEfficiency { get; init; }

        public double MeanSamplingIntervalMs { get; init; }
        public double SamplingIntervalStdMs { get; init; }
        public double SamplingIntervalCv { get; init; }

        public double MeanVelocityPxPerSecond { get; init; }
        public double PeakVelocityPxPerSecond { get; init; }
        public double PeakVelocityPositionRatio { get; init; }
        public double ReleaseVelocityPxPerSecond { get; init; }
        public double TargetReleaseVelocityPxPerSecond { get; init; }
        public double ReleaseVelocityRelativeError { get; init; }

        public double PeakAccelerationPxPerSecond2 { get; init; }
        public double PeakJerkPxPerSecond3 { get; init; }

        public double CurvatureRatio { get; init; }
        public double MeanAbsLateralDeviationPx { get; init; }
        public double MaxAbsLateralDeviationPx { get; init; }
        public int CurvatureSignChanges { get; init; }
        public double LateralResidualLag1Autocorrelation { get; init; }

        public double MeanForce { get; init; }
        public double ForceStd { get; init; }
        public double MeanRadiusPx { get; init; }
        public double RadiusStdPx { get; init; }
        public double PressureVelocityCorrelation { get; init; }
        public double RadiusForceCorrelation { get; init; }
        public double PlannedEndpointErrorPx { get; init; }
    }

    public sealed class GestureTraceBatchMetrics
    {
        public int Gestures { get; init; }
        public double MeanDurationMs { get; init; }
        public double MeanPathEfficiency { get; init; }
        public double MeanPeakVelocityPositionRatio { get; init; }
        public double MeanSamplingIntervalCv { get; init; }
        public double MeanLateralResidualLag1Autocorrelation { get; init; }
        public double StartXStdPx { get; init; }
        public double StartYStdPx { get; init; }
        public double EndXStdPx { get; init; }
        public double EndYStdPx { get; init; }
        public double IntentRepeatRate { get; init; }
        public double DirectionRepeatRate { get; init; }
    }

    public static class GestureTraceAnalyzer
    {
        public static GestureTraceMetrics Analyze(HumanSwipeTrace trace)
        {
            if (trace?.Points == null || trace.Points.Count < 2)
                return new GestureTraceMetrics();

            var points = trace.Points;
            var intervals = new List<double>(points.Count - 1);
            var velocities = new List<double>(points.Count - 1);
            var velocityTimes = new List<double>(points.Count - 1);
            var accelerations = new List<double>();
            var jerks = new List<double>();
            var force = new List<double>(points.Count);
            var radius = new List<double>(points.Count);
            double pathDistance = 0;

            for (int i = 0; i < points.Count; i++)
            {
                force.Add(points[i].Force);
                radius.Add((points[i].RadiusX + points[i].RadiusY) * 0.5);
            }

            for (int i = 1; i < points.Count; i++)
            {
                double dtMs = Math.Max(0.1, points[i].TimeMs - points[i - 1].TimeMs);
                double dx = points[i].X - points[i - 1].X;
                double dy = points[i].Y - points[i - 1].Y;
                double ds = Math.Sqrt(dx * dx + dy * dy);
                double v = ds / (dtMs / 1000.0);

                pathDistance += ds;
                intervals.Add(dtMs);
                velocities.Add(v);
                velocityTimes.Add(points[i].TimeMs);
            }

            for (int i = 1; i < velocities.Count; i++)
            {
                double dt = Math.Max(
                    0.0001,
                    intervals[Math.Min(i, intervals.Count - 1)] / 1000.0);

                accelerations.Add((velocities[i] - velocities[i - 1]) / dt);
            }

            for (int i = 1; i < accelerations.Count; i++)
            {
                double dt = Math.Max(
                    0.0001,
                    intervals[Math.Min(i + 1, intervals.Count - 1)] / 1000.0);

                jerks.Add((accelerations[i] - accelerations[i - 1]) / dt);
            }

            double directDx = points[^1].X - points[0].X;
            double directDy = points[^1].Y - points[0].Y;
            double direct = Math.Sqrt(directDx * directDx + directDy * directDy);

            int peakVelocityIndex = IndexOfMax(velocities);
            double peakVelocityPositionRatio =
                peakVelocityIndex >= 0 && trace.DurationMs > 0
                    ? Math.Clamp(velocityTimes[peakVelocityIndex] / trace.DurationMs, 0, 1)
                    : 0;

            var lateral = SignedLateralDeviations(trace);
            var lateralResidual = DetrendMovingAverage(lateral, 5);
            double lag1 = Lag1Autocorrelation(lateralResidual);

            int signChanges = CountCurvatureSignChanges(points);

            var alignedForce = force.Skip(1).Take(velocities.Count).ToList();
            var alignedRadius = radius.Skip(1).Take(velocities.Count).ToList();

            double targetRelease = trace.TargetReleaseVelocityPxPerSecond;
            double actualRelease = velocities.Count == 0 ? 0 : velocities[^1];
            double releaseError = targetRelease <= 1
                ? 0
                : Math.Abs(actualRelease - targetRelease) / targetRelease;

            double plannedEndDx = trace.EndX - trace.PlannedEndX;
            double plannedEndDy = trace.EndY - trace.PlannedEndY;
            double plannedEndpointError = Math.Sqrt(
                plannedEndDx * plannedEndDx +
                plannedEndDy * plannedEndDy);

            return new GestureTraceMetrics
            {
                Samples = points.Count,
                DurationMs = trace.DurationMs,
                DistancePx = pathDistance,
                DirectDistancePx = direct,
                PathEfficiency = pathDistance <= 1e-9 ? 1.0 : direct / pathDistance,

                MeanSamplingIntervalMs = intervals.Count == 0 ? 0 : intervals.Average(),
                SamplingIntervalStdMs = Std(intervals),
                SamplingIntervalCv = CoefficientOfVariation(intervals),

                MeanVelocityPxPerSecond = velocities.Count == 0 ? 0 : velocities.Average(),
                PeakVelocityPxPerSecond = velocities.Count == 0 ? 0 : velocities.Max(),
                PeakVelocityPositionRatio = peakVelocityPositionRatio,
                ReleaseVelocityPxPerSecond = actualRelease,
                TargetReleaseVelocityPxPerSecond = targetRelease,
                ReleaseVelocityRelativeError = releaseError,

                PeakAccelerationPxPerSecond2 = accelerations.Count == 0
                    ? 0
                    : accelerations.Max(x => Math.Abs(x)),
                PeakJerkPxPerSecond3 = jerks.Count == 0
                    ? 0
                    : jerks.Max(x => Math.Abs(x)),

                CurvatureRatio = direct <= 0.001 ? 1.0 : pathDistance / direct,
                MeanAbsLateralDeviationPx = lateral.Count == 0
                    ? 0
                    : lateral.Average(x => Math.Abs(x)),
                MaxAbsLateralDeviationPx = lateral.Count == 0
                    ? 0
                    : lateral.Max(x => Math.Abs(x)),
                CurvatureSignChanges = signChanges,
                LateralResidualLag1Autocorrelation = lag1,

                MeanForce = force.Count == 0 ? 0 : force.Average(),
                ForceStd = Std(force),
                MeanRadiusPx = radius.Count == 0 ? 0 : radius.Average(),
                RadiusStdPx = Std(radius),
                PressureVelocityCorrelation = RandomMath.Pearson(alignedForce, velocities),
                RadiusForceCorrelation = RandomMath.Pearson(radius, force),
                PlannedEndpointErrorPx = plannedEndpointError
            };
        }

        public static GestureTraceBatchMetrics AnalyzeBatch(IEnumerable<HumanSwipeTrace> traces)
        {
            var list = traces?
                .Where(x => x != null && x.Points != null && x.Points.Count >= 2)
                .ToList() ?? new List<HumanSwipeTrace>();

            if (list.Count == 0)
                return new GestureTraceBatchMetrics();

            var metrics = list.Select(Analyze).ToList();
            int sameIntent = 0;
            int sameDirection = 0;

            for (int i = 1; i < list.Count; i++)
            {
                if (list[i].Intent == list[i - 1].Intent)
                    sameIntent++;
                if (list[i].Direction == list[i - 1].Direction)
                    sameDirection++;
            }

            double transitionDenom = Math.Max(1, list.Count - 1);

            return new GestureTraceBatchMetrics
            {
                Gestures = list.Count,
                MeanDurationMs = metrics.Average(x => x.DurationMs),
                MeanPathEfficiency = metrics.Average(x => x.PathEfficiency),
                MeanPeakVelocityPositionRatio = metrics.Average(x => x.PeakVelocityPositionRatio),
                MeanSamplingIntervalCv = metrics.Average(x => x.SamplingIntervalCv),
                MeanLateralResidualLag1Autocorrelation = metrics.Average(x => x.LateralResidualLag1Autocorrelation),
                StartXStdPx = Std(list.Select(x => x.StartX).ToList()),
                StartYStdPx = Std(list.Select(x => x.StartY).ToList()),
                EndXStdPx = Std(list.Select(x => x.EndX).ToList()),
                EndYStdPx = Std(list.Select(x => x.EndY).ToList()),
                IntentRepeatRate = sameIntent / transitionDenom,
                DirectionRepeatRate = sameDirection / transitionDenom
            };
        }

        private static List<double> SignedLateralDeviations(HumanSwipeTrace trace)
        {
            var result = new List<double>(trace.Points.Count);
            double x0 = trace.Points[0].X;
            double y0 = trace.Points[0].Y;

            double x1 = Math.Abs(trace.PlannedEndX) > 1e-9 || Math.Abs(trace.PlannedEndY) > 1e-9
                ? trace.PlannedEndX
                : trace.Points[^1].X;
            double y1 = Math.Abs(trace.PlannedEndX) > 1e-9 || Math.Abs(trace.PlannedEndY) > 1e-9
                ? trace.PlannedEndY
                : trace.Points[^1].Y;

            double dx = x1 - x0;
            double dy = y1 - y0;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length <= 1e-9)
                return result;

            foreach (var p in trace.Points)
            {
                double px = p.X - x0;
                double py = p.Y - y0;
                double signed = (dx * py - dy * px) / length;
                result.Add(signed);
            }

            return result;
        }

        private static List<double> DetrendMovingAverage(IReadOnlyList<double> values, int window)
        {
            var output = new List<double>(values.Count);
            if (values.Count == 0)
                return output;

            int half = Math.Max(1, window / 2);
            for (int i = 0; i < values.Count; i++)
            {
                int from = Math.Max(0, i - half);
                int to = Math.Min(values.Count - 1, i + half);
                double sum = 0;
                int count = 0;

                for (int j = from; j <= to; j++)
                {
                    sum += values[j];
                    count++;
                }

                double mean = count == 0 ? 0 : sum / count;
                output.Add(values[i] - mean);
            }

            return output;
        }

        private static double Lag1Autocorrelation(IReadOnlyList<double> values)
        {
            if (values.Count < 4)
                return 0;

            var a = values.Take(values.Count - 1).ToList();
            var b = values.Skip(1).ToList();
            return RandomMath.Pearson(a, b);
        }

        private static int CountCurvatureSignChanges(IReadOnlyList<HumanSwipeTracePoint> points)
        {
            int changes = 0;
            int lastSign = 0;

            for (int i = 1; i < points.Count - 1; i++)
            {
                double ax = points[i].X - points[i - 1].X;
                double ay = points[i].Y - points[i - 1].Y;
                double bx = points[i + 1].X - points[i].X;
                double by = points[i + 1].Y - points[i].Y;
                double cross = ax * by - ay * bx;

                if (Math.Abs(cross) < 0.02)
                    continue;

                int sign = cross > 0 ? 1 : -1;
                if (lastSign != 0 && sign != lastSign)
                    changes++;
                lastSign = sign;
            }

            return changes;
        }

        private static int IndexOfMax(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
                return -1;

            int index = 0;
            double max = values[0];
            for (int i = 1; i < values.Count; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                    index = i;
                }
            }
            return index;
        }

        private static double CoefficientOfVariation(IReadOnlyList<double> values)
        {
            if (values.Count < 2)
                return 0;

            double mean = values.Average();
            if (Math.Abs(mean) <= 1e-12)
                return 0;

            return Std(values) / Math.Abs(mean);
        }

        private static double Std(IReadOnlyList<double> values)
        {
            if (values.Count < 2)
                return 0;

            double mean = values.Average();
            double sum = 0;

            foreach (double v in values)
                sum += (v - mean) * (v - mean);

            return Math.Sqrt(sum / (values.Count - 1));
        }
    }
}
