# HumanTouchP2.Enhancedv3

基于当前 HumanTouchP2.Enhanced 源码建立的独立强化项目。程序集为 `HumanTouchP2.Enhancedv3.dll`，保留 `PlaywrightHumanInput` API 命名空间和现有调用签名。MainClient、SMAd 已改为引用此项目，回归测试通过 SMAd 使用 v3。

## v3 首批功能

- Tap 的按压时长使用用户的反应、速度偏好和会话疲劳参数。
- 接触压力先增后减，接触面积随压力阶段变化；使用设备能力和用户触点偏好。同一次 Tap 保持坐标不变。
- 点击位置在可见安全区域内结合惯用手采样，保持原有遮挡与布局检查。
- Tap 与 Swipe 共用 HumanActionRhythm；连续动作之间保留短观察间隔，已过去的等待时间会抵扣该间隔。
- 成功释放触点后才增加 TapCount 并记录 LastTapTrace，其中包含计划时长、实际派发时长、坐标及接触参数。该记录表示输入完成，不表示业务结果成功。

已有业务无需修改 `TapAsync`、`BrowseTimesAsync`、`SwipeAsync` 等调用。如需由上层独立控制全部操作间隔，可设置 `human.Engine.Rhythm.Enabled = false`。

本轮新增功能只放在 v3；原 Enhanced 保留为迁移时的参考版本。原有能力说明保留在下面。

实际内核验证限制：本地 145 无界面 Chromium 在原始 CDP 触摸后也出现脚本调用超时。迁移和模型回归不代表该问题已解决，详情见工作区 docs/SMAd-CDP改进记录.md 第 7 项。

## 继承的 Enhanced 能力

这是在你原始 `HumanTouchP2` 基础上的完整增强版，目标是用于 **PC Chromium + Playwright/CDP 的更自然触屏交互测试**。

这版不是简单“再加随机数”，而是重点修改了长期画像、会话随机、运动学、连续行为、页面上下文和诊断统计。

## 主要改动

### 1. 用户画像 Seed 与 Session Seed 分离

原版：

```csharp
_random = new Random(UserProfile.Seed);
```

如果同一账号重启程序并走同样调用顺序，有机会重复同一随机序列。

现在：

- `HumanUserProfile.Seed`：只决定长期稳定画像，例如惯用手、起点中心、速度偏好、曲率偏好。
- `HumanTouchSession.SessionSeed`：决定本次浏览的随机过程，默认每次新建 Session 都不同。
- 测试时可以显式提供 `sessionSeed` 来完全复现实验。

示例：

```csharp
var user = HumanUserProfile.CreateRandom(accountSeed);

var session = new HumanTouchSession(
    user,
    brand,
    model,
    desktopCdp: true);
```

测试复现：

```csharp
var session = new HumanTouchSession(
    user,
    brand,
    model,
    desktopCdp: true,
    sessionSeed: 123456);
```

---

### 2. 运动学不再共享同一模板

增强后的每个 Gesture 会独立生成：

- `MotionPeakRatio`：主速度峰位置
- `SecondarySubmovement`：可选第二运动单元
- `CurvePeakRatio`：曲率峰位置
- `CurveSecondHarmonic`：轻微二次曲率成分
- hesitation
- pull-back
- release velocity

因此不再是固定：

```text
MinimumJerk + sin(pi*t)
```

只改变随机幅度。

---

### 3. 采样抖动改为相关随机过程

`TouchDeviceProfile` 新增：

```csharp
SamplingAutocorrelation
```

采样 interval 不再每帧独立抽样，而是具有短期相关性。

---

### 4. Tremor / Drift 按真实时间 dt 演化

`BiomechanicsModel` 现在使用带时间常数的相关过程。

这样不同采样率下的扰动频谱不会只是简单换点数。

同时增强了：

- pressure 与 velocity 的轻微相关
- radius 与 pressure 的相关
- rotation 的低频连续变化
- Session 内 force 状态缓慢漂移

---

### 5. 自动浏览加入页面上下文

新增：

- `Behavior/PageContextSnapshot.cs`
- `Playwright/PageContextAnalyzer.cs`

自动浏览时可读取：

- 文本字符量
- 图片数量
- 视频数量
- 链接/按钮/输入框密度
- 当前滚动进度
- 是否接近顶部/底部

并影响：

- Reading
- Preview
- Fling
- FastScan
- MicroAdjust
- BackReview

注意：

```csharp
SwipeByIntentAsync(...)
```

属于显式意图调用，不会被页面上下文修改成别的手势。

