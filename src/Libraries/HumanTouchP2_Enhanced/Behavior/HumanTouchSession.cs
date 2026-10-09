using System;
using System.Security.Cryptography;

namespace PlaywrightHumanInput
{
    /// <summary>
    /// 一次连续浏览任务的状态。
    /// UserProfile 保持“人”的长期一致性；SessionSeed 保证每次运行不会复播相同随机序列。
    /// </summary>
    public sealed class HumanTouchSession
    {
        private readonly Random _random;
        private readonly TimeProvider _timeProvider;
        private long _lastRecoveryTimestamp;
        private double _speedDrift;
        private double _forceDrift;

        public HumanTouchSession(
            HumanUserProfile? userProfile = null,
            TouchDeviceProfile? deviceProfile = null,
            int? sessionSeed = null)
            : this(userProfile, deviceProfile, sessionSeed, TimeProvider.System)
        {
        }

        public HumanTouchSession(
            HumanUserProfile? userProfile,
            TouchDeviceProfile? deviceProfile,
            int? sessionSeed,
            TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _lastRecoveryTimestamp = _timeProvider.GetTimestamp();
            UserProfile = userProfile ?? HumanUserProfile.CreateRandom();
            DeviceProfile = deviceProfile ?? TouchDeviceProfile.GenericAndroid();
            SessionSeed = sessionSeed ?? CreateSessionSeed(UserProfile.Seed);
            _random = new Random(SessionSeed);

            LastActionUtc = _timeProvider.GetUtcNow().UtcDateTime;
            PreferredVerticalXRatio = UserProfile.VerticalCenterXRatio;
            PreferredHorizontalYRatio = UserProfile.HorizontalCenterYRatio;

            // 这些是一次会话内缓慢变化的潜变量，不属于长期画像。
            _speedDrift = RandomMath.Normal(_random, 0, 0.025);
            _forceDrift = RandomMath.Normal(_random, 0, 0.025);
        }

        public HumanTouchSession(
            HumanUserProfile? userProfile,
            string? brand,
            string? model,
            bool desktopCdp = true,
            int? sessionSeed = null)
            : this(
                userProfile,
                desktopCdp
                    ? TouchDeviceProfiles.ResolveForDesktopCdp(brand, model)
                    : TouchDeviceProfiles.Resolve(brand, model),
                sessionSeed)
        {
        }

        public HumanUserProfile UserProfile { get; }
        public TouchDeviceProfile DeviceProfile { get; }
        public int SessionSeed { get; }

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
            var now = _timeProvider.GetTimestamp();
            double idle = Math.Max(0, _timeProvider.GetElapsedTime(_lastRecoveryTimestamp, now).TotalSeconds);
            if (idle <= 0) return;
            // 独立记录已结算的时间，保留 LastActionUtc 的“最近动作”语义。
            _lastRecoveryTimestamp = now;

            double recovery = Math.Max(0.45, UserProfile.RecoveryBias);
            ShortFatigue *= Math.Exp(-idle / (18.0 / recovery));
            LongFatigue *= Math.Exp(-idle / (180.0 / recovery));
            Attention = Math.Clamp(
                Attention + (1.0 - Math.Exp(-idle / 12.0)) * 0.16,
                0.15,
                1.0);
        }

        public void RecordGesture(HumanSwipeTrace? trace)
        {
            RecoverToNow();
            LastActionUtc = _timeProvider.GetUtcNow().UtcDateTime;

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

            LastSpeedScale = Math.Clamp(
                0.82 * LastSpeedScale +
                0.18 * Math.Max(0.4, trace.ReleaseVelocityPxPerSecond / 1800.0),
                0.45,
                1.8);
        }

        public void RecordObserve(TimeSpan duration)
        {
            RecoverToNow();
            double seconds = Math.Max(0, duration.TotalSeconds);
            Attention = Math.Clamp(
                Attention + Math.Min(0.14, seconds * 0.025),
                0.15,
                1.0);
            BehaviorState = BrowseBehaviorState.Observe;
            LastActionUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        public double NextPreferredVerticalXRatio()
        {
            double target = UserProfile.VerticalCenterXRatio;
            double slowNoise = RandomMath.Normal(_random, 0, 0.006);
            PreferredVerticalXRatio = Math.Clamp(
                0.94 * PreferredVerticalXRatio + 0.06 * target + slowNoise,
                0.20,
                0.80);
            return PreferredVerticalXRatio;
        }

        public double NextPreferredHorizontalYRatio()
        {
            double target = UserProfile.HorizontalCenterYRatio;
            PreferredHorizontalYRatio = Math.Clamp(
                0.94 * PreferredHorizontalYRatio +
                0.06 * target +
                RandomMath.Normal(_random, 0, 0.006),
                0.30,
                0.78);
            return PreferredHorizontalYRatio;
        }

        /// <summary>一次会话中缓慢漂移的速度状态，避免每次手势完全独立抽样。</summary>
        public double NextSpeedDrift()
        {
            _speedDrift = Math.Clamp(
                _speedDrift * 0.88 + RandomMath.Normal(_random, 0, 0.018) * 0.12,
                -0.10,
                0.10);
            return 1.0 + _speedDrift;
        }

        /// <summary>一次会话中缓慢漂移的按压力状态。</summary>
        public double NextForceDrift()
        {
            _forceDrift = Math.Clamp(
                _forceDrift * 0.90 + RandomMath.Normal(_random, 0, 0.016) * 0.10,
                -0.08,
                0.08);
            return 1.0 + _forceDrift;
        }

        private static int CreateSessionSeed(int profileSeed)
        {
            int entropy = RandomNumberGenerator.GetInt32(1, int.MaxValue);
            unchecked
            {
                int mixed = entropy ^ (profileSeed * 397) ^ Environment.TickCount;
                mixed &= 0x7FFFFFFF;
                return mixed == 0 ? entropy : mixed;
            }
        }
    }
}
