namespace PlaywrightHumanInput
{
    /// <summary>
    /// 用真实设备/真人轨迹统计值生成 Profile。
    /// 这里只保存统计参数，不保存个人原始轨迹。
    /// </summary>
    public sealed class HumanTouchCalibrationProfile
    {
        public double PreferredVerticalCenterXRatio { get; init; } = 0.63;
        public double PreferredHorizontalCenterYRatio { get; init; } = 0.56;
        public double StartPositionStdRatio { get; init; } = 0.055;

        public double SpeedBias { get; init; } = 1.0;
        public double DistanceBias { get; init; } = 1.0;
        public double CurveBias { get; init; } = 1.0;
        public double CurveAsymmetryBias { get; init; } = 1.0;
        public double DriftBias { get; init; } = 1.0;
        public double TremorBias { get; init; } = 1.0;
        public double SubmovementBias { get; init; } = 1.0;
        public double MotionPeakBias { get; init; } = 1.0;

        public double ForceBias { get; init; } = 1.0;
        public double TouchAreaBias { get; init; } = 1.0;
        public double PauseBias { get; init; } = 1.0;
        public double ReadingBias { get; init; } = 1.0;
        public double ScanBias { get; init; } = 1.0;

        public double MinSamplingHz { get; init; } = 80;
        public double MaxSamplingHz { get; init; } = 120;
        public double SamplingJitterRatio { get; init; } = 0.07;
        public double SamplingAutocorrelation { get; init; } = 0.58;
        public double CoalescedSampleChance { get; init; } = 0.025;

        public double PreferredTouchRadiusPx { get; init; } = 3.8;
        public double PreferredRotationDeg { get; init; } = 42;

        public HumanUserProfile CreateUserProfile(
            int seed,
            HumanHandedness handedness)
        {
            return new HumanUserProfile
            {
                Seed = seed,
                Handedness = handedness,
                VerticalCenterXRatio = PreferredVerticalCenterXRatio,
                HorizontalCenterYRatio = PreferredHorizontalCenterYRatio,
                StartPositionStdRatio = StartPositionStdRatio,

                SpeedBias = SpeedBias,
                DistanceBias = DistanceBias,
                CurveBias = CurveBias,
                CurveAsymmetryBias = CurveAsymmetryBias,
                DriftBias = DriftBias,
                TremorBias = TremorBias,
                SubmovementBias = SubmovementBias,
                MotionPeakBias = MotionPeakBias,

                ForceBias = ForceBias,
                TouchAreaBias = TouchAreaBias,
                PauseBias = PauseBias,
                ReadingBias = ReadingBias,
                ScanBias = ScanBias,

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
                SamplingAutocorrelation = SamplingAutocorrelation,
                CoalescedSampleChance = CoalescedSampleChance
            };
        }
    }
}
