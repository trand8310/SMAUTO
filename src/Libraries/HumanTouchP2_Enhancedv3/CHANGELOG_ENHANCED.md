# Enhanced 版修改清单

## Core
- `HumanSwipeTrace` 增加 PlannedEnd、Hold、TargetReleaseVelocity。
- `GesturePlan` 增加 MotionPeak、SecondarySubmovement、CurvePeak、SecondHarmonic。
- `HumanTouchRequest` 增加 Submovement 控制。
- `RandomMath` 增加 Gaussian、WarpAroundPeak、SkewedEnvelope、Pearson。

## Profiles / Session
- 用户画像 Seed 与 SessionSeed 分离。
- 增加 Session 内 speed/force 慢漂移。
- `TouchDeviceProfile` 增加 SamplingAutocorrelation。
- 校准 Profile 同步支持新参数。

## Motion
- 速度峰位置可变。
- 支持第二 submovement。
- Fling 保留非零抬手速度。
- 几何曲线由非对称 envelope 生成。
- sampling jitter 改为相关过程。
- tremor/drift 改为 dt-aware 相关过程。
- pressure/radius/velocity 建立轻微相关。

## Behavior
- 新增页面上下文快照。
- 自动浏览行为可受文本、媒体、滚动位置影响。

## Diagnostics
- 增加单次轨迹高级统计。
- 增加批量轨迹统计。
