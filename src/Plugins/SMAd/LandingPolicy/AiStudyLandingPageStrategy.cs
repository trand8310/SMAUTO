using QTP.Common;
using QTP.Plugins;
using SMAd.Models;
using SMAd.Swiper;
 
namespace SMAd.LandingPolicy
{



    public sealed class AiStudyLandingPageStrategy : ILandingPageStrategy
    {
        private readonly SMAdTask _owner;

        public AiStudyLandingPageStrategy(SMAdTask owner)
        {
            _owner = owner;
        }

        public bool CanHandle(string url) => url.StartsWith("https://aistudy.baidu.com/", StringComparison.OrdinalIgnoreCase);

        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

            var recommend = ctx.Page!.Locator(".recommend-adlist .waterfall-column");
            var count = await recommend.CountAsync();

            if (count == 0)
            {
                var ok1 = await CDPHelper.FindItemAndClickAsync(ctx.Page, ctx.CdpSession!, ".search-page-container input");
                var ok2 = ok1 && await CDPHelper.FindItemAndClickAsync(ctx.Page, ctx.CdpSession!, ".search-page-container .search");

                if (ok2)
                {
                    var retry = await RetryPolicy.ExecuteBoolAsync(
                        async ct =>
                        {
                            ct.ThrowIfCancellationRequested();

                            recommend = ctx.Page.Locator(".recommend-adlist .waterfall-column");
                            count = await recommend.CountAsync();
                            if (count > 0)
                                return true;

                            if (await CDPHelper.FindItemAndClickAsync(ctx.Page, ctx.CdpSession!, ".no-result-btn"))
                                await Task.Delay(1500, ct);

                            return false;
                        },
                        maxAttempts: 5,
                        token: token);

                    if (retry.IsSuccess)
                    {
                        recommend = ctx.Page.Locator(".recommend-adlist .waterfall-column");
                        count = await recommend.CountAsync();
                    }
                }
            }

            if (count > 0)
            {
                var item = recommend.Nth(CommonHelper.RandomRange(0, count));
                await SwipeEmulator.SwipeToElementAsync(
                ctx.Page,
                ctx.CdpSession!,
                item,
                maxSwipes: 10,
                cancellationToken: token);

                await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                await _owner.ClickAndDetectNavigationAsync(ctx, item, token);
            }

            return FlowControl.Continue;
        }
    }
}
