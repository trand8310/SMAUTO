
namespace CefClient
{
    public enum DevicePlatform
    {
        Android,
        iPhone
    }

    public sealed class DeviceProfileResult
    {
        public DevicePlatform Platform { get; set; }

        // 输入物理分辨率，内部统一按竖屏保存：短边 x 长边
        public int PhysicalWidth { get; set; }
        public int PhysicalHeight { get; set; }

        // 用于 CDP / CEF / Playwright viewport 的 CSS 视口尺寸
        public int CssWidth { get; set; }
        public int CssHeight { get; set; }

        // 用于 deviceScaleFactor / window.devicePixelRatio
        public float DeviceScaleFactor { get; set; }

        // 物理分辨率 / CSS 视口反推出来的比例。
        // 注意：部分 iPhone Plus 老机型存在 downsample，DprX/DprY 可能不是最终 DeviceScaleFactor。
        public double DprX { get; set; }
        public double DprY { get; set; }

        public double Score { get; set; }

        public override string ToString()
        {
            return string.Format(
                "Platform={0}, Physical={1}x{2}, CSS={3}x{4}, DPR={5:F3}, DprX={6:F3}, DprY={7:F3}, Score={8:F6}",
                Platform,
                PhysicalWidth,
                PhysicalHeight,
                CssWidth,
                CssHeight,
                DeviceScaleFactor,
                DprX,
                DprY,
                Score
            );
        }
    }

    public static class ViewportMatcher
    {
        private sealed class DeviceProfile
        {
            public DevicePlatform Platform { get; private set; }
            public int CssW { get; private set; }
            public int CssH { get; private set; }

            // iPhone 建议固定 JS DPR：旧 SE/8 等为 2，新全面屏/Plus/Pro 为 3。
            // Android 不固定，按物理分辨率 / CSS viewport 反推后再归一化。
            public float? FixedDeviceScaleFactor { get; private set; }

            public DeviceProfile(DevicePlatform platform, int cssW, int cssH, float? fixedDeviceScaleFactor = null)
            {
                Platform = platform;
                CssW = cssW;
                CssH = cssH;
                FixedDeviceScaleFactor = fixedDeviceScaleFactor;
            }
        }

        // 主流 Android / iPhone CSS 档位。
        // 这里存的是竖屏 CSS viewport，不是物理分辨率。
        private static readonly DeviceProfile[] Profiles =
        {
            // =========================
            // Android
            // =========================
            new DeviceProfile(DevicePlatform.Android, 360, 640),
            new DeviceProfile(DevicePlatform.Android, 360, 720),
            new DeviceProfile(DevicePlatform.Android, 360, 740),
            new DeviceProfile(DevicePlatform.Android, 360, 760),
            new DeviceProfile(DevicePlatform.Android, 360, 780),
            new DeviceProfile(DevicePlatform.Android, 360, 800),

            new DeviceProfile(DevicePlatform.Android, 384, 854),

            new DeviceProfile(DevicePlatform.Android, 390, 844),
            new DeviceProfile(DevicePlatform.Android, 392, 872),
            new DeviceProfile(DevicePlatform.Android, 393, 851),
            new DeviceProfile(DevicePlatform.Android, 393, 873),

            new DeviceProfile(DevicePlatform.Android, 411, 891),
            new DeviceProfile(DevicePlatform.Android, 411, 914),

            new DeviceProfile(DevicePlatform.Android, 412, 869),
            new DeviceProfile(DevicePlatform.Android, 412, 891),
            new DeviceProfile(DevicePlatform.Android, 412, 915),

            new DeviceProfile(DevicePlatform.Android, 432, 960),

            // =========================
            // iPhone
            // Apple HIG / Safari CSS viewport 档位
            // =========================
            new DeviceProfile(DevicePlatform.iPhone, 320, 568, 2.0f),  // iPhone SE 1 / 5 / 5s
            new DeviceProfile(DevicePlatform.iPhone, 375, 667, 2.0f),  // 6 / 7 / 8 / SE2 / SE3
            new DeviceProfile(DevicePlatform.iPhone, 375, 812, 3.0f),  // X / XS / 11 Pro / 12 mini / 13 mini

            new DeviceProfile(DevicePlatform.iPhone, 390, 844, 3.0f),  // 12 / 12 Pro / 13 / 13 Pro / 14 / 16e / 17e
            new DeviceProfile(DevicePlatform.iPhone, 393, 852, 3.0f),  // 14 Pro / 15 / 15 Pro / 16
            new DeviceProfile(DevicePlatform.iPhone, 402, 874, 3.0f),  // 16 Pro / 17 / 17 Pro

            new DeviceProfile(DevicePlatform.iPhone, 414, 736, 3.0f),  // 6 Plus / 7 Plus / 8 Plus
            new DeviceProfile(DevicePlatform.iPhone, 414, 896, 3.0f),  // XR / 11 / XS Max / 11 Pro Max

            new DeviceProfile(DevicePlatform.iPhone, 420, 912, 3.0f),  // iPhone Air
            new DeviceProfile(DevicePlatform.iPhone, 428, 926, 3.0f),  // 12 Pro Max / 13 Pro Max / 14 Plus
            new DeviceProfile(DevicePlatform.iPhone, 430, 932, 3.0f),  // 14 Pro Max / 15 Plus / 15 Pro Max / 16 Plus
            new DeviceProfile(DevicePlatform.iPhone, 440, 956, 3.0f),  // 16 Pro Max / 17 Pro Max
        };

