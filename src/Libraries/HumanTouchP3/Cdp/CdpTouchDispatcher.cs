using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HumanTouchP3.Input
{
    public sealed class CdpTouchDispatcher
    {
        private sealed class TouchCommandTimeout(Task pending, string type, long elapsedMs, Exception inner)
            : InvalidOperationException($"Touch command timed out: {type}; input state is unknown.", inner)
        {
            public Task Pending { get; } = pending;
            public string EventType { get; } = type;
            public long ElapsedMs { get; } = elapsedMs;
        }
        private sealed class TouchCommandCanceled(Task pending, string type, CancellationToken token)
            : OperationCanceledException("Touch command canceled while in flight.", token)
        {
            public Task Pending { get; } = pending;
            public string EventType { get; } = type;
        }
        private readonly object _timestampLock = new();
        private readonly double _unixAnchorSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        private readonly long _stopwatchAnchor = Stopwatch.GetTimestamp();
        private double _lastTimestamp;

        public async Task EnableAsync(IPage page, ICDPSession cdp, TouchDeviceProfile device)
        {
            await CdpTouchRuntime.InitializeAsync(page, cdp, device.MaxTouchPoints);
        }

        public async Task DispatchAsync(
            ICDPSession cdp,
            IReadOnlyList<TouchSample> samples,
            GesturePlan plan,
            TouchDeviceProfile device,
            CancellationToken cancellationToken = default,
            Func<bool>? bindingValid = null)
        {
            using var lease = await CdpTouchRuntime.AcquireAsync(cdp, cancellationToken);
            await DispatchWithinLeaseAsync(cdp, samples, plan, device, cancellationToken, bindingValid);
        }

        internal async Task<bool> DispatchWithinLeaseAsync(ICDPSession cdp, IReadOnlyList<TouchSample> samples,
            GesturePlan plan, TouchDeviceProfile device, CancellationToken cancellationToken, Func<bool>? bindingValid = null,
            Func<CancellationToken, Task<bool>>? swipeHealthCheck = null, Action<string>? log = null)
        {
            if (samples == null || samples.Count == 0) return true;
            cancellationToken.ThrowIfCancellationRequested();
            if (bindingValid?.Invoke() == false) throw new OperationCanceledException("Page or CDP binding changed.");
            bool started = false;
            bool completed = false;
            bool cleanupAttempted = false;
            var stopwatch = Stopwatch.StartNew();
            var commandDurations = new List<long>();
            async Task SendTrackedAsync(string type, TouchSample sample)
            {
                var clock = Stopwatch.StartNew();
                try { await SendTouchAsync(cdp, type, sample, device, cancellationToken, log); }
                finally { commandDurations.Add(clock.ElapsedMilliseconds); }
            }
            try
            {
                started = true;
                await SendTrackedAsync("touchStart", samples[0]);

                if (plan.StartHoldMs > 0)
                    await Task.Delay(plan.StartHoldMs, cancellationToken);

                stopwatch.Restart();
                for (int i = 1; i < samples.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double targetMs = samples[i].TimeMs + device.InputLatencyMs;
                    await DelayUntilAsync(stopwatch, targetMs, cancellationToken);
                    if (bindingValid?.Invoke() == false) throw new OperationCanceledException("Page or CDP binding changed.");
                    await SendTrackedAsync("touchMove", samples[i]);
                }

                if (plan.EndHoldMs > 0)
                    await Task.Delay(plan.EndHoldMs, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (bindingValid?.Invoke() == false) throw new OperationCanceledException("Page or CDP binding changed.");
                completed = true;
            }
            catch (TouchCommandTimeout ex) when (swipeHealthCheck != null && ex.EventType == "touchMove")
            {
                try
                {
                    // Keep both input locks until the original command has completed, then cancel the contact.
                    await ex.Pending.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                    SafeLog(log, $"CDP touch late response: type={ex.EventType}, elapsedMs={ex.ElapsedMs}, recovered=true");
                    cancellationToken.ThrowIfCancellationRequested();
                    if (bindingValid?.Invoke() == false) throw new InvalidOperationException("Page binding changed during touch recovery.");
                    cleanupAttempted = true;
                    await ReleaseLoggedAsync(cdp, true, log);
                    if (bindingValid?.Invoke() == false || !await swipeHealthCheck(cancellationToken))
                        throw new InvalidOperationException("Touch recovery health check failed.");
                    cancellationToken.ThrowIfCancellationRequested();
                    SafeLog(log, "CDP swipe canceled after late response; contact released and page healthy");
                    return false;
                }
                catch (OperationCanceledException) { CdpTouchRuntime.Invalidate(cdp); throw; }
                catch (Exception recoveryError)
                {
                    CdpTouchRuntime.Invalidate(cdp);
                    SafeLog(log, $"CDP swipe recovery failed: type={ex.EventType}, reason={recoveryError.GetType().Name}");
                    throw new InvalidOperationException("Touch command timed out; session input state is unknown.", ex);
                }
            }
            catch (TouchCommandCanceled ex) when (swipeHealthCheck != null && ex.EventType == "touchMove")
            {
                try
                {
                    // A local stay deadline may expire while a command is in flight. Keep locks and
                    // briefly drain it before deciding whether this session remains safe to reuse.
                    await ex.Pending.WaitAsync(TimeSpan.FromMilliseconds(500));
                    if (bindingValid?.Invoke() == false) throw new InvalidOperationException("Page binding changed during touch recovery.");
                    cleanupAttempted = true;
                    await ReleaseLoggedAsync(cdp, true, log);
                    using var healthBudget = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    if (bindingValid?.Invoke() == false || !await swipeHealthCheck(healthBudget.Token))
                        throw new InvalidOperationException("Touch recovery health check failed.");
                    SafeLog(log, $"CDP swipe cancellation drained: type={ex.EventType}, sessionHealthy=true");
                }
                catch (Exception recoveryError)
                {
                    CdpTouchRuntime.Invalidate(cdp);
                    SafeLog(log, $"CDP swipe cancellation recovery failed: type={ex.EventType}, reason={recoveryError.GetType().Name}");
                }
                throw;
            }
            catch (TouchCommandCanceled)
            {
                CdpTouchRuntime.Invalidate(cdp);
                throw;
            }
            catch (TouchCommandTimeout)
            {
                CdpTouchRuntime.Invalidate(cdp);
                throw;
            }
            finally
            {
                if (started && !cleanupAttempted)
                {
                    if (completed) await ReleaseLoggedAsync(cdp, false, log);
                    else { try { await ReleaseLoggedAsync(cdp, true, log); } catch { } }
                }
                if (commandDurations.Count > 0)
                {
                    var ordered = commandDurations.OrderBy(x => x).ToArray();
                    SafeLog(log, $"CDP gesture timing: commands={ordered.Length}, p95Ms={ordered[(int)Math.Ceiling(ordered.Length * .95) - 1]}, maxMs={ordered[^1]}");
                }
            }
            return true;
        }

        private async Task SendTouchAsync(ICDPSession cdp, string type, TouchSample sample,
            TouchDeviceProfile device, CancellationToken token, Action<string>? log)
        {
            var clock = Stopwatch.StartNew();
            var pending = SendPointAsync(cdp, type, sample, device);
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(5), token);
                if (clock.ElapsedMilliseconds >= 250) SafeLog(log, $"CDP touch slow: type={type}, elapsedMs={clock.ElapsedMilliseconds}");
            }
            catch (TimeoutException ex)
            {
                SafeLog(log, $"CDP touch timeout: type={type}, elapsedMs={clock.ElapsedMilliseconds}");
                _ = pending.ContinueWith(t => { _ = t.Exception; SafeLog(log, $"CDP touch late completion: type={type}, status={t.Status}, elapsedMs={clock.ElapsedMilliseconds}"); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new TouchCommandTimeout(pending, type, clock.ElapsedMilliseconds, ex);
            }
            catch (OperationCanceledException)
            {
                SafeLog(log, $"CDP touch canceled: type={type}, elapsedMs={clock.ElapsedMilliseconds}");
                _ = pending.ContinueWith(t => { _ = t.Exception; SafeLog(log, $"CDP touch late completion: type={type}, status={t.Status}, elapsedMs={clock.ElapsedMilliseconds}"); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw new TouchCommandCanceled(pending, type, token);
            }
        }

        private static void SafeLog(Action<string>? log, string message) { try { log?.Invoke(message); } catch { } }

        private static async Task ReleaseLoggedAsync(ICDPSession cdp, bool canceled, Action<string>? log)
        {
            var clock = Stopwatch.StartNew();
            var type = canceled ? "touchCancel" : "touchEnd";
            try
            {
                await CdpTouchRuntime.ReleaseTouchAsync(cdp, canceled);
                if (clock.ElapsedMilliseconds >= 250) SafeLog(log, $"CDP touch slow: type={type}, elapsedMs={clock.ElapsedMilliseconds}");
            }
            catch (Exception ex)
            {
                SafeLog(log, $"CDP touch release failed: type={type}, elapsedMs={clock.ElapsedMilliseconds}, reason={ex.GetType().Name}");
                throw;
            }
        }

        private async Task DelayUntilAsync(Stopwatch stopwatch, double targetMs, CancellationToken cancellationToken)
        {
            while (true)
            {
                double remaining = targetMs - stopwatch.Elapsed.TotalMilliseconds;
                if (remaining <= 0.20) return;
                if (remaining > 3.0)
                {
                    int sleep = Math.Max(1, (int)Math.Floor(remaining - 1.4));
                    await Task.Delay(sleep, cancellationToken);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.SpinWait(60);
                }
            }
        }

        private Task SendPointAsync(ICDPSession cdp, string type, TouchSample sample, TouchDeviceProfile device)
        {
            var point = new Dictionary<string, object>
            {
                ["x"] = Math.Round(sample.Point.X, 2),
                ["y"] = Math.Round(sample.Point.Y, 2),
                ["id"] = 0
            };
            if (device.SupportsTouchArea)
            {
                point["radiusX"] = Math.Round(sample.RadiusX, 2);
                point["radiusY"] = Math.Round(sample.RadiusY, 2);
            }
            if (device.SupportsForce)
                point["force"] = Math.Round(sample.Force, 3);
            if (device.SupportsRotationAngle)
                point["rotationAngle"] = Math.Round(sample.RotationAngle, 2);

            var payload = new Dictionary<string, object>
            {
                ["type"] = type,
                ["touchPoints"] = new object[] { point },
                ["modifiers"] = 0,
                ["timestamp"] = NextMonotonicUnixTimestamp()
            };
            return cdp.SendAsync("Input.dispatchTouchEvent", payload);
        }

        private double NextMonotonicUnixTimestamp()
        {
            double elapsed = (Stopwatch.GetTimestamp() - _stopwatchAnchor) / (double)Stopwatch.Frequency;
            double now = _unixAnchorSeconds + elapsed;
            lock (_timestampLock)
            {
                if (now <= _lastTimestamp)
                    now = _lastTimestamp + 0.000001;
                _lastTimestamp = now;
                return now;
            }
        }
    }
}