---

### 6. TraceAnalyzer 大幅增强

单次轨迹新增指标：

- PathEfficiency
- PeakVelocityPositionRatio
- SamplingIntervalCv
- ReleaseVelocityRelativeError
- Mean/Max lateral deviation
- CurvatureSignChanges
- LateralResidualLag1Autocorrelation
- PressureVelocityCorrelation
- RadiusForceCorrelation
- PlannedEndpointErrorPx

并增加：

```csharp
GestureTraceAnalyzer.AnalyzeBatch(...)
```

用于分析多次手势的统计分布。

---

## 推荐接入方式

```csharp
int accountSeed = StableSeed.Create(dev);

var user = HumanUserProfile.CreateRandom(
    seed: accountSeed,
    handedness: HumanHandedness.Right);

var human = new HumanTouchOperator(new HumanTouchOperatorOptions
{
    UserProfile = user,
    Brand = deviceParams.Brand,
    Model = deviceParams.Model,
    UseDesktopCdpDeviceProfile = true,

    // 生产环境不要固定 SessionSeed。
    SessionSeed = null,

    DelayFactor = 1.0,
    AllowBackReview = true,
    EnablePageContextAwareness = true,
    PageContextRefreshEveryGestures = 1,

    Log = Console.WriteLine
});
```

连续浏览：

```csharp
await human.BrowseTimesAsync(
    page,
    cdp,
    minTimes: 3,
    maxTimes: 8,
    cancellationToken);
```

只向上直到页面不能继续滚：

```csharp
await human.RandomUpUntilStopAsync(
    page,
    cdp,
    minTimes: 2,
    maxTimes: 8,
    cancellationToken);
```

显式阅读型滑动：

```csharp
await human.SwipeByIntentAsync(
    page,
    cdp,
    SwipeIntent.Reading,
    cancellationToken);
```

Fling：

```csharp
await human.SwipeByIntentAsync(
    page,
    cdp,
    SwipeIntent.Fling,
    cancellationToken);
```

---

## 诊断示例

```csharp
var traces = await human.BrowseTimesAsync(page, cdp, 5, 10);

foreach (var trace in traces)
{
    var m = GestureTraceAnalyzer.Analyze(trace);

    Console.WriteLine(
        $"Intent={trace.Intent}, " +
        $"Duration={m.DurationMs:0}ms, " +
        $"PeakAt={m.PeakVelocityPositionRatio:P0}, " +
        $"Efficiency={m.PathEfficiency:0.000}, " +
        $"IntervalCV={m.SamplingIntervalCv:0.000}, " +
        $"LateralLag1={m.LateralResidualLag1Autocorrelation:0.000}");
}

var batch = GestureTraceAnalyzer.AnalyzeBatch(traces);

Console.WriteLine(
    $"Count={batch.Gestures}, " +
    $"PeakAt={batch.MeanPeakVelocityPositionRatio:P0}, " +
    $"StartXStd={batch.StartXStdPx:0.0}px");
```

---

## 是否需要修改原来的业务调用

一般不需要。

以下入口全部保留：

- `HumanTouchOperator`
- `HumanTouchEngine`
- `BrowseOnceAsync`
- `BrowseTimesAsync`
- `BrowseForAsync`
- `RandomUpUntilStopAsync`
- `SwipeByIntentAsync`
- `MoveToElementAsync`
- `MoveToElementVisibleAsync`
- `SwipeElementLeftAsync`
- `SwipeElementRightAsync`
- `LegacyCompatibility.cs`

所以原项目通常只需要把本目录源码替换进去。

---

## 一个重要建议

同一账号的一次浏览任务应该始终复用同一个：

```csharp
HumanTouchSession
```

不要每次 Swipe 都：

```csharp
new HumanTouchSession(...)
```

否则 Session 中的：

- fatigue
- attention
- preferred touch region
- speed drift
- force drift
- previous intent
- consecutive direction

都会丢失。

---

## 设备参数说明

内置 `Xiaomi / Samsung / Honor / Huawei / vivo / OPPO` Profile 仍属于工程型近似值，不代表对应厂商真实 Touch IC 参数。

如果后续你有真机采集数据，优先校准：

- sampling interval distribution
- sampling interval autocorrelation
- velocity profile
- peak velocity position
- release velocity
- lateral deviation distribution
- pressure / velocity correlation
- contact area / pressure correlation

然后通过：

```csharp
TouchDeviceProfiles.RegisterModelProfile(...)
```

注册精确型号。
