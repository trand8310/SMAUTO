using Microsoft.Playwright;

namespace SMAd
{
    public static class ViewportAreaClickHelper
    {
        public sealed class AreaClickOptions
        {
            public string Selector { get; set; } = "body,iframe";
            public double XMinRatio { get; set; } = 0.20;
            public double XMaxRatio { get; set; } = 0.80;
            public double YMinRatio { get; set; } = 0.20;
            public double YMaxRatio { get; set; } = 0.80;

            /// <summary>
            /// 候选节点过多时，最多取前多少个做判断，避免太慢
            /// </summary>
            public int MaxCandidatesToCheck { get; set; } = 80;

            /// <summary>
            /// 点击前鼠标是否先移动过去
            /// </summary>
            public bool MoveBeforeClick { get; set; } = true;

            /// <summary>
            /// 鼠标移动步数
            /// </summary>
            public int MoveSteps { get; set; } = 8;
        }

        private sealed class CandidatePoint
        {
            public ILocator Locator { get; set; } = null!;
            public float X { get; set; }
            public float Y { get; set; }
            public string TagName { get; set; } = "";
        }

        public static async Task<bool> ClickAnyVisibleNodeInViewportAreaAsync(
            IPage page,
            AreaClickOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || page.ViewportSize == null)
                return false;

            options ??= new AreaClickOptions();

            try
            {
                var point = await GetRandomClickablePointInViewportAreaAsync(page, options, cancellationToken);
                if (point == null)
                    return false;

                if (options.MoveBeforeClick)
                {
                    await page.Mouse.MoveAsync(point.X, point.Y, new() { Steps = options.MoveSteps });
                    await Task.Delay(Random.Shared.Next(30, 80), cancellationToken);
                }

                await page.Mouse.ClickAsync(point.X, point.Y);
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

        public static async Task<bool> TouchAnyVisibleNodeInViewportAreaAsync(
            IPage page,
            ICDPSession client,
            AreaClickOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || page.ViewportSize == null || client == null)
                return false;

            options ??= new AreaClickOptions();

            try
            {
                var point = await GetRandomClickablePointInViewportAreaAsync(page, options, cancellationToken);
                if (point == null)
                    return false;

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchStart",
                    ["touchPoints"] = new object[]
                    {
                        new { x = point.X, y = point.Y }
                    },
                    ["modifiers"] = 0
                });

