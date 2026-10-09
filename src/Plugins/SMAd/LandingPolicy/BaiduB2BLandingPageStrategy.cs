using QTP.Plugins;
using SMAd.Models;
using Microsoft.Playwright;

namespace SMAd.LandingPolicy;

/// <summary>Browse first, then tap one unobscured product in the current viewport.</summary>
public sealed class BaiduB2BLandingPageStrategy : ILandingPageStrategy
{
    private readonly SMAdTask _owner;

    public BaiduB2BLandingPageStrategy(SMAdTask owner) => _owner = owner;

    public bool CanHandle(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.Equals(uri.Host, "b2b.baidu.com", StringComparison.OrdinalIgnoreCase);

    public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var page = ctx.Page;
        if (page == null || page.IsClosed || !CanHandle(page.Url)) return FlowControl.Continue;
        var initialUrl = page.Url;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = budget.Token;
        try
        {
            if (!await ViewportLandingInteraction.BrowseAsync(ctx, _owner, "百度爱采购", token))
                return FlowControl.Continue;
            budget.CancelAfter(5000);
            var offers = ctx.Page!.Locator(".img-content,.list-title,.content-without-title,a.product-item-link"); 
            if (offers == null) return FlowControl.Continue;
            var candidates = await GetVisibleCandidateIndicesAsync(offers, ct);
            if (candidates.Length == 0)
            {
                _owner.LogWriteLine("百度爱采购: 当前视口没有未被遮挡的商品，继续停留流程");
                return FlowControl.Continue;
            }
            var index = candidates[Random.Shared.Next(candidates.Length)];
            _owner.LogWriteLine($"百度爱采购: 当前可点候选={candidates.Length}，随机选择[{index}]，直接触屏点击");
            ct.ThrowIfCancellationRequested();
            if (page.IsClosed || !ReferenceEquals(ctx.Page, page) || page.Url != initialUrl)
                return FlowControl.Continue;
            budget.CancelAfter(Timeout.Infinite);
            var click = await _owner.ClickAndDetectNavigationAsync(ctx, offers.Nth(index), token);
            _owner.LogWriteLine($"百度爱采购: 候选[{index}]点击结果={click.Outcome}, 原因={click.Reason}");
            if (click.Downloaded) return FlowControl.EndTask;
            return FlowControl.Continue;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            _owner.LogWriteLine("百度爱采购: 目标筛选预算耗尽，继续停留流程");
            return FlowControl.Continue;
        }
    }

    internal static Task<int[]> GetVisibleCandidateIndicesAsync(ILocator offers, CancellationToken token)
        => ViewportLandingInteraction.GetVisibleCandidateIndicesAsync(offers, token);
}
