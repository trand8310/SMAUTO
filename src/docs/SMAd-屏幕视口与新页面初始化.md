# SMAd 屏幕、视口和新页面初始化

本轮将手机屏幕、可用屏幕、网页视口及 Windows 外部窗口分别配置。所有页面复用任务创建时确定的同一份 `DeviceDisplayProfile`，不再根据页面重新抽取栏位高度。

当前实现已移除 SMAd 中两处 CDP 尺寸覆盖调用。屏幕、可用区域、DPR、视口及移动布局由启动参数和定制 Chromium 原生实现提供；CDP 仍用于触摸配置、目标管理、窗口调整和只读校验，不包含 JavaScript 属性覆盖。原生屏幕、可用屏幕或 DPR 与档案不符时，初始化明确失败。JS 仅用于读取校验值。

## 尺寸与单位

| 值 | 单位及来源 |
| --- | --- |
| `dev.sw/sh` | 原有输入：设备物理分辨率，继续交给设备匹配器 |
| `dev.dpr` | 可选明确 DPR；提供时按物理分辨率除以 DPR 并四舍五入生成 CSS 屏幕尺寸 |
| `dev.cssWidth/cssHeight` | 可选明确 CSS 屏幕尺寸，优先于匹配或物理分辨率换算 |
| `screen.width/height` | CSS 像素，通过 `--screen-size` 和定制内核设置 |
| `dev.availWidth/availHeight` | CSS 像素，默认等于完整屏幕；可以按目标设备档案覆盖，不能扣浏览器工具栏 |
| `dev.viewportWidth/viewportHeight` | CSS 像素；保留配置计算，当前不再通过 CDP 应用，也未新增视口启动参数，需由内核实现实际视口 |
| `dev.windowWidth/windowHeight` | Windows DIP 外部窗口尺寸，与 DPR、手机物理像素分开 |

Android 默认状态栏 24、导航栏 24、Chrome 工具栏 56 CSS 像素。这是固定测试档案，不保证对应任意真机。UA 包含 `; wv` 的 WebView 默认工具栏为 0。可用 `dev.statusBarHeight/navigationBarHeight/browserToolbarHeight` 明确覆盖；全屏模式可设为 0。桌面默认不扣 Android 栏位。

Windows 外部窗口默认宽度 `max(500, CSS屏幕宽度 + 32)`、高度 `CSS屏幕高度 + 128`，用于显示和调试。它不会绘制 Android 系统栏。连接后通过 `Browser.setWindowBounds` 设置，并回读实际边界、等待更新、有限校正边框差值。如果 Windows 限制窗口大小，以日志记录的实际值为准；网页视口由内核和实际内容区布局决定，不再通过 CDP 独立控制。

示例字段可合并到原有任务的 `dev` 中，UA 等原有字段仍需保留：

```json
{
  "dev": {
    "sw": 1080,
    "sh": 2400,
    "dpr": 2.625,
    "cssWidth": 412,
    "cssHeight": 915,
    "availWidth": 412,
    "availHeight": 867,
    "viewportWidth": 412,
    "viewportHeight": 811,
    "windowWidth": 640,
    "windowHeight": 1040
  }
}
```

不提供新增字段时使用现有匹配器的屏幕/DPR，配合固定栏位默认值。旧的 `AndroidBrowserUiMatcher` 不再用于计算本流程尺寸。启动参数中的 DPR 固定使用 `InvariantCulture`，避免逗号小数造成参数失效。

## 设置顺序与新页面

