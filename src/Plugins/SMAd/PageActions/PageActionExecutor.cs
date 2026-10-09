using System.Diagnostics;
using Microsoft.Playwright;
using SMAd.Models;
using PlaywrightHumanInput;

namespace SMAd.PageActions;

public sealed record PageActionRecord(DateTimeOffset Started, string Intent, string BeforeUrl,
    string AfterUrl, bool Dispatched, bool Succeeded, double DurationMs, string? Reason, ClickOutcome Outcome, object? Trace = null,
    int ObservationDelayMs = 0, double ObservationElapsedMs = 0, double? DestinationReadyMs = null);

/// <summary>Observe before dispatch. Never retry an input with an uncertain outcome.</summary>
public sealed class PageActionExecutor
{
    private readonly WorkerRunContext _ctx;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _actions = new(1, 1);
    public PageActionExecutor(WorkerRunContext ctx, Action<string> log) { _ctx = ctx; _log = log; }

    public async Task<ClickResult> ExecuteAsync(string intent, Func<WorkerRunContext.PageBinding, CancellationToken, Task<bool>> dispatch,
        Func<IPage, CancellationToken, Task>? activate, CancellationToken token,
        Func<IPage, CancellationToken, Task<bool>>? expectedEffect = null, int timeoutMs = 10000)
    {
        var captured = _ctx.ActivePage;
        if (captured == null) return ClickResult.Fail("No active page");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _ctx.Config.LinkedCts.Token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var ct = deadline.Token;
        var started = DateTimeOffset.UtcNow;
        var clock = Stopwatch.StartNew();
        var before = captured.Page.Url;
        var result = ClickResult.Fail("Action not started");
        bool sent = false, acquired = false, changed = false;
        IDisposable? observationInput = null;
        int observationDelayMs = 0;
        var observationClock = new Stopwatch();
        double? destinationReadyMs = null;
        IPage? popup = null;
        int downloaded = 0;
        int navigated = 0;
        EventHandler<IPage> onPopup = (_, page) => Interlocked.CompareExchange(ref popup, page, null);
        EventHandler<IDownload> onDownload = (_, _) => Interlocked.Exchange(ref downloaded, 1);
        EventHandler<IFrame> onNavigation = (_, frame) => { if (ReferenceEquals(frame, captured.Page.MainFrame)) Interlocked.Exchange(ref navigated, 1); };
        try
        {
            // Queueing behind another action's reading pause is not a navigation timeout.
            await _actions.WaitAsync(lifetime.Token); acquired = true;
            deadline.CancelAfter(timeoutMs);
            if (!ReferenceEquals(_ctx.ActivePage, captured) || captured.Page.IsClosed)
                return result = ClickResult.Fail("Active page changed before action");
            if (_ctx.PageHttpStatuses.TryGetValue(captured.Page, out var sourceStatus) && sourceStatus >= 400)
                return result = ClickResult.Fail($"Source HTTP {sourceStatus}");
            captured.Page.Popup += onPopup;
            captured.Page.Download += onDownload;
            captured.Page.FrameNavigated += onNavigation;
            sent = await dispatch(captured, ct);
            if (!sent) return result = ClickResult.Fail("Target was not actionable");
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var newPage = Volatile.Read(ref popup);
                if (newPage != null)
                {
                    changed = true;
                    await WaitReadyAsync(newPage, ct);
                    if (_ctx.PageHttpStatuses.TryGetValue(newPage, out var popupStatus) && popupStatus >= 400)
                        return result = ClickResult.Fail($"Destination HTTP {popupStatus}", sent);
                    if (expectedEffect != null) await WaitExpectedAsync(newPage, expectedEffect, ct);
                    if (activate != null) await activate(newPage, ct);
                    ct.ThrowIfCancellationRequested();
                    deadline.CancelAfter(Timeout.Infinite);
                    destinationReadyMs = clock.Elapsed.TotalMilliseconds;
                    observationInput = await HumanInputCoordinator.AcquireAsync(_ctx.human.Session, lifetime.Token);
                    if (!await ObserveDestinationAsync(newPage))
                        return result = ClickResult.Fail("Destination changed during observation", sent);
                    return result = ClickResult.SuccessNewPage();
                }
                if (Volatile.Read(ref downloaded) != 0) return result = ClickResult.DownloadSuccess();
                if (!captured.Page.IsClosed && (Volatile.Read(ref navigated) != 0 || !string.Equals(captured.Page.Url, before, StringComparison.Ordinal)))
                {
                    changed = true;
                    await WaitReadyAsync(captured.Page, ct);
                    if (_ctx.PageHttpStatuses.TryGetValue(captured.Page, out var status) && status >= 400)
                        return result = ClickResult.Fail($"Destination HTTP {status}", sent);
                    if (expectedEffect != null) await WaitExpectedAsync(captured.Page, expectedEffect, ct);
                    ct.ThrowIfCancellationRequested();
                    deadline.CancelAfter(Timeout.Infinite);
                    destinationReadyMs = clock.Elapsed.TotalMilliseconds;
                    observationInput = await HumanInputCoordinator.AcquireAsync(_ctx.human.Session, lifetime.Token);
                    if (!await ObserveDestinationAsync(captured.Page))
                        return result = ClickResult.Fail("Destination changed during observation", sent);
                    return result = ClickResult.SuccessSamePage();
                }
                if (expectedEffect != null && await expectedEffect(captured.Page, ct))
                    return result = ClickResult.VerifiedEffect();
                if (captured.Page.IsClosed) return result = ClickResult.Fail("Source page closed without a correlated popup", true);
                await Task.Delay(50, ct);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && !_ctx.Config.LinkedCts.IsCancellationRequested)
        {
            return result = changed ? ClickResult.Fail("Destination did not become ready", sent) : sent ? ClickResult.NoNavigation("No expected effect before deadline") : ClickResult.Fail("Action deadline exceeded");
        }
        catch (OperationCanceledException)
        { result = ClickResult.Cancelled(sent); throw; }
        catch (Exception ex)
        {
            return result = ClickResult.Fail(ex.Message, sent);
        }
        finally
        {
            observationClock.Stop();
            observationInput?.Dispose();
            captured.Page.Popup -= onPopup;
            captured.Page.Download -= onDownload;
            captured.Page.FrameNavigated -= onNavigation;
            if (acquired) _actions.Release();
            var record = new PageActionRecord(started, intent, before, popup?.Url ?? captured.Page.Url,
                sent, result.Succeeded, clock.Elapsed.TotalMilliseconds, result.Reason, result.Outcome,
                ObservationDelayMs: observationDelayMs, ObservationElapsedMs: observationClock.Elapsed.TotalMilliseconds,
                DestinationReadyMs: destinationReadyMs);
            _ctx.RecordAction(record);
            _log($"Action {intent}: success={result.Succeeded}, dispatched={sent}, {record.DurationMs:0}ms, {result.Reason}");
        }

        async Task<bool> ObserveDestinationAsync(IPage page)
        {
            var binding = _ctx.ActivePage;
            var url = page.Url;
            observationDelayMs = Random.Shared.Next(2000, 6001);
            _log($"Navigation ready: {destinationReadyMs:0}ms, observe={observationDelayMs}ms, url={url}");
            observationClock.Start();
            try
            {
                while (observationClock.ElapsedMilliseconds < observationDelayMs)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    if (page.IsClosed || !ReferenceEquals(_ctx.ActivePage, binding) || page.Url != url) return false;
                    await Task.Delay((int)Math.Clamp(observationDelayMs - observationClock.ElapsedMilliseconds, 0, 100), lifetime.Token);
                }
                lifetime.Token.ThrowIfCancellationRequested();
                return !page.IsClosed && ReferenceEquals(_ctx.ActivePage, binding) && page.Url == url;
            }
            finally
            {
                observationClock.Stop();
                _ctx.human.Session.RecordObserve(observationClock.Elapsed);
                _log($"Navigation observation: planned={observationDelayMs}ms, actual={observationClock.Elapsed.TotalMilliseconds:0}ms");
            }
        }
    }

    public static async Task WaitReadyAsync(IPage page, CancellationToken token)
    {
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 5000 }).WaitAsync(token);
        await page.Locator("body").WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 }).WaitAsync(token);
    }

    private static async Task WaitExpectedAsync(IPage page, Func<IPage, CancellationToken, Task<bool>> effect, CancellationToken token)
    {
        while (!await effect(page, token)) await Task.Delay(50, token);
    }
}
