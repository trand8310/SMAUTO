using Microsoft.Playwright;

namespace QTP.Common;

/// <summary>A borrower never disposes the shared driver directly.</summary>
public sealed class PlaywrightLease : IAsyncDisposable
{
    private Func<ValueTask>? _release;
    public IPlaywright Playwright { get; }
    public CancellationToken DriverToken { get; }
    internal object? OwnerTag { get; }
    public PlaywrightLease(IPlaywright playwright, Func<ValueTask> release, CancellationToken driverToken = default,
        object? ownerTag = null)
    { Playwright = playwright; _release = release; DriverToken = driverToken; OwnerTag = ownerTag; }
    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _release, null)?.Invoke() ?? ValueTask.CompletedTask;
}
