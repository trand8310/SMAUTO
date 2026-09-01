using System;

namespace PlaywrightHumanInput
{
    public sealed class HumanUserProfile
    {
        public string ProfileId { get; init; } = Guid.NewGuid().ToString("N");
        public int Seed { get; init; }
        public HumanHandedness Handedness { get; init; } = HumanHandedness.Right;
        public double VerticalCenterXRatio { get; init; } = 0.63;
        public double HorizontalCenterYRatio { get; init; } = 0.56;
        public double StartPositionStdRatio { get; init; } = 0.055;
        public double SpeedBias { get; init; } = 1.0;
        public double DistanceBias { get; init; } = 1.0;
        public double CurveBias { get; init; } = 1.0;
        public double DriftBias { get; init; } = 1.0;
        public double TremorBias { get; init; } = 1.0;
        public double ForceBias { get; init; } = 1.0;
        public double TouchAreaBias { get; init; } = 1.0;
        public double PauseBias { get; init; } = 1.0;
        public double HesitationBias { get; init; } = 1.0;
        public double PullBackBias { get; init; } = 1.0;
        public double FatigueSensitivity { get; init; } = 1.0;
        public double RecoveryBias { get; init; } = 1.0;
        public double ReactionBias { get; init; } = 1.0;
        public double PreferredTouchRadiusPx { get; init; } = 3.8;
        public double PreferredRotationDeg { get; init; } = 42;

        public static HumanUserProfile CreateRandom(int? seed = null, HumanHandedness? handedness = null)
        {
            int actualSeed = seed ?? Guid.NewGuid().GetHashCode();
            var random = new Random(actualSeed);
            var hand = handedness ?? (random.NextDouble() < 0.88 ? HumanHandedness.Right : HumanHandedness.Left);
            double center = hand == HumanHandedness.Right
                ? RandomMath.TruncatedNormal(random, 0.64, 0.055, 0.48, 0.78)
                : RandomMath.TruncatedNormal(random, 0.36, 0.055, 0.22, 0.52);

            return new HumanUserProfile
            {
                Seed = actualSeed,
                Handedness = hand,
                VerticalCenterXRatio = center,
                HorizontalCenterYRatio = RandomMath.TruncatedNormal(random, 0.56, 0.05, 0.42, 0.70),
                StartPositionStdRatio = RandomMath.NextDouble(random, 0.035, 0.075),
                SpeedBias = RandomMath.NextDouble(random, 0.86, 1.17),
                DistanceBias = RandomMath.NextDouble(random, 0.88, 1.15),
                CurveBias = RandomMath.NextDouble(random, 0.75, 1.28),
                DriftBias = RandomMath.NextDouble(random, 0.72, 1.32),
                TremorBias = RandomMath.NextDouble(random, 0.72, 1.30),
                ForceBias = RandomMath.NextDouble(random, 0.86, 1.14),
                TouchAreaBias = RandomMath.NextDouble(random, 0.86, 1.16),
                PauseBias = RandomMath.NextDouble(random, 0.78, 1.30),
                HesitationBias = RandomMath.NextDouble(random, 0.72, 1.35),
                PullBackBias = RandomMath.NextDouble(random, 0.70, 1.35),
                FatigueSensitivity = RandomMath.NextDouble(random, 0.75, 1.30),
                RecoveryBias = RandomMath.NextDouble(random, 0.80, 1.25),
                ReactionBias = RandomMath.NextDouble(random, 0.82, 1.25),
                PreferredTouchRadiusPx = RandomMath.NextDouble(random, 3.0, 4.8),
                PreferredRotationDeg = hand == HumanHandedness.Right
                    ? RandomMath.NextDouble(random, 28, 62)
                    : RandomMath.NextDouble(random, 118, 152)
            };
        }
    }
}
