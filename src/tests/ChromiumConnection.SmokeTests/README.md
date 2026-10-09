# 自动 CDP 地址验证

仅解析检查：

```powershell
dotnet run --project tests/ChromiumConnection.SmokeTests/ChromiumConnection.SmokeTests.csproj
```

本地 Chromium 验证（在 src 目录执行）：

```powershell
dotnet run --project tests/ChromiumConnection.SmokeTests/ChromiumConnection.SmokeTests.csproj -- Build/publish/File/chrome-win/145.0.7632.110/chrome.exe
```

使用两个独立测试用户目录，以 headless 模式验证自动分配端口、直接 WebSocket
连接、DOM 操作、会话关闭与启动前取消。不访问业务网站。
页面 load 事件兼容性与业务启动参数不在此测试范围内，见 docs/SMAd-CDP改进记录.md。
