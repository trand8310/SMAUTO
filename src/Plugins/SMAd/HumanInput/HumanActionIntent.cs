namespace SMAd.HumanInput
{
    /// <summary>
    /// 与具体输入设备无关的浏览意图。触摸端映射为 Swipe，PC 端映射为滚轮动作。
    /// </summary>
    public enum HumanActionIntent
    {
        Reading = 0,
        Preview = 1,
        FastScan = 2,
        MicroAdjust = 3,
        BackReview = 4
    }
}
