# 浏览器会话与并发管理：第一阶段

本次完成统一会话管理、三类并发配置、共享 Playwright 生命周期。MainClient 的生产执行路径已经使用这套管理。

第 4、5 项的回收结果、到期取消、目录重试和并发验证，见 [回收结果与并发验证](SMAd-回收结果与并发验证.md)。

## 资源归属

MainClient 在应用级共享 `PlaywrightProvider`、`ChromiumSessionManager` 和 `BrowserRuntimeManager`。每次执行仍创建独立 SMAd 对象。

SMAd 通过 `BrowserRuntimeManager.AcquireAsync` 获取 `BrowserRuntimeLease`。租约包含本次执行的浏览器进程、CDP 连接、默认 Context、取消令牌和驱动借用凭证。不同执行禁止同时占用相同执行 ID 或用户数据目录。

默认 Context 属于通过 CDP 连接的浏览器，借用者不单独销毁它。页面及 CDP 的清理注册到会话；随后关闭连接，确认所属进程已经退出，再归还驱动租约和运行名额。如果进程退出未确认，保留会话及名额，允许按执行 ID 重试关闭。启动过程中部分创建成功而清理失败的进程也纳入隔离会话。

浏览器断开只取消所属任务。确认共享驱动连接损坏后，使旧代驱动失效、取消旧代任务；新任务可获取新代驱动。旧代在所有借用者归还后才销毁。单个任务取消初始化等待，不会取消其他任务共享的初始化。

## 三种限制

| 配置 | 含义 | 默认/兼容行为 |
|---|---|---|
| `TaskQueueCapacity` | 流水线待处理队列容量 | 0 时保留 `Multiple × MaximumConcurrency` 的旧计算方式 |
| `BrowserLaunchConcurrency` | 同时启动和建立连接的数量 | 默认 4，实际至少 1 |
| `MaximumConcurrency` | 同时执行的任务及受管浏览器数量 | 使用现有设置，实际至少 1 |

启动名额在建立连接后释放；运行名额一直持有到进程退出。关闭中的浏览器仍占运行名额。队列已满时生产者等待，不继续无限堆积任务。

新增配置随现有 `AppSettings` 保存到配置文件，本阶段没有增加界面控件。示例：`TaskQueueCapacity: 20`、`BrowserLaunchConcurrency: 2`、`MaximumConcurrency: 5`。这表示最多 20 个任务等待、最多 2 个浏览器同时启动、最多 5 个任务同时运行；具体数值应根据机器资源调整。

限制在两轮任务之间通过 `BeginRun` 应用，不会替换仍有活动会话或等待者的信号量。`Snapshot` 提供等待、启动、运行、关闭数量与是否接受新任务。

## 停止与退出

停止先取消流水线，等待生产者和所有消费者完成，包括执行任务的 `finally` 清理，再等待会话管理器停止、统计聚合器停止。窗口关闭使用同一停止路径。应用最终按会话、进程管理器、共享驱动的顺序释放服务。清理失败会报告错误，不以成功释放名额来掩盖进程仍存活的情况。

不再通过全局查找并关闭 Chrome 清理程序；只处理本应用登记的进程。启动资源错误也不自动重启操作系统。

## 验证及边界

`tests/BrowserRuntime.RegressionTests` 验证共享初始化、取消隔离、代际失效、释放顺序、启动/运行限制、独占目录、失败回滚、隔离会话重试、停止后重新运行以及流水线取消等待清理。

运行：`dotnet run --project tests/BrowserRuntime.RegressionTests/BrowserRuntime.RegressionTests.csproj`。追加 Chromium 可执行文件路径可运行真实浏览器测试，页面仅为本地空白页。

这次验证的是资源管理和并发，不代表第三方页面触屏操作已经验证。此前自定义 Chromium 145 内核的原始 CDP 触屏后 Runtime.evaluate 超时问题，仍应作为后续独立兼容性任务处理。
