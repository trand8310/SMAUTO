namespace QTP.Common;

public sealed record BrowserRuntimeOptions(int MaximumRunning = 1, int MaximumLaunching = 4);
public sealed record BrowserStartOptions(string ExecutionId, string ExecutablePath, string UserDataDir,
    TimeSpan Lifetime, string Arguments = "about:blank", string? Proxy = null,
    TimeSpan? ReadyTimeout = null);
public sealed record BrowserRuntimeSnapshot(int MaximumRunning, int MaximumLaunching, int Waiting,
    int Opening, int Running, int Closing, bool Accepting)
{
    public BrowserStopKind? AdmissionStopKind { get; init; }
}
