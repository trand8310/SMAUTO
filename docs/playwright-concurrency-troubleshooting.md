# Playwright 并发下“浏览器打开但不动作”排查清单

> 适用范围：`SMAdTask` + `ChromiumSessionManager` 的执行链路。

## 1) 首先确认的硬限制（最容易误判成 CPU 不够）

### A. Chromium 启动并发被固定限制为 4

`ChromiumSessionManager` 内部用 `_launchLimiter = new SemaphoreSlim(4, 4)` 限制了同时“启动浏览器进程”的并发数。即使 UI 把 `MaximumConcurrency` 设为更高，真正进入启动阶段仍会排队。

- 位置：`src/Libraries/QTP.Common/ChromiumSessionManager.cs`
- 影响：高并发下会出现“部分任务已创建，但启动动作排队”，外观上像“有浏览器但不动”。

### B. 启动完成后，连接 CDP 只有 3 次重试、每次间隔 200ms

`ConnectOverCDPWithRetryAsync` 只重试 3 次，失败就直接退出当前 worker。

- 位置：`src/Plugins/SMAd/SMAdTask.cs`
- 影响：并发大时 DevTools endpoint 可能短暂不可用，导致连接失败；用户侧可能只看到窗口出现但流程没继续。

### C. 等待 Debug 端口就绪超时只有 15 秒

`StartAndConnectBrowserAsync` 调用 `StartChromium(... readyTimeout: 15s)`。

- 位置：`src/Plugins/SMAd/SMAdTask.cs`
- 影响：机器 IO 忙、代理慢、初次 profile 初始化慢时，15 秒可能不够，任务会被中断。

---

## 2) 容易让“看起来卡住”的流程点

### A. `ConfigureContextAsync` 直接取 `browser.Contexts[0]` / `Pages[0]`

当前逻辑依赖连接后立即存在 context/page。如果并发下 context/page 建立延迟，可能出现空集合或后续初始化异常。

- 位置：`src/Plugins/SMAd/SMAdTask.cs`
- 影响：任务提前失败（但如果日志不显眼，会被误认为浏览器空转）。

### B. 页面生命周期回调里有取消逻辑，会“静默结束”

在 `InitPageAsync` 中：
- `page.Crash` 会触发 `linkedCts.Cancel()`；
- `RequestFailed` 遇到隧道失败/认证失败也会 cancel。

- 位置：`src/Plugins/SMAd/SMAdTask.cs`
- 影响：并发+代理不稳时，窗口还在但 worker 已经进入取消路径。

### C. 主流程中有大量“吞异常”代码段

例如若干 `catch { }` 或 `catch (Exception) {}`，会隐藏真实失败原因。

- 位置：`src/Plugins/SMAd/SMAdTask.cs` 及相关 helper。
- 影响：现场表现为“没动作”，但根因日志缺失。

---

## 3) 并发错配（配置层）

### A. 消费者并发由 `MaximumConcurrency` 控制，但启动器另有限流

`PipelineRunner` 的 `consumerCount = MaximumConcurrency`，理论上可以很高；但浏览器启动被 `_launchLimiter=4` 限制，导致上游并发与下游启动吞吐不匹配。

- 位置：
  - `src/MainClient/MainForm.cs`（初始化 pipeline）
  - `src/Libraries/QTP.Common/ChromiumSessionManager.cs`（启动限流）

### B. 任务容量 `Multiple * MaximumConcurrency`

队列容量会放大，容易出现“很多任务处于等待状态”，用户直觉像“线程够但浏览器没干活”。

- 位置：`src/MainClient/MainForm.cs`

---

## 4) 建议你优先加的追踪点（按收益排序）

1. **启动排队耗时**：记录每个 `uniqueId` 从进入 `StartChromium` 到拿到 `_launchLimiter` 的耗时。
2. **CDP 连接耗时 + 每次重试异常**：记录 endpoint、attempt、异常类型、耗时。
3. **Context/Page 可用性**：在 `ConfigureContextAsync` 前后记录 `browser.Contexts.Count`、`context.Pages.Count`。
4. **取消原因归因**：在每个 `linkedCts.Cancel()` 之前写出统一 reason（Crash / ProxyAuth / TunnelFail / BrowserDisconnected）。
5. **阶段打点**：为 `ExecuteWorkerAsync` 增加 stage（StartChromium / ConnectCDP / InitPage / Navigate / Scroll / Click）。

---

## 5) 快速判断是“吞吐瓶颈”还是“逻辑故障”

- 若大量任务停在 `StartChromium` 前：多半是 `_launchLimiter` 限制。
- 若停在 `ConnectOverCDP`：多半是端口就绪窗口太短或 CDP 重试过少。
- 若进入 `InitPageAsync` 后马上 cancel：多半是代理链路失败或页面 crash。
- 若流程继续但无交互：重点看是否走进了 `catch {}` 分支导致动作被吞。

---

## 6) 建议的第一轮实验参数

1. 暂时把 `MaximumConcurrency` 调到 4，与 `_launchLimiter` 对齐，观察是否明显稳定。
2. 把 `readyTimeout` 从 15s 提高到 30~45s 做 A/B。
3. 把 CDP 重试从 `3 x 200ms` 提高到 `5~8 次，指数退避`。
4. 代理模式下单独跑一轮（关闭代理对照），确认是不是网络错误触发 cancel。

如果你愿意，我下一步可以直接给你一版“最小侵入式埋点补丁”（只加日志、不改业务分支），方便你线上快速定位。
