using Microsoft.Playwright;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput;

public sealed class ScrollTargetResolver
{
    // Identity is per DOM node, so identical classes cannot make unrelated containers look stable.
    private const string Snapshot = @"({x,y,key,documentOnly}) => {
        const store = window.__htp3Scroll ||= { ids:new WeakMap(), refs:new Map(), n:0 };
        const doc = document.scrollingElement || document.documentElement;
        const can = el => { const s=getComputedStyle(el);
            return /auto|scroll|overlay/.test(s.overflowY) && el.scrollHeight>el.clientHeight+2 ||
                /auto|scroll|overlay/.test(s.overflowX) && el.scrollWidth>el.clientWidth+2; };
        let el;
        if (key) el=key==='document' ? doc : store.refs.get(key)?.deref();
        else if (documentOnly) el=doc;
        else { el=document.elementFromPoint(x,y);
            while(el && el!==doc && !can(el)) el=el.parentElement;
            el ||= doc; }
        if(!el || !el.isConnected) return null;
        let id='document';
        if(el!==doc) { id=store.ids.get(el); if(!id) {
            id='scroll-'+(++store.n); store.ids.set(el,id); store.refs.set(id,new WeakRef(el));
            if(store.refs.size>1024) for(const [k,r] of store.refs) if(!r.deref()) store.refs.delete(k);
        }}
        return { Kind:el===doc?'document':'element', Key:id,
            ScrollLeft:el.scrollLeft, ScrollTop:el.scrollTop,
            ScrollWidth:el.scrollWidth, ScrollHeight:el.scrollHeight,
            ClientWidth:el.clientWidth, ClientHeight:el.clientHeight };
    }";

    public async Task<ScrollTargetState> GetAtPointAsync(IPage page, double x, double y, CancellationToken token = default)
    {
        var frame = page.MainFrame;
        double localX = x, localY = y;
        // Descend through the frame tree using main viewport bounding boxes, including cross-origin frames.
        while (true)
        {
            Microsoft.Playwright.IFrame? child = null;
            foreach (var candidate in frame.ChildFrames)
            {
                await using var element = await candidate.FrameElementAsync().WaitAsync(token);
                var box = await element.BoundingBoxAsync().WaitAsync(token);
                if (box == null || x < box.X || y < box.Y || x >= box.X + box.Width || y >= box.Y + box.Height) continue;
                var dimensions = await element.EvaluateAsync<double[]>("el => [el.clientWidth,el.clientHeight,el.offsetWidth,el.offsetHeight,el.clientLeft,el.clientTop]").WaitAsync(token);
                localX = (x - box.X) * dimensions[2] / box.Width - dimensions[4];
                localY = (y - box.Y) * dimensions[3] / box.Height - dimensions[5];
                if (localX < 0 || localY < 0 || localX >= dimensions[0] || localY >= dimensions[1]) continue;
                child = candidate; break;
            }
            if (child == null) break;
            frame = child;
        }
        return await ReadAsync(frame, new { x = localX, y = localY, key = (string?)null, documentOnly = false }, token);
    }
    public async Task<ScrollTargetState> GetDocumentAsync(IPage page, CancellationToken token = default)
        => await ReadAsync(page.MainFrame, new { x = 0, y = 0, key = (string?)null, documentOnly = true }, token);
    private static async Task<ScrollTargetState> ReadAsync(IFrame frame, object args, CancellationToken token)
    {
        var state = await frame.EvaluateAsync<ScrollTargetState>(Snapshot, args).WaitAsync(TimeSpan.FromSeconds(2), token)
            ?? throw new InvalidOperationException("Scroll target disappeared.");
        state.OwnerFrame = frame;
        return state;
    }

    public bool CanScroll(ScrollTargetState state, HumanSwipeDirection direction) => direction switch
    {
        HumanSwipeDirection.Up => state.CanScrollVertically && !state.IsNearBottom,
        HumanSwipeDirection.Down => state.CanScrollVertically && !state.IsNearTop,
        HumanSwipeDirection.Left => state.CanScrollHorizontally && !state.IsNearRight,
        HumanSwipeDirection.Right => state.CanScrollHorizontally && !state.IsNearLeft,
        _ => false
    };

    public async Task<bool> DidScrollAsync(IPage page, ScrollTargetState before, ScrollTargetState documentBefore,
        double x, double y, HumanSwipeDirection direction, double minDelta, CancellationToken token = default)
    {
        var clock = Stopwatch.StartNew();
        var previous = before;
        int stable = 0;
        while(clock.ElapsedMilliseconds < 2500)
        {
            await Task.Delay(80, token);
            var current = await ReadAsync(before.OwnerFrame ?? page.MainFrame, new { x, y, key = before.Key, documentOnly = false }, token);
            if (current.Key != before.Key) return false;
            stable = Math.Abs(current.ScrollLeft-previous.ScrollLeft)<.5 &&
                Math.Abs(current.ScrollTop-previous.ScrollTop)<.5 ? stable+1 : 0;
            previous=current;
            if(stable>=3)
            {
                var delta=direction is HumanSwipeDirection.Left or HumanSwipeDirection.Right ?
                    current.ScrollLeft-before.ScrollLeft : current.ScrollTop-before.ScrollTop;
                return direction is HumanSwipeDirection.Up or HumanSwipeDirection.Left ? delta>=minDelta : delta<=-minDelta;
            }
        }
        return false;
    }

    public async Task<ElementRect?> GetElementRectAsync(ILocator locator)
    {
        var b=await locator.BoundingBoxAsync(new() { Timeout=2000 }).WaitAsync(TimeSpan.FromSeconds(3));
        return b==null ? null : new() { X=b.X, Y=b.Y, Width=b.Width, Height=b.Height };
    }
    public async Task<ElementRect?> GetElementRectAsync(IElementHandle element)
    {
        var b=await element.BoundingBoxAsync().WaitAsync(TimeSpan.FromSeconds(3));
        return b==null ? null : new() { X=b.X, Y=b.Y, Width=b.Width, Height=b.Height };
    }
}
