using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput;

/// <summary>Serialize touch and keyboard input across pages using the same human session.</summary>
public static class HumanInputCoordinator
{
    private static readonly ConditionalWeakTable<HumanTouchSession, SemaphoreSlim> Gates = new();
    public static async Task<IDisposable> AcquireAsync(HumanTouchSession session, CancellationToken token)
    {
        var gate = Gates.GetValue(session, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        return new Lease(gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
