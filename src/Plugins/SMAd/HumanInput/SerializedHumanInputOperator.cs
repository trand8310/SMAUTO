using Microsoft.Playwright;

namespace SMAd.HumanInput
{
    /// <summary>
    /// 每个 Worker 的输入串行协调器。主流程与弹窗守护共享同一个实例，
    /// 因此不会交叉发送 mouseDown/touchStart/wheel 等输入事件。
    /// </summary>
    internal sealed class SerializedHumanInputOperator : IHumanInputOperator
    {
        private readonly IHumanInputOperator _inner;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public SerializedHumanInputOperator(IHumanInputOperator inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public bool IsTouch => _inner.IsTouch;

        public Task BrowseOnceAsync(IPage page, ICDPSession cdp, CancellationToken cancellationToken = default) =>
            RunAsync(() => _inner.BrowseOnceAsync(page, cdp, cancellationToken), cancellationToken);

        public Task BrowseForAsync(IPage page, ICDPSession cdp, TimeSpan duration, CancellationToken cancellationToken = default) =>
            RunAsync(() => _inner.BrowseForAsync(page, cdp, duration, cancellationToken), cancellationToken);

        public Task ScrollByIntentAsync(
            IPage page,
            ICDPSession cdp,
            HumanActionIntent intent,
            CancellationToken cancellationToken = default) =>
            RunAsync(() => _inner.ScrollByIntentAsync(page, cdp, intent, cancellationToken), cancellationToken);

        public Task ScrollToTopAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default) =>
            RunAsync(() => _inner.ScrollToTopAsync(page, cdp, cancellationToken), cancellationToken);

        public Task MoveToElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            CancellationToken cancellationToken = default) =>
            RunAsync(
                () => _inner.MoveToElementAsync(page, cdp, locator, maxSwipes, cancellationToken),
                cancellationToken);

        public Task<bool> ClickAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default) =>
            RunAsync(() => _inner.ClickAsync(page, cdp, locator, cancellationToken), cancellationToken);

        public Task<bool> ClickAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            CancellationToken cancellationToken = default) =>
            RunAsync(() => _inner.ClickAsync(page, cdp, element, cancellationToken), cancellationToken);

        private async Task RunAsync(Func<Task> action, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await action();
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                return await action();
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
