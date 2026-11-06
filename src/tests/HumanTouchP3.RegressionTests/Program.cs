using HumanTouchP3;
using HumanTouchP3.Input;
using Microsoft.Playwright;
using PWTest;

if (args.Length == 0) throw new ArgumentException("传入 Chromium 路径");
// Compare strength distributions with matching seeds; actual page inertia is checked separately.
var planner = new GesturePlanner();
var softPlans = new List<GesturePlan>(); var strongPlans = new List<GesturePlan>();
for (int i = 1; i <= 60; i++)
{
    var user = new HumanUserProfile();
    softPlans.Add(planner.Plan(new HumanTouchSession(user, null, i), 390, 844, new HumanTouchRequest { Intent = SwipeIntent.Fling, FlingStrength = FlingStrength.Soft }));
    strongPlans.Add(planner.Plan(new HumanTouchSession(user, null, i), 390, 844, new HumanTouchRequest { Intent = SwipeIntent.Fling, FlingStrength = FlingStrength.Strong }));
}
if (strongPlans.Average(x => x.ReleaseVelocityPxPerSecond) <= softPlans.Average(x => x.ReleaseVelocityPxPerSecond) ||
    strongPlans.Average(x => x.DurationMs) >= softPlans.Average(x => x.DurationMs))
    throw new Exception("轻甩与强甩的计划速度或时长分布不符合预期");
