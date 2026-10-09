using System;
using System.Diagnostics;
using System.Threading;

namespace PlaywrightHumanInput;

internal sealed class TapCancellationDiagnostics : IDisposable
{
    private static long _nextId;
    private readonly long _id = Interlocked.Increment(ref _nextId);
    private readonly Action<string>? _log;
    private readonly CancellationToken _upstream;
    private readonly CancellationToken _deadline;
    private readonly CancellationTokenRegistration _registration;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly int _timeout;
    private string _stage = "start";
    private string? _canceledStage;
    private string? _cancellationSource;
    private long _canceledElapsed;

    public TapCancellationDiagnostics(Action<string>? log, CancellationToken upstream,
        CancellationToken deadline, int timeout)
    {
        _log = log;
        _upstream = upstream;
        _deadline = deadline;
        _timeout = timeout;
        _registration = deadline.Register(() =>
        {
            // Cancellation callbacks only capture state. Logging here could block
            // cancellation propagation and delay releasing the input locks.
            _canceledStage = Volatile.Read(ref _stage);
            _canceledElapsed = _elapsed.ElapsedMilliseconds;
            _cancellationSource = _upstream.IsCancellationRequested ? "upstream cancellation" : "tap deadline";
        });
    }

    public void EnterStage(string stage)
    {
        _deadline.ThrowIfCancellationRequested();
        Volatile.Write(ref _stage, stage);
        Write($"Tap[{_id}] stage: {stage}, elapsed={_elapsed.ElapsedMilliseconds}ms, budget={_timeout}ms");
        _deadline.ThrowIfCancellationRequested();
    }

    private void Write(string message)
    {
        Trace.WriteLine(message);
        // Diagnostic callbacks must not interrupt cancellation or touch cleanup.
        try { _log?.Invoke(message); }
        catch (Exception ex) { Trace.WriteLine($"Tap diagnostic logger failed: {ex.Message}"); }
    }

    public void Dispose()
    {
        _registration.Dispose();
        if (_canceledStage != null)
            Write($"Tap[{_id}] canceled: source={_cancellationSource}, stage={_canceledStage}, " +
                $"elapsed={_canceledElapsed}ms, budget={_timeout}ms");
    }
}
