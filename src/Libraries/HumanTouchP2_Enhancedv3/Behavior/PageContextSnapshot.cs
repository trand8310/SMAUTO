using System;

namespace PlaywrightHumanInput
{
    /// <summary>
    /// 轻量页面上下文。用于让“下一次为什么滑”与当前页面内容有一点关系。
    /// 它不是视觉识别模型，只做低成本 DOM/页面统计。
    /// </summary>
    public sealed class PageContextSnapshot
    {
        public int TextChars { get; set; }
        public int Images { get; set; }
        public int Videos { get; set; }
        public int Links { get; set; }
        public int Buttons { get; set; }
        public int Inputs { get; set; }

        public double ScrollTop { get; set; }
        public double ScrollHeight { get; set; }
        public double ClientHeight { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }

        public double ScrollProgress
        {
            get
            {
                double range = Math.Max(1, ScrollHeight - ClientHeight);
                return Math.Clamp(ScrollTop / range, 0, 1);
            }
        }

        public bool IsNearTop => ScrollTop <= Math.Max(12, ClientHeight * 0.025);

        public bool IsNearBottom
            => ScrollTop + ClientHeight >= ScrollHeight - Math.Max(16, ClientHeight * 0.035);

        public double TextDensityScore
        {
            get
            {
                // 约 3500 字符以上视作明显文本型页面。
                return Math.Clamp(TextChars / 3500.0, 0, 1);
            }
        }

        public double MediaDensityScore
        {
            get
            {
                double weighted = Images + Videos * 3.0;
                return Math.Clamp(weighted / 18.0, 0, 1);
            }
        }

        public double InteractiveDensityScore
        {
            get
            {
                double weighted = Links * 0.45 + Buttons + Inputs * 1.2;
                return Math.Clamp(weighted / 28.0, 0, 1);
            }
        }
    }
}
