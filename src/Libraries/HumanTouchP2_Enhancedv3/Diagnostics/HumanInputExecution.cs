using Microsoft.Playwright;
using System;

namespace PlaywrightHumanInput;

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
}
