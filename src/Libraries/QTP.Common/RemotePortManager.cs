
namespace QTP.Common
{
    using System.Collections.Concurrent;
    using System.Net;
    using System.Net.Sockets;

 
    public static class RemotePortManager
    {
        private const int MinPort = 34567;
        private const int MaxPort = 56789;

        // 下一个优先尝试的新端口
        private static int _next = MinPort - 1;

        // 是否已经至少完整扫过一轮新端口范围
        private static bool _wrapped = false;

        // 已释放、可复用的端口（FIFO：最早释放的最先复用）
        private static readonly ConcurrentQueue<int> _recycled = new();

        // 防止同一个端口重复进入回收队列
        private static readonly ConcurrentDictionary<int, byte> _recycledSet = new();

        // 当前已经分配出去、尚未释放的端口
        private static readonly ConcurrentDictionary<int, byte> _leased = new();

        // 分配过程串行化，避免并发重复分配
        private static readonly object _sync = new();

        public static int AcquirePort()
        {
            lock (_sync)
            {
                int totalRange = MaxPort - MinPort + 1;

                // 第一阶段：优先向后产生“新端口”
                for (int i = 0; i < totalRange; i++)
                {
                    int candidate = GetNextPortUnsafe();

                    if (_leased.ContainsKey(candidate))
                        continue;

                    if (!IsPortAvailable(candidate))
                        continue;

                    if (_leased.TryAdd(candidate, 0))
                        return candidate;
                }

                // 走到这里，说明整个范围至少尝试过一轮了
                _wrapped = true;

                // 第二阶段：开始复用已释放端口，按释放先后顺序（最早释放的优先）
                while (_recycled.TryDequeue(out var recycledPort))
                {
                    _recycledSet.TryRemove(recycledPort, out _);

                    if (!IsInRange(recycledPort))
                        continue;

                    if (_leased.ContainsKey(recycledPort))
                        continue;

                    if (!IsPortAvailable(recycledPort))
                        continue;

                    if (_leased.TryAdd(recycledPort, 0))
                        return recycledPort;
                }

                throw new InvalidOperationException(
                    $"No available remote debugging port found in range [{MinPort}, {MaxPort}].");
            }
        }

        public static void Release(int port)
        {
            if (!IsInRange(port))
                return;

            // 只有真的租出去过，才允许归还
            if (!_leased.TryRemove(port, out _))
                return;

            // 没有绕完一圈前，不需要进回收池也可以；
            // 但为了避免将来 wrapped 后无可复用端口，这里仍然先记下来
            if (_recycledSet.TryAdd(port, 0))
            {
                _recycled.Enqueue(port);
            }
        }

        public static bool IsLeased(int port)
        {
            return _leased.ContainsKey(port);
        }

        public static int LeasedCount => _leased.Count;

        public static int RecycledCount => _recycledSet.Count;

        public static bool HasWrapped => _wrapped;

        private static int GetNextPortUnsafe()
        {
            _next++;
            if (_next > MaxPort)
            {
                _next = MinPort;
                _wrapped = true;
            }

            return _next;
        }

        private static bool IsInRange(int port)
        {
            return port >= MinPort && port <= MaxPort;
        }

        private static bool IsPortAvailable(int port)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