1. 先以 `about:blank` 启动自有浏览器，继续使用自动分配的 CDP 端口。
2. 在业务导航前，创建独立的扁平 CDP WebSocket 控制器。
3. 控制器调用 `Target.setAutoAttach`，仅附加 `page` 目标，并请求 `waitForDebuggerOnStart`。附加后仅配置触屏能力、启用 Page 域，再调用 `Runtime.runIfWaitingForDebugger`。
4. 主页面、新标签页、新窗口进入已有 `InitPageAsync` 去重流程；操作前等待目标初始化，再通过页面 CDP 会话设置实际窗口、只读校验原生屏幕与 DPR、绑定 v3 触屏。
5. 同一页面导航的设备指标由内核提供。原有的下载、崩溃和请求失败监听继续只注册一次。
6. 初始化通道失败会停止所属浏览器任务；任务回收时关闭独立通道，清理附加状态和待完成命令，然后继续清理页面 CDP 会话。一个清理环节失败不会跳过另一个环节。

### 原生可用屏幕的支持要求

SMAd 不再调用 `Emulation.setDeviceMetricsOverride`。继续传入 `--screen-size`、`--screen-avail-size`、`--device-pixel-ratio`、`--screen-color-depth` 和 `--window-size`；定制内核需要真正实现这些参数。没有任何 JS 回退。

Chromium 上游 `ScreenMetricsEmulator::Apply` 中将 `emulated_screen_info.available_rect` 赋值为 `screen_rect`。这意味着仅修改原始屏幕信息还可能被 CDP 模拟覆盖。需要根据定制内核版本，在浏览器配置传递和渲染器设备模拟两条路径保留同一份可用区域。原生数据应在创建渲染页面时已经生效，新标签页、新窗口和跨进程导航继承同一设备档案。

自动附加采用单独的 `ClientWebSocket`，因为当前 Patchright 的 `ICDPSession.SendAsync` 不提供消息顶层 `sessionId`，而 Chromium 135 浏览器级自动附加只接受扁平协议。该通道由任务持有，复用已分配的 WebSocket 地址，不增加调试端口。

网页缺少移动 viewport 标签或主动缩放时，布局视口、`innerWidth` 和 `visualViewport` 会受到页面缩放影响。不能通过强制它们相等来判断模拟正确，也没有强制改写业务页面的 viewport 标签。页面动作仍使用现有 v3 定位与输入流程。

## 验证

`tests/DeviceDisplay.RegressionTests` 使用本地 HTTP 页面，其首个脚本立即保存屏幕、可用屏幕、DPR 和 visualViewport。覆盖主页面、`window.open` 新标签页、独立新窗口、同标签页再次导航、主动 `NewPageAsync`，并核验实际外部窗口。

测试命令：

```powershell
dotnet run --project tests/DeviceDisplay.RegressionTests -- <本地chrome.exe>
dotnet run --project tests/DeviceDisplay.RegressionTests -- <本地chrome.exe> --headed
```

2026-10-09 早期含 JS 回退版本曾在 135 无头、135 Windows 有窗口、145 无头三种模式验证 `412×867` 可用区域；对应旧报告保留为历史记录，不能证明内核原生支持该值。

历史验证（已移除 JS 回退，但当时仍启用 CDP 尺寸覆盖；不能作为本次删除尺寸覆盖后的验证）：135 无头模式通过验证：屏幕及可用屏幕均为 `412×915`，网页视口 `412×800`，DPR `2.625`，实际外部窗口 `640×1040` DIP。另验证要求可用高度 `867` 时明确拒绝配置，页面原生属性未被改写。新的报告带有 `NativeOnly=true`。

135 移动视口下的 v3 真实 Tap、遮挡拒绝、并发触摸顺序和下载回归也通过；145 本轮只验证尺寸设置，不代表之前的触屏兼容性问题已解决。

参考：[CDP Emulation](https://chromedevtools.github.io/devtools-protocol/tot/Emulation/)、[CDP Target](https://chromedevtools.github.io/devtools-protocol/tot/Target/)、[Chromium 屏幕模拟代码](https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/core/frame/screen_metrics_emulator.cc)、[Blink Screen 原生读取](https://github.com/chromium/chromium/blob/main/third_party/blink/renderer/core/frame/screen.cc)。当前工作区未发现 Chromium 源码，因此尚未制作或编译内核补丁。

