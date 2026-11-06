using QTP.Plugins;
using SMAd.Models;
using Microsoft.Playwright;
using System.Diagnostics;

namespace SMAd.LandingPolicy;

/// <summary>Browse first, then tap one unobscured product in the current viewport.</summary>
public sealed class BaiduSJHLandingPageStrategy : ILandingPageStrategy
{
    internal enum PageKind { Other, HealthSeason, Assistant }
    private readonly SMAdTask _owner;

    public BaiduSJHLandingPageStrategy(SMAdTask owner) => _owner = owner;

    public bool CanHandle(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && string.Equals(uri.Host, "sjh.baidu.com", StringComparison.OrdinalIgnoreCase);

    public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var page = ctx.Page;
        if (page == null || page.IsClosed || !CanHandle(page.Url)) return FlowControl.Continue;
        var initialUrl = page.Url;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        var ct = budget.Token;
        var stage = "读取页面标题";
        var stageClock = Stopwatch.StartNew();
        try
        {
            var binding = ctx.ActivePage;
            var title = (await page.TitleAsync().WaitAsync(TimeSpan.FromSeconds(5), token)).Trim();
            if (page.IsClosed || binding == null || !ReferenceEquals(ctx.ActivePage, binding) ||
                !ReferenceEquals(binding.Page, page) || page.Url != initialUrl)
                return FlowControl.Continue;
            var pageKind = ClassifyTitle(title);
            if (pageKind == PageKind.Assistant)
            {
                _owner.LogWriteLine("百度健康助手: 已识别助手页面，等待配置专属操作");
                return FlowControl.Continue;
            }
            if (pageKind != PageKind.HealthSeason)
            {
                _owner.LogWriteLine("百度健康: 页面标题不匹配，跳过健康季操作");
                return FlowControl.Continue;
            }
            stage = "页面浏览";
            stageClock.Restart();
            if (!await ViewportLandingInteraction.BrowseAsync(ctx, _owner, "百度健康季", token))
                return FlowControl.Continue;
            stage = "筛选可见候选";
            stageClock.Restart();
            budget.CancelAfter(5000);
            var offers = ctx.Page!.Locator(".adcard-row-one-root,.bottom-consult-button,.adcard-row-one-content-title"); 
            if (offers == null) return FlowControl.Continue;
            var candidates = await GetVisibleCandidateIndicesAsync(offers, ct);
            if (candidates.Length == 0)
            {
                _owner.LogWriteLine("百度健康季: 当前视口没有未被遮挡的商品，继续停留流程");
                return FlowControl.Continue;
            }
            var index = candidates[Random.Shared.Next(candidates.Length)];
            _owner.LogWriteLine($"百度健康季: 当前可点候选={candidates.Length}，随机选择[{index}]，直接触屏点击");
            ct.ThrowIfCancellationRequested();
            if (page.IsClosed || !ReferenceEquals(ctx.Page, page) || page.Url != initialUrl)
                return FlowControl.Continue;
            budget.CancelAfter(Timeout.Infinite);
            stage = "触屏点击与导航观察";
            stageClock.Restart();
            var click = await _owner.ClickAndDetectNavigationAsync(ctx, offers.Nth(index), token);
            _owner.LogWriteLine($"百度健康季: 候选[{index}]点击结果={click.Outcome}, 原因={click.Reason}");
            if (click.Downloaded) return FlowControl.EndTask;
            return FlowControl.Continue;
        }
        catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
        {
            Debug.WriteLine(ex.Message);
            _owner.LogWriteLine($"百度健康季: {stage}取消或局部预算耗尽，耗时={stageClock.ElapsedMilliseconds}ms，继续停留流程");
            return FlowControl.Continue;
        }
        catch (TimeoutException)
        {
            _owner.LogWriteLine($"百度健康季: {stage}超时，耗时={stageClock.ElapsedMilliseconds}ms");
            throw;
        }
    }

    internal static Task<int[]> GetVisibleCandidateIndicesAsync(ILocator offers, CancellationToken token)
        => ViewportLandingInteraction.GetVisibleCandidateIndicesAsync(offers, token);

    internal static PageKind ClassifyTitle(string title) =>
        title.Contains("助手", StringComparison.Ordinal) ? PageKind.Assistant :
        string.Equals(title.Trim(), "百度健康季", StringComparison.Ordinal) ? PageKind.HealthSeason : PageKind.Other;
}
