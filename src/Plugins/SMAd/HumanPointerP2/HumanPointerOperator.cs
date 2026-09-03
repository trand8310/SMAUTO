using Microsoft.Playwright;
using SMAd.HumanInput;
using System.Diagnostics;

namespace SMAd.HumanPointerP2
{
    /// <summary>
    /// 基于 Playwright Mouse 的 PC 输入实现。Playwright 负责浏览器状态，
    /// 本类负责轨迹、节奏、滚轮批次和连续光标状态。
    /// </summary>
    public sealed class HumanPointerOperator : IHumanInputOperator
    {
        private sealed record TargetBox(double X, double Y, double Width, double Height);

        private readonly PointerPathPlanner _planner = new();
        private readonly ScrollTargetResolver _scrollResolver = new();

        public HumanPointerOperator(HumanPointerOperatorOptions? options = null)
        {
            Options = options ?? new HumanPointerOperatorOptions();
            Session = Options.Session ?? new HumanPointerSession(
                PointerUserProfile.Create(Guid.NewGuid().GetHashCode()));
        }

        public HumanPointerOperatorOptions Options { get; }
        public HumanPointerSession Session { get; }
        public bool IsTouch => false;

        public async Task BrowseOnceAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            var intent = DecideNextIntent();
            await ScrollByIntentAsync(page, cdp, intent, cancellationToken);
        }

