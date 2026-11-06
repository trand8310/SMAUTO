using Microsoft.Playwright;
using HumanTouchP3.Input;

namespace HumanTouchP3;

public enum HandPosture { RightHandOneHanded, LeftHandOneHanded, LeftHandHoldRightIndex }
public enum BrowseMode { Reading, Normal, Fast }
public sealed record InertiaStopResult(bool InputDispatched, ScrollObservation Before, ScrollObservation? After, string Reason);

/// <summary>One session owns one posture and owns its input coordinator and cancellation handling.</summary>
public sealed class TouchSession
{
    private readonly HumanTouchOperator _operator;
    private readonly Random _strengthRandom;
    public HandPosture Posture { get; }
    public TouchSession(HandPosture posture, int? seed = null)
    {
        Posture = posture;
        _strengthRandom = seed.HasValue ? new Random(seed.Value) : new Random();
        var user = PostureProfiles.Create(posture);
        _operator = new HumanTouchOperator(new() { Session = new HumanTouchSession(user, TouchDeviceProfile.ConservativeDesktopEmulation(), seed) });
    }
    public Task<bool> TapAsync(IPage page, ICDPSession cdp, ILocator target, CancellationToken token = default)
        => _operator.Engine.TapAsync(page, cdp, target, cancellationToken: token);
    private FlingStrength ChooseStrength(FlingStrength? strength)
    {
        if (strength.HasValue)
            return Enum.IsDefined(strength.Value) ? strength.Value : throw new ArgumentOutOfRangeException(nameof(strength));
        lock (_strengthRandom) return (FlingStrength)_strengthRandom.Next(0, 3);
    }
    public Task<HumanSwipeTrace?> SwipeAsync(IPage page, ICDPSession cdp, bool up, BrowseMode mode, CancellationToken token = default, FlingStrength? strength = null)
        => _operator.Engine.SwipeAsync(page, cdp, new HumanTouchRequest {
            Direction = up ? HumanSwipeDirection.Up : HumanSwipeDirection.Down,
            Intent = mode switch { BrowseMode.Reading => SwipeIntent.Reading, BrowseMode.Fast => SwipeIntent.Fling, _ => SwipeIntent.Preview },
            FlingStrength = ChooseStrength(strength)
        }, token);
    public Task PauseReadingAsync(IPage page, ICDPSession cdp, TimeSpan duration, CancellationToken token = default)
        => _operator.Engine.PauseReadingAsync(page, cdp, duration, token);

    public Task<HumanSwipeTrace?> MicroAdjustAsync(IPage page, ICDPSession cdp, bool up, CancellationToken token = default)
        => _operator.Engine.SwipeAsync(page, cdp, new HumanTouchRequest {
            Direction = up ? HumanSwipeDirection.Up : HumanSwipeDirection.Down, Intent = SwipeIntent.MicroAdjust
        }, token);

    public Task<HumanSwipeTrace?> BackReviewAsync(IPage page, ICDPSession cdp, CancellationToken token = default)
        => _operator.Engine.SwipeAsync(page, cdp, new HumanTouchRequest { Direction = HumanSwipeDirection.Down, Intent = SwipeIntent.BackReview }, token);

    public Task<ScrollObservation> ObserveScrollAsync(IPage page, ICDPSession cdp, double x, double y, TimeSpan duration, CancellationToken token = default)
        => _operator.Engine.ObserveScrollAsync(page, cdp, x, y, duration, token);

    public async Task<HumanSwipeTrace?> ScrollContainerAsync(IPage page, ICDPSession cdp, ILocator container,
        bool up, CancellationToken token = default)
    {
        await using var element = await container.ElementHandleAsync().WaitAsync(token);
        if (element == null || !await element.IsVisibleAsync().WaitAsync(token)) return null;
        var box = await element.BoundingBoxAsync().WaitAsync(token);
        if (box == null || box.Width < 24 || box.Height < 24) return null;
        var viewport = await page.EvaluateAsync<int[]>("() => [innerWidth, innerHeight]").WaitAsync(token);
        double left = Math.Max(0, box.X), top = Math.Max(0, box.Y);
        double right = Math.Min(viewport[0], box.X + box.Width), bottom = Math.Min(viewport[1], box.Y + box.Height);
        if (right - left < 24 || bottom - top < 24) return null;
        var resolver = new ScrollTargetResolver();
        var target = await resolver.GetAtPointAsync(page, (left + right) / 2, (top + bottom) / 2, token);
        var key = await element.EvaluateAsync<string?>("el => window.__htp3Scroll?.ids.get(el) ?? null").WaitAsync(token);
        var direction = up ? HumanSwipeDirection.Up : HumanSwipeDirection.Down;
        if (target.Kind != "element" || target.Key != key || !resolver.CanScroll(target, direction)) return null;
        return await _operator.Engine.SwipeInsideElementAsync(page, cdp, element,
            new HumanTouchRequest { Direction = direction, Intent = SwipeIntent.Reading, ExpectedScrollTargetKey = target.Key }, token);
    }

