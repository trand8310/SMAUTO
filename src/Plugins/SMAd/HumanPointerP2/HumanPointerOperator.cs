using Microsoft.Playwright;
using SMAd.HumanInput;

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

            var end = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < end && !page.IsClosed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await BrowseOnceAsync(page, cdp, cancellationToken);

                var remaining = end - DateTime.UtcNow;
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
            ValidatePage(page);
            cancellationToken.ThrowIfCancellationRequested();

            var viewport = page.ViewportSize;
            if (viewport == null || viewport.Width <= 2 || viewport.Height <= 2)
                return;

            var cursor = Session.GetCursor(page);
            if (cursor.X < 2 || cursor.Y < 2 || cursor.X >= viewport.Width - 2 || cursor.Y >= viewport.Height - 2)
            {
                var readingPoint = new PointerPosition(
                    viewport.Width * Next(0.48, 0.76),
                    viewport.Height * Next(0.38, 0.68));
                await MovePointerAsync(page, readingPoint, targetWidth: 180, cancellationToken);
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

            Session.LastIntent = intent;
            if (intent == HumanActionIntent.BackReview)
                Session.ConsecutiveForwardScrolls = 0;
            else
                Session.ConsecutiveForwardScrolls++;

            Options.Log?.Invoke($"PC Wheel: {intent}, delta={totalDelta:0}, events={eventCount}");
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
            for (int attempt = 0; attempt < maxSwipes; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var viewport = page.ViewportSize;
                var box = ToTargetBox(await locator.BoundingBoxAsync());
                if (viewport == null || box == null)
                    return;

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

                await ScrollByIntentAsync(page, cdp, intent, cancellationToken);
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
                (x, y) => IsLocatorHitAsync(locator, x, y),
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

            return await ClickCoreAsync(
                page,
                async () => ToTargetBox(await element.BoundingBoxAsync()),
                (x, y) => IsElementHitAsync(element, x, y),
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

            int hoverDelay = (int)Math.Round(Next(45, 220) * Session.Profile.ReactionBias);
            await Task.Delay(hoverDelay, cancellationToken);

            await page.Mouse.DownAsync(new MouseDownOptions { Button = MouseButton.Left });
            bool released = false;
            try
            {
                int holdDelay = (int)Math.Round(Next(48, 135) * Session.Profile.ReactionBias);
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

            await Task.Delay(Session.Random.Next(80, 260), cancellationToken);
            Options.Log?.Invoke($"PC Click: ({target.X:0},{target.Y:0}), box=({box.X:0},{box.Y:0},{box.Width:0},{box.Height:0})");
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
            var trace = _planner.Plan(Session, start, target, targetWidth);
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

        private async Task<bool> IsLocatorHitAsync(ILocator locator, double x, double y)
        {
            try
            {
                return await locator.EvaluateAsync<bool>(
                    "(el, p) => { const hit = document.elementFromPoint(p.x, p.y); return !!hit && (hit === el || el.contains(hit)); }",
                    new { x, y });
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> IsElementHitAsync(IElementHandle element, double x, double y)
        {
            try
            {
                return await element.EvaluateAsync<bool>(
                    "(el, p) => { const hit = document.elementFromPoint(p.x, p.y); return !!hit && (hit === el || el.contains(hit)); }",
                    new { x, y });
            }
            catch
            {
                return false;
            }
        }

        private HumanActionIntent DecideNextIntent()
        {
            double value = Session.Random.NextDouble();
            if (Session.ConsecutiveForwardScrolls >= 4 && value < 0.12)
                return HumanActionIntent.BackReview;
            if (value < 0.34) return HumanActionIntent.Reading;
            if (value < 0.61) return HumanActionIntent.Preview;
            if (value < 0.76) return HumanActionIntent.FastScan;
            if (value < 0.91) return HumanActionIntent.MicroAdjust;
            return HumanActionIntent.BackReview;
        }

        private double Next(double min, double max) =>
            min + (Session.Random.NextDouble() * (max - min));

        private static void ValidatePage(IPage page)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            if (page.IsClosed)
                throw new InvalidOperationException("Page is closed.");
        }
    }
}
