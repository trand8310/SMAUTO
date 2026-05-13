using QTP.Common;
using QTP.Plugins;
using SMAd.Models;
using SMAd.Swiper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
            if (_owner._appSettings.p4psearch && _owner._appSettings.p4psearchRate > 0 && ctx.Page!.Url.Contains("m.1688.com"))
            {
                await _owner.TryHandle1688RecommendWordsAsync(ctx, token);
            }
            await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
            var offerItems = await _owner.ResolveOfferItemsAsync(ctx, token);

            if (offerItems != null && await offerItems.CountAsync() > 0)
            {
                int count = await offerItems.CountAsync();
                var item = offerItems.Nth(CommonHelper.RandomRange(0, count));

                await SwipeEmulator.SwipeToElementAsync(
                ctx.Page!,
                ctx.CdpSession!,
                item,
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
                    if (ctx.Page!.Url.Contains("1688.com") && ctx.Page.Url.Contains("_tmd_") && ctx.Page!.Url.Contains("punish?x5secdata"))
                    {
                        await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                        if (await TouchDragHelper.WaitAllVisibleWithTextsAsync(ctx.Page!, 5000))
                        {
                            if (await TouchDragHelper.DragSliderAsync(ctx.Page!, ctx.CdpSession!, ".btn_slide", ".slidetounlock", token))
                            {
                                await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
                            }
                        }
                        else
                        {
                            return FlowControl.Continue;
                        }
                    }


                    _owner.ProcessingPageElementTask(ctx, token);

                    if (ctx.Page.Url.StartsWith("https://re.1688.com/"))
                    {
                        await HumanScrollHelper.TouchPageLongScrollAsync(
                        ctx.Page!,
                        ctx.CdpSession!,
                        scrollCount: CommonHelper.RandomRange(1, 4),
                        direction: PageScrollDirection.Up,
                        cancellationToken: token);
                        await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
                        count = await CenterClickableFinder.MarkCandidatesAsync(ctx.Page);
                        if (count > 0)
                        {
                            var locator_list = CenterClickableFinder.GetMarkedLocator(ctx.Page);
                            var locator_count = await locator_list.CountAsync();
                            if (locator_count > 0)
                            {
                                foreach (var target_index in Enumerable.Range(0, locator_count).OrderBy(o => Guid.NewGuid()))
                                {
                                    var target = locator_list.Nth(target_index);
                                    var result = await _owner.ClickAndDetectNavigationAsync(ctx, target, token);
                                    if (result.Navigated)
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                        else
                        {
                            var locator = ctx.Page
                            .Locator("body,iframe")
                            .Filter(new() { Visible = true })
                            .First;
                            if (await locator.CountAsync() > 0)
                            {
                                await locator.First.ScrollIntoViewIfNeededAsync();
                                await _owner.ClickAndDetectNavigationAsync(ctx, locator.First, token);
                            }
                        }
                    }

                    await Task.Delay(CommonHelper.RandomRange(2000, 3000), token);
                }
            }

            return FlowControl.Continue;
        }
    }

}
