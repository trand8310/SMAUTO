

namespace PlaywrightHumanInput;

/// <summary>
/// 单个浏览器会话的操作习惯。
///
/// 不要在每一次操作时重新生成配置，
/// 同一个用户会话应始终复用同一个 Profile。
/// </summary>
public sealed class HumanBehaviorProfile
{
    /// <summary>
    /// 整体速度系数。
    /// 小于 1 更快，大于 1 更慢。
    /// 建议范围：0.75～1.8。
    /// </summary>
    public double SpeedFactor { get; init; } = 1.0;

    /// <summary>
    /// 长距离鼠标移动时发生轻微过冲的概率。
    /// </summary>
    public double MouseOvershootProbability { get; init; } = 0.38;

    /// <summary>
    /// 移动到元素后，点击前稍作停顿的概率。
    /// </summary>
    public double PreClickPauseProbability { get; init; } = 0.72;

    /// <summary>
    /// 点击前发生小范围二次修正的概率。
    /// </summary>
    public double MouseCorrectionProbability { get; init; } = 0.24;

    /// <summary>
    /// 英文输入时产生可恢复输入错误的概率。
    /// 建议不要设置得太大。
    /// </summary>
    public double TypoProbability { get; init; } = 0.018;

    /// <summary>
    /// 输入时出现思考停顿的概率。
    /// </summary>
    public double ThinkingPauseProbability { get; init; } = 0.055;

    /// <summary>
    /// 浏览过程中发生小幅回看滚动的概率。
    /// </summary>
    public double ScrollBackProbability { get; init; } = 0.12;

    /// <summary>
    /// 普通按键间隔。
    /// </summary>
    public int MinKeyDelayMs { get; init; } = 38;

    public int MaxKeyDelayMs { get; init; } = 135;

    /// <summary>
    /// 点击按下到释放的时间。
    /// </summary>
    public int MinMouseDownMs { get; init; } = 55;

    public int MaxMouseDownMs { get; init; } = 145;

    /// <summary>
    /// 页面操作超时时间。
    /// </summary>
    public float ActionTimeoutMs { get; init; } = 15_000;

    public float NavigationTimeoutMs { get; init; } = 35_000;

    /// <summary>
    /// 页面阅读停顿。
    /// </summary>
    public int MinReadingDelayMs { get; init; } = 650;

    public int MaxReadingDelayMs { get; init; } = 2_400;

    /// <summary>
    /// 鼠标轨迹的最少和最多节点。
    /// </summary>
    public int MinMouseSteps { get; init; } = 12;

    public int MaxMouseSteps { get; init; } = 80;

    /// <summary>
    /// 定位目标时最多滚动次数。
    /// </summary>
    public int MaxTargetScrollAttempts { get; init; } = 14;

    public static HumanBehaviorProfile Normal()
    {
        return new HumanBehaviorProfile
        {
            SpeedFactor = 1.0,
            TypoProbability = 0.018,
            MouseOvershootProbability = 0.38
        };
    }

    public static HumanBehaviorProfile Deliberate()
    {
        return new HumanBehaviorProfile
        {
            SpeedFactor = 1.35,
            TypoProbability = 0.012,
            MouseOvershootProbability = 0.25,
            MinReadingDelayMs = 900,
            MaxReadingDelayMs = 3_200
        };
    }

    public static HumanBehaviorProfile Fast()
    {
        return new HumanBehaviorProfile
        {
            SpeedFactor = 0.78,
            TypoProbability = 0.025,
            MouseOvershootProbability = 0.45,
            MinReadingDelayMs = 350,
            MaxReadingDelayMs = 1_400
        };
    }
}
