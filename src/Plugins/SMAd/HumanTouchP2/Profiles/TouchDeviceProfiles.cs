using System;
using System.Collections.Generic;
using System.Text;

namespace PlaywrightHumanInput
{
    /// <summary>
    /// brand + model 双键设备 Profile 解析器。
    ///
    /// 解析顺序：
    /// 1. 运行时注册的精确 Brand+Model Profile；
    /// 2. 内置 Brand 基线；
    /// 3. GenericAndroid。
    ///
    /// 注意：内置品牌参数是“工程型保守基线”，不是厂商触摸 IC 的实测声明。
    /// 真正要求型号级一致性时，请使用 RegisterModelProfile 注册实测/校准参数。
    /// </summary>
    public static class TouchDeviceProfiles
    {
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<string, TouchDeviceProfile> ExactModels =
            new(StringComparer.OrdinalIgnoreCase);

        public static TouchDeviceProfile Resolve(string? brand, string? model)
        {
            string canonicalBrand = NormalizeBrand(brand);
            string cleanModel = NormalizeModelDisplay(model);
            string key = BuildDeviceKey(canonicalBrand, cleanModel);

            if (!string.IsNullOrWhiteSpace(cleanModel))
            {
                lock (SyncRoot)
                {
                    if (ExactModels.TryGetValue(key, out var exact))
                        return Clone(exact,
                            brand: canonicalBrand,
                            model: cleanModel,
                            sourceType: TouchDeviceProfileSource.ExactModel,
                            profileId: BuildProfileId("model", canonicalBrand, cleanModel));
                }
            }

            return CreateBrandBaseline(canonicalBrand, cleanModel);
        }

        /// <summary>
        /// 针对 Windows Chromium + Playwright/CDP 的品牌/型号解析。
        /// 保留 brand/model、触点面积/压力/旋转等设备特征；
        /// 只把采样频率、采样抖动、合并概率和输入时延限制到 Desktop-CDP 的保守范围。
        /// </summary>
        public static TouchDeviceProfile ResolveForDesktopCdp(string? brand, string? model)
        {
            var native = Resolve(brand, model);
            var desktop = TouchDeviceProfile.ConservativeDesktopEmulation();

            return Clone(native,
                brand: native.Brand,
                model: native.Model,
                sourceType: TouchDeviceProfileSource.DesktopCdp,
                profileId: BuildProfileId("desktop-cdp", native.Brand, native.Model),
                minSamplingHz: Math.Min(native.MinSamplingHz, desktop.MinSamplingHz),
                maxSamplingHz: Math.Min(native.MaxSamplingHz, desktop.MaxSamplingHz),
                samplingJitterRatio: Math.Max(native.SamplingJitterRatio * 0.60, desktop.SamplingJitterRatio),
                coalescedSampleChance: Math.Max(native.CoalescedSampleChance * 0.65, desktop.CoalescedSampleChance),
                timingNoiseMs: Math.Max(native.TimingNoiseMs * 0.65, desktop.TimingNoiseMs),
                inputLatencyMs: Math.Max(native.InputLatencyMs * 0.70, desktop.InputLatencyMs));
        }

        /// <summary>
        /// 注册精确 brand+model Profile。适合把你后续采集的真机统计参数注册进来。
        /// </summary>
        public static void RegisterModelProfile(TouchDeviceProfile profile, bool overwrite = true)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            string brand = NormalizeBrand(profile.Brand);
            string model = NormalizeModelDisplay(profile.Model);

            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentException("精确型号 Profile 必须提供 Model。", nameof(profile));

            string key = BuildDeviceKey(brand, model);
            var normalized = Clone(profile,
                brand: brand,
                model: model,
                sourceType: TouchDeviceProfileSource.ExactModel,
                profileId: string.IsNullOrWhiteSpace(profile.ProfileId)
                    ? BuildProfileId("model", brand, model)
                    : profile.ProfileId);

            lock (SyncRoot)
            {
                if (!overwrite && ExactModels.ContainsKey(key))
                    throw new InvalidOperationException($"设备 Profile 已存在: {brand} / {model}");

                ExactModels[key] = normalized;
            }
        }

        public static bool TryGetRegisteredModel(string? brand, string? model, out TouchDeviceProfile? profile)
        {
            string key = BuildDeviceKey(brand, model);
            lock (SyncRoot)
            {
                if (ExactModels.TryGetValue(key, out var value))
                {
                    profile = value;
                    return true;
                }
            }

            profile = null;
            return false;
        }