Console.WriteLine("PASS 轻甩和强甩的速度、时长分布存在差异");
// Boundaries and directional trends from aggregate measurements, not raw personal traces.
foreach (var posture in Enum.GetValues<HandPosture>())
{
    var profile = PostureProfiles.Create(posture);
    foreach (var direction in new[] { HumanSwipeDirection.Up, HumanSwipeDirection.Down })
    {
        var plans = Enumerable.Range(1, 300).Select(seed => planner.Plan(
            new HumanTouchSession(profile, null, seed), 411, 721,
            new HumanTouchRequest { Direction = direction, Intent = SwipeIntent.Fling, FlingStrength = FlingStrength.Normal })).ToArray();
        if (plans.Any(x => x.Start.X < 24 || x.Start.X > 387 || x.Start.Y < 24 || x.Start.Y > 697 || x.End.X < 24 || x.End.X > 387 || x.End.Y < 24 || x.End.Y > 697))
            throw new Exception($"{posture}/{direction} 越过安全边距");
        double expectedX = direction == HumanSwipeDirection.Up ? profile.VerticalUpStartXRatio!.Value : profile.VerticalDownStartXRatio!.Value;
        double expectedY = direction == HumanSwipeDirection.Up ? profile.VerticalUpStartYRatio!.Value : profile.VerticalDownStartYRatio!.Value;
        if (Math.Abs(plans.Average(x => x.Start.X / 411) - expectedX) > .08 || Math.Abs(plans.Average(x => x.Start.Y / 721) - expectedY) > .06)
            throw new Exception($"{posture}/{direction} 起点分布偏离校准范围");
        double drift = plans.Average(x => x.End.X - x.Start.X);
        if ((posture == HandPosture.RightHandOneHanded && (direction == HumanSwipeDirection.Up ? drift < 5 : drift > -5)) ||
            (posture == HandPosture.LeftHandOneHanded && (direction == HumanSwipeDirection.Up ? drift > -5 : drift < 5)))
            throw new Exception($"{posture}/{direction} 横向趋势不符合样本");
        if (plans.Select(x => Math.Round(x.Start.X)).Distinct().Count() < 20) throw new Exception("起点缺少变化");
        foreach (var (plan, seed) in plans.Take(40).Select((x, i) => (x, i + 1)))
        {
            var session = new HumanTouchSession(profile, null, seed);
            var points = new BiomechanicsModel().Apply(session, plan, new KinematicsEngine().GenerateBaseTrajectory(session, plan));
            if (points.Any(x => x.Point.X < 24 || x.Point.X > 387 || x.Point.Y < 24 || x.Point.Y > 697))
                throw new Exception($"{posture}/{direction} 实际触点越过安全边距");
        }
    }
}
Console.WriteLine("PASS 三种姿势双方向起点分布、横向趋势和视口安全边距");
using var server = new FixtureServer(); server.Start();
using var pw = await Playwright.CreateAsync();
if (!args.Contains("--custom-only"))
{
await using var browser = await pw.Chromium.LaunchAsync(new() { ExecutablePath = Path.GetFullPath(args[0]), Headless = true });
await using var context = await browser.NewContextAsync(new() { IsMobile = true, HasTouch = true, ViewportSize = new() { Width = 390, Height = 844 } });
var page = await context.NewPageAsync(); var cdp = await context.NewCDPSessionAsync(page);
// A non-scrollable page still consumes the requested reading interval.
var staticPage = await context.NewPageAsync();
await staticPage.GotoAsync("data:text/html,%3C!doctype%20html%3E%3Cbody%3Estatic%20reading%3C/body%3E", new() { Timeout = 10000 });
var staticCdp = await context.NewCDPSessionAsync(staticPage);
var staticSession = new TouchSession(HandPosture.RightHandOneHanded, 51);
var readClock = System.Diagnostics.Stopwatch.StartNew();
var staticTraces = await staticSession.BrowseAsync(staticPage, staticCdp, BrowseMode.Reading, TimeSpan.FromMilliseconds(550)).WaitAsync(TimeSpan.FromSeconds(5));
if (staticTraces.Count != 0 || readClock.Elapsed < TimeSpan.FromMilliseconds(450)) throw new Exception("静态页面应继续停留阅读");
await staticPage.CloseAsync();
Console.WriteLine("PASS 不可滚动时静态阅读剩余时间");
// Controlled fixture: container input must not be mistaken for a document scroll.
await page.GotoAsync($"http://127.0.0.1:{server.Port}/");
await page.Locator("#showInner").CheckAsync();
var readingSession = new TouchSession(HandPosture.RightHandOneHanded, 4321);
var beforeInner = await page.EvaluateAsync<double>("() => document.querySelector('#inner').scrollTop");
var beforeDoc = await page.EvaluateAsync<double>("() => scrollY");
var innerTrace = await readingSession.ScrollContainerAsync(page, cdp, page.Locator("#inner"), true).WaitAsync(TimeSpan.FromSeconds(20));
var afterInner = await page.EvaluateAsync<double>("() => document.querySelector('#inner').scrollTop");
var afterDoc = await page.EvaluateAsync<double>("() => scrollY");
if (innerTrace == null || afterInner <= beforeInner || Math.Abs(afterDoc - beforeDoc) > 1)
    throw new Exception("容器滑动没有正确限定在内层滚动区域");
var noContainer = await readingSession.ScrollContainerAsync(page, cdp, page.Locator("article").First, true).WaitAsync(TimeSpan.FromSeconds(10));
if (noContainer != null) throw new Exception("非滚动容器不能回退为外层滚动");
var innerBox = await page.Locator("#inner").BoundingBoxAsync() ?? throw new Exception("无内部容器");
var observation = await readingSession.ObserveScrollAsync(page, cdp, innerBox.X + innerBox.Width / 2, innerBox.Y + innerBox.Height / 2, TimeSpan.FromSeconds(2));
if (observation.TargetKind != "element" || !observation.Settled) throw new Exception("未正确观察内层容器稳定状态");
var inactive = await readingSession.StopInertiaAsync(page, cdp, innerBox.X + innerBox.Width / 2, innerBox.Y + innerBox.Height / 2, page.Locator("#stopPad"));
if (inactive.InputDispatched) throw new Exception("静止时不能派发制动触摸");
using (var cancelled = new CancellationTokenSource(100))
{
    try { await readingSession.PauseReadingAsync(page, cdp, TimeSpan.FromSeconds(5), cancelled.Token); throw new Exception("阅读停留未取消"); }
    catch (OperationCanceledException) when (cancelled.IsCancellationRequested) { }
}
await page.ReloadAsync();
var docTrace = await readingSession.SwipeAsync(page, cdp, true, BrowseMode.Reading).WaitAsync(TimeSpan.FromSeconds(20));
if (docTrace == null) throw new Exception("文档滑动未完成");
var fling = await readingSession.FlingForObservationAsync(page, cdp, FlingStrength.Strong).WaitAsync(TimeSpan.FromSeconds(20));
if (fling == null) throw new Exception("惯性观察用甩动未派发");
var stop = await readingSession.StopInertiaAsync(page, cdp, fling.StartX, fling.StartY, page.Locator("#stopPad")).WaitAsync(TimeSpan.FromSeconds(10));
if (stop.InputDispatched && stop.After == null) throw new Exception("制动触摸后缺少观察结果");
if (!stop.Before.MovementObserved && stop.InputDispatched) throw new Exception("未检测到运动却派发了制动触摸");
Console.WriteLine($"惯性制动夹具: 移动={stop.Before.MovementObserved}, 触摸已派发={stop.InputDispatched}, 观察稳定={stop.After?.Settled}, 原因={stop.Reason}");
var unsafeFling = await readingSession.FlingForObservationAsync(page, cdp, FlingStrength.Strong).WaitAsync(TimeSpan.FromSeconds(20));
if (unsafeFling != null)
{
    var rejected = await readingSession.StopInertiaAsync(page, cdp, unsafeFling.StartX, unsafeFling.StartY, page.Locator("#target")).WaitAsync(TimeSpan.FromSeconds(10));
    if (rejected.InputDispatched) throw new Exception("禁止使用交互按钮作为制动区域");
}

var micro = await readingSession.MicroAdjustAsync(page, cdp, true).WaitAsync(TimeSpan.FromSeconds(20));
if (micro == null || micro.Intent != SwipeIntent.MicroAdjust) throw new Exception("微调未派发或无滚动效果");
var back = await readingSession.BackReviewAsync(page, cdp).WaitAsync(TimeSpan.FromSeconds(20));
if (back == null || back.Direction != HumanSwipeDirection.Down || back.Intent != SwipeIntent.BackReview) throw new Exception("回看未向前文滚动");
Console.WriteLine("PASS 阅读取消、微调、回看、容器隔离与静止不制动");
var readingSeed = Enumerable.Range(1, 1000).First(seed => { var r = new Random(seed); r.Next(0, 3); var choice = r.Next(100); return choice >= 6 && choice < 22; });
var continuous = new TouchSession(HandPosture.RightHandOneHanded, readingSeed);
var readingActions = await continuous.BrowseAsync(page, cdp, BrowseMode.Reading, TimeSpan.FromSeconds(9)).WaitAsync(TimeSpan.FromSeconds(15));
if (!readingActions.Any(t => t.Intent == SwipeIntent.MicroAdjust)) throw new Exception("连续阅读未执行预期的微调");
Console.WriteLine("PASS 连续阅读中按会话随机选择微调并受时间预算约束");
foreach (var posture in Enum.GetValues<HandPosture>())
{
    await page.GotoAsync($"http://127.0.0.1:{server.Port}/");
    var mode = posture switch { HandPosture.LeftHandOneHanded => "left", HandPosture.LeftHandHoldRightIndex => "index", _ => "right" };
    await page.EvaluateAsync("mode => window.diag.setPosture(mode)", mode);
    var overlay = await page.EvaluateAsync<bool>("mode => { const svg = document.querySelector('#postureOverlay'); const visible = getComputedStyle(svg.querySelector('.zone.' + mode)).display !== 'none'; return svg.dataset.mode === mode && visible && getComputedStyle(svg).pointerEvents === 'none' && document.elementFromPoint(innerWidth / 2, innerHeight * .75) !== svg; }", mode);
    if (!overlay) throw new Exception($"{posture} 姿势示意不可见或阻挡输入");
    if (Environment.GetEnvironmentVariable("PWTEST_CAPTURE") is { Length: > 0 } captureDirectory)
    {
        Directory.CreateDirectory(captureDirectory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(captureDirectory, mode + ".png") });
    }
    await page.EvaluateAsync("() => window.diag.start('测试')");
    var session = new TouchSession(posture, 123);
    await page.EvaluateAsync("() => window.diag.markAction({name:'第一步', posture:'right', phase:'begin'})");
    var chosenStrength = posture == HandPosture.RightHandOneHanded ? FlingStrength.Soft : FlingStrength.Strong;
    var trace = await session.SwipeAsync(page, cdp, true, BrowseMode.Fast, default, chosenStrength).WaitAsync(TimeSpan.FromSeconds(20));
    if (trace?.FlingStrength != chosenStrength) throw new Exception("轨迹未记录所选甩动强度");
    await page.EvaluateAsync("() => window.diag.markAction({name:'第一步', posture:'right', phase:'end'})");
    if (posture == HandPosture.RightHandOneHanded)
    {
        await page.EvaluateAsync("() => window.diag.markAction({name:'第二步', posture:'right', phase:'begin'})");
        var secondTrace = await session.SwipeAsync(page, cdp, true, BrowseMode.Reading).WaitAsync(TimeSpan.FromSeconds(20));
        if (secondTrace is null) throw new Exception("第二次滑动未执行");
        await page.EvaluateAsync("() => window.diag.markAction({name:'第二步', posture:'right', phase:'end'})");
    }
    await Task.Delay(2500);
    var traceVisible = await page.EvaluateAsync<bool>("() => { const c = document.querySelector('#touchTrace'); const pixels = c.getContext('2d').getImageData(0, 0, c.width, c.height).data; return getComputedStyle(c).pointerEvents === 'none' && Array.from(pixels).some((value, index) => index % 4 === 3 && value > 0); }");
    if (!traceVisible) throw new Exception($"{posture} 触摸轨迹未绘制或阻挡输入");
    if (Environment.GetEnvironmentVariable("PWTEST_CAPTURE") is { Length: > 0 } traceDirectory)
        await page.ScreenshotAsync(new() { Path = Path.Combine(traceDirectory, mode + "-trace.png") });
    var result = await page.EvaluateAsync<System.Text.Json.JsonElement>("() => window.diag.stop()").WaitAsync(TimeSpan.FromSeconds(10));
    if (Environment.GetEnvironmentVariable("PWTEST_RESULT_DIR") is { Length: > 0 } resultDirectory)
    {
        Directory.CreateDirectory(resultDirectory);
        await File.WriteAllTextAsync(Path.Combine(resultDirectory, mode + ".json"), result.GetRawText());
    }
    if (result.GetProperty("posture").GetString() != mode) throw new Exception("采集结果未记录姿势示意");
    var events = result.GetProperty("events").EnumerateArray().ToArray();
    if (posture == HandPosture.RightHandOneHanded && (events.Count(e => e.GetProperty("type").GetString() == "touchstart") < 2 || events.Count(e => e.GetProperty("type").GetString() == "action") != 4))
        throw new Exception("连续采集未保留两次动作和标记");
    if (trace is null || !trace.ScrollChanged || !events.Any(e => e.GetProperty("type").GetString() == "touchend") || !events.Any(e => e.GetProperty("type").GetString() == "scroll"))
        throw new Exception($"{posture} 触摸或滚动未得到验证");
    await page.EvaluateAsync("() => document.querySelector('#clearTrace').click()");
    var cleared = await page.EvaluateAsync<bool>("() => { const c = document.querySelector('#touchTrace'); const pixels = c.getContext('2d').getImageData(0, 0, c.width, c.height).data; return !Array.from(pixels).some((value, index) => index % 4 === 3 && value > 0); }");
    if (!cleared) throw new Exception("触摸轨迹未清除");
    Console.WriteLine($"PASS {posture}: 触摸、滚动、轨迹绘制和清除，起点 {trace.StartX:F0}, {trace.StartY:F0}, 事件 {events.Length}");
}
}
var testProfile = Path.Combine(Path.GetTempPath(), "PWTest-Regression", Guid.NewGuid().ToString("N"));
var customArgs = LaunchConfiguration.Parse(LaunchConfiguration.DefaultArguments, testProfile);
var pathArgs = LaunchConfiguration.Parse("--log-file=\"%PROFILE%\\125.log\"", testProfile);
if (!customArgs.Contains("--webgl-vendor=Google Inc. (Qualcomm)") || !customArgs.Contains("--window-size=428,915") ||
    !pathArgs.Contains("--log-file=" + Path.Combine(testProfile, "125.log")))
    throw new Exception("启动参数引号或用户目录解析错误");
