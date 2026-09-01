# HumanTouchP2 — 完全重构版

目标不是“随机得更多”，而是让一次触摸的**时间、位置、速度、加速度、抬手速度、触点参数和连续行为状态彼此自洽**。

## 目录

- `Core/`：公共模型、Trace、Request、随机分布工具
- `Profiles/`：用户长期习惯与设备采样特性
- `Behavior/`：Session、疲劳恢复、注意力、状态相关行为选择
- `Motion/`：手势规划、时间运动学、生物力学微扰
- `Cdp/`：`Input.dispatchTouchEvent` 派发与单调时间戳
- `Playwright/`：滚动容器识别、滚动前后验证、元素矩形读取
- `HumanTouchEngine.cs`：单次手势编排
- `HumanTouchOperator.cs`：连续浏览/意图层 API
- `Legacy/`：旧 `HumanSwipeEmulator/HumanSwipeOperator` 的主要 API 兼容外壳

## 最推荐的使用方式

```csharp
var user = HumanUserProfile.CreateRandom(
    seed: accountSeed,
    handedness: HumanHandedness.Right);

string brand = deviceParams.Brand;
string model = deviceParams.Model;

// Windows Chromium + Playwright/CDP 推荐：
var device = TouchDeviceProfiles.ResolveForDesktopCdp(brand, model);
var session = new HumanTouchSession(user, device);

// 也可以一步创建：
// var session = new HumanTouchSession(user, brand, model, desktopCdp: true);

var human = new HumanTouchOperator(new HumanTouchOperatorOptions
{
    Session = session,
    DelayFactor = 1.0,
    AllowBackReview = true
});

await human.BrowseTimesAsync(page, cdp, 3, 8);
await human.SwipeByIntentAsync(page, cdp, SwipeIntent.Reading);
await human.SwipeByIntentAsync(page, cdp, SwipeIntent.Fling);
```

**同一个账号/同一个浏览任务务必复用同一个 `HumanTouchSession`。** 不要每次 Swipe 都 new Session，否则会丢失最重要的长期相关性。


## Brand + Model 设备解析

设备层现在原生支持 `Brand + Model` 双键。解析顺序：

1. 精确 `Brand + Model` 注册 Profile
2. Brand 基线 Profile
3. `GenericAndroid`

当前内置品牌基线：

- Xiaomi（`Redmi` / `POCO` 也归到 Xiaomi 基线）
- Honor
- Samsung
- Huawei
- vivo（`iQOO` 归到 vivo 基线）
- OPPO（`realme` / `OnePlus` 归到 OPPO 基线）

当前是 Windows Chromium + CDP 时建议：

```csharp
var device = TouchDeviceProfiles.ResolveForDesktopCdp(
    deviceParams.Brand,
    deviceParams.Model);
```

如果未来是真 Android 输入链路，则使用：

```csharp
var device = TouchDeviceProfiles.Resolve(
    deviceParams.Brand,
    deviceParams.Model);
```

### 精确型号覆盖

内置 Brand 数字只是工程型保守基线，不应当被视为厂商真实触摸 IC 参数。
当你拿到某个型号的真机统计数据后，可以注册精确型号：

```csharp
TouchDeviceProfiles.RegisterModelProfile(new TouchDeviceProfile
{
    Brand = "Samsung",
    Model = "SM-S9380",
    ProfileId = "samsung-sm-s9380-calibrated",
    Source = TouchDeviceProfileSource.Calibrated,

    MaxTouchPoints = 10,
    MinSamplingHz = 90,
    MaxSamplingHz = 120,
    SamplingJitterRatio = 0.04,
    CoalescedSampleChance = 0.015,
    TimingNoiseMs = 0.4,
    InputLatencyMs = 1.0
});

var profile = TouchDeviceProfiles.ResolveForDesktopCdp(
    "Samsung",
    "SM-S9380");
```

之后 HumanTouch 核心不需要任何改动。

### 直接把现有设备参数传给 Operator

如果你的设备对象本身就有 `Brand` / `Model`，可以不手工创建 `TouchDeviceProfile`：

```csharp
var human = new HumanTouchOperator(new HumanTouchOperatorOptions
{
    UserProfile = HumanUserProfile.CreateRandom(accountSeed),
    Brand = deviceParams.Brand,
    Model = deviceParams.Model,
    UseDesktopCdpDeviceProfile = true
});

Console.WriteLine(human.Session.DeviceProfile.Brand);
Console.WriteLine(human.Session.DeviceProfile.Model);
Console.WriteLine(human.Session.DeviceProfile.Source);
```

如果同时设置了 `Session`，则显式 `Session` 优先，`Brand/Model` 不会重新创建 Session。

## 与旧版最本质的差别

1. 不再以 `Steps + 每点 Random Delay` 作为运动学核心。
2. Fling 以非零 `ReleaseVelocity` 为核心，手指位移不再为了页面滚动距离而撞向屏幕边界。
3. hesitation 是一次完整手势的相位变化：减速 → 短暂低速/停顿 → 再加速。
4. 起点围绕同一用户的长期中心缓慢漂移，不是每次独立 Uniform Random。
5. tremor 使用相关随机过程，低频 drift 与高频 tremor 分离。
6. Force/Radius/Rotation 由用户 Profile 与设备 Profile 共同决定，并在一次手势中连续变化。
7. Fatigue 有短期/长期两层，并按真实空闲时间恢复。
8. 连续浏览意图根据上一行为、连续方向、Attention/Fatigue 改变权重，不是 IID 抽签。

## 关于真实设备校准

当前默认分布是工程型人体模型，并不是某台手机的真实统计数据。要进一步提高设备级一致性，建议采集真机轨迹：

- `timestamp`
- `x/y`
- `pressure`
- `radius/major/minor`（设备能拿到时）

然后拟合：

- sampling interval 分布
- velocity / acceleration / jerk
- release velocity
- 起点/终点二维分布
- curvature
- pressure/contact-area 相关性
- 同一用户连续手势的自相关

这样只需要替换 `HumanUserProfile` / `TouchDeviceProfile` 的参数来源，核心架构不用再改。

## 迁移

完全重构时建议删除旧的 `HumanSwipeEmulator(2).cs` 和 `HumanSwipeOperator(2).cs`，把本目录 `.cs` 文件加入项目。

`Legacy/LegacyCompatibility.cs` 保留了最常用的旧入口：

- `HumanSwipeEmulator.SwipeAsync`
- `BrowseSwipeAsync`
- `SwipeInsideElementAsync`
- `SwipeToElementAsync`
- `SwipeToElementVisibleAsync`
- `HumanSwipeOperator.BrowseOnceAsync/BrowseTimesAsync`
- Reading / Preview / Fling / Micro Up/Down
- LongFling
- MoveToElement / MoveToElementVisible
- 元素左右滑
- CustomAsync

旧版大量 `TimedChaotic*` / 多重回调 overload 没有机械复制；新代码应优先使用 `HumanTouchOperator.BrowseForAsync` 和业务侧 `CancellationToken/stopVerifier` 组合。
