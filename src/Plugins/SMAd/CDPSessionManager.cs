
 
namespace QTP.Plugins
{
    using Microsoft.Playwright;
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class CDPSessionManager
    {
        private sealed class SessionEntry
        {
            public required Lazy<Task<ICDPSession>> LazySession { get; init; }
            public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        }

        private readonly IBrowserContext _context;
        private readonly ConcurrentDictionary<IPage, SessionEntry> _sessionMap = new();

        public CDPSessionManager(IBrowserContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));

            _context.Page += (_, page) =>
            {
                page.Close += (_, _) =>
                {
                    RemoveSession(page);
                };
            };
        }

        public async Task<ICDPSession> GetOrCreateSessionAsync(IPage page)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));

            var entry = _sessionMap.GetOrAdd(page, CreateEntry);

            try
            {
                return await entry.LazySession.Value.ConfigureAwait(false);
            }
            catch
            {
                // 创建失败后把坏缓存清掉，避免后续一直拿到 faulted task
                _sessionMap.TryRemove(new KeyValuePair<IPage, SessionEntry>(page, entry));
                throw;
            }
        }

        public bool ContainsPage(IPage page)
        {
            if (page == null)
                return false;

            return _sessionMap.ContainsKey(page);
        }

        public bool RemoveSession(IPage page)
        {
            if (page == null)
                return false;

            return _sessionMap.TryRemove(page, out _);
        }

        public int Count => _sessionMap.Count;

        public IReadOnlyCollection<IPage> GetTrackedPages()
        {
            return _sessionMap.Keys.ToArray();
        }

        public async Task<bool> TryWarmupSessionAsync(IPage page)
        {
            try
            {
                await GetOrCreateSessionAsync(page).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Clear()
        {
            _sessionMap.Clear();
        }

        private SessionEntry CreateEntry(IPage page)
        {
            return new SessionEntry
            {
                LazySession = new Lazy<Task<ICDPSession>>(
                    () => CreateSessionCoreAsync(page),
                    LazyThreadSafetyMode.ExecutionAndPublication)
            };
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
    }
}
