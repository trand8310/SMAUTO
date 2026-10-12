using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QTP.Common;

/// <summary>Independent, synchronous diagnostic journal; logging failures never stop an execution.</summary>
public static class BrowserLifecycleLog
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly Regex Url = new(@"[a-z][a-z0-9+.-]*://[^\s""'<>]+", RegexOptions.IgnoreCase);
    private static readonly Regex Headers = new(@"\b(authorization|proxy-authorization|cookie|set-cookie)\s*[:=][^\r\n]+", RegexOptions.IgnoreCase);
    private static readonly Regex Secrets = new(@"\b(password|passwd|pwd|proxyPassword|proxyUsername|username|access_token|token|api_key)\b\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s,;]+)", RegexOptions.IgnoreCase);
    private static readonly Regex SecretKey = new(@"^(password|passwd|pwd|proxyPassword|proxyUsername|username|authorization|proxy-authorization|cookie|set-cookie|token|access_token|api_key)$", RegexOptions.IgnoreCase);
    private static readonly Regex NativeCommandLine = new(@"(command line(?: too long for RegisterApplicationRestart)?\s*[:=])[^\r\n]*", RegexOptions.IgnoreCase);
    public static string DirectoryPath => Path.Combine(AppContext.BaseDirectory, "logs", "browser-lifecycle");
    public static string CurrentFilePath => Path.Combine(DirectoryPath, $"browser-{ChinaNow:yyyyMMdd}-{Environment.ProcessId}.log");
    public static string? LastWriteError { get; private set; }
    private static DateTimeOffset ChinaNow => DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));

    public static void Write(string executionId, string eventName, string source, string message, object? data = null)
    {
        try
        {
            var now = ChinaNow;
            var entry = JsonSerializer.SerializeToNode(new
            { time = now, applicationPid = Environment.ProcessId, executionId, @event = eventName, source, message, data });
            Redact(entry);
            var line = entry!.ToJsonString(JsonOptions);
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                // Per-application file avoids cross-process write contention; each line is flushed on disposal.
                File.AppendAllText(Path.Combine(DirectoryPath, $"browser-{now:yyyyMMdd}-{Environment.ProcessId}.log"), line + Environment.NewLine, new UTF8Encoding(false));
                LastWriteError = null;
            }
        }
        catch (Exception ex) { LastWriteError = ex.GetType().Name + ": " + Sanitize(ex.Message); }
    }

    public static string Explain(BrowserStopKind kind) => kind switch
    {
        BrowserStopKind.Completed => "任务正常结束，程序主动回收浏览器",
        BrowserStopKind.Canceled => "上游取消本次执行，程序主动回收浏览器",
        BrowserStopKind.RuntimeStopping => "客户端停止浏览器运行时，统一回收所属执行",
        BrowserStopKind.LifetimeExpired => "本次浏览器生命周期 TTL 到期，取消执行并回收",
        BrowserStopKind.BrowserCrashed => "检测到进程提前退出或页面崩溃；结合退出事件与退出码确认",
        BrowserStopKind.CdpDisconnected => "浏览器 CDP 连接断开；单凭断开不能认定原生崩溃",
        BrowserStopKind.DriverDisconnected => "共享 Playwright 驱动失效，本次连接无法继续使用",
        BrowserStopKind.OperationTimedOut => "执行或操作超时，结束任务后回收浏览器",
        BrowserStopKind.ResourceExhausted => "系统资源不足，启动或执行无法继续",
        BrowserStopKind.StartupFailed => "浏览器启动、连接或初始化失败，回收已创建的资源",
        BrowserStopKind.CleanupFailed => "清理失败或未能确认进程退出，需要检查回收错误",
        BrowserStopKind.ProxyFailed => "确认代理故障，本次执行停止并回收浏览器",
        BrowserStopKind.ExecutionFailed => "页面业务流程失败，程序在执行结束时回收浏览器",
        BrowserStopKind.DeviceConfigurationFailed => "原生屏幕或设备配置验证失败，停止本次执行",
        _ => kind.ToString()
    };

    public static object Snapshot(ChromiumSession? session)
    {
        int? pid = null, exitCode = null; bool? exited = null;
        if (session != null) try
        { pid = session.Process.Id; exited = session.Process.HasExited; if (exited == true) exitCode = session.Process.ExitCode; }
        catch (Exception) { } // Process handles may already be disposed; diagnostics must remain optional.
        return new { pid, exited, exitCode, exitConfirmed = session?.ExitConfirmed, stopKind = session?.StopKind?.ToString(),
            elapsedMs = session == null ? (double?)null : System.Diagnostics.Stopwatch.GetElapsedTime(session.CreatedTimestamp).TotalMilliseconds };
    }

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject obj) foreach (var pair in obj.ToArray())
        {
            if (SecretKey.IsMatch(pair.Key)) obj[pair.Key] = "<redacted>";
            else if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text)) obj[pair.Key] = Sanitize(text);
            else Redact(pair.Value);
        }
        else if (node is JsonArray array) for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Sanitize(text);
            else Redact(array[i]);
        }
    }
    private static string Sanitize(string text)
    {
        // Chromium stderr can echo its full launch arguments; do not copy them into the shareable journal.
        text = NativeCommandLine.Replace(text, "$1 <command-line omitted>");
        text = Url.Replace(text, match =>
        {
            if (!Uri.TryCreate(match.Value, UriKind.Absolute, out var uri)) return "<redacted-url>";
            try { var safe = new UriBuilder(uri) { UserName = "", Password = "", Fragment = "", Query = uri.Query.Length == 0 ? "" : "redacted" }; return safe.Uri.ToString(); }
            catch { return "<redacted-url>"; }
        });
        text = Headers.Replace(text, "$1=<redacted>");
        text = Secrets.Replace(text, "$1=<redacted>");
        return text.Length > 24000 ? text[..24000] + "…[truncated]" : text;
    }
}