        public async Task BrowseForAsync(
            IPage page,
            ICDPSession cdp,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            if (duration <= TimeSpan.Zero)
                return;

            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < duration && !page.IsClosed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await BrowseOnceAsync(page, cdp, cancellationToken);

                var remaining = duration - clock.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    break;

                int delayMs = (int)Math.Round(
                    Next(430, 1650) * Session.Profile.ReactionBias * Options.DelayFactor);
                var delay = TimeSpan.FromMilliseconds(Math.Min(delayMs, remaining.TotalMilliseconds));
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
            }
        }

        public async Task ScrollByIntentAsync(
            IPage page,
            ICDPSession cdp,
            HumanActionIntent intent,
            CancellationToken cancellationToken = default)
        {
            await ScrollByIntentCoreAsync(page, intent, cancellationToken);
        }

        public async Task ScrollToTopAsync(
            IPage page,
            ICDPSession cdp,
            CancellationToken cancellationToken = default)
        {
            ValidatePage(page);
            var viewport = page.ViewportSize;
            if (viewport == null || viewport.Width <= 2 || viewport.Height <= 2)
                return;

            var cursor = Session.GetCursor(page);
            if (cursor.X < 2 || cursor.Y < 2 || cursor.X >= viewport.Width - 2 || cursor.Y >= viewport.Height - 2)
            {
                cursor = new PointerPosition(
                    viewport.Width * Next(0.58, 0.76),
                    viewport.Height * Next(0.38, 0.62));
                await MovePointerAsync(page, cursor, targetWidth: 180, cancellationToken);
            }

            const int maxAttempts = 24;
            for (int attempt = 0; attempt < maxAttempts && !page.IsClosed; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int eventCount = Session.Random.Next(4, 8);
                double totalDelta = -viewport.Height *
                    Next(0.62, 1.05) *
                    Session.Profile.WheelBias;

                bool moved = await DispatchWheelBatchAsync(
                    page,
                    cursor,
                    HumanActionIntent.BackReview,
                    totalDelta,
                    eventCount,
                    direction: -1,
                    forceRelocate: false,
                    cancellationToken);

                if (!moved)
                {
                    // 鼠标下方的内层容器已到顶部时，再寻找父级或页面滚动区域。
                    moved = await DispatchWheelBatchAsync(
                        page,
                        Session.GetCursor(page),
                        HumanActionIntent.BackReview,
                        totalDelta * Next(0.48, 0.72),
                        Math.Max(2, eventCount / 2),
                        direction: -1,
                        forceRelocate: true,
                        cancellationToken);
                }

                if (!moved)
                    break;

                cursor = Session.GetCursor(page);
                await Task.Delay(Session.Random.Next(110, 360), cancellationToken);
            }

            Session.LastIntent = HumanActionIntent.BackReview;
            Session.ConsecutiveForwardScrolls = 0;
        }

        private async Task<bool> ScrollByIntentCoreAsync(
            IPage page,
            HumanActionIntent intent,
            CancellationToken cancellationToken)
        {
            ValidatePage(page);
            cancellationToken.ThrowIfCancellationRequested();

            var viewport = page.ViewportSize;
            if (viewport == null || viewport.Width <= 2 || viewport.Height <= 2)
                return false;

            var cursor = Session.GetCursor(page);
            if (cursor.X < 2 || cursor.Y < 2 || cursor.X >= viewport.Width - 2 || cursor.Y >= viewport.Height - 2)
            {
                var readingPoint = new PointerPosition(
                    viewport.Width * Next(0.48, 0.76),
                    viewport.Height * Next(0.38, 0.68));
                await MovePointerAsync(page, readingPoint, targetWidth: 180, cancellationToken);
                cursor = readingPoint;
            }

            double ratio = intent switch
            {
                HumanActionIntent.MicroAdjust => Next(0.06, 0.15),
                HumanActionIntent.Reading => Next(0.18, 0.36),
                HumanActionIntent.FastScan => Next(0.55, 0.95),
                HumanActionIntent.BackReview => -Next(0.16, 0.38),
                _ => Next(0.32, 0.58)
            };

            double totalDelta = viewport.Height * ratio * Session.Profile.WheelBias;
            int eventCount = intent switch
            {
                HumanActionIntent.MicroAdjust => Session.Random.Next(1, 3),
                HumanActionIntent.FastScan => Session.Random.Next(5, 10),
                _ => Session.Random.Next(3, 7)
            };

            int direction = totalDelta >= 0 ? 1 : -1;
            bool moved = await DispatchWheelBatchAsync(
                page,
                cursor,
                intent,
                totalDelta,
                eventCount,
                direction,
                forceRelocate: false,
                cancellationToken);

            if (!moved)
            {
                // 当前鼠标下方的容器可能已到边界，换到另一个可滚动区域后重试一次。
                moved = await DispatchWheelBatchAsync(
                    page,
                    Session.GetCursor(page),
                    intent,
                    totalDelta * Next(0.42, 0.68),
                    Math.Max(1, eventCount / 2),
                    direction,
                    forceRelocate: true,
                    cancellationToken);
            }

            if (!moved)
            {
                Options.Log?.Invoke($"PC Wheel stalled: {intent}, no scrollable target moved");
                return false;
            }

            Session.LastIntent = intent;
            if (intent == HumanActionIntent.BackReview)
                Session.ConsecutiveForwardScrolls = 0;
            else
                Session.ConsecutiveForwardScrolls++;

            return true;
        }

        private async Task<bool> DispatchWheelBatchAsync(
            IPage page,
            PointerPosition cursor,
            HumanActionIntent intent,
            double totalDelta,
            int eventCount,
            int direction,
            bool forceRelocate,
            CancellationToken cancellationToken)
        {
            var target = await _scrollResolver.ResolveAsync(page, cursor, direction, forceRelocate);
            if (target == null)
                return false;

            await MovePointerAsync(page, target.PagePoint, targetWidth: 160, cancellationToken);

            var weights = Enumerable.Range(0, eventCount)
                .Select(i => Math.Sin(Math.PI * ((i + 0.65) / (eventCount + 0.3))))
                .ToArray();
            double weightSum = weights.Sum();

            for (int i = 0; i < eventCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double delta = totalDelta * (weights[i] / weightSum) * Next(0.90, 1.10);
                await page.Mouse.WheelAsync(0, (float)delta);

                if (i + 1 < eventCount)
                {
                    int delay = intent == HumanActionIntent.FastScan
                        ? Session.Random.Next(38, 105)
                        : Session.Random.Next(70, 185);
                    await Task.Delay(delay, cancellationToken);
                }
            }

            await Task.Delay(70, cancellationToken);
            var after = await _scrollResolver.ProbeAsync(page, target, direction);
            bool moved = after != null &&
                (Math.Abs(after.WindowX - target.Before.WindowX) > 0.5 ||
                 Math.Abs(after.WindowY - target.Before.WindowY) > 0.5 ||
                 (after.TargetKey == target.Before.TargetKey &&
                  Math.Abs(after.Position - target.Before.Position) > 0.5));

            double actualDelta = after == null ? 0 : after.Position - target.Before.Position;
            Options.Log?.Invoke(
                $"PC Wheel: {intent}, target={target.Before.TargetLabel}, requested={totalDelta:0}, actual={actualDelta:0}, events={eventCount}, moved={moved}");
            return moved;
        }

        public async Task MoveToElementAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            int maxSwipes = 10,
            CancellationToken cancellationToken = default)
        {
            ValidatePage(page);
            if (locator == null)
                throw new ArgumentNullException(nameof(locator));

            maxSwipes = Math.Max(1, maxSwipes);
            int stalledAttempts = 0;
            for (int attempt = 0; attempt < maxSwipes; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var viewport = page.ViewportSize;
                var box = ToTargetBox(await locator.BoundingBoxAsync());
                if (viewport == null)
                    return;

                if (box == null)
                {
                    if (attempt == 0)
                    {
                        await locator.ScrollIntoViewIfNeededAsync();
                        await Task.Delay(Session.Random.Next(100, 240), cancellationToken);
                        continue;
                    }

                    return;
                }

                double visibleTop = Math.Max(0, box.Y);
                double visibleBottom = Math.Min(viewport.Height, box.Y + box.Height);
                double visibleHeight = visibleBottom - visibleTop;

                if (visibleHeight >= Math.Min(12, box.Height) &&
                    box.X + box.Width > 2 && box.X < viewport.Width - 2)
                {
                    var target = PickTargetPoint(box, viewport.Width, viewport.Height);
                    await MovePointerAsync(page, target, Math.Max(8, Math.Min(box.Width, box.Height)), cancellationToken);
                    return;
                }

                var intent = box.Y < 0
                    ? HumanActionIntent.BackReview
                    : (Math.Abs(box.Y - viewport.Height) < viewport.Height * 0.20
                        ? HumanActionIntent.MicroAdjust
                        : HumanActionIntent.Preview);

                bool moved = await ScrollByIntentCoreAsync(page, intent, cancellationToken);
                stalledAttempts = moved ? 0 : stalledAttempts + 1;
                if (stalledAttempts >= 2)
                {
                    // 两次真实滚轮都没有位移时才使用 Playwright 定位作为恢复措施。
                    await locator.ScrollIntoViewIfNeededAsync();
                    stalledAttempts = 0;
                }

                await Task.Delay(Session.Random.Next(120, 330), cancellationToken);
            }
        }

        public async Task<bool> ClickAsync(
            IPage page,
            ICDPSession cdp,
            ILocator locator,
            CancellationToken cancellationToken = default)
        {
            if (locator == null)
                return false;

            await MoveToElementAsync(page, cdp, locator, 10, cancellationToken);
            return await ClickCoreAsync(
                page,
                async () => ToTargetBox(await locator.BoundingBoxAsync()),
                (x, y) => IsLocatorHitAsync(page, locator, x, y),
                cancellationToken);
        }

        public async Task<bool> ClickAsync(
            IPage page,
            ICDPSession cdp,
            IElementHandle element,
            CancellationToken cancellationToken = default)
        {
            if (element == null)
                return false;

            await element.ScrollIntoViewIfNeededAsync();
            return await ClickCoreAsync(
                page,
                async () => ToTargetBox(await element.BoundingBoxAsync()),
                (x, y) => IsElementHitAsync(page, element, x, y),
                cancellationToken);
        }

        private async Task<bool> ClickCoreAsync(
            IPage page,
            Func<Task<TargetBox?>> getBox,
            Func<double, double, Task<bool>> verifyHit,
            CancellationToken cancellationToken)
        {
            ValidatePage(page);
            cancellationToken.ThrowIfCancellationRequested();

            var viewport = page.ViewportSize;
            var box = await getBox();
            if (viewport == null || box == null || box.Width <= 2 || box.Height <= 2)
                return false;

            var target = PickTargetPoint(box, viewport.Width, viewport.Height);
            await MovePointerAsync(page, target, Math.Max(8, Math.Min(box.Width, box.Height)), cancellationToken);

            if (Options.VerifyHitTarget && !await verifyHit(target.X, target.Y))
            {
                // DOM 在移动过程中可能变化，重新取框并尝试一次。
                box = await getBox();
                if (box == null)
                    return false;

                target = PickTargetPoint(box, viewport.Width, viewport.Height);
                await MovePointerAsync(page, target, Math.Max(8, Math.Min(box.Width, box.Height)), cancellationToken);
                if (!await verifyHit(target.X, target.Y))
                    return false;
            }

            int hoverDelay = (int)Math.Round(NextCentered(55, 205) * Session.Profile.ReactionBias);
            await Task.Delay(hoverDelay, cancellationToken);

            await page.Mouse.DownAsync(new MouseDownOptions { Button = MouseButton.Left });
            bool released = false;
            try
            {
                int holdDelay = (int)Math.Round(NextCentered(52, 128) * Session.Profile.ReactionBias);
                await Task.Delay(holdDelay, cancellationToken);
                await page.Mouse.UpAsync(new MouseUpOptions { Button = MouseButton.Left });
                released = true;
            }
            finally
            {
                if (!released && !page.IsClosed)
                {
                    try { await page.Mouse.UpAsync(new MouseUpOptions { Button = MouseButton.Left }); }
                    catch { }
                }
            }

            Options.Log?.Invoke($"PC Click: ({target.X:0},{target.Y:0}), box=({box.X:0},{box.Y:0},{box.Width:0},{box.Height:0})");
            try
            {
                await Task.Delay((int)Math.Round(NextCentered(85, 245)), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // mouseUp 已经发生；取消观察延时不能把已提交的点击重新标记为失败。
            }

            return true;
        }

        private async Task MovePointerAsync(
            IPage page,
            PointerPosition target,
            double targetWidth,
            CancellationToken cancellationToken)
        {
            var viewport = page.ViewportSize;
            if (viewport == null)
                return;

            target = new PointerPosition(
                Math.Clamp(target.X, 1, viewport.Width - 1),
                Math.Clamp(target.Y, 1, viewport.Height - 1));

            var start = Session.GetCursor(page);
            var trace = _planner.Plan(
                Session,
                start,
                target,
                targetWidth,
                viewport.Width,
                viewport.Height);
            foreach (var point in trace.Points)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await page.Mouse.MoveAsync(
                    (float)Math.Clamp(point.X, 1, viewport.Width - 1),
                    (float)Math.Clamp(point.Y, 1, viewport.Height - 1),
                    new MouseMoveOptions { Steps = 1 });

                if (point.DelayMs > 0)
                    await Task.Delay(point.DelayMs, cancellationToken);
            }

            Session.SetCursor(page, target);
            Options.Log?.Invoke($"PC Move: ({start.X:0},{start.Y:0})->({target.X:0},{target.Y:0}), points={trace.Points.Count}, duration={trace.DurationMs:0}ms");
        }

        private PointerPosition PickTargetPoint(TargetBox box, int viewportWidth, int viewportHeight)
        {
            double left = Math.Max(1, box.X);
            double top = Math.Max(1, box.Y);
            double right = Math.Min(viewportWidth - 1, box.X + box.Width);
            double bottom = Math.Min(viewportHeight - 1, box.Y + box.Height);
            if (right <= left || bottom <= top)
                return new PointerPosition(
                    Math.Clamp(box.X + (box.Width / 2), 1, viewportWidth - 1),
                    Math.Clamp(box.Y + (box.Height / 2), 1, viewportHeight - 1));

            double width = right - left;
            double height = bottom - top;
            double x = left + (width * TruncatedCenterRatio());
            double y = top + (height * TruncatedCenterRatio());
            return new PointerPosition(x, y);
        }

        private double TruncatedCenterRatio()
        {
            // 两次均匀分布平均后自然集中在中部，同时保留足够变化。
            double ratio = (Session.Random.NextDouble() + Session.Random.NextDouble()) * 0.5;
            return Math.Clamp(0.18 + (ratio * 0.64), 0.18, 0.82);
        }

        private static TargetBox? ToTargetBox(LocatorBoundingBoxResult? box) => box == null
            ? null
            : new TargetBox(box.X, box.Y, box.Width, box.Height);

        private static TargetBox? ToTargetBox(ElementHandleBoundingBoxResult? box) => box == null
            ? null
            : new TargetBox(box.X, box.Y, box.Width, box.Height);

        private async Task<bool> IsLocatorHitAsync(IPage page, ILocator locator, double x, double y)
        {
            IElementHandle? element = null;
            try
            {
                element = await locator.ElementHandleAsync();
                return element != null && await IsElementHitAsync(page, element, x, y);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (element != null)
                {
                    try { await element.DisposeAsync(); }
                    catch { }
                }
            }
        }

        private async Task<bool> IsElementHitAsync(IPage page, IElementHandle element, double x, double y)
        {
            try
            {
                var ownerFrame = await element.OwnerFrameAsync();
                var localPoint = await _scrollResolver.ToLocalPointAsync(
                    page,
                    ownerFrame,
                    new PointerPosition(x, y));
                if (localPoint == null)
                    return false;

                return await element.EvaluateAsync<bool>(
                    "(el, p) => { const hit = document.elementFromPoint(p.x, p.y); return !!hit && (hit === el || el.contains(hit)); }",
                    new { x = localPoint.Value.X, y = localPoint.Value.Y });
            }
            catch
            {
                return false;
            }
        }

        private HumanActionIntent DecideNextIntent()
        {
            double value = Session.Random.NextDouble();
            if (Session.ConsecutiveForwardScrolls >= 4 && value < 0.18)
                return HumanActionIntent.BackReview;

            return Session.LastIntent switch
            {
                HumanActionIntent.FastScan => value switch
                {
                    < 0.42 => HumanActionIntent.Reading,
                    < 0.68 => HumanActionIntent.Preview,
                    < 0.84 => HumanActionIntent.MicroAdjust,
                    < 0.94 => HumanActionIntent.BackReview,
                    _ => HumanActionIntent.FastScan
                },
                HumanActionIntent.BackReview => value switch
                {
                    < 0.46 => HumanActionIntent.Reading,
                    < 0.76 => HumanActionIntent.Preview,
                    < 0.91 => HumanActionIntent.MicroAdjust,
                    _ => HumanActionIntent.FastScan
                },
                HumanActionIntent.MicroAdjust => value switch
                {
                    < 0.44 => HumanActionIntent.Reading,
                    < 0.76 => HumanActionIntent.Preview,
                    < 0.89 => HumanActionIntent.FastScan,
                    _ => HumanActionIntent.BackReview
                },
                _ => value switch
                {
                    < 0.38 => HumanActionIntent.Reading,
                    < 0.66 => HumanActionIntent.Preview,
                    < 0.80 => HumanActionIntent.MicroAdjust,
                    < 0.93 => HumanActionIntent.FastScan,
                    _ => HumanActionIntent.BackReview
                }
            };
        }

        private double Next(double min, double max) =>
            min + (Session.Random.NextDouble() * (max - min));

        private double NextCentered(double min, double max) =>
            min + (((Session.Random.NextDouble() + Session.Random.NextDouble()) * 0.5) * (max - min));

        private static void ValidatePage(IPage page)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            if (page.IsClosed)
                throw new InvalidOperationException("Page is closed.");
        }
    }
}