    // The caller must designate a non-interactive pad in a controlled fixture. This is an opt-in,
    // not a way to find or click an arbitrary page location.
    public async Task<InertiaStopResult> StopInertiaAsync(IPage page, ICDPSession cdp, double x, double y,
        ILocator safePad, CancellationToken token = default)
    {
        var before = await ObserveScrollAsync(page, cdp, x, y, TimeSpan.FromMilliseconds(180), token);
        if (!before.MovementObserved) return new(false, before, null, "未检测到滚动，未派发触摸");
        bool safe = await safePad.EvaluateAsync<bool>(@"el => el.isConnected && el.hasAttribute('data-htp-safe-stop') &&
            !el.closest('a,button,input,select,textarea,[role=button],[contenteditable=true]') &&
            !el.querySelector('a,button,input,select,textarea,[role=button],[contenteditable=true]')").WaitAsync(token);
        if (!safe) return new(false, before, null, "未提供安全测试区域");
        bool dispatched = await _operator.Engine.TapAsync(page, cdp, safePad, cancellationToken: token);
        if (!dispatched) return new(false, before, null, "安全区域不可点击或被遮挡");
        var after = await ObserveScrollAsync(page, cdp, x, y, TimeSpan.FromSeconds(2), token);
        return new(true, before, after, after.Settled ? "触摸已派发，随后观测到稳定" : "触摸已派发，观察期内未稳定");
    }

    public Task<HumanSwipeTrace?> FlingForObservationAsync(IPage page, ICDPSession cdp, FlingStrength? strength = null, CancellationToken token = default)
        => _operator.Engine.SwipeAsync(page, cdp, new HumanTouchRequest { Direction = HumanSwipeDirection.Up,
            Intent = SwipeIntent.Fling, FlingStrength = ChooseStrength(strength), VerifyScrollChanged = false }, token);

    public async Task<IReadOnlyList<HumanSwipeTrace>> BrowseAsync(IPage page, ICDPSession cdp, BrowseMode mode, TimeSpan duration, CancellationToken token = default, FlingStrength? strength = null,
        bool includeReadingAdjustments = true)
    {
        if (duration <= TimeSpan.Zero) return Array.Empty<HumanSwipeTrace>();
        var traces = new List<HumanSwipeTrace>();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(duration);
        try
        {
            while (!page.IsClosed)
            {
                var trace = await SwipeAsync(page, cdp, true, mode, deadline.Token, strength);
                if (trace == null || !trace.ScrollChanged)
                {
                    if (mode == BrowseMode.Reading)
                    {
                        var remaining = duration - System.Diagnostics.Stopwatch.GetElapsedTime(started);
                        if (remaining > TimeSpan.Zero) await PauseReadingAsync(page, cdp, remaining, deadline.Token);
                    }
                    break;
                }
                traces.Add(trace);
                int delay = mode switch { BrowseMode.Reading => 1600, BrowseMode.Fast => 350, _ => 800 };
                await PauseReadingAsync(page, cdp, TimeSpan.FromMilliseconds(delay), deadline.Token);
                if (mode == BrowseMode.Reading && includeReadingAdjustments)
                {
                    int choice;
                    lock (_strengthRandom) choice = _strengthRandom.Next(100);
                    HumanSwipeTrace? adjustment = choice switch
                    {
                        < 6 => await BackReviewAsync(page, cdp, deadline.Token),
                        < 22 => await MicroAdjustAsync(page, cdp, true, deadline.Token),
                        _ => null
                    };
                    if (adjustment != null)
                    {
                        traces.Add(adjustment);
                        await PauseReadingAsync(page, cdp, TimeSpan.FromMilliseconds(400), deadline.Token);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested) { }
        return traces;
    }
}
