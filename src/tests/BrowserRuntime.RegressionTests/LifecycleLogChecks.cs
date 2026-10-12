using System.Text.Json;
using QTP.Common;

internal static class LifecycleLogChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var id = "lifecycle-log-" + Guid.NewGuid().ToString("N");
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => BrowserLifecycleLog.Write(id, "fixture", "parallel", "并发诊断",
            new { sequence = i, url = "https://private-user:private-password@example.test/path?token=private-query#private-fragment",
                error = "password=private-inline\nAuthorization: Bearer private-header\nproxy=socks5://proxy-user:proxy-password@localhost:1080\nCommand line too long for RegisterApplicationRestart: --custom-credential=private-command",
                password = "private-field", nested = new[] { "https://example.test/?secret=private-array" } }))));
        var lines = File.ReadLines(BrowserLifecycleLog.CurrentFilePath).Where(line => line.Contains(id)).ToArray();
        check(lines.Length == 20 && lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("data").GetProperty("sequence").GetInt32()).Distinct().Count() == 20,
            "并发独立日志每行完整可解析且无丢失或混写");
        check(lines.All(line => !line.Contains("private-") && !line.Contains("proxy-user") && !line.Contains("proxy-password")) && lines[0].Contains("example.test/path"),
            "日志脱敏字段、异常凭据、URI用户名密码、查询参数及fragment，保留站点路径");
        using (var locked = new FileStream(BrowserLifecycleLog.CurrentFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            BrowserLifecycleLog.Write(id, "blocked", "fixture", "写入失败不得影响执行");
            check(BrowserLifecycleLog.LastWriteError != null, "日志文件被占用时不抛异常，保留写入故障提示");
        }
        BrowserLifecycleLog.Write(id, "recovered", "fixture", "占用解除后恢复写入");
        check(BrowserLifecycleLog.LastWriteError == null && File.ReadLines(BrowserLifecycleLog.CurrentFilePath).Any(line => line.Contains(id) && line.Contains("recovered")),
            "诊断日志失败后可继续写入，不永久关闭日志");
    }
}
