using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace QTP.Common
{
    public static class RemotePortManager
    {
        const int min_port = 30000;
        const int max_port = 50000;

        private static int _next = 30000;
        private static readonly ConcurrentQueue<int> _pool = new();

        public static int AcquirePort()
        {
            while (true)
            {
                int port;

                if (_pool.TryDequeue(out var p))
                    port = p;
                else
                    port = Interlocked.Increment(ref _next);

                if (port > max_port)
                    Interlocked.Exchange(ref _next, min_port);

                if (!IsPortInUse(port))
                    return port;
            }
        }



        //public static int AcquirePort()
        //{
        //    if (_pool.TryDequeue(out var p))
        //        return p;

        //    return Interlocked.Increment(ref _next);
        //}
        //public int AcquirePort()
        //{
        //    lock (_lock)
        //    {
        //        for (int port = _startPort; port <= _endPort; port++)
        //        {
        //            if (!_usedPorts.Contains(port) && !IsPortInUse(port))
        //            {
        //                _usedPorts.Add(port);
        //                return port;
        //            }
        //        }
        //    }
        //    throw new InvalidOperationException("No available port.");
        //}


        public static bool IsPortInUse(int port)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return false;
            }
            catch
            {
                return true;
            }
        }

        public static void Release(int port)
        {
            _pool.Enqueue(port);
        }
    }
}
