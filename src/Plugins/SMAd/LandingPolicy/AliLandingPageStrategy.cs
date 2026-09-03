using PlaywrightHumanInput;
using QTP.Common;
using QTP.Plugins;
using SMAd.Models;
using System.Text.RegularExpressions;


namespace SMAd.LandingPolicy
{
    /// <summary>
    ///1688落地页处理策略
    /// </summary>
    public sealed class AliLandingPageStrategy : ILandingPageStrategy
    {
        private readonly SMAdTask _owner;

        public AliLandingPageStrategy(SMAdTask owner)
        {
            _owner = owner;
        }

        private static readonly Regex UrlRegex = new Regex(
            @"^https://([a-z0-9-]+\.)*1688\.com/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public bool CanHandle(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return UrlRegex.IsMatch(url);
        }


        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await ctx.Human.BrowseForAsync(ctx.Page!, ctx.CdpSession!, TimeSpan.FromSeconds(CommonHelper.RandomRange(5, 10)), token);
            var offer_items = ctx.Page!.Locator("#offerList .offer_item");
            int offer_count = await offer_items.CountAsync();
            if (offer_count > 0)
            {
                var offer_indexes = Enumerable.Range(0, offer_count).ToList();
                CommonHelper.Shuffle(offer_indexes);
                foreach (var offer_index in offer_indexes)
                {
                    var offer_item = offer_items.Nth(offer_index);
                    if (!await offer_item.IsVisibleAsync())
                        continue;
                    if (!await offer_item.IsEnabledAsync())
                        continue;
                    await ctx.Human.MoveToElementAsync(ctx.Page!, ctx.CdpSession!, offer_item, 10, token);
                    await Task.Delay(CommonHelper.RandomRange(2000, 3000));
                    var offer_clicked = await _owner.ClickAndDetectNavigationAsync(ctx, offer_item, token);
                    if (offer_clicked.Navigated)
                    {
                        return FlowControl.Continue;
                    }
                }
            }
            return FlowControl.Continue;
        }
    }

}
