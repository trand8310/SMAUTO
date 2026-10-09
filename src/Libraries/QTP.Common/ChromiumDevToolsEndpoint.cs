namespace QTP.Common;

internal sealed record ChromiumDevToolsEndpoint(int Port, string WebSocketUrl)
{
    public static bool TryParse(string[] lines, out ChromiumDevToolsEndpoint? endpoint)
    {
        endpoint = null;
        if (lines.Length < 2 || !int.TryParse(lines[0].Trim(), out int port) || port is < 1 or > 65535)
            return false;
        string path = lines[1].Trim();
        if (!path.StartsWith("/devtools/browser/", StringComparison.Ordinal) ||
            path.Length <= "/devtools/browser/".Length || path.Any(char.IsWhiteSpace) ||
            path.Contains('?') || path.Contains('#'))
            return false;
        endpoint = new ChromiumDevToolsEndpoint(port, $"ws://127.0.0.1:{port}{path}");
        return true;
    }
}
