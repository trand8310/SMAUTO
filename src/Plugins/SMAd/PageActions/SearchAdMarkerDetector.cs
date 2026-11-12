using Microsoft.Playwright;

namespace SMAd.PageActions;

public enum SearchAdDetectionStatus { Found, ConfirmedAbsent, Uncertain }
public sealed record SearchAdDetection(SearchAdDetectionStatus Status, int Count, string Reason);

/// <summary>Count existing business markers; do not treat an unrecognised visible ad label as absence.</summary>
public static class SearchAdMarkerDetector
{
    public const string Selector = "div[ad_dot_url^='http'],div.ad-wolong-container:has(a[data-url^='http'])";

    public static async Task<SearchAdDetection> DetectAsync(IPage page, bool previouslyObserved,
        Func<bool> bindingValid, CancellationToken token)
    {
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (page.IsClosed || !bindingValid()) return new(SearchAdDetectionStatus.Uncertain, 0, "page_binding_changed");
                string url = page.Url;
                int count = await page.Locator(Selector).CountAsync().WaitAsync(TimeSpan.FromSeconds(3), token);
                if (page.IsClosed || !bindingValid() || page.Url != url) return new(SearchAdDetectionStatus.Uncertain, 0, "page_changed");
                if (count > 0) return new(SearchAdDetectionStatus.Found, count, "matched_business_marker");

                bool visibleLabel = await page.EvaluateAsync<bool>(@"() => Array.from(document.querySelectorAll('body *')).some(el => {
                    if (el.children.length || !/^(广告|推广)$/.test((el.textContent || '').trim())) return false;
                    const r = el.getBoundingClientRect();
                    if (r.width <= 1 || r.height <= 1 || r.bottom <= 0 || r.top >= innerHeight || r.right <= 0 || r.left >= innerWidth) return false;
                    for (let p = el; p; p = p.parentElement) {
                        const s = getComputedStyle(p);
                        if (s.display === 'none' || s.visibility !== 'visible' || Number(s.opacity) === 0) return false;
                    }
                    return true;
                })").WaitAsync(TimeSpan.FromSeconds(3), token);
                if (page.IsClosed || !bindingValid() || page.Url != url) return new(SearchAdDetectionStatus.Uncertain, 0, "page_changed");
                if (!visibleLabel && !previouslyObserved) return new(SearchAdDetectionStatus.ConfirmedAbsent, 0, "no_marker_or_visible_label");
                if (attempt < 2) await Task.Delay(350, token);
                else return new(SearchAdDetectionStatus.Uncertain, 0, visibleLabel ? "visible_label_without_marker" : "previous_marker_no_longer_matched");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException) when (!page.IsClosed && bindingValid() && page.Context.Browser?.IsConnected != false)
        { return new(SearchAdDetectionStatus.Uncertain, 0, "check_timeout"); }
        catch (PlaywrightException ex) when (!page.IsClosed && bindingValid() && page.Context.Browser?.IsConnected != false &&
            !ex.Message.Contains("closed", StringComparison.OrdinalIgnoreCase) && !ex.Message.Contains("crash", StringComparison.OrdinalIgnoreCase))
        { return new(SearchAdDetectionStatus.Uncertain, 0, "page_check_failed"); }
        return new(SearchAdDetectionStatus.Uncertain, 0, "unknown");
    }
}
