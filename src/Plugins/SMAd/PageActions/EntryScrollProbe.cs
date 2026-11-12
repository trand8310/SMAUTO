using Microsoft.Playwright;

namespace SMAd.PageActions;

/// <summary>Conservative read-only check: uncertainty must retain normal browsing.</summary>
public static class EntryScrollProbe
{
    public static async Task<bool?> HasScrollSpaceAsync(IPage page, CancellationToken token)
    {
        try
        {
            return await page.EvaluateAsync<bool?>(@"() => {
                const root = document.scrollingElement;
                if (!root || !document.body || document.querySelector('iframe,frame')) return null;
                if (root.scrollHeight > root.clientHeight + 2 || root.scrollWidth > root.clientWidth + 2) return true;
                for (const el of document.querySelectorAll('body *')) {
                    if (el === root) continue;
                    const s = getComputedStyle(el), r = el.getBoundingClientRect();
                    if (s.display === 'none' || s.visibility !== 'visible' || Number(s.opacity) === 0 || r.width <= 0 || r.height <= 0) continue;
                    if ((/(auto|scroll)/.test(s.overflowY) && el.scrollHeight > el.clientHeight + 2) ||
                        (/(auto|scroll)/.test(s.overflowX) && el.scrollWidth > el.clientWidth + 2)) return true;
                }
                return false;
            }").WaitAsync(TimeSpan.FromSeconds(2), token);
        }
        catch (OperationCanceledException) { throw; }
        catch (TimeoutException) { return null; }
        catch (PlaywrightException) { return null; }
    }
}
