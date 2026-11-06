using Newtonsoft.Json.Linq;
using QTP.Common;

namespace SMAd;

/// <summary>Validate device data before proxy preparation and browser startup.</summary>
public static class DeviceValidation
{
    public static bool TryPrepare(JToken token, int os, out string reason, out bool repaired)
    {
        repaired = false;
        if (token is not JObject dev) { reason = "设备数据不是对象"; return false; }
        if (os is not (1 or 2)) { reason = "任务不是手机系统"; return false; }
        var ua = dev.Value<string>("ua") ?? "";
        if (string.IsNullOrWhiteSpace(ua)) { reason = "缺少设备 UA"; return false; }
        if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) || ua.Contains("tablet", StringComparison.OrdinalIgnoreCase))
        { reason = "设备标识为平板"; return false; }
        if (!PositiveInt(dev["sw"], out var sw) || !PositiveInt(dev["sh"], out var sh))
        { reason = "屏幕物理尺寸缺失或无效"; return false; }
        var profile = AndroidViewportMatcher.Match(sw, sh, dev.Value<string>("make") ?? "", dev.Value<string>("model") ?? "");
        double width = profile.CssWidth, height = profile.CssHeight;
        var scaleToken = dev["dpr"]?.Type == JTokenType.Null ? dev["ratio"] : dev["dpr"] ?? dev["ratio"];
        if (dev["ratio"] is { Type: not JTokenType.Null } ratio &&
            (!double.TryParse(ratio.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ratioValue) || !double.IsFinite(ratioValue) || ratioValue < 0))
        { reason = "屏幕缩放比例无效"; return false; }
        if (dev["dpr"] is { Type: not JTokenType.Null } dpr &&
            (!double.TryParse(dpr.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var explicitScale) || !double.IsFinite(explicitScale) || explicitScale <= 0))
        { reason = "DPR 无效"; return false; }
        if (scaleToken != null && scaleToken.Type != JTokenType.Null && double.TryParse(scaleToken.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale) && scale > 0 && double.IsFinite(scale))
        { width = sw / scale; height = sh / scale; }
        if (dev["cssWidth"] != null && dev["cssWidth"]!.Type != JTokenType.Null)
        { if (!PositiveInt(dev["cssWidth"], out var cssWidth)) { reason = "CSS 宽度无效"; return false; } width = cssWidth; }
        if (dev["cssHeight"] != null && dev["cssHeight"]!.Type != JTokenType.Null)
        { if (!PositiveInt(dev["cssHeight"], out var cssHeight)) { reason = "CSS 高度无效"; return false; } height = cssHeight; }
        if (width < 240 || width > 600 || height <= width || height / width < 1.3)
        { reason = $"屏幕不符合手机竖屏范围: CSS={width:0.#}x{height:0.#}"; return false; }
        var defaults = new List<string>();
        if (!PositiveInt(dev["cpu"], out _)) { dev["cpu"] = 8; defaults.Add("cpu=8"); }
        if (!PositiveLong(dev["storage"], out var storage) || storage > long.MaxValue / (1024L * 1024 * 1024))
        { dev["storage"] = 128; defaults.Add("storage=128GiB"); }
        var ram = (dev.Value<string>("ram") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (ram.Length == 0 || ram.Any(x => !int.TryParse(x, out var n) || n <= 0)) { dev["ram"] = "8"; defaults.Add("ram=8GiB"); }
        repaired = defaults.Count > 0;
        reason = repaired ? "设备字段缺失或无效，默认: " + string.Join(", ", defaults) : "";
        return true;
    }
    private static bool PositiveInt(JToken? value, out int result) => int.TryParse(value?.ToString(), out result) && result > 0;
    private static bool PositiveLong(JToken? value, out long result) => long.TryParse(value?.ToString(), out result) && result > 0;

    public static void LogDevice(int taskId, int uv, string reason, JToken? original, string? executionId = null)
    {
        try
        {
            var copy = original?.DeepClone();
            Redact(copy);
            using var json = System.Text.Json.JsonDocument.Parse(copy?.ToString(Newtonsoft.Json.Formatting.None) ?? "null");
            BrowserLifecycleLog.Write(executionId ?? $"device-{taskId}-{uv}", "device.validation", "DeviceValidation", reason,
                new { taskId, uv, device = json.RootElement.Clone() });
        }
        catch { } // Diagnostics never interrupt device selection.
    }
    private static void Redact(JToken? value)
    {
        if (value is JObject obj)
            foreach (var property in obj.Properties().ToArray())
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(property.Name, "password|passwd|pwd|token|secret|cookie|authorization|api.?key", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) property.Value = "<redacted>";
                else Redact(property.Value);
            }
        else if (value is JArray array) foreach (var item in array) Redact(item);
    }
}
