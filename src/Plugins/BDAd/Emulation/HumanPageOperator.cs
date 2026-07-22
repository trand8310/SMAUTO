using Microsoft.Playwright;
using System.Text;

namespace PlaywrightHumanInput;


/// <summary>
/// 面向授权网页测试和业务自动化的真人交互模拟。
///
/// 包含：
/// 1. 最小加加速度鼠标轨迹
/// 2. 贝塞尔曲线偏移
/// 3. 鼠标过冲与二次修正
/// 4. 元素内部自然落点
/// 5. 滚轮脉冲和惯性衰减
/// 6. 输入停顿、标点停顿和少量纠错
/// 7. 阅读、浏览和回看行为
/// </summary>
public sealed class HumanPageOperator
{
    private readonly IPage _page;
    private readonly StableRandom _random;
    private readonly HumanBehaviorProfile _profile;

    private float _mouseX;
    private float _mouseY;
    private bool _mouseInitialized;

    private int _actionCount;
    private DateTime _lastActionTime = DateTime.UtcNow;

    public HumanPageOperator(
        IPage page,
        HumanBehaviorProfile profile,
        int randomSeed)
    {
        _page = page ??
                throw new ArgumentNullException(
                    nameof(page));

        _profile = profile ??
                   throw new ArgumentNullException(
                       nameof(profile));

        _random = new StableRandom(randomSeed);
    }

