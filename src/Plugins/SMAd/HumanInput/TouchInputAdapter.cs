using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Common;

namespace SMAd.HumanInput
{
    /// <summary>
    /// 保持现有 HumanTouchP2 API，同时让业务层只依赖统一输入接口。
    /// </summary>
    public sealed class TouchInputAdapter : IHumanInputOperator
    {
        private readonly HumanTouchOperator _touch;

        public TouchInputAdapter(HumanTouchOperator touch)
        {
            _touch = touch ?? throw new ArgumentNullException(nameof(touch));
        }

        public bool IsTouch => true;

        public async Task BrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            await _touch.BrowseOnceAsync(page, cdp, cancellationToken);
        }

        public async Task BrowseForAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            await _touch.BrowseForAsync(page, cdp, duration, cancellationToken);
        }

        public async Task ScrollByIntentAsync(
            IPage page,
            ICDPSession cdp,
            HumanActionIntent intent,
            CancellationToken cancellationToken = default)
        {
            await _touch.SwipeByIntentAsync(page, cdp, MapIntent(intent), cancellationToken);
        }

        public async Task ScrollToTopAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            const int maxAttempts = 24;
            for (int attempt = 0; attempt < maxAttempts && !page.IsClosed; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 手指向下滑动，页面内容向下移动，也就是逐步返回页头。
                var trace = await _touch.SwipeByIntentAsync(
                    page,
                    cdp,
                    SwipeIntent.BackReview,
                    cancellationToken);
                if (trace == null)
                    break;

                await Task.Delay(Random.Shared.Next(120, 360), cancellationToken);
            }
        }

        public async Task MoveToElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            CancellationToken cancellationToken = default)
        {
            await _touch.MoveToElementAsync(page, cdp, locator, maxSwipes, cancellationToken);
        }

        public Task<bool> ClickAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CDPHelper.MouseClickAsync(page, cdp, locator);
        }

        public Task<bool> ClickAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CDPHelper.MouseClickAsync(page, cdp, element);
        }

        private static SwipeIntent MapIntent(HumanActionIntent intent) => intent switch
        {
            HumanActionIntent.Reading => SwipeIntent.Reading,
            HumanActionIntent.MicroAdjust => SwipeIntent.MicroAdjust,
            HumanActionIntent.FastScan => SwipeIntent.FastScan,
            HumanActionIntent.BackReview => SwipeIntent.BackReview,
            _ => SwipeIntent.Preview
        };
    }
}
