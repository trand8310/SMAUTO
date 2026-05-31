using Microsoft.Playwright;
using System.Diagnostics;
namespace BDAd
{
    public class AdHelper
    {

        public static bool IsBlockedMediaUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath.ToLowerInvariant();
                return path.EndsWith(".mp4") ||
                       path.EndsWith(".webm") ||
                       path.EndsWith(".m3u8") ||
                       path.EndsWith(".ts") ||
                       path.EndsWith(".m4s") ||
                       path.EndsWith(".mov") ||
                       path.EndsWith(".avi") ||
                       path.EndsWith(".flv") ||
                       path.EndsWith(".mp3") ||
                       path.EndsWith(".wav") ||
                       path.EndsWith(".aac") ||
                       path.EndsWith(".ogg");
            }
            catch
            {
                var cleanUrl = url.Split('?')[0].Split('#')[0].ToLowerInvariant();
                return cleanUrl.EndsWith(".mp4") ||
                       cleanUrl.EndsWith(".webm") ||
                       cleanUrl.EndsWith(".m3u8") ||
                       cleanUrl.EndsWith(".ts") ||
                       cleanUrl.EndsWith(".m4s") ||
                       cleanUrl.EndsWith(".mov") ||
                       cleanUrl.EndsWith(".avi") ||
                       cleanUrl.EndsWith(".flv") ||
                       cleanUrl.EndsWith(".mp3") ||
                       cleanUrl.EndsWith(".wav") ||
                       cleanUrl.EndsWith(".aac") ||
                       cleanUrl.EndsWith(".ogg");
            }
        }


        public static async Task<ILocator?> WaitVisibleLocatorAsync(
        IEnumerable<ILocator> locators,
        CancellationToken token,
        int timeoutMs = 10000,
        int intervalMs = 250)
        {
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();

                foreach (var locator in locators)
                {
                    try
                    {
                        var first = locator.First;
                        if (await first.CountAsync() > 0 && await first.IsVisibleAsync())
                        {
                            return first;
                        }
                    }
                    catch
                    {
                    }
                }

                await Task.Delay(intervalMs, token);
            }

            return null;
        }


        public static async Task TestFingerSwipeUpAsync(
    IPage page,
    ICDPSession client,
    CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null || page.ViewportSize == null)
                return;

            await client.SendAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["maxTouchPoints"] = 5
            });

            int width = page.ViewportSize.Width;
            int height = page.ViewportSize.Height;

            float x = width * 0.50f;

            // 手指起点：屏幕下方
            float startY = height * 0.78f;

            // 手指终点：屏幕上方
            float endY = height * 0.25f;

            await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchStart",
                ["touchPoints"] = new object[]
                {
            new
            {
                x = x,
                y = startY,
                radiusX = 5,
                radiusY = 5,
                force = 0.9,
                id = 0
            }
                },
                ["modifiers"] = 0
            });

            await Task.Delay(80, cancellationToken);

            int steps = 20;

            for (int i = 1; i <= steps; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                float t = i / (float)steps;

                // y 从大变小，就是手指从下往上
                float y = startY + (endY - startY) * t;

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchMove",
                    ["touchPoints"] = new object[]
                    {
                new
                {
                    x = x,
                    y = y,
                    radiusX = 5,
                    radiusY = 5,
                    force = 0.85,
                    id = 0
                }
                    },
                    ["modifiers"] = 0
                });

                await Task.Delay(12, cancellationToken);
            }

            await Task.Delay(30, cancellationToken);

            await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = "touchEnd",
                ["touchPoints"] = Array.Empty<object>(),
                ["modifiers"] = 0
            });
        }
    }
}
