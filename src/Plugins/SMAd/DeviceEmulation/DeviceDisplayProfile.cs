using System.Globalization;
using Newtonsoft.Json.Linq;
using SMAd.Models;

namespace SMAd.DeviceEmulation;

/// <summary>Screen and viewport use CSS pixels; the native window uses Windows DIP.</summary>
public sealed record DeviceDisplayProfile(int ScreenWidth, int ScreenHeight, int AvailableWidth,
    int AvailableHeight, int ViewportWidth, int ViewportHeight, float Dpr, bool Mobile,
    int WindowWidth, int WindowHeight)
{
    public static DeviceDisplayProfile From(TaskConfig config)
    {
        var args = config.TaskArgs ?? new JObject();
        int Read(string name, int fallback) => args.SelectToken("dev." + name)?.Value<int>() ?? fallback;
        bool mobile = config.Os is 1 or 2;
        //bool webView = config.UserAgent.Contains("; wv", StringComparison.OrdinalIgnoreCase);
        int status = Read("statusBarHeight", mobile ? 24 : 0);
        int navigation = Read("navigationBarHeight", mobile ? 24 : 0);
        int toolbar = Read("browserToolbarHeight", mobile ? 56 : 0);
        if (status < 0 || navigation < 0 || toolbar < 0)
            throw new ArgumentOutOfRangeException(nameof(config), "System and browser bar heights must be nonnegative CSS pixels.");
        var result = new DeviceDisplayProfile(config.Sw, config.Sh,
            Read("availWidth", config.Sw), Read("availHeight", config.Sh - ( navigation + toolbar)),
            Read("viewportWidth", config.Sw), Read("viewportHeight", config.Sh - status - navigation - toolbar),
            config.DeviceScale, mobile,
            Read("windowWidth", config.Sw),
            Read("windowHeight", config.Sh));
        if (result.ScreenWidth <= 0 || result.ScreenHeight <= 0 || result.ScreenWidth > 10000000 || result.ScreenHeight > 10000000 || !float.IsFinite(result.Dpr) || result.Dpr <= 0 ||
            result.AvailableWidth <= 0 || result.AvailableWidth > result.ScreenWidth ||
            result.AvailableHeight <= 0 || result.AvailableHeight > result.ScreenHeight ||
            result.ViewportWidth <= 0 || result.ViewportWidth > result.ScreenWidth ||
            result.ViewportHeight <= 0 || result.ViewportHeight > result.ScreenHeight ||
            result.WindowWidth <= 0 || result.WindowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(config), "Invalid screen, viewport, DPR or window dimensions.");
        return result;
    }


    public string DprArgument => Dpr.ToString(CultureInfo.InvariantCulture);

}
