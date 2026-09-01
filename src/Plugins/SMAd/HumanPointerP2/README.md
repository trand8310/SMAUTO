# HumanPointerP2

PC 输入实现使用 Playwright `IPage.Mouse` 派发事件，并由本模块生成动作轨迹和节奏。

## 已实现

- 同一页面内持续维护鼠标位置
- Fitts 定律估算移动时间
- Minimum-jerk 时间曲线
- 三次 Bézier 空间轨迹
- 相关微扰和低概率越界修正
- 元素可见区域内的非固定点击点
- Hover、按下、抬起和点击后停顿
- Reading、Preview、FastScan、MicroAdjust、BackReview 滚轮意图
- 点击前 `elementFromPoint` 命中检查

## 在 SMAd 中的选择规则

- `os == 1 || os == 2`：使用 `HumanTouchP2`
- 其他 `os`：使用 `HumanPointerP2`

业务代码统一通过 `WorkerRunContext.Human` 调用，不应在同一流程中绕过 Operator
直接派发鼠标移动，否则 PointerSession 记录的位置可能与 Playwright 当前鼠标位置不一致。

## 独立创建

```csharp
var input = HumanInputFactory.Create(
    os: 0,
    seed: stableSeed,
    brand: null,
    model: null);

await input.BrowseForAsync(page, cdp, TimeSpan.FromSeconds(8), token);
await input.MoveToElementAsync(page, cdp, locator, 10, token);
await input.ClickAsync(page, cdp, locator, token);
```
