using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BrowserVisionAgent
{
    using Microsoft.Playwright;

    public static class TouchHelper
    {
        public static async Task SwipeAsync(
            IPage page,
            double startX,
            double startY,
            double endX,
            double endY,
            int durationMs = 500,
            int steps = 30)
        {
            var cdp = await page.Context.NewCDPSessionAsync(page);

            try
            {
                async Task SendTouchAsync(
                    string type,
                    double x,
                    double y,
                    bool release = false)
                {
                    await cdp.SendAsync(
                        "Input.dispatchTouchEvent",
                        new Dictionary<string, object>
                        {
                            ["type"] = type,
                            ["touchPoints"] = release
                                ? Array.Empty<object>()
                                : new object[]
                                {
                                new
                                {
                                    x,
                                    y,
                                    id = 1
                                }
                                }
                        });
                }

                await SendTouchAsync(
                    "touchStart", startX, startY);

                for (int i = 1; i <= steps; i++)
                {
                    double t = (double)i / steps;

                    // 平滑加速和减速
                    double progress =
                        t * t * (3 - 2 * t);

                    double x = startX +
                        (endX - startX) * progress;

                    double y = startY +
                        (endY - startY) * progress;

                    await SendTouchAsync(
                        "touchMove", x, y);

                    await Task.Delay(
                        Math.Max(1, durationMs / steps));
                }

                await SendTouchAsync(
                    "touchEnd", endX, endY, true);
            }
            finally
            {
                await cdp.DetachAsync();
            }
        }
    }
}