        public static bool RemoveRegisteredModel(string? brand, string? model)
        {
            string key = BuildDeviceKey(brand, model);
            lock (SyncRoot)
                return ExactModels.Remove(key);
        }

        public static string BuildDeviceKey(string? brand, string? model)
        {
            return $"{NormalizeBrandKey(NormalizeBrand(brand))}|{NormalizeModelKey(model)}";
        }

        public static string NormalizeBrand(string? brand)
        {
            string value = (brand ?? string.Empty).Trim();
            if (value.Length == 0)
                return "Generic";

            string key = NormalizeBrandKey(value);

            // 子品牌沿用母品牌输入基线；Model 仍保留，可注册精确型号覆盖。
            return key switch
            {
                "xiaomi" or "mi" or "redmi" or "poco" => "Xiaomi",
                "honor" => "Honor",
                "samsung" or "samsung electronics" => "Samsung",
                "huawei" => "Huawei",
                "vivo" or "iqoo" => "vivo",
                "oppo" or "realme" or "oneplus" => "OPPO",
                _ => value
            };
        }

        private static TouchDeviceProfile CreateBrandBaseline(string canonicalBrand, string model)
        {
            string key = NormalizeBrandKey(canonicalBrand);

            // 这些值只作为工程默认分布，用于 brand 间保持轻微但稳定的设备差异。
            // 有真机统计时应通过 RegisterModelProfile 覆盖。
            return key switch
            {
                "xiaomi" => new TouchDeviceProfile
                {
                    ProfileId = BuildProfileId("brand", "Xiaomi", model),
                    Brand = "Xiaomi",
                    Model = model,
                    Source = TouchDeviceProfileSource.BrandBaseline,
                    MaxTouchPoints = 10,
                    MinSamplingHz = 80,
                    MaxSamplingHz = 110,
                    SamplingJitterRatio = 0.050,
                    CoalescedSampleChance = 0.020,
                    ForceScale = 1.00,
                    RadiusScale = 1.00,
                    RotationScale = 1.00,
                    TimingNoiseMs = 0.50,
                    InputLatencyMs = 1.10
                },

                "honor" => new TouchDeviceProfile
                {
                    ProfileId = BuildProfileId("brand", "Honor", model),
                    Brand = "Honor",
                    Model = model,
                    Source = TouchDeviceProfileSource.BrandBaseline,
                    MaxTouchPoints = 10,
                    MinSamplingHz = 80,
                    MaxSamplingHz = 112,
                    SamplingJitterRatio = 0.048,
                    CoalescedSampleChance = 0.019,
                    ForceScale = 1.00,
                    RadiusScale = 1.01,
                    RotationScale = 1.00,
                    TimingNoiseMs = 0.48,
                    InputLatencyMs = 1.08
                },

                "samsung" => new TouchDeviceProfile
                {
                    ProfileId = BuildProfileId("brand", "Samsung", model),
                    Brand = "Samsung",
                    Model = model,
                    Source = TouchDeviceProfileSource.BrandBaseline,
                    MaxTouchPoints = 10,
                    MinSamplingHz = 85,
                    MaxSamplingHz = 120,
                    SamplingJitterRatio = 0.044,
                    CoalescedSampleChance = 0.017,
                    ForceScale = 1.00,
                    RadiusScale = 0.98,
                    RotationScale = 1.00,
                    TimingNoiseMs = 0.44,
                    InputLatencyMs = 1.00
                },

                "huawei" => new TouchDeviceProfile
                {
                    ProfileId = BuildProfileId("brand", "Huawei", model),
                    Brand = "Huawei",
                    Model = model,
                    Source = TouchDeviceProfileSource.BrandBaseline,
                    MaxTouchPoints = 10,
                    MinSamplingHz = 80,
                    MaxSamplingHz = 112,
                    SamplingJitterRatio = 0.049,
                    CoalescedSampleChance = 0.020,
                    ForceScale = 1.00,
                    RadiusScale = 1.02,
                    RotationScale = 1.00,
                    TimingNoiseMs = 0.50,
                    InputLatencyMs = 1.10
                },

                "vivo" => new TouchDeviceProfile
                {
                    ProfileId = BuildProfileId("brand", "vivo", model),
                    Brand = "vivo",
                    Model = model,
                    Source = TouchDeviceProfileSource.BrandBaseline,
                    MaxTouchPoints = 10,
                    MinSamplingHz = 84,
                    MaxSamplingHz = 118,
                    SamplingJitterRatio = 0.045,
                    CoalescedSampleChance = 0.018,
                    ForceScale = 1.00,
                    RadiusScale = 1.00,
                    RotationScale = 1.00,
                    TimingNoiseMs = 0.46,
                    InputLatencyMs = 1.03
                },

                "oppo" => new TouchDeviceProfile
                {
                    ProfileId = BuildProfileId("brand", "OPPO", model),
                    Brand = "OPPO",
                    Model = model,
                    Source = TouchDeviceProfileSource.BrandBaseline,
                    MaxTouchPoints = 10,
                    MinSamplingHz = 82,
                    MaxSamplingHz = 116,
                    SamplingJitterRatio = 0.047,
                    CoalescedSampleChance = 0.019,
                    ForceScale = 1.00,
                    RadiusScale = 1.00,
                    RotationScale = 1.00,
                    TimingNoiseMs = 0.47,
                    InputLatencyMs = 1.05
                },

                _ => new TouchDeviceProfile
                {
                    ProfileId = BuildProfileId("generic", canonicalBrand, model),
                    Brand = canonicalBrand,
                    Model = model,
                    Source = TouchDeviceProfileSource.GenericAndroid
                }
            };
        }

