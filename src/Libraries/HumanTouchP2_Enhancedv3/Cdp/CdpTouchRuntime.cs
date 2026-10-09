using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput;

/// <summary>One input stream and one touch configuration per CDP session.</summary>
public static class CdpTouchRuntime
{
    private sealed class State
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public IPage? Page;
        public bool Initialized;
        public volatile bool Closed;
    }
    private static readonly ConditionalWeakTable<ICDPSession, State> States = new();

    public sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _gate;
        internal Lease(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }

    public static async Task<Lease> AcquireAsync(ICDPSession session, CancellationToken token = default)
    {
        var state = States.GetValue(session, _ => new State());
        await state.Gate.WaitAsync(token);
        if (state.Closed || state.Page?.IsClosed == true)
        {
            state.Gate.Release();
            throw new InvalidOperationException("The touch session is closed.");
        }
        return new Lease(state.Gate);
    }

    public static async Task InitializeAsync(IPage page, ICDPSession session, int maxTouchPoints = 5,
        CancellationToken token = default)
    {
        using var lease = await AcquireAsync(session, token);
        var state = States.GetValue(session, _ => new State());
        if (state.Page != null && !ReferenceEquals(state.Page, page))
            throw new InvalidOperationException("The CDP session belongs to another page.");
        if (page.IsClosed) throw new InvalidOperationException("The page is closed.");
        state.Page = page;
        if (state.Initialized) return;
        await session.SendAsync("Input.setIgnoreInputEvents", new Dictionary<string, object> { ["ignore"] = false }).WaitAsync(TimeSpan.FromSeconds(5));
        await session.SendAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object>
        {
            ["enabled"] = true, ["maxTouchPoints"] = Math.Clamp(maxTouchPoints, 1, 16)
        }).WaitAsync(TimeSpan.FromSeconds(5));
        state.Initialized = true;
    }

    public static void Invalidate(ICDPSession session) => States.GetValue(session, _ => new State()).Closed = true;

    public static async Task<Lease> StopAsync(ICDPSession session)
    {
        var state = States.GetValue(session, _ => new State());
        state.Closed = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await state.Gate.WaitAsync(timeout.Token);
        return new Lease(state.Gate);
    }

    public static async Task ReleaseTouchAsync(ICDPSession session, bool canceled = false)
    {
        // Cleanup ignores caller cancellation, but must not hang worker shutdown.
        try
        {
            await session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = canceled ? "touchCancel" : "touchEnd",
                ["touchPoints"] = Array.Empty<object>(), ["modifiers"] = 0
            }).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch { Invalidate(session); throw; }
    }
}