        // 常见 Android DPR。
        // 不建议只保留 3.0；很多 Android 机型会落在 2.625 / 2.75 / 2.8125。
        private static readonly float[] AndroidCommonDprs =
        {
            2.0f,
            2.25f,
            2.5f,
            2.625f,
            2.75f,
            2.8125f,
            3.0f,
            3.5f,
            3.75f,
            4.0f
        };

        // iPhone Web 侧常见 devicePixelRatio。
        // 老款非 Plus 为 2，新全面屏和 Plus/Pro 通常按 3 处理。
        private static readonly float[] IPhoneCommonDprs =
        {
            2.0f,
            3.0f
        };

        /// <summary>
        /// 自动匹配 Android / iPhone。
        /// 注意：真实业务里如果已经知道平台，优先使用 MatchAndroid / MatchIPhone，
        /// 不要靠分辨率猜平台。
        /// </summary>
        public static DeviceProfileResult Match(int width, int height)
        {
            return MatchInternal(width, height, null);
        }

        public static DeviceProfileResult MatchAndroid(int width, int height)
        {
            return MatchInternal(width, height, DevicePlatform.Android);
        }

        public static DeviceProfileResult MatchIPhone(int width, int height)
        {
            return MatchInternal(width, height, DevicePlatform.iPhone);
        }

        private static DeviceProfileResult MatchInternal(int width, int height, DevicePlatform? platformFilter)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException("width/height", "分辨率必须大于 0");

            int physicalWidth = Math.Min(width, height);
            int physicalHeight = Math.Max(width, height);

            DeviceProfileResult best = null;

            for (int i = 0; i < Profiles.Length; i++)
            {
                DeviceProfile p = Profiles[i];

                if (platformFilter.HasValue && p.Platform != platformFilter.Value)
                    continue;

                double dprX = (double)physicalWidth / p.CssW;
                double dprY = (double)physicalHeight / p.CssH;
                double avgDpr = (dprX + dprY) / 2.0;

                if (!IsReasonableDpr(p.Platform, avgDpr))
                    continue;

                float deviceScaleFactor = p.FixedDeviceScaleFactor.HasValue
                    ? p.FixedDeviceScaleFactor.Value
                    : NormalizeDpr(p.Platform, (float)avgDpr);

                // 主评分：横纵 DPR 越接近越好，说明这个 CSS 档位越能解释当前物理分辨率。
                double score = Math.Abs(dprX - dprY);

                // 轻微倾向常见宽度。
                score += GetWidthPenalty(p.Platform, p.CssW);

                // 轻微倾向常见 DPR。
                score += GetDprPenalty(p.Platform, avgDpr);

                var result = new DeviceProfileResult
                {
                    Platform = p.Platform,
                    PhysicalWidth = physicalWidth,
                    PhysicalHeight = physicalHeight,
                    CssWidth = p.CssW,
                    CssHeight = p.CssH,
                    DeviceScaleFactor = deviceScaleFactor,
                    DprX = dprX,
                    DprY = dprY,
                    Score = score
                };

                if (best == null || result.Score < best.Score)
                    best = result;
            }

            if (best != null)
                return best;

            return CreateFallback(physicalWidth, physicalHeight, platformFilter);
        }

