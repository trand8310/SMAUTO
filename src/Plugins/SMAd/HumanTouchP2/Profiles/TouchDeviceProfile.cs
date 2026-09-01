using System;

namespace PlaywrightHumanInput
{
    public enum TouchDeviceProfileSource
    {
        GenericAndroid,
        BrandBaseline,
        ExactModel,
        DesktopCdp,
        Calibrated
    }

    /// <summary>
    /// 描述“设备/输入链路”的触摸采样特征。
    /// HumanUserProfile 描述人；TouchDeviceProfile 描述设备，两者不要混在一起。
    /// </summary>
    public sealed class TouchDeviceProfile
    {
        public string ProfileId { get; init; } = "generic-android";

        /// <summary>标准化后的品牌名称，例如 Xiaomi / Honor / Samsung / Huawei / vivo / OPPO。</summary>
        public string Brand { get; init; } = "Generic";

        /// <summary>设备型号。可以是营销型号，也可以是系统返回的 model code，例如 SM-S9380。</summary>
        public string Model { get; init; } = "";

        /// <summary>本 Profile 来自 Generic、品牌基线、精确型号、Desktop CDP 适配还是实测校准。</summary>
        public TouchDeviceProfileSource Source { get; init; } = TouchDeviceProfileSource.GenericAndroid;

        public int MaxTouchPoints { get; init; } = 5;
        public double MinSamplingHz { get; init; } = 80;
        public double MaxSamplingHz { get; init; } = 120;
        public double SamplingJitterRatio { get; init; } = 0.07;
        public double CoalescedSampleChance { get; init; } = 0.025;
        public bool SupportsForce { get; init; } = true;
        public bool SupportsTouchArea { get; init; } = true;
        public bool SupportsRotationAngle { get; init; } = true;
        public double ForceScale { get; init; } = 1.0;
        public double RadiusScale { get; init; } = 1.0;
        public double RotationScale { get; init; } = 1.0;
        public double TimingNoiseMs { get; init; } = 0.55;
        public double InputLatencyMs { get; init; } = 1.2;

        public string DeviceKey => TouchDeviceProfiles.BuildDeviceKey(Brand, Model);

        public static TouchDeviceProfile GenericAndroid() => new();

        /// <summary>
        /// 根据设备参数中的 brand/model 解析设备 Profile。
        /// 精确型号 -> 品牌基线 -> GenericAndroid。
        /// </summary>
        public static TouchDeviceProfile ForDevice(string? brand, string? model)
            => TouchDeviceProfiles.Resolve(brand, model);

        /// <summary>
        /// 适合 Windows Chromium + CDP 的默认配置。
        /// </summary>
        public static TouchDeviceProfile ConservativeDesktopEmulation() => new()
        {
            ProfileId = "desktop-cdp-conservative",
            Brand = "Generic",
            Model = "Desktop-CDP",
            Source = TouchDeviceProfileSource.DesktopCdp,
            MinSamplingHz = 60,
            MaxSamplingHz = 90,
            SamplingJitterRatio = 0.045,
            CoalescedSampleChance = 0.015,
            TimingNoiseMs = 0.35,
            InputLatencyMs = 0.8
        };

        /// <summary>
        /// Windows Chromium + CDP 环境下，根据 Android brand/model 创建 Profile。
        /// 品牌/型号和触点能力来自 Android Profile，事件采样时序使用较保守的 Desktop-CDP 包络。
        /// </summary>
        public static TouchDeviceProfile ConservativeDesktopEmulation(string? brand, string? model)
            => TouchDeviceProfiles.ResolveForDesktopCdp(brand, model);
    }
}
