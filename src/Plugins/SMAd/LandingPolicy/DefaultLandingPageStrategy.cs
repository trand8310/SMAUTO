using PlaywrightHumanInput;
using QTP.Common;
using QTP.Plugins;
using SMAd.Models;

namespace SMAd.LandingPolicy
{
    /// <summary>
    /// 默认的落地页处理策略
    /// </summary>
    public sealed class DefaultLandingPageStrategy : ILandingPageStrategy
    {
        private readonly SMAdTask _owner;

        public DefaultLandingPageStrategy(SMAdTask owner)
        {
            _owner = owner;
        }

        public bool CanHandle(string url) => true;

        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
            var offerItems = await _owner.ResolveOfferItemsAsync(ctx, token);

            if (!ctx.Page!.Url.StartsWith("https://plogin.m.jd.com/"))
            {
                if (offerItems != null && await offerItems.CountAsync() > 0)
                {
                    int count = await offerItems.CountAsync();
                    var item = offerItems.Nth(CommonHelper.RandomRange(0, count));

                    await HumanSwipeOperator.MoveToElementAsync(
                      ctx.Page!,
                      ctx.CdpSession!,
                      item,
                      maxSwipes: 10,
                      cancellationToken: token);

                    await Task.Delay(CommonHelper.RandomRange(800, 1200), token);

                    var text = await item.InnerTextAsync();
                    var box = await item.BoundingBoxAsync();

                    if (box != null)
                        _owner.LogWriteLine($"触发点击:{text}:({box.X},{box.Y},{box.Width},{box.Height})");
                    else
                        _owner.LogWriteLine($"触发点击:{text}");


                    var click = await _owner.ClickAndDetectNavigationAsync(ctx, item, token);
                    if (click.Navigated)
                    {

                    }
                }
            }






            return FlowControl.Continue;
        }
    }

}
