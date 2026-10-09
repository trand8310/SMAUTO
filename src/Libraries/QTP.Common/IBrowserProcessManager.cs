namespace QTP.Common;

public interface IBrowserProcessManager
{
    Task<ChromiumSession> StartChromium(string uniqueId, string exePath, string userDataDir, TimeSpan ttl,
        string arguments = "--incognito", string? proxyServer = null, TimeSpan? readyTimeout = null,
        CancellationToken token = default);
    /// <summary>Completes only after process exit is confirmed. Throws when exit cannot be confirmed.</summary>
    Task CloseAsync(string uniqueId);
    bool TryGetSession(string uniqueId, out ChromiumSession? session);
}