var batchArgs = LaunchConfiguration.Parse("--one=1 ^\r\n--two=\"a b\"", testProfile);
if (batchArgs.Length != 2 || batchArgs[1] != "--two=a b") throw new Exception("批处理续行解析错误");
try { LaunchConfiguration.Parse("--user-data-dir=wrong", testProfile); throw new Exception("重复用户目录未被拒绝"); }
catch (ArgumentException) { }
Console.WriteLine("PASS 启动参数、引号、续行和用户目录隔离");
await using (var customContext = await pw.Chromium.LaunchPersistentContextAsync(testProfile, new() {
    ExecutablePath = Path.GetFullPath(args[0]), Headless = true, Args = customArgs, ViewportSize = ViewportSize.NoViewport
}))
{
    var customPage = customContext.Pages.FirstOrDefault() ?? await customContext.NewPageAsync();
    await customPage.GotoAsync($"http://127.0.0.1:{server.Port}/");
    var actual = await customPage.EvaluateAsync<string>("() => JSON.stringify({innerWidth,innerHeight,screenWidth:screen.width,screenHeight:screen.height,dpr:devicePixelRatio})");
    var customCdp = await customContext.NewCDPSessionAsync(customPage);
    await customPage.EvaluateAsync("() => window.diag.start('自定义参数')");
    var customTrace = await new TouchSession(HandPosture.RightHandOneHanded, 123).SwipeAsync(customPage, customCdp, true, BrowseMode.Fast).WaitAsync(TimeSpan.FromSeconds(20));
    if (customTrace == null || !customTrace.ScrollChanged) throw new Exception("自定义参数下触屏或滚动未验证");
    Console.WriteLine("PASS 自定义参数启动、本地页面与触屏；实际屏幕/视口: " + actual);
}
Console.WriteLine("HumanTouchP3 实际浏览器检查通过");