    public async Task NavigateAsync(
        string url,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        await _page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = _profile.NavigationTimeoutMs
        });

        await WaitForPageSettledAsync(cancellationToken);

        await ReadingPauseAsync(cancellationToken);
    }

    /// <summary>
    /// 模拟浏览当前页面。
    /// </summary>
    public async Task BrowsePageAsync(
        int minSections = 2,
        int maxSections = 6,
        CancellationToken cancellationToken = default)
    {
        if (minSections < 0)
            throw new ArgumentOutOfRangeException(nameof(minSections));

        if (maxSections < minSections)
            throw new ArgumentOutOfRangeException(nameof(maxSections));

        int sections = _random.NextInt(minSections, maxSections + 1);

        for (int i = 0; i < sections; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int distance = _random.NextInt(280, 850);

            await ScrollByAsync(distance, cancellationToken);
            await ReadingPauseAsync(cancellationToken);

            // 偶尔将鼠标移动到正文区域，模拟阅读时鼠标停留。
            if (_random.NextDouble() < 0.35)
            {
                await MoveMouseToReadingAreaAsync(cancellationToken);
            }

            // 偶尔轻微向上回看。
            if (i > 0 &&
                _random.NextDouble() < _profile.ScrollBackProbability)
            {
                await ScrollByAsync(
                    -_random.NextInt(80, 260),
                    cancellationToken);

                await DelayAsync(350, 1_200, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 将鼠标移动到元素并悬停。
    /// </summary>
    public async Task HoverAsync(
        ILocator locator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);

        await PrepareLocatorAsync(locator, cancellationToken);

        MouseTarget target =
            await GetNaturalTargetPointAsync(locator);

        await MoveMouseAsync(
            target.X,
            target.Y,
            target.Width,
            cancellationToken);

        await DelayAsync(180, 680, cancellationToken);

        RegisterAction();
    }

    /// <summary>
    /// 真人方式点击元素。
    /// </summary>
    public async Task ClickAsync(
        ILocator locator,
        MouseButton button = MouseButton.Left,
        int retryCount = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);

        Exception? lastException = null;

        for (int attempt = 1; attempt <= retryCount; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await PrepareLocatorAsync(locator, cancellationToken);

                // 只进行 Playwright 的 actionability 检查，不真正点击。
                await locator.ClickAsync(new LocatorClickOptions
                {
                    Trial = true,
                    Timeout = _profile.ActionTimeoutMs
                });

                MouseTarget target =
                    await GetNaturalTargetPointAsync(locator);

                await MoveMouseAsync(
                    target.X,
                    target.Y,
                    target.Width,
                    cancellationToken);

                if (_random.NextDouble() <
                    _profile.PreClickPauseProbability)
                {
                    await DelayAsync(
                        90,
                        460,
                        cancellationToken);
                }

                // 点击前可能出现微小的位置修正。
                if (_random.NextDouble() <
                    _profile.MouseCorrectionProbability)
                {
                    float correctionX = Clamp(
                        target.X + RandomGaussian(0f, 1.8f),
                        target.Left + 1,
                        target.Right - 1);

                    float correctionY = Clamp(
                        target.Y + RandomGaussian(0f, 1.3f),
                        target.Top + 1,
                        target.Bottom - 1);

                    await MoveMouseSegmentAsync(
                        _mouseX,
                        _mouseY,
                        correctionX,
                        correctionY,
                        target.Width,
                        allowOvershoot: false,
                        cancellationToken);

                    target = target with
                    {
                        X = correctionX,
                        Y = correctionY
                    };
                }

                await _page.Mouse.DownAsync(new MouseDownOptions
                {
                    Button = button
                });

                await DelayAsync(
                    _profile.MinMouseDownMs,
                    _profile.MaxMouseDownMs,
                    cancellationToken);

                await _page.Mouse.UpAsync(new MouseUpOptions
                {
                    Button = button
                });

                await DelayAsync(240, 950, cancellationToken);

                RegisterAction();
                return;
            }
            catch (Exception ex) when (attempt < retryCount)
            {
                lastException = ex;

                await DelayAsync(
                    350 * attempt,
                    850 * attempt,
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"点击元素失败，已经重试 {retryCount} 次。",
            lastException);
    }

    public Task RightClickAsync(
        ILocator locator,
        CancellationToken cancellationToken = default)
    {
        return ClickAsync(
            locator,
            MouseButton.Right,
            cancellationToken: cancellationToken);
    }

    public async Task DoubleClickAsync(
        ILocator locator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);

        await PrepareLocatorAsync(locator, cancellationToken);

        MouseTarget target =
            await GetNaturalTargetPointAsync(locator);

        await MoveMouseAsync(
            target.X,
            target.Y,
            target.Width,
            cancellationToken);

        await DelayAsync(80, 280, cancellationToken);

        await PerformSinglePhysicalClickAsync(
            MouseButton.Left,
            cancellationToken);

        await DelayAsync(70, 190, cancellationToken);

        await PerformSinglePhysicalClickAsync(
            MouseButton.Left,
            cancellationToken);

        await DelayAsync(300, 850, cancellationToken);

        RegisterAction();
    }

    /// <summary>
    /// 点击输入框并逐字输入。
    /// 英文字母会产生 keydown/keyup，中文等字符使用 InsertText。
    /// </summary>
    public async Task TypeAsync(
        ILocator locator,
        string text,
        bool clearBeforeInput = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(text);

        await ClickAsync(
            locator,
            cancellationToken: cancellationToken);

        if (clearBeforeInput)
        {
            await DelayAsync(80, 260, cancellationToken);

            await _page.Keyboard.PressAsync("ControlOrMeta+A");

            await DelayAsync(60, 190, cancellationToken);

            await _page.Keyboard.PressAsync("Backspace");

            await DelayAsync(120, 360, cancellationToken);
        }

        foreach (Rune rune in text.EnumerateRunes())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string current = rune.ToString();

            if (CanSimulateTypo(rune) &&
                _random.NextDouble() < GetAdjustedTypoProbability())
            {
                char wrongKey =
                    GetNearbyKeyboardKey((char)rune.Value);

                await TypeSingleTextAsync(
                    wrongKey.ToString(),
                    cancellationToken);

                await DelayAsync(90, 280, cancellationToken);

                await _page.Keyboard.PressAsync("Backspace");

                await DelayAsync(70, 220, cancellationToken);
            }

            await TypeSingleTextAsync(
                current,
                cancellationToken);

            if (IsWordBoundary(rune))
            {
                await DelayAsync(45, 220, cancellationToken);
            }

            if (IsPunctuation(rune))
            {
                await DelayAsync(180, 620, cancellationToken);
            }

            if (_random.NextDouble() <
                _profile.ThinkingPauseProbability)
            {
                await DelayAsync(
                    260,
                    1_050,
                    cancellationToken);
            }
        }

        await DelayAsync(280, 850, cancellationToken);

        RegisterAction();
    }

    public async Task PressAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await DelayAsync(50, 230, cancellationToken);

        await _page.Keyboard.PressAsync(key);

        await DelayAsync(120, 520, cancellationToken);

        RegisterAction();
    }

    /// <summary>
    /// 模拟鼠标滚轮分段滚动。
    /// 正数向下，负数向上。
    /// </summary>
    public async Task ScrollByAsync(
        int totalDistance,
        CancellationToken cancellationToken = default)
    {
        if (totalDistance == 0)
            return;

        int direction = Math.Sign(totalDistance);
        float remaining = Math.Abs(totalDistance);

        // 一次滚动动作由多个脉冲构成。
        int pulseCount = Math.Clamp(
            (int)(remaining / 100f) + _random.NextInt(2, 5),
            3,
            16);

        float[] weights = BuildScrollWeights(pulseCount);
        float weightSum = weights.Sum();

        for (int i = 0; i < pulseCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float delta =
                remaining * (weights[i] / weightSum);

            // 单次滚轮不能过于巨大。
            delta = Math.Clamp(delta, 35f, 340f);

            float horizontalNoise =
                RandomGaussian(0f, 1.4f);

            await _page.Mouse.WheelAsync(
                horizontalNoise,
                delta * direction);

            int pause;

            if (i == pulseCount - 1)
            {
                pause = ScaleDelay(_random.NextInt(90, 260));
            }
            else
            {
                pause = ScaleDelay(_random.NextInt(28, 105));
            }

            await Task.Delay(pause, cancellationToken);
        }

        RegisterAction();
    }

    /// <summary>
    /// 在列表或无限滚动页面中寻找目标。
    /// </summary>
    public async Task<bool> FindAndClickAsync(
        Func<ILocator> locatorFactory,
        int maxScrolls = 15,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locatorFactory);

        for (int i = 0; i <= maxScrolls; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ILocator locator = locatorFactory();

            int count = await locator.CountAsync();

            if (count > 0)
            {
                ILocator first = locator.First;

                if (await first.IsVisibleAsync())
                {
                    await ClickAsync(
                        first,
                        cancellationToken: cancellationToken);

                    return true;
                }
            }

            if (i == maxScrolls)
                break;

            await ScrollByAsync(
                _random.NextInt(380, 760),
                cancellationToken);

            await DelayAsync(420, 1_250, cancellationToken);
        }

        return false;
    }


    /// <summary>
    /// 使用鼠标滚轮逐步滑动到指定目标。
    ///
    /// 返回 true：目标已进入指定的视口安全区域。
    /// 返回 false：达到最大滚动次数后仍未定位成功。
    /// </summary>
    public async Task<bool> ScrollToElementAsync(
        ILocator locator,
        int maxScrollAttempts = 20,
        float targetViewportRatio = 0.50f,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);

        targetViewportRatio = Math.Clamp(
            targetViewportRatio,
            0.20f,
            0.80f);

        if (maxScrollAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxScrollAttempts));
        }

        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = _profile.ActionTimeoutMs
        });

        int viewportHeight =
            _page.ViewportSize?.Height ??
            await _page.EvaluateAsync<int>(
                "() => window.innerHeight");

        int viewportWidth =
            _page.ViewportSize?.Width ??
            await _page.EvaluateAsync<int>(
                "() => window.innerWidth");

        // 目标最终停留位置，例如 0.50 表示视口纵向中间。
        float desiredY =
            viewportHeight * targetViewportRatio;

        // 目标可接受的安全范围。
        float safeTop =
            viewportHeight * Math.Max(
                0.12f,
                targetViewportRatio - 0.12f);

        float safeBottom =
            viewportHeight * Math.Min(
                0.88f,
                targetViewportRatio + 0.12f);

        double? previousScrollY = null;
        int noMovementCount = 0;

        for (int attempt = 0;
             attempt < maxScrollAttempts;
             attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LocatorBoundingBoxResult? box;

            try
            {
                box = await locator.BoundingBoxAsync();
            }
            catch (PlaywrightException)
            {
                // 页面正在重新渲染，稍后重试。
                await DelayAsync(
                    120,
                    360,
                    cancellationToken);

                continue;
            }

            if (box != null &&
                box.Width > 0 &&
                box.Height > 0)
            {
                float elementCenterX =
                    box.X + box.Width / 2f;

                float elementCenterY =
                    box.Y + box.Height / 2f;

                bool horizontalVisible =
                    elementCenterX >= 0 &&
                    elementCenterX <= viewportWidth;

                bool verticalInSafeArea =
                    elementCenterY >= safeTop &&
                    elementCenterY <= safeBottom;

                if (horizontalVisible &&
                    verticalInSafeArea)
                {
                    // 到达目标后稍微停留。
                    await DelayAsync(
                        180,
                        620,
                        cancellationToken);

                    return true;
                }

                // 元素中心相对于理想位置的距离。
                float difference =
                    elementCenterY - desiredY;

                // 不一次滚完，只移动其中一部分。
                float ratio =
                    RandomFloat(0.56f, 0.82f);

                int scrollDistance =
                    (int)(difference * ratio);

                scrollDistance = Math.Clamp(
                    scrollDistance,
                    -680,
                    680);

                // 避免滚动距离太小，导致反复不动。
                if (Math.Abs(scrollDistance) < 90)
                {
                    scrollDistance =
                        difference >= 0
                            ? _random.NextInt(90, 151)
                            : -_random.NextInt(90, 151);
                }

                await ScrollByAsync(
                    scrollDistance,
                    cancellationToken);
            }
            else
            {
                /*
                 * 元素已存在但 BoundingBox 为 null，可能是：
                 * 1. display:none；
                 * 2. 父元素隐藏；
                 * 3. 虚拟列表还没有真正渲染；
                 * 4. 元素正在重新创建。
                 */

                await ScrollByAsync(
                    _random.NextInt(280, 581),
                    cancellationToken);
            }

            await DelayAsync(
                160,
                520,
                cancellationToken);

            double currentScrollY =
                await _page.EvaluateAsync<double>(
                    "() => window.scrollY");

            if (previousScrollY.HasValue &&
                Math.Abs(currentScrollY -
                         previousScrollY.Value) < 1)
            {
                noMovementCount++;
            }
            else
            {
                noMovementCount = 0;
            }

            previousScrollY = currentScrollY;

            // 连续几次无法移动，说明可能到顶部、底部或页面不可滚动。
            if (noMovementCount >= 3)
            {
                break;
            }
        }

        return false;
    }




    /// <summary>
    /// 等待页面网络和 DOM 相对稳定。
    /// 不强制使用 NetworkIdle，避免持续请求页面永久等待。
    /// </summary>
    private async Task WaitForPageSettledAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await _page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions
                {
                    Timeout = 15_000
                });
        }
        catch (TimeoutException)
        {
            // 页面已经可操作时，不因为少量持续请求终止任务。
        }

        await DelayAsync(300, 950, cancellationToken);
    }

    private async Task PrepareLocatorAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = _profile.ActionTimeoutMs
        });

        await ScrollLocatorIntoNaturalPositionAsync(
            locator,
            cancellationToken);
    }

    /// <summary>
    /// 不直接使用 ScrollIntoView 把元素硬拉到边缘，
    /// 而是分几次滚动到视口中部附近。
    /// </summary>
    private async Task ScrollLocatorIntoNaturalPositionAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        int viewportHeight =
            _page.ViewportSize?.Height ?? 768;

        for (int i = 0;
             i < _profile.MaxTargetScrollAttempts;
             i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            LocatorBoundingBoxResult? box =
                await locator.BoundingBoxAsync();

            if (box == null)
            {
                await locator.ScrollIntoViewIfNeededAsync();

                await DelayAsync(
                    120,
                    320,
                    cancellationToken);

                continue;
            }

            float centerY = box.Y + box.Height / 2f;

            float safeTop = viewportHeight * 0.20f;
            float safeBottom = viewportHeight * 0.78f;

            if (centerY >= safeTop &&
                centerY <= safeBottom)
            {
                return;
            }

            float desiredY = RandomFloat(
                viewportHeight * 0.40f,
                viewportHeight * 0.62f);

            float difference = centerY - desiredY;

            int scrollDistance = (int)Math.Clamp(
                difference * RandomFloat(0.55f, 0.78f),
                -620f,
                620f);

            if (Math.Abs(scrollDistance) < 90)
            {
                scrollDistance =
                    Math.Sign(difference) * 90;
            }

            await ScrollByAsync(
                scrollDistance,
                cancellationToken);

            await DelayAsync(
                120,
                360,
                cancellationToken);
        }

        await locator.ScrollIntoViewIfNeededAsync();
    }

    /// <summary>
    /// 点击位置倾向元素中部，但服从中心附近的正态分布。
    /// </summary>
    private async Task<MouseTarget> GetNaturalTargetPointAsync(
        ILocator locator)
    {
        LocatorBoundingBoxResult? box =
            await locator.BoundingBoxAsync();

        if (box == null)
        {
            throw new InvalidOperationException(
                "无法获取元素坐标。元素可能已从 DOM 移除或不可见。");
        }

        float marginX = Math.Clamp(
            box.Width * 0.13f,
            2f,
            14f);

        float marginY = Math.Clamp(
            box.Height * 0.16f,
            2f,
            11f);

        float left = box.X + marginX;
        float right = box.X + box.Width - marginX;
        float top = box.Y + marginY;
        float bottom = box.Y + box.Height - marginY;

        float centerX = box.X + box.Width / 2f;
        float centerY = box.Y + box.Height / 2f;

        float sigmaX = Math.Max(1f, box.Width * 0.15f);
        float sigmaY = Math.Max(1f, box.Height * 0.14f);

        float targetX = Clamp(
            RandomGaussian(centerX, sigmaX),
            left,
            right);

        float targetY = Clamp(
            RandomGaussian(centerY, sigmaY),
            top,
            bottom);

        return new MouseTarget(
            targetX,
            targetY,
            box.X,
            box.Y,
            box.Width,
            box.Height);
    }

    private async Task MoveMouseAsync(
        float targetX,
        float targetY,
        float targetWidth,
        CancellationToken cancellationToken)
    {
        await EnsureMouseInitializedAsync(
            cancellationToken);

        await MoveMouseSegmentAsync(
            _mouseX,
            _mouseY,
            targetX,
            targetY,
            targetWidth,
            allowOvershoot: true,
            cancellationToken);
    }

    /// <summary>
    /// 最小加加速度曲线 + 贝塞尔偏移。
    ///
    /// Minimum Jerk:
    /// 10t³ - 15t⁴ + 6t⁵
    ///
    /// 它会产生较自然的起步、加速和减速。
    /// </summary>
    private async Task MoveMouseSegmentAsync(
        float startX,
        float startY,
        float targetX,
        float targetY,
        float targetWidth,
        bool allowOvershoot,
        CancellationToken cancellationToken)
    {
        float deltaX = targetX - startX;
        float deltaY = targetY - startY;

        float distance = MathF.Sqrt(
            deltaX * deltaX + deltaY * deltaY);

        if (distance < 1.5f)
        {
            await _page.Mouse.MoveAsync(targetX, targetY);

            _mouseX = targetX;
            _mouseY = targetY;
            return;
        }

        bool shouldOvershoot =
            allowOvershoot &&
            distance > 170f &&
            _random.NextDouble() <
            _profile.MouseOvershootProbability;

        if (shouldOvershoot)
        {
            float overshootDistance = Math.Clamp(
                distance * RandomFloat(0.015f, 0.055f),
                4f,
                18f);

            float unitX = deltaX / distance;
            float unitY = deltaY / distance;

            float perpendicularX = -unitY;
            float perpendicularY = unitX;

            float sideNoise =
                RandomFloat(-3.5f, 3.5f);

            float overshootX =
                targetX +
                unitX * overshootDistance +
                perpendicularX * sideNoise;

            float overshootY =
                targetY +
                unitY * overshootDistance +
                perpendicularY * sideNoise;

            await MoveMouseCurveAsync(
                startX,
                startY,
                overshootX,
                overshootY,
                targetWidth,
                cancellationToken);

            await DelayAsync(20, 90, cancellationToken);

            await MoveMouseCurveAsync(
                overshootX,
                overshootY,
                targetX,
                targetY,
                targetWidth,
                cancellationToken);
        }
        else
        {
            await MoveMouseCurveAsync(
                startX,
                startY,
                targetX,
                targetY,
                targetWidth,
                cancellationToken);
        }

        _mouseX = targetX;
        _mouseY = targetY;
    }

    private async Task MoveMouseCurveAsync(
        float startX,
        float startY,
        float targetX,
        float targetY,
        float targetWidth,
        CancellationToken cancellationToken)
    {
        int viewportWidth =
            _page.ViewportSize?.Width ?? 1366;

        int viewportHeight =
            _page.ViewportSize?.Height ?? 768;

        float deltaX = targetX - startX;
        float deltaY = targetY - startY;

        float distance = MathF.Sqrt(
            deltaX * deltaX + deltaY * deltaY);

        float effectiveWidth =
            Math.Max(targetWidth, 8f);

        // Fitts 定律的简化形式：
        // 移动距离越长、目标越小，耗时越长。
        double indexOfDifficulty =
            Math.Log2(distance / effectiveWidth + 1.0);

        double durationMs =
            85 +
            indexOfDifficulty * 115 +
            distance * 0.12;

        durationMs *= GetCurrentSpeedFactor();
        durationMs *= RandomFloat(0.88f, 1.16f);

        int steps = Math.Clamp(
            (int)(durationMs / RandomFloat(8f, 14f)),
            _profile.MinMouseSteps,
            _profile.MaxMouseSteps);

        float unitX = deltaX / Math.Max(distance, 1f);
        float unitY = deltaY / Math.Max(distance, 1f);

        float perpendicularX = -unitY;
        float perpendicularY = unitX;

        float curveStrength = Math.Min(
            distance * RandomFloat(0.035f, 0.12f),
            72f);

        curveStrength *=
            _random.NextInt(0, 2) == 0 ? -1f : 1f;

        float control1X =
            startX +
            deltaX * RandomFloat(0.22f, 0.38f) +
            perpendicularX * curveStrength;

        float control1Y =
            startY +
            deltaY * RandomFloat(0.22f, 0.38f) +
            perpendicularY * curveStrength;

        float control2X =
            startX +
            deltaX * RandomFloat(0.62f, 0.82f) +
            perpendicularX * curveStrength * 0.52f;

        float control2Y =
            startY +
            deltaY * RandomFloat(0.62f, 0.82f) +
            perpendicularY * curveStrength * 0.52f;

        control1X = Clamp(
            control1X,
            1,
            viewportWidth - 1);

        control1Y = Clamp(
            control1Y,
            1,
            viewportHeight - 1);

        control2X = Clamp(
            control2X,
            1,
            viewportWidth - 1);

        control2Y = Clamp(
            control2Y,
            1,
            viewportHeight - 1);

        int stepDelay = Math.Max(
            1,
            (int)(durationMs / steps));

        for (int i = 1; i <= steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float rawT = i / (float)steps;

            // Minimum Jerk 时间曲线。
            float t = MinimumJerk(rawT);
            float inverseT = 1f - t;

            float x =
                inverseT * inverseT * inverseT * startX +
                3f * inverseT * inverseT * t * control1X +
                3f * inverseT * t * t * control2X +
                t * t * t * targetX;

            float y =
                inverseT * inverseT * inverseT * startY +
                3f * inverseT * inverseT * t * control1Y +
                3f * inverseT * t * t * control2Y +
                t * t * t * targetY;

            // 中途轻微抖动，接近目标时逐渐消失。
            float tremorScale =
                MathF.Sin(rawT * MathF.PI) *
                Math.Min(1.1f, distance / 400f);

            x += RandomGaussian(0f, 0.20f) * tremorScale;
            y += RandomGaussian(0f, 0.18f) * tremorScale;

            x = Clamp(x, 1, viewportWidth - 1);
            y = Clamp(y, 1, viewportHeight - 1);

            await _page.Mouse.MoveAsync(x, y);

            if (i < steps)
            {
                int jitteredDelay = Math.Max(
                    1,
                    stepDelay + _random.NextInt(-2, 3));

                await Task.Delay(
                    jitteredDelay,
                    cancellationToken);
            }
        }

        await _page.Mouse.MoveAsync(targetX, targetY);
    }

    private async Task EnsureMouseInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (_mouseInitialized)
            return;

        int viewportWidth =
            _page.ViewportSize?.Width ?? 1366;

        int viewportHeight =
            _page.ViewportSize?.Height ?? 768;

        _mouseX = RandomFloat(
            viewportWidth * 0.20f,
            viewportWidth * 0.72f);

        _mouseY = RandomFloat(
            viewportHeight * 0.20f,
            viewportHeight * 0.68f);

        await _page.Mouse.MoveAsync(_mouseX, _mouseY);

        await DelayAsync(80, 250, cancellationToken);

        _mouseInitialized = true;
    }

    private async Task MoveMouseToReadingAreaAsync(
        CancellationToken cancellationToken)
    {
        int viewportWidth =
            _page.ViewportSize?.Width ?? 1366;

        int viewportHeight =
            _page.ViewportSize?.Height ?? 768;

        float targetX = RandomFloat(
            viewportWidth * 0.28f,
            viewportWidth * 0.73f);

        float targetY = RandomFloat(
            viewportHeight * 0.30f,
            viewportHeight * 0.72f);

        await MoveMouseAsync(
            targetX,
            targetY,
            180f,
            cancellationToken);

        await DelayAsync(180, 720, cancellationToken);
    }

    private async Task PerformSinglePhysicalClickAsync(
        MouseButton button,
        CancellationToken cancellationToken)
    {
        await _page.Mouse.DownAsync(new MouseDownOptions
        {
            Button = button
        });

        await DelayAsync(
            _profile.MinMouseDownMs,
            _profile.MaxMouseDownMs,
            cancellationToken);

        await _page.Mouse.UpAsync(new MouseUpOptions
        {
            Button = button
        });
    }

    private async Task TypeSingleTextAsync(
        string value,
        CancellationToken cancellationToken)
    {
        int delay = GetTypingDelay(value);

        if (IsBasicKeyboardText(value))
        {
            await _page.Keyboard.TypeAsync(
                value,
                new KeyboardTypeOptions
                {
                    Delay = delay
                });
        }
        else
        {
            // 中文、Emoji、复杂 Unicode 字符。
            await _page.Keyboard.InsertTextAsync(value);

            await Task.Delay(
                delay,
                cancellationToken);
        }
    }

    private int GetTypingDelay(string value)
    {
        int baseDelay = _random.NextInt(
            _profile.MinKeyDelayMs,
            _profile.MaxKeyDelayMs + 1);

        // 大写字符通常会稍慢。
        if (value.Length == 1 &&
            char.IsUpper(value[0]))
        {
            baseDelay += _random.NextInt(20, 65);
        }

        // 数字和符号略慢。
        if (value.Length == 1 &&
            !char.IsLetter(value[0]) &&
            !char.IsWhiteSpace(value[0]))
        {
            baseDelay += _random.NextInt(15, 80);
        }

        return ScaleDelay(baseDelay);
    }

    private double GetAdjustedTypoProbability()
    {
        // 连续操作较多时，错误率略微上升，但保持有限。
        double fatigue =
            Math.Min(_actionCount / 2000.0, 0.008);

        return Math.Min(
            _profile.TypoProbability + fatigue,
            0.04);
    }

    private double GetCurrentSpeedFactor()
    {
        // 操作次数增加后产生轻微疲劳。
        double fatigueFactor =
            1.0 + Math.Min(_actionCount * 0.0012, 0.18);

        // 长时间没有动作，下一步会稍慢一点。
        double idleSeconds =
            (DateTime.UtcNow - _lastActionTime).TotalSeconds;

        double resumeFactor =
            idleSeconds > 8 ? 1.08 : 1.0;

        return _profile.SpeedFactor *
               fatigueFactor *
               resumeFactor;
    }

    private async Task ReadingPauseAsync(
        CancellationToken cancellationToken)
    {
        await DelayAsync(
            _profile.MinReadingDelayMs,
            _profile.MaxReadingDelayMs,
            cancellationToken);
    }

    private async Task DelayAsync(
        int minMilliseconds,
        int maxMilliseconds,
        CancellationToken cancellationToken)
    {
        if (maxMilliseconds < minMilliseconds)
        {
            (minMilliseconds, maxMilliseconds) =
                (maxMilliseconds, minMilliseconds);
        }

        int rawDelay = _random.NextInt(
            minMilliseconds,
            maxMilliseconds + 1);

        await Task.Delay(
            ScaleDelay(rawDelay),
            cancellationToken);
    }

    private int ScaleDelay(int milliseconds)
    {
        return Math.Max(
            1,
            (int)(milliseconds * GetCurrentSpeedFactor()));
    }

    private void RegisterAction()
    {
        _actionCount++;
        _lastActionTime = DateTime.UtcNow;
    }

    private float[] BuildScrollWeights(int count)
    {
        var result = new float[count];

        // 前面加速，中间最大，后面逐渐衰减。
        for (int i = 0; i < count; i++)
        {
            float t = (i + 1f) / (count + 1f);

            float envelope =
                MathF.Sin(t * MathF.PI);

            float noise =
                RandomFloat(0.86f, 1.14f);

            result[i] =
                Math.Max(0.08f, envelope * noise);
        }

        return result;
    }

    private bool CanSimulateTypo(Rune rune)
    {
        return rune.Value <= 127 &&
               char.IsLetter((char)rune.Value);
    }

    private char GetNearbyKeyboardKey(char value)
    {
        bool upper = char.IsUpper(value);
        char lower = char.ToLowerInvariant(value);

        string neighbors = lower switch
        {
            'q' => "wa",
            'w' => "qase",
            'e' => "wsdr",
            'r' => "edft",
            't' => "rfgy",
            'y' => "tghu",
            'u' => "yhji",
            'i' => "ujko",
            'o' => "iklp",
            'p' => "ol",

            'a' => "qwsz",
            's' => "awedxz",
            'd' => "serfcx",
            'f' => "drtgcv",
            'g' => "ftyhbv",
            'h' => "gyujbn",
            'j' => "huiknm",
            'k' => "jiolm",
            'l' => "kop",

            'z' => "asx",
            'x' => "zsdc",
            'c' => "xdfv",
            'v' => "cfgb",
            'b' => "vghn",
            'n' => "bhjm",
            'm' => "njk",

            _ => lower.ToString()
        };

        char result =
            neighbors[_random.NextInt(0, neighbors.Length)];

        return upper
            ? char.ToUpperInvariant(result)
            : result;
    }

    private static bool IsBasicKeyboardText(string value)
    {
        return value.All(c =>
            c >= 32 && c <= 126);
    }

    private static bool IsWordBoundary(Rune rune)
    {
        return rune.Value is ' ' or '\t' or '\n';
    }

    private static bool IsPunctuation(Rune rune)
    {
        string value = rune.ToString();

        return value is
            "." or "," or "!" or "?" or ";" or ":" or
            "。" or "，" or "！" or "？" or "；" or "：";
    }

    private float RandomFloat(
        float minValue,
        float maxValue)
    {
        return minValue +
               (float)_random.NextDouble() *
               (maxValue - minValue);
    }

    /// <summary>
    /// Box-Muller 正态分布。
    /// </summary>
    private float RandomGaussian(
        float mean,
        float standardDeviation)
    {
        double u1 =
            1.0 - _random.NextDouble();

        double u2 =
            1.0 - _random.NextDouble();

        double normal =
            Math.Sqrt(-2.0 * Math.Log(u1)) *
            Math.Sin(2.0 * Math.PI * u2);

        return mean +
               standardDeviation * (float)normal;
    }

    private static float MinimumJerk(float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        float t4 = t3 * t;
        float t5 = t4 * t;

        return 10f * t3 -
               15f * t4 +
               6f * t5;
    }

    private static float Clamp(
        float value,
        float min,
        float max)
    {
        if (max < min)
            return (min + max) / 2f;

        return Math.Clamp(value, min, max);
    }

    private readonly record struct MouseTarget(
        float X,
        float Y,
        float Left,
        float Top,
        float Width,
        float Height)
    {
        public float Right => Left + Width;

        public float Bottom => Top + Height;
    }
}

