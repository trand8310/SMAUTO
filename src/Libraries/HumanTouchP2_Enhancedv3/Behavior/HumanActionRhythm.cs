using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput;

/// <summary>Spacing between inputs; elapsed observation time counts toward the gap.</summary>
public sealed class HumanActionRhythm
{
    public bool Enabled { get; set; } = true;

    public TimeSpan NextPause(HumanTouchSession session)
    {
        if (!Enabled) return TimeSpan.Zero;
        session.RecoverToNow();
        var user = session.UserProfile;
        double median = 180 * Math.Clamp(user.ReactionBias * user.PauseBias, .5, 2)
            * (1 + session.Fatigue * .5);
        if (session.LastInputWasSwipe && session.LastIntent is SwipeIntent.Fling or SwipeIntent.FastScan)
            median *= 1.35;
        double desired = RandomMath.LogNormal(session.Random, median, .20, 80, 600);
        double elapsed = session.IdleSinceInput?.TotalMilliseconds ?? 0;
        return TimeSpan.FromMilliseconds(Math.Max(0, desired - elapsed));
    }

    internal async Task PrepareAsync(HumanTouchSession session, Action<string>? log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var delay = NextPause(session);
        if (delay <= TimeSpan.Zero) return;
        log?.Invoke($"Prepare: {delay.TotalMilliseconds:0}ms");
        await Task.Delay(delay, token);
    }
}
