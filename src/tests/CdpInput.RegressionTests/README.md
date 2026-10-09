# CDP 输入回归

`dotnet run --project tests/CdpInput.RegressionTests/CdpInput.RegressionTests.csproj`

默认不启动浏览器，验证初始化幂等、输入串行化、取消和失败清理、页面会话配对、会话缓存失效、显式分离、释放时创建竞争、下载监听去重。

传入 Chromium 可执行文件路径启用实际触屏与下载检查。附加 `--raw-touch` 使用最简单的原始 CDP 按下/抬起做对照。

本地 145.0.7632.110 无界面内核中，触摸前 Runtime.evaluate 可读到值；Enhanced Tap 与原始 CDP 触摸均完成发送后，Runtime.evaluate 超时。这两种实际浏览器检查目前未通过，不能据此宣称业务点击或下载兼容性已验证。该现象记录在 docs/SMAd-CDP改进记录.md 第 7 项。
