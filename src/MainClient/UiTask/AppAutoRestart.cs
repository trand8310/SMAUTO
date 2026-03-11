using System.Diagnostics;

namespace MainClient.UiTask
{
    public sealed class AppAutoRestart : IDisposable
    {
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
