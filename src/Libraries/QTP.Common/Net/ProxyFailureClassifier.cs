using System.Text.RegularExpressions;

namespace QTP.Common;

public enum ProxyFailureKind { Connection, Authentication, Configuration, TargetRoute }

/// <summary>TargetRoute means this proxy route failed; it does not prove the proxy is globally dead.</summary>
public static class ProxyFailureClassifier
{
    public static ProxyFailureKind? Classify(string? message, bool proxyEnabled, bool mainNavigation)
    {
        if (!proxyEnabled || string.IsNullOrWhiteSpace(message)) return null;
        var code = Regex.Match(message, @"\bERR_[A-Z_]+\b").Value;
        return code switch
        {
            "ERR_PROXY_CONNECTION_FAILED" => ProxyFailureKind.Connection,
            "ERR_PROXY_AUTH_REQUESTED" or "ERR_PROXY_AUTH_UNSUPPORTED" => ProxyFailureKind.Authentication,
            "ERR_NO_SUPPORTED_PROXIES" or "ERR_MANDATORY_PROXY_CONFIGURATION_FAILED" or
                "ERR_PROXY_CERTIFICATE_INVALID" => ProxyFailureKind.Configuration,
            "ERR_TUNNEL_CONNECTION_FAILED" or "ERR_SOCKS_CONNECTION_FAILED" or
                "ERR_SOCKS_CONNECTION_HOST_UNREACHABLE" when mainNavigation => ProxyFailureKind.TargetRoute,
            _ => null
        };
    }

    public static string Address(string address, string? protocol)
    {
        var scheme = string.IsNullOrWhiteSpace(protocol)
            ? (address.Contains("://", StringComparison.Ordinal) ? new Uri(address).Scheme : "http")
            : protocol.ToLowerInvariant();
        if (scheme is not ("http" or "https" or "socks5")) throw new ArgumentException("Unsupported proxy protocol");
        var uri = new Uri(address.Contains("://", StringComparison.Ordinal) ? address : $"{scheme}://{address}");
        if (uri.Scheme != scheme || string.IsNullOrWhiteSpace(uri.Host) || uri.Port <= 0 ||
            uri.Port > 65535 || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0)
            throw new ArgumentException("Invalid proxy endpoint or protocol mismatch");
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
