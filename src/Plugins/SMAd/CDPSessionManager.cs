
 
namespace QTP.Plugins
{
    using Microsoft.Playwright;
    using PlaywrightHumanInput;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class CDPSessionManager : IAsyncDisposable
    {
        private sealed class SessionEntry
        {
            public required Lazy<Task<ICDPSession>> LazySession { get; init; }
            public EventHandler<IPage>? CloseHandler;
            public EventHandler<ICDPSession>? SessionCloseHandler;
            public int Removed;
        }

        private readonly IBrowserContext _context;
        private readonly ConcurrentDictionary<IPage, SessionEntry> _sessionMap = new();
        private readonly EventHandler<IPage> _contextPageHandler;
        private int _disposeStarted;
        private readonly object _sync = new();
        private readonly List<Task> _cleanup = new();
        private Task? _disposeTask;

        public CDPSessionManager(IBrowserContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));

            _contextPageHandler = (_, page) => AttachPageCloseHandler(page);
            _context.Page += _contextPageHandler;

            foreach (var existingPage in _context.Pages)
            {
                AttachPageCloseHandler(existingPage);
            }
        }

        public async Task<ICDPSession> GetOrCreateSessionAsync(IPage page)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            if (page.Context is { } owner && !ReferenceEquals(owner, _context))
                throw new InvalidOperationException("The page belongs to another browser context.");

            SessionEntry entry;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
                entry = _sessionMap.GetOrAdd(page, CreateEntry);
                AttachPageCloseHandler(page);
                _ = entry.LazySession.Value;
            }

            try
            {
                var session = await entry.LazySession.Value.ConfigureAwait(false);
                ObjectDisposedException.ThrowIf(_disposeStarted != 0, this);
                if (Volatile.Read(ref entry.Removed) != 0 || page.IsClosed)
                    throw new InvalidOperationException("The page session has closed.");
                return session;
            }
            catch
            {
                // 创建失败后把坏缓存清掉，避免后续一直拿到 faulted task
                RemoveEntry(page, entry);
                throw;
            }
        }


        public bool RemoveSession(IPage page)
        {
            if (page == null)
                return false;

            lock (_sync)
            {
                return _sessionMap.TryGetValue(page, out var entry) && RemoveEntry(page, entry);
            }
        }

        private bool RemoveEntry(IPage page, SessionEntry entry)
        {
            lock (_sync)
            {
                if (Interlocked.Exchange(ref entry.Removed, 1) != 0) return false;
                _sessionMap.TryRemove(new KeyValuePair<IPage, SessionEntry>(page, entry));
                if (entry.CloseHandler != null) page.Close -= entry.CloseHandler;
                _cleanup.Add(DisposeSessionEntryAsync(entry));
                return true;
            }
        }

        public int Count => _sessionMap.Count;



        public void Clear()
        {
            foreach (var pair in _sessionMap.ToArray())
            {
                RemoveSession(pair.Key);
            }
        }

        private SessionEntry CreateEntry(IPage page)
        {
            SessionEntry entry = null!;
            entry = new SessionEntry
            {
                LazySession = new Lazy<Task<ICDPSession>>(
                    async () => {
                        var session = await CreateSessionCoreAsync(page);
                        EventHandler<ICDPSession> closed = (_, _) => { CdpTouchRuntime.Invalidate(session); RemoveEntry(page, entry); };
                        session.Close += closed;
                        entry.SessionCloseHandler = closed;
                        return session;
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication)
            };
            return entry;
        }

        private void AttachPageCloseHandler(IPage page)
        {
            lock (_sync)
            {
                if (page == null || Volatile.Read(ref _disposeStarted) != 0)
                    return;

                var entry = _sessionMap.GetOrAdd(page, CreateEntry);
                if (entry.CloseHandler != null)
                    return;

                EventHandler<IPage> closeHandler = (_, _) => RemoveEntry(page, entry);
                if (Interlocked.CompareExchange(ref entry.CloseHandler, closeHandler, null) == null)
                {
                    page.Close += closeHandler;
                }
            }
        }

        private async Task<ICDPSession> CreateSessionCoreAsync(IPage page)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));

            // 页面可能已经关了，提前拦一下
            if (page.IsClosed)
                throw new InvalidOperationException("Page is already closed.");

            return await _context.NewCDPSessionAsync(page).ConfigureAwait(false);
        }

        private static async Task DisposeSessionEntryAsync(SessionEntry entry)
        {
            if (!entry.LazySession.IsValueCreated)
                return;

            try
            {
                var session = await entry.LazySession.Value.ConfigureAwait(false);
                if (entry.SessionCloseHandler != null) session.Close -= entry.SessionCloseHandler;
                CdpTouchRuntime.Lease? lease = null;
                try
                {
                    try { lease = await CdpTouchRuntime.StopAsync(session); } catch { }
                    await session.DetachAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally { lease?.Dispose(); }
            }
            catch
            {
            }
        }

        public ValueTask DisposeAsync()
        {
            KeyValuePair<IPage, SessionEntry>[] entries;
            lock (_sync)
            {
                if (_disposeTask != null) return new ValueTask(_disposeTask);
                Interlocked.Exchange(ref _disposeStarted, 1);
                entries = _sessionMap.ToArray();
                foreach (var pair in entries) RemoveEntry(pair.Key, pair.Value);
                _context.Page -= _contextPageHandler;
                _disposeTask = Task.WhenAll(_cleanup.ToArray());
                return new ValueTask(_disposeTask);
            }
        }
    }
}
