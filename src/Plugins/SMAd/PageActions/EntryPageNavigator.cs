using Microsoft.Playwright;

namespace SMAd.PageActions;

public sealed record EntryNavigationResult(bool Opened, bool Downloaded, string? Reason = null, int? HttpStatus = null);

/// <summary>Entry downloads are navigation diagnostics, never a successful page visit.</summary>
public static class EntryPageNavigator
{
    public static async Task<EntryNavigationResult> NavigateAsync(IPage page, string url, int timeoutMs,
        CancellationToken token, Action<string> log)
    {
        int downloads = 0;
        int attachmentResponse = 0;
        var downloadObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<IDownload> onDownload = (_, download) =>
        {
            Interlocked.Increment(ref downloads);
            Volatile.Read(ref downloadObserved).TrySetResult();
            log($"Entry download: url={download.Url}, filename={download.SuggestedFilename}; excluded from business downloads");
        };
        EventHandler<IResponse> onResponse = (_, response) =>
        {
            if (!response.Request.IsNavigationRequest || !ReferenceEquals(response.Request.Frame, page.MainFrame)) return;
            var headers = response.Headers;
            headers.TryGetValue("content-type", out var type);
            headers.TryGetValue("content-disposition", out var disposition);
            headers.TryGetValue("location", out var location);
            if (disposition?.Contains("attachment", StringComparison.OrdinalIgnoreCase) == true)
                Interlocked.Exchange(ref attachmentResponse, 1);
            log($"Entry response: status={response.Status}, url={response.Url}, content-type={type}, content-disposition={disposition}, location={location}");
        };
        page.Download += onDownload;
        page.Response += onResponse;
        try
        {
            // One bounded retry on the same page/browser; never repeatedly launch a browser.
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (page.IsClosed) return new(false, downloads > 0, "Entry page closed");
                Interlocked.Exchange(ref attachmentResponse, 0);
                Volatile.Write(ref downloadObserved, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                try
                {
                    var response = await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = timeoutMs }).WaitAsync(token);
                    if (response is { Status: >= 400 })
                        return new(false, false, $"Entry HTTP {response.Status}: {response.Url}", response.Status);
                    await PageActionExecutor.WaitReadyAsync(page, token);
                    return new(true, false);
                }
                catch (PlaywrightException ex)
                {
                    bool becameDownload = ex.Message.Contains("Download is starting", StringComparison.OrdinalIgnoreCase);
                    if (!becameDownload && ex.Message.Contains("ERR_ABORTED", StringComparison.OrdinalIgnoreCase))
                    {
                        // Some Chromium builds report the abort before delivering the download event.
                        await Task.WhenAny(downloadObserved.Task, Task.Delay(500, token));
                        token.ThrowIfCancellationRequested();
                        becameDownload = downloadObserved.Task.IsCompletedSuccessfully || Volatile.Read(ref attachmentResponse) != 0;
                    }
                    if (!becameDownload) throw;
                    log($"Entry navigation became a download: attempt={attempt}/2, url={url}");
                    if (attempt == 2 || page.IsClosed)
                        return new(false, true, "Entry navigation repeatedly became a download; no page visit completed");
                    await Task.Delay(Random.Shared.Next(800, 1401), token);
                }
            }
            return new(false, downloads > 0, "Entry navigation did not open a page");
        }
        finally
        {
            page.Download -= onDownload;
            page.Response -= onResponse;
        }
    }
}
