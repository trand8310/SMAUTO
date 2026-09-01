using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace PlaywrightHumanInput
{
    public sealed class CdpTouchDispatcher
    {
        private readonly object _timestampLock = new();
        private readonly double _unixAnchorSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        private readonly long _stopwatchAnchor = Stopwatch.GetTimestamp();
        private double _lastTimestamp;

        public async Task EnableAsync(IPage page, ICDPSession cdp, TouchDeviceProfile device)
        {
            if (page == null || page.IsClosed) return;
            try { await page.BringToFrontAsync(); } catch { }
            try
            {
                await cdp.SendAsync("Input.setIgnoreInputEvents", new Dictionary<string, object> { ["ignore"] = false });
            }
            catch { }

            try
            {
                await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["maxTouchPoints"] = Math.Max(1, device.MaxTouchPoints)
                });
            }
            catch { }
        }

        public async Task DispatchAsync(
            ICDPSession cdp,
            IReadOnlyList<TouchSample> samples,
            GesturePlan plan,
            TouchDeviceProfile device,
            CancellationToken cancellationToken = default)
        {
            if (samples == null || samples.Count == 0) return;

            bool started = false;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await SendPointAsync(cdp, "touchStart", samples[0], device);
                started = true;

                if (plan.StartHoldMs > 0)
                    await Task.Delay(plan.StartHoldMs, cancellationToken);

                stopwatch.Restart();
                for (int i = 1; i < samples.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double targetMs = samples[i].TimeMs + device.InputLatencyMs;
                    await DelayUntilAsync(stopwatch, targetMs, cancellationToken);
                    await SendPointAsync(cdp, "touchMove", samples[i], device);
                }

                if (plan.EndHoldMs > 0)
                    await Task.Delay(plan.EndHoldMs, cancellationToken);
            }
            finally
            {
                if (started)
                {
                    try { await SendEndAsync(cdp); } catch { }
                }
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

        private Task SendEndAsync(ICDPSession cdp)
        {
            var payload = new Dictionary<string, object>
            {
                ["type"] = "touchEnd",
                ["touchPoints"] = Array.Empty<object>(),
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
