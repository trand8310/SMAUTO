# Browser runtime regression checks

Run `dotnet run --project tests/BrowserRuntime.RegressionTests/BrowserRuntime.RegressionTests.csproj`.

Append a local Chromium executable path after `--` to exercise real processes, automatic ports, two CDP connections sharing one provider, blank-page DOM operations, and confirmed process exit. Profiles are created below this test project's artifacts directory. No external website interaction is performed.

The default checks use controlled process/connection doubles to verify races and failure cleanup without relying on browser timing. They also compile the production PipelineRunner source to verify cancellation and producer/consumer failure drain behavior.

Append `--stress` after the executable path for six rounds of two browsers, cancellation and process crash isolation, TTL notification before forced reclaim, real locked-profile cleanup/retry, CDP cache drain, host and owned Node-driver memory/handle samples, and final driver exit. Driver sampling currently targets Windows. Results are written to `artifacts/<id>/report.json`.

Append `--touch-probe` for a standalone raw touch diagnostic and, when raw touch is verified, real v3 Tap and concurrent contact-order checks. A touch compatibility timeout is recorded separately from lifecycle check passes; inspect the JSON `Touch` object, not just the exit code. Chromium 145 headless still reproduces this timeout, while the local PC Chromium 135 fixture verifies touch.
