using Microsoft.Playwright;

namespace QTP.Common;

public sealed class PlaywrightProvider : IPlaywrightProvider, IAsyncDisposable
{
    private readonly ManagedPlaywrightProvider _inner;
    public PlaywrightProvider() : this(() => Playwright.CreateAsync()) { }
    public PlaywrightProvider(Func<Task<IPlaywright>> factory) => _inner = new(factory);
    public int ActiveLeases => _inner.ActiveLeases;
    public Task<IPlaywright> GetAsync() => _inner.GetAsync();
    public Task<IPlaywright> GetAsync(CancellationToken token) => _inner.GetAsync(token);
    public Task<PlaywrightLease> AcquireAsync(CancellationToken token = default) => _inner.AcquireAsync(token);
    public void Invalidate(PlaywrightLease lease) => _inner.Invalidate(lease);
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
