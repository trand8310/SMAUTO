using Microsoft.Playwright;

namespace SMAd.PageActions;

public sealed record EntryNavigationResult(bool Opened, bool Downloaded, string? Reason = null, int? HttpStatus = null,
    QTP.Common.BrowserStopKind? FailureKind = null);

/// <summary>Entry downloads are navigation diagnostics, never a successful page visit.</summary>
public static class EntryPageNavigator
{
    private sealed record MainResponse(string Url, int Status, bool Attachment);

    public static async Task<EntryNavigationResult> NavigateAsync(IPage page, string url, int timeoutMs,
        CancellationToken token, Action<string> log)
    {
        int downloads = 0;
        int attachmentResponse = 0;
        int mainFrameCommitted = 0;
        long attemptStarted = 0;
        long responseMs = -1;
        long commitMs = -1;
        long domContentLoadedMs = -1;
        long Elapsed() => Volatile.Read(ref attemptStarted) == 0 ? -1 : (long)System.Diagnostics.Stopwatch.GetElapsedTime(Volatile.Read(ref attemptStarted)).TotalMilliseconds;
        MainResponse? mainResponse = null;
        var downloadObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<IDownload> onDownload = (_, download) =>
        {
            Interlocked.Increment(ref downloads);
            Volatile.Read(ref downloadObserved).TrySetResult();
            log($"Entry download: url={download.Url}, filename={download.SuggestedFilename}; excluded from business downloads");
        };
        EventHandler<IResponse> onResponse = (_, response) =>
        {
            try
            {
                if (!response.Request.IsNavigationRequest || !ReferenceEquals(response.Request.Frame, page.MainFrame)) return;
            }
            catch (PlaywrightException) { return; }
            var headers = response.Headers;
            headers.TryGetValue("content-type", out var type);
            headers.TryGetValue("content-disposition", out var disposition);
            headers.TryGetValue("location", out var location);
            if (disposition?.Contains("attachment", StringComparison.OrdinalIgnoreCase) == true)
                Interlocked.Exchange(ref attachmentResponse, 1);
            Volatile.Write(ref mainResponse, new MainResponse(response.Url, response.Status,
                disposition?.Contains("attachment", StringComparison.OrdinalIgnoreCase) == true));
            Interlocked.Exchange(ref responseMs, Elapsed());
            log($"Entry response: status={response.Status}, url={response.Url}, content-type={type}, content-disposition={disposition}, location={location}");
        };
        EventHandler<IFrame> onFrameNavigated = (_, frame) =>
        {
            if (ReferenceEquals(frame, page.MainFrame)) { Interlocked.Exchange(ref mainFrameCommitted, 1); Interlocked.Exchange(ref commitMs, Elapsed()); }
        };
        EventHandler<IPage> onDomContentLoaded = (_, _) =>
        {
            var observed = Volatile.Read(ref mainResponse);
            if (Volatile.Read(ref mainFrameCommitted) != 0 && observed != null && SameDocument(page.Url, observed.Url))
                Interlocked.CompareExchange(ref domContentLoadedMs, Elapsed(), -1);
        };
        page.Download += onDownload;
        page.Response += onResponse;
        page.FrameNavigated += onFrameNavigated;
        page.DOMContentLoaded += onDomContentLoaded;
        try
        {
            // One bounded retry on the same page/browser; never repeatedly launch a browser.
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (page.IsClosed) return new(false, downloads > 0, "Entry page closed");
                Interlocked.Exchange(ref attachmentResponse, 0);
                Interlocked.Exchange(ref mainFrameCommitted, 0);
                Volatile.Write(ref mainResponse, null);
                Volatile.Write(ref downloadObserved, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                Interlocked.Exchange(ref attemptStarted, System.Diagnostics.Stopwatch.GetTimestamp());
                Interlocked.Exchange(ref responseMs, -1);
                Interlocked.Exchange(ref commitMs, -1);
                Interlocked.Exchange(ref domContentLoadedMs, -1);
                log($"Entry attempt start: attempt={attempt}/2, timeoutMs={timeoutMs}");
                long attemptEndMs = -1;
                try
                {
                    var navigationClock = System.Diagnostics.Stopwatch.StartNew();
                    var navigation = PlaywrightTaskWait.WithCancellation(page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeoutMs }), token);
                    if (IsSearchPage(url) && await WaitForVisibleSearchAdAsync(page, navigation,
                            () => Volatile.Read(ref mainResponse), () => Volatile.Read(ref mainFrameCommitted) != 0,
                            () => Volatile.Read(ref downloads) != 0 || Volatile.Read(ref attachmentResponse) != 0, navigationClock, log, token))
                    {
                        // Goto may still be waiting for DOMContentLoaded. Observe its eventual outcome;
                        // input is allowed only after a committed 2xx document and stable visible ad.
                        _ = ObserveLateNavigationAsync(navigation, log);
                        log($"Entry search result ready: visible advertisement confirmed before DOMContentLoaded, elapsedMs={navigationClock.ElapsedMilliseconds}");
                        return new(true, false, "Visible search result ready", Volatile.Read(ref mainResponse)?.Status);
                    }
                    var response = await navigation;
                    if (response is { Status: >= 400 })
                        return new(false, false, $"Entry HTTP {response.Status}: {response.Url}", response.Status);
                    await PageActionExecutor.WaitReadyAsync(page, token);
                    log($"Entry navigation ready: elapsedMs={navigationClock.ElapsedMilliseconds}");
                    return new(true, false);
                }
                catch (Exception ex) when (ex is PlaywrightException || IsTransient(ex))
                {
                    token.ThrowIfCancellationRequested();
                    bool becameDownload = ex.Message.Contains("Download is starting", StringComparison.OrdinalIgnoreCase);
                    if (!becameDownload && ex.Message.Contains("ERR_ABORTED", StringComparison.OrdinalIgnoreCase))
                    {
                        // Some Chromium builds report the abort before delivering the download event.
                        await Task.WhenAny(downloadObserved.Task, Task.Delay(500, token));
                        token.ThrowIfCancellationRequested();
                        becameDownload = downloadObserved.Task.IsCompletedSuccessfully || Volatile.Read(ref attachmentResponse) != 0;
                    }
                    if (ex is PageDocumentException documentError)
                        return new(false, false, documentError.Message, documentError.Status);
                    if (!becameDownload && IsTimeout(ex))
                    {
                        var observed = Volatile.Read(ref mainResponse);
                        if (observed is { Status: >= 400 })
                            return new(false, false, $"Entry HTTP {observed.Status}: {observed.Url}", observed.Status);
                        if (downloads == 0 && Volatile.Read(ref mainFrameCommitted) != 0 && observed is { Status: >= 200 and < 300, Attachment: false } &&
                            SameDocument(page.Url, observed.Url) && await HasVisibleContentAsync(page, token))
                        {
                            token.ThrowIfCancellationRequested();
                            if (!page.IsClosed && Volatile.Read(ref downloads) == 0 &&
                                ReferenceEquals(Volatile.Read(ref mainResponse), observed) && SameDocument(page.Url, observed.Url))
                            {
                                log($"Entry navigation timed out but main document has visible content: status={observed.Status}, url={observed.Url}");
                                return new(true, false, "Entry navigation timed out; visible document accepted", observed.Status);
                            }
                        }
                    }
                    if (!becameDownload && !IsTransient(ex)) throw;
                    log($"Entry navigation {(becameDownload ? "became a download" : "temporarily failed")}: attempt={attempt}/2, url={url}, error={ex.Message}");
                    if (attempt == 2 || page.IsClosed)
                        return becameDownload
                            ? new(false, true, "Entry navigation repeatedly became a download; no page visit completed")
                            : new(false, false, $"Entry navigation failed after bounded retry: {ex.Message}",
                                FailureKind: IsTimeout(ex) ? QTP.Common.BrowserStopKind.OperationTimedOut : QTP.Common.BrowserStopKind.ExecutionFailed);
                    attemptEndMs = Elapsed();
                    await Task.Delay(Random.Shared.Next(800, 1401), token);
                }
                finally
                {
                    log($"Entry attempt end: attempt={attempt}/2, elapsedMs={(attemptEndMs >= 0 ? attemptEndMs : Elapsed())}, responseMs={Volatile.Read(ref responseMs)}, commitMs={Volatile.Read(ref commitMs)}, domContentLoadedMs={Volatile.Read(ref domContentLoadedMs)}, status={Volatile.Read(ref mainResponse)?.Status.ToString() ?? "unknown"}, closed={page.IsClosed}");
                }
            }
            return new(false, downloads > 0, "Entry navigation did not open a page");
        }
        finally
        {
            page.Download -= onDownload;
            page.Response -= onResponse;
            page.FrameNavigated -= onFrameNavigated;
            page.DOMContentLoaded -= onDomContentLoaded;
        }
    }

    private static bool IsTimeout(Exception ex) => ex is TimeoutException || ex.GetType().Name == "TimeoutException";
    private static bool IsSearchPage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.Equals(uri.Host, "wm.m.sm.cn", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> WaitForVisibleSearchAdAsync(IPage page, Task navigation,
        Func<MainResponse?> response, Func<bool> committed, Func<bool> download,
        System.Diagnostics.Stopwatch clock, Action<string> log, CancellationToken token)
    {
        int stable = 0;
        long nextReportMs = 5000;
        string probeStatus = "not_checked";
        int candidates = -1, visibleCount = -1;
        MainResponse? previous = null;
        while (!navigation.IsCompleted)
        {
            token.ThrowIfCancellationRequested();
            if (page.IsClosed) return false;
            var current = response();
            if (committed() && !download() && current is { Status: >= 200 and < 300, Attachment: false } &&
                SameDocument(page.Url, current.Url))
            {
                var probeTask = HasVisibleSearchAdAsync(page, token);
                if (!await WaitForProbeOrNavigationAsync(probeTask, navigation, token))
                {
                    _ = ObserveLateProbeAsync(probeTask);
                    token.ThrowIfCancellationRequested();
                    log($"Entry readiness probe abandoned: elapsedMs={clock.ElapsedMilliseconds}, reason=navigation_completed");
                    return false;
                }
                var probe = await probeTask;
                probeStatus = probe.Status;
                candidates = probe.Candidates;
                visibleCount = probe.Visible;
                bool visible = visibleCount > 0;
                token.ThrowIfCancellationRequested();
                if (visible && !navigation.IsCompleted && !page.IsClosed && !download() &&
                    ReferenceEquals(current, response()) && SameDocument(page.Url, current.Url))
                {
                    stable = ReferenceEquals(previous, current) ? stable + 1 : 1;
                    previous = current;
                    if (stable >= 2)
                    {
                        log($"Entry readiness check accepted: elapsedMs={clock.ElapsedMilliseconds}, candidates={candidates}, visible={visibleCount}, stableSamples={stable}");
                        return true;
                    }
                }
                else { stable = 0; previous = null; }
            }
            if (clock.ElapsedMilliseconds >= nextReportMs)
            {
                log($"Entry wait: elapsedMs={clock.ElapsedMilliseconds}, responseStatus={current?.Status.ToString() ?? "unknown"}, committed={committed()}, urlMatch={(current != null && SameDocument(page.Url, current.Url))}, download={download()}, probe={probeStatus}, candidates={candidates}, visible={visibleCount}, stableSamples={stable}, waiting=DOMContentLoaded_or_visible_ad");
                nextReportMs = clock.ElapsedMilliseconds + 5000;
            }
            await Task.WhenAny(navigation, Task.Delay(250, token));
        }
        token.ThrowIfCancellationRequested();
        log($"Entry readiness check ended: elapsedMs={clock.ElapsedMilliseconds}, probe={probeStatus}, candidates={candidates}, visible={visibleCount}, stableSamples={stable}");
        return false;
    }

    private static async Task ObserveLateProbeAsync(Task probe)
    {
        try { await probe.ConfigureAwait(false); } catch { }
    }

    internal static async Task<bool> WaitForProbeOrNavigationAsync(Task probe, Task navigation, CancellationToken token)
    {
        var completed = await Task.WhenAny(probe, navigation).WaitAsync(token);
        token.ThrowIfCancellationRequested();
        return completed == probe && !navigation.IsCompleted;
    }

    private static async Task<(string Status, int Candidates, int Visible)> HasVisibleSearchAdAsync(IPage page, CancellationToken token)
    {
        try
        {
            var result = await page.EvaluateAsync<int[]>(@"() => { const items = Array.from(document.querySelectorAll(
                'div[ad_dot_url^=""http""],div.ad-wolong-container:has(a[data-url^=""http""])')); const visible = items.filter(el => {
                if (!el.isConnected) return false;
                const r=el.getBoundingClientRect();
                let l=Math.max(0,r.left), t=Math.max(0,r.top), right=Math.min(innerWidth,r.right), bottom=Math.min(innerHeight,r.bottom);
                for (let p=el; p; p=p.parentElement) {
                    const s=getComputedStyle(p);
                    if (s.display==='none' || s.visibility!=='visible' || Number(s.opacity)===0) return false;
                    if (p!==el && p!==document.documentElement && p!==document.scrollingElement) {
                        const b=p.getBoundingClientRect();
                        if (/hidden|clip|auto|scroll/.test(s.overflowX)) { l=Math.max(l,b.left); right=Math.min(right,b.right); }
                        if (/hidden|clip|auto|scroll/.test(s.overflowY)) { t=Math.max(t,b.top); bottom=Math.min(bottom,b.bottom); }
                    }
                }
                return right-l>1 && bottom-t>1;
            }).length; return [items.length, visible]; }").WaitAsync(TimeSpan.FromSeconds(1), token);
            return ("ok", result[0], result[1]);
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException) { return ("timeout", -1, -1); }
        catch (PlaywrightException) { return ("page_error", -1, -1); }
    }

    private static async Task ObserveLateNavigationAsync(Task navigation, Action<string> log)
    {
        try { await navigation.ConfigureAwait(false); }
        catch (Exception ex)
        {
            try { log($"Entry navigation continued after visible result: {ex.GetType().Name}"); } catch { }
        }
    }
    private static bool SameDocument(string current, string response) =>
        Uri.TryCreate(current, UriKind.Absolute, out var a) && Uri.TryCreate(response, UriKind.Absolute, out var b) &&
        (a.Scheme == Uri.UriSchemeHttp || a.Scheme == Uri.UriSchemeHttps) &&
        string.Equals(a.GetLeftPart(UriPartial.Query), b.GetLeftPart(UriPartial.Query), StringComparison.Ordinal);

    private static async Task<bool> HasVisibleContentAsync(IPage page, CancellationToken token)
    {
        if (page.IsClosed) return false;
        try
        {
            return await page.EvaluateAsync<bool>(@"() => {
                const b = document.body;
                if (!b || !b.isConnected) return false;
                const s = getComputedStyle(b), r = b.getBoundingClientRect();
                if (s.display === 'none' || s.visibility === 'hidden' || !r.width || !r.height) return false;
                if ((b.innerText || '').trim()) return true;
                return Array.from(b.querySelectorAll('img,video,canvas,svg,input,textarea,button')).some(el => {
                    const style = getComputedStyle(el), box = el.getBoundingClientRect();
                    return style.display !== 'none' && style.visibility !== 'hidden' && Number(style.opacity) > 0 && box.width > 0 && box.height > 0;
                });
            }").WaitAsync(TimeSpan.FromSeconds(2), token);
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException) { return false; }
        catch (PlaywrightException) { return false; }
    }
    private static bool IsTransient(Exception ex) => IsTimeout(ex) || ex is PlaywrightException &&
        (ex.Message.Contains("ERR_CONNECTION_RESET", StringComparison.Ordinal) ||
         ex.Message.Contains("ERR_EMPTY_RESPONSE", StringComparison.Ordinal) ||
         ex.Message.Contains("ERR_CONNECTION_TIMED_OUT", StringComparison.Ordinal));
}