        private static DeviceProfileResult CreateFallback(int physicalWidth, int physicalHeight, DevicePlatform? platformFilter)
        {
            if (platformFilter == DevicePlatform.iPhone)
                return CreateIPhoneFallback(physicalWidth, physicalHeight);

            if (platformFilter == DevicePlatform.Android)
                return CreateAndroidFallback(physicalWidth, physicalHeight);

            // 自动模式兜底：
            // 短边 <= 1320，且按 3x 反推后的 CSS 宽在 iPhone 常见区间内，优先给 iPhone。
            // 否则给 Android 360 宽档位。
            if (physicalWidth <= 1320)
            {
                int iphoneCssW = (int)Math.Round(physicalWidth / 3.0);
                int iphoneCssH = (int)Math.Round(physicalHeight / 3.0);

                if (iphoneCssW >= 320 && iphoneCssW <= 440)
                {
                    return new DeviceProfileResult
                    {
                        Platform = DevicePlatform.iPhone,
                        PhysicalWidth = physicalWidth,
                        PhysicalHeight = physicalHeight,
                        CssWidth = iphoneCssW,
                        CssHeight = iphoneCssH,
                        DeviceScaleFactor = 3.0f,
                        DprX = 3.0,
                        DprY = 3.0,
                        Score = 999
                    };
                }
            }

            return CreateAndroidFallback(physicalWidth, physicalHeight);
        }

        private static DeviceProfileResult CreateIPhoneFallback(int physicalWidth, int physicalHeight)
        {
            float dpr = physicalWidth <= 750 ? 2.0f : 3.0f;
            int cssWidth = (int)Math.Round(physicalWidth / dpr);
            int cssHeight = (int)Math.Round(physicalHeight / dpr);

            return new DeviceProfileResult
            {
                Platform = DevicePlatform.iPhone,
                PhysicalWidth = physicalWidth,
                PhysicalHeight = physicalHeight,
                CssWidth = cssWidth,
                CssHeight = cssHeight,
                DeviceScaleFactor = dpr,
                DprX = dpr,
                DprY = dpr,
                Score = 999
            };
        }

        private static DeviceProfileResult CreateAndroidFallback(int physicalWidth, int physicalHeight)
        {
            float rawDpr = (float)physicalWidth / 360f;
            float dpr = NormalizeDpr(DevicePlatform.Android, rawDpr);
            int cssHeight = (int)Math.Round(physicalHeight / dpr);

            return new DeviceProfileResult
            {
                Platform = DevicePlatform.Android,
                PhysicalWidth = physicalWidth,
                PhysicalHeight = physicalHeight,
                CssWidth = 360,
                CssHeight = cssHeight,
                DeviceScaleFactor = dpr,
                DprX = rawDpr,
                DprY = (double)physicalHeight / cssHeight,
                Score = 999
            };
        }

        private static bool IsReasonableDpr(DevicePlatform platform, double dpr)
        {
            if (platform == DevicePlatform.iPhone)
                return dpr >= 1.9 && dpr <= 3.25;

            return dpr >= 2.0 && dpr <= 4.1;
        }

        private static double GetWidthPenalty(DevicePlatform platform, int cssWidth)
        {
            if (platform == DevicePlatform.iPhone)
            {
                // 当前新机档位优先级稍高。
                if (cssWidth == 402) return 0.0000; // 16 Pro / 17 / 17 Pro
                if (cssWidth == 440) return 0.0003; // 16 Pro Max / 17 Pro Max
                if (cssWidth == 420) return 0.0005; // iPhone Air

                if (cssWidth == 393) return 0.0007;
                if (cssWidth == 390) return 0.0009;
                if (cssWidth == 430) return 0.0010;
                if (cssWidth == 428) return 0.0012;
                if (cssWidth == 414) return 0.0015;
                if (cssWidth == 375) return 0.0018;
                if (cssWidth == 320) return 0.0020;

                return 0.0030;
            }

            if (cssWidth == 360) return 0.0000;
            if (cssWidth == 393) return 0.0010;
            if (cssWidth == 392) return 0.0012;
            if (cssWidth == 390) return 0.0015;
            if (cssWidth == 412) return 0.0020;
            if (cssWidth == 411) return 0.0022;
            if (cssWidth == 384) return 0.0030;
            if (cssWidth == 432) return 0.0032;

            return 0.0050;
        }

        private static double GetDprPenalty(DevicePlatform platform, double dpr)
        {
            float[] commonDprs = platform == DevicePlatform.iPhone
                ? IPhoneCommonDprs
                : AndroidCommonDprs;

            double nearestDiff = commonDprs.Min(x => Math.Abs(x - dpr));
            return nearestDiff * 0.01;
        }

        private static float NormalizeDpr(DevicePlatform platform, float dpr)
        {
            float[] commonDprs = platform == DevicePlatform.iPhone
                ? IPhoneCommonDprs
                : AndroidCommonDprs;

            float nearest = commonDprs
                .OrderBy(x => Math.Abs(x - dpr))
                .First();

            if (Math.Abs(nearest - dpr) <= 0.08f)
                return nearest;

            return (float)Math.Round(dpr, 3, MidpointRounding.AwayFromZero);
        }
    }
}
