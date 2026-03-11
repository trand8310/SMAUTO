using System.Diagnostics;

namespace MainClient.UiTask
{
    public sealed class AppAutoRestart : IDisposable
    {
        private static readonly TimeSpan RestartCooldown = TimeSpan.FromMinutes(2);
        private static int _restartRequested;
        private static DateTime _lastRestartRequestUtc = DateTime.MinValue;

        private PeriodicTimer? _timer;
        private CancellationTokenSource? _cts;
        private readonly Func<bool> _shouldRestart;
        private readonly TimeSpan _interval;

        public AppAutoRestart(TimeSpan interval, Func<bool> shouldRestart)
        {
            _interval = interval;
            _shouldRestart = shouldRestart ?? throw new ArgumentNullException(nameof(shouldRestart));
        }

        public void Start()
        {
            Stop(); // 避免重复启动

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _timer = new PeriodicTimer(_interval);

            _ = Task.Run(async () =>
            {
                try
                {
                    while (await _timer.WaitForNextTickAsync(token))
                    {
                        if (_shouldRestart())
                        {
                            RestartApplication();
                            return;
                        }
                    }
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _timer?.Dispose();
            _cts = null;
            _timer = null;
        }


        private void RestartApplication()
        {
            if (Interlocked.Exchange(ref _restartRequested, 1) == 1)
                return;

            var utcNow = DateTime.UtcNow;
            var elapsed = utcNow - _lastRestartRequestUtc;
            if (elapsed >= TimeSpan.Zero && elapsed < RestartCooldown)
                return;

            _lastRestartRequestUtc = utcNow;

            try
            {
                var exePath = Application.ExecutablePath;
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "restart",
                    WorkingDirectory = Path.GetDirectoryName(exePath)!,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                //_.Fatal(ex, "RestartApplication failed");
            }
            finally
            {
                Environment.Exit(1);
            }
        }


        //private void RestartApplication()
        //{
        //    var exePath = Application.ExecutablePath;
        //    System.Diagnostics.Process.Start(exePath);
        //    Application.Exit();
        //}

        public void Dispose() => Stop();
    }
}
