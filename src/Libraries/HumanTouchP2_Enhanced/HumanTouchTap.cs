using Microsoft.Playwright;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput;

public sealed partial class HumanTouchEngine
{
    public async Task<bool> TapAsync(IPage page, ICDPSession session, ILocator target, int dir = 0,
        int timeout = 5000, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var handle = await target.ElementHandleAsync(new() { Timeout = timeout });
        return handle != null && await TapAsync(page, session, handle, dir, timeout, log, cancellationToken);
    }

    public async Task<bool> TapAsync(IPage page, ICDPSession session, IElementHandle target, int dir = 0,
        int timeout = 5000, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        Validate(page, session);
        var owner = await target.OwnerFrameAsync();
        if (owner == null || !ReferenceEquals(owner.Page, page))
            throw new InvalidOperationException("The tap target belongs to another page.");
        await CdpTouchRuntime.InitializeAsync(page, session, Session.DeviceProfile.MaxTouchPoints, cancellationToken);
        using var lease = await CdpTouchRuntime.AcquireAsync(session, cancellationToken);
        if (!await target.IsVisibleAsync() || !await target.IsEnabledAsync()) return false;
        var box = await target.BoundingBoxAsync();
        if (box == null) return false;
        var viewport = await GetEffectiveViewportAsync(page);
        double left = Math.Max(0, box.X), right = Math.Min(viewport.Width, box.X + box.Width);
        double top = Math.Max(0, box.Y), bottom = Math.Min(viewport.Height, box.Y + box.Height);
        if (right - left < 1 || bottom - top < 1) return false;
        double x = left + (right - left) * (0.3 + Session.Random.NextDouble() * 0.4);
        double ratio = dir switch { 1 => 0.3, 3 => 0.7, _ => 0.5 };
        double y = top + (bottom - top) * Math.Clamp(ratio + (Session.Random.NextDouble() - 0.5) * 0.2, 0.1, 0.9);
        // Evaluate in the target's frame; bounding boxes are in main-frame coordinates.
        bool hit = await target.EvaluateAsync<bool>(@"(el,p) => {
            const r=el.getBoundingClientRect();
            const h=el.ownerDocument.elementFromPoint(r.left+r.width*p.rx,r.top+r.height*p.ry);
            return h && (h===el || el.contains(h));
        }", new { rx = (x - box.X) / box.Width, ry = (y - box.Y) / box.Height });
        if (!hit) { log?.Invoke("Tap: target is covered"); return false; }
        // Also check iframe ancestors; an overlay outside the frame can cover its target.
        for (var frame = owner; frame.ParentFrame != null; frame = frame.ParentFrame)
        {
            await using var frameElement = await frame.FrameElementAsync();
            var frameBox = await frameElement.BoundingBoxAsync();
            if (frameBox == null || frameBox.Width <= 0 || frameBox.Height <= 0) return false;
            bool frameHit = await frameElement.EvaluateAsync<bool>(@"(el,p) => {
                const r=el.getBoundingClientRect();
                const h=el.ownerDocument.elementFromPoint(r.left+r.width*p.rx,r.top+r.height*p.ry);
                return h && (h===el || el.contains(h));
            }", new { rx = (x - frameBox.X) / frameBox.Width, ry = (y - frameBox.Y) / frameBox.Height });
            if (!frameHit) return false;
        }
        var latest = await target.BoundingBoxAsync();
        if (latest == null || Math.Abs(latest.X - box.X) > 1 || Math.Abs(latest.Y - box.Y) > 1 ||
            Math.Abs(latest.Width - box.Width) > 1 || Math.Abs(latest.Height - box.Height) > 1) return false;
        await TapWithinLeaseAsync(session, x, y, log, cancellationToken);
        return true;
    }

    public async Task<bool> TapAtAsync(IPage page, ICDPSession session, double x, double y,
        CancellationToken cancellationToken = default)
    {
        Validate(page, session);
        await CdpTouchRuntime.InitializeAsync(page, session, Session.DeviceProfile.MaxTouchPoints, cancellationToken);
        using var lease = await CdpTouchRuntime.AcquireAsync(session, cancellationToken);
        var viewport = await GetEffectiveViewportAsync(page);
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= viewport.Width || y >= viewport.Height)
            return false;
        await TapWithinLeaseAsync(session, x, y, null, cancellationToken);
        return true;
    }

    private async Task TapWithinLeaseAsync(ICDPSession session, double x, double y, Action<string>? log, CancellationToken token)
    {
        var point = new PointD(x, y);
        int hold = Session.Random.Next(45, 105);
        await _dispatcher.DispatchWithinLeaseAsync(session, new[] { new TouchSample
        {
            Point = point, RadiusX = 4, RadiusY = 4, Force = 0.6
        } }, new GesturePlan { Start = point, End = point, StartHoldMs = hold }, Session.DeviceProfile, token);
        log?.Invoke($"Tap: ({x:0.0},{y:0.0}), hold={hold}ms");
    }
}
