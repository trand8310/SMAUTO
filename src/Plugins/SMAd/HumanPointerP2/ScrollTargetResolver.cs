using Microsoft.Playwright;

namespace SMAd.HumanPointerP2
{
    internal sealed class ScrollTargetProbe
    {
        public bool Found { get; set; }
        public double LocalX { get; set; }
        public double LocalY { get; set; }
        public double Position { get; set; }
        public double MaxPosition { get; set; }
        public double WindowX { get; set; }
        public double WindowY { get; set; }
        public string TargetKey { get; set; } = "";
        public string TargetLabel { get; set; } = "";
    }

    internal sealed record ResolvedScrollTarget(
        IFrame Frame,
        PointerPosition PagePoint,
        ScrollTargetProbe Before);

    internal sealed class FrameViewportMetrics
    {
        public double BorderLeft { get; set; }
        public double BorderTop { get; set; }
    }

    /// <summary>
    /// 把主视口坐标转换到实际 iframe，并解析鼠标下方可滚动容器。
    /// Playwright 可以直接进入跨域 frame，因此不依赖父页面读取 contentDocument。
    /// </summary>
    internal sealed class ScrollTargetResolver
    {
        private const string ResolveScript = @"arg => {
            const x = Math.max(1, Math.min(window.innerWidth - 1, arg.x));
            const y = Math.max(1, Math.min(window.innerHeight - 1, arg.y));
            const direction = arg.direction >= 0 ? 1 : -1;
            const tolerance = 2;
            const scrollingElement = document.scrollingElement || document.documentElement;

            const canMove = el => {
                if (!el) return false;
                const max = Math.max(0, el.scrollHeight - el.clientHeight);
                return direction > 0 ? el.scrollTop < max - tolerance : el.scrollTop > tolerance;
            };

            const isScrollable = el => {
                if (!el || el === document.body || el === document.documentElement) return false;
                const style = getComputedStyle(el);
                const overflow = style.overflowY;
                return /(auto|scroll|overlay)/.test(overflow) &&
                    el.scrollHeight > el.clientHeight + tolerance &&
                    el.clientHeight > 18;
            };

            const keyOf = el => {
                if (el === scrollingElement) return 'document';
                const id = el.id ? '#' + el.id : '';
                const classes = typeof el.className === 'string'
                    ? '.' + el.className.trim().split(/\s+/).filter(Boolean).slice(0, 2).join('.')
                    : '';
                return el.tagName.toLowerCase() + id + classes;
            };

            const result = (el, px, py) => ({
                Found: true,
                LocalX: Math.max(1, Math.min(window.innerWidth - 1, px)),
                LocalY: Math.max(1, Math.min(window.innerHeight - 1, py)),
                Position: el === scrollingElement ? window.scrollY : el.scrollTop,
                MaxPosition: Math.max(0, el.scrollHeight - el.clientHeight),
                WindowX: window.scrollX,
                WindowY: window.scrollY,
                TargetKey: keyOf(el),
                TargetLabel: keyOf(el).slice(0, 120)
            });

            if (!arg.forceRelocate) {
                let hit = document.elementFromPoint(x, y);
                for (let el = hit; el; el = el.parentElement) {
                    if (isScrollable(el) && canMove(el))
                        return result(el, x, y);
                }
                if (canMove(scrollingElement))
                    return result(scrollingElement, x, y);
            }

            let best = null;
            let bestScore = 0;
            const candidates = document.querySelectorAll(
                'main,section,article,div,ul,ol,table,tbody,[role=main],[role=dialog],[role=region]');
            for (const el of candidates) {
                if (!isScrollable(el) || !canMove(el)) continue;
                const rect = el.getBoundingClientRect();
                const left = Math.max(0, rect.left);
                const top = Math.max(0, rect.top);
                const right = Math.min(window.innerWidth, rect.right);
                const bottom = Math.min(window.innerHeight, rect.bottom);
                const width = right - left;
                const height = bottom - top;
                if (width < 40 || height < 40) continue;
                const area = width * height;
                const centerPenalty = 1 + Math.abs(((left + right) / 2) - (window.innerWidth / 2)) / window.innerWidth;
                const score = area / centerPenalty;
                if (score > bestScore) {
                    bestScore = score;
                    best = { el, left, top, right, bottom };
                }
            }

            if (best) {
                const px = best.left + ((best.right - best.left) * 0.62);
                const py = best.top + ((best.bottom - best.top) * 0.52);
                return result(best.el, px, py);
            }

            if (canMove(scrollingElement)) {
                const px = window.innerWidth * 0.66;
                const py = window.innerHeight * 0.55;
                return result(scrollingElement, px, py);
            }

