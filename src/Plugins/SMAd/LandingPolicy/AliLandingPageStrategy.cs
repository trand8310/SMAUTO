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
            if (ctx.Page == null || ctx.Page.IsClosed || !CanHandle(ctx.Page.Url)) return FlowControl.Continue;
            await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);
            if (_owner._appSettings.p4psearch && _owner._appSettings.p4psearchRate > 0 && ctx.Page!.Url.Contains("m.1688.com"))
            {
                await _owner.TryHandle1688RecommendWordsAsync(ctx, token);
            }
            if (ctx.TriggerDownloadSign > 0) return FlowControl.EndTask;
            if (ctx.Page == null || ctx.Page.IsClosed || !CanHandle(ctx.Page.Url)) return FlowControl.Continue;
            var click = await BrowseAndClickAsync(ctx, "div[class^='offer-item']", token);
            if (click?.Downloaded == true) return FlowControl.EndTask;
            if (click?.Navigated == true)
            {
                if (ctx.Page!.Url.Contains("1688.com") && ctx.Page.Url.Contains("_tmd_") && ctx.Page!.Url.Contains("punish?x5secdata"))
                {
                    await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                    if (await TouchDragHelper.WaitAllVisibleWithTextsAsync(ctx.Page!, 5000))
                    {
                        if (await TouchDragHelper.DragSliderAsync(ctx.Page!, ctx.CdpSession!, ".btn_slide", ".slidetounlock", token))
                        {
                            await Task.Delay(CommonHelper.RandomRange(3500, 5500), token);
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
                    var nextClick = await BrowseAndClickAsync(ctx, null, token);
                    if (nextClick?.Downloaded == true) return FlowControl.EndTask;
                }

            }

            return FlowControl.Continue;
        }

        private async Task<ClickResult?> BrowseAndClickAsync(WorkerRunContext ctx, string? selector, CancellationToken token)
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            var ct = budget.Token;
            try
            {
                if (!await ViewportLandingInteraction.BrowseAsync(ctx, _owner, "1688", token)) return null;
                budget.CancelAfter(5000);
                var page = ctx.Page!;
                var initialUrl = page.Url;
                Microsoft.Playwright.ILocator offers;
                if (selector != null)
                    offers = page.Locator(selector);
                else
                {
                    // Preserve the recommendation-page candidate rules; never seek an offscreen link.
                    var count = await CenterClickableFinder.MarkCandidatesAsync(page).WaitAsync(ct);
                    offers = count > 0 ? CenterClickableFinder.GetMarkedLocator(page) : page.Locator("a[href]");
                }
                var candidates = await ViewportLandingInteraction.GetVisibleCandidateIndicesAsync(offers, ct);
                if (candidates.Length == 0)
                {
                    _owner.LogWriteLine("1688: 当前视口没有未被遮挡的候选，继续停留流程");
                    return null;
                }
                var index = candidates[Random.Shared.Next(candidates.Length)];
                ct.ThrowIfCancellationRequested();
                if (page.IsClosed || !ReferenceEquals(ctx.Page, page) || page.Url != initialUrl) return null;
                _owner.LogWriteLine($"1688: 当前可点候选={candidates.Length}，随机选择[{index}]，直接触屏点击");
                budget.CancelAfter(Timeout.Infinite);
                var click = await _owner.ClickAndDetectNavigationAsync(ctx, offers.Nth(index), token);
                _owner.LogWriteLine($"1688: 候选[{index}]点击结果={click.Outcome}, 原因={click.Reason}");
                return click;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                _owner.LogWriteLine("1688: 目标筛选预算耗尽，继续停留流程");
                return null;
            }
        }
    }

}
