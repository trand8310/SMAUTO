

namespace SMAd.Swiper
{
    public sealed class ScrollOptions
    {
        /// <summary>滚动模式，默认自动</summary>
        public HumanScrollMode Mode { get; set; } = HumanScrollMode.Auto;

        /// <summary>滑动距离占屏高比例（优先级低于 DistancePx）</summary>
        public double? HeightRatio { get; set; }

        /// <summary>直接指定像素距离（最高优先级）</summary>
        public int? DistancePx { get; set; }

        /// <summary>轨迹点数量范围</summary>
        public (int Min, int Max)? PointCountRange { get; set; }

        /// <summary>每步延迟范围</summary>
        public (int Min, int Max)? DelayRangeMs { get; set; }

        /// <summary>抖动范围</summary>
        public (double Min, double Max)? JitterRange { get; set; }

        /// <summary>每次滚动后的停顿范围</summary>
        public (int Min, int Max)? PauseRangeMs { get; set; }

        /// <summary>是否允许自动混入偶发探测滑/微调滑</summary>
        public bool EnableAutoMix { get; set; } = true;

        /// <summary>是否在向下滑时做顶部保护</summary>
        public bool EnableTopProtection { get; set; } = true;

        /// <summary>接近顶部阈值</summary>
        public int NearTopThresholdPx { get; set; } = 10;
    }
}
