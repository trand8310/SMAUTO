using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput
{
    public sealed class HumanTouchEngine
    {
        private readonly GesturePlanner _planner;
        private readonly KinematicsEngine _kinematics;
        private readonly BiomechanicsModel _biomechanics;
        private readonly CdpTouchDispatcher _dispatcher;
        private readonly ScrollTargetResolver _scrollResolver;

        public HumanTouchEngine(
            HumanTouchSession? session = null,
            GesturePlanner? planner = null,
            KinematicsEngine? kinematics = null,
            BiomechanicsModel? biomechanics = null,
            CdpTouchDispatcher? dispatcher = null,
            ScrollTargetResolver? scrollResolver = null)
        {
            Session = session ?? new HumanTouchSession();
            _planner = planner ?? new GesturePlanner();
            _kinematics = kinematics ?? new KinematicsEngine();
            _biomechanics = biomechanics ?? new BiomechanicsModel();
            _dispatcher = dispatcher ?? new CdpTouchDispatcher();
            _scrollResolver = scrollResolver ?? new ScrollTargetResolver();
        }

        public HumanTouchSession Session { get; }

        public async Task<HumanSwipeTrace?> SwipeAsync(
            IPage page,
            ICDPSession cdp,
            HumanTouchRequest? request = null,
            CancellationToken cancellationToken = default)
        {
            request ??= new HumanTouchRequest();
            Validate(page, cdp);
            var viewport = await GetEffectiveViewportAsync(page);
            if (viewport.Width <= 0 || viewport.Height <= 0) return null;

            await _dispatcher.EnableAsync(page, cdp, Session.DeviceProfile);

            GesturePlan? plan = null;
            ScrollTargetState? before = null;
            ScrollTargetState? docBefore = null;

            // 失败重选起点，避免触点落在不可滚动的内部控件/边界。
            for (int attempt = 0; attempt < 8; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                plan = _planner.Plan(Session, viewport.Width, viewport.Height, request);
                if (!request.CheckScrollableBeforeSwipe)
                    break;

                before = await _scrollResolver.GetAtPointAsync(page, plan.Start.X, plan.Start.Y);
                if (_scrollResolver.CanScroll(before, plan.Direction))
                    break;

                var document = await _scrollResolver.GetDocumentAsync(page);
                if (_scrollResolver.CanScroll(document, plan.Direction))
                {
                    before = document;
                    break;
                }
                plan = null;
            }

            if (plan == null)
            {
                Session.RecordGesture(null);
                return null;
            }

            if (request.VerifyScrollChanged)
            {
                before ??= await _scrollResolver.GetAtPointAsync(page, plan.Start.X, plan.Start.Y);
                docBefore = await _scrollResolver.GetDocumentAsync(page);
            }

            request.Log?.Invoke($"{plan.Intent}/{plan.Direction} ({plan.Start.X:0},{plan.Start.Y:0})->({plan.End.X:0},{plan.End.Y:0}) duration={plan.DurationMs:0}ms release={plan.ReleaseVelocityPxPerSecond:0}px/s");

            var baseTrajectory = _kinematics.GenerateBaseTrajectory(Session, plan);
            var samples = _biomechanics.Apply(Session, plan, baseTrajectory);
            await _dispatcher.DispatchAsync(cdp, samples, plan, Session.DeviceProfile, cancellationToken);

            bool moved = true;
            if (request.VerifyScrollChanged && before != null && docBefore != null)
            {
                moved = await _scrollResolver.DidScrollAsync(
                    page, before, docBefore, plan.Start.X, plan.Start.Y, plan.Direction, request.ScrollChangedMinDelta);
            }

            var trace = BuildTrace(plan, samples, moved);
            Session.RecordGesture(moved || !request.VerifyScrollChanged ? trace : null);
            return moved || !request.VerifyScrollChanged ? trace : null;
        }

        public async Task<HumanSwipeTrace?> SwipeInsideElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            HumanTouchRequest request,
            CancellationToken cancellationToken = default)
        {
            var rect = await _scrollResolver.GetElementRectAsync(locator);
            return rect == null ? null : await SwipeInsideRectAsync(page, cdp, rect, request, cancellationToken);
        }

        public async Task<HumanSwipeTrace?> SwipeInsideElementAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            HumanTouchRequest request,
            CancellationToken cancellationToken = default)
        {
            var rect = await _scrollResolver.GetElementRectAsync(element);
            return rect == null ? null : await SwipeInsideRectAsync(page, cdp, rect, request, cancellationToken);
        }

        public async Task<List<HumanSwipeTrace>> SwipeToElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            double comfortTopRatio = 0.22,
            double comfortBottomRatio = 0.72,
            CancellationToken cancellationToken = default)
        {
            Validate(page, cdp);
            var traces = new List<HumanSwipeTrace>();
            var viewport = await GetEffectiveViewportAsync(page);
            if (viewport.Width <= 0 || viewport.Height <= 0) return traces;

            for (int i = 0; i < maxSwipes; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rect = await _scrollResolver.GetElementRectAsync(locator);
                if (rect == null) break;

                double vh = viewport.Height;
                double center = rect.CenterY;
                if (center >= vh * comfortTopRatio && center <= vh * comfortBottomRatio)
                    break;

                var direction = center > vh * comfortBottomRatio ? HumanSwipeDirection.Up : HumanSwipeDirection.Down;
                double targetCenter = vh * ((comfortTopRatio + comfortBottomRatio) * 0.5);
                int distance = (int)Math.Clamp(Math.Abs(center - targetCenter), vh * 0.08, vh * 0.46);
                var intent = distance < vh * 0.16 ? SwipeIntent.MicroAdjust : SwipeIntent.Preview;

                var trace = await SwipeAsync(page, cdp, new HumanTouchRequest
                {
                    Direction = direction,
                    Intent = intent,
                    DistancePx = distance,
                    VerifyScrollChanged = true,
                    CheckScrollableBeforeSwipe = true,
                    ScrollChangedMinDelta = intent == SwipeIntent.MicroAdjust ? 3 : 7
                }, cancellationToken);

                if (trace == null) break;
                traces.Add(trace);
                await Task.Delay(RandomMath.NextInt(Session.Random, 140, 420), cancellationToken);
            }
            return traces;
        }

        public async Task<List<HumanSwipeTrace>> SwipeToElementVisibleAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            double visibleMarginPx = 8,
            CancellationToken cancellationToken = default)
        {
            Validate(page, cdp);
            var traces = new List<HumanSwipeTrace>();
            var viewport = await GetEffectiveViewportAsync(page);
            if (viewport.Width <= 0 || viewport.Height <= 0) return traces;

            for (int i = 0; i < maxSwipes; i++)
            {
                var rect = await _scrollResolver.GetElementRectAsync(locator);
                if (rect == null) break;
                double vh = viewport.Height;
                if (rect.Bottom >= visibleMarginPx && rect.Top <= vh - visibleMarginPx)
                    break;

                var direction = rect.Top > vh ? HumanSwipeDirection.Up : HumanSwipeDirection.Down;
                var trace = await SwipeAsync(page, cdp, new HumanTouchRequest
                {
                    Direction = direction,
                    Intent = SwipeIntent.Preview,
                    DistancePx = (int)(vh * RandomMath.NextDouble(Session.Random, 0.28, 0.44))
                }, cancellationToken);
                if (trace == null) break;
                traces.Add(trace);
                await Task.Delay(RandomMath.NextInt(Session.Random, 130, 360), cancellationToken);
            }
            return traces;
        }

        private async Task<HumanSwipeTrace?> SwipeInsideRectAsync(
            IPage page,
            ICDPSession cdp,
            ElementRect rect,
            HumanTouchRequest request,
            CancellationToken cancellationToken)
        {
            Validate(page, cdp);
            var viewport = await GetEffectiveViewportAsync(page);
            if (viewport.Width <= 0 || viewport.Height <= 0 || rect.Width < 12 || rect.Height < 12) return null;

            int safe = 6;
            var r = Session.Random;
            bool horizontal = request.Direction is HumanSwipeDirection.Left or HumanSwipeDirection.Right;
            double startX, startY, endX, endY;

            if (horizontal)
            {
                startY = Math.Clamp(RandomMath.TruncatedNormal(r, rect.CenterY, rect.Height * 0.07, rect.Top + safe, rect.Bottom - safe), safe, viewport.Height - safe);
                if (request.Direction == HumanSwipeDirection.Left)
                {
                    startX = rect.Left + rect.Width * RandomMath.NextDouble(r, 0.70, 0.86);
                    endX = rect.Left + rect.Width * RandomMath.NextDouble(r, 0.18, 0.36);
                }
                else
                {
                    startX = rect.Left + rect.Width * RandomMath.NextDouble(r, 0.14, 0.30);
                    endX = rect.Left + rect.Width * RandomMath.NextDouble(r, 0.64, 0.82);
                }
                endY = startY + RandomMath.Normal(r, 0, 2.5);
            }
            else
            {
                startX = Math.Clamp(RandomMath.TruncatedNormal(r, rect.CenterX, rect.Width * 0.07, rect.Left + safe, rect.Right - safe), safe, viewport.Width - safe);
                if (request.Direction == HumanSwipeDirection.Up)
                {
                    startY = rect.Top + rect.Height * RandomMath.NextDouble(r, 0.68, 0.86);
                    endY = rect.Top + rect.Height * RandomMath.NextDouble(r, 0.16, 0.34);
                }
                else
                {
                    startY = rect.Top + rect.Height * RandomMath.NextDouble(r, 0.14, 0.30);
                    endY = rect.Top + rect.Height * RandomMath.NextDouble(r, 0.66, 0.84);
                }
                endX = startX + RandomMath.Normal(r, 0, 2.5);
            }

            request.StartX = (int)Math.Round(startX);
            request.StartY = (int)Math.Round(startY);
            request.EndX = (int)Math.Round(endX);
            request.EndY = (int)Math.Round(endY);
            request.CheckScrollableBeforeSwipe = false;
            request.VerifyScrollChanged = false;
            return await SwipeAsync(page, cdp, request, cancellationToken);
        }


        private sealed class EffectiveViewport
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public double DevicePixelRatio { get; set; }
            public double ScreenWidth { get; set; }
            public double ScreenHeight { get; set; }
            public double VisualViewportScale { get; set; }
        }

        /// <summary>
        /// 获取当前页面实际可用的 CSS viewport。
        ///
        /// 普通 Playwright NewContext/NewPage 创建的页面优先使用 Page.ViewportSize。
        /// ConnectOverCDPAsync 附加到既有 Chromium 时，Page.ViewportSize 可能为 null，
        /// 此时从页面运行时读取 visualViewport/innerWidth/innerHeight。
        /// </summary>
        private static async Task<EffectiveViewport> GetEffectiveViewportAsync(IPage page)
        {
            if (page.ViewportSize != null)
            {
                return new EffectiveViewport
                {
                    Width = page.ViewportSize.Width,
                    Height = page.ViewportSize.Height,
                    DevicePixelRatio = 1,
                    ScreenWidth = page.ViewportSize.Width,
                    ScreenHeight = page.ViewportSize.Height,
                    VisualViewportScale = 1
                };
            }

            try
            {
                var result = await page.EvaluateAsync<EffectiveViewport>(@"
                () => {
                    const vv = window.visualViewport;

                    // 触摸坐标、Playwright BoundingBox、DOM client 坐标都以 CSS layout viewport 为基准，
                    // 所以这里优先 innerWidth/innerHeight，而不是 visualViewport.width/height。
                    const width = window.innerWidth || document.documentElement.clientWidth || vv?.width || 0;
                    const height = window.innerHeight || document.documentElement.clientHeight || vv?.height || 0;

                    return {
                        Width: Math.max(1, Math.round(width)),
                        Height: Math.max(1, Math.round(height)),
                        DevicePixelRatio: Number(window.devicePixelRatio || 1),
                        ScreenWidth: Number(window.screen?.width || 0),
                        ScreenHeight: Number(window.screen?.height || 0),
                        VisualViewportScale: Number(vv?.scale || 1)
                    };
                }");

                if (result != null && result.Width > 0 && result.Height > 0)
                    return result;
            }
            catch
            {
                // 页面可能正在导航、执行上下文刚被销毁等。下面再走一次轻量 fallback。
            }

            try
            {
                int width = await page.EvaluateAsync<int>("() => Math.max(1, Math.round(window.innerWidth || document.documentElement.clientWidth || 0))");
                int height = await page.EvaluateAsync<int>("() => Math.max(1, Math.round(window.innerHeight || document.documentElement.clientHeight || 0))");

                return new EffectiveViewport
                {
                    Width = width,
                    Height = height,
                    DevicePixelRatio = 1,
                    ScreenWidth = width,
                    ScreenHeight = height,
                    VisualViewportScale = 1
                };
            }
            catch
            {
                return new EffectiveViewport();
            }
        }

        private static HumanSwipeTrace BuildTrace(GesturePlan plan, IReadOnlyList<TouchSample> samples, bool moved)
        {
            if (samples.Count == 0)
            {
                return new HumanSwipeTrace
                {
                    StartX = plan.Start.X,
                    StartY = plan.Start.Y,
                    EndX = plan.Start.X,
                    EndY = plan.Start.Y,
                    PlannedEndX = plan.End.X,
                    PlannedEndY = plan.End.Y,
                    Direction = plan.Direction,
                    Mode = plan.Mode,
                    Intent = plan.Intent,
                    StartHoldMs = plan.StartHoldMs,
                    EndHoldMs = plan.EndHoldMs,
                    TotalDelayMs = plan.StartHoldMs + plan.EndHoldMs,
                    TargetReleaseVelocityPxPerSecond = plan.ReleaseVelocityPxPerSecond,
                    ScrollChanged = moved
                };
            }

            var points = new List<HumanSwipeTracePoint>(samples.Count);
            for (int i = 0; i < samples.Count; i++)
            {
                double nextTime = i + 1 < samples.Count ? samples[i + 1].TimeMs : samples[i].TimeMs;
                points.Add(new HumanSwipeTracePoint
                {
                    X = samples[i].Point.X,
                    Y = samples[i].Point.Y,
                    TimeMs = samples[i].TimeMs,
                    DelayMs = (int)Math.Max(0, Math.Round(nextTime - samples[i].TimeMs)),
                    RadiusX = samples[i].RadiusX,
                    RadiusY = samples[i].RadiusY,
                    Force = samples[i].Force,
                    RotationAngle = samples[i].RotationAngle,
                    VelocityPxPerSecond = samples[i].VelocityPxPerSecond
                });
            }

            return new HumanSwipeTrace
            {
                StartX = plan.Start.X,
                StartY = plan.Start.Y,
                EndX = samples.Last().Point.X,
                EndY = samples.Last().Point.Y,
                PlannedEndX = plan.End.X,
                PlannedEndY = plan.End.Y,
                Direction = plan.Direction,
                Mode = plan.Mode,
                Intent = plan.Intent,
                Steps = samples.Count,
                StartHoldMs = plan.StartHoldMs,
                EndHoldMs = plan.EndHoldMs,
                TotalDelayMs = (int)Math.Round((samples.Count == 0 ? 0 : samples[^1].TimeMs) + plan.StartHoldMs + plan.EndHoldMs),
                DurationMs = samples.Count == 0 ? 0 : samples[^1].TimeMs,
                ReleaseVelocityPxPerSecond = samples.Count == 0 ? 0 : samples[^1].VelocityPxPerSecond,
                TargetReleaseVelocityPxPerSecond = plan.ReleaseVelocityPxPerSecond,
                ScrollChanged = moved,
                Points = points
            };
        }

        private static void Validate(IPage page, ICDPSession cdp)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));
            if (cdp == null) throw new ArgumentNullException(nameof(cdp));
            if (page.IsClosed) throw new InvalidOperationException("Page is closed.");
        }
    }
}
