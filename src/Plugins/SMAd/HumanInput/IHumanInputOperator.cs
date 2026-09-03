using Microsoft.Playwright;

namespace SMAd.HumanInput
{
    /// <summary>
    /// SMAd 业务层使用的统一输入接口。
    /// </summary>
    public interface IHumanInputOperator
    {
        bool IsTouch { get; }

        Task BrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default);

        Task BrowseForAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            CancellationToken cancellationToken = default);

        Task ScrollByIntentAsync(
            IPage page,
            ICDPSession cdp,
            HumanActionIntent intent,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 使用当前设备对应的人类输入方式，逐步返回页面顶部。
        /// </summary>
        Task ScrollToTopAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default);

        Task MoveToElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            CancellationToken cancellationToken = default);

        Task<bool> ClickAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default);

        Task<bool> ClickAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            CancellationToken cancellationToken = default);
    }
}
