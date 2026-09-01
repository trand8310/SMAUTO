using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace PlaywrightHumanInput
{
    public sealed class ScrollTargetResolver
    {
        public async Task<ScrollTargetState> GetAtPointAsync(IPage page, double x, double y)
        {
            try
            {
                return await page.EvaluateAsync<ScrollTargetState>(@"
                    ({x,y}) => {
                        const doc = document.scrollingElement || document.documentElement || document.body;
                        const can = (el) => {
                            if (!el) return false;
                            const s = getComputedStyle(el);
                            const oy = s.overflowY;
                            const ox = s.overflowX;
                            return ((oy === 'auto' || oy === 'scroll' || oy === 'overlay') && el.scrollHeight > el.clientHeight + 2) ||
                                   ((ox === 'auto' || ox === 'scroll' || ox === 'overlay') && el.scrollWidth > el.clientWidth + 2);
                        };
                        let hit = document.elementFromPoint(x,y);
                        let target = hit;
                        while (target && target !== document.body && target !== document.documentElement) {
                            if (can(target)) break;
                            target = target.parentElement;
                        }
                        if (!target || !can(target)) target = doc;
                        const isDoc = target === doc || target === document.documentElement || target === document.body;
                        return {
                            Kind: isDoc ? 'document' : 'element',
                            Key: isDoc ? 'document' : `${target.tagName}:${target.id || ''}:${typeof target.className === 'string' ? target.className : ''}`,
                            ScrollLeft: Number(target.scrollLeft || 0),
                            ScrollTop: Number(target.scrollTop || 0),
                            ScrollWidth: Number(target.scrollWidth || 0),
                            ScrollHeight: Number(target.scrollHeight || 0),
                            ClientWidth: Number(target.clientWidth || innerWidth || 0),
                            ClientHeight: Number(target.clientHeight || innerHeight || 0)
                        };
                    }
                ", new { x, y }) ?? new ScrollTargetState();
            }
            catch
            {
                return new ScrollTargetState();
            }
        }

        public async Task<ScrollTargetState> GetDocumentAsync(IPage page)
        {
            try
            {
                return await page.EvaluateAsync<ScrollTargetState>(@"
                    () => {
                        const target = document.scrollingElement || document.documentElement || document.body;
                        return {
                            Kind:'document', Key:'document',
                            ScrollLeft:Number(target.scrollLeft || 0), ScrollTop:Number(target.scrollTop || 0),
                            ScrollWidth:Number(target.scrollWidth || 0), ScrollHeight:Number(target.scrollHeight || 0),
                            ClientWidth:Number(target.clientWidth || innerWidth || 0), ClientHeight:Number(target.clientHeight || innerHeight || 0)
                        };
                    }
                ") ?? new ScrollTargetState();
            }
            catch
            {
                return new ScrollTargetState();
            }
        }

        public bool CanScroll(ScrollTargetState state, HumanSwipeDirection direction) => direction switch
        {
            HumanSwipeDirection.Up => state.CanScrollVertically && !state.IsNearBottom,
            HumanSwipeDirection.Down => state.CanScrollVertically && !state.IsNearTop,
            HumanSwipeDirection.Left => state.CanScrollHorizontally && !state.IsNearRight,
            HumanSwipeDirection.Right => state.CanScrollHorizontally && !state.IsNearLeft,
            _ => false
        };

        public async Task<bool> DidScrollAsync(IPage page, ScrollTargetState before, ScrollTargetState documentBefore, double x, double y, HumanSwipeDirection direction, double minDelta)
        {
            await Task.Delay(45);
            var after = await GetAtPointAsync(page, x, y);
            double delta = direction is HumanSwipeDirection.Left or HumanSwipeDirection.Right
                ? Math.Abs(after.ScrollLeft - before.ScrollLeft)
                : Math.Abs(after.ScrollTop - before.ScrollTop);
            if (delta >= minDelta) return true;

            var docAfter = await GetDocumentAsync(page);
            double docDelta = direction is HumanSwipeDirection.Left or HumanSwipeDirection.Right
                ? Math.Abs(docAfter.ScrollLeft - documentBefore.ScrollLeft)
                : Math.Abs(docAfter.ScrollTop - documentBefore.ScrollTop);
            return docDelta >= minDelta;
        }

        public async Task<ElementRect?> GetElementRectAsync(ILocator locator)
        {
            if (locator == null) return null;
            try
            {
                return await locator.EvaluateAsync<ElementRect>(@"
                    el => { const r = el.getBoundingClientRect(); return { X:r.left, Y:r.top, Width:r.width, Height:r.height }; }
                ");
            }
            catch { return null; }
        }

        public async Task<ElementRect?> GetElementRectAsync(IElementHandle element)
        {
            if (element == null) return null;
            try
            {
                return await element.EvaluateAsync<ElementRect>(@"
                    el => { const r = el.getBoundingClientRect(); return { X:r.left, Y:r.top, Width:r.width, Height:r.height }; }
                ");
            }
            catch { return null; }
        }
    }
}
