using System.Diagnostics;

namespace MainClient.UiTask
{
    public class UiTaskRunner
    {
        private readonly List<(PeriodicTimer timer, Func<Task> onTick)> _periodicActions = new();
        private readonly List<CancellationTokenSource> _timerCtsList = new();

        /// <summary>
        /// 运行时间统计
        /// </summary>
        private readonly Stopwatch _stopwatch = new();

        private readonly Func<CancellationToken, Task> _runTask;
        private CancellationTokenSource? _cts;

        public event Action<RunnerState>? StateChanged;
        public event Action<Exception>? Faulted;

        public RunnerState State { get; private set; } = RunnerState.Stopped;

        /// <summary>
        /// 程序已运行时长
        /// </summary>
        public TimeSpan RunElapsed => _stopwatch.Elapsed;

        public UiTaskRunner(Func<CancellationToken, Task> runTask)
        {
            _runTask = runTask;
        }

        public void Start()
        {
            if (State == RunnerState.Running) return;

            _stopwatch.Reset();
            _stopwatch.Start();

            _cts = new CancellationTokenSource();

            State = RunnerState.Running;
            StateChanged?.Invoke(State);

            // 启动所有定时任务
            foreach (var action in _periodicActions)
            {
                var timer = action.timer;
                var onTick = action.onTick;

                var timerCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                _timerCtsList.Add(timerCts);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        while (await timer.WaitForNextTickAsync(timerCts.Token))
                        {
                            await onTick();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Faulted?.Invoke(ex);
                    }
                });
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _runTask(_cts.Token);

                    State = RunnerState.Stopped;
                    StateChanged?.Invoke(State);
                }
                catch (OperationCanceledException)
                {
                    State = RunnerState.Stopped;
                    StateChanged?.Invoke(State);
                }
                catch (Exception ex)
                {
                    State = RunnerState.Faulted;
                    StateChanged?.Invoke(State);
                    Faulted?.Invoke(ex);
                }
                finally
                {
                    StopInternal();
                }
            });
        }

        public async Task StopAsync()
        {
            if (State != RunnerState.Running) return;

            _cts?.Cancel();

            while (State == RunnerState.Running)
            {
                await Task.Delay(50);
            }
        }

        private void StopInternal()
        {
            if (_stopwatch.IsRunning)
                _stopwatch.Stop();

            foreach (var cts in _timerCtsList)
            {
                try { cts.Cancel(); } catch { }
            }

            foreach (var item in _periodicActions)
            {
                item.timer.Dispose();
            }

            _timerCtsList.Clear();
            _periodicActions.Clear();
        }

        /// <summary>
        /// 增加一个定时任务
        /// </summary>
        public void SetPeriodicAction(TimeSpan interval, Func<Task> onTick)
        {
            var timer = new PeriodicTimer(interval);
            _periodicActions.Add((timer, onTick));
        }
    }
}
