using Microsoft.Playwright;
using System;

namespace HumanTouchP3.Input;

public sealed record HumanInputExecution(IPage Page, string Kind, bool EffectVerified, object Trace, double DurationMs = 0);

public sealed partial class HumanTouchEngine
{
    public event Action<HumanInputExecution>? InputExecuted;
    private void PublishInput(HumanInputExecution execution)
    {
        if (InputExecuted == null) return;
        foreach (Action<HumanInputExecution> listener in InputExecuted.GetInvocationList())
        {
            try { listener(execution); }
            catch (Exception ex) { SessionLog?.Invoke($"Input diagnostics failed: {ex.Message}"); }
        }
    }
    public Action<string>? SessionLog { get; set; }
    private void LogTouchDiagnostic(Action<string>? local, string message)
    {
        try { SessionLog?.Invoke(message); } catch { }
        if (!ReferenceEquals(local, SessionLog)) { try { local?.Invoke(message); } catch { } }
    }
}
