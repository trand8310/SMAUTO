namespace PlaywrightHumanInput
{
    // 用真实设备/真人轨迹统计值生成 Profile。这里存统计分布参数，不存原始个人轨迹。
    public sealed class HumanTouchCalibrationProfile
    {
        public double PreferredVerticalCenterXRatio { get; init; } = 0.63;
        public double StartPositionStdRatio { get; init; } = 0.055;
        public double SpeedBias { get; init; } = 1.0;
        public double DistanceBias { get; init; } = 1.0;
        public double CurveBias { get; init; } = 1.0;
        public double DriftBias { get; init; } = 1.0;
        public double TremorBias { get; init; } = 1.0;
        public double ForceBias { get; init; } = 1.0;
        public double TouchAreaBias { get; init; } = 1.0;
        public double PauseBias { get; init; } = 1.0;
        public double MinSamplingHz { get; init; } = 80;
        public double MaxSamplingHz { get; init; } = 120;
        public double SamplingJitterRatio { get; init; } = 0.07;
        public double CoalescedSampleChance { get; init; } = 0.025;
        public double PreferredTouchRadiusPx { get; init; } = 3.8;
        public double PreferredRotationDeg { get; init; } = 42;

        public HumanUserProfile CreateUserProfile(int seed, HumanHandedness handedness)
        {
            return new HumanUserProfile
            {
                Seed = seed,
                Handedness = handedness,
                VerticalCenterXRatio = PreferredVerticalCenterXRatio,
                StartPositionStdRatio = StartPositionStdRatio,
                SpeedBias = SpeedBias,
                DistanceBias = DistanceBias,
                CurveBias = CurveBias,
                DriftBias = DriftBias,
                TremorBias = TremorBias,
                ForceBias = ForceBias,
                TouchAreaBias = TouchAreaBias,
                PauseBias = PauseBias,
                PreferredTouchRadiusPx = PreferredTouchRadiusPx,
                PreferredRotationDeg = PreferredRotationDeg
            };
        }

        public TouchDeviceProfile CreateDeviceProfile(
            string profileId = "calibrated-device",
            string brand = "Generic",
            string model = "")
        {
            return new TouchDeviceProfile
            {
                ProfileId = profileId,
                Brand = TouchDeviceProfiles.NormalizeBrand(brand),
                Model = model?.Trim() ?? string.Empty,
                Source = TouchDeviceProfileSource.Calibrated,
                MinSamplingHz = MinSamplingHz,
                MaxSamplingHz = MaxSamplingHz,
                SamplingJitterRatio = SamplingJitterRatio,
                CoalescedSampleChance = CoalescedSampleChance
            };
        }
    }
}
