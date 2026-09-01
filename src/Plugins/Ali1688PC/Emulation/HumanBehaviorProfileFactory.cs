

namespace PlaywrightHumanInput;

public static class HumanBehaviorProfileFactory
{
    public static HumanBehaviorProfile Create(int seed)
    {
        var random = new StableRandom(seed);

        // 越大越慢。
        double tempo = random.NextDouble();

        // 越大越准确。
        double precision = random.NextDouble();

        // 越大越有耐心。
        double patience = random.NextDouble();

        // 越大越容易悬停、回看和修正。
        double exploration = random.NextDouble();

        double speedFactor = Lerp(
            0.82,
            1.35,
            tempo);

        double overshootProbability = Lerp(
            0.48,
            0.18,
            precision);

        double correctionProbability =
            Lerp(0.14, 0.34, exploration) *
            Lerp(1.15, 0.85, precision);

        double typoProbability =
            0.004 +
            (1.0 - precision) * 0.014 +
            (1.0 - tempo) * 0.005;

        typoProbability = Math.Clamp(
            typoProbability,
            0.004,
            0.028);

        double thinkingProbability =
            Lerp(0.025, 0.085, patience);

        double scrollBackProbability =
            Lerp(0.05, 0.18, exploration);

        int minKeyDelay = RoundToInt(
            Lerp(25, 58, tempo));

        int maxKeyDelay = minKeyDelay +
                          random.NextInt(55, 105);

        int minReadingDelay = RoundToInt(
            Lerp(400, 1_050, patience) *
            speedFactor);

        int maxReadingDelay = minReadingDelay +
                              RoundToInt(
                                  Lerp(
                                      850,
                                      2_600,
                                      patience));

        int minMouseSteps = RoundToInt(
            Lerp(10, 18, tempo));

        int maxMouseSteps = RoundToInt(
            Lerp(48, 82, tempo));

        return new HumanBehaviorProfile
        {
            Seed = seed,

            SpeedFactor = speedFactor,

            MouseOvershootProbability =
                ClampProbability(overshootProbability),

            PreClickPauseProbability =
                ClampProbability(
                    Lerp(0.58, 0.88, patience)),

            MouseCorrectionProbability =
                ClampProbability(correctionProbability),

            TypoProbability =
                ClampProbability(typoProbability),

            ThinkingPauseProbability =
                ClampProbability(thinkingProbability),

            ScrollBackProbability =
                ClampProbability(scrollBackProbability),

            MinKeyDelayMs = minKeyDelay,
            MaxKeyDelayMs = maxKeyDelay,

            MinMouseDownMs = RoundToInt(
                Lerp(45, 75, tempo)),

            MaxMouseDownMs = RoundToInt(
                Lerp(105, 165, tempo)),

            ActionTimeoutMs = 15_000,
            NavigationTimeoutMs = 35_000,

            MinReadingDelayMs = minReadingDelay,
            MaxReadingDelayMs = maxReadingDelay,

            MinMouseSteps = minMouseSteps,
            MaxMouseSteps = Math.Max(
                minMouseSteps + 20,
                maxMouseSteps),

            MaxTargetScrollAttempts =
                random.NextInt(12, 17)
        };
    }

    private static double Lerp(
        double min,
        double max,
        double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return min + (max - min) * amount;
    }

    private static double ClampProbability(double value)
    {
        return Math.Clamp(value, 0, 1);
    }

    private static int RoundToInt(double value)
    {
        return (int)Math.Round(
            value,
            MidpointRounding.AwayFromZero);
    }
}

