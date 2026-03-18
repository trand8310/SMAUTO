
namespace QTP.Common
{
    using Microsoft.Playwright;

    public interface IPlaywrightProvider
    {
        Task<IPlaywright> GetAsync();
    }
}
