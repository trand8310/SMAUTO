

namespace QTP.Common
{
    using Microsoft.Playwright;
    using System.Threading;

    public sealed class PlaywrightProvider : IPlaywrightProvider, IAsyncDisposable
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Task<IPlaywright>? _instanceTask;

        public async Task<IPlaywright> GetAsync()
        {
            var task = Volatile.Read(ref _instanceTask);
            if (task != null)
                return await task;

            await _lock.WaitAsync();
            try
            {
                task = _instanceTask;
                if (task == null)
                {
                    task = Playwright.CreateAsync();
                    _instanceTask = task;
                }
            }
            finally
            {
                _lock.Release();
            }

            return await task;
        }

        public async ValueTask DisposeAsync()
        {
            var task = Volatile.Read(ref _instanceTask);
            if (task == null)
                return;

            try
            {
                var pw = await task;
                if (pw is IAsyncDisposable d)
                    await d.DisposeAsync();
            }
            finally
            {
                _instanceTask = null;
                _lock.Dispose();
            }
        }
    }
}
