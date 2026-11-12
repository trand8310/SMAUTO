# CDP 输入回归

`dotnet run --project tests/CdpInput.RegressionTests/CdpInput.RegressionTests.csproj`

默认不启动浏览器，验证初始化幂等、输入串行化、取消和失败清理、页面会话配对、会话缓存失效、显式分离、释放时创建竞争、下载监听去重。

传入 Chromium 可执行文件路径启用实际触屏与下载检查。附加 `--raw-touch` 使用最简单的原始 CDP 按下/抬起做对照。

本地 145.0.7632.110 无界面内核中，触摸前 Runtime.evaluate 可读到值；Enhanced Tap 与原始 CDP 触摸均完成发送后，Runtime.evaluate 超时。这两种实际浏览器检查目前未通过，不能据此宣称业务点击或下载兼容性已验证。该现象记录在 docs/SMAd-CDP改进记录.md 第 7 项。

2026-10-12 增加页面关闭竞争、会话替换、原生显示诊断及入口短暂失败恢复检查。按保存配置使用 145 有界面/隐身模式的页面操作调试通过；这不替代上面的 headless 兼容性检查。

实际配置调试：`CdpInput.RegressionTests.exe --startup-diagnostic --rounds=6`。默认从 src/Build/publish 读取保存配置和本地设备样本，关闭代理，只操作本地页面；`--base=<目录>` 可替换输出目录。添加 `--url=<URL>` 会实际访问并触屏操作指定页面，不提交任务或上报统计。报告写入 artifacts/browser-operation-*，失败返回非零退出码。详见 docs/SMAd-浏览器退出诊断与页面故障隔离-20261012.md。

加入 `--actual-only --url=<URL>` 可单独诊断真实搜索页，不执行前置本地夹具；检查搜索输入框实际获得焦点、触屏滑动后滚动得到验证及页面仍可读取，不点击广告。该模式不能证明本地夹具或完整业务流程通过。

`--startup-diagnostic --touch-timing --rounds=1` 使用本地 data 页面检查触屏点击实际改变 URL 片段以及滑动实际滚动，记录每次手势的 CDP 命令数、p95 和最大响应时间；任一页面效果未验证时返回失败。该诊断可以发现 data 页面与其他页面的内核差异，不以命令发送完成替代点击成功。并发运行时各进程使用各自的诊断目录和浏览器。

`--startup-diagnostic --ai-site-no-result --rounds=1` 使用本地页面验证 AI 站点无结果提示在隐藏、可见及缺失时的判断，不访问业务站点。

`--startup-diagnostic --ai-site-live --rounds=1` 读取 `SMAdTask.cs` 中测试模式当前启用的 AI 站点 URL，并在独立浏览器中执行实际站点策略；该模式会访问真实网站并可能触发真实点击与跳转，仅在明确需要实站诊断时使用。它直接执行落地页策略，不代表运行完整测试分支或真实业务统计。

`--startup-diagnostic --ai-site-close-fixture --rounds=1` 使用拦截的本地页面验证隐藏按钮、关闭成功、关闭无效果和无法确认容器的处理。`--ai-site-close` 则访问测试模式指定的真实站点并触屏关闭弹窗，仅在明确授权实站测试时使用。

`--startup-diagnostic --entry-search-ready-fixture --rounds=1` 拦截搜索入口并延迟脚本，验证可见广告提前就绪、隐藏广告不能触发提前就绪、慢加载诊断、短页面滚动判定，以及广告标记缺失、可见标签冲突和延迟插入的处理；不访问真实搜索站点。

`--startup-diagnostic --entry-readiness-live --rounds=1` 读取源码当前启用的入口 URL，使用保存的设备和内核配置直连访问，并检查入口就绪、文档状态与广告标记；会访问真实网站，但不触屏或点击广告。该诊断在入口就绪后执行配置的加载后等待，记录匹配元素的可见性、视口位置和嵌套关系，不实际执行完整业务流程或代理链路。

`--live-entry-flow --rounds=1` 使用真实 Worker 和当前源码的入口逻辑、保存的设备与内核配置直连测试，点击率设为 0，不提交外部统计；可能浏览真实页面，但不会主动触发广告点击。源码若硬编码入口地址，该地址会覆盖诊断输入。

完整流程调试：从 src 执行 `CdpInput.RegressionTests.exe --full-page-flow --rounds=2`，使用真实 `SMAdTask.ExecuteWorkerWithResultAsync`，覆盖入口浏览、首次触屏点击、落地页策略浏览、当前可视候选二跳、详情停留和执行资源回收。两轮分别验证爱采购首次新页/二跳同页、1688 首次同页/二跳新页。

完整流程沿用保存的内核、显示模式、隐身、TTL、加载和阅读设置及本地设备样本；使用一个 PV、100% 点击分支和 8–12 秒详情停留。所有浏览器请求在导航前拦截并返回受控页面，不访问广告网络，不启动统计上传，也不走 `isTest` 的停留捷径。当前夹具要求 `HompageTrigger=0`。报告写入 artifacts/full-page-flow-*；入口、两次跳转、停留、PV/点击结算和退出确认全部通过才返回零退出码，不能仅以 worker 成功判断完整流程成功。

`--headless=true/false` 可覆盖完整流程测试的显示模式，不写回保存配置。默认协议检查包含超过 6 MiB 的 UTF-8 WebSocket 分片，验证超大无关事件、Target 附加/分离、命令响应隔离、关键启动保护、后续连接断开及诊断不记录正文。

`--device-large-messages` 只运行上述大消息协议检查，不启动 Chromium，适合单独验证设备连接及诊断调整。
