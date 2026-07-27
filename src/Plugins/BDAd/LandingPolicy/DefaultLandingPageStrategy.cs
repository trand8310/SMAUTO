using BDAd.Models;
using PlaywrightHumanInput;
using QTP.Common;
using QTP.Plugins;

namespace BDAd.LandingPolicy
{
    /// <summary>
    /// 默认的落地页处理策略
    /// </summary>
    public sealed class DefaultLandingPageStrategy : ILandingPageStrategy
    {
        private readonly BDAdTask _owner;

        public DefaultLandingPageStrategy(BDAdTask owner)
        {
            _owner = owner;
        }

        public bool CanHandle(string url) => true;

        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);

            var offerItems = await _owner.ResolveOfferItemsAsync(ctx, token);
            if (offerItems != null && await offerItems.CountAsync() > 0)
            {
                int count = await offerItems.CountAsync();
                var item = offerItems.Nth(CommonHelper.RandomRange(0, count));
                var human = ctx.HumanSession!.For(ctx.Page!);
                await human.ScrollToElementAsync(item,
                maxScrollAttempts: 20,
                targetViewportRatio: 0.48f,
                cancellationToken: token);

                await Task.Delay(CommonHelper.RandomRange(800, 1400), token);

                var text = await item.InnerTextAsync();
                var box = await item.BoundingBoxAsync();

                if (box != null)
                    _owner.LogWriteLine($"触发点击:{text}:({box.X},{box.Y},{box.Width},{box.Height})");
                else
                    _owner.LogWriteLine($"触发点击:{text}");

                var click = await _owner.ClickAndDetectNavigationAsync(ctx, item, token);
                if (click.Navigated)
                {

                    _owner.ProcessingPageElementTask(ctx, token);

                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                }
            }

            return FlowControl.Continue;
        }
    }

}
