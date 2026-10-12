using System.ComponentModel;

namespace QTP.Common;

public enum BrowserStopKind
{
    Completed, Canceled, RuntimeStopping, LifetimeExpired, BrowserCrashed, CdpDisconnected,
    DriverDisconnected, OperationTimedOut, ResourceExhausted, StartupFailed, CleanupFailed, ProxyFailed,
    ExecutionFailed, DeviceConfigurationFailed
}
public sealed record ProfileCleanupResult(string UserDataDir, bool Deleted, int Attempts, string? Error = null);
public sealed record ChromiumCloseResult(string ExecutionId, BrowserStopKind Reason, bool ExitConfirmed,
    bool Forced, int? ExitCode, TimeSpan Elapsed, string? Error, Task<ProfileCleanupResult> DirectoryCleanup)
{
    public string? DiagnosticTail { get; init; }
}
public class BrowserFailureException(BrowserStopKind kind, string message, Exception? inner = null)
    : InvalidOperationException(message, inner)
{
    public BrowserStopKind Kind { get; } = kind;
}
public sealed record BrowserReclaimResult(string ExecutionId, BrowserStopKind Reason, bool ExitConfirmed,
    TimeSpan Elapsed, IReadOnlyList<string> CleanupErrors, ChromiumCloseResult? ProcessResult = null);
public sealed class BrowserStartupCanceledException(BrowserStopKind reason, Exception inner, CancellationToken token)
    : OperationCanceledException($"Browser startup stopped: {reason}", inner, token)
{
    public BrowserStopKind Reason { get; } = reason;
}

public static class BrowserFailureClassifier
{
    public static BrowserStopKind Classify(Exception error)
    {
        if (error is AggregateException aggregate)
        {
            var kinds = aggregate.Flatten().InnerExceptions.Select(Classify).ToArray();
            if (kinds.Contains(BrowserStopKind.ResourceExhausted)) return BrowserStopKind.ResourceExhausted;
            return kinds.FirstOrDefault(BrowserStopKind.StartupFailed);
        }
        if (error is OutOfMemoryException || error is Win32Exception { NativeErrorCode: 8 or 14 or 1455 })
            return BrowserStopKind.ResourceExhausted;
        if (error is BrowserStartupCanceledException stopped) return stopped.Reason;
        if (error is BrowserFailureException failure) return failure.Kind;
        if (error is OperationCanceledException) return BrowserStopKind.Canceled;
        if (error is TimeoutException || error.GetType().Name == "TimeoutException") return BrowserStopKind.OperationTimedOut;
        return BrowserStopKind.StartupFailed;
    }
}
