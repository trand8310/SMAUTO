

using Microsoft.Playwright;
using QTP.Common;

namespace BDAd
{
    public sealed class ClickNavigationResult
    {
        public bool Success { get; set; }
        public bool Navigated { get; set; }
        public bool UrlChanged { get; set; }
        public bool NewPageOpened { get; set; }

        public string Method { get; set; } = "";
        public string BeforeUrl { get; set; } = "";
        public string AfterUrl { get; set; } = "";
        public string? NewPageUrl { get; set; }
        public string Message { get; set; } = "";

        // 关键：后续应该继续操作的页面
        public IPage? EffectivePage { get; set; }

        // 关键：后续应该继续操作的 CDP
        public ICDPSession? EffectiveClient { get; set; }

        // 原页面 / 新页面
        public IPage? SourcePage { get; set; }
        public IPage? PopupPage { get; set; }
    }


    public static class ClickFallbackHelper
    {
        public static async Task<ClickNavigationResult> ClickAndDetectNavigationAsync(
            IPage page,
            ICDPSession client,
            ILocator locator,
            CancellationToken token = default,
            int navigationWaitMs = 4000,
            int settleDelayMs = 1000,
            bool enableTouch = true,
            bool enableLocatorClickFallback = true,
            bool enableJsClickFallback = true)
        {
            var result = new ClickNavigationResult
            {
                SourcePage = page,
                EffectivePage = page,
                EffectiveClient = client,
                BeforeUrl = page?.Url ?? ""
            };

            if (page == null || page.IsClosed)
            {
                result.Message = "page is null or closed";
                return result;
            }

            if (locator == null)
            {
                result.Message = "locator is null";
                return result;
            }

            IPage? popupPage = null;

            void PopupHandler(object? sender, IPage popup)
            {
                popupPage = popup;
            }

            page.Popup += PopupHandler;

            try
            {
                await locator.ScrollIntoViewIfNeededAsync();

                // 1. touch
                if (enableTouch && client != null)
                {
                    var ok = await TouchClickVisibleLocatorAsync(page, client, locator, token);
                    if (ok)
                    {
                        result.Method = "touch";
                        result.Success = true;

                        var detected = await DetectNavigationAndResolveTargetAsync(
                            sourcePage: page,
                            sourceClient: client,
                            popupPage: () => popupPage,
                            result: result,
                            token: token,
                            navigationWaitMs: navigationWaitMs,
                            settleDelayMs: settleDelayMs);

                        if (detected)
                            return result;
                    }
                }

                // 2. locator.click
                if (enableLocatorClickFallback)
                {
                    try
                    {
                        await locator.ClickAsync(new LocatorClickOptions
                        {
                            Force = true,
                            Timeout = 3000
                        });

                        result.Method = "locator.click";
                        result.Success = true;

                        var detected = await DetectNavigationAndResolveTargetAsync(
                            sourcePage: page,
                            sourceClient: client,
                            popupPage: () => popupPage,
                            result: result,
                            token: token,
                            navigationWaitMs: navigationWaitMs,
                            settleDelayMs: settleDelayMs);

                        if (detected)
                            return result;
                    }
                    catch (Exception ex)
                    {
                        result.Message = $"locator.click failed: {ex.Message}";
                    }
                }

                // 3. js click
                if (enableJsClickFallback)
                {
                    try
                    {
                        await locator.EvaluateAsync("e => e.click && e.click()");
                        result.Method = "js.click";
                        result.Success = true;

                        var detected = await DetectNavigationAndResolveTargetAsync(
                            sourcePage: page,
                            sourceClient: client,
                            popupPage: () => popupPage,
                            result: result,
                            token: token,
                            navigationWaitMs: navigationWaitMs,
                            settleDelayMs: settleDelayMs);

                        if (detected)
                            return result;
                    }
                    catch (Exception ex)
                    {
                        result.Message = $"js.click failed: {ex.Message}";
                    }
                }

                result.AfterUrl = page.Url ?? "";
                result.EffectivePage = page;
                result.EffectiveClient = client;

                if (!result.Navigated)
                {
                    result.Message = string.IsNullOrWhiteSpace(result.Message)
                        ? "click sent but no navigation detected"
                        : result.Message;
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                result.AfterUrl = page?.Url ?? "";
                result.Message = "operation canceled";
                return result;
            }
            catch (Exception ex)
            {
                result.AfterUrl = page?.Url ?? "";
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                page.Popup -= PopupHandler;
            }
        }

        private static async Task<bool> DetectNavigationAndResolveTargetAsync(
            IPage sourcePage,
            ICDPSession sourceClient,
            Func<IPage?> popupPage,
            ClickNavigationResult result,
            CancellationToken token,
            int navigationWaitMs,
            int settleDelayMs)
        {
            var start = Environment.TickCount64;

            while (!token.IsCancellationRequested)
            {
                // 1. 新开页优先
                var currentPopup = popupPage();
                if (currentPopup != null && !currentPopup.IsClosed)
                {
                    result.Navigated = true;
                    result.NewPageOpened = true;
                    result.PopupPage = currentPopup;
                    result.NewPageUrl = currentPopup.Url ?? "";
                    result.AfterUrl = currentPopup.Url ?? "";
                    result.EffectivePage = currentPopup;

                    try
                    {
                        result.EffectiveClient = await currentPopup.Context.NewCDPSessionAsync(currentPopup);
                    }
                    catch
                    {
                        result.EffectiveClient = null;
                    }

                    try
                    {
                        await currentPopup.WaitForLoadStateAsync(
                            LoadState.DOMContentLoaded,
                            new PageWaitForLoadStateOptions
                            {
                                Timeout = Math.Max(1000, navigationWaitMs / 2)
                            });
                    }
                    catch
                    {
                    }

                    if (settleDelayMs > 0)
                    {
                        try { await Task.Delay(settleDelayMs, token); } catch { }
                    }

                    result.Message = "new page opened";
                    return true;
                }

                // 2. 原页 URL 变化
                var currentUrl = sourcePage.Url ?? "";
                if (!string.Equals(result.BeforeUrl, currentUrl, StringComparison.OrdinalIgnoreCase))
                {
                    result.Navigated = true;
                    result.UrlChanged = true;
                    result.AfterUrl = currentUrl;
                    result.EffectivePage = sourcePage;
                    result.EffectiveClient = sourceClient;

                    if (settleDelayMs > 0)
                    {
                        try { await Task.Delay(settleDelayMs, token); } catch { }
                    }

                    result.Message = "url changed";
                    return true;
                }

                if (Environment.TickCount64 - start >= navigationWaitMs)
                    break;

                await Task.Delay(100, token);
            }

            // 最后再稳一下
            try
            {
                await sourcePage.WaitForLoadStateAsync(
                    LoadState.DOMContentLoaded,
                    new PageWaitForLoadStateOptions
                    {
                        Timeout = Math.Max(1000, navigationWaitMs / 2)
                    });
            }
            catch
            {
            }

            if (settleDelayMs > 0)
            {
                try { await Task.Delay(settleDelayMs, token); } catch { }
            }

            var finalPopup = popupPage();
            if (finalPopup != null && !finalPopup.IsClosed)
            {
                result.Navigated = true;
                result.NewPageOpened = true;
                result.PopupPage = finalPopup;
                result.NewPageUrl = finalPopup.Url ?? "";
                result.AfterUrl = finalPopup.Url ?? "";
                result.EffectivePage = finalPopup;

                try
                {
                    result.EffectiveClient = await finalPopup.Context.NewCDPSessionAsync(finalPopup);
                }
                catch
                {
                    result.EffectiveClient = null;
                }

                result.Message = "new page opened after settle";
                return true;
            }

            var afterUrl = sourcePage.Url ?? "";
            result.AfterUrl = afterUrl;

            if (!string.Equals(result.BeforeUrl, afterUrl, StringComparison.OrdinalIgnoreCase))
            {
                result.Navigated = true;
                result.UrlChanged = true;
                result.EffectivePage = sourcePage;
                result.EffectiveClient = sourceClient;
                result.Message = "url changed after settle";
                return true;
            }

            result.EffectivePage = sourcePage;
            result.EffectiveClient = sourceClient;
            return false;
        }

        public static async Task<bool> TouchClickVisibleLocatorAsync(
            IPage page,
            ICDPSession client,
            ILocator locator,
            CancellationToken cancellationToken = default,
            int insetPercentMin = 30,
            int insetPercentMax = 70,
            int minHoldMs = 40,
            int maxHoldMs = 90,
            int minMoveDelayMs = 12,
            int maxMoveDelayMs = 35,
            int minPostDelayMs = 180,
            int maxPostDelayMs = 450,
            bool useTinyMove = true)
        {
            if (page == null || page.IsClosed || client == null || locator == null)
                return false;

            try
            {
                await locator.ScrollIntoViewIfNeededAsync();

                var box = await locator.BoundingBoxAsync();
                if (box == null || box.Width <= 2 || box.Height <= 2)
                    return false;

                var viewport = page.ViewportSize;
                if (viewport == null || viewport.Width <= 2 || viewport.Height <= 2)
                    return false;

                var visibleLeft = Math.Max(0, box.X);
                var visibleTop = Math.Max(0, box.Y);
                var visibleRight = Math.Min(viewport.Width, box.X + box.Width);
                var visibleBottom = Math.Min(viewport.Height, box.Y + box.Height);

                var visibleWidth = visibleRight - visibleLeft;
                var visibleHeight = visibleBottom - visibleTop;

                if (visibleWidth <= 2 || visibleHeight <= 2)
                    return false;

                var rnd = Random.Shared;

                double x = visibleLeft + visibleWidth * CommonHelper.NextDouble(
                    insetPercentMin / 100.0,
                    insetPercentMax / 100.0);

                double y = visibleTop + visibleHeight * CommonHelper.NextDouble(
                    insetPercentMin / 100.0,
                    insetPercentMax / 100.0);

                x = Math.Clamp(x, 1, viewport.Width - 1);
                y = Math.Clamp(y, 1, viewport.Height - 1);

                double moveX = x + rnd.Next(-2, 3);
                double moveY = y + rnd.Next(-2, 3);

                moveX = Math.Clamp(moveX, 1, viewport.Width - 1);
                moveY = Math.Clamp(moveY, 1, viewport.Height - 1);

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchStart",
                    ["touchPoints"] = new object[]
                    {
                    new Dictionary<string, object>
                    {
                        ["x"] = x,
                        ["y"] = y,
                        ["radiusX"] = 2,
                        ["radiusY"] = 2,
                        ["force"] = 1,
                        ["id"] = 0
                    }
                    },
                    ["modifiers"] = 0
                });

                await Task.Delay(rnd.Next(minHoldMs, maxHoldMs + 1), cancellationToken);

                if (useTinyMove)
                {
                    await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                    {
                        ["type"] = "touchMove",
                        ["touchPoints"] = new object[]
                        {
                        new Dictionary<string, object>
                        {
                            ["x"] = moveX,
                            ["y"] = moveY,
                            ["radiusX"] = 2,
                            ["radiusY"] = 2,
                            ["force"] = 1,
                            ["id"] = 0
                        }
                        },
                        ["modifiers"] = 0
                    });

                    await Task.Delay(rnd.Next(minMoveDelayMs, maxMoveDelayMs + 1), cancellationToken);
                }

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchEnd",
                    ["touchPoints"] = Array.Empty<object>(),
                    ["modifiers"] = 0
                });

                await Task.Delay(rnd.Next(minPostDelayMs, maxPostDelayMs + 1), cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }
    }


}
