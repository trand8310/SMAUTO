using System.Diagnostics;
using Microsoft.Playwright;

namespace HumanTouchP3.Input;

public sealed record ScrollObservation(string TargetKind, bool MovementObserved, bool Settled,
    double InitialTop, double FinalTop, double ElapsedMs);

public sealed partial class HumanTouchEngine
{
    public async Task PauseReadingAsync(IPage page, ICDPSession cdp, TimeSpan duration, CancellationToken token = default)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        using var input = await HumanInputCoordinator.AcquireAsync(Session, token);
        Validate(page, cdp);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < duration)
        {
            token.ThrowIfCancellationRequested();
            if (page.IsClosed || !IsCurrentBinding(page, cdp)) throw new InvalidOperationException("Page closed or binding changed.");
            var left = duration - clock.Elapsed;
            if (left <= TimeSpan.Zero) break;
            await Task.Delay(left < TimeSpan.FromMilliseconds(100) ? left : TimeSpan.FromMilliseconds(100), token);
        }
    }

    /// <summary>Observe the same scroll target while holding the session input coordinator.</summary>
    public async Task<ScrollObservation> ObserveScrollAsync(IPage page, ICDPSession cdp, double x, double y,
        TimeSpan timeout, CancellationToken token = default)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var input = await HumanInputCoordinator.AcquireAsync(Session, token);
        Validate(page, cdp);
        if (!IsCurrentBinding(page, cdp)) throw new InvalidOperationException("Page binding changed.");
        var initial = await _scrollResolver.GetAtPointAsync(page, x, y, token);
        var last = initial; var stable = 0; bool moved = false;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < timeout)
        {
            token.ThrowIfCancellationRequested();
            if (page.IsClosed || !IsCurrentBinding(page, cdp)) throw new InvalidOperationException("Page closed or binding changed.");
            var remaining = timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(80) ? remaining : TimeSpan.FromMilliseconds(80), token);
            var current = await _scrollResolver.ReadCurrentAsync(page, initial, token);
            var delta = Math.Abs(current.ScrollTop - last.ScrollTop) + Math.Abs(current.ScrollLeft - last.ScrollLeft);
            if (delta >= .5) { moved = true; stable = 0; } else stable++;
            last = current;
            if (stable >= 3) return new(initial.Kind, moved, true, initial.ScrollTop, last.ScrollTop, clock.Elapsed.TotalMilliseconds);
        }
        return new(initial.Kind, moved, false, initial.ScrollTop, last.ScrollTop, clock.Elapsed.TotalMilliseconds);
    }
}
