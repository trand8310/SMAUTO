using Microsoft.Playwright;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput;

public sealed partial class HumanTouchEngine
{
    public async Task<bool> TapAsync(IPage page, ICDPSession session, ILocator target, int dir = 0,
        int timeout = 5000, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var diagnostics = new TapCancellationDiagnostics(log, cancellationToken, deadline.Token, timeout);
        cancellationToken = deadline.Token;
        cancellationToken.ThrowIfCancellationRequested();
        diagnostics.EnterStage("resolve locator");
        await using var handle = await target.ElementHandleAsync(new() { Timeout = timeout }).WaitAsync(cancellationToken);
        diagnostics.EnterStage("tap resolved element");
        return handle != null && await TapAsync(page, session, handle, dir, timeout, log, cancellationToken);
    }

    public async Task<bool> TapAsync(IPage page, ICDPSession session, IElementHandle target, int dir = 0,
        int timeout = 5000, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var diagnostics = new TapCancellationDiagnostics(log, cancellationToken, deadline.Token, timeout);
        cancellationToken = deadline.Token;
        diagnostics.EnterStage("wait for input lock");
        using var input = await HumanInputCoordinator.AcquireAsync(Session, cancellationToken);
        Validate(page, session);
        if (IsActivePage?.Invoke(page) == false) return false;
        diagnostics.EnterStage("resolve owner frame");
        var owner = await target.OwnerFrameAsync().WaitAsync(cancellationToken);
        if (owner == null || !ReferenceEquals(owner.Page, page))
            throw new InvalidOperationException("The tap target belongs to another page.");
        diagnostics.EnterStage("initialize touch");
        await CdpTouchRuntime.InitializeAsync(page, session, Session.DeviceProfile.MaxTouchPoints, cancellationToken);
        diagnostics.EnterStage("wait for touch session lock");
        using var lease = await CdpTouchRuntime.AcquireAsync(session, cancellationToken);
        diagnostics.EnterStage("prepare action rhythm");
        await Rhythm.PrepareAsync(Session, log, cancellationToken);
        diagnostics.EnterStage("check visibility and enabled state");
        if (!await target.IsVisibleAsync().WaitAsync(cancellationToken) || !await target.IsEnabledAsync().WaitAsync(cancellationToken)) return false;
        diagnostics.EnterStage("read bounding box");
        var box = await target.BoundingBoxAsync().WaitAsync(cancellationToken);
        if (box == null) return false;
        diagnostics.EnterStage("read viewport");
        var viewport = await GetEffectiveViewportAsync(page).WaitAsync(cancellationToken);
        double left = Math.Max(0, box.X), right = Math.Min(viewport.Width, box.X + box.Width);
        double top = Math.Max(0, box.Y), bottom = Math.Min(viewport.Height, box.Y + box.Height);
        if (right - left < 1 || bottom - top < 1) return false;
        double preferredX = Session.UserProfile.Handedness == HumanHandedness.Right ? .54 : .46;
        double x = left + (right - left) * RandomMath.TruncatedNormal(Session.Random, preferredX, .08, .3, .7);
        double ratio = dir switch { 1 => 0.3, 3 => 0.7, _ => 0.5 };
        double y = top + (bottom - top) * Math.Clamp(ratio + (Session.Random.NextDouble() - 0.5) * 0.2, 0.1, 0.9);
        // Evaluate in the target's frame; bounding boxes are in main-frame coordinates.
        diagnostics.EnterStage("target hit test");
        bool hit = await target.EvaluateAsync<bool>(@"(el,p) => {
            const r=el.getBoundingClientRect();
            const h=el.ownerDocument.elementFromPoint(r.left+r.width*p.rx,r.top+r.height*p.ry);
            return !!h && (h===el || el.contains(h));
        }", new { rx = (x - box.X) / box.Width, ry = (y - box.Y) / box.Height }).WaitAsync(cancellationToken);
        if (!hit) { log?.Invoke("Tap: target is covered"); return false; }
        // Also check iframe ancestors; an overlay outside the frame can cover its target.
        for (var frame = owner; frame.ParentFrame != null; frame = frame.ParentFrame)
        {
            diagnostics.EnterStage("iframe ancestor hit test");
            await using var frameElement = await frame.FrameElementAsync().WaitAsync(cancellationToken);
            var frameBox = await frameElement.BoundingBoxAsync().WaitAsync(cancellationToken);
            if (frameBox == null || frameBox.Width <= 0 || frameBox.Height <= 0) return false;
            bool frameHit = await frameElement.EvaluateAsync<bool>(@"(el,p) => {
                const r=el.getBoundingClientRect();
                const h=el.ownerDocument.elementFromPoint(r.left+r.width*p.rx,r.top+r.height*p.ry);
                return !!h && (h===el || el.contains(h));
            }", new { rx = (x - frameBox.X) / frameBox.Width, ry = (y - frameBox.Y) / frameBox.Height }).WaitAsync(cancellationToken);
            if (!frameHit) return false;
        }
        diagnostics.EnterStage("check target stability");
        await Task.Delay(32, cancellationToken);
        var latest = await target.BoundingBoxAsync().WaitAsync(cancellationToken);
        if (latest == null || Math.Abs(latest.X - box.X) > 1 || Math.Abs(latest.Y - box.Y) > 1 ||
            Math.Abs(latest.Width - box.Width) > 1 || Math.Abs(latest.Height - box.Height) > 1) return false;
        diagnostics.EnterStage("dispatch touch");
        await TapWithinLeaseAsync(session, x, y, log, cancellationToken);
        PublishInput(new(page, "Tap", false, LastTapTrace!, LastTapTrace!.ActualDurationMs));
        return true;
    }

