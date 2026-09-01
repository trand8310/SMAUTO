using System;

namespace PlaywrightHumanInput
{
    public sealed class HumanTouchSession
    {
        private readonly Random _random;

        public HumanTouchSession(HumanUserProfile? userProfile = null, TouchDeviceProfile? deviceProfile = null)
        {
            UserProfile = userProfile ?? HumanUserProfile.CreateRandom();
            DeviceProfile = deviceProfile ?? TouchDeviceProfile.GenericAndroid();
            _random = new Random(UserProfile.Seed);
            LastActionUtc = DateTime.UtcNow;
            PreferredVerticalXRatio = UserProfile.VerticalCenterXRatio;
            PreferredHorizontalYRatio = UserProfile.HorizontalCenterYRatio;
        }

        /// <summary>
        /// 直接使用设备参数中的 brand/model 创建 Session。
        /// desktopCdp=true 适用于 Windows Chromium + Playwright/CDP 模拟 Android 触屏。
        /// </summary>
        public HumanTouchSession(
            HumanUserProfile? userProfile,
            string? brand,
            string? model,
            bool desktopCdp = true)
            : this(
                userProfile,
                desktopCdp
                    ? TouchDeviceProfiles.ResolveForDesktopCdp(brand, model)
                    : TouchDeviceProfiles.Resolve(brand, model))
        {
        }

        public HumanUserProfile UserProfile { get; }
        public TouchDeviceProfile DeviceProfile { get; }
        public BrowseBehaviorState BehaviorState { get; set; } = BrowseBehaviorState.Observe;
        public SwipeIntent LastIntent { get; set; } = SwipeIntent.Preview;
        public HumanSwipeDirection LastDirection { get; set; } = HumanSwipeDirection.Up;
        public int SwipeCount { get; private set; }
        public int ConsecutiveUpCount { get; private set; }
        public int ConsecutiveDownCount { get; private set; }
        public double ShortFatigue { get; private set; }
        public double LongFatigue { get; private set; }
        public double Attention { get; private set; } = 0.78;
        public DateTime LastActionUtc { get; private set; }
        public double PreferredVerticalXRatio { get; private set; }
        public double PreferredHorizontalYRatio { get; private set; }
        public double LastLateralOffsetPx { get; set; }
        public double LastSpeedScale { get; private set; } = 1.0;
        public Random Random => _random;
        public double Fatigue => Math.Clamp(ShortFatigue * 0.72 + LongFatigue * 0.28, 0, 1);

        public void RecoverToNow()
        {
            var now = DateTime.UtcNow;
            double idle = Math.Max(0, (now - LastActionUtc).TotalSeconds);
            if (idle <= 0) return;

            double recovery = Math.Max(0.45, UserProfile.RecoveryBias);
            ShortFatigue *= Math.Exp(-idle / (18.0 / recovery));
            LongFatigue *= Math.Exp(-idle / (180.0 / recovery));
            Attention = Math.Clamp(Attention + (1.0 - Math.Exp(-idle / 12.0)) * 0.16, 0.15, 1.0);
        }

        public void RecordGesture(HumanSwipeTrace? trace)
        {
            RecoverToNow();
            LastActionUtc = DateTime.UtcNow;
            if (trace == null)
            {
                Attention = Math.Clamp(Attention - 0.025, 0.15, 1.0);
                return;
            }

            SwipeCount++;
            LastIntent = trace.Intent;
            LastDirection = trace.Direction;
            BehaviorState = trace.Intent switch
            {
                SwipeIntent.Reading => BrowseBehaviorState.Read,
                SwipeIntent.FastScan => BrowseBehaviorState.FastScan,
                SwipeIntent.BackReview => BrowseBehaviorState.BackReview,
                SwipeIntent.Preview => BrowseBehaviorState.Preview,
                SwipeIntent.Fling => BrowseBehaviorState.Preview,
                SwipeIntent.MicroAdjust => BrowseBehaviorState.Read,
                _ => BrowseBehaviorState.Observe
            };

            if (trace.Direction == HumanSwipeDirection.Up)
            {
                ConsecutiveUpCount++;
                ConsecutiveDownCount = 0;
            }
            else if (trace.Direction == HumanSwipeDirection.Down)
            {
                ConsecutiveDownCount++;
                ConsecutiveUpCount = 0;
            }
            else
            {
                ConsecutiveUpCount = 0;
                ConsecutiveDownCount = 0;
            }

            double intensity = trace.Mode switch
            {
                HumanSwipeMode.Fling => 0.030,
                HumanSwipeMode.Preview => 0.017,
                HumanSwipeMode.Reading => 0.008,
                HumanSwipeMode.Micro => 0.006,
                _ => 0.014
            };
            intensity *= UserProfile.FatigueSensitivity;
            ShortFatigue = Math.Clamp(ShortFatigue + intensity, 0, 1);
            LongFatigue = Math.Clamp(LongFatigue + intensity * 0.12, 0, 1);

            Attention = trace.Intent switch
            {
                SwipeIntent.Reading => Math.Clamp(Attention + 0.035, 0.15, 1.0),
                SwipeIntent.MicroAdjust => Math.Clamp(Attention + 0.025, 0.15, 1.0),
                SwipeIntent.FastScan => Math.Clamp(Attention - 0.045, 0.15, 1.0),
                _ => Math.Clamp(Attention - 0.008, 0.15, 1.0)
            };

            LastSpeedScale = Math.Clamp(0.82 * LastSpeedScale + 0.18 * Math.Max(0.4, trace.ReleaseVelocityPxPerSecond / 1800.0), 0.45, 1.8);
        }

        public void RecordObserve(TimeSpan duration)
        {
            // 应在真实观察/等待结束后调用。RecoverToNow() 使用实际经过时间恢复疲劳，
            // duration 这里只用于估算本次观察对注意力的提升，避免重复恢复。
            RecoverToNow();
            double seconds = Math.Max(0, duration.TotalSeconds);
            Attention = Math.Clamp(Attention + Math.Min(0.14, seconds * 0.025), 0.15, 1.0);
            BehaviorState = BrowseBehaviorState.Observe;
            LastActionUtc = DateTime.UtcNow;
        }

        public double NextPreferredVerticalXRatio()
        {
            double target = UserProfile.VerticalCenterXRatio;
            double slowNoise = RandomMath.Normal(_random, 0, 0.006);
            PreferredVerticalXRatio = Math.Clamp(0.94 * PreferredVerticalXRatio + 0.06 * target + slowNoise, 0.20, 0.80);
            return PreferredVerticalXRatio;
        }

        public double NextPreferredHorizontalYRatio()
        {
            double target = UserProfile.HorizontalCenterYRatio;
            PreferredHorizontalYRatio = Math.Clamp(0.94 * PreferredHorizontalYRatio + 0.06 * target + RandomMath.Normal(_random, 0, 0.006), 0.30, 0.78);
            return PreferredHorizontalYRatio;
        }
    }
}
