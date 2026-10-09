using Microsoft.Playwright;
using SMAd.Models;

namespace SMAd.PageActions;

/// <summary>Only optional page actions may recover; task/session cancellation always propagates.</summary>
public static class OptionalPageOperation
{
    public static bool CanContinue(WorkerRunContext ctx, CancellationToken token) =>
        !token.IsCancellationRequested && !ctx.Config.LinkedCts.IsCancellationRequested &&
        ctx.BrowserSession?.Token.IsCancellationRequested != true && !ctx.ProxyFailed && !ctx.PageCrashed &&
        ctx.Page is { IsClosed: false } && (ctx.Browser == null || ctx.Browser.IsConnected) &&
        (!ctx.PageHttpStatuses.TryGetValue(ctx.Page, out var status) || status < 400);

    public static async Task<bool> RunAsync(WorkerRunContext ctx, string action, Func<Task> work,
        CancellationToken token, Action<string> log)
    {
        token.ThrowIfCancellationRequested();
        ctx.Config.LinkedCts.Token.ThrowIfCancellationRequested();
        ctx.BrowserSession?.Token.ThrowIfCancellationRequested();
        if (ctx.Page != null && ctx.PageHttpStatuses.TryGetValue(ctx.Page, out var status) && status >= 400)
        { log($"Optional action skipped: {action}, main document HTTP {status}"); return false; }
        try { await work(); return true; }
        catch (OperationCanceledException ex) when (CanContinue(ctx, token))
        { log($"Optional action skipped: {action}, local timeout: {ex.Message}"); return false; }
        catch (TimeoutException ex) when (CanContinue(ctx, token))
        { log($"Optional action skipped: {action}, timeout: {ex.Message}"); return false; }
        catch (PlaywrightException ex) when (CanContinue(ctx, token) &&
            !ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase) &&
            !ex.Message.Contains("crash", StringComparison.OrdinalIgnoreCase))
        { log($"Optional action skipped: {action}, page action failed: {ex.Message}"); return false; }
    }
}
