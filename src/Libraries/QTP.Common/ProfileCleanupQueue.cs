using System.Threading.Channels;

namespace QTP.Common;

/// <summary>Failed deletions wait outside workers, so locked profiles cannot block other profiles.</summary>
public sealed class ProfileCleanupQueue : IAsyncDisposable
{
    private sealed class Job(string path)
    {
        public readonly string Path = path;
        public int Attempts;
        public string? LastError;
        public readonly TaskCompletionSource<ProfileCleanupResult> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object _sync = new();
    private readonly Dictionary<string, Job> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<Job> _queue = Channel.CreateUnbounded<Job>();
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<string, Task>? _delete;
    private readonly TimeSpan[] _retryDelays;
    private readonly Task[] _workers;
    private readonly HashSet<Task> _retries = new();
    private TaskCompletionSource _drained = Completed();
    private bool _stopping;
    private Task? _disposal;
    public int PendingCount { get { lock (_sync) return _pending.Count; } }
    public ProfileCleanupQueue(int concurrency = 4, Func<string, Task>? delete = null, TimeSpan[]? retryDelays = null)
    {
        if (concurrency < 1) throw new ArgumentOutOfRangeException(nameof(concurrency));
        _delete = delete;
        _retryDelays = retryDelays ?? [TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];
        if (_retryDelays.Any(x => x < TimeSpan.Zero)) throw new ArgumentOutOfRangeException(nameof(retryDelays));
        _workers = Enumerable.Range(0, concurrency).Select(_ => Task.Run(WorkAsync)).ToArray();
    }
    public Task<ProfileCleanupResult> Enqueue(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (path == Path.GetPathRoot(path)) throw new ArgumentException("A filesystem root cannot be a browser profile.", nameof(path));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_pending.TryGetValue(path, out var current)) return current.Result.Task;
            var job = new Job(path);
            if (_pending.Count == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(path, job); _queue.Writer.TryWrite(job);
            return job.Result.Task;
        }
    }
    public Task DrainAsync() { lock (_sync) return _drained.Task; }
    private async Task WorkAsync()
    {
        try
        {
            await foreach (var job in _queue.Reader.ReadAllAsync(_stop.Token).ConfigureAwait(false))
            {
                job.Attempts++;
                try
                {
                    if (_delete != null)
                    {
                        await _delete(job.Path).ConfigureAwait(false);
                        Complete(job, true);
                    }
                    else
                    {
                        var error = await Task.Run(() => TryDeleteProfile(job.Path)).ConfigureAwait(false);
                        if (error == null) Complete(job, true);
                        else RetryOrComplete(job, error);
                    }
                }
                catch (Exception ex)
                {
                    RetryOrComplete(job, ex.Message);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private static string? TryDeleteProfile(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            return null;
        }
        catch (DirectoryNotFoundException) { return null; }
        // Catch expected filesystem failures in the same Task delegate that
        // deletes the directory. They are retry results, not faulted Tasks.
        catch (IOException ex) { return ex.Message; }
        catch (UnauthorizedAccessException ex) { return ex.Message; }
    }
    private void RetryOrComplete(Job job, string error)
    {
        job.LastError = error;
        if (job.Attempts > _retryDelays.Length || _stop.IsCancellationRequested) Complete(job, false);
        else
        {
            var retry = RetryAsync(job, _retryDelays[job.Attempts - 1]);
            lock (_sync) _retries.Add(retry);
            _ = retry.ContinueWith(t => { lock (_sync) _retries.Remove(t); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
    private async Task RetryAsync(Job job, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _stop.Token).ConfigureAwait(false);
            if (!_queue.Writer.TryWrite(job)) Complete(job, false);
        }
        catch (OperationCanceledException) { Complete(job, false); }
    }
    private void Complete(Job job, bool deleted)
    {
        lock (_sync)
        {
            if (!_pending.TryGetValue(job.Path, out var current) || !ReferenceEquals(current, job)) return;
            _pending.Remove(job.Path);
            job.Result.TrySetResult(new(job.Path, deleted, job.Attempts, deleted ? null : job.LastError ?? "Cleanup stopped before deletion"));
            if (_pending.Count == 0) _drained.TrySetResult();
        }
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _stopping = true;
            _disposal ??= Task.Run(async () =>
            {
                try { await DrainAsync().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); }
                catch (TimeoutException) { _stop.Cancel(); }
                _queue.Writer.TryComplete();
                await Task.WhenAll(_workers).ConfigureAwait(false);
                Job[] remaining;
                lock (_sync) remaining = _pending.Values.ToArray();
                foreach (var job in remaining) Complete(job, false);
                Task[] retries;
                lock (_sync) retries = _retries.ToArray();
                await Task.WhenAll(retries).ConfigureAwait(false);
                _stop.Dispose();
            });
            return new(_disposal);
        }
    }
    private static TaskCompletionSource Completed()
    { var value = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); value.SetResult(); return value; }
}
