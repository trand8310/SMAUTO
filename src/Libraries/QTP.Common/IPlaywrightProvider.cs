
namespace QTP.Common
{
    using Microsoft.Playwright;

    public interface IPlaywrightProvider
    {
        Task<IPlaywright> GetAsync();
        Task<IPlaywright> GetAsync(System.Threading.CancellationToken token)
            => GetAsync().WaitAsync(token);
        async Task<PlaywrightLease> AcquireAsync(System.Threading.CancellationToken token = default)
            => new(await GetAsync(token), () => ValueTask.CompletedTask);
    }
}
