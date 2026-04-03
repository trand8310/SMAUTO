
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing;
using System.Numerics;

namespace SMAd.Swiperv3
{
    public static class RandomUtil
    {
        public static int NextInt(int min, int max) => Random.Shared.Next(min, max);
        public static long NextInt64(long min, long max) => Random.Shared.NextInt64(min, max);
        public static double NextDouble() => Random.Shared.NextDouble();
        public static double NextDouble(double min, double max) => min + Random.Shared.NextDouble() * (max - min);

        public static bool Chance(double probability)
        {
            if (probability <= 0) return false;
            if (probability >= 1) return true;
            return Random.Shared.NextDouble() < probability;
        }
    }

    public enum ScrollDirection
    {
        Up,
        Down,
        Random
    }

    public sealed class SwipeArea
    {
        public float MinXRatio { get; set; } = 0.35f;
        public float MaxXRatio { get; set; } = 0.65f;
        public float MinYRatio { get; set; } = 0.18f;
        public float MaxYRatio { get; set; } = 0.82f;

        public static SwipeArea Normal => new()
        {
            MinXRatio = 0.35f,
            MaxXRatio = 0.65f,
            MinYRatio = 0.18f,
            MaxYRatio = 0.82f
        };

        public static SwipeArea Micro => new()
        {
            MinXRatio = 0.42f,
            MaxXRatio = 0.60f,
            MinYRatio = 0.40f,
            MaxYRatio = 0.68f
        };

        public static SwipeArea Wide => new()
        {
            MinXRatio = 0.22f,
            MaxXRatio = 0.78f,
            MinYRatio = 0.18f,
            MaxYRatio = 0.82f
        };
    }

    public sealed class SwipeTrace
    {
        public Vector2 Start { get; set; }
        public Vector2 End { get; set; }
        public List<Vector2> Points { get; set; } = new();
        public int TotalDelayMs { get; set; }
        public ScrollDirection Direction { get; set; }
        public bool IsMicroSwipe { get; set; }
    }

    public sealed class PageScrollState
    {
        public double ScrollX { get; set; }
        public double ScrollY { get; set; }
        public double ScrollHeight { get; set; }
        public double ClientHeight { get; set; }

        public bool CanScrollVertically => ScrollHeight > ClientHeight + 2;
        public bool IsNearTop => ScrollY <= 6;
        public bool IsNearBottom => ScrollY + ClientHeight >= ScrollHeight - 6;
    }

    public sealed class JsScrollState
    {
        public double ScrollX { get; set; }
        public double ScrollY { get; set; }
        public double ScrollHeight { get; set; }
        public double ClientHeight { get; set; }
    }

    public sealed class ScrollTargetState
    {
        public string Kind { get; set; } = "document"; // document / element
        public string ElementTag { get; set; } = "";
        public string ElementId { get; set; } = "";
        public string ElementClass { get; set; } = "";

        public double ScrollLeft { get; set; }
        public double ScrollTop { get; set; }
        public double ScrollWidth { get; set; }
        public double ScrollHeight { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }

        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }

