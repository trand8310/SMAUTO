# Chromium 145.0.7632.110 原生视口与移动布局定位

源码基线为用户提供的 `refs/tags/145.0.7632.110`。Gitiles 页面无法由当前浏览工具读取，实际核对使用官方 `chromium/chromium` GitHub 镜像的同名标签，并将关键文件原样保存到 `artifacts/chromium-145.0.7632.110/`。下列行号对应这些标签文件，不是 main 分支。

## 已确认的入口

| 责任 | 文件 | 函数/行号 |
| --- | --- | --- |
| 页面偏好统一配置 | `chrome/browser/chrome_content_browser_client.cc` | `ChromeContentBrowserClient::OverrideWebPreferences()`，4460 |
| 移动布局字段 | `third_party/blink/public/common/web_preferences/web_preferences.h` | viewport 字段 159 起，文本自动调整 245，页面缩放范围 313 起 |
| 原生移动模式行为参考 | `third_party/blink/renderer/core/inspector/dev_tools_emulator.cc` | `DevToolsEmulator::EnableMobileEmulation()`，403 |
| 新主框架渲染视图尺寸 | `content/browser/web_contents/web_contents_impl.cc` | `CreateRenderWidgetHostViewForRenderManager()`，10521；`GetSizeForMainFrame()`，10841 |
| 已有设备模拟内容区域尺寸机制 | 同上 | `SetDeviceEmulationSize()`，11293 |
| 首次及后续视觉参数发送 | `content/browser/renderer_host/render_widget_host_impl.cc` | `GetVisualProperties()`，1040 |
| 渲染器原生设备模拟入口 | `third_party/blink/renderer/core/frame/web_frame_widget_impl.cc` | `EnableDeviceEmulation()`，2299 |
| 屏幕、可视区域及 Widget 尺寸应用 | 同上 | `SetScreenInfoAndSize()`，4743 |
| 可用区域被设备模拟覆盖的位置 | `third_party/blink/renderer/core/frame/screen_metrics_emulator.cc` | `available_rect = screen_rect`，168 |

## 移动布局建议

在 `OverrideWebPreferences()` 中，由统一设备档案开关控制以下配置。放在不会再被其他偏好覆盖的位置，并且新页面创建前已经可用。不要修改全部 Windows 页面的默认值。

```cpp
// 仅示意设置字段；启用条件、设备档案存储和作用域还需接入定制内核。
web_prefs->viewport_enabled = true;
web_prefs->viewport_meta_enabled = true;
web_prefs->viewport_style = blink::mojom::ViewportStyle::kMobile;
web_prefs->shrinks_viewport_contents_to_fit = true;
web_prefs->text_autosizing_enabled = true;
web_prefs->main_frame_resizes_are_orientation_changes = true;
web_prefs->default_minimum_page_scale_factor = 0.25f;
web_prefs->default_maximum_page_scale_factor = 5.f;
```

这些字段已在 145 标签中核实，仍需根据 Chrome/WebView 的目标行为选择具体配置。145 使用 `text_autosizing_enabled`，不能照搬当前 main 中 `SetTextSizeAdjustEnabled` 的实现。

`EnableMobileEmulation()` 同时设置 Android overlay scrollbar、LCD 文本偏好、ZoomFactorOverride 和页面默认缩放范围，并更新 visual viewport 滚动条。上述 WebPreferences 字段不能单独代表完整 `mobile=true`；其余行为需要沿 Blink 页面初始化/偏好应用链路实现。DevToolsEmulator 是参考实现，不建议让应用仍等待 DevTools 客户端连接才启用。

145 的 `WebContentsImpl` 会在某些 UA override 情况下将 Windows 的 `viewport_meta_enabled` 设为 false。应在统一偏好配置入口重新确立原生设备模式，并检查导航后偏好更新不会将其撤销。`OverrideWebPreferences()` 调用位于该文件 3904 行。

## 网页视口建议

`GetSizeForMainFrame()` 当前先读取 delegate 的新视图尺寸，再读取 `device_emulation_size_`，最后回退到 Windows 容器尺寸。创建主框架 Widget 时会调用该函数并 `SetSize()`；部分渲染视图切换路径也会重新设置尺寸。

定制设备档案应在 WebContents 创建时确定，将固定模拟内容区域纳入初始尺寸来源，覆盖新标签页、新窗口和跨进程导航。还必须处理后续 Windows 原生窗口 resize，不应只修改这个 getter 或创建时单次调用 `SetSize()`。

`RenderWidgetHostImpl::GetVisualProperties()` 从 view 收集 `new_size_device_px`、`visible_viewport_size_device_px` 和 `compositor_viewport_pixel_rect`，首个 Widget 初始化也会使用这些参数。内容区域、可视区域和合成表面必须一起保持一致。配置中的 CSS 像素、Windows DIP、物理像素需要在对应层正确换算，不能把一个 CSS 尺寸直接写入所有 `*_device_px` 字段。

应继续沿 Windows 的实际 View 尺寸、视觉参数更新和渲染器接收路径实现，避免通过只修改 JS 返回值或只覆盖某个视觉参数形成输入、布局和截图不一致。

## 屏幕信息与 CDP

用户提供的 `render_widget_host_view_base.cc` 修改可以继续承担原生屏幕、可用区域、DPR 的参数应用。145 标签中也确实存在 `ScreenMetricsEmulator::Apply()` 将可用区域覆盖为完整屏幕的代码。

若最终所有设备行为均由内核在创建页面时原生实现，SMAd 可以移除设备尺寸 CDP 调用；若还会启用原生 DeviceEmulationParams 链路，则必须额外保留设备档案中的可用区域，避免上述覆盖。触屏支持和输入派发是独立职责。

## 后续实施与验证

目前仅完成精确版本的源码核对和入口定位，没有制作或编译 Chromium 内核补丁。完整补丁还需要针对实际定制源码、原有改动和构建配置确认生命周期与作用域。

验收应覆盖：首个业务脚本读取、首次布局、Windows 窗口 resize、主页面跳转、`window.open` 标签页/独立窗口、跨进程 iframe、跨站导航、触屏坐标和截图分辨率。仅验证 `screen.width/height` 不足以证明原生视口及移动布局正确。

精确标签参考：

- [ChromeContentBrowserClient](https://github.com/chromium/chromium/blob/145.0.7632.110/chrome/browser/chrome_content_browser_client.cc#L4460)
- [WebPreferences](https://github.com/chromium/chromium/blob/145.0.7632.110/third_party/blink/public/common/web_preferences/web_preferences.h#L159)
- [EnableMobileEmulation](https://github.com/chromium/chromium/blob/145.0.7632.110/third_party/blink/renderer/core/inspector/dev_tools_emulator.cc#L403)
- [GetSizeForMainFrame](https://github.com/chromium/chromium/blob/145.0.7632.110/content/browser/web_contents/web_contents_impl.cc#L10841)
- [GetVisualProperties](https://github.com/chromium/chromium/blob/145.0.7632.110/content/browser/renderer_host/render_widget_host_impl.cc#L1040)
- [ScreenMetricsEmulator](https://github.com/chromium/chromium/blob/145.0.7632.110/third_party/blink/renderer/core/frame/screen_metrics_emulator.cc#L168)
