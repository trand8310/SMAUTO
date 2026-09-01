using System;

namespace PlaywrightHumanInput
{
    internal static class RandomMath
    {
        public static double NextDouble(Random random, double min, double max) => min + random.NextDouble() * (max - min);

        public static int NextInt(Random random, int min, int maxInclusive)
        {
            if (maxInclusive <= min) return min;
            return random.Next(min, maxInclusive + 1);
        }

        public static bool Chance(Random random, double p) => random.NextDouble() < Math.Clamp(p, 0.0, 1.0);

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
    }
}