            return {
                Found: false,
                LocalX: x,
                LocalY: y,
                Position: 0,
                MaxPosition: 0,
                WindowX: window.scrollX,
                WindowY: window.scrollY,
                TargetKey: '',
                TargetLabel: ''
            };
        }";

        public async Task<ResolvedScrollTarget?> ResolveAsync(
            IPage page,
            PointerPosition pagePoint,
            int direction,
            bool forceRelocate)
        {
            var frames = await GetFramesAtPointAsync(page, pagePoint);
            foreach (var framePoint in frames)
            {
                try
                {
                    var probe = await framePoint.Frame.EvaluateAsync<ScrollTargetProbe>(
                        ResolveScript,
                        new
                        {
                            x = framePoint.LocalX,
                            y = framePoint.LocalY,
                            direction,
                            forceRelocate
                        });

                    if (probe?.Found != true)
                        continue;

                    var resolvedPoint = new PointerPosition(
                        framePoint.OffsetX + probe.LocalX,
                        framePoint.OffsetY + probe.LocalY);
                    return new ResolvedScrollTarget(framePoint.Frame, resolvedPoint, probe);
                }
                catch (PlaywrightException)
                {
                    // frame 可能在解析期间被替换，继续尝试父 frame。
                }
            }

            return null;
        }

        public async Task<ScrollTargetProbe?> ProbeAsync(
            IPage page,
            ResolvedScrollTarget target,
            int direction)
        {
            try
            {
                var coordinates = await ToFrameCoordinatesAsync(page, target.Frame, target.PagePoint);
                return await target.Frame.EvaluateAsync<ScrollTargetProbe>(
                    ResolveScript,
                    new
                    {
                        x = coordinates.LocalX,
                        y = coordinates.LocalY,
                        direction,
                        forceRelocate = false
                    });
            }
            catch (PlaywrightException)
            {
                return null;
            }
        }

        public async Task<PointerPosition?> ToLocalPointAsync(
            IPage page,
            IFrame? frame,
            PointerPosition pagePoint)
        {
            if (frame == null)
                return null;

            var coordinates = await ToFrameCoordinatesAsync(page, frame, pagePoint);
            return new PointerPosition(coordinates.LocalX, coordinates.LocalY);
        }

        private async Task<IReadOnlyList<FramePoint>> GetFramesAtPointAsync(
            IPage page,
            PointerPosition pagePoint)
        {
            var result = new List<FramePoint>();
            foreach (var frame in page.Frames)
            {
                if (frame == page.MainFrame)
                    continue;

                try
                {
                    var point = await ToFrameCoordinatesAsync(page, frame, pagePoint);
                    if (point.ContainsPagePoint)
                        result.Add(point);
                }
                catch (PlaywrightException)
                {
                }
            }

            // 最内层（可视面积最小）的 frame 优先，最后回退主文档。
            result.Sort((a, b) => a.Area.CompareTo(b.Area));
            result.Add(new FramePoint(page.MainFrame, 0, 0, pagePoint.X, pagePoint.Y, double.MaxValue, true));
            return result;
        }

        private static async Task<FramePoint> ToFrameCoordinatesAsync(
            IPage page,
            IFrame frame,
            PointerPosition pagePoint)
        {
            if (frame == page.MainFrame)
                return new FramePoint(frame, 0, 0, pagePoint.X, pagePoint.Y, double.MaxValue, true);

            var frameElement = await frame.FrameElementAsync();
            try
            {
                var box = await frameElement.BoundingBoxAsync();
                if (box == null)
                    return new FramePoint(frame, 0, 0, pagePoint.X, pagePoint.Y, 0, false);

                var borders = await frameElement.EvaluateAsync<FrameViewportMetrics>(
                    "el => ({ BorderLeft: el.clientLeft || 0, BorderTop: el.clientTop || 0 })");
                double offsetX = box.X + (borders?.BorderLeft ?? 0);
                double offsetY = box.Y + (borders?.BorderTop ?? 0);
                bool contains = pagePoint.X >= box.X && pagePoint.X <= box.X + box.Width &&
                                pagePoint.Y >= box.Y && pagePoint.Y <= box.Y + box.Height;

                return new FramePoint(
                    frame,
                    offsetX,
                    offsetY,
                    pagePoint.X - offsetX,
                    pagePoint.Y - offsetY,
                    Math.Max(1, box.Width * box.Height),
                    contains);
            }
            finally
            {
                await frameElement.DisposeAsync();
            }
        }

        private sealed record FramePoint(
            IFrame Frame,
            double OffsetX,
            double OffsetY,
            double LocalX,
            double LocalY,
            double Area,
            bool ContainsPagePoint);
    }
}
