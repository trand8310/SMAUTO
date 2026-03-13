using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAd
{
    using Microsoft.Playwright;

    public sealed class CenterClickCandidate
    {
        public string TagName { get; set; } = "";
        public string SelectorHint { get; set; } = "";
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Score { get; set; }
    }

    public static class CenterClickableFinder
    {
        /// <summary>
        /// 获取当前可见视口中部区域（默认 30%~70%）内的可点击候选节点。
        /// 注意：返回的是候选信息，不是直接的 DOM 节点句柄。
        /// </summary>
        public static async Task<List<CenterClickCandidate>> GetCandidatesAsync(
            IPage page,
            double xMinRatio = 0.30,
            double xMaxRatio = 0.70,
            double yMinRatio = 0.30,
            double yMaxRatio = 0.70,
            int xSteps = 5,
            int ySteps = 5)
        {
            if (page == null || page.IsClosed || page.ViewportSize == null)
                return new List<CenterClickCandidate>();

            int vw = page.ViewportSize.Width;
            int vh = page.ViewportSize.Height;

            xMinRatio = ClampRatio(xMinRatio);
            xMaxRatio = ClampRatio(xMaxRatio);
            yMinRatio = ClampRatio(yMinRatio);
            yMaxRatio = ClampRatio(yMaxRatio);

            xSteps = Math.Clamp(xSteps, 2, 9);
            ySteps = Math.Clamp(ySteps, 2, 9);

            var result = await page.EvaluateAsync<List<CenterClickCandidate>>(
                @"([vw, vh, xMinRatio, xMaxRatio, yMinRatio, yMaxRatio, xSteps, ySteps]) => {
                const results = [];
                const seen = new Set();

                function buildPercents(min, max, steps) {
                    if (steps <= 1) return [(min + max) / 2];
                    const arr = [];
                    const span = max - min;
                    for (let i = 0; i < steps; i++) {
                        arr.push(min + span * (i / (steps - 1)));
                    }
                    return arr;
                }

                const xPercents = buildPercents(xMinRatio, xMaxRatio, xSteps);
                const yPercents = buildPercents(yMinRatio, yMaxRatio, ySteps);

                function isVisible(el) {
                    if (!el) return false;

                    const style = getComputedStyle(el);
                    if (style.display === 'none') return false;
                    if (style.visibility === 'hidden') return false;
                    if (style.pointerEvents === 'none') return false;
                    if (parseFloat(style.opacity || '1') <= 0.05) return false;

                    const rect = el.getBoundingClientRect();
                    if (!rect) return false;
                    if (rect.width < 2 || rect.height < 2) return false;
                    if (rect.bottom <= 0 || rect.right <= 0 || rect.left >= vw || rect.top >= vh) return false;

                    return true;
                }

                function isClickable(el) {
                    if (!el) return false;
                    if (el.disabled) return false;

                    const tag = (el.tagName || '').toLowerCase();

                    if (['body', 'html'].includes(tag)) return false;

                    if (['a', 'button', 'summary', 'label'].includes(tag)) return true;
                    if (tag === 'input' && el.type !== 'hidden') return true;

                    const role = (el.getAttribute('role') || '').toLowerCase();
                    if (role === 'button' || role === 'link' || role === 'menuitem' || role === 'tab') return true;

                    if (el.hasAttribute('onclick')) return true;
                    if (el.hasAttribute('tabindex') && el.tabIndex >= 0) return true;
                    if (el.hasAttribute('aria-expanded')) return true;
                    if (el.hasAttribute('aria-pressed')) return true;

                    const style = getComputedStyle(el);
                    if (style.cursor === 'pointer') return true;

                    return false;
                }

                function isReasonableArea(el) {
                    const rect = el.getBoundingClientRect();
                    if (!rect) return false;

                    const area = rect.width * rect.height;
                    if (area < 36) return false;

                    // 超大容器一般不作为点击目标
                    if (rect.width > vw * 0.95 && rect.height > vh * 0.45) return false;

                    return true;
                }

                function getClickableAncestor(el) {
                    let cur = el;
                    while (cur && cur !== document.documentElement) {
                        if (isVisible(cur) && isClickable(cur) && isReasonableArea(cur)) {
                            return cur;
                        }
                        cur = cur.parentElement;
                    }
                    return null;
                }

                function getSelectorHint(el) {
                    if (!el) return '';
                    const tag = (el.tagName || '').toLowerCase();
                    const id = el.id ? `#${el.id}` : '';
                    const cls = el.classList && el.classList.length > 0
                        ? '.' + Array.from(el.classList).slice(0, 2).join('.')
                        : '';
                    const dataType = el.getAttribute('data-type');
                    const dt = dataType ? `[data-type=""${dataType}""]` : '';
                    return `${tag}${id}${cls}${dt}`;
                }

                for (const xp of xPercents) {
                    for (const yp of yPercents) {
                        const x = Math.round(vw * xp);
                        const y = Math.round(vh * yp);

                        const stack = document.elementsFromPoint(x, y);
                        if (!stack || stack.length === 0) continue;

                        for (const raw of stack) {
                            const hit = getClickableAncestor(raw);
                            if (!hit) continue;

                            const rect = hit.getBoundingClientRect();

                            const key =
                                (hit.tagName || '') + '|' +
                                (hit.id || '') + '|' +
                                (hit.className || '') + '|' +
                                Math.round(rect.left) + '|' +
                                Math.round(rect.top) + '|' +
                                Math.round(rect.width) + '|' +
                                Math.round(rect.height);

                            if (seen.has(key)) break;
                            seen.add(key);

                            const cx = rect.left + rect.width / 2;
                            const cy = rect.top + rect.height / 2;

                            const dx = Math.abs(cx - vw / 2);
                            const dy = Math.abs(cy - vh / 2);

                            let score = 1000 - dx * 2 - dy * 2;

                            const area = rect.width * rect.height;
                            if (area < 100) score -= 120;
                            else if (area < 180) score -= 50;
                            else if (area > vw * vh * 0.20) score -= 120;

                            const tag = (hit.tagName || '').toLowerCase();
                            if (tag === 'button' || tag === 'a') score += 40;

                            results.push({
                                tagName: tag,
                                selectorHint: getSelectorHint(hit),
                                centerX: cx,
                                centerY: cy,
                                width: rect.width,
                                height: rect.height,
                                score
                            });

                            break;
                        }
                    }
                }

                results.sort((a, b) => b.score - a.score);
                return results;
            }",
                new object[] { vw, vh, xMinRatio, xMaxRatio, yMinRatio, yMaxRatio, xSteps, ySteps });

            return result ?? new List<CenterClickCandidate>();
        }

        /// <summary>
        /// 给中间区域的可点击候选节点打标记 data-oai-click-candidate='1'
        /// 返回标记数量。
        /// </summary>
        public static async Task<int> MarkCandidatesAsync(
            IPage page,
            double xMinRatio = 0.30,
            double xMaxRatio = 0.70,
            double yMinRatio = 0.30,
            double yMaxRatio = 0.70,
            int xSteps = 5,
            int ySteps = 5)
        {
            if (page == null || page.IsClosed || page.ViewportSize == null)
                return 0;

            int vw = page.ViewportSize.Width;
            int vh = page.ViewportSize.Height;

            xMinRatio = ClampRatio(xMinRatio);
            xMaxRatio = ClampRatio(xMaxRatio);
            yMinRatio = ClampRatio(yMinRatio);
            yMaxRatio = ClampRatio(yMaxRatio);

            xSteps = Math.Clamp(xSteps, 2, 9);
            ySteps = Math.Clamp(ySteps, 2, 9);

            return await page.EvaluateAsync<int>(
                @"([vw, vh, xMinRatio, xMaxRatio, yMinRatio, yMaxRatio, xSteps, ySteps]) => {
                document.querySelectorAll('[data-oai-click-candidate=""1""]')
                    .forEach(el => el.removeAttribute('data-oai-click-candidate'));

                const seen = new Set();
                let count = 0;

                function buildPercents(min, max, steps) {
                    if (steps <= 1) return [(min + max) / 2];
                    const arr = [];
                    const span = max - min;
                    for (let i = 0; i < steps; i++) {
                        arr.push(min + span * (i / (steps - 1)));
                    }
                    return arr;
                }

                const xPercents = buildPercents(xMinRatio, xMaxRatio, xSteps);
                const yPercents = buildPercents(yMinRatio, yMaxRatio, ySteps);

                function isVisible(el) {
                    if (!el) return false;

                    const style = getComputedStyle(el);
                    if (style.display === 'none') return false;
                    if (style.visibility === 'hidden') return false;
                    if (style.pointerEvents === 'none') return false;
                    if (parseFloat(style.opacity || '1') <= 0.05) return false;

                    const rect = el.getBoundingClientRect();
                    if (!rect || rect.width < 2 || rect.height < 2) return false;
                    if (rect.bottom <= 0 || rect.right <= 0 || rect.left >= vw || rect.top >= vh) return false;

                    return true;
                }

                function isClickable(el) {
                    if (!el) return false;
                    if (el.disabled) return false;

                    const tag = (el.tagName || '').toLowerCase();
                    if (['body', 'html'].includes(tag)) return false;
                    if (['a', 'button', 'summary', 'label'].includes(tag)) return true;
                    if (tag === 'input' && el.type !== 'hidden') return true;

                    const role = (el.getAttribute('role') || '').toLowerCase();
                    if (role === 'button' || role === 'link' || role === 'menuitem' || role === 'tab') return true;

                    if (el.hasAttribute('onclick')) return true;
                    if (el.hasAttribute('tabindex') && el.tabIndex >= 0) return true;

                    const style = getComputedStyle(el);
                    if (style.cursor === 'pointer') return true;

                    return false;
                }

                function isReasonableArea(el) {
                    const rect = el.getBoundingClientRect();
                    if (!rect) return false;
                    const area = rect.width * rect.height;
                    if (area < 36) return false;
                    if (rect.width > vw * 0.95 && rect.height > vh * 0.45) return false;
                    return true;
                }

                function getClickableAncestor(el) {
                    let cur = el;
                    while (cur && cur !== document.documentElement) {
                        if (isVisible(cur) && isClickable(cur) && isReasonableArea(cur)) {
                            return cur;
                        }
                        cur = cur.parentElement;
                    }
                    return null;
                }

                for (const xp of xPercents) {
                    for (const yp of yPercents) {
                        const x = Math.round(vw * xp);
                        const y = Math.round(vh * yp);
                        const stack = document.elementsFromPoint(x, y);

                        if (!stack || stack.length === 0) continue;

                        for (const raw of stack) {
                            const hit = getClickableAncestor(raw);
                            if (!hit) continue;

                            if (seen.has(hit)) break;
                            seen.add(hit);

                            hit.setAttribute('data-oai-click-candidate', '1');
                            count++;
                            break;
                        }
                    }
                }

                return count;
            }",
                new object[] { vw, vh, xMinRatio, xMaxRatio, yMinRatio, yMaxRatio, xSteps, ySteps });
        }

        /// <summary>
        /// 获取最佳候选。
        /// </summary>
        public static async Task<CenterClickCandidate?> GetBestCandidateAsync(
            IPage page,
            double xMinRatio = 0.30,
            double xMaxRatio = 0.70,
            double yMinRatio = 0.30,
            double yMaxRatio = 0.70,
            int xSteps = 5,
            int ySteps = 5)
        {
            var list = await GetCandidatesAsync(page, xMinRatio, xMaxRatio, yMinRatio, yMaxRatio, xSteps, ySteps);
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>
        /// 用鼠标点击最佳候选。
        /// </summary>
        public static async Task<bool> ClickBestByMouseAsync(
            IPage page,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed)
                return false;

            var best = await GetBestCandidateAsync(page);
            if (best == null)
                return false;

            try
            {
                await page.Mouse.MoveAsync((float)best.CenterX,(float)best.CenterY);
                await Task.Delay(Random.Shared.Next(35, 90), cancellationToken);
                await page.Mouse.ClickAsync((float)best.CenterX, (float)best.CenterY);
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

        /// <summary>
        /// 用触屏事件点击最佳候选。
        /// </summary>
        public static async Task<bool> ClickBestByTouchAsync(
            IPage page,
            ICDPSession client,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null)
                return false;

            var best = await GetBestCandidateAsync(page);
            if (best == null)
                return false;

            try
            {
                float x = (float)best.CenterX;
                float y = (float)best.CenterY;

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchStart",
                    ["touchPoints"] = new object[]
                    {
                    new { x, y }
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

        /// <summary>
        /// 返回已标记节点的 Locator。
        /// 调用前需先执行 MarkCandidatesAsync。
        /// </summary>
        public static ILocator GetMarkedLocator(IPage page)
        {
            return page.Locator("[data-oai-click-candidate='1']");
        }

        private static double ClampRatio(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }
    }
}
