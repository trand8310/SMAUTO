using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput
{
    public enum HumanSwipeDirection
    {
        Up,
        Down,
        Left,
        Right,
        RandomVertical,
        RandomAny
    }

    public enum HumanSwipeMode
    {
        /// <summary>慢速阅读：手指慢慢拖动，基本无惯性。</summary>
        Reading,

        /// <summary>快速预览：中速滑动，有少量惯性。</summary>
        Preview,

        /// <summary>快速甩动：快速抬手，浏览器产生惯性滚动。</summary>
        Fling,

        /// <summary>小幅微调：适合把元素移动到舒适区。</summary>
        Micro
    }

    public sealed class HumanSwipeOptions
    {
        public HumanSwipeDirection Direction { get; set; } = HumanSwipeDirection.Up;
        public HumanSwipeMode Mode { get; set; } = HumanSwipeMode.Preview;

        /// <summary>
        /// 速度倍率：1.0 默认，0.7 更慢，1.3 更快，最大会限制到 3.0。
        /// </summary>
        public double SpeedFactor { get; set; } = 1.0;

        public bool UseBezierCurve { get; set; } = true;
        public bool UseJitter { get; set; } = true;
        public bool HoldBeforeMove { get; set; } = true;

        /// <summary>
        /// null 表示按 Mode 自动决定。Reading/Micro 默认 true，Fling 默认 false。
        /// </summary>
        public bool? HoldBeforeEnd { get; set; }

        public int? StartX { get; set; }
        public int? StartY { get; set; }
        public int? EndX { get; set; }
        public int? EndY { get; set; }

        /// <summary>
        /// 指定滑动距离。垂直方向表示 Y 轴距离，横向方向表示 X 轴距离。
        /// </summary>
        public int? DistancePx { get; set; }

        public int? Steps { get; set; }

        /// <summary>
        /// 视口安全边距，避免太靠近顶部/底部/左右边缘。
        /// </summary>
        public int SafeMargin { get; set; } = 24;

        /// <summary>
        /// 抖动最大像素。
        /// </summary>
        public double MaxJitter { get; set; } = 1.8;

        /// <summary>
        /// 横向滑动时，副轴 Y 的随机偏移；纵向滑动时，副轴 X 的随机偏移。
        /// </summary>
        public int CrossAxisJitter { get; set; } = 10;

        /// <summary>
        /// 连续型真人微抖动。true 时，微抖不是每个点独立随机，而是带惯性地平滑变化。
        /// </summary>
        public bool UseSmoothJitter { get; set; } = true;

        /// <summary>
        /// 连续型副轴漂移。垂直滑动时主要影响 X，横向滑动时主要影响 Y。
        /// </summary>
        public bool EnableCrossAxisDrift { get; set; } = true;

        /// <summary>
        /// 副轴漂移最大像素。null 表示根据 Mode 和距离自动计算。
        /// </summary>
        public double? MaxCrossAxisDriftPx { get; set; }

        /// <summary>
        /// 启用触点压力连续曲线。压力会按“按下变重 -> 中段稳定 -> 抬手变轻”变化。
        /// </summary>
        public bool EnableForceCurve { get; set; } = true;

        /// <summary>
        /// 启用触点面积连续曲线。radiusX/radiusY 会跟随进度和压力平滑变化。
        /// </summary>
        public bool EnableTouchAreaCurve { get; set; } = true;

        /// <summary>
        /// 启用中途犹豫停顿。Reading/Micro 更明显，Fling 自动忽略。
        /// </summary>
        public bool EnableHesitationPause { get; set; } = true;

        /// <summary>
        /// 中途犹豫概率。null 表示按 Mode 自动决定。
        /// </summary>
        public double? HesitationChance { get; set; }

        public int MinHesitationMs { get; set; } = 80;
        public int MaxHesitationMs { get; set; } = 260;

        /// <summary>
        /// 启用末尾轻微回拉。只建议 Reading/Micro/少量 Preview，Fling 自动忽略。
        /// </summary>
        public bool EnableEndPullBack { get; set; } = true;

        /// <summary>
        /// 末尾回拉概率。null 表示按 Mode 自动决定。
        /// </summary>
        public double? EndPullBackChance { get; set; }

        public int MinPullBackPx { get; set; } = 2;
        public int MaxPullBackPx { get; set; } = 8;

        /// <summary>
        /// 滑到元素后启用视觉确认停顿。用于“滑一下，看一下，再继续”的节奏。
        /// </summary>
        public bool EnableVisualConfirmPause { get; set; } = true;

        public int MinVisualConfirmMs { get; set; } = 180;
        public int MaxVisualConfirmMs { get; set; } = 680;
        public int NearTargetExtraPauseMs { get; set; } = 300;

        /// <summary>
        /// true 时，滑动前判断当前触点命中的 document/内部滚动容器是否还能按指定方向滚动。
        /// </summary>
        public bool CheckScrollableBeforeSwipe { get; set; } = true;

        /// <summary>
        /// true 时，滑动后验证 document/内部滚动容器是否真的发生滚动。
        /// </summary>
        public bool VerifyScrollChanged { get; set; } = true;

        public double ScrollChangedMinDelta { get; set; } = 8;

        public int MaxPathTry { get; set; } = 10;

        public Action<string>? Log { get; set; }
        public HumanSwipeStyleProfile? StyleProfile { get; set; }
    }

    public sealed class HumanSwipeStyleProfile
    {
        public string ProfileId { get; init; } = Guid.NewGuid().ToString("N");
        public int Seed { get; init; } = Environment.TickCount;

        public double SpeedBias { get; init; } = 1.0;
        public double CurveBias { get; init; } = 1.0;
        public double JitterBias { get; init; } = 1.0;
        public double DriftBias { get; init; } = 1.0;
        public double ForceBias { get; init; } = 1.0;
        public double TouchAreaBias { get; init; } = 1.0;
        public double PauseBias { get; init; } = 1.0;
        public double HesitationBias { get; init; } = 1.0;
        public double PullBackBias { get; init; } = 1.0;
        public double DistanceBias { get; init; } = 1.0;

        public double VerticalCenterXRatio { get; init; } = 0.5;
        public double HorizontalCenterYRatio { get; init; } = 0.5;
        public double StartHoldBias { get; init; } = 1.0;
        public double EndHoldBias { get; init; } = 1.0;

        public static HumanSwipeStyleProfile CreateRandom()
        {
            var seed = Guid.NewGuid().GetHashCode();
            var rnd = new Random(seed);
            double n(double min, double max) => min + rnd.NextDouble() * (max - min);
            return new HumanSwipeStyleProfile
            {
                ProfileId = Guid.NewGuid().ToString("N"),
                Seed = seed,
                SpeedBias = n(0.85, 1.20),
                CurveBias = n(0.85, 1.20),
                JitterBias = n(0.75, 1.30),
                DriftBias = n(0.75, 1.30),
                ForceBias = n(0.82, 1.20),
                TouchAreaBias = n(0.82, 1.20),
                PauseBias = n(0.75, 1.35),
                HesitationBias = n(0.70, 1.45),
                PullBackBias = n(0.70, 1.45),
                DistanceBias = n(0.85, 1.20),
                VerticalCenterXRatio = n(0.40, 0.72),
                HorizontalCenterYRatio = n(0.40, 0.70),
                StartHoldBias = n(0.75, 1.35),
                EndHoldBias = n(0.75, 1.35)
            };
        }
    }

    public sealed class HumanSwipeTracePoint
    {
        public double X { get; init; }
        public double Y { get; init; }

        /// <summary>当前点之后的停顿时间，用于 GIF 按真实节奏播放。</summary>
        public int DelayMs { get; init; }

        public double RadiusX { get; init; }
        public double RadiusY { get; init; }
        public double Force { get; init; }
        public double RotationAngle { get; init; }
    }

    public sealed class HumanSwipeTrace
    {
        public double StartX { get; init; }
        public double StartY { get; init; }
        public double EndX { get; init; }
        public double EndY { get; init; }
        public HumanSwipeDirection Direction { get; init; }
        public HumanSwipeMode Mode { get; init; }
        public int Steps { get; init; }
        public int TotalDelayMs { get; init; }
        public bool ScrollChanged { get; init; }

        /// <summary>HumanSwipeEmulator 实际生成并发送的轨迹点。</summary>
        public List<HumanSwipeTracePoint> Points { get; init; } = new();
    }

    public sealed class ElementRect
    {
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }

        public double Left => X;
        public double Right => X + Width;
        public double Top => Y;
        public double Bottom => Y + Height;
        public double CenterX => X + Width / 2.0;
        public double CenterY => Y + Height / 2.0;
    }

    public sealed class ScrollTargetState
    {
        public string Kind { get; set; } = "document";
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
        public bool CanScrollHorizontally => ScrollWidth > ClientWidth + 2;

        public bool IsNearTop => ScrollTop <= 6;
        public bool IsNearBottom => ScrollTop + ClientHeight >= ScrollHeight - 6;
        public bool IsNearLeft => ScrollLeft <= 6;
        public bool IsNearRight => ScrollLeft + ClientWidth >= ScrollWidth - 6;
    }

    internal readonly struct PointD
    {
        public PointD(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

    internal readonly struct TouchSample
    {
        public TouchSample(PointD point, int delayMs, double radiusX, double radiusY, double force, double rotationAngle)
        {
            Point = point;
            DelayMs = delayMs;
            RadiusX = radiusX;
            RadiusY = radiusY;
            Force = force;
            RotationAngle = rotationAngle;
        }

        public PointD Point { get; }
        public int DelayMs { get; }
        public double RadiusX { get; }
        public double RadiusY { get; }
        public double Force { get; }
        public double RotationAngle { get; }
    }

    internal sealed class GestureDynamics
    {
        public double DriftPeak { get; init; }
        public double DriftSide { get; init; }
        public double DriftPhase { get; init; }
        public double SmoothJitterX { get; set; }
        public double SmoothJitterY { get; set; }
        public double ForceNoise { get; set; }
        public double RadiusNoiseX { get; set; }
        public double RadiusNoiseY { get; set; }
    }

    /// <summary>
    /// 合并版：
    /// 1. 保留滚动容器检测、滚动前后验证、滑到元素舒适区。
    /// 2. 合并上下左右、Reading/Preview/Fling/Micro、贝塞尔轨迹、速度倍率。
    /// 3. 不再向页面写 data-BDAd-swipe-id，避免污染业务 DOM。
    /// </summary>
    public static class HumanSwipeEmulator
    {
        private static readonly ThreadLocal<Random> RandomLocal =
            new(() => new Random(Guid.NewGuid().GetHashCode()));
        private static readonly AsyncLocal<HumanSwipeStyleProfile?> StyleScope = new();

        public static IDisposable BeginStyleScope(HumanSwipeStyleProfile? styleProfile)
        {
            var previous = StyleScope.Value;
            StyleScope.Value = styleProfile;
            return new Scope(() => StyleScope.Value = previous);
        }

        #region 对外入口

        public static async Task EnableTouchInputAsync(IPage page, ICDPSession cdp)
        {
            if (page == null || page.IsClosed || cdp == null)
                return;

            try
            {
                await page.BringToFrontAsync();
            }
            catch
            {
            }

            try
            {
                await cdp.SendAsync("Input.setIgnoreInputEvents", new Dictionary<string, object>
                {
                    ["ignore"] = false
                });
            }
            catch
            {
            }

            try
            {
                await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["maxTouchPoints"] = 5
                });
            }
            catch
            {
            }
        }

        /// <summary>
        /// 页面/视口滑动：会自动选择安全起点，并可验证 document 或内部滚动容器是否滚动。
        /// </summary>
        public static async Task<HumanSwipeTrace?> SwipeAsync(
            IPage page,
            ICDPSession cdp,
            HumanSwipeOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new HumanSwipeOptions();
            ApplyStyle(options);

            if (page == null || page.IsClosed || cdp == null || page.ViewportSize == null)
                return null;

            await EnableTouchInputAsync(page, cdp);

            int viewportWidth = page.ViewportSize.Width;
            int viewportHeight = page.ViewportSize.Height;
            var actualDirection = PickDirection(options.Direction);

            (int startX, int startY, int endX, int endY)? path = null;

            for (int i = 0; i < Math.Max(1, options.MaxPathTry); i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidate = BuildViewportSwipePath(viewportWidth, viewportHeight, actualDirection, options);

                if (!options.CheckScrollableBeforeSwipe)
                {
                    path = candidate;
                    break;
                }

                bool canSwipe = await CanSafelySwipeDirectionOnTargetAsync(
                    page,
                    actualDirection,
                    candidate.startX,
                    candidate.startY);

                if (canSwipe)
                {
                    path = candidate;
                    break;
                }
            }

            if (path == null)
                return null;

            ScrollTargetState? before = null;

            if (options.VerifyScrollChanged)
            {
                before = await GetScrollTargetStateAsync(page, path.Value.startX, path.Value.startY);
            }

            var trace = await RunSwipePathAsync(
                cdp,
                path.Value.startX,
                path.Value.startY,
                path.Value.endX,
                path.Value.endY,
                actualDirection,
                options,
                cancellationToken);

            bool moved = true;

            if (options.VerifyScrollChanged && before != null)
            {
                await Task.Delay(RandomRange(80, 180), cancellationToken);

                moved = await DidScrollTargetAsync(
                    page,
                    before,
                    path.Value.startX,
                    path.Value.startY,
                    actualDirection,
                    options.ScrollChangedMinDelta);
            }

            if (options.VerifyScrollChanged && !moved)
                return null;

            return new HumanSwipeTrace
            {
                StartX = path.Value.startX,
                StartY = path.Value.startY,
                EndX = path.Value.endX,
                EndY = path.Value.endY,
                Direction = actualDirection,
                Mode = options.Mode,
                Steps = trace.steps,
                TotalDelayMs = trace.totalDelayMs,
                ScrollChanged = moved,
                Points = trace.points
            };
        }

        /// <summary>
        /// 元素内部滑动：适合 Banner、Swiper、横向卡片、元素内列表。
        /// </summary>
        public static async Task<HumanSwipeTrace?> SwipeInsideElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            HumanSwipeOptions options,
            CancellationToken cancellationToken = default)
        {
            if (locator == null)
                return null;

            var rect = await GetElementRectAsync(locator);
            if (rect == null)
                return null;

            return await SwipeInsideElementAsync(page, cdp, rect, options, cancellationToken);
        }

        public static async Task<HumanSwipeTrace?> SwipeInsideElementAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            HumanSwipeOptions options,
            CancellationToken cancellationToken = default)
        {
            if (element == null)
                return null;

            var rect = await GetElementRectAsync(element);
            if (rect == null)
                return null;

            return await SwipeInsideElementAsync(page, cdp, rect, options, cancellationToken);
        }

        public static async Task<HumanSwipeTrace?> SwipeInsideElementAsync(
            IPage page,
            ICDPSession cdp,
            ElementRect rect,
            HumanSwipeOptions options,
            CancellationToken cancellationToken = default)
        {
            if (page == null || page.IsClosed || cdp == null || rect == null || page.ViewportSize == null)
                return null;

            options ??= new HumanSwipeOptions
            {
                Direction = HumanSwipeDirection.Left,
                Mode = HumanSwipeMode.Preview,
                SafeMargin = 8
            };

            await EnableTouchInputAsync(page, cdp);

            var actualDirection = PickDirection(options.Direction);

            var path = BuildElementSwipePath(
                rect,
                page.ViewportSize.Width,
                page.ViewportSize.Height,
                actualDirection,
                options);

            var run = await RunSwipePathAsync(
                cdp,
                path.startX,
                path.startY,
                path.endX,
                path.endY,
                actualDirection,
                options,
                cancellationToken);

            return new HumanSwipeTrace
            {
                StartX = path.startX,
                StartY = path.startY,
                EndX = path.endX,
                EndY = path.endY,
                Direction = actualDirection,
                Mode = options.Mode,
                Steps = run.steps,
                TotalDelayMs = run.totalDelayMs,
                ScrollChanged = true,
                Points = run.points
            };
        }

        /// <summary>
        /// 把元素滑动到屏幕舒适区。主要处理垂直方向。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> SwipeToElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            float comfortTopRatio = 0.22f,
            float comfortBottomRatio = 0.72f,
            CancellationToken cancellationToken = default)
        {
            var adapter = new LocatorElementAdapter(locator);
            return await SwipeToElementCoreAsync(page, cdp, adapter, maxSwipes, comfortTopRatio, comfortBottomRatio, cancellationToken);
        }

        public static async Task<List<HumanSwipeTrace>> SwipeToElementAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            int maxSwipes = 10,
            float comfortTopRatio = 0.22f,
            float comfortBottomRatio = 0.72f,
            CancellationToken cancellationToken = default)
        {
            var adapter = new HandleElementAdapter(element);
            return await SwipeToElementCoreAsync(page, cdp, adapter, maxSwipes, comfortTopRatio, comfortBottomRatio, cancellationToken);
        }

        /// <summary>
        /// 把元素滑动到屏幕可见即可。
        /// 不要求进入舒适区，元素只要有一部分出现在 viewport 内就返回。
        /// 适合元素在头部、底部、广告位、列表边缘等场景。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> SwipeToElementVisibleAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            float visibleMarginPx = 8f,
            CancellationToken cancellationToken = default)
        {
            var adapter = new LocatorElementAdapter(locator);

            return await SwipeToElementVisibleCoreAsync(
                page,
                cdp,
                adapter,
                maxSwipes,
                visibleMarginPx,
                cancellationToken);
        }

        /// <summary>
        /// 把元素滑动到屏幕可见即可。
        /// IElementHandle 版本。
        /// </summary>
        public static async Task<List<HumanSwipeTrace>> SwipeToElementVisibleAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            int maxSwipes = 10,
            float visibleMarginPx = 8f,
            CancellationToken cancellationToken = default)
        {
            var adapter = new HandleElementAdapter(element);

            return await SwipeToElementVisibleCoreAsync(
                page,
                cdp,
                adapter,
                maxSwipes,
                visibleMarginPx,
                cancellationToken);
        }



        public static async Task<ElementRect?> GetElementRectAsync(ILocator locator)
        {
            if (locator == null)
                return null;

            try
            {
                var box = await locator.BoundingBoxAsync();
                if (box == null)
                    return null;

                return new ElementRect
                {
                    X = box.X,
                    Y = box.Y,
                    Width = box.Width,
                    Height = box.Height
                };
            }
            catch
            {
                return null;
            }
        }

        public static async Task<ElementRect?> GetElementRectAsync(IElementHandle element)
        {
            if (element == null)
                return null;

            try
            {
                var box = await element.BoundingBoxAsync();
                if (box == null)
                    return null;

                return new ElementRect
                {
                    X = box.X,
                    Y = box.Y,
                    Width = box.Width,
                    Height = box.Height
                };
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region 滑到元素核心逻辑

        private interface IElementAdapter
        {
            Task<bool> ExistsAsync();
            Task<ElementRect?> BoundingBoxAsync();
            Task<ElementViewportPosition?> GetViewportPositionAsync(IPage page);
            Task<bool> ScrollIntoViewIfNeededAsync(CancellationToken cancellationToken);
        }

        private sealed class LocatorElementAdapter : IElementAdapter
        {
            private readonly ILocator _locator;

            public LocatorElementAdapter(ILocator locator)
            {
                _locator = locator;
            }

            public async Task<bool> ExistsAsync()
            {
                try
                {
                    return _locator != null && await _locator.CountAsync() > 0;
                }
                catch
                {
                    return false;
                }
            }

            public Task<ElementRect?> BoundingBoxAsync()
            {
                return GetElementRectAsync(_locator);
            }

            public async Task<ElementViewportPosition?> GetViewportPositionAsync(IPage page)
            {
                try
                {
                    var handle = await _locator.ElementHandleAsync();
                    if (handle == null)
                        return null;

                    return await page.EvaluateAsync<ElementViewportPosition?>(@"
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
                        }", handle);
                }
                catch
                {
                    return null;
                }
            }

            public async Task<bool> ScrollIntoViewIfNeededAsync(CancellationToken cancellationToken)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await _locator.ScrollIntoViewIfNeededAsync(new()
                    {
                        Timeout = RandomRange(1200, 2600)
                    });

                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return false;
                }
            }
        }

        private sealed class HandleElementAdapter : IElementAdapter
        {
            private readonly IElementHandle _element;

            public HandleElementAdapter(IElementHandle element)
            {
                _element = element;
            }

            public async Task<bool> ExistsAsync()
            {
                try
                {
                    return _element != null && await _element.EvaluateAsync<bool>("el => !!el && el.isConnected");
                }
                catch
                {
                    return false;
                }
            }

            public Task<ElementRect?> BoundingBoxAsync()
            {
                return GetElementRectAsync(_element);
            }

            public async Task<ElementViewportPosition?> GetViewportPositionAsync(IPage page)
            {
                try
                {
                    return await _element.EvaluateAsync<ElementViewportPosition?>(@"
                        (el) => {
                            try {
                                if (!el || !el.isConnected) return null;
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
                        }");
                }
                catch
                {
                    return null;
                }
            }

            public async Task<bool> ScrollIntoViewIfNeededAsync(CancellationToken cancellationToken)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await _element.ScrollIntoViewIfNeededAsync(new()
                    {
                        Timeout = RandomRange(1200, 2600)
                    });

                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    return false;
                }
            }
        }

        private sealed class ElementViewportPosition
        {
            public double Top { get; set; }
            public double Bottom { get; set; }
            public double CenterY { get; set; }
            public double ViewportHeight { get; set; }
        }

        private static async Task<List<HumanSwipeTrace>> SwipeToElementCoreAsync(
            IPage page,
            ICDPSession cdp,
            IElementAdapter element,
            int maxSwipes,
            float comfortTopRatio,
            float comfortBottomRatio,
            CancellationToken cancellationToken)
        {
            var traces = new List<HumanSwipeTrace>();

            if (page == null || page.IsClosed || cdp == null || element == null || page.ViewportSize == null || maxSwipes <= 0)
                return traces;

            int viewportHeight = page.ViewportSize.Height;
            float comfortTop = viewportHeight * comfortTopRatio;
            float comfortBottom = viewportHeight * comfortBottomRatio;

            if (!await element.ExistsAsync())
                return traces;

            await Task.Delay(RandomRange(180, 420), cancellationToken);

            for (int i = 0; i < maxSwipes; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    return traces;

                var box = await element.BoundingBoxAsync();

                if (box == null)
                {
                    var pos = await element.GetViewportPositionAsync(page);

                    if (pos == null)
                    {
                        if (await element.ScrollIntoViewIfNeededAsync(cancellationToken))
                        {
                            await Task.Delay(RandomRange(260, 560), cancellationToken);
                            continue;
                        }

                        return traces;
                    }

                    var dir = pos.CenterY < 0
                        ? HumanSwipeDirection.Down
                        : HumanSwipeDirection.Up;

                    var trace = await SwipeAsync(
                        page,
                        cdp,
                        new HumanSwipeOptions
                        {
                            Direction = dir,
                            Mode = HumanSwipeMode.Preview,
                            DistancePx = (int)Math.Clamp(viewportHeight * 0.30, viewportHeight * 0.18, viewportHeight * 0.38),
                            VerifyScrollChanged = true,
                            CheckScrollableBeforeSwipe = true
                        },
                        cancellationToken);

                    if (trace == null)
                    {
                        if (await element.ScrollIntoViewIfNeededAsync(cancellationToken))
                        {
                            await Task.Delay(RandomRange(260, 560), cancellationToken);
                            continue;
                        }

                        return traces;
                    }

                    traces.Add(trace);
                    await Task.Delay(
                        GetVisualConfirmDelay(new HumanSwipeOptions(), false, false, 420, 980),
                        cancellationToken);
                    continue;
                }

                double centerY = box.CenterY;

                if (centerY >= comfortTop && centerY <= comfortBottom)
                    return traces;

                double distanceToComfort = centerY < comfortTop
                    ? comfortTop - centerY
                    : centerY - comfortBottom;

                var direction = centerY < comfortTop
                    ? HumanSwipeDirection.Down
                    : HumanSwipeDirection.Up;

                bool useMicro = distanceToComfort < viewportHeight * 0.24;

                int distance = useMicro
                    ? CalcTargetDistance(distanceToComfort, viewportHeight, 0.58, 0.74, 0.06, 0.16)
                    : CalcTargetDistance(distanceToComfort, viewportHeight, 0.64, 0.80, 0.14, 0.32);

                var swipeTrace = await SwipeAsync(
                    page,
                    cdp,
                    new HumanSwipeOptions
                    {
                        Direction = direction,
                        Mode = useMicro ? HumanSwipeMode.Micro : HumanSwipeMode.Preview,
                        DistancePx = distance,
                        VerifyScrollChanged = true,
                        CheckScrollableBeforeSwipe = true,
                        ScrollChangedMinDelta = useMicro ? 4 : 8
                    },
                    cancellationToken);

                if (swipeTrace == null)
                {
                    if (await element.ScrollIntoViewIfNeededAsync(cancellationToken))
                    {
                        await Task.Delay(RandomRange(260, 560), cancellationToken);
                        continue;
                    }

                    return traces;
                }

                traces.Add(swipeTrace);

                bool nearComfort = distanceToComfort < viewportHeight * 0.18;
                await Task.Delay(
                    GetVisualConfirmDelay(
                        new HumanSwipeOptions(),
                        useMicro,
                        nearComfort,
                        useMicro ? 260 : 420,
                        useMicro ? 560 : 980),
                    cancellationToken);
            }

            await element.ScrollIntoViewIfNeededAsync(cancellationToken);
            return traces;
        }



        private static async Task<List<HumanSwipeTrace>> SwipeToElementVisibleCoreAsync(
        IPage page,
        ICDPSession cdp,
        IElementAdapter element,
        int maxSwipes,
        float visibleMarginPx,
        CancellationToken cancellationToken)
        {
            var traces = new List<HumanSwipeTrace>();

            if (page == null || page.IsClosed || cdp == null || element == null || page.ViewportSize == null || maxSwipes <= 0)
                return traces;

            int viewportHeight = page.ViewportSize.Height;

            if (!await element.ExistsAsync())
                return traces;

            await Task.Delay(RandomRange(120, 320), cancellationToken);

            for (int i = 0; i < maxSwipes; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (page.IsClosed)
                    return traces;

                var box = await element.BoundingBoxAsync();

                if (box != null)
                {
                    // 只要元素与 viewport 有交集，就认为可见。
                    if (IsElementVisibleInViewport(box, viewportHeight, visibleMarginPx))
                        return traces;

                    double centerY = box.CenterY;

                    HumanSwipeDirection direction;
                    int distance;

                    if (box.Top >= viewportHeight)
                    {
                        // 元素在屏幕下方，向上滑，让页面内容向上走。
                        direction = HumanSwipeDirection.Up;

                        double distanceToViewport = box.Top - viewportHeight;
                        distance = CalcVisibleTargetDistance(
                            distanceToViewport,
                            viewportHeight,
                            minViewportRatio: 0.16,
                            maxViewportRatio: 0.42);
                    }
                    else if (box.Bottom <= 0)
                    {
                        // 元素在屏幕上方，向下滑，让页面内容往下回。
                        direction = HumanSwipeDirection.Down;

                        double distanceToViewport = -box.Bottom;
                        distance = CalcVisibleTargetDistance(
                            distanceToViewport,
                            viewportHeight,
                            minViewportRatio: 0.14,
                            maxViewportRatio: 0.36);
                    }
                    else
                    {
                        // 理论上已经有交集了，直接返回。
                        return traces;
                    }

                    bool useMicro = distance < viewportHeight * 0.18;

                    var trace = await SwipeAsync(
                        page,
                        cdp,
                        new HumanSwipeOptions
                        {
                            Direction = direction,
                            Mode = useMicro ? HumanSwipeMode.Micro : HumanSwipeMode.Preview,
                            DistancePx = distance,

                            Steps = useMicro
                                ? RandomRange(16, 30)
                                : RandomRange(24, 42),

                            SpeedFactor = useMicro
                                ? RandomRangeDouble(0.85, 1.15)
                                : RandomRangeDouble(1.0, 1.35),

                            HoldBeforeMove = true,
                            HoldBeforeEnd = useMicro,

                            UseBezierCurve = true,
                            UseJitter = true,
                            MaxJitter = useMicro
                                ? RandomRangeDouble(0.4, 0.9)
                                : RandomRangeDouble(1.0, 1.8),

                            VerifyScrollChanged = true,
                            CheckScrollableBeforeSwipe = true,
                            ScrollChangedMinDelta = useMicro ? 3 : 8
                        },
                        cancellationToken);

                    if (trace == null)
                    {
                        // 如果手势滚不动，最后兜底用 Playwright 原生 scrollIntoViewIfNeeded。
                        if (await element.ScrollIntoViewIfNeededAsync(cancellationToken))
                        {
                            await Task.Delay(RandomRange(120, 300), cancellationToken);

                            var afterBox = await element.BoundingBoxAsync();
                            if (afterBox != null && IsElementVisibleInViewport(afterBox, viewportHeight, visibleMarginPx))
                                return traces;
                        }

                        return traces;
                    }

                    traces.Add(trace);

                    bool nearVisible = Math.Abs(box.Top) < viewportHeight * 0.18 || Math.Abs(box.Bottom - viewportHeight) < viewportHeight * 0.18;
                    await Task.Delay(
                        GetVisualConfirmDelay(
                            new HumanSwipeOptions(),
                            useMicro,
                            nearVisible,
                            useMicro ? 180 : 300,
                            useMicro ? 420 : 760),
                        cancellationToken);

                    continue;
                }

                // box == null：元素可能在 viewport 外、被虚拟列表处理、暂不可见。
                var pos = await element.GetViewportPositionAsync(page);

                if (pos == null)
                {
                    if (await element.ScrollIntoViewIfNeededAsync(cancellationToken))
                    {
                        await Task.Delay(RandomRange(150, 360), cancellationToken);

                        var afterBox = await element.BoundingBoxAsync();
                        if (afterBox != null && IsElementVisibleInViewport(afterBox, viewportHeight, visibleMarginPx))
                            return traces;

                        continue;
                    }

                    return traces;
                }

                if (pos.Bottom > visibleMarginPx && pos.Top < viewportHeight - visibleMarginPx)
                    return traces;

                var dir = pos.CenterY < 0
                    ? HumanSwipeDirection.Down
                    : HumanSwipeDirection.Up;

                int fallbackDistance = RandomRange(
                    (int)(viewportHeight * 0.22),
                    (int)(viewportHeight * 0.38));

                var fallbackTrace = await SwipeAsync(
                    page,
                    cdp,
                    new HumanSwipeOptions
                    {
                        Direction = dir,
                        Mode = HumanSwipeMode.Preview,
                        DistancePx = fallbackDistance,
                        VerifyScrollChanged = true,
                        CheckScrollableBeforeSwipe = true,
                        ScrollChangedMinDelta = 8
                    },
                    cancellationToken);

                if (fallbackTrace == null)
                {
                    if (await element.ScrollIntoViewIfNeededAsync(cancellationToken))
                    {
                        await Task.Delay(RandomRange(150, 360), cancellationToken);
                        continue;
                    }

                    return traces;
                }

                traces.Add(fallbackTrace);

                await Task.Delay(RandomRange(300, 760), cancellationToken);
            }

            return traces;
        }

        private static int GetVisualConfirmDelay(
            HumanSwipeOptions options,
            bool useMicro,
            bool nearTarget,
            int baseMinMs,
            int baseMaxMs)
        {
            int min = Math.Max(0, options.MinVisualConfirmMs);
            int max = Math.Max(min, options.MaxVisualConfirmMs);

            int delay = options.EnableVisualConfirmPause
                ? RandomRange(Math.Max(baseMinMs, min), Math.Max(baseMaxMs, max))
                : RandomRange(baseMinMs, baseMaxMs);

            if (options.EnableVisualConfirmPause && nearTarget)
                delay += RandomRange(80, Math.Max(90, options.NearTargetExtraPauseMs));

            if (useMicro)
                delay = (int)Math.Round(delay * RandomRangeDouble(0.82, 1.05));

            return Math.Max(1, delay);
        }

        private static bool IsElementVisibleInViewport(
        ElementRect box,
        int viewportHeight,
        float visibleMarginPx)
        {
            if (box == null)
                return false;

            // 只判断垂直可见即可。
            // 允许边缘留一点 margin，避免刚好 1px 出现就误判。
            return box.Bottom > visibleMarginPx
                && box.Top < viewportHeight - visibleMarginPx
                && box.Height > 0;
        }


        private static int CalcVisibleTargetDistance(
        double distanceToViewport,
        int viewportHeight,
        double minViewportRatio,
        double maxViewportRatio)
        {
            double ratio = RandomRangeDouble(0.62, 0.86);

            return (int)Math.Clamp(
                distanceToViewport * ratio + viewportHeight * 0.08,
                viewportHeight * minViewportRatio,
                viewportHeight * maxViewportRatio);
        }


        private static int CalcTargetDistance(
            double distanceToTarget,
            int viewportHeight,
            double minDistanceRatio,
            double maxDistanceRatio,
            double minViewportRatio,
            double maxViewportRatio)
        {
            double ratio = RandomRangeDouble(minDistanceRatio, maxDistanceRatio);

            return (int)Math.Clamp(
                distanceToTarget * ratio,
                viewportHeight * minViewportRatio,
                viewportHeight * maxViewportRatio);
        }

        #endregion

        #region 滚动容器检测：不污染 DOM

        private static async Task<ScrollTargetState> GetScrollTargetStateAsync(
            IPage page,
            double hitX,
            double hitY)
        {
            if (page == null || page.IsClosed)
                return new ScrollTargetState();

            try
            {
                var result = await page.EvaluateAsync<ScrollTargetState>(@"
                    (arg) => {
                        const x = Number(arg.x || 0);
                        const y = Number(arg.y || 0);

                        function canScroll(el, axis) {
                            if (!el) return false;

                            const style = getComputedStyle(el);
                            if (!style) return false;
                            if (style.display === 'none') return false;
                            if (style.visibility === 'hidden') return false;

                            if (axis === 'y') {
                                const overflowY = style.overflowY;
                                const scrollable =
                                    overflowY === 'auto' ||
                                    overflowY === 'scroll' ||
                                    overflowY === 'overlay';
                                return scrollable && el.scrollHeight > el.clientHeight + 2;
                            }

                            const overflowX = style.overflowX;
                            const scrollableX =
                                overflowX === 'auto' ||
                                overflowX === 'scroll' ||
                                overflowX === 'overlay';
                            return scrollableX && el.scrollWidth > el.clientWidth + 2;
                        }

                        function canScrollAny(el) {
                            return canScroll(el, 'y') || canScroll(el, 'x');
                        }

                        function pickScrollable(startEl) {
                            let el = startEl;

                            while (el && el !== document.body && el !== document.documentElement) {
                                if (canScrollAny(el)) return el;
                                el = el.parentElement;
                            }

                            return document.scrollingElement || document.documentElement || document.body;
                        }

                        function toState(target, isDoc) {
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
                        }

                        try {
                            const hitEl = document.elementFromPoint(x, y);
                            const target = pickScrollable(hitEl);
                            const docTarget = document.scrollingElement || document.documentElement || document.body;
                            return toState(target, target === docTarget);
                        } catch {
                            const target = document.scrollingElement || document.documentElement || document.body;
                            return toState(target, true);
                        }
                    }", new { x = hitX, y = hitY });

                return result ?? new ScrollTargetState();
            }
            catch
            {
                return new ScrollTargetState();
            }
        }

        private static async Task<ScrollTargetState> GetDocumentScrollTargetStateAsync(IPage page)
        {
            if (page == null || page.IsClosed)
                return new ScrollTargetState();

            try
            {
                var result = await page.EvaluateAsync<ScrollTargetState>(@"
                    () => {
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
                    }");

                return result ?? new ScrollTargetState();
            }
            catch
            {
                return new ScrollTargetState();
            }
        }

        private static async Task<bool> CanSafelySwipeDirectionOnTargetAsync(
            IPage page,
            HumanSwipeDirection direction,
            double hitX,
            double hitY)
        {
            if (page == null || page.IsClosed)
                return false;

            try
            {
                var state = await GetScrollTargetStateAsync(page, hitX, hitY);

                if (!CanStateScrollDirection(state, direction))
                {
                    state = await GetDocumentScrollTargetStateAsync(page);

                    if (!CanStateScrollDirection(state, direction))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool CanStateScrollDirection(ScrollTargetState state, HumanSwipeDirection direction)
        {
            if (state == null)
                return false;

            switch (direction)
            {
                case HumanSwipeDirection.Up:
                    return state.CanScrollVertically && !state.IsNearBottom;

                case HumanSwipeDirection.Down:
                    return state.CanScrollVertically && !state.IsNearTop;

                case HumanSwipeDirection.Left:
                    return state.CanScrollHorizontally && !state.IsNearRight;

                case HumanSwipeDirection.Right:
                    return state.CanScrollHorizontally && !state.IsNearLeft;

                default:
                    return false;
            }
        }

        private static async Task<bool> DidScrollTargetAsync(
            IPage page,
            ScrollTargetState before,
            double hitX,
            double hitY,
            HumanSwipeDirection direction,
            double minDelta)
        {
            if (page == null || page.IsClosed || before == null)
                return false;

            try
            {
                // 不再用 data-BDAd-swipe-id 绑定元素，避免污染 DOM。
                // 这里重新从相同触点位置获取可滚动容器；如果页面布局变化导致命中不同容器，则回退到 document。
                var after = await GetScrollTargetStateAsync(page, hitX, hitY);

                double delta = IsHorizontalDirection(direction)
                    ? Math.Abs(after.ScrollLeft - before.ScrollLeft)
                    : Math.Abs(after.ScrollTop - before.ScrollTop);

                if (delta >= minDelta)
                    return true;

                var docAfter = await GetDocumentScrollTargetStateAsync(page);
                double docDelta = IsHorizontalDirection(direction)
                    ? Math.Abs(docAfter.ScrollLeft - before.ScrollLeft)
                    : Math.Abs(docAfter.ScrollTop - before.ScrollTop);

                return docDelta >= minDelta;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 路径生成

        private static (int startX, int startY, int endX, int endY) BuildViewportSwipePath(
            int viewportWidth,
            int viewportHeight,
            HumanSwipeDirection direction,
            HumanSwipeOptions options)
        {
            int safe = Math.Max(0, options.SafeMargin);

            double distanceRate = options.Mode switch
            {
                HumanSwipeMode.Micro => RandomRangeDouble(0.08, 0.20),
                HumanSwipeMode.Reading => RandomRangeDouble(0.18, 0.30),
                HumanSwipeMode.Preview => RandomRangeDouble(0.34, 0.52),
                HumanSwipeMode.Fling => RandomRangeDouble(0.58, 0.78),
                _ => RandomRangeDouble(0.34, 0.52)
            };

            int centerX = RandomRange(
                (int)(viewportWidth * 0.40),
                (int)(viewportWidth * 0.60));

            int centerY = RandomRange(
                (int)(viewportHeight * 0.36),
                (int)(viewportHeight * 0.64));

            int verticalDistance = options.DistancePx ?? (int)(viewportHeight * distanceRate);
            int horizontalDistance = options.DistancePx ?? (int)(viewportWidth * distanceRate);

            int startX;
            int startY;
            int endX;
            int endY;

            switch (direction)
            {
                case HumanSwipeDirection.Up:
                    startX = centerX;
                    startY = RandomRange(
                        Math.Max(safe, (int)(viewportHeight * 0.58)),
                        Math.Min(viewportHeight - safe, (int)(viewportHeight * 0.88)));

                    endX = startX + RandomRange(-options.CrossAxisJitter, options.CrossAxisJitter);
                    endY = Math.Max(safe, startY - verticalDistance);
                    break;

                case HumanSwipeDirection.Down:
                    startX = centerX;
                    startY = RandomRange(
                        Math.Max(safe, (int)(viewportHeight * 0.16)),
                        Math.Min(viewportHeight - safe, (int)(viewportHeight * 0.46)));

                    endX = startX + RandomRange(-options.CrossAxisJitter, options.CrossAxisJitter);
                    endY = Math.Min(viewportHeight - safe, startY + verticalDistance);
                    break;

                case HumanSwipeDirection.Left:
                    startX = RandomRange(
                        Math.Max(safe, (int)(viewportWidth * 0.66)),
                        Math.Min(viewportWidth - safe, (int)(viewportWidth * 0.90)));

                    startY = centerY;
                    endX = Math.Max(safe, startX - horizontalDistance);
                    endY = startY + RandomRange(-options.CrossAxisJitter, options.CrossAxisJitter);
                    break;

                case HumanSwipeDirection.Right:
                    startX = RandomRange(
                        Math.Max(safe, (int)(viewportWidth * 0.10)),
                        Math.Min(viewportWidth - safe, (int)(viewportWidth * 0.34)));

                    startY = centerY;
                    endX = Math.Min(viewportWidth - safe, startX + horizontalDistance);
                    endY = startY + RandomRange(-options.CrossAxisJitter, options.CrossAxisJitter);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(direction));
            }

            startX = options.StartX ?? Clamp(startX, safe, viewportWidth - safe);
            startY = options.StartY ?? Clamp(startY, safe, viewportHeight - safe);
            endX = options.EndX ?? Clamp(endX, safe, viewportWidth - safe);
            endY = options.EndY ?? Clamp(endY, safe, viewportHeight - safe);

            return (startX, startY, endX, endY);
        }

        private static (int startX, int startY, int endX, int endY) BuildElementSwipePath(
            ElementRect rect,
            int viewportWidth,
            int viewportHeight,
            HumanSwipeDirection direction,
            HumanSwipeOptions options)
        {
            double left = Clamp(rect.Left, 0, viewportWidth);
            double right = Clamp(rect.Right, 0, viewportWidth);
            double top = Clamp(rect.Top, 0, viewportHeight);
            double bottom = Clamp(rect.Bottom, 0, viewportHeight);

            if (right - left < 10 || bottom - top < 10)
                throw new InvalidOperationException("Element rect is too small for swipe.");

            double width = right - left;
            double height = bottom - top;

            double verticalDistanceRate = options.Mode switch
            {
                HumanSwipeMode.Micro => RandomRangeDouble(0.08, 0.18),
                HumanSwipeMode.Reading => RandomRangeDouble(0.18, 0.30),
                HumanSwipeMode.Preview => RandomRangeDouble(0.35, 0.52),
                HumanSwipeMode.Fling => RandomRangeDouble(0.55, 0.78),
                _ => RandomRangeDouble(0.35, 0.52)
            };

            double horizontalDistanceRate = options.Mode switch
            {
                HumanSwipeMode.Micro => RandomRangeDouble(0.12, 0.22),
                HumanSwipeMode.Reading => RandomRangeDouble(0.22, 0.36),
                HumanSwipeMode.Preview => RandomRangeDouble(0.48, 0.68),
                HumanSwipeMode.Fling => RandomRangeDouble(0.65, 0.86),
                _ => RandomRangeDouble(0.48, 0.68)
            };

            double startX;
            double startY;
            double endX;
            double endY;

            switch (direction)
            {
                case HumanSwipeDirection.Up:
                    startX = left + width * RandomRangeDouble(0.42, 0.58);
                    startY = top + height * RandomRangeDouble(0.68, 0.86);
                    endX = startX + RandomRangeDouble(-8, 8);
                    endY = Math.Max(top + 6, startY - (options.DistancePx ?? (int)(height * verticalDistanceRate)));
                    break;

                case HumanSwipeDirection.Down:
                    startX = left + width * RandomRangeDouble(0.42, 0.58);
                    startY = top + height * RandomRangeDouble(0.16, 0.34);
                    endX = startX + RandomRangeDouble(-8, 8);
                    endY = Math.Min(bottom - 6, startY + (options.DistancePx ?? (int)(height * verticalDistanceRate)));
                    break;

                case HumanSwipeDirection.Left:
                    startX = left + width * RandomRangeDouble(0.72, 0.88);
                    startY = top + height * RandomRangeDouble(0.42, 0.62);
                    endX = Math.Max(left + 6, startX - (options.DistancePx ?? (int)(width * horizontalDistanceRate)));
                    endY = startY + RandomRangeDouble(-6, 6);
                    break;

                case HumanSwipeDirection.Right:
                    startX = left + width * RandomRangeDouble(0.12, 0.28);
                    startY = top + height * RandomRangeDouble(0.42, 0.62);
                    endX = Math.Min(right - 6, startX + (options.DistancePx ?? (int)(width * horizontalDistanceRate)));
                    endY = startY + RandomRangeDouble(-6, 6);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(direction));
            }

            startX = options.StartX ?? Clamp(startX, 0, viewportWidth);
            startY = options.StartY ?? Clamp(startY, 0, viewportHeight);
            endX = options.EndX ?? Clamp(endX, 0, viewportWidth);
            endY = options.EndY ?? Clamp(endY, 0, viewportHeight);

            return (
                (int)Math.Round(startX),
                (int)Math.Round(startY),
                (int)Math.Round(endX),
                (int)Math.Round(endY));
        }

        #endregion

        #region 轨迹和事件派发

        private static async Task<(int steps, int totalDelayMs, List<HumanSwipeTracePoint> points)> RunSwipePathAsync(
            ICDPSession cdp,
            int startX,
            int startY,
            int endX,
            int endY,
            HumanSwipeDirection direction,
            HumanSwipeOptions options,
            CancellationToken cancellationToken)
        {
            double speed = Math.Clamp(options.SpeedFactor, 0.25, 3.0);

            int steps = options.Steps ?? GetDefaultSteps(options.Mode, speed);
            steps = Math.Max(6, steps);

            bool holdBeforeEnd = options.HoldBeforeEnd ?? (options.Mode == HumanSwipeMode.Reading || options.Mode == HumanSwipeMode.Micro);

            options.Log?.Invoke(
                $"Swipe {direction}/{options.Mode}: ({startX},{startY}) -> ({endX},{endY}), steps={steps}, speed={speed:0.00}");

            var dynamics = BuildGestureDynamics(startX, startY, endX, endY, direction, options);
            var points = BuildGesturePoints(startX, startY, endX, endY, direction, options.Mode, options.UseBezierCurve, steps);
            var samples = BuildTouchSamples(points, direction, options, dynamics);

            var tracePoints = new List<HumanSwipeTracePoint>(samples.Count + 2);
            foreach (var s in samples)
            {
                tracePoints.Add(new HumanSwipeTracePoint
                {
                    X = s.Point.X,
                    Y = s.Point.Y,
                    DelayMs = s.DelayMs,
                    RadiusX = s.RadiusX,
                    RadiusY = s.RadiusY,
                    Force = s.Force,
                    RotationAngle = s.RotationAngle
                });
            }

            int totalDelay = 0;
            bool touchStarted = false;

            try
            {
                await DispatchTouchAsync(cdp, "touchStart", samples[0]);
                touchStarted = true;

                if (options.HoldBeforeMove)
                {
                    int startHold = GetStartHoldDelay(options.Mode, speed);
                    await Task.Delay(startHold, cancellationToken);
                    totalDelay += startHold;
                }

                PointD lastPoint = samples[0].Point;
                TouchSample lastDispatched = samples[0];

                for (int i = 1; i < samples.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var current = LimitExtremeStep(lastPoint, samples[i].Point, options.Mode);
                    lastPoint = current;

                    var sample = new TouchSample(
                        current,
                        samples[i].DelayMs,
                        samples[i].RadiusX,
                        samples[i].RadiusY,
                        samples[i].Force,
                        samples[i].RotationAngle);

                    tracePoints[i] = new HumanSwipeTracePoint
                    {
                        X = current.X,
                        Y = current.Y,
                        DelayMs = samples[i].DelayMs,
                        RadiusX = samples[i].RadiusX,
                        RadiusY = samples[i].RadiusY,
                        Force = samples[i].Force,
                        RotationAngle = samples[i].RotationAngle
                    };

                    await DispatchTouchAsync(cdp, "touchMove", sample);
                    lastDispatched = sample;

                    int delay = ScaleDelay(sample.DelayMs, speed);

                    if (ShouldHesitate(options.Mode, options))
                        delay += RandomRange(options.MinHesitationMs, options.MaxHesitationMs);

                    await Task.Delay(delay, cancellationToken);
                    totalDelay += delay;
                }

                if (options.Mode != HumanSwipeMode.Fling)
                {
                    var endSample = BuildTerminalSample(new PointD(endX, endY), 1.0, options, dynamics);

                    await DispatchTouchAsync(cdp, "touchMove", endSample);
                    lastDispatched = endSample;

                    tracePoints.Add(new HumanSwipeTracePoint
                    {
                        X = endX,
                        Y = endY,
                        DelayMs = 0,
                        RadiusX = endSample.RadiusX,
                        RadiusY = endSample.RadiusY,
                        Force = endSample.Force,
                        RotationAngle = endSample.RotationAngle
                    });
                }

                if (ShouldEndPullBack(options.Mode, options))
                {
                    int pullBackPx = RandomRange(
                        Math.Max(1, options.MinPullBackPx),
                        Math.Max(options.MinPullBackPx, options.MaxPullBackPx));

                    var pullBackPoint = BuildPullBackPoint(lastDispatched.Point, direction, pullBackPx);
                    var pullBackSample = BuildTerminalSample(pullBackPoint, 0.96, options, dynamics);

                    await DispatchTouchAsync(cdp, "touchMove", pullBackSample);

                    int pullDelay = ScaleDelay(RandomRange(18, 55), speed);
                    await Task.Delay(pullDelay, cancellationToken);
                    totalDelay += pullDelay;

                    tracePoints.Add(new HumanSwipeTracePoint
                    {
                        X = pullBackSample.Point.X,
                        Y = pullBackSample.Point.Y,
                        DelayMs = pullDelay,
                        RadiusX = pullBackSample.RadiusX,
                        RadiusY = pullBackSample.RadiusY,
                        Force = pullBackSample.Force,
                        RotationAngle = pullBackSample.RotationAngle
                    });
                }

                if (holdBeforeEnd)
                {
                    int endHold = GetEndHoldDelay(options.Mode, speed);
                    await Task.Delay(endHold, cancellationToken);
                    totalDelay += endHold;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            finally
            {
                if (touchStarted)
                {
                    try
                    {
                        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
                        {
                            ["type"] = "touchEnd",
                            ["touchPoints"] = Array.Empty<object>(),
                            ["modifiers"] = 0
                        });
                    }
                    catch
                    {
                    }
                }
            }

            return (steps, totalDelay, tracePoints);
        }

        private static List<PointD> BuildGesturePoints(
            int startX,
            int startY,
            int endX,
            int endY,
            HumanSwipeDirection direction,
            HumanSwipeMode mode,
            bool useBezier,
            int steps)
        {
            var points = new List<PointD>(steps + 1);

            var control = BuildBezierControlPoints(startX, startY, endX, endY, mode);

            for (int i = 0; i <= steps; i++)
            {
                double rawT = (double)i / steps;
                double motionT = ApplyMotionCurve(rawT, mode);

                PointD p = useBezier
                    ? CubicBezier(control.p0, control.p1, control.p2, control.p3, motionT)
                    : new PointD(Lerp(startX, endX, motionT), Lerp(startY, endY, motionT));

                points.Add(p);
            }

            return points;
        }

        private static List<TouchSample> BuildTouchSamples(
            IReadOnlyList<PointD> points,
            HumanSwipeDirection direction,
            HumanSwipeOptions options,
            GestureDynamics dynamics)
        {
            var samples = new List<TouchSample>(points.Count);

            for (int i = 0; i < points.Count; i++)
            {
                double progress = points.Count <= 1 ? 1 : i / (double)(points.Count - 1);

                var p = points[i];

                if (options.EnableCrossAxisDrift)
                    p = ApplyCrossAxisDrift(p, progress, direction, dynamics);

                if (options.UseJitter)
                {
                    p = options.UseSmoothJitter
                        ? ApplySmoothHumanJitter(p, progress, direction, options.Mode, options.MaxJitter, dynamics)
                        : ApplyHumanJitter(p, progress, direction, options.Mode, options.MaxJitter);
                }

                int delay = i == 0 ? 0 : GetMoveDelay(progress, options.Mode);

                if (i > 0 && RandomChance(options.Mode == HumanSwipeMode.Micro ? 0.03 : 0.05))
                    delay += RandomRange(6, 18);

                double force = options.EnableForceCurve
                    ? GetHumanForceCurve(progress, options.Mode, dynamics)
                    : GetHumanForce(progress, options.Mode);

                var radius = options.EnableTouchAreaCurve
                    ? GetTouchRadiusCurve(progress, options.Mode, force, dynamics)
                    : (RandomRangeDouble(2.4, 5.8), RandomRangeDouble(2.4, 5.8));

                samples.Add(new TouchSample(
                    p,
                    delay,
                    radius.Item1,
                    radius.Item2,
                    force,
                    GetRotationAngle(progress, direction, options.Mode)));
            }

            return samples;
        }


        private static GestureDynamics BuildGestureDynamics(
            int startX,
            int startY,
            int endX,
            int endY,
            HumanSwipeDirection direction,
            HumanSwipeOptions options)
        {
            double dx = endX - startX;
            double dy = endY - startY;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            double autoDrift = options.Mode switch
            {
                HumanSwipeMode.Micro => Math.Min(3.5, distance * 0.012),
                HumanSwipeMode.Reading => Math.Min(6.0, distance * 0.018),
                HumanSwipeMode.Preview => Math.Min(9.0, distance * 0.026),
                HumanSwipeMode.Fling => Math.Min(13.0, distance * 0.034),
                _ => Math.Min(8.0, distance * 0.024)
            };

            return new GestureDynamics
            {
                DriftPeak = Math.Max(0, options.MaxCrossAxisDriftPx ?? autoDrift),
                DriftSide = RandomBool() ? 1.0 : -1.0,
                DriftPhase = RandomRangeDouble(-0.18, 0.18),
                SmoothJitterX = 0,
                SmoothJitterY = 0,
                ForceNoise = RandomRangeDouble(-0.025, 0.025),
                RadiusNoiseX = RandomRangeDouble(-0.12, 0.12),
                RadiusNoiseY = RandomRangeDouble(-0.12, 0.12)
            };
        }

        private static PointD ApplyCrossAxisDrift(
            PointD p,
            double t,
            HumanSwipeDirection direction,
            GestureDynamics dynamics)
        {
            t = Clamp01(t);

            // 平滑漂移：开始和结束弱，中段明显；末尾稍微回正。
            double wave = Math.Sin(Math.PI * Clamp01(t + dynamics.DriftPhase));
            double returnBias = 1.0 - 0.25 * SmoothStep(0.72, 1.0, t);
            double drift = dynamics.DriftPeak * wave * returnBias * dynamics.DriftSide;

            if (direction == HumanSwipeDirection.Up || direction == HumanSwipeDirection.Down)
                return new PointD(p.X + drift, p.Y);

            return new PointD(p.X, p.Y + drift);
        }

        private static PointD ApplySmoothHumanJitter(
            PointD p,
            double t,
            HumanSwipeDirection direction,
            HumanSwipeMode mode,
            double maxJitter,
            GestureDynamics dynamics)
        {
            double baseJitter = mode switch
            {
                HumanSwipeMode.Micro => maxJitter * 0.35,
                HumanSwipeMode.Reading => maxJitter * 0.55,
                HumanSwipeMode.Preview => maxJitter * 0.85,
                HumanSwipeMode.Fling => maxJitter * 1.15,
                _ => maxJitter
            };

            double envelope = Math.Sin(Math.PI * Clamp01(t));
            double jitter = baseJitter * envelope;

            // 带惯性的随机游走，避免每个点独立随机造成锯齿。
            dynamics.SmoothJitterX = dynamics.SmoothJitterX * 0.72 + RandomRangeDouble(-jitter, jitter) * 0.28;
            dynamics.SmoothJitterY = dynamics.SmoothJitterY * 0.72 + RandomRangeDouble(-jitter, jitter) * 0.28;

            double xJitter = dynamics.SmoothJitterX;
            double yJitter = dynamics.SmoothJitterY;

            if (direction == HumanSwipeDirection.Up || direction == HumanSwipeDirection.Down)
                yJitter *= 0.45;
            else
                xJitter *= 0.45;

            return new PointD(p.X + xJitter, p.Y + yJitter);
        }

        private static double GetHumanForceCurve(
            double progress,
            HumanSwipeMode mode,
            GestureDynamics dynamics)
        {
            progress = Clamp01(progress);

            double min;
            double max;

            switch (mode)
            {
                case HumanSwipeMode.Micro:
                    min = 0.56;
                    max = 0.86;
                    break;

                case HumanSwipeMode.Reading:
                    min = 0.62;
                    max = 0.92;
                    break;

                case HumanSwipeMode.Fling:
                    min = 0.66;
                    max = 0.98;
                    break;

                default:
                    min = 0.64;
                    max = 0.95;
                    break;
            }

            // 压力曲线：按下后逐渐变重，中段稳定，抬手前减轻。
            double pressIn = SmoothStep(0.00, 0.18, progress);
            double liftOut = 1.0 - SmoothStep(0.76, 1.00, progress);
            double body = Math.Min(pressIn, liftOut);

            // 小幅连续噪声。
            dynamics.ForceNoise = dynamics.ForceNoise * 0.82 + RandomRangeDouble(-0.025, 0.025) * 0.18;

            double force = min + (max - min) * body + dynamics.ForceNoise;
            return Math.Clamp(force, 0.45, 1.0);
        }

        private static (double, double) GetTouchRadiusCurve(
            double progress,
            HumanSwipeMode mode,
            double force,
            GestureDynamics dynamics)
        {
            progress = Clamp01(progress);

            double baseRadius = mode switch
            {
                HumanSwipeMode.Micro => RandomRangeDouble(2.7, 3.6),
                HumanSwipeMode.Reading => RandomRangeDouble(3.1, 4.2),
                HumanSwipeMode.Preview => RandomRangeDouble(3.0, 4.6),
                HumanSwipeMode.Fling => RandomRangeDouble(2.8, 4.4),
                _ => RandomRangeDouble(3.0, 4.4)
            };

            // 面积与压力轻微相关：压力越大，半径略大；抬手前自然缩小。
            double forceEffect = (force - 0.65) * 1.15;
            double liftShrink = 1.0 - 0.22 * SmoothStep(0.78, 1.0, progress);

            dynamics.RadiusNoiseX = dynamics.RadiusNoiseX * 0.78 + RandomRangeDouble(-0.10, 0.10) * 0.22;
            dynamics.RadiusNoiseY = dynamics.RadiusNoiseY * 0.78 + RandomRangeDouble(-0.10, 0.10) * 0.22;

            double rx = (baseRadius + forceEffect + dynamics.RadiusNoiseX) * liftShrink;
            double ry = (baseRadius + forceEffect + dynamics.RadiusNoiseY) * liftShrink * RandomRangeDouble(0.88, 1.12);

            return (
                Math.Clamp(rx, 2.2, 6.2),
                Math.Clamp(ry, 2.2, 6.2));
        }

        private static TouchSample BuildTerminalSample(
            PointD point,
            double progress,
            HumanSwipeOptions options,
            GestureDynamics dynamics)
        {
            double force = options.EnableForceCurve
                ? GetHumanForceCurve(progress, options.Mode, dynamics)
                : RandomRangeDouble(0.55, 0.90);

            var radius = options.EnableTouchAreaCurve
                ? GetTouchRadiusCurve(progress, options.Mode, force, dynamics)
                : (RandomRangeDouble(2.4, 5.8), RandomRangeDouble(2.4, 5.8));

            return new TouchSample(
                point,
                0,
                radius.Item1,
                radius.Item2,
                force,
                RandomRangeDouble(0, 90));
        }

        private static PointD BuildPullBackPoint(
            PointD point,
            HumanSwipeDirection direction,
            int pullBackPx)
        {
            return direction switch
            {
                HumanSwipeDirection.Up => new PointD(point.X + RandomRangeDouble(-1.5, 1.5), point.Y + pullBackPx),
                HumanSwipeDirection.Down => new PointD(point.X + RandomRangeDouble(-1.5, 1.5), point.Y - pullBackPx),
                HumanSwipeDirection.Left => new PointD(point.X + pullBackPx, point.Y + RandomRangeDouble(-1.5, 1.5)),
                HumanSwipeDirection.Right => new PointD(point.X - pullBackPx, point.Y + RandomRangeDouble(-1.5, 1.5)),
                _ => point
            };
        }

        private static bool ShouldHesitate(HumanSwipeMode mode, HumanSwipeOptions options)
        {
            if (!options.EnableHesitationPause || mode == HumanSwipeMode.Fling)
                return false;

            double chance = options.HesitationChance ?? mode switch
            {
                HumanSwipeMode.Reading => 0.055,
                HumanSwipeMode.Micro => 0.040,
                HumanSwipeMode.Preview => 0.018,
                _ => 0.015
            };

            return RandomChance(chance);
        }

        private static bool ShouldEndPullBack(HumanSwipeMode mode, HumanSwipeOptions options)
        {
            if (!options.EnableEndPullBack || mode == HumanSwipeMode.Fling)
                return false;

            double chance = options.EndPullBackChance ?? mode switch
            {
                HumanSwipeMode.Micro => 0.32,
                HumanSwipeMode.Reading => 0.22,
                HumanSwipeMode.Preview => 0.08,
                _ => 0.05
            };

            return RandomChance(chance);
        }

        private static double GetRotationAngle(
            double progress,
            HumanSwipeDirection direction,
            HumanSwipeMode mode)
        {
            double baseAngle = IsHorizontalDirection(direction)
                ? RandomRangeDouble(8, 38)
                : RandomRangeDouble(18, 68);

            double modeOffset = mode == HumanSwipeMode.Fling
                ? RandomRangeDouble(-8, 8)
                : RandomRangeDouble(-4, 4);

            return Math.Clamp(baseAngle + modeOffset + Math.Sin(progress * Math.PI) * RandomRangeDouble(-3, 3), 0, 90);
        }

        private static Task DispatchTouchAsync(ICDPSession cdp, string type, TouchSample sample)
        {
            return cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = type,
                ["touchPoints"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["x"] = Math.Round(sample.Point.X, 2),
                        ["y"] = Math.Round(sample.Point.Y, 2),
                        ["radiusX"] = Math.Round(sample.RadiusX, 2),
                        ["radiusY"] = Math.Round(sample.RadiusY, 2),
                        ["force"] = Math.Round(sample.Force, 3),
                        ["rotationAngle"] = Math.Round(sample.RotationAngle, 2),
                        ["id"] = 0
                    }
                },
                ["modifiers"] = 0
            });
        }

        private static (
            PointD p0,
            PointD p1,
            PointD p2,
            PointD p3) BuildBezierControlPoints(
            int startX,
            int startY,
            int endX,
            int endY,
            HumanSwipeMode mode)
        {
            PointD p0 = new(startX, startY);
            PointD p3 = new(endX, endY);

            double dx = endX - startX;
            double dy = endY - startY;

            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 1)
                distance = 1;

            double nx = -dy / distance;
            double ny = dx / distance;

            double curveStrength = mode switch
            {
                HumanSwipeMode.Micro => RandomRangeDouble(0.006, 0.018),
                HumanSwipeMode.Reading => RandomRangeDouble(0.012, 0.035),
                HumanSwipeMode.Preview => RandomRangeDouble(0.025, 0.065),
                HumanSwipeMode.Fling => RandomRangeDouble(0.035, 0.09),
                _ => RandomRangeDouble(0.025, 0.065)
            };

            double side = RandomBool() ? 1.0 : -1.0;
            double offset = distance * curveStrength * side;

            double p1Rate = RandomRangeDouble(0.22, 0.38);
            double p2Rate = RandomRangeDouble(0.62, 0.82);

            PointD p1 = new(
                startX + dx * p1Rate + nx * offset,
                startY + dy * p1Rate + ny * offset);

            PointD p2 = new(
                startX + dx * p2Rate - nx * offset * RandomRangeDouble(0.45, 0.95),
                startY + dy * p2Rate - ny * offset * RandomRangeDouble(0.45, 0.95));

            return (p0, p1, p2, p3);
        }

        #endregion

        #region 曲线/延迟/力度

        private static int GetDefaultSteps(HumanSwipeMode mode, double speedFactor)
        {
            int min;
            int max;

            switch (mode)
            {
                case HumanSwipeMode.Micro:
                    min = 16;
                    max = 34;
                    break;

                case HumanSwipeMode.Reading:
                    min = 42;
                    max = 68;
                    break;

                case HumanSwipeMode.Preview:
                    min = 24;
                    max = 40;
                    break;

                case HumanSwipeMode.Fling:
                    min = 10;
                    max = 18;
                    break;

                default:
                    min = 24;
                    max = 36;
                    break;
            }

            int baseSteps = RandomRange(min, max);

            if (speedFactor > 1.0)
                baseSteps = (int)Math.Round(baseSteps / Math.Sqrt(speedFactor));

            if (speedFactor < 1.0)
                baseSteps = (int)Math.Round(baseSteps * (1.0 / speedFactor));

            return Math.Max(6, baseSteps);
        }

        private static int GetStartHoldDelay(HumanSwipeMode mode, double speedFactor)
        {
            int delay = mode switch
            {
                HumanSwipeMode.Micro => RandomRange(18, 55),
                HumanSwipeMode.Reading => RandomRange(55, 120),
                HumanSwipeMode.Preview => RandomRange(25, 65),
                HumanSwipeMode.Fling => RandomRange(15, 45),
                _ => RandomRange(25, 65)
            };

            return ScaleDelay(delay, speedFactor);
        }

        private static int GetEndHoldDelay(HumanSwipeMode mode, double speedFactor)
        {
            int delay = mode switch
            {
                HumanSwipeMode.Micro => RandomRange(8, 35),
                HumanSwipeMode.Reading => RandomRange(55, 110),
                HumanSwipeMode.Preview => RandomRange(8, 25),
                HumanSwipeMode.Fling => 0,
                _ => RandomRange(8, 25)
            };

            return ScaleDelay(delay, speedFactor);
        }

        private static int GetMoveDelay(double t, HumanSwipeMode mode)
        {
            switch (mode)
            {
                case HumanSwipeMode.Micro:
                    if (t < 0.08) return RandomRange(10, 20);
                    if (t < 0.22) return RandomRange(7, 14);
                    if (t < 0.72) return RandomRange(5, 10);
                    if (t < 0.90) return RandomRange(7, 15);
                    return RandomRange(11, 22);

                case HumanSwipeMode.Reading:
                    int delay = RandomRange(16, 36);
                    if (t > 0.75)
                        delay += RandomRange(6, 18);
                    return delay;

                case HumanSwipeMode.Preview:
                    return t < 0.70
                        ? RandomRange(9, 22)
                        : RandomRange(5, 12);

                case HumanSwipeMode.Fling:
                    if (t < 0.35) return RandomRange(9, 18);
                    if (t < 0.70) return RandomRange(5, 10);
                    return RandomRange(2, 5);

                default:
                    return RandomRange(9, 22);
            }
        }

        private static double GetHumanForce(double progress, HumanSwipeMode mode)
        {
            double baseForce;

            if (progress < 0.12)
            {
                baseForce = mode == HumanSwipeMode.Micro
                    ? RandomRangeDouble(0.65, 0.84)
                    : RandomRangeDouble(0.70, 0.90);
            }
            else if (progress < 0.80)
            {
                baseForce = mode == HumanSwipeMode.Micro
                    ? RandomRangeDouble(0.72, 0.92)
                    : RandomRangeDouble(0.78, 0.98);
            }
            else
            {
                baseForce = mode == HumanSwipeMode.Micro
                    ? RandomRangeDouble(0.58, 0.80)
                    : RandomRangeDouble(0.62, 0.86);
            }

            return Math.Clamp(baseForce, 0.45, 1.0);
        }

        private static double ApplyMotionCurve(double t, HumanSwipeMode mode)
        {
            t = Clamp01(t);

            return mode switch
            {
                HumanSwipeMode.Micro => EaseInOutCubic(t),
                HumanSwipeMode.Reading => EaseOutCubic(t),
                HumanSwipeMode.Preview => EaseInOutCubic(t),
                HumanSwipeMode.Fling => EaseInCubic(t),
                _ => t
            };
        }

        private static PointD ApplyHumanJitter(
            PointD p,
            double t,
            HumanSwipeDirection direction,
            HumanSwipeMode mode,
            double maxJitter)
        {
            double baseJitter = mode switch
            {
                HumanSwipeMode.Micro => maxJitter * 0.35,
                HumanSwipeMode.Reading => maxJitter * 0.55,
                HumanSwipeMode.Preview => maxJitter * 0.85,
                HumanSwipeMode.Fling => maxJitter * 1.15,
                _ => maxJitter
            };

            double envelope = Math.Sin(Math.PI * Clamp01(t));
            double jitter = baseJitter * envelope;

            double xJitter = RandomRangeDouble(-jitter, jitter);
            double yJitter = RandomRangeDouble(-jitter, jitter);

            if (direction == HumanSwipeDirection.Up || direction == HumanSwipeDirection.Down)
                yJitter *= 0.55;
            else
                xJitter *= 0.55;

            return new PointD(p.X + xJitter, p.Y + yJitter);
        }

        private static PointD LimitExtremeStep(PointD last, PointD current, HumanSwipeMode mode)
        {
            if (mode == HumanSwipeMode.Fling)
                return current;

            double maxStep = mode switch
            {
                HumanSwipeMode.Micro => 14.0,
                HumanSwipeMode.Reading => 18.0,
                HumanSwipeMode.Preview => 30.0,
                _ => 30.0
            };

            double dx = current.X - last.X;
            double dy = current.Y - last.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance <= maxStep)
                return current;

            double ratio = maxStep / distance;

            return new PointD(
                last.X + dx * ratio,
                last.Y + dy * ratio);
        }

        #endregion

        #region 基础工具

        private static HumanSwipeDirection PickDirection(HumanSwipeDirection direction)
        {
            switch (direction)
            {
                case HumanSwipeDirection.RandomVertical:
                    return RandomChance(0.88)
                        ? HumanSwipeDirection.Up
                        : HumanSwipeDirection.Down;

                case HumanSwipeDirection.RandomAny:
                    int n = RandomRange(0, 3);
                    return n switch
                    {
                        0 => HumanSwipeDirection.Up,
                        1 => HumanSwipeDirection.Down,
                        2 => HumanSwipeDirection.Left,
                        _ => HumanSwipeDirection.Right
                    };

                default:
                    return direction;
            }
        }

        private static bool IsHorizontalDirection(HumanSwipeDirection direction)
        {
            return direction == HumanSwipeDirection.Left || direction == HumanSwipeDirection.Right;
        }

        private static PointD CubicBezier(PointD p0, PointD p1, PointD p2, PointD p3, double t)
        {
            t = Clamp01(t);

            double u = 1.0 - t;
            double tt = t * t;
            double uu = u * u;
            double uuu = uu * u;
            double ttt = tt * t;

            double x =
                uuu * p0.X +
                3 * uu * t * p1.X +
                3 * u * tt * p2.X +
                ttt * p3.X;

            double y =
                uuu * p0.Y +
                3 * uu * t * p1.Y +
                3 * u * tt * p2.Y +
                ttt * p3.Y;

            return new PointD(x, y);
        }

        private static double EaseOutCubic(double t)
        {
            t = Clamp01(t);
            return 1.0 - Math.Pow(1.0 - t, 3.0);
        }

        private static double EaseInCubic(double t)
        {
            t = Clamp01(t);
            return t * t * t;
        }

        private static double EaseInOutCubic(double t)
        {
            t = Clamp01(t);

            return t < 0.5
                ? 4 * t * t * t
                : 1 - Math.Pow(-2 * t + 2, 3) / 2;
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * Clamp01(t);
        }

        private static int ScaleDelay(int delay, double speedFactor)
        {
            double speed = Math.Clamp(speedFactor, 0.25, 3.0);
            int scaled = (int)Math.Round(delay / speed);
            return Math.Max(1, scaled);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (max < min)
                return min;

            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
                return min;

            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        private static double Clamp01(double value)
        {
            if (value < 0)
                return 0;

            if (value > 1)
                return 1;

            return value;
        }

        private static double SmoothStep(double edge0, double edge1, double value)
        {
            if (Math.Abs(edge1 - edge0) < 0.000001)
                return value >= edge1 ? 1.0 : 0.0;

            double t = Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3.0 - 2.0 * t);
        }

        private static int RandomRange(int min, int max)
        {
            if (max < min)
                (min, max) = (max, min);

            return RandomLocal.Value!.Next(min, max + 1);
        }

        private static double RandomRangeDouble(double min, double max)
        {
            if (max < min)
                (min, max) = (max, min);

            return min + RandomLocal.Value!.NextDouble() * (max - min);
        }

        private static bool RandomBool()
        {
            return RandomLocal.Value!.Next(0, 2) == 0;
        }

        private static bool RandomChance(double chance)
        {
            if (chance <= 0)
                return false;

            if (chance >= 1)
                return true;

            return RandomLocal.Value!.NextDouble() < chance;
        }

        #endregion
        private static void ApplyStyle(HumanSwipeOptions options)
        {
            var profile = options.StyleProfile ?? StyleScope.Value;
            if (profile == null) return;

            var rnd = new Random(unchecked(profile.Seed ^ Environment.TickCount));
            double f(double min, double max) => min + (max - min) * rnd.NextDouble();
            double clamp01(double x) => Math.Clamp(x, 0.0, 1.0);

            options.SpeedFactor = Math.Clamp(options.SpeedFactor * profile.SpeedBias * f(0.95, 1.05), 0.35, 3.2);
            if (options.DistancePx.HasValue)
                options.DistancePx = Math.Max(8, (int)Math.Round(options.DistancePx.Value * profile.DistanceBias * f(0.96, 1.04)));

            options.UseBezierCurve = options.UseBezierCurve && profile.CurveBias >= 0.85;
            options.MaxJitter = Math.Clamp(options.MaxJitter * profile.JitterBias * f(0.9, 1.1), 0.1, 6.0);
            options.CrossAxisJitter = Math.Max(0, (int)Math.Round(options.CrossAxisJitter * profile.DriftBias * f(0.9, 1.1)));

            if (options.MaxCrossAxisDriftPx.HasValue)
                options.MaxCrossAxisDriftPx = Math.Clamp(options.MaxCrossAxisDriftPx.Value * profile.DriftBias * f(0.92, 1.08), 0.0, 80.0);

            options.EnableForceCurve = options.EnableForceCurve && profile.ForceBias >= 0.80;
            options.EnableTouchAreaCurve = options.EnableTouchAreaCurve && profile.TouchAreaBias >= 0.80;

            if (options.HesitationChance.HasValue)
                options.HesitationChance = clamp01(options.HesitationChance.Value * profile.HesitationBias * f(0.92, 1.08));
            options.MinHesitationMs = Math.Max(0, (int)Math.Round(options.MinHesitationMs * profile.PauseBias * f(0.95, 1.05)));
            options.MaxHesitationMs = Math.Max(options.MinHesitationMs, (int)Math.Round(options.MaxHesitationMs * profile.PauseBias * f(0.95, 1.05)));

            if (options.EndPullBackChance.HasValue)
                options.EndPullBackChance = clamp01(options.EndPullBackChance.Value * profile.PullBackBias * f(0.92, 1.08));
            options.MinPullBackPx = Math.Max(0, (int)Math.Round(options.MinPullBackPx * profile.PullBackBias * f(0.95, 1.05)));
            options.MaxPullBackPx = Math.Max(options.MinPullBackPx, (int)Math.Round(options.MaxPullBackPx * profile.PullBackBias * f(0.95, 1.05)));

            options.MinVisualConfirmMs = Math.Max(0, (int)Math.Round(options.MinVisualConfirmMs * profile.PauseBias * f(0.95, 1.05)));
            options.MaxVisualConfirmMs = Math.Max(options.MinVisualConfirmMs, (int)Math.Round(options.MaxVisualConfirmMs * profile.PauseBias * f(0.95, 1.05)));
            options.NearTargetExtraPauseMs = Math.Max(0, (int)Math.Round(options.NearTargetExtraPauseMs * profile.PauseBias * f(0.95, 1.05)));

            options.HoldBeforeMove = profile.StartHoldBias >= 0.75 ? options.HoldBeforeMove : false;
            if (options.HoldBeforeEnd.HasValue)
            {
                options.HoldBeforeEnd = profile.EndHoldBias >= 0.75 ? options.HoldBeforeEnd : false;
            }

        }

        private sealed class Scope : IDisposable
        {
            private readonly Action _onDispose;
            public Scope(Action onDispose) => _onDispose = onDispose;
            public void Dispose() => _onDispose();
        }

    }
}
