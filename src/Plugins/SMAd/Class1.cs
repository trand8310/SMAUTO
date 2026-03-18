namespace SMAd.Finderv2
{
    using Microsoft.Playwright;
    using System.Text;

    public sealed class CenterClickCandidate
    {
        public string FrameUrl { get; set; } = "";
        public string TagName { get; set; } = "";
        public string SelectorHint { get; set; } = "";
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Score { get; set; }

        public IFrame? Frame { get; set; }
        public ILocator? Locator { get; set; }
    }

    public static class CenterClickableFinder
    {
        private const string MarkerAttr = "data-oai-click-candidate";

        public static async Task<List<CenterClickCandidate>> GetCandidatesAsync(
            IPage page,
            double xMinRatio = 0.30,
            double xMaxRatio = 0.70,
            double yMinRatio = 0.30,
            double yMaxRatio = 0.70,
            int xSteps = 5,
            int ySteps = 5,
            CancellationToken cancellationToken = default)
        {
            var result = new List<CenterClickCandidate>();

            if (page == null || page.IsClosed || page.ViewportSize == null)
                return result;

            NormalizeArgs(
                page,
                ref xMinRatio,
                ref xMaxRatio,
                ref yMinRatio,
                ref yMaxRatio,
                ref xSteps,
                ref ySteps,
                out int vw,
                out int vh);

            string markScript = BuildMarkScript();

            foreach (var frame in page.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // 在每个 frame 内执行标记
                    int marked = await frame.EvaluateAsync<int>(
                        markScript,
                        new object[] { vw, vh, xMinRatio, xMaxRatio, yMinRatio, yMaxRatio, xSteps, ySteps, MarkerAttr });

                    if (marked <= 0)
                        continue;

                    var markedLocator = frame.Locator($"[{MarkerAttr}='1']");
                    int count = await markedLocator.CountAsync();
                    if (count <= 0)
                        continue;

                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var item = markedLocator.Nth(i);

                        try
                        {
                            if (!await item.IsVisibleAsync())
                                continue;
                        }
                        catch
                        {
                            continue;
                        }

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

                        // BoundingBoxAsync 对 frame 内元素返回的是主页面视口坐标，可直接用于点击
                        double left = box.X;
                        double top = box.Y;
                        double right = box.X + box.Width;
                        double bottom = box.Y + box.Height;

                        // 必须与主页面视口相交
                        double visibleLeft = Math.Max(0, left);
                        double visibleTop = Math.Max(0, top);
                        double visibleRight = Math.Min(vw, right);
                        double visibleBottom = Math.Min(vh, bottom);

                        if (visibleRight - visibleLeft < 2 || visibleBottom - visibleTop < 2)
                            continue;

                        string tagName = "";
                        string selectorHint = "";

                        try
                        {
                            tagName = await item.EvaluateAsync<string>("el => (el.tagName || '').toLowerCase()");
                        }
                        catch
                        {
                        }

                        try
                        {
                            selectorHint = await item.EvaluateAsync<string>(
                                @"el => {
                                    const tag = (el.tagName || '').toLowerCase();
                                    const id = el.id ? ('#' + el.id) : '';
                                    const cls = el.classList && el.classList.length > 0
                                        ? '.' + Array.from(el.classList).slice(0, 2).join('.')
                                        : '';
                                    const dataType = el.getAttribute('data-type');
                                    const dt = dataType ? ('[data-type=""' + dataType + '""]') : '';
                                    return '' + tag + id + cls + dt;
                                }");
                        }
                        catch
                        {
                        }

                        double centerX = box.X + box.Width / 2.0;
                        double centerY = box.Y + box.Height / 2.0;

                        double score = CalcScore(centerX, centerY, box.Width, box.Height, vw, vh, tagName);

                        result.Add(new CenterClickCandidate
                        {
                            FrameUrl = SafeFrameUrl(frame),
                            TagName = tagName,
                            SelectorHint = selectorHint,
                            CenterX = centerX,
                            CenterY = centerY,
                            Width = box.Width,
                            Height = box.Height,
                            Score = score,
                            Frame = frame,
                            Locator = item
                        });
                    }
                }
                catch
                {
                }
            }

            // 去重：位置 + 尺寸 + tag + hint
            result = result
                .GroupBy(x => $"{Math.Round(x.CenterX)}|{Math.Round(x.CenterY)}|{Math.Round(x.Width)}|{Math.Round(x.Height)}|{x.TagName}|{x.SelectorHint}")
                .Select(g => g.OrderByDescending(x => x.Score).First())
                .OrderByDescending(x => x.Score)
                .ToList();

            return result;
        }

        public static async Task<int> MarkCandidatesAsync(
            IPage page,
            double xMinRatio = 0.30,
            double xMaxRatio = 0.70,
            double yMinRatio = 0.30,
            double yMaxRatio = 0.70,
            int xSteps = 5,
            int ySteps = 5,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || page.ViewportSize == null)
                return 0;

            NormalizeArgs(
                page,
                ref xMinRatio,
                ref xMaxRatio,
                ref yMinRatio,
                ref yMaxRatio,
                ref xSteps,
                ref ySteps,
                out int vw,
                out int vh);

            string markScript = BuildMarkScript();
            int total = 0;

            foreach (var frame in page.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    int count = await frame.EvaluateAsync<int>(
                        markScript,
                        new object[] { vw, vh, xMinRatio, xMaxRatio, yMinRatio, yMaxRatio, xSteps, ySteps, MarkerAttr });

                    total += count;
                }
                catch
                {
                }
            }

            return total;
        }

        public static async Task<CenterClickCandidate?> GetBestCandidateAsync(
            IPage page,
            double xMinRatio = 0.30,
            double xMaxRatio = 0.70,
            double yMinRatio = 0.30,
            double yMaxRatio = 0.70,
            int xSteps = 5,
            int ySteps = 5,
            CancellationToken cancellationToken = default)
        {
            var list = await GetCandidatesAsync(
                page,
                xMinRatio,
                xMaxRatio,
                yMinRatio,
                yMaxRatio,
                xSteps,
                ySteps,
                cancellationToken);

            return list.Count > 0 ? list[0] : null;
        }

        public static async Task<bool> ClickBestByMouseAsync(
            IPage page,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || page.ViewportSize == null)
                return false;

            var best = await GetBestCandidateAsync(page, cancellationToken: cancellationToken);
            if (best == null)
                return false;

            try
            {
                var viewport = page.ViewportSize;

                float x = (float)Math.Clamp(
                    GetSafeInnerPoint(best.Width, best.CenterX),
                    1,
                    viewport!.Width - 1);

                float y = (float)Math.Clamp(
                    GetSafeInnerPoint(best.Height, best.CenterY),
                    1,
                    viewport.Height - 1);

                await page.Mouse.MoveAsync(x, y, new() { Steps = Random.Shared.Next(6, 10) });
                await Task.Delay(Random.Shared.Next(35, 90), cancellationToken);
                await page.Mouse.ClickAsync(x, y);

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

        public static async Task<bool> ClickBestByTouchAsync(
            IPage page,
            ICDPSession client,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null || page.ViewportSize == null)
                return false;

            var best = await GetBestCandidateAsync(page, cancellationToken: cancellationToken);
            if (best == null)
                return false;

            try
            {
                var viewport = page.ViewportSize;

                float x = (float)Math.Clamp(
                    GetSafeInnerPoint(best.Width, best.CenterX),
                    1,
                    viewport!.Width - 1);

                float y = (float)Math.Clamp(
                    GetSafeInnerPoint(best.Height, best.CenterY),
                    1,
                    viewport.Height - 1);

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
        /// 获取所有 frame 中被标记的节点 Locator。
        /// </summary>
        public static async Task<List<ILocator>> GetMarkedLocatorsAsync(
            IPage page,
            CancellationToken cancellationToken = default)
        {
            var result = new List<ILocator>();

            if (page == null || page.IsClosed)
                return result;

            foreach (var frame in page.Frames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var locator = frame.Locator($"[{MarkerAttr}='1']");
                    int count = await locator.CountAsync();

                    for (int i = 0; i < count; i++)
                    {
                        result.Add(locator.Nth(i));
                    }
                }
                catch
                {
                }
            }

            return result;
        }

        /// <summary>
        /// 仅主文档中已标记节点。
        /// 不含 iframe 内部节点；跨 frame 请用 GetMarkedLocatorsAsync。
        /// </summary>
        public static ILocator GetMarkedLocator(IPage page)
        {
            return page.Locator($"[{MarkerAttr}='1']");
        }

        private static void NormalizeArgs(
            IPage page,
            ref double xMinRatio,
            ref double xMaxRatio,
            ref double yMinRatio,
            ref double yMaxRatio,
            ref int xSteps,
            ref int ySteps,
            out int vw,
            out int vh)
        {
            vw = page.ViewportSize!.Width;
            vh = page.ViewportSize!.Height;

            xMinRatio = ClampRatio(xMinRatio);
            xMaxRatio = ClampRatio(xMaxRatio);
            yMinRatio = ClampRatio(yMinRatio);
            yMaxRatio = ClampRatio(yMaxRatio);

            if (xMinRatio > xMaxRatio)
                (xMinRatio, xMaxRatio) = (xMaxRatio, xMinRatio);

            if (yMinRatio > yMaxRatio)
                (yMinRatio, yMaxRatio) = (yMaxRatio, yMinRatio);

            xSteps = Math.Clamp(xSteps, 2, 9);
            ySteps = Math.Clamp(ySteps, 2, 9);
        }

        private static double ClampRatio(double value)
        {
            if (value < 0) return 0;
            if (value > 1) return 1;
            return value;
        }

        private static double GetSafeInnerPoint(double size, double center)
        {
            if (size <= 2)
                return center;

            double half = size / 2.0;
            double offset = Random.Shared.NextDouble() * (half * 0.20 * 2) - (half * 0.20);
            return center + offset;
        }

        private static double CalcScore(
            double centerX,
            double centerY,
            double width,
            double height,
            int vw,
            int vh,
            string tagName)
        {
            double dx = Math.Abs(centerX - vw / 2.0);
            double dy = Math.Abs(centerY - vh / 2.0);

            double score = 1000 - dx * 2 - dy * 2;

            double area = width * height;
            if (area < 100) score -= 120;
            else if (area < 180) score -= 50;
            else if (area > vw * vh * 0.20) score -= 120;

            tagName = (tagName ?? "").ToLowerInvariant();
            if (tagName == "button" || tagName == "a") score += 40;
            if (tagName == "input") score += 20;

            return score;
        }

        private static string SafeFrameUrl(IFrame frame)
        {
            try
            {
                return frame.Url ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string BuildMarkScript()
        {
            var sb = new StringBuilder();

            sb.Append(
@"([vw, vh, xMinRatio, xMaxRatio, yMinRatio, yMaxRatio, xSteps, ySteps, markerAttr]) => {
    document.querySelectorAll('[' + markerAttr + '=""1""]').forEach(el => el.removeAttribute(markerAttr));

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

    const seen = new Set();
    let markedCount = 0;

    function tryParseUrl(raw) {
        if (!raw || typeof raw !== 'string') return null;
        const value = raw.trim();
        if (!value) return null;

        try {
            return new URL(value, location.href);
        } catch {
            return null;
        }
    }

    function isHttpProtocol(urlObj) {
        if (!urlObj || !urlObj.protocol) return false;
        const p = String(urlObj.protocol).toLowerCase();
        return p === 'http:' || p === 'https:';
    }

    function containsAny(text, keywords) {
        if (!text) return false;
        const s = String(text).toLowerCase();
        for (const k of keywords) {
            if (s.includes(k)) return true;
        }
        return false;
    }

    function isBlockedHost(hostname) {
        if (!hostname) return false;
        const h = String(hostname).toLowerCase();

        const exactBlockedHosts = [
            'work.weixin.qq.com',
            'weixin.qq.com',
            'open.weixin.qq.com',
            'qm.qq.com',
            'w.url.cn',
            'c.pc.qq.com',
            'connect.qq.com',
            'graph.qq.com'
        ];

        if (exactBlockedHosts.includes(h))
            return true;

        const suffixBlocked = [
            '.work.weixin.qq.com',
            '.weixin.qq.com',
            '.open.weixin.qq.com',
            '.qm.qq.com',
            '.url.cn'
        ];

        for (const s of suffixBlocked) {
            if (h.endsWith(s)) return true;
        }

        return false;
    }

    function isBlockedHttpAppLink(urlObj) {
        if (!urlObj) return false;
        if (!isHttpProtocol(urlObj)) return true;
        if (isBlockedHost(urlObj.hostname)) return true;

        const path = (urlObj.pathname || '').toLowerCase();
        const search = (urlObj.search || '').toLowerCase();
        const hash = (urlObj.hash || '').toLowerCase();
        const full = path + search + hash;

        const blockedPathParts = [
            '/ca/',
            '/cgi-bin/',
            '/deeplink',
            '/deep-link',
            '/deep_link',
            '/openapp',
            '/launch',
            '/jump',
            '/invoke',
            '/universal-link'
        ];

        for (const p of blockedPathParts) {
            if (path.includes(p)) return true;
        }

        const blockedKeywords = [
            'weixin',
            'wechat',
            'wxwork',
            'mqq',
            'qqlaunch',
            'customer_channel=',
            'scheme=',
            'scheme%3a',
            'deeplink',
            'deep_link',
            'openapp',
            'launchapp',
            'launch=',
            'intent:',
            'weixin:',
            'wxwork:',
            'mqq:',
            'qq:'
        ];

        if (containsAny(full, blockedKeywords))
            return true;

        return false;
    }

    function isSafeWebUrl(raw) {
        if (!raw || typeof raw !== 'string')
            return true;

        const value = raw.trim();
        if (!value)
            return true;

        const urlObj = tryParseUrl(value);
        if (!urlObj)
            return true;

        return !isBlockedHttpAppLink(urlObj);
    }

    function hasBlockedUrlAttribute(el) {
        if (!el || !el.getAttribute) return false;

        const attrs = [
            'href',
            'data-href',
            'data-url',
            'data-link',
            'data-jump',
            'data-redirect',
            'data-target-url',
            'formaction',
            'action',
            'xlink:href'
        ];

        for (const name of attrs) {
            const val = el.getAttribute(name);
            if (!val) continue;

            if (!isSafeWebUrl(val))
                return true;
        }

        return false;
    }

    function hasBlockedOnclick(el) {
        if (!el || !el.getAttribute) return false;

        const onclickText = el.getAttribute('onclick') || '';
        if (!onclickText) return false;

        const s = onclickText.toLowerCase();

        const blockedSnippets = [
            'javascript:',
            'mailto:',
            'tel:',
            'sms:',
            'intent:',
            'weixin:',
            'wxwork:',
            'wechat:',
            'mqq:',
            'qq:',
            'alipays:',
            'openapp',
            'launchapp',
            'deeplink',
            'deep_link'
        ];

        if (containsAny(s, blockedSnippets))
            return true;

        if ((s.includes('location.href') || s.includes('window.open') || s.includes('open(')) &&
            containsAny(s, ['weixin', 'wechat', 'wxwork', 'qq', 'mqq']))
            return true;

        return false;
    }

    function isAllowedTarget(el) {
        if (!el) return false;

        const linkLike = el.closest('a, area, form');
        if (linkLike && hasBlockedUrlAttribute(linkLike))
            return false;

        if (hasBlockedUrlAttribute(el))
            return false;

        if (hasBlockedOnclick(el))
            return false;

        return true;
    }

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

        // 只要求在当前 frame 视口内可见，最终是否落在主页面视口里由 C# 再用 BoundingBoxAsync 统一判断
        if (rect.bottom <= 0 || rect.right <= 0) return false;
        if (rect.left >= window.innerWidth || rect.top >= window.innerHeight) return false;

        return true;
    }

    function isReasonableArea(el) {
        const rect = el.getBoundingClientRect();
        if (!rect) return false;

        const area = rect.width * rect.height;
        if (area < 36) return false;

        if (rect.width > window.innerWidth * 0.95 && rect.height > window.innerHeight * 0.45)
            return false;

        return true;
    }

    function isClickable(el) {
        if (!el) return false;
        if (el.disabled) return false;
        if (!isAllowedTarget(el)) return false;

        const tag = (el.tagName || '').toLowerCase();
        if (tag === 'html' || tag === 'body') return false;

        if (tag === 'a' || tag === 'area') {
            const href = el.getAttribute('href') || '';
            if (!href) return false;
            return isSafeWebUrl(href);
        }

        if (tag === 'button' || tag === 'summary' || tag === 'label') return true;
        if (tag === 'input' && el.type !== 'hidden') return true;

        const role = (el.getAttribute('role') || '').toLowerCase();
        if (role === 'button' || role === 'link' || role === 'menuitem' || role === 'tab')
            return true;

        if (el.hasAttribute('onclick')) return true;
        if (el.hasAttribute('tabindex') && el.tabIndex >= 0) return true;
        if (el.hasAttribute('aria-expanded')) return true;
        if (el.hasAttribute('aria-pressed')) return true;

        const style = getComputedStyle(el);
        if (style.cursor === 'pointer') return true;

        return false;
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

    function buildKey(hit) {
        const rect = hit.getBoundingClientRect();
        return [
            hit.tagName || '',
            hit.id || '',
            hit.className || '',
            Math.round(rect.left),
            Math.round(rect.top),
            Math.round(rect.width),
            Math.round(rect.height)
        ].join('|');
    }

    for (const xp of xPercents) {
        for (const yp of yPercents) {
            const x = Math.round(window.innerWidth * xp);
            const y = Math.round(window.innerHeight * yp);

            const stack = document.elementsFromPoint(x, y);
            if (!stack || stack.length === 0) continue;

            for (const raw of stack) {
                const hit = getClickableAncestor(raw);
                if (!hit) continue;

                const key = buildKey(hit);
                if (seen.has(key))
                    break;

                seen.add(key);
                hit.setAttribute(markerAttr, '1');
                markedCount++;
                break;
            }
        }
    }

    return markedCount;
}");
            return sb.ToString();
        }
    }
}