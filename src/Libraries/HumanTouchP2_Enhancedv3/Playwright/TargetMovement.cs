using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput;

public enum TargetMovementStatus { Ready, NotFound, Disabled, Covered, NoProgress, TimedOut, PageChanged }
public sealed record TargetMovementResult(TargetMovementStatus Status, IReadOnlyList<HumanSwipeTrace> Traces, string? Reason = null)
{
    public bool Ready => Status == TargetMovementStatus.Ready;
}

public sealed partial class HumanTouchEngine
{
    public Func<IPage, bool>? IsActivePage { get; set; }

    public async Task<TargetMovementResult> MoveToTargetAsync(IPage page, ICDPSession cdp, ILocator target,
        int maxSwipes = 10, int timeoutMs = 15000, CancellationToken cancellationToken = default)
    {
        var traces = new List<HumanSwipeTrace>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(timeoutMs);
        var ct = budget.Token;
        var url = page.Url;
        try
        {
            for (int i = 0; i <= maxSwipes; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (page.IsClosed || page.Url != url || IsActivePage?.Invoke(page) == false)
                    return new(TargetMovementStatus.PageChanged, traces);
                await using var element = await target.ElementHandleAsync(new() { Timeout = 2000 }).WaitAsync(ct);
                if (element == null) return new(TargetMovementStatus.NotFound, traces);
                if (!await element.IsEnabledAsync().WaitAsync(ct)) return new(TargetMovementStatus.Disabled, traces);
                var rect = await element.BoundingBoxAsync().WaitAsync(ct);
                if (rect == null || rect.Width <= 0 || rect.Height <= 0) return new(TargetMovementStatus.NotFound, traces, "No layout box");
                var viewport = await GetEffectiveViewportAsync(page).WaitAsync(ct);
                bool actionable = rect.X + rect.Width > 8 && rect.X < viewport.Width - 8 &&
                    rect.Y + rect.Height > 8 && rect.Y < viewport.Height - 8 && await element.IsVisibleAsync().WaitAsync(ct) &&
                    await element.EvaluateAsync<bool>(@"el => {
                        const r=el.getBoundingClientRect();
                        let l=Math.max(0,r.left),t=Math.max(0,r.top),right=Math.min(innerWidth,r.right),bottom=Math.min(innerHeight,r.bottom);
                        for(let p=el.parentElement;p;p=p.parentElement) {
                            const s=getComputedStyle(p),b=p.getBoundingClientRect();
                            if(/auto|scroll|hidden|clip/.test(s.overflowX)) { l=Math.max(l,b.left);right=Math.min(right,b.right); }
                            if(/auto|scroll|hidden|clip/.test(s.overflowY)) { t=Math.max(t,b.top);bottom=Math.min(bottom,b.bottom); }
                        }
                        if(right-l<Math.min(24,r.width) || bottom-t<Math.min(24,r.height)) return false;
                        const x=r.left+r.width*.5,y=r.top+r.height*.5;
                        if(x<l || x>=right || y<t || y>=bottom) return false;
                        const h=el.ownerDocument.elementFromPoint(x,y);
                        return !!h && (h===el || el.contains(h));
                    }").WaitAsync(ct);
                double centerY = rect.Y + rect.Height * .5;
                if (actionable && centerY >= viewport.Height * .22 && centerY <= viewport.Height * .72)
                {
                    await Task.Delay(80, ct);
                    var after = await element.BoundingBoxAsync().WaitAsync(ct);
                    if (after != null && Math.Abs(after.X - rect.X) < .5 && Math.Abs(after.Y - rect.Y) < .5 &&
                        Math.Abs(after.Width - rect.Width) < .5 && Math.Abs(after.Height - rect.Height) < .5)
                        return new(TargetMovementStatus.Ready, traces);
                    continue;
                }
                if (i == maxSwipes) return new(actionable ? TargetMovementStatus.Ready : TargetMovementStatus.NoProgress, traces, "Swipe limit reached");
                // Use the target's nearest scrolling ancestor instead of an arbitrary viewport point.
                await using var ancestor = await element.EvaluateHandleAsync(@"el => {
                    for(let p=el.parentElement;p;p=p.parentElement) {
                        const s=getComputedStyle(p);
                        if((/auto|scroll/.test(s.overflowY)&&p.scrollHeight>p.clientHeight+2)||
                           (/auto|scroll/.test(s.overflowX)&&p.scrollWidth>p.clientWidth+2)) return p;
                    }
                    return el.ownerDocument.scrollingElement;
                }").WaitAsync(ct);
                var container = ancestor.AsElement();
                var box = container == null ? null : await container.BoundingBoxAsync().WaitAsync(ct);
                double top = Math.Max(0, box?.Y ?? 0), bottom = Math.Min(viewport.Height, box == null ? viewport.Height : box.Y + box.Height);
                double left = Math.Max(0, box?.X ?? 0), right = Math.Min(viewport.Width, box == null ? viewport.Width : box.X + box.Width);
                if (right - left < 20 || bottom - top < 20)
                {
                    // The frame/container is itself offscreen: move its outer page first.
                    top = 0; bottom = viewport.Height; left = 0; right = viewport.Width;
                }
                double areaHeight = bottom - top;
                double comfortTop = Math.Max(top + areaHeight * .20, viewport.Height * .22);
                double comfortBottom = Math.Min(bottom - areaHeight * .20, viewport.Height * .72);
                if (comfortBottom <= comfortTop)
                { comfortTop = top + areaHeight * .20; comfortBottom = bottom - areaHeight * .20; }
                double centerX = rect.X + rect.Width * .5;
                bool horizontal = centerX < left || centerX >= right;
                var direction = horizontal ? (centerX >= right ? HumanSwipeDirection.Left : HumanSwipeDirection.Right) :
                    centerY > (comfortTop + comfortBottom) * .5 ? HumanSwipeDirection.Up : HumanSwipeDirection.Down;
                double desired = horizontal ? Math.Abs(centerX - (left + right) * .5) :
                    Math.Abs(centerY - (comfortTop + comfortBottom) * .5);
                int distance = (int)Math.Clamp(desired, 12, Math.Max(12, (horizontal ? right - left : areaHeight) * .45));
                // Leave room around the gesture for common fixed headers/footers.
                if (!horizontal) { top += areaHeight * .12; bottom -= areaHeight * .12; }
                var trace = await SwipeInsideRectAsync(page, cdp,
                    new ElementRect { X = left, Y = top, Width = right - left, Height = bottom - top },
                    new HumanTouchRequest { Direction = direction, Intent = SwipeIntent.MicroAdjust, DistancePx = distance, VerifyScrollChanged = true, ScrollChangedMinDelta = 2 }, ct);
                if (trace == null) return new(actionable ? TargetMovementStatus.Ready : TargetMovementStatus.NoProgress, traces, "Container reached boundary or did not settle");
                traces.Add(trace);
            }
            return new(TargetMovementStatus.NoProgress, traces, "Swipe limit reached");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(TargetMovementStatus.TimedOut, traces); }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException ex) { return new(TargetMovementStatus.TimedOut, traces, ex.Message); }
        catch (PlaywrightException ex) { return new(TargetMovementStatus.NotFound, traces, ex.Message); }
    }
}