    public async Task<bool> TapAtAsync(IPage page, ICDPSession session, double x, double y,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(5000);
        cancellationToken = deadline.Token;
        using var input = await HumanInputCoordinator.AcquireAsync(Session, cancellationToken);
        Validate(page, session);
        if (IsActivePage?.Invoke(page) == false) return false;
        await CdpTouchRuntime.InitializeAsync(page, session, Session.DeviceProfile.MaxTouchPoints, cancellationToken);
        using var lease = await CdpTouchRuntime.AcquireAsync(session, cancellationToken);
        await Rhythm.PrepareAsync(Session, null, cancellationToken);
        var viewport = await GetEffectiveViewportAsync(page).WaitAsync(cancellationToken);
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= viewport.Width || y >= viewport.Height)
            return false;
        await TapWithinLeaseAsync(session, x, y, null, cancellationToken);
        PublishInput(new(page, "TapAt", false, LastTapTrace!, LastTapTrace!.ActualDurationMs));
        return true;
    }

    private async Task TapWithinLeaseAsync(ICDPSession session, double x, double y, Action<string>? log, CancellationToken token)
    {
        var plan = new HumanTapPlanner().Plan(Session, x, y);
        var elapsed = Stopwatch.StartNew();
        await _dispatcher.DispatchWithinLeaseAsync(session, plan.Samples, plan.Gesture, Session.DeviceProfile, token);
        Session.RecordTap();
        LastTapTrace = new HumanTapTrace
        {
            X = x, Y = y, PlannedHoldMs = plan.HoldMs, ActualDurationMs = elapsed.Elapsed.TotalMilliseconds,
            PeakForce = plan.Samples[1].Force, RadiusX = plan.Samples[1].RadiusX, RadiusY = plan.Samples[1].RadiusY
        };
        log?.Invoke($"Tap: ({x:0.0},{y:0.0}), planned={plan.HoldMs}ms, actual={LastTapTrace.ActualDurationMs:0}ms, force={LastTapTrace.PeakForce:0.00}");
    }
}
