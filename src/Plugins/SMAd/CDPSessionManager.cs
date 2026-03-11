using Microsoft.Playwright;
using System.Collections.Concurrent;

namespace QTP.Plugins
{
    public class CDPSessionManager
    {
        private readonly IBrowserContext _context;
        // 用 Lazy<Task> 包装异步创建，确保并发时只创建一个会话
        private readonly ConcurrentDictionary<IPage, Lazy<Task<ICDPSession>>> _sessionMap = new();
        public CDPSessionManager(IBrowserContext context)
        {
            _context = context;

            _context.Page += (_, page) =>
            {
                page.Close += (_, _) =>
                {
                    // 页面关闭时移除缓存
                    _sessionMap.TryRemove(page, out var _);
                };
            };
        }

        public Task<ICDPSession> GetOrCreateSessionAsync(IPage page)
        {
            // GetOrAdd 保证原子操作
            var lazySession = _sessionMap.GetOrAdd(page, p =>
                new Lazy<Task<ICDPSession>>(() => _context.NewCDPSessionAsync(p))
            );

            return lazySession.Value;
        }

        public bool HasSession(IPage page)
        {
            return _sessionMap.ContainsKey(page);
        }

        public void RemoveSession(IPage page)
        {
            _sessionMap.TryRemove(page, out var _);
        }

        public IReadOnlyDictionary<IPage, Lazy<Task<ICDPSession>>> GetAllSessions()
        {
            return _sessionMap;
        }
    }
}