                await Task.Delay(Random.Shared.Next(35, 70), cancellationToken);

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchEnd",
                    ["touchPoints"] = Array.Empty<object>()
                });

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

        public static async Task<ILocator?> GetAnyVisibleNodeLocatorInViewportAreaAsync(
            IPage page,
            AreaClickOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || page.ViewportSize == null)
                return null;

            options ??= new AreaClickOptions();

            var point = await GetRandomClickablePointInViewportAreaAsync(page, options, cancellationToken);
            return point?.Locator;
        }

        private static async Task<CandidatePoint?> GetRandomClickablePointInViewportAreaAsync(
            IPage page,
            AreaClickOptions options,
            CancellationToken cancellationToken)
        {
            if (page.ViewportSize == null)
                return null;

            NormalizeOptions(options);

            int vw = page.ViewportSize.Width;
            int vh = page.ViewportSize.Height;

            float areaLeft = (float)(vw * options.XMinRatio);
            float areaRight = (float)(vw * options.XMaxRatio);
            float areaTop = (float)(vh * options.YMinRatio);
            float areaBottom = (float)(vh * options.YMaxRatio);

            var locator = page.Locator(options.Selector);
            int count = await locator.CountAsync();

            if (count <= 0)
                return null;

            int maxCheck = Math.Min(count, options.MaxCandidatesToCheck);
            var candidates = new List<CandidatePoint>(maxCheck);

            for (int i = 0; i < maxCheck; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = locator.Nth(i);

                bool visible;
                try
                {
                    visible = await item.IsVisibleAsync();
                }
                catch
                {
                    continue;
                }

                if (!visible)
                    continue;

                LocatorBoundingBoxResult? box;
                try
                {
                    box = await item.BoundingBoxAsync();
                }
                catch
                {
                    continue;
                }

                if (box == null || box.Width <= 2 || box.Height <= 2)
                    continue;

                float left = (float)box.X;
                float top = (float)box.Y;
                float right = (float)(box.X + box.Width);
                float bottom = (float)(box.Y + box.Height);

                // 与视口相交
                float visibleLeft = Math.Max(0, left);
                float visibleTop = Math.Max(0, top);
                float visibleRight = Math.Min(vw, right);
                float visibleBottom = Math.Min(vh, bottom);

                if (visibleRight - visibleLeft < 2 || visibleBottom - visibleTop < 2)
                    continue;

                // 与目标点击区域相交
                float hitLeft = Math.Max(visibleLeft, areaLeft);
                float hitTop = Math.Max(visibleTop, areaTop);
                float hitRight = Math.Min(visibleRight, areaRight);
                float hitBottom = Math.Min(visibleBottom, areaBottom);

                if (hitRight - hitLeft < 2 || hitBottom - hitTop < 2)
                    continue;

                // 在重叠区内随机点，尽量不贴边
                float safePaddingX = Math.Min(8, (hitRight - hitLeft) * 0.15f);
                float safePaddingY = Math.Min(8, (hitBottom - hitTop) * 0.15f);

                float x1 = hitLeft + safePaddingX;
                float x2 = hitRight - safePaddingX;
                float y1 = hitTop + safePaddingY;
                float y2 = hitBottom - safePaddingY;

                if (x2 <= x1)
                {
                    x1 = hitLeft;
                    x2 = hitRight;
                }

                if (y2 <= y1)
                {
                    y1 = hitTop;
                    y2 = hitBottom;
                }

                float x = (float)RandomBetween(x1, x2);
                float y = (float)RandomBetween(y1, y2);

                // 再做一次 elementFromPoint 校验，确保该点确实能打到这个元素或其后代
                bool pointOk = await IsPointHittingElementAsync(page, item, x, y);
                if (!pointOk)
                    continue;

                string tagName = "";
                try
                {
                    tagName = await item.EvaluateAsync<string>("el => (el.tagName || '').toLowerCase()");
                }
                catch
                {
                }

                candidates.Add(new CandidatePoint
                {
                    Locator = item,
                    X = x,
                    Y = y,
                    TagName = tagName
                });
            }

            if (candidates.Count == 0)
                return null;

            return candidates[Random.Shared.Next(0, candidates.Count)];
        }

        private static async Task<bool> IsPointHittingElementAsync(
            IPage page,
            ILocator locator,
            float x,
            float y)
        {
            try
            {
                return await locator.EvaluateAsync<bool>(
                    @"(el, p) => {
                        const hit = document.elementFromPoint(p.x, p.y);
                        if (!hit) return false;
                        return hit === el || el.contains(hit) || hit.contains(el);
                    }",
                    new { x, y });
            }
            catch
            {
                return false;
            }
        }

        private static void NormalizeOptions(AreaClickOptions options)
        {
            options.XMinRatio = ClampRatio(options.XMinRatio);
            options.XMaxRatio = ClampRatio(options.XMaxRatio);
            options.YMinRatio = ClampRatio(options.YMinRatio);
            options.YMaxRatio = ClampRatio(options.YMaxRatio);

            if (options.XMinRatio > options.XMaxRatio)
                (options.XMinRatio, options.XMaxRatio) = (options.XMaxRatio, options.XMinRatio);

            if (options.YMinRatio > options.YMaxRatio)
                (options.YMinRatio, options.YMaxRatio) = (options.YMaxRatio, options.YMinRatio);

            options.MaxCandidatesToCheck = Math.Clamp(options.MaxCandidatesToCheck, 1, 300);

            if (string.IsNullOrWhiteSpace(options.Selector))
                options.Selector = "body,iframe";
        }

        private static double ClampRatio(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }

        private static double RandomBetween(double min, double max)
        {
            if (max <= min)
                return min;

            return min + Random.Shared.NextDouble() * (max - min);
        }
    }
}