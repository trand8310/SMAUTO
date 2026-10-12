using System.Text.Json;
using System.Diagnostics;
using Microsoft.Playwright;

namespace SMAd.DeviceEmulation;

public static class DevicePageDisplay
{
    public static async Task<JsonElement> ApplyAsync(ICDPSession session, DeviceDisplayProfile display, CancellationToken token)
    {
        // Screen, DPR and viewport are supplied by the custom Chromium kernel.
        var window = await session.SendAsync("Browser.getWindowForTarget").WaitAsync(TimeSpan.FromSeconds(5), token);
        int windowId = window!.Value.GetProperty("windowId").GetInt32();
        await session.SendAsync("Browser.setWindowBounds", new Dictionary<string, object>
        { ["windowId"] = windowId, ["bounds"] = new { windowState = "normal" } }).WaitAsync(TimeSpan.FromSeconds(5), token);
        await session.SendAsync("Browser.setWindowBounds", new Dictionary<string, object>
        { ["windowId"] = windowId, ["bounds"] = new { width = display.WindowWidth, height = display.WindowHeight } }).WaitAsync(TimeSpan.FromSeconds(5), token);
        JsonElement? actual;
        long started = Stopwatch.GetTimestamp();
        int requestedWidth = display.WindowWidth, requestedHeight = display.WindowHeight, adjustments = 0;
        await Task.Delay(100, token);
        do
        {
            actual = await session.SendAsync("Browser.getWindowBounds", new Dictionary<string, object> { ["windowId"] = windowId }).WaitAsync(TimeSpan.FromSeconds(5), token);
            var bounds = actual!.Value.GetProperty("bounds");
            if (bounds.GetProperty("width").GetInt32() == display.WindowWidth && bounds.GetProperty("height").GetInt32() == display.WindowHeight) break;
            if (Stopwatch.GetElapsedTime(started) >= TimeSpan.FromSeconds(2)) break;
            // Some Windows/headless kernels report a frame border in addition to the requested size.
            // Correct the measured delta; a constrained window manager still keeps its actual bounds.
            if (adjustments++ < 3)
            {
                requestedWidth = Math.Max(1, requestedWidth + display.WindowWidth - bounds.GetProperty("width").GetInt32());
                requestedHeight = Math.Max(1, requestedHeight + display.WindowHeight - bounds.GetProperty("height").GetInt32());
                await session.SendAsync("Browser.setWindowBounds", new Dictionary<string, object>
                { ["windowId"] = windowId, ["bounds"] = new { width = requestedWidth, height = requestedHeight } }).WaitAsync(TimeSpan.FromSeconds(5), token);
            }
            await Task.Delay(100, token);
        } while (true);
        // Read-only verification. Never patch Screen properties when the kernel cannot supply them.
        var check = await session.SendAsync("Runtime.evaluate", new Dictionary<string, object>
        {
            ["expression"] = "({width:screen.width,height:screen.height,availWidth:screen.availWidth,availHeight:screen.availHeight,dpr:devicePixelRatio})",
            ["returnByValue"] = true
        }).WaitAsync(TimeSpan.FromSeconds(5), token);
        if (!check!.Value.GetProperty("result").TryGetProperty("value", out var metrics) ||
            metrics.ValueKind != JsonValueKind.Object ||
            !Matches(metrics, "width", display.ScreenWidth) || !Matches(metrics, "height", display.ScreenHeight) ||
            !Matches(metrics, "availWidth", display.AvailableWidth) || !Matches(metrics, "availHeight", display.AvailableHeight) ||
            !Matches(metrics, "dpr", display.Dpr, 0.001))
            throw new QTP.Common.BrowserFailureException(QTP.Common.BrowserStopKind.DeviceConfigurationFailed,
                $"Native display mismatch: expected screen={display.ScreenWidth}x{display.ScreenHeight}, available={display.AvailableWidth}x{display.AvailableHeight}, DPR={display.DprArgument}; " +
                $"actual={check.Value.GetRawText()}. The kernel must implement these values; JavaScript fallback is disabled.");
        return actual!.Value.Clone();
    }

    private static bool Matches(JsonElement metrics, string name, double expected, double tolerance = 0) =>
        metrics.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) &&
        double.IsFinite(number) && Math.Abs(number - expected) <= tolerance;
}
