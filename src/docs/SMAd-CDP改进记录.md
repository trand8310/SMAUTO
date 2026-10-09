# SMAd CDP 改进记录

按项处理；未完成项保留，避免一次改变输入与业务流程。

## 本轮：浏览器启动与连接

- 改用 `--remote-debugging-port=0`，由 Chromium 分配并占用端口。
- 从独立 user-data-dir 的 DevToolsActivePort 读取浏览器 WebSocket 地址。
- 就绪检查验证 /json/version 返回的浏览器路径与文件一致。
- SMAd 使用会话返回的连接地址，DebugPort 仅保留诊断与旧调用兼容。
- 连接设置明确超时；不再调用 RemotePortManager 分配或回收端口。

## 第 1—6 项：已实施

1. CDPSessionManager 显式 DetachAsync；订阅会话关闭并移除对应缓存，解除事件；释放后拒绝访问，释放等待正在创建的会话及已移除会话的清理。
2. Enhanced.CdpTouchRuntime 按会话缓存触屏配置，并串行初始化；初始化错误向上传播。SMAd 所有页面走同一入口。公共库旧 InitCDPSession 也补上真实配置，保留兼容调用。
3. Enhanced 提供 TapAsync/TapAtAsync。SMAd 普通点击改走 SmAdTouch，滑动继续走 Enhanced；辅助类也移除鼠标/JavaScript 点击回退。Tap 检查可见区域、遮挡、iframe 祖先及布局变化，失败不会进入点击成功的导航判断。
4. 正常发送 touchEnd，取消/异常发送 touchCancel；清理不使用已取消的业务令牌，设置等待上限。释放失败使 Enhanced 会话不可复用。公共库旧 TouchMove、TouchClickVisibleLocator 和 SMAd 辅助拖动也补齐清理，取消向上传播。
5. WorkerRunContext 用不可变 PageBinding 整体发布页面与会话；后台读取一次配对。同一 CDP 会话的完整点击和滑动受共用输入锁保护，退出时等待后台停止后分离会话。
6. 页面初始化按页缓存 Lazy<Task>，监听只注册一次；下载计数只来自 Playwright Page.Download，移除 CDP Page.downloadWillBegin 计数，保留取消下载行为。

验证：MainClient 与 SMAd 编译通过；新增 CDP 输入回归及已有第一步回归通过。真实浏览器验证的限制见第 7 项与 tests/CdpInput.RegressionTests/README.md。

## 第 7 项：仍待处理

7. 本地 145 内核无界面测试中，SetContentAsync 默认等待 load 超时；
   WebSocket 连接成功。连接与 DOM 操作测试独立进行，页面生命周期兼容性待查。

   本轮进一步发现：无界面 145 内核在触摸前 Runtime.evaluate 能读取数值；
   Enhanced Tap 发送完成后，Runtime.evaluate 等待 5 秒超时。
   使用没有 Enhanced 的原始 CDP touchStart/touchEnd 对照也复现。
   因此实际页面 click、并发触屏事件及下载检查尚未全部验证，不能将命令发送成功当作业务操作成功。

本轮保留自行启动 Chromium 的参数和进程管理，不切换到 Playwright 启动方式。
