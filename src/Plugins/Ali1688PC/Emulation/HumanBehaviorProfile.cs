

namespace PlaywrightHumanInput;

public sealed record HumanBehaviorProfile
{
    public int Seed { get; init; }

    /// <summary>
    /// 整体操作速度。越大越慢。
    /// </summary>
    public double SpeedFactor { get; init; }

    public double MouseOvershootProbability { get; init; }

    public double PreClickPauseProbability { get; init; }

    public double MouseCorrectionProbability { get; init; }

    public double TypoProbability { get; init; }

    public double ThinkingPauseProbability { get; init; }

    public double ScrollBackProbability { get; init; }

    public int MinKeyDelayMs { get; init; }

    public int MaxKeyDelayMs { get; init; }

    public int MinMouseDownMs { get; init; }

    public int MaxMouseDownMs { get; init; }

    public float ActionTimeoutMs { get; init; }

    public float NavigationTimeoutMs { get; init; }

    public int MinReadingDelayMs { get; init; }

    public int MaxReadingDelayMs { get; init; }

    public int MinMouseSteps { get; init; }

    public int MaxMouseSteps { get; init; }

    public int MaxTargetScrollAttempts { get; init; }
}
