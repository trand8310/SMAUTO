using Newtonsoft.Json.Linq;
using QTP.Common;

namespace MainClient.Services;

internal static class ProxyIpMetadata
{
    public static (JObject Info, string RealIp) Parse(ProxyTestResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Data)) throw new FormatException("IP metadata response is empty.");
        var json = JObject.Parse(result.Data);
        var full = result.SuccessUrl is "http://ip-api.com/json" or
            "http://117.21.200.221/api/dash/ipinfo.php" or "http://211.154.24.179:9000/api/dash/ipinfo.php";
        var realIp = json["query"]?.Value<string>() ?? "";
        if (!full && string.IsNullOrWhiteSpace(realIp)) realIp = json["ip"]?.Value<string>() ?? "";
        return (full ? json : new JObject { ["query"] = realIp }, realIp);
    }
}