        public bool CanScrollVertically => ScrollHeight > ClientHeight + 2;
        public bool IsNearTop => ScrollTop <= 6;
        public bool IsNearBottom => ScrollTop + ClientHeight >= ScrollHeight - 6;
    }

    public static class SwipeEmulator
    {
        #region 对外主方法

        /// <summary>
        /// 单次拟真人滑动。支持内部滚动容器。
        /// </summary>
        public static async Task<SwipeTrace?> SwipeOnceHumanAsync(
            IPage page,
            ICDPSession client,
            ScrollDirection direction = ScrollDirection.Up,
            SwipeArea? area = null,
            bool microSwipe = false,
            int? steps = null,
            int? totalDistancePx = null,
            bool verifyScrollChanged = true,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || client == null || page.ViewportSize == null)
                return null;

            try
            {
                area ??= microSwipe ? SwipeArea.Micro : SwipeArea.Normal;

                ScrollDirection actualDirection = direction switch
                {
                    ScrollDirection.Random => RandomUtil.Chance(0.97) ? ScrollDirection.Up : ScrollDirection.Down,
                    _ => direction
                };

                if (page.IsClosed || page.ViewportSize == null)
                    return null;

                int vw = page.ViewportSize.Width;
                int vh = page.ViewportSize.Height;

                var path = CreateHumanSwipePath(vw, vh, actualDirection, area, microSwipe, totalDistancePx);
                if (path.start == path.end)
                    return null;

                // 基于起点命中的内部滚动容器判断是否安全可滑
                if (!await CanSafelySwipeDirectionOnTargetAsync(page, actualDirection, path.start.X, path.start.Y))
                    return null;

                ScrollTargetState? before = null;
                if (verifyScrollChanged)
                    before = await GetScrollTargetStateAsync(page, path.start.X, path.start.Y);

                int actualSteps = steps ?? CalcSteps(Vector2.Distance(path.start, path.end), vh, microSwipe);

                var trace = await DispatchHumanSwipeAsync(
                    client: client,
                    start: path.start,
                    end: path.end,
                    steps: actualSteps,
                    direction: actualDirection,
                    microSwipe: microSwipe,
                    cancellationToken: cancellationToken);

                if (verifyScrollChanged && before != null)
                {
                    await Task.Delay(RandomUtil.NextInt(60, 140), cancellationToken);

                    if (page.IsClosed)
                        return null;

                    bool moved = await DidScrollTargetAsync(
                        page,
                        before,
                        path.start.X,
                        path.start.Y,
                        minDelta: microSwipe ? 4 : 8);

                    if (!moved)
                        return null;
                }

                return trace;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 连续拟真人滑动。支持内部滚动容器。
        /// </summary>
        public static async Task<List<SwipeTrace>> SwipeMultipleHumanAsync(
            IPage page,
            ICDPSession client,
            int times,
            ScrollDirection direction = ScrollDirection.Random,
            SwipeArea? area = null,
            bool microSwipe = false,
            int maxConsecutiveNoMove = 2,
            CancellationToken cancellationToken = default)
        {
            var list = new List<SwipeTrace>();
            if (page == null || page.IsClosed || client == null || times <= 0)
                return list;

            area ??= microSwipe ? SwipeArea.Micro : SwipeArea.Normal;
            int noMoveCount = 0;

            for (int i = 0; i < times; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    break;

                try
                {
                    ScrollDirection actualDirection = direction;
                    if (direction == ScrollDirection.Random)
                    {
                        actualDirection = RandomUtil.Chance(0.97)
                            ? ScrollDirection.Up
                            : ScrollDirection.Down;
                    }

                    var trace = await SwipeOnceHumanAsync(
                        page: page,
                        client: client,
                        direction: actualDirection,
                        area: area,
                        microSwipe: microSwipe,
                        verifyScrollChanged: true,
                        cancellationToken: cancellationToken);

                    if (trace == null)
                    {
                        noMoveCount++;
                        if (noMoveCount >= maxConsecutiveNoMove)
                            break;

                        continue;
                    }

                    noMoveCount = 0;
                    list.Add(trace);

                    await Task.Delay(
                        microSwipe
                            ? RandomUtil.NextInt(180, 420)
                            : RandomUtil.NextInt(260, 900),
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    noMoveCount++;
                    if (noMoveCount >= maxConsecutiveNoMove)
                        break;
                }
            }

            return list;
        }

        /// <summary>
        /// 连续微滑动
        /// </summary>
        public static Task<List<SwipeTrace>> SwipeMultipleMicroHumanAsync(
            IPage page,
            ICDPSession client,
            int times,
            ScrollDirection direction = ScrollDirection.Random,
            int maxConsecutiveNoMove = 2,
            CancellationToken cancellationToken = default)
        {
            return SwipeMultipleHumanAsync(
                page: page,
                client: client,
                times: times,
                direction: direction,
                area: SwipeArea.Micro,
                microSwipe: true,
                maxConsecutiveNoMove: maxConsecutiveNoMove,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// 将元素滑入舒适操作区。支持内部滚动容器。
        /// </summary>
        public static async Task<List<SwipeTrace>> SwipeElementIntoComfortZoneAsync(
            IPage page,
            ICDPSession client,
            ILocator element,
            int maxSwipes = 8,
            float comfortTopRatio = 0.22f,
            float comfortBottomRatio = 0.72f,
            CancellationToken cancellationToken = default)
        {
            var all = new List<SwipeTrace>();

            if (page == null || page.IsClosed || client == null || element == null || page.ViewportSize == null)
                return all;

            int vh = page.ViewportSize.Height;
            float comfortTop = vh * comfortTopRatio;
            float comfortBottom = vh * comfortBottomRatio;

            for (int i = 0; i < maxSwipes; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    return all;

                try
                {
                    var box = await element.BoundingBoxAsync();
                    if (box == null)
                        return all;

                    float centerY = (float)(box.Y + box.Height / 2.0);

                    if (centerY >= comfortTop && centerY <= comfortBottom)
                        return all;

                    ScrollDirection direction = centerY < comfortTop
                        ? ScrollDirection.Down
                        : ScrollDirection.Up;

                    double distanceToComfort = centerY < comfortTop
                        ? comfortTop - centerY
                        : centerY - comfortBottom;

                    bool useMicro = distanceToComfort < vh * 0.20;

                    int? targetDistance = useMicro
                        ? (int)Math.Clamp(distanceToComfort * 0.85, vh * 0.08, vh * 0.20)
                        : (int)Math.Clamp(distanceToComfort * 0.90, vh * 0.22, vh * 0.58);

                    var trace = await SwipeOnceHumanAsync(
                        page: page,
                        client: client,
                        direction: direction,
                        area: useMicro ? SwipeArea.Micro : SwipeArea.Normal,
                        microSwipe: useMicro,
                        totalDistancePx: targetDistance,
                        verifyScrollChanged: true,
                        cancellationToken: cancellationToken);

                    if (trace == null)
                        return all;

                    all.Add(trace);

                    await Task.Delay(RandomUtil.NextInt(120, 260), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return all;
                }
            }

            return all;
        }

        #endregion

        #region 页面滚动状态判断（document 兜底）

        private static async Task<PageScrollState> GetPageScrollStateAsync(IPage page)
        {
            if (page == null || page.IsClosed)
                return new PageScrollState();

            try
            {
                var result = await page.EvaluateAsync<JsScrollState>(@"
() => {
    try {
        const el = document.scrollingElement || document.documentElement || document.body;
        return {
            ScrollX: Number(window.scrollX || el?.scrollLeft || 0),
            ScrollY: Number(window.scrollY || el?.scrollTop || 0),
            ScrollHeight: Number(el?.scrollHeight || 0),
            ClientHeight: Number(window.innerHeight || el?.clientHeight || 0)
        };
    } catch {
        return {
            ScrollX: 0,
            ScrollY: 0,
            ScrollHeight: 0,
            ClientHeight: 0
        };
    }
}");

                return new PageScrollState
                {
                    ScrollX = result?.ScrollX ?? 0,
                    ScrollY = result?.ScrollY ?? 0,
                    ScrollHeight = result?.ScrollHeight ?? 0,
                    ClientHeight = result?.ClientHeight ?? 0
                };
            }
            catch
            {
                return new PageScrollState();
            }
        }

        #endregion

        #region 内部滚动容器检测

        /// <summary>
        /// 根据坐标寻找当前命中的最近可滚动容器，找不到则回退到 document。
        /// </summary>
        private static async Task<ScrollTargetState> GetScrollTargetStateAsync(
            IPage page,
            float hitX,
            float hitY)
        {
            if (page == null || page.IsClosed)
                return new ScrollTargetState();

            try
            {
                var result = await page.EvaluateAsync<ScrollTargetState>(@"
(arg) => {
    const x = Number(arg.x || 0);
    const y = Number(arg.y || 0);

    function canScrollY(el) {
        if (!el) return false;
        const style = getComputedStyle(el);
        const overflowY = style.overflowY;
        const scrollable = overflowY === 'auto' || overflowY === 'scroll' || overflowY === 'overlay';
        return scrollable && el.scrollHeight > el.clientHeight + 2;
    }

    function pickScrollable(startEl) {
        let el = startEl;
        while (el && el !== document.body && el !== document.documentElement) {
            if (canScrollY(el)) return el;
            el = el.parentElement;
        }
        return document.scrollingElement || document.documentElement || document.body;
    }

    try {
        const hitEl = document.elementFromPoint(x, y);
        const target = pickScrollable(hitEl);

        const docTarget = document.scrollingElement || document.documentElement || document.body;
        const isDoc = target === docTarget;

        return {
            Kind: isDoc ? 'document' : 'element',
            ElementTag: (target?.tagName || '').toLowerCase(),
            ElementId: target?.id || '',
            ElementClass: typeof target?.className === 'string' ? target.className : '',
            ScrollLeft: Number(target?.scrollLeft || 0),
            ScrollTop: Number(target?.scrollTop || 0),
            ScrollWidth: Number(target?.scrollWidth || 0),
            ScrollHeight: Number(target?.scrollHeight || 0),
            ClientWidth: Number(target?.clientWidth || window.innerWidth || 0),
            ClientHeight: Number(target?.clientHeight || window.innerHeight || 0),
            ViewportWidth: Number(window.innerWidth || 0),
            ViewportHeight: Number(window.innerHeight || 0)
        };
    } catch {
        const target = document.scrollingElement || document.documentElement || document.body;
        return {
            Kind: 'document',
            ElementTag: (target?.tagName || '').toLowerCase(),
            ElementId: target?.id || '',
            ElementClass: typeof target?.className === 'string' ? target.className : '',
            ScrollLeft: Number(target?.scrollLeft || 0),
            ScrollTop: Number(target?.scrollTop || 0),
            ScrollWidth: Number(target?.scrollWidth || 0),
            ScrollHeight: Number(target?.scrollHeight || 0),
            ClientWidth: Number(target?.clientWidth || window.innerWidth || 0),
            ClientHeight: Number(target?.clientHeight || window.innerHeight || 0),
            ViewportWidth: Number(window.innerWidth || 0),
            ViewportHeight: Number(window.innerHeight || 0)
        };
    }
}", new { x = hitX, y = hitY });

                return result ?? new ScrollTargetState();
            }
            catch
            {
                return new ScrollTargetState();
            }
        }

        /// <summary>
        /// document 兜底状态。
        /// </summary>
        private static async Task<ScrollTargetState> GetDocumentScrollTargetStateAsync(IPage page)
        {
            if (page == null || page.IsClosed)
                return new ScrollTargetState();

            try
            {
                var result = await page.EvaluateAsync<ScrollTargetState>(@"
() => {
    try {
        const target = document.scrollingElement || document.documentElement || document.body;
        return {
            Kind: 'document',
            ElementTag: (target?.tagName || '').toLowerCase(),
            ElementId: target?.id || '',
            ElementClass: typeof target?.className === 'string' ? target.className : '',
            ScrollLeft: Number(target?.scrollLeft || 0),
            ScrollTop: Number(target?.scrollTop || 0),
            ScrollWidth: Number(target?.scrollWidth || 0),
            ScrollHeight: Number(target?.scrollHeight || 0),
            ClientWidth: Number(target?.clientWidth || window.innerWidth || 0),
            ClientHeight: Number(target?.clientHeight || window.innerHeight || 0),
            ViewportWidth: Number(window.innerWidth || 0),
            ViewportHeight: Number(window.innerHeight || 0)
        };
    } catch {
        return {
            Kind: 'document',
            ElementTag: '',
            ElementId: '',
            ElementClass: '',
            ScrollLeft: 0,
            ScrollTop: 0,
            ScrollWidth: 0,
            ScrollHeight: 0,
            ClientWidth: 0,
            ClientHeight: 0,
            ViewportWidth: 0,
            ViewportHeight: 0
        };
    }
}");

                return result ?? new ScrollTargetState();
            }
            catch
            {
                return new ScrollTargetState();
            }
        }

        /// <summary>
        /// 检查目标滚动容器是否真的发生了滚动。
        /// </summary>
        private static async Task<bool> DidScrollTargetAsync(
            IPage page,
            ScrollTargetState before,
            float hitX,
            float hitY,
            double minDelta = 8)
        {
            if (page == null || page.IsClosed || before == null)
                return false;

            try
            {
                var after = await GetScrollTargetStateAsync(page, hitX, hitY);

                if (after == null)
                    after = await GetDocumentScrollTargetStateAsync(page);

                // 若前后目标类型差异较大，且前一个是 document，则兜底再比较一次 document
                if (!string.Equals(before.Kind, after.Kind, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(before.Kind, "document", StringComparison.OrdinalIgnoreCase))
                {
                    after = await GetDocumentScrollTargetStateAsync(page);
                }

                return Math.Abs(after.ScrollTop - before.ScrollTop) >= minDelta;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 基于起点命中的滚动容器进行安全判断。
        /// </summary>
        private static async Task<bool> CanSafelySwipeDirectionOnTargetAsync(
            IPage page,
            ScrollDirection direction,
            float hitX,
            float hitY)
        {
            if (page == null || page.IsClosed)
                return false;

            try
            {
                var state = await GetScrollTargetStateAsync(page, hitX, hitY);

                if (!state.CanScrollVertically)
                {
                    state = await GetDocumentScrollTargetStateAsync(page);
                    if (!state.CanScrollVertically)
                        return false;
                }

                // 手指下滑，滚动容器往上回弹；顶部附近容易刷新
                if (direction == ScrollDirection.Down && state.IsNearTop)
                    return false;

                // 手指上滑，滚动容器继续往下浏览；到底就不要再滑
                if (direction == ScrollDirection.Up && state.IsNearBottom)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 兼容旧 document 判断（保留备用）

        private static async Task<bool> DidScrollAsync(
            IPage page,
            PageScrollState before,
            double minDelta = 8)
        {
            if (page == null || page.IsClosed || before == null)
                return false;

            try
            {
                var after = await GetPageScrollStateAsync(page);
                return Math.Abs(after.ScrollY - before.ScrollY) >= minDelta;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> CanSafelySwipeDirectionAsync(
            IPage page,
            ScrollDirection direction)
        {
            if (page == null || page.IsClosed)
                return false;

            try
            {
                var state = await GetPageScrollStateAsync(page);

                if (!state.CanScrollVertically)
                    return false;

                if (direction == ScrollDirection.Down && state.IsNearTop)
                    return false;

                if (direction == ScrollDirection.Up && state.IsNearBottom)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 轨迹生成

        private static (Vector2 start, Vector2 end) CreateHumanSwipePath(
            int vw,
            int vh,
            ScrollDirection direction,
            SwipeArea area,
            bool microSwipe,
            int? totalDistancePx)
        {
            float minX = vw * area.MinXRatio;
            float maxX = vw * area.MaxXRatio;
            float minY = vh * area.MinYRatio;
            float maxY = vh * area.MaxYRatio;

            float safeTop = Math.Max(vh * 0.16f, minY);
            float safeBottom = Math.Min(vh * 0.84f, maxY);
            float safeLeft = Math.Max(vw * 0.12f, minX);
            float safeRight = Math.Min(vw * 0.88f, maxX);

            float startX = (float)RandomUtil.NextDouble(safeLeft, safeRight);
            float endX = microSwipe
                ? startX + (float)RandomUtil.NextDouble(-10, 10)
                : startX + (float)RandomUtil.NextDouble(-18, 18);

            endX = Math.Clamp(endX, safeLeft, safeRight);

            float distance;
            if (totalDistancePx.HasValue && totalDistancePx.Value > 0)
            {
                distance = totalDistancePx.Value;
            }
            else
            {
                double r = RandomUtil.NextDouble();

                if (microSwipe)
                {
                    distance = r < 0.70
                        ? (float)RandomUtil.NextDouble(vh * 0.08, vh * 0.16)
                        : (float)RandomUtil.NextDouble(vh * 0.16, vh * 0.24);
                }
                else
                {
                    distance = r < 0.20
                        ? (float)RandomUtil.NextDouble(vh * 0.18, vh * 0.28)
                        : r < 0.75
                            ? (float)RandomUtil.NextDouble(vh * 0.30, vh * 0.48)
                            : (float)RandomUtil.NextDouble(vh * 0.50, vh * 0.66);
                }
            }

            distance = Math.Clamp(distance, vh * 0.06f, vh * 0.72f);

            Vector2 start, end;

            switch (direction)
            {
                case ScrollDirection.Down:
                    {
                        // Down 更保守，不从太靠顶部开始，减少下拉刷新
                        float startY = (float)RandomUtil.NextDouble(vh * 0.28f, vh * 0.42f);
                        startY = Math.Clamp(startY, safeTop, safeBottom);

                        float endY = startY + distance;
                        if (endY > safeBottom)
                        {
                            endY = safeBottom;
                            startY = Math.Max(vh * 0.24f, endY - distance);
                            startY = Math.Clamp(startY, safeTop, safeBottom);
                        }

                        start = new Vector2(startX, startY);
                        end = new Vector2(endX, endY);
                        break;
                    }

                case ScrollDirection.Up:
                default:
                    {
                        float startY = (float)RandomUtil.NextDouble(vh * 0.58f, safeBottom);
                        float endY = startY - distance;

                        if (endY < safeTop)
                        {
                            endY = safeTop;
                            startY = Math.Min(safeBottom, endY + distance);
                        }

                        start = new Vector2(startX, startY);
                        end = new Vector2(endX, endY);
                        break;
                    }
            }

            return (start, end);
        }

        private static List<Vector2> GetHumanLikeSwipePoints(
            Vector2 start,
            Vector2 end,
            int steps,
            bool microSwipe)
        {
            var points = new List<Vector2>(steps + 1);

            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float distance = Vector2.Distance(start, end);

            if (distance < 1)
            {
                points.Add(start);
                points.Add(end);
                return points;
            }

            float nx = -dy / distance;
            float ny = dx / distance;

            float sideDriftBase = microSwipe
                ? (float)RandomUtil.NextDouble(1.0, 2.2)
                : (float)RandomUtil.NextDouble(2.2, 4.8);

            float phase1 = (float)RandomUtil.NextDouble(0, Math.PI * 2);
            float phase2 = (float)RandomUtil.NextDouble(0, Math.PI * 2);

            float amp1 = (float)RandomUtil.NextDouble(sideDriftBase * 0.35, sideDriftBase);
            float amp2 = (float)RandomUtil.NextDouble(sideDriftBase * 0.12, sideDriftBase * 0.40);

            for (int i = 0; i <= steps; i++)
            {
                float tRaw = i / (float)steps;
                float t = EaseInOutCubic(tRaw);

                float x = start.X + dx * t;
                float y = start.Y + dy * t;

                float drift =
                    MathF.Sin(tRaw * MathF.PI * 1.05f + phase1) * amp1 +
                    MathF.Sin(tRaw * MathF.PI * 2.10f + phase2) * amp2;

                float fade = MathF.Sin(tRaw * MathF.PI);
                drift *= fade;

                x += nx * drift;
                y += ny * drift;

                if (tRaw > 0.84f)
                {
                    float tiny = microSwipe
                        ? (float)RandomUtil.NextDouble(0.08, 0.55)
                        : (float)RandomUtil.NextDouble(0.18, 0.90);

                    x += (float)RandomUtil.NextDouble(-tiny, tiny);
                    y += (float)RandomUtil.NextDouble(-tiny, tiny);
                }

                points.Add(new Vector2(x, y));
            }

            return points;
        }

        #endregion

        #region 事件派发

        private static async Task<SwipeTrace> DispatchHumanSwipeAsync(
            ICDPSession client,
            Vector2 start,
            Vector2 end,
            int steps,
            ScrollDirection direction,
            bool microSwipe,
            CancellationToken cancellationToken)
        {
            var points = GetHumanLikeSwipePoints(start, end, steps, microSwipe);
            int totalDelay = 0;

            try
            {
                double startForce = microSwipe
                    ? RandomUtil.NextDouble(0.78, 0.93)
                    : RandomUtil.NextDouble(0.84, 0.98);

                int radius = microSwipe
                    ? RandomUtil.NextInt(2, 4)
                    : RandomUtil.NextInt(3, 6);

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchStart",
                    ["touchPoints"] = new object[]
                    {
                        new
                        {
                            x = MathF.Round(points[0].X, 2),
                            y = MathF.Round(points[0].Y, 2),
                            radiusX = radius,
                            radiusY = radius,
                            force = startForce,
                            id = 0
                        }
                    },
                    ["modifiers"] = 0
                });

                int holdBeforeMove = microSwipe
                    ? RandomUtil.NextInt(12, 40)
                    : RandomUtil.NextInt(20, 65);

                await Task.Delay(holdBeforeMove, cancellationToken);
                totalDelay += holdBeforeMove;

                for (int i = 1; i < points.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    float progress = i / (float)(points.Count - 1);

                    int delay;
                    if (progress < 0.16f)
                    {
                        delay = microSwipe
                            ? RandomUtil.NextInt(8, 16)
                            : RandomUtil.NextInt(12, 20);
                    }
                    else if (progress < 0.76f)
                    {
                        delay = microSwipe
                            ? RandomUtil.NextInt(5, 11)
                            : RandomUtil.NextInt(6, 13);
                    }
                    else
                    {
                        delay = microSwipe
                            ? RandomUtil.NextInt(8, 15)
                            : RandomUtil.NextInt(10, 18);
                    }

                    if (RandomUtil.Chance(microSwipe ? 0.05 : 0.07))
                        delay += RandomUtil.NextInt(6, 18);

                    double force = microSwipe
                        ? RandomUtil.NextDouble(0.72, 0.90)
                        : RandomUtil.NextDouble(0.78, 0.95);

                    int moveRadius = microSwipe
                        ? RandomUtil.NextInt(2, 4)
                        : RandomUtil.NextInt(3, 5);

                    await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                    {
                        ["type"] = "touchMove",
                        ["touchPoints"] = new object[]
                        {
                            new
                            {
                                x = MathF.Round(points[i].X, 2),
                                y = MathF.Round(points[i].Y, 2),
                                radiusX = moveRadius,
                                radiusY = moveRadius,
                                force = force,
                                id = 0
                            }
                        },
                        ["modifiers"] = 0
                    });

                    await Task.Delay(delay, cancellationToken);
                    totalDelay += delay;
                }

                int holdAfterMove = microSwipe
                    ? RandomUtil.NextInt(5, 18)
                    : RandomUtil.NextInt(8, 28);

                await Task.Delay(holdAfterMove, cancellationToken);
                totalDelay += holdAfterMove;

                await client.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                {
                    ["type"] = "touchEnd",
                    ["touchPoints"] = Array.Empty<object>(),
                    ["modifiers"] = 0
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // 吞掉 CDP/IO 等异常，保留轨迹数据
            }

            return new SwipeTrace
            {
                Start = start,
                End = end,
                Points = points,
                TotalDelayMs = totalDelay,
                Direction = direction,
                IsMicroSwipe = microSwipe
            };
        }

        #endregion

        #region 工具方法

        private static int CalcSteps(double distance, int viewportHeight, bool microSwipe)
        {
            if (distance <= 0)
                return microSwipe ? 8 : 12;

            int minSteps = microSwipe ? 8 : 14;
            int maxSteps = microSwipe ? 18 : 34;

            double ratio = Math.Min(distance / (viewportHeight * 0.75), 1.0);
            int steps = (int)(minSteps + (maxSteps - minSteps) * ratio);

            steps += RandomUtil.NextInt(-2, 3);

            return Math.Max(minSteps, steps);
        }

        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f
                ? 4 * t * t * t
                : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
        }

        #endregion



        public static async Task<List<SwipeTrace>> SwipeToElementAsync(
        IPage page,
        ICDPSession client,
        ILocator element,
        int maxSwipes = 10,
        CancellationToken cancellationToken = default)
        {
            var traces = new List<SwipeTrace>();

            if (page == null || page.IsClosed || client == null || element == null || page.ViewportSize == null || maxSwipes <= 0)
                return traces;

            int vw = page.ViewportSize.Width;
            int vh = page.ViewportSize.Height;

            float comfortTop = vh * 0.22f;
            float comfortBottom = vh * 0.72f;

            try
            {
                // 先确保元素至少挂在 DOM 中
                if (await element.CountAsync() <= 0)
                    return traces;
            }
            catch
            {
                return traces;
            }

            for (int i = 0; i < maxSwipes; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    return traces;

                try
                {
                    var box = await element.BoundingBoxAsync();

                    // 元素拿不到 box，通常是还没进视口，先根据 DOM 位置判断大致方向
                    if (box == null)
                    {
                        var pos = await GetElementViewportPositionAsync(page, element);
                        if (pos == null)
                            return traces;

                        ScrollDirection direction = pos.CenterY < 0
                            ? ScrollDirection.Down   // 元素在屏幕上方，手指下滑，让页面往上回
                            : ScrollDirection.Up;    // 元素在屏幕下方，手指上滑，让页面往下走

                        bool micro = false;
                        int? distance = (int)Math.Clamp(vh * 0.42, vh * 0.24, vh * 0.58);

                        var trace = await SwipeOnceHumanAsync(
                            page: page,
                            client: client,
                            direction: direction,
                            area: SwipeArea.Normal,
                            microSwipe: micro,
                            totalDistancePx: distance,
                            verifyScrollChanged: true,
                            cancellationToken: cancellationToken);

                        if (trace == null)
                            return traces;

                        traces.Add(trace);

                        await Task.Delay(RandomUtil.NextInt(120, 260), cancellationToken);
                        continue;
                    }

                    float top = (float)box.Y;
                    float bottom = (float)(box.Y + box.Height);
                    float centerY = (float)(box.Y + box.Height / 2.0);

                    // 已经在舒适区，直接结束
                    if (centerY >= comfortTop && centerY <= comfortBottom)
                        return traces;

                    // 在视口里，但不在舒适区：做微调/中调
                    if (bottom >= 0 && top <= vh)
                    {
                        double distanceToComfort = centerY < comfortTop
                            ? comfortTop - centerY
                            : centerY - comfortBottom;

                        ScrollDirection direction = centerY < comfortTop
                            ? ScrollDirection.Down
                            : ScrollDirection.Up;

                        bool useMicro = distanceToComfort < vh * 0.20;

                        int? targetDistance = useMicro
                            ? (int)Math.Clamp(distanceToComfort * 0.90, vh * 0.08, vh * 0.18)
                            : (int)Math.Clamp(distanceToComfort * 0.95, vh * 0.18, vh * 0.42);

                        var trace = await SwipeOnceHumanAsync(
                            page: page,
                            client: client,
                            direction: direction,
                            area: useMicro ? SwipeArea.Micro : SwipeArea.Normal,
                            microSwipe: useMicro,
                            totalDistancePx: targetDistance,
                            verifyScrollChanged: true,
                            cancellationToken: cancellationToken);

                        if (trace == null)
                            return traces;

                        traces.Add(trace);

                        await Task.Delay(RandomUtil.NextInt(100, 220), cancellationToken);
                        continue;
                    }

                    // 完全在视口下方
                    if (top > vh)
                    {
                        double distance = top - comfortBottom;
                        int? targetDistance = (int)Math.Clamp(distance * 0.92, vh * 0.22, vh * 0.58);

                        var trace = await SwipeOnceHumanAsync(
                            page: page,
                            client: client,
                            direction: ScrollDirection.Up,
                            area: SwipeArea.Normal,
                            microSwipe: false,
                            totalDistancePx: targetDistance,
                            verifyScrollChanged: true,
                            cancellationToken: cancellationToken);

                        if (trace == null)
                            return traces;

                        traces.Add(trace);

                        await Task.Delay(RandomUtil.NextInt(120, 260), cancellationToken);
                        continue;
                    }

                    // 完全在视口上方
                    if (bottom < 0)
                    {
                        double distance = comfortTop - bottom;
                        int? targetDistance = (int)Math.Clamp(distance * 0.92, vh * 0.18, vh * 0.46);

                        var trace = await SwipeOnceHumanAsync(
                            page: page,
                            client: client,
                            direction: ScrollDirection.Down,
                            area: SwipeArea.Normal,
                            microSwipe: false,
                            totalDistancePx: targetDistance,
                            verifyScrollChanged: true,
                            cancellationToken: cancellationToken);

                        if (trace == null)
                            return traces;

                        traces.Add(trace);

                        await Task.Delay(RandomUtil.NextInt(120, 260), cancellationToken);
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return traces;
                }
            }

            return traces;
        }


        private sealed class ElementViewportPosition
        {
            public double Top { get; set; }
            public double Bottom { get; set; }
            public double CenterY { get; set; }
            public double ViewportHeight { get; set; }
        }

        private static async Task<ElementViewportPosition?> GetElementViewportPositionAsync(IPage page, ILocator element)
        {
            if (page == null || page.IsClosed || element == null)
                return null;

            try
            {
                return await page.EvaluateAsync<ElementViewportPosition>(@"
                (el) => {
                    try {
                        if (!el) return null;
                        const r = el.getBoundingClientRect();
                        return {
                            Top: Number(r.top || 0),
                            Bottom: Number(r.bottom || 0),
                            CenterY: Number((r.top + r.bottom) / 2 || 0),
                            ViewportHeight: Number(window.innerHeight || document.documentElement.clientHeight || 0)
                        };
                    } catch {
                        return null;
                    }
                }", await element.ElementHandleAsync());
            }
            catch
            {
                return null;
            }
        }
    }

    public static class SwipeTraceRenderer
    {
        public static void DrawTrajectoriesPng(
            List<SwipeTrace> traces,
            string filePath,
            int width,
            int height)
        {
            if (traces == null || traces.Count == 0)
                return;

            var colors = new Color[]
            {
                Color.Red,
                Color.Blue,
                Color.Green,
                Color.Orange,
                Color.Purple,
                Color.Brown,
                Color.DeepPink
            };

            using var image = new Image<Rgba32>(width, height);
            image.Mutate(ctx => ctx.Clear(Color.White));

            for (int t = 0; t < traces.Count; t++)
            {
                var trace = traces[t];
                var color = colors[t % colors.Length];

                image.Mutate(ctx =>
                {
                    ctx.Fill(Color.DarkGreen, new EllipsePolygon(trace.Start.X, trace.Start.Y, 5));
                    ctx.Fill(Color.DarkRed, new EllipsePolygon(trace.End.X, trace.End.Y, 5));
                });

                for (int i = 1; i < trace.Points.Count; i++)
                {
                    var p1 = trace.Points[i - 1];
                    var p2 = trace.Points[i];

                    image.Mutate(ctx =>
                    {
                        ctx.DrawLine(color, 2, new PointF[]
                        {
                            new PointF(p1.X, p1.Y),
                            new PointF(p2.X, p2.Y)
                        });

                        ctx.Fill(Color.Black, new EllipsePolygon(p2.X, p2.Y, 1.8f));
                    });
                }
            }

            image.Save(filePath);
        }

        public static void DrawSwipeGif(
            List<SwipeTrace> traces,
            string filePath,
            int width,
            int height,
            int frameDelayMs = 40,
            bool drawStartEnd = true)
        {
            if (traces == null || traces.Count == 0)
                return;

            var colors = new Color[]
            {
                Color.Red,
                Color.Blue,
                Color.Green,
                Color.Orange,
                Color.Purple,
                Color.Brown,
                Color.DeepPink
            };

            using var gif = new Image<Rgba32>(width, height);
            gif.Mutate(ctx => ctx.Clear(Color.White));

            for (int t = 0; t < traces.Count; t++)
            {
                var trace = traces[t];
                var color = colors[t % colors.Length];

                for (int i = 0; i < trace.Points.Count; i++)
                {
                    using var frame = new Image<Rgba32>(width, height);
                    frame.Mutate(ctx => ctx.Clear(Color.White));

                    if (drawStartEnd)
                    {
                        frame.Mutate(ctx =>
                        {
                            ctx.Fill(Color.DarkGreen, new EllipsePolygon(trace.Start.X, trace.Start.Y, 5));
                            ctx.Fill(Color.DarkRed, new EllipsePolygon(trace.End.X, trace.End.Y, 5));
                        });
                    }

                    for (int j = 1; j <= i; j++)
                    {
                        var p1 = trace.Points[j - 1];
                        var p2 = trace.Points[j];

                        frame.Mutate(ctx =>
                        {
                            ctx.DrawLine(color, 2, new PointF[]
                            {
                                new PointF(p1.X, p1.Y),
                                new PointF(p2.X, p2.Y)
                            });

                            ctx.Fill(Color.Black, new EllipsePolygon(p2.X, p2.Y, 2));
                        });
                    }

                    var current = trace.Points[i];
                    frame.Mutate(ctx =>
                    {
                        ctx.Fill(Color.Black, new EllipsePolygon(current.X, current.Y, 3.5f));
                    });

                    frame.Frames.RootFrame.Metadata.GetGifMetadata().FrameDelay = Math.Max(1, frameDelayMs / 10);
                    gif.Frames.AddFrame(frame.Frames.RootFrame);
                }
            }

            gif.SaveAsGif(filePath);
        }

        public static void DrawPngAndGif(
            List<SwipeTrace> traces,
            string pngPath,
            string gifPath,
            int width,
            int height,
            int frameDelayMs = 40)
        {
            DrawTrajectoriesPng(traces, pngPath, width, height);
            DrawSwipeGif(traces, gifPath, width, height, frameDelayMs);
        }
    }
}
