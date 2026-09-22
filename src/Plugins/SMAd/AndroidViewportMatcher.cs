namespace SMAd
{
    using System;
    using System.Diagnostics;
    using System.Linq;

    public sealed class DeviceProfileResult
    {
        public int PhysicalWidth { get; set; }
        public int PhysicalHeight { get; set; }

        public int CssWidth { get; set; }
        public int CssHeight { get; set; }

        public float DeviceScaleFactor { get; set; }

        public double DprX { get; set; }
        public double DprY { get; set; }

        public double Score { get; set; }

        public bool IsSuspiciousInput { get; set; }
        public bool IsFallback { get; set; }

        public override string ToString()
        {
            return $"Physical={PhysicalWidth}x{PhysicalHeight}, CSS={CssWidth}x{CssHeight}, " +
                   $"DPR={DeviceScaleFactor:F3}, DprX={DprX:F4}, DprY={DprY:F4}, " +
                   $"Score={Score:F6}, Suspicious={IsSuspiciousInput}, Fallback={IsFallback}";
        }
    }

    public static class AndroidViewportMatcher
    {
        /*
         * 基准：
         *   1080 x 2400 -> 412 x 915 @ 2.625
         *
         * 说明：
         *   412 * 2.625 = 1081.5
         *   915 * 2.625 = 2401.875
         *
         * Android / Chromium 中 CSS viewport 与物理像素并不要求整数完全相除，
         * 所以允许很小的 rounding error。
         *
         * 设计原则：
         * 1. DPR 最大控制在 3.0。
         * 2. 2.625 作为首选基准 DPR。
         * 3. 分辨率越高，CSS viewport 越大，不再强行压到 360/390。
         * 4. 只在 CSS 尺寸明显不合理时，才切换到其它常见 DPR。
         */

        private static readonly float[] CandidateDprs =
        {
            2.625f, // 基准，最高优先级
            2.75f,
            3.0f,
            2.5f,
            2.25f,
            2.0f
        };

        private const float PreferredDpr = 2.625f;
        private const float MaxDpr = 3.0f;

        // 允许的手机 CSS viewport 范围。
        // 高分辨率设备允许更大的 CSS width。
        private const int MinCssWidth = 320;
        private const int MaxCssWidth = 560;

        private const int MinCssHeight = 640;
        private const int MaxCssHeight = 1280;

        // 手机屏幕纵横比（height / width）。
        private const double MinPhoneRatio = 1.75;
        private const double MaxPhoneRatio = 2.55;

        // 用于轻微贴近常见 Android viewport 宽度，不强制。
        private static readonly int[] CommonCssWidths =
        {
            360,
            384,
            390,
            393,
            400,
            411,
            412,
            420,
            432,
            448,
            480,
            512,
            540
        };

        public static DeviceProfileResult Match(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "分辨率必须大于 0");

            int physicalWidth = Math.Min(width, height);
            int physicalHeight = Math.Max(width, height);

            double physicalRatio = (double)physicalHeight / physicalWidth;
            bool suspiciousInput =
                physicalRatio < MinPhoneRatio ||
                physicalRatio > MaxPhoneRatio;

            DeviceProfileResult? best = null;

            foreach (float dpr in CandidateDprs)
            {
                if (dpr > MaxDpr)
                    continue;

                var candidate = BuildCandidate(
                    physicalWidth,
                    physicalHeight,
                    dpr,
                    suspiciousInput);

                if (candidate == null)
                    continue;

                if (best == null || candidate.Score < best.Score)
                    best = candidate;
            }

            if (best != null)
            {
                Debug.WriteLine("[AndroidViewportMatcher] Matched: " + best);
                return best;
            }

            // 极端输入的安全 fallback：
            // 仍然不超过 DPR 3.0。
            var fallback = BuildFallback(
                physicalWidth,
                physicalHeight,
                suspiciousInput);

            Debug.WriteLine("[AndroidViewportMatcher] Fallback: " + fallback);
            return fallback;
        }

        private static DeviceProfileResult? BuildCandidate(
            int physicalWidth,
            int physicalHeight,
            float dpr,
            bool suspiciousInput)
        {
            // 用真实分辨率 / DPR 得到基础 CSS 尺寸。
            double rawCssWidth = physicalWidth / (double)dpr;
            double rawCssHeight = physicalHeight / (double)dpr;

            // Chromium / Android viewport 本身可能有 1~2 px rounding，
            // 因此这里只做整数化，不要求严格整除。
            int cssWidth = (int)Math.Round(
                rawCssWidth,
                MidpointRounding.AwayFromZero);

            int cssHeight = (int)Math.Round(
                rawCssHeight,
                MidpointRounding.AwayFromZero);

            // 对 1080x2400 + 2.625 做基准校正：
            // raw 大约是 411.43 x 914.29，
            // 实机参考值是 412 x 915。
            //
            // 对其它分辨率采用同样原则：
            // 很接近常见 CSS width 时，允许向最近常见宽度贴 1~2px。
            cssWidth = SnapCssWidthIfVeryClose(cssWidth);

            // 高度按物理宽高比重新计算，避免宽度 snap 后 X/Y 比例失衡。
            double physicalRatio =
                (double)physicalHeight / physicalWidth;

            cssHeight = (int)Math.Round(
                cssWidth * physicalRatio,
                MidpointRounding.AwayFromZero);

            if (cssWidth < MinCssWidth ||
                cssWidth > MaxCssWidth ||
                cssHeight < MinCssHeight ||
                cssHeight > MaxCssHeight)
            {
                return null;
            }

            double cssRatio =
                (double)cssHeight / cssWidth;

            if (cssRatio < MinPhoneRatio ||
                cssRatio > MaxPhoneRatio)
            {
                return null;
            }

            double dprX =
                (double)physicalWidth / cssWidth;

            double dprY =
                (double)physicalHeight / cssHeight;

            double score = 0;

            // 1) 首要：X/Y 推导出的实际 DPR 应接近。
            score += Math.Abs(dprX - dprY) * 100.0;

            // 2) 2.625 为蓝本，优先选择接近它的 DPR。
            score += Math.Abs(dpr - PreferredDpr) * 8.0;

            // 3) 选择的 DPR 与实际推导 DPR 应接近。
            score += Math.Abs(dprX - dpr) * 4.0;
            score += Math.Abs(dprY - dpr) * 4.0;

            // 4) 只轻微偏向常见 CSS width。
            score += GetCssWidthPenalty(cssWidth);

            // 5) 分辨率越高允许 CSS 越大，不给大 CSS 额外惩罚。
            //    只要在合理手机区间即可。

            if (suspiciousInput)
                score += 25.0;

            return new DeviceProfileResult
            {
                PhysicalWidth = physicalWidth,
                PhysicalHeight = physicalHeight,

                CssWidth = cssWidth,
                CssHeight = cssHeight,

                DeviceScaleFactor = dpr,

                DprX = dprX,
                DprY = dprY,

                Score = score,

                IsSuspiciousInput = suspiciousInput,
                IsFallback = false
            };
        }

        private static int SnapCssWidthIfVeryClose(int cssWidth)
        {
            int nearest =
                CommonCssWidths
                    .OrderBy(x => Math.Abs(x - cssWidth))
                    .First();

            // 只允许非常小的 1~2px 校正。
            // 避免把高分辨率设备硬压到传统 360/390。
            if (Math.Abs(nearest - cssWidth) <= 2)
                return nearest;

            return cssWidth;
        }

        private static double GetCssWidthPenalty(int cssWidth)
        {
            int nearestDiff =
                CommonCssWidths.Min(x => Math.Abs(x - cssWidth));

            // 很弱的偏好，不能盖过真实物理尺寸。
            return nearestDiff * 0.002;
        }

        private static DeviceProfileResult BuildFallback(
            int physicalWidth,
            int physicalHeight,
            bool suspiciousInput)
        {
            // 优先用 DPR 3.0，保证永远 <= 3。
            float dpr = 3.0f;

            int cssWidth = (int)Math.Round(
                physicalWidth / (double)dpr,
                MidpointRounding.AwayFromZero);

            int cssHeight = (int)Math.Round(
                physicalHeight / (double)dpr,
                MidpointRounding.AwayFromZero);

            // 如果 CSS 太窄，则降低 DPR。
            while (cssWidth < MinCssWidth && dpr > 2.0f)
            {
                dpr -= 0.125f;

                cssWidth = (int)Math.Round(
                    physicalWidth / (double)dpr,
                    MidpointRounding.AwayFromZero);

                cssHeight = (int)Math.Round(
                    physicalHeight / (double)dpr,
                    MidpointRounding.AwayFromZero);
            }

            return new DeviceProfileResult
            {
                PhysicalWidth = physicalWidth,
                PhysicalHeight = physicalHeight,

                CssWidth = cssWidth,
                CssHeight = cssHeight,

                DeviceScaleFactor = dpr,

                DprX = (double)physicalWidth / cssWidth,
                DprY = (double)physicalHeight / cssHeight,

                Score = 9999.0,
                IsSuspiciousInput = suspiciousInput,
                IsFallback = true
            };
        }
    }
}