        private static TouchDeviceProfile Clone(
            TouchDeviceProfile source,
            string? brand = null,
            string? model = null,
            TouchDeviceProfileSource? sourceType = null,
            string? profileId = null,
            double? minSamplingHz = null,
            double? maxSamplingHz = null,
            double? samplingJitterRatio = null,
            double? coalescedSampleChance = null,
            double? timingNoiseMs = null,
            double? inputLatencyMs = null)
        {
            return new TouchDeviceProfile
            {
                ProfileId = profileId ?? source.ProfileId,
                Brand = brand ?? source.Brand,
                Model = model ?? source.Model,
                Source = sourceType ?? source.Source,
                MaxTouchPoints = source.MaxTouchPoints,
                MinSamplingHz = minSamplingHz ?? source.MinSamplingHz,
                MaxSamplingHz = maxSamplingHz ?? source.MaxSamplingHz,
                SamplingJitterRatio = samplingJitterRatio ?? source.SamplingJitterRatio,
                CoalescedSampleChance = coalescedSampleChance ?? source.CoalescedSampleChance,
                SupportsForce = source.SupportsForce,
                SupportsTouchArea = source.SupportsTouchArea,
                SupportsRotationAngle = source.SupportsRotationAngle,
                ForceScale = source.ForceScale,
                RadiusScale = source.RadiusScale,
                RotationScale = source.RotationScale,
                TimingNoiseMs = timingNoiseMs ?? source.TimingNoiseMs,
                InputLatencyMs = inputLatencyMs ?? source.InputLatencyMs
            };
        }

        private static string NormalizeBrandKey(string? brand)
        {
            string value = (brand ?? string.Empty).Trim().ToLowerInvariant();
            var sb = new StringBuilder(value.Length);
            bool lastSpace = false;

            foreach (char c in value)
            {
                bool space = char.IsWhiteSpace(c) || c == '_' || c == '-';
                if (space)
                {
                    if (!lastSpace && sb.Length > 0)
                        sb.Append(' ');
                    lastSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastSpace = false;
                }
            }

            return sb.ToString().Trim();
        }

        private static string NormalizeModelDisplay(string? model)
        {
            string value = (model ?? string.Empty).Trim();
            if (value.Length == 0)
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            bool lastSpace = false;
            foreach (char c in value)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastSpace && sb.Length > 0)
                        sb.Append(' ');
                    lastSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastSpace = false;
                }
            }
            return sb.ToString().Trim();
        }

        private static string NormalizeModelKey(string? model)
            => NormalizeModelDisplay(model).ToLowerInvariant();

        private static string BuildProfileId(string prefix, string brand, string model)
        {
            static string clean(string value)
            {
                var sb = new StringBuilder(value.Length);
                foreach (char c in value.ToLowerInvariant())
                {
                    if (char.IsLetterOrDigit(c))
                        sb.Append(c);
                    else if (sb.Length > 0 && sb[^1] != '-')
                        sb.Append('-');
                }
                return sb.ToString().Trim('-');
            }

            string b = clean(brand);
            string m = clean(model);
            return string.IsNullOrEmpty(m) ? $"{prefix}-{b}" : $"{prefix}-{b}-{m}";
        }
    }
}
