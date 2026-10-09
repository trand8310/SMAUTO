using Microsoft.Playwright;

namespace QTP.Common;

/// <summary>Generations isolate replacement of a failed driver from existing borrowers.</summary>
internal sealed class ManagedPlaywrightProvider : IPlaywrightProvider, IAsyncDisposable
{
    private sealed class Generation
    {
        public required Task<IPlaywright> Creation;
        public readonly CancellationTokenSource Health = new();
        public int Borrowers;
        public bool Retired;
        public Task? Disposal;
    }
    private readonly object _sync = new();
    private readonly Func<Task<IPlaywright>> _factory;
    private readonly List<Generation> _generations = new();
    private Generation? _current;
    private bool _stopping;
    private Task? _disposal;
    private TaskCompletionSource _drained = Completed();
    private int _borrowers;
    public ManagedPlaywrightProvider(Func<Task<IPlaywright>> factory) => _factory = factory;
    public int ActiveLeases { get { lock (_sync) return _borrowers; } }

    public Task<IPlaywright> GetAsync() => GetAsync(default);
    public async Task<IPlaywright> GetAsync(CancellationToken token)
    {
        // Legacy borrowed access: callers must finish before application shutdown.
        await using var lease = await AcquireAsync(token).ConfigureAwait(false);
        return lease.Playwright;
    }

    public async Task<PlaywrightLease> AcquireAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        Generation entry;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (_current == null)
            {
                entry = new() { Creation = Task.Run(CreateAsync) };
                _generations.Add(entry); _current = entry;
            }
            else entry = _current;
            if (_borrowers++ == 0) _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
            entry.Borrowers++;
        }
        try
        {
            var driver = await entry.Creation.WaitAsync(token).ConfigureAwait(false);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_stopping, this);
                if (entry.Retired) throw new InvalidOperationException("Playwright driver was retired during acquisition.");
                return new(driver, () => ReleaseAsync(entry), entry.Health.Token, entry);
            }
        }
        catch
        {
            if (entry.Creation.IsFaulted || entry.Creation.IsCanceled) Retire(entry);
            await ReleaseAsync(entry).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<IPlaywright> CreateAsync()
    {
        var creating = _factory();
        try { return await creating.WaitAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false); }
        catch
        {
            _ = DisposeLateCreationAsync(creating);
            throw;
        }
    }
    private static async Task DisposeLateCreationAsync(Task<IPlaywright> creating)
    {
        try { (await creating.ConfigureAwait(false)).Dispose(); } catch { }
    }

    public void Invalidate(PlaywrightLease lease)
    {
        if (lease.OwnerTag is Generation entry)
        {
            lock (_sync) if (!_generations.Contains(entry)) throw new ArgumentException("Lease belongs to another provider.");
            Retire(entry);
        }
        else throw new ArgumentException("Lease belongs to another provider.");
    }
    private void Retire(Generation entry)
    {
        lock (_sync)
        {
            if (entry.Retired) return;
            entry.Retired = true;
            if (ReferenceEquals(_current, entry)) _current = null;
        }
        // Cancellation callbacks can release leases; never invoke them under the state lock.
        try { entry.Health.Cancel(); } catch (AggregateException) { } catch (ObjectDisposedException) { }
        lock (_sync) if (entry.Borrowers == 0) ScheduleDisposal(entry);
    }
    private ValueTask ReleaseAsync(Generation entry)
    {
        lock (_sync)
        {
            entry.Borrowers--; _borrowers--;
            if (entry.Retired && entry.Borrowers == 0) ScheduleDisposal(entry);
            if (_borrowers == 0) _drained.TrySetResult();
        }
        return ValueTask.CompletedTask;
    }
    private static void ScheduleDisposal(Generation entry)
    {
        entry.Disposal ??= Task.Run(async () =>
        {
            IPlaywright driver;
            try { driver = await entry.Creation.ConfigureAwait(false); }
            catch { entry.Health.Dispose(); return; }
            try { driver.Dispose(); } finally { entry.Health.Dispose(); }
        });
    }
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_disposal != null) return new(_disposal);
            _stopping = true;
            _disposal = Task.Run(async () =>
            {
                Generation[] entries;
                lock (_sync) entries = _generations.ToArray();
                foreach (var entry in entries) Retire(entry);
                Task drained;
                lock (_sync) drained = _drained.Task;
                await drained.ConfigureAwait(false);
                Task[] disposals;
                lock (_sync)
                {
                    foreach (var entry in entries) ScheduleDisposal(entry);
                    disposals = entries.Select(x => x.Disposal!).ToArray();
                }
                await Task.WhenAll(disposals).ConfigureAwait(false);
            });
            return new(_disposal);
        }
    }
    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(); return source;
    }
}
