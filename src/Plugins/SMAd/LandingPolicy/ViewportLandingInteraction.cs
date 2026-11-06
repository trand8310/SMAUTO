using Microsoft.Playwright;
using HumanTouchP3.Input;
using QTP.Plugins;
using SMAd.Models;

namespace SMAd.LandingPolicy;

internal static class ViewportLandingInteraction
{
    public static async Task<bool> BrowseAsync(WorkerRunContext ctx, SMAdTask owner, string name, CancellationToken token)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(25000);
        var completed = false;
        await SMAd.PageActions.OptionalPageOperation.RunAsync(ctx, name + " browse", async () =>
            completed = await BrowseCoreAsync(ctx, owner, name, budget.Token), token,
            message => owner.LogWriteLine(message));
        return completed;
    }

    private static async Task<bool> BrowseCoreAsync(WorkerRunContext ctx, SMAdTask owner, string name, CancellationToken token)
    {
        var page = ctx.Page;
        if (page == null || page.IsClosed) return false;
        var initialUrl = page.Url;
        var binding = ctx.ActivePage;
        if (binding == null || !ReferenceEquals(binding.Page, page)) return false;
        // Only measure page length; candidates are still selected exclusively from the final viewport.
        bool longPage = false;
        try
        {
            longPage = await page.EvaluateAsync<bool>("() => { const e=document.scrollingElement; return !!e && e.scrollHeight > e.clientHeight * 4; }")
                .WaitAsync(TimeSpan.FromSeconds(2), token);
        }
        catch (TimeoutException) { }
        var desired = longPage ? Random.Shared.Next(5, 7) : Random.Shared.Next(3, 5);
        bool allowBack = Random.Shared.Next(100) < 15;
        int backAt = allowBack ? Random.Shared.Next(2, desired) : -1;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < desired; i++)
        {
            token.ThrowIfCancellationRequested();
            if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding) || page.Url != initialUrl) return false;
            // Reserve time for a final gesture within the existing overall list budget.
            if (clock.Elapsed >= TimeSpan.FromSeconds(17)) break;
            var direction = i == backAt ? HumanSwipeDirection.Down : HumanSwipeDirection.Up;
            var trace = await ctx.human!.Engine.SwipeAsync(page, binding.Session, new HumanTouchRequest
            {
                Direction = direction,
                Intent = direction == HumanSwipeDirection.Up ? SwipeIntent.Preview : SwipeIntent.BackReview,
                CheckScrollableBeforeSwipe = true,
                VerifyScrollChanged = true
            }, token);
            owner.LogWriteLine($"{name}: 浏览滑动={direction}，实际滚动={trace?.ScrollChanged == true}");
            if (trace == null || !trace.ScrollChanged) break;
            var reading = TimeSpan.FromMilliseconds(Random.Shared.Next(800, 1401));
            await Task.Delay(reading, token);
            ctx.human.Session.RecordObserve(reading);
        }
        return !page.IsClosed && ReferenceEquals(ctx.ActivePage, binding) && page.Url == initialUrl;
    }
    internal static Task<int[]> GetVisibleCandidateIndicesAsync(ILocator offers, CancellationToken token)
        => offers.EvaluateAllAsync<int[]>("""
            elements => elements.flatMap((el, index) => {
                const doc = el.ownerDocument, win = doc.defaultView;
                const style = win.getComputedStyle(el), r = el.getBoundingClientRect();
                if (!el.isConnected || r.width <= 0 || r.height <= 0 ||
                    style.visibility !== 'visible' || style.display === 'none' ||
                    el.closest('[inert]') || el.matches(':disabled')) return [];
                const vv = win.visualViewport;
                let left = vv ? vv.offsetLeft : 0, top = vv ? vv.offsetTop : 0;
                let right = left + (vv ? vv.width : win.innerWidth);
                let bottom = top + (vv ? vv.height : win.innerHeight);
                for (let parent = el.parentElement; parent; parent = parent.parentElement) {
                    const s = win.getComputedStyle(parent), p = parent.getBoundingClientRect();
                    if (Number(s.opacity) === 0) return [];
                    // The document scrolling element's rectangle moves with the page;
                    // the viewport bounds above already represent its visible clip.
                    if (parent === doc.documentElement || parent === doc.scrollingElement) continue;
                    if (/auto|scroll|hidden|clip/.test(s.overflowX)) {
                        left = Math.max(left, p.left + parent.clientLeft);
                        right = Math.min(right, p.left + parent.clientLeft + parent.clientWidth);
                    }
                    if (/auto|scroll|hidden|clip/.test(s.overflowY)) {
                        top = Math.max(top, p.top + parent.clientTop);
                        bottom = Math.min(bottom, p.top + parent.clientTop + parent.clientHeight);
                    }
                }
                if (Number(style.opacity) === 0) return [];
                // Cover the central region used by TapAsync, including fixed-bar hit testing.
                for (const rx of [0.3, 0.5, 0.7]) for (const ry of [0.4, 0.5, 0.6]) {
                    const x = r.left + r.width * rx, y = r.top + r.height * ry;
                    if (x <= left || x >= right || y <= top || y >= bottom) return [];
                    const hit = doc.elementFromPoint(x, y);
                    if (!hit || !(hit === el || el.contains(hit))) return [];
                }
                return [index];
            })
            """).WaitAsync(token);
}
