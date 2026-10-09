# 第 4、5 项：回收结果与本地并发验证

## 第 4 项实现

### 回收结果可查询

`BrowserRuntimeLease.CloseWithResultAsync()` 返回 `BrowserReclaimResult`；原有 `DisposeAsync()` 继续在进程退出无法确认时抛出异常，保留运行名额和驱动租约，允许重试。

`ChromiumSessionManager.CloseWithResultAsync()` 返回 `ChromiumCloseResult`，包含退出确认、是否强制终止、退出码、耗时、错误和独立的目录清理任务。关闭完成后的记录也保留在 `ChromiumSession.LastCloseResult`。同一会话并发关闭仍共享同一个关闭任务。

目录结果与进程结果分开：`DirectoryCleanup` 返回 `ProfileCleanupResult`，包含是否删除成功、尝试次数和失败原因。进程已经退出并不意味着目录已经删掉。

SMAd 的 `WorkerExecutionResult` 增加 `BrowserStopKind` 和 `BrowserReclaim`。MainClient 记录会话回收、进程回收和目录失败日志。原有元组接口保持兼容。

### 到期先取消，再强制回收

浏览器有效期使用 `Stopwatch` 单调时间，UTC 到期字段保留作为显示信息，不参与到期判定。默认每秒扫描一次，到期先触发 `StopRequested(LifetimeExpired)`，传播到会话令牌和 SMAd 的取消链。

默认保留 5 秒清理时间。任务仍未结束时，进程管理器强制回收该会话的进程树。多个会话的到期回收独立执行，不被某个慢关闭串行阻塞。连接尚未建立时到期，也会取消连接等待并回滚资源，保留到期原因。

### 原因分类与资源保护

分类包含正常结束、调用者取消、整体停止、到期、浏览器/页面崩溃、CDP 断开、共享驱动失效、操作超时、资源不足、启动失败和清理失败。报告保留原始结束原因，清理过程中新增的错误单独保留。

页面动作预算耗尽也会保留操作超时。若业务流程因此失败，回收记录使用这个原因；业务成功完成时仍按正常结束记录，不把已恢复的可选动作失败误报为任务故障。

Win32 8/14/1455 和 `OutOfMemoryException` 归为资源不足。运行时停止接受新浏览器；MainClient 停止拉取任务、停止启动排队任务及后续执行，健康任务继续完成。资源恢复后，用户开始下一轮任务才恢复接收。不自动重启系统，也不自动替换内核。

### 目录失败不会占着清理线程等待

`ProfileCleanupQueue` 默认最多 4 个删除操作并行。重试间隔为 0.5、1、2、4 秒，延后重试不占用删除线程；最后仍失败时返回明确结果并记录日志。停止时给清理任务最多 10 秒退避收束时间。

活动目录、待清理目录和清理失败目录都保留归属，禁止新浏览器复用，避免旧清理任务删掉新会话的目录。失败目录可以在进程退出后通过 `RetryProfileCleanupAsync()` 重试。同一路径并发清理共用任务。文件系统根目录不能作为浏览器用户目录。

## 第 5 项验证

测试位于 `tests/BrowserRuntime.RegressionTests`。所有真实页面为本地空白页中的按钮和 iframe，没有运行第三方网站业务。

- 六轮启停，每轮两个独立浏览器，共享一个 Playwright 驱动。
- 真实页面和 CDP 的跨任务使用被拒绝；任务输入锁互不阻塞。
- 一轮取消任务，另一轮终止所属浏览器进程，健康会话继续操作页面。
- 每轮停止后受管进程、驱动租约和 CDP 缓存归零，旧输入会话无法复用。
- 真实到期先通知任务，未响应时强制回收并清理目录。
- 真实文件锁造成目录删除失败；其他目录仍可清理，失败目录不能复用，释放文件锁后重试成功。
- 记录宿主及所属 Playwright 驱动的私有内存和句柄，并验证最终 Provider 销毁后驱动进程退出。

内存和句柄检查是六轮的短期回归观测：预热后末轮相对基线增长阈值为 64 MiB / 32 个句柄，不等同于长时间无泄漏证明。每次运行将原始数据写入独立的 `artifacts/<id>/report.json`。

运行示例：

```powershell
dotnet run --project tests/BrowserRuntime.RegressionTests/BrowserRuntime.RegressionTests.csproj
dotnet run --project tests/BrowserRuntime.RegressionTests/BrowserRuntime.RegressionTests.csproj -- ../chrome.packed/145.0.7632.110/chrome.exe --stress
dotnet run --project tests/BrowserRuntime.RegressionTests/BrowserRuntime.RegressionTests.csproj -- ../pc_chrome.packed/135.0.7049.119/chrome.exe --touch-probe
```

## 触屏兼容性对照

2026-10-08～09 的本地无界面测试中，145.0.7632.110 的原始 `touchStart` / `touchEnd` 得到发送应答，但随后 `Runtime.evaluate` 等待超时，无法确认页面点击。另一浏览器会话保持响应，失败会话仍能够独立回收。

相同夹具、同一驱动版本和同一初始化流程，在本地 PC 内核 135.0.7049.119 中可以确认真实点击。进一步运行 v3 Tap 和两个并发 Tap，点击次数及 `touchstart/touchend` 顺序均符合预期。

这将问题范围缩小到当前 145 内核、无界面模式和 CDP 组合的兼容性；尚不能据此确定内核内部根因。145 的触屏问题仍未修复，也未将其测试计为触屏成功。`--touch-probe` 是诊断模式：请查看 `Touch.ClickVerified`、`Touch.ScriptResponsive` 和 `Touch.Error`，不要仅凭程序退出码判断触屏兼容。

本轮没有修改生产环境的内核版本。下一步应针对 145 做专门的内核兼容性调查，再决定修复内核或调整运行方式。

## 本轮验证结果

- 浏览器回收及并发检查：124 项通过，其中包括六轮真实启停。
- 原有输入与页面动作检查：60 项通过；执行结果与适配层检查：24 项通过。
- MainClient 编译通过。
- 135 内核对照中的原始触屏、v3 Tap、并发 Tap 和接触顺序均通过；145 的触屏超时仍保留为已复现的问题。

原始数据：

- [六轮内存、句柄与 145 诊断报告](browser-runtime-validation-20261009.json)
- [135 内核触屏及 v3 验证报告](browser-touch-135-validation-20261009.json)

预热后的第 2 轮至第 6 轮，宿主私有内存约 24.1 MiB → 22.6 MiB，句柄 443 → 461；驱动私有内存约 97.6 MiB → 100.7 MiB，句柄 255 → 254。该短期样本满足设定阈值，但宿主句柄仍有增长，需要更长时间的观测才能确认稳定性。
