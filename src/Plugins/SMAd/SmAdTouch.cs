using Microsoft.Playwright;
using System.Text;
using PlaywrightHumanInput;
using System.Runtime.CompilerServices;

namespace SMAd;

public static class SmAdTouch
{
    public static bool AllowDirectScrollFallback { get; set; }
    private sealed record Binding(ICDPSession Session, HumanTouchOperator Human, CancellationToken Token);
    private static readonly ConditionalWeakTable<IPage, Binding> Bindings = new();
    public static void Bind(IPage page, ICDPSession session, CancellationToken token, HumanTouchOperator? human = null)
    {
        lock (Bindings)
        {
            Bindings.Remove(page);
            Bindings.Add(page, new(session, human ?? new(), token));
        }
    }
    private static Binding Get(IPage page, ICDPSession? session = null)
    {
        if (!Bindings.TryGetValue(page, out var b) || (session != null && !ReferenceEquals(b.Session, session)))
            throw new InvalidOperationException("The page has no matching touch session.");
        return b;
    }
    public static Task<bool> TapAsync(IPage page, ICDPSession session, ILocator target, int dir = 0,
        int timeout = 5000, Action<string>? action = null)
    {
        var b = Get(page, session);
        return TryTapAsync(page, b, () => b.Human.Engine.TapAsync(page, session, target, dir, timeout,
            action ?? b.Human.Options.Log, b.Token), b.Token, action);
    }
    public static Task<bool> TapAsync(IPage page, ICDPSession session, IElementHandle target, int dir = 0,
        int timeout = 5000, Action<string>? action = null)
    {
        var b = Get(page, session);
        return TryTapAsync(page, b, () => b.Human.Engine.TapAsync(page, session, target, dir, timeout,
            action ?? b.Human.Options.Log, b.Token), b.Token, action);
    }
    public static async Task<bool> TapAsync(IPage page, ILocator target, CancellationToken token = default)
    {
        var b = Get(page);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, b.Token);
        return await TryTapAsync(page, b, () => b.Human.Engine.TapAsync(page, b.Session, target,
            cancellationToken: linked.Token), linked.Token);
    }

    private static async Task<bool> TryTapAsync(IPage page, Binding binding, Func<Task<bool>> tap,
        CancellationToken token, Action<string>? log = null)
    {
        token.ThrowIfCancellationRequested();
        try { return await tap(); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested &&
            !binding.Token.IsCancellationRequested && !page.IsClosed)
        {
            (log ?? binding.Human.Options.Log)?.Invoke("Tap skipped: local deadline exceeded; task remains active");
            return false;
        }
        catch (TimeoutException ex) when (!token.IsCancellationRequested &&
            !binding.Token.IsCancellationRequested && !page.IsClosed)
        {
            (log ?? binding.Human.Options.Log)?.Invoke($"Tap skipped: {ex.Message}");
            return false;
        }
        catch (PlaywrightException ex) when (!token.IsCancellationRequested &&
            !binding.Token.IsCancellationRequested && !page.IsClosed &&
            (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("not attached", StringComparison.OrdinalIgnoreCase)))
        {
            (log ?? binding.Human.Options.Log)?.Invoke($"Tap skipped: {ex.Message}");
            return false;
        }
    }

    public static Task<bool> FindItemAndClickAsync(IPage page, ICDPSession session, string selector, int dir = 0,
        int timeout = 5000, Action<string>? action = null)
        => TapAsync(page, session, page.Locator(selector).First, dir, timeout, action);

    public static async Task ReplaceTextAsync(IPage page, ILocator target, string text, CancellationToken token)
    {
        var b = Get(page);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, b.Token);
        deadline.CancelAfter(10000);
        var ct = deadline.Token;
        using var input = await HumanInputCoordinator.AcquireAsync(b.Human.Session, ct);
        if (b.Human.Engine.IsActivePage?.Invoke(page) == false) throw new InvalidOperationException("Active page changed before keyboard input.");
        using var lease = await CdpTouchRuntime.AcquireAsync(b.Session, ct);
        // Each key has a bounded native timeout. Cancellation cannot leave a background typing task.
        await target.FillAsync("", new() { Timeout = 2000 });
        foreach (var character in text.EnumerateRunes())
        {
            ct.ThrowIfCancellationRequested();
            await target.PressSequentiallyAsync(character.ToString(), new() { Delay = 0, Timeout = 1000 });
        }
        var actual = await target.InputValueAsync(new() { Timeout = 2000 }).WaitAsync(ct);
        if (!string.Equals(actual, text, StringComparison.Ordinal))
            throw new InvalidOperationException("Input value differs from the requested text.");
        b.Human.Options.Log?.Invoke($"Input verified: {text.Length} characters");
    }

    public static async Task<bool> ScrollFallbackAsync(IPage page, ILocator target, CancellationToken token)
    {
        var b = Get(page);
        if (!AllowDirectScrollFallback) { b.Human.Options.Log?.Invoke("Direct scroll fallback disabled"); return false; }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, b.Token);
        deadline.CancelAfter(5000);
        using var input = await HumanInputCoordinator.AcquireAsync(b.Human.Session, deadline.Token);
        if (b.Human.Engine.IsActivePage?.Invoke(page) == false) return false;
        using var lease = await CdpTouchRuntime.AcquireAsync(b.Session, deadline.Token);
        await target.ScrollIntoViewIfNeededAsync(new() { Timeout = 2000 });
        var previous = await target.BoundingBoxAsync(new() { Timeout = 1000 }).WaitAsync(deadline.Token);
        for (int i = 0, stable = 0; i < 15; i++)
        {
            await Task.Delay(80, deadline.Token);
            var current = await target.BoundingBoxAsync(new() { Timeout = 1000 }).WaitAsync(deadline.Token);
            if (current == null || previous == null) return false;
            stable = Math.Abs(current.X - previous.X) < .5 && Math.Abs(current.Y - previous.Y) < .5 ? stable + 1 : 0;
            previous = current;
            if (stable >= 3) { b.Human.Options.Log?.Invoke("Direct scroll fallback: layout stable"); return true; }
        }
        return false;
    }
}
