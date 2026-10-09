using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaywrightHumanInput
{
    internal static class RandomMath
    {
        public static double NextDouble(Random random, double min, double max)
            => min + random.NextDouble() * (max - min);

        public static int NextInt(Random random, int min, int maxInclusive)
        {
            if (maxInclusive <= min) return min;
            return random.Next(min, maxInclusive + 1);
        }

        public static bool Chance(Random random, double p)
            => random.NextDouble() < Math.Clamp(p, 0.0, 1.0);

        public static double Normal(Random random, double mean = 0, double stdDev = 1)
        {
            double u1 = Math.Max(double.Epsilon, 1.0 - random.NextDouble());
            double u2 = 1.0 - random.NextDouble();
            double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            return mean + stdDev * z;
        }

        public static double TruncatedNormal(Random random, double mean, double stdDev, double min, double max)
        {
            if (stdDev <= 0) return Math.Clamp(mean, min, max);
            for (int i = 0; i < 12; i++)
            {
                double x = Normal(random, mean, stdDev);
                if (x >= min && x <= max) return x;
            }
            return Math.Clamp(mean + Normal(random, 0, stdDev * 0.35), min, max);
        }

        public static double LogNormal(Random random, double median, double sigma, double min, double max)
        {
            double mu = Math.Log(Math.Max(1e-6, median));
            double value = Math.Exp(Normal(random, mu, sigma));
            return Math.Clamp(value, min, max);
        }

        public static double SmoothStep(double edge0, double edge1, double x)
        {
            if (Math.Abs(edge1 - edge0) < 1e-9) return x >= edge1 ? 1 : 0;
            double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
            return t * t * (3.0 - 2.0 * t);
        }

        public static double MinimumJerk(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return 10 * t * t * t - 15 * Math.Pow(t, 4) + 6 * Math.Pow(t, 5);
        }

        public static double MinimumJerkDerivative(double t)
        {
            t = Math.Clamp(t, 0, 1);
            return 30 * t * t - 60 * t * t * t + 30 * Math.Pow(t, 4);
        }

        public static double Gaussian(double x, double center, double width)
        {
            width = Math.Max(1e-6, width);
            double z = (x - center) / width;
            return Math.Exp(-0.5 * z * z);
        }

        /// <summary>
        /// 将输入时间映射为一个“主速度峰位置可变”的时间轴。
        /// MinimumJerkDerivative 的峰值在 0.5；此函数把真实时间中的 peakRatio 映射到 0.5。
        /// </summary>
        public static double WarpAroundPeak(double t, double peakRatio)
        {
            t = Math.Clamp(t, 0, 1);
            peakRatio = Math.Clamp(peakRatio, 0.20, 0.80);

            if (t <= peakRatio)
                return 0.5 * t / Math.Max(1e-6, peakRatio);

            return 0.5 + 0.5 * (t - peakRatio) / Math.Max(1e-6, 1.0 - peakRatio);
        }

        /// <summary>
        /// 产生一个起点/终点都为 0，峰值位置可调的非对称包络。
        /// </summary>
        public static double SkewedEnvelope(double t, double peakRatio, double concentration = 4.0)
        {
            t = Math.Clamp(t, 0, 1);
            peakRatio = Math.Clamp(peakRatio, 0.18, 0.82);
            concentration = Math.Clamp(concentration, 2.0, 12.0);

            if (t <= 0 || t >= 1) return 0;

            double a = Math.Max(0.35, peakRatio * concentration);
            double b = Math.Max(0.35, (1.0 - peakRatio) * concentration);
            double value = Math.Pow(t, a) * Math.Pow(1.0 - t, b);
            double peak = Math.Pow(peakRatio, a) * Math.Pow(1.0 - peakRatio, b);
            return peak <= 1e-12 ? 0 : value / peak;
        }

        public static double Pearson(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            int n = Math.Min(a.Count, b.Count);
            if (n < 3) return 0;

            double meanA = a.Take(n).Average();
            double meanB = b.Take(n).Average();
            double cov = 0;
            double varA = 0;
            double varB = 0;

            for (int i = 0; i < n; i++)
            {
                double da = a[i] - meanA;
                double db = b[i] - meanB;
                cov += da * db;
                varA += da * da;
                varB += db * db;
            }

            double denom = Math.Sqrt(varA * varB);
            return denom <= 1e-12 ? 0 : cov / denom;
        }
    }
}
