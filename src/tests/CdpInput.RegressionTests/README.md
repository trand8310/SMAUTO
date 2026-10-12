# CDP 输入回归

`dotnet run --project tests/CdpInput.RegressionTests/CdpInput.RegressionTests.csproj`

默认不启动浏览器，验证初始化幂等、输入串行化、取消和失败清理、页面会话配对、会话缓存失效、显式分离、释放时创建竞争、下载监听去重。

传入 Chromium 可执行文件路径启用实际触屏与下载检查。附加 `--raw-touch` 使用最简单的原始 CDP 按下/抬起做对照。

本地 145.0.7632.110 无界面内核中，触摸前 Runtime.evaluate 可读到值；Enhanced Tap 与原始 CDP 触摸均完成发送后，Runtime.evaluate 超时。这两种实际浏览器检查目前未通过，不能据此宣称业务点击或下载兼容性已验证。该现象记录在 docs/SMAd-CDP改进记录.md 第 7 项。

2026-10-12 增加页面关闭竞争、会话替换、原生显示诊断及入口短暂失败恢复检查。按保存配置使用 145 有界面/隐身模式的页面操作调试通过；这不替代上面的 headless 兼容性检查。

实际配置调试：`CdpInput.RegressionTests.exe --startup-diagnostic --rounds=6`。默认从 src/Build/publish 读取保存配置和本地设备样本，关闭代理，只操作本地页面；`--base=<目录>` 可替换输出目录。添加 `--url=<URL>` 会实际访问并触屏操作指定页面，不提交任务或上报统计。报告写入 artifacts/browser-operation-*，失败返回非零退出码。详见 docs/SMAd-浏览器退出诊断与页面故障隔离-20261012.md。

完整流程调试：从 src 执行 `CdpInput.RegressionTests.exe --full-page-flow --rounds=2`，使用真实 `SMAdTask.ExecuteWorkerWithResultAsync`，覆盖入口浏览、首次触屏点击、落地页策略浏览、当前可视候选二跳、详情停留和执行资源回收。两轮分别验证爱采购首次新页/二跳同页、1688 首次同页/二跳新页。

完整流程沿用保存的内核、显示模式、隐身、TTL、加载和阅读设置及本地设备样本；使用一个 PV、100% 点击分支和 8–12 秒详情停留。所有浏览器请求在导航前拦截并返回受控页面，不访问广告网络，不启动统计上传，也不走 `isTest` 的停留捷径。当前夹具要求 `HompageTrigger=0`。报告写入 artifacts/full-page-flow-*；入口、两次跳转、停留、PV/点击结算和退出确认全部通过才返回零退出码，不能仅以 worker 成功判断完整流程成功。
