using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using QTP.Common;

namespace SMAd.DeviceEmulation;

/// <summary>Native metrics are present at page creation; the CDP connection gates input readiness without pausing navigation.</summary>
public sealed class DeviceTargetController : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DeviceDisplayProfile _profile;
    private readonly int _touchPoints;
    private readonly Action<Exception> _failure;
    private readonly Action<string>? _log;
    private const int MaximumBufferedMessage = 4 * 1024 * 1024;
    private const int RoutingPrefixLength = 64 * 1024;
    private sealed record PendingCommand(string? Session, string Method, TaskCompletionSource<JsonElement> Completion);
    private readonly ConcurrentDictionary<int, PendingCommand> _pending = new();
    private readonly ConcurrentDictionary<Task, byte> _jobs = new();
    private sealed record TargetReadiness(string Session, Task Ready);
    private readonly ConcurrentDictionary<string, TargetReadiness> _ready = new();
    private readonly ConcurrentDictionary<string, string> _sessions = new();
    private Task _reader = Task.CompletedTask;
    private int _nextId, _disposed, _failed;

    private DeviceTargetController(DeviceDisplayProfile profile, int touchPoints, Action<Exception> failure, Action<string>? log)
    { _profile = profile; _touchPoints = touchPoints; _failure = failure; _log = log; }

    public static async Task<DeviceTargetController> CreateAsync(string endpoint, DeviceDisplayProfile profile,
        int touchPoints, Action<Exception> failure, CancellationToken token, Action<string>? log = null)
    {
        var controller = new DeviceTargetController(profile, touchPoints, failure, log);
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
            connect.CancelAfter(TimeSpan.FromSeconds(5));
            await controller._socket.ConnectAsync(new Uri(endpoint), connect.Token);
            controller._reader = controller.ReadAsync();
            await controller.SendAsync(null, "Target.setAutoAttach", new
            {
                autoAttach = true, waitForDebuggerOnStart = false, flatten = true,
                filter = new object[] { new { type = "page", exclude = false }, new { exclude = true } }
            }).WaitAsync(token);
            return controller;
        }
        catch { try { await controller.DisposeAsync(); } catch { } throw; }
    }

    private async Task ReadAsync()
    {
        var buffer = new byte[16384];
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                long messageBytes = 0;
                WebSocketReceiveResult received;
                do
                {
                    received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _shutdown.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                        throw new BrowserFailureException(BrowserStopKind.CdpDisconnected, "Device CDP connection closed.");
                    if (received.MessageType != WebSocketMessageType.Text)
                        throw new BrowserFailureException(BrowserStopKind.DeviceProtocolFailed, "Device CDP received a non-text message.");
                    messageBytes += received.Count;
                    // Drain every fragment to EndOfMessage, retaining no more than the existing memory budget.
                    int keep = (int)Math.Min(received.Count, MaximumBufferedMessage - message.Length);
                    if (keep > 0) message.Write(buffer, 0, keep);
                } while (!received.EndOfMessage);
                if (messageBytes > MaximumBufferedMessage)
                {
                    HandleOversizedMessage(ReadRouting(message.GetBuffer().AsSpan(0, (int)Math.Min(message.Length, RoutingPrefixLength))), messageBytes);
                    continue;
                }
                using var document = JsonDocument.Parse(message.GetBuffer().AsMemory(0, (int)message.Length));
                var data = document.RootElement;
                if (data.TryGetProperty("id", out var id))
                {
                    if (_pending.TryGetValue(id.GetInt32(), out var completion))
                    {
                        if (data.TryGetProperty("error", out var error))
                            completion.Completion.TrySetException(new InvalidOperationException("Device CDP: " + error.GetRawText()));
                        else completion.Completion.TrySetResult(data.GetProperty("result").Clone());
                    }
                }
                else if (data.TryGetProperty("method", out var method))
                {
                    if (method.GetString() == "Target.attachedToTarget") OnAttached(data.GetProperty("params"));
                    else if (method.GetString() == "Target.detachedFromTarget") OnDetached(data.GetProperty("params"));
                }
            }
        }
        catch (Exception ex)
        {
            var failure = ex is BrowserFailureException ? ex : new BrowserFailureException(
                ex is WebSocketException ? BrowserStopKind.CdpDisconnected
                    : ex is OutOfMemoryException ? BrowserStopKind.ResourceExhausted : BrowserStopKind.DeviceProtocolFailed,
                "Device CDP reader failed: " + ex.Message, ex);
            foreach (var pending in _pending.Values) pending.Completion.TrySetException(failure);
            ReportFailure(failure);
        }
    }

    private sealed record Routing(int? Id, string? Method, string? Session, string? Target, string? TargetType);

    // The CDP envelope precedes potentially huge URLs/bodies. Incomplete string tokens are never copied or logged.
    private static Routing ReadRouting(ReadOnlySpan<byte> prefix)
    {
        int? id = null; string? method = null, session = null, target = null, type = null;
        var reader = new Utf8JsonReader(prefix, isFinalBlock: false, state: default);
        bool inParams = false, inTarget = false;
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                int depth = reader.CurrentDepth;
                bool isId = depth == 1 && reader.ValueTextEquals("id");
                bool isMethod = depth == 1 && reader.ValueTextEquals("method");
                bool isSession = (depth == 1 || depth == 2 && inParams) && reader.ValueTextEquals("sessionId");
                bool isTarget = depth == 3 && inParams && inTarget && reader.ValueTextEquals("targetId");
                bool isType = depth == 3 && inParams && inTarget && reader.ValueTextEquals("type");
                if (depth == 1) inParams = reader.ValueTextEquals("params");
                if (depth == 2) inTarget = inParams && reader.ValueTextEquals("targetInfo");
                if (!reader.Read()) break;
                if (isId && reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number)) id = number;
                if (reader.TokenType != JsonTokenType.String || reader.ValueSpan.Length > 256) continue;
                if (isMethod) method = reader.GetString();
                if (isSession) session = reader.GetString();
                if (isTarget) target = reader.GetString();
                if (isType) type = reader.GetString();
            }
        }
        catch (JsonException) { } // Preserve only routing tokens that were fully decoded.
        return new(id, method, session, target, type);
    }

    private void HandleOversizedMessage(Routing route, long bytes)
    {
        PendingCommand? command = null;
        if (route.Id is { } responseId) _pending.TryGetValue(responseId, out command);
        string metadata = $"bytes={bytes}, method={route.Method ?? command?.Method ?? "unknown"}, id={route.Id?.ToString() ?? "none"}, session={route.Session ?? command?.Session ?? "none"}";
        if (route.Id is { } id)
        {
            _log?.Invoke("Device CDP oversized response rejected: " + metadata);
            // A target command fails locally; a required startup command propagates to its caller.
            if (_pending.TryGetValue(id, out var response))
                response.Completion.TrySetException(new BrowserFailureException(BrowserStopKind.DeviceProtocolFailed,
                    "Device CDP required response exceeded the buffering limit: " + metadata));
            return;
        }
        if (route.Method == "Target.attachedToTarget" && route.Session != null && route.Target != null && route.TargetType != null)
        {
            OnAttached(JsonSerializer.SerializeToElement(new { sessionId = route.Session, targetInfo = new { targetId = route.Target, type = route.TargetType } }));
            _log?.Invoke("Device CDP oversized target event routed without payload: " + metadata);
            return;
        }
        if (route.Method == "Target.detachedFromTarget" && route.Session != null)
        {
            OnDetached(JsonSerializer.SerializeToElement(new { sessionId = route.Session }));
            _log?.Invoke("Device CDP oversized target event routed without payload: " + metadata);
            return;
        }
        if (route.Method != null && route.Method != "Target.attachedToTarget" && route.Method != "Target.detachedFromTarget")
        {
            _log?.Invoke("Device CDP oversized unrelated event drained and ignored: " + metadata);
            return;
        }
        // Do not guess and silently lose a required target transition or an unidentifiable response.
        throw new BrowserFailureException(BrowserStopKind.DeviceProtocolFailed,
            "Device CDP oversized message has incomplete required routing metadata: " + metadata);
    }

    private void ReportFailure(Exception ex)
    { if (_disposed == 0 && Interlocked.Exchange(ref _failed, 1) == 0) _failure(ex); }

    private void OnAttached(JsonElement payload)
    {
        var session = payload.GetProperty("sessionId").GetString()!;
        var target = payload.GetProperty("targetInfo").GetProperty("targetId").GetString()!;
        _sessions[session] = target;
        var job = InitializeAsync(session, target);
        _ready[target] = new(session, job);
        _jobs.TryAdd(job, 0);
        _ = job.ContinueWith(t => { _ = t.Exception; _jobs.TryRemove(t, out _); }, TaskScheduler.Default);
    }

    private async Task InitializeAsync(string session, string target)
    {
        try
        {
            // Preserve the screen and viewport initialized by the native kernel.
            if (_profile.Mobile)
                await SendAsync(session, "Emulation.setTouchEmulationEnabled", new { enabled = true, maxTouchPoints = Math.Clamp(_touchPoints, 1, 16) });
            // This connection only consumes Target events. Page.enable belongs to the page's business session.
            await SendAsync(session, "Runtime.runIfWaitingForDebugger", new { });
        }
        catch (Exception ex)
        {
            if (_disposed == 0 && _sessions.ContainsKey(session))
            {
                // A target may close while its commands are in flight. Other pages remain usable.
                _log?.Invoke($"Device target initialization failed: target={target}, session={session}, error={ex.Message}");
                // Never close by targetId here: it may already have a replacement session.
                try { await SendAsync(session, "Runtime.runIfWaitingForDebugger", new { }); } catch { }
                try { await SendAsync(null, "Target.detachFromTarget", new { sessionId = session }); } catch { }
            }
            throw;
        }
    }

    public async Task WaitForPageAsync(ICDPSession pageSession, CancellationToken token)
    {
        var response = await pageSession.SendAsync("Target.getTargetInfo").WaitAsync(TimeSpan.FromSeconds(5), token);
        var target = response!.Value.GetProperty("targetInfo").GetProperty("targetId").GetString()!;
        long started = Stopwatch.GetTimestamp();
        while (!_ready.TryGetValue(target, out _))
        {
            token.ThrowIfCancellationRequested();
            if (_failed != 0 || _disposed != 0) throw new InvalidOperationException("Device CDP controller is unavailable.");
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5)) throw new TimeoutException("Device target was not automatically attached.");
            await Task.Delay(10, token);
        }
        if (!_ready.TryGetValue(target, out var ready)) throw new InvalidOperationException("Device target closed during initialization.");
        await ready.Ready.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    private async Task<JsonElement> SendAsync(string? session, string method, object parameters)
    {
        int id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = new(session, method, completion);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var command = new Dictionary<string, object> { ["id"] = id, ["method"] = method, ["params"] = parameters };
            if (session != null) command["sessionId"] = session;
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command));
            await _sendGate.WaitAsync(deadline.Token);
            try { await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, deadline.Token); }
            finally { _sendGate.Release(); }
            return await completion.Task.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException ex) when (!_shutdown.IsCancellationRequested)
        { throw new TimeoutException($"Device CDP command timed out: method={method}, session={session}", ex); }
        finally { _pending.TryRemove(id, out _); }
    }

    private void OnDetached(JsonElement payload)
    {
        var session = payload.GetProperty("sessionId").GetString()!;
        if (_sessions.TryRemove(session, out var target) && _ready.TryGetValue(target, out var ready) && ready.Session == session)
            _ready.TryRemove(new KeyValuePair<string, TargetReadiness>(target, ready));
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            if (_failed == 0 && _socket.State == WebSocketState.Open)
                await SendAsync(null, "Target.setAutoAttach", new { autoAttach = false, waitForDebuggerOnStart = false, flatten = true });
        }
        finally
        {
            _shutdown.Cancel(); _socket.Abort();
            foreach (var pending in _pending.Values) pending.Completion.TrySetCanceled();
            await _reader;
            try { await Task.WhenAll(_jobs.Keys).WaitAsync(TimeSpan.FromSeconds(6)); } catch { }
            _socket.Dispose(); _shutdown.Dispose(); _sendGate.Dispose();
            _ready.Clear(); _sessions.Clear();
        }
    }
}

