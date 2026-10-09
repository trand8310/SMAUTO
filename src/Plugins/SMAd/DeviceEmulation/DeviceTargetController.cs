using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace SMAd.DeviceEmulation;

/// <summary>A flat CDP connection configures new page targets before releasing their startup pause.</summary>
public sealed class DeviceTargetController : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly DeviceDisplayProfile _profile;
    private readonly int _touchPoints;
    private readonly Action<Exception> _failure;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<Task, byte> _jobs = new();
    private readonly ConcurrentDictionary<string, Task> _ready = new();
    private readonly ConcurrentDictionary<string, string> _sessions = new();
    private Task _reader = Task.CompletedTask;
    private int _nextId, _disposed, _failed;

    private DeviceTargetController(DeviceDisplayProfile profile, int touchPoints, Action<Exception> failure)
    { _profile = profile; _touchPoints = touchPoints; _failure = failure; }

    public static async Task<DeviceTargetController> CreateAsync(string endpoint, DeviceDisplayProfile profile,
        int touchPoints, Action<Exception> failure, CancellationToken token)
    {
        var controller = new DeviceTargetController(profile, touchPoints, failure);
        try
        {
            using var connect = CancellationTokenSource.CreateLinkedTokenSource(token);
            connect.CancelAfter(TimeSpan.FromSeconds(5));
            await controller._socket.ConnectAsync(new Uri(endpoint), connect.Token);
            controller._reader = controller.ReadAsync();
            await controller.SendAsync(null, "Target.setAutoAttach", new
            {
                autoAttach = true, waitForDebuggerOnStart = true, flatten = true,
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
                WebSocketReceiveResult received;
                do
                {
                    received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _shutdown.Token);
                    if (received.MessageType == WebSocketMessageType.Close) throw new IOException("Device CDP connection closed.");
                    message.Write(buffer, 0, received.Count);
                    if (message.Length > 4 * 1024 * 1024) throw new IOException("Device CDP message exceeded the limit.");
                } while (!received.EndOfMessage);
                using var document = JsonDocument.Parse(message.ToArray());
                var data = document.RootElement;
                if (data.TryGetProperty("id", out var id))
                {
                    if (_pending.TryGetValue(id.GetInt32(), out var completion))
                    {
                        if (data.TryGetProperty("error", out var error))
                            completion.TrySetException(new InvalidOperationException("Device CDP: " + error.GetRawText()));
                        else completion.TrySetResult(data.GetProperty("result").Clone());
                    }
                }
                else if (data.TryGetProperty("method", out var method))
                {
                    var payload = data.GetProperty("params");
                    if (method.GetString() == "Target.attachedToTarget") OnAttached(payload);
                    else if (method.GetString() == "Target.detachedFromTarget") OnDetached(payload);
                }
            }
        }
        catch (Exception ex)
        {
            foreach (var pending in _pending.Values) pending.TrySetException(ex);
            ReportFailure(ex);
        }
    }

    private void ReportFailure(Exception ex)
    { if (_disposed == 0 && Interlocked.Exchange(ref _failed, 1) == 0) _failure(ex); }

    private void OnAttached(JsonElement payload)
    {
        var session = payload.GetProperty("sessionId").GetString()!;
        var target = payload.GetProperty("targetInfo").GetProperty("targetId").GetString()!;
        _sessions[session] = target;
        var job = InitializeAsync(session, target);
        _ready[target] = job;
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
            await SendAsync(session, "Page.enable", new { });
            await SendAsync(session, "Runtime.runIfWaitingForDebugger", new { });
        }
        catch (Exception ex)
        {
            if (_disposed == 0 && _sessions.ContainsKey(session))
            {
                ReportFailure(ex);
                try { await SendAsync(null, "Target.closeTarget", new { targetId = target }); } catch { }
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
        await ready.WaitAsync(TimeSpan.FromSeconds(10), token);
    }

    private async Task<JsonElement> SendAsync(string? session, string method, object parameters)
    {
        int id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
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
        finally { _pending.TryRemove(id, out _); }
    }

    private void OnDetached(JsonElement payload)
    {
        var session = payload.GetProperty("sessionId").GetString()!;
        if (_sessions.TryRemove(session, out var target)) _ready.TryRemove(target, out _);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            if (_socket.State == WebSocketState.Open)
                await SendAsync(null, "Target.setAutoAttach", new { autoAttach = false, waitForDebuggerOnStart = false, flatten = true });
        }
        finally
        {
            _shutdown.Cancel(); _socket.Abort();
            foreach (var pending in _pending.Values) pending.TrySetCanceled();
            await _reader;
            try { await Task.WhenAll(_jobs.Keys).WaitAsync(TimeSpan.FromSeconds(6)); } catch { }
            _socket.Dispose(); _shutdown.Dispose(); _sendGate.Dispose();
            _ready.Clear(); _sessions.Clear();
        }
    }
}

