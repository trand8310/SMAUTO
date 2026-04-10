

namespace SMAd
{
    using System;
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

        public override string ToString()
        {
            return $"Physical={PhysicalWidth}x{PhysicalHeight}, CSS={CssWidth}x{CssHeight}, DPR={DeviceScaleFactor:F3}, Score={Score:F6}";
        }
    }

    public static class AndroidViewportMatcher
    {
        // 主流国内安卓 CSS 档位
        private static readonly (int CssW, int CssH)[] Profiles =
        {
        (360, 800),
        (360, 780),
        (360, 760),
        (360, 740),

        (393, 851),
        (393, 873),
        (392, 872),
        (390, 844),

        (412, 915),
        (412, 891),
        (412, 869),

        (384, 854),
        (411, 914),
    };

        // 常见安卓 DPR 档位
        private static readonly float[] CommonDprs =
        {
        2.5f,
        2.625f,
        2.75f,
        3.0f,
        3.5f
    };

        public static DeviceProfileResult Match(int width, int height)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException("分辨率必须大于 0");

            // 统一成竖屏逻辑
            int physicalWidth = Math.Min(width, height);
            int physicalHeight = Math.Max(width, height);

            DeviceProfileResult? best = null;

            foreach (var p in Profiles)
            {
                double dprX = (double)physicalWidth / p.CssW;
                double dprY = (double)physicalHeight / p.CssH;
                double avgDpr = (dprX + dprY) / 2.0;

                // 过滤明显不合理的 DPR
                if (avgDpr < 2.4 || avgDpr > 3.6)
                    continue;

                // 分数 1：横纵 DPR 差异越小越好
                double score = Math.Abs(dprX - dprY);

                // 分数 2：轻微偏向主流宽度
                // 360 > 393 > 412 > 其他
                score += GetWidthPenalty(p.CssW);

                // 分数 3：轻微偏向常见 DPR
                score += GetDprPenalty(avgDpr);

                var result = new DeviceProfileResult
                {
                    PhysicalWidth = physicalWidth,
                    PhysicalHeight = physicalHeight,
                    CssWidth = p.CssW,
                    CssHeight = p.CssH,
                    DeviceScaleFactor = NormalizeDpr((float)avgDpr),
                    DprX = dprX,
                    DprY = dprY,
                    Score = score
                };

                if (best == null || result.Score < best.Score)
                    best = result;
            }

            if (best == null)
            {
                // 兜底：按最常见 360 宽去算
                float dpr = (float)physicalWidth / 360f;
                int cssHeight = (int)Math.Round(physicalHeight / dpr);

                return new DeviceProfileResult
                {
                    PhysicalWidth = physicalWidth,
                    PhysicalHeight = physicalHeight,
                    CssWidth = 360,
                    CssHeight = cssHeight,
                    DeviceScaleFactor = NormalizeDpr(dpr),
                    DprX = dpr,
                    DprY = dpr,
                    Score = 999
                };
            }

            return best;
        }

        private static double GetWidthPenalty(int cssWidth)
        {
            return cssWidth switch
            {
                360 => 0.0000,
                393 => 0.0010,
                392 => 0.0012,
                390 => 0.0015,
                412 => 0.0020,
                411 => 0.0022,
                384 => 0.0030,
                _ => 0.0050
            };
        }

        private static double GetDprPenalty(double dpr)
        {
            double nearest = CommonDprs.Min(x => Math.Abs(x - dpr));
            return nearest * 0.01; // 很轻的惩罚，不抢主评分权重
        }

        private static float NormalizeDpr(float dpr)
        {
            // 足够接近主流档位时，吸附成标准值
            foreach (var common in CommonDprs)
            {
                if (Math.Abs(dpr - common) <= 0.08f)
                    return common;
            }

            // 否则保留三位小数
            return (float)Math.Round(dpr, 3, MidpointRounding.AwayFromZero);
        }
    }
}
