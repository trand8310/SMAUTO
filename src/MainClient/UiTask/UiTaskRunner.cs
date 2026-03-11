using System.Diagnostics;

namespace MainClient.UiTask
{
    public class UiTaskRunner
    {
        private readonly List<(TimeSpan interval, Func<Task> onTick)> _periodicDefinitions = new();
        private readonly List<(PeriodicTimer timer, CancellationTokenSource cts, Task loopTask)> _periodicLoops = new();

        private readonly Stopwatch _stopwatch = new();
        private readonly Func<CancellationToken, Task> _runTask;
        private readonly object _sync = new();

        private CancellationTokenSource? _cts;
        private Task? _runLoopTask;

        public event Action<RunnerState>? StateChanged;
        public event Action<Exception>? Faulted;

        public RunnerState State { get; private set; } = RunnerState.Stopped;
        public TimeSpan RunElapsed => _stopwatch.Elapsed;

        public UiTaskRunner(Func<CancellationToken, Task> runTask)
        {
            _runTask = runTask ?? throw new ArgumentNullException(nameof(runTask));
        }

        public void Start()
        {
            lock (_sync)
            {
                if (State is RunnerState.Running or RunnerState.Stopping)
                    return;

                _stopwatch.Reset();
                _stopwatch.Start();

                _cts = new CancellationTokenSource();
                var rootToken = _cts.Token;

                ChangeState(RunnerState.Running);
                StartPeriodicLoops(rootToken);

                _runLoopTask = Task.Run(async () =>
                {
                    try
                    {
                        await _runTask(rootToken);
                        ChangeState(RunnerState.Stopped);
                    }
                    catch (OperationCanceledException)
                    {
                        ChangeState(RunnerState.Stopped);
                    }
                    catch (Exception ex)
                    {
                        ChangeState(RunnerState.Faulted);
                        Faulted?.Invoke(ex);
                    }
                    finally
                    {
                        await StopInternalAsync();
                    }
                });
            }
        }

        public async Task StopAsync()
        {
            Task? runnerTask;
            lock (_sync)
            {
                if (State == RunnerState.Stopped)
                    return;

                ChangeState(RunnerState.Stopping);
                _cts?.Cancel();
                runnerTask = _runLoopTask;
            }

            if (runnerTask != null)
            {
                try
                {
                    await runnerTask;
                }
                catch
                {
                    // 异常已在 runner 内部处理并通过事件上报
                }
            }
        }

        private void StartPeriodicLoops(CancellationToken rootToken)
        {
            foreach (var (interval, onTick) in _periodicDefinitions)
            {
                var timer = new PeriodicTimer(interval);
                var cts = CancellationTokenSource.CreateLinkedTokenSource(rootToken);

                var loopTask = Task.Run(async () =>
                {
                    try
                    {
                        while (await timer.WaitForNextTickAsync(cts.Token))
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
                }, cts.Token);

                _periodicLoops.Add((timer, cts, loopTask));
            }
        }

        private async Task StopInternalAsync()
        {
            if (_stopwatch.IsRunning)
                _stopwatch.Stop();

            foreach (var (_, cts, _) in _periodicLoops)
            {
                try { cts.Cancel(); } catch { }
            }

            var allLoopTasks = _periodicLoops.Select(x => x.loopTask).ToArray();
            try
            {
                await Task.WhenAll(allLoopTasks);
            }
            catch
            {
            }

            foreach (var (timer, cts, _) in _periodicLoops)
            {
                timer.Dispose();
                cts.Dispose();
            }

            _periodicLoops.Clear();

            _cts?.Dispose();
            _cts = null;
            _runLoopTask = null;

            if (State == RunnerState.Stopping)
                ChangeState(RunnerState.Stopped);
        }

        public void SetPeriodicAction(TimeSpan interval, Func<Task> onTick)
        {
            if (interval <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(interval));
            if (onTick == null)
                throw new ArgumentNullException(nameof(onTick));

            _periodicDefinitions.Add((interval, onTick));
        }

        private void ChangeState(RunnerState state)
        {
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
