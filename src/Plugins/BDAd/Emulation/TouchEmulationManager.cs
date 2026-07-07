
namespace BDAd.Emulation
{
    using global::BDAd.Models;
    using Microsoft.Playwright;
    using QTP.Plugins;
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    public sealed class TouchEmulationManager : IAsyncDisposable
    {
        private readonly WorkerRunContext _ctx;
        private readonly IPage _page;
        private readonly CDPSessionManager _manager;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private volatile bool _disposed;
        private volatile bool _enabled;
        private int _maxTouchPoints;

        public TouchEmulationManager(WorkerRunContext ctx, IPage page, CDPSessionManager manager, int maxTouchPoints = 5)
        {
            _ctx = ctx;
            _page = page ?? throw new ArgumentNullException(nameof(page));
            _manager = manager;
            _maxTouchPoints = maxTouchPoints;
        }

        public async Task StartAsync()
        {
            if (_disposed)
                return;
            _enabled = true;
            BindEvents();
            await ReapplyAsync("start");
        }

        public async Task StopAsync()
        {
            _enabled = false;
            await Task.CompletedTask;
            //await SendTouchEmulationAsync(false, _maxTouchPoints, "stop");
        }

        private void BindEvents()
        {
            _page.FrameNavigated += OnFrameNavigated;
            //_page.DOMContentLoaded += OnDOMContentLoaded;
            //_page.Load += OnLoad;
            //_page.Crash += OnCrash;
            //_page.Close += OnClose;
        }

        private void UnbindEvents()
        {
            _page.FrameNavigated -= OnFrameNavigated;
            _page.DOMContentLoaded -= OnDOMContentLoaded;
            _page.Load -= OnLoad;
            _page.Crash -= OnCrash;
            _page.Close -= OnClose;
        }

        private void OnFrameNavigated(object? sender, IFrame frame)
        {
            if (_disposed || !_enabled)
                return;

            // 只处理主 Frame，避免 iframe 每次跳转都重复刷
            if (frame == _page.MainFrame)
            {
                _ = Task.Run(async () =>
                {
                    await DelayAndReapplyAsync("main-frame-navigated");
                });
            }
        }

        private void OnDOMContentLoaded(object? sender, IPage page)
        {
            if (_disposed || !_enabled)
                return;

            //_ = Task.Run(async () =>
            //{
            //    await DelayAndReapplyAsync("domcontentloaded");
            //});
        }

        private void OnLoad(object? sender, IPage page)
        {
            if (_disposed || !_enabled)
                return;

            //_ = Task.Run(async () =>
            //{
            //    await DelayAndReapplyAsync("load");
            //});
        }

        private void OnCrash(object? sender, IPage page)
        {
            // Renderer 崩了，旧 CDP Session 不可靠

        }

        private void OnClose(object? sender, IPage page)
        {
            _disposed = true;

        }

        private async Task DelayAndReapplyAsync(string reason)
        {
            // 给 renderer / frame 切换一点时间，避免导航中 CDP 命令打早了
           //await Task.Delay(50);
            await ReapplyAsync(reason);
        }


        public async Task ReapplyAsync(string reason = "manual")
        {
            if (_disposed || _page.IsClosed || !_enabled)
                return;

            await SendTouchEmulationAsync(true, _maxTouchPoints, reason);
        }

        private async Task SendTouchEmulationAsync(bool enabled, int maxTouchPoints, string reason)
        {
            if (_disposed || _page.IsClosed)
                return;

            await _lock.WaitAsync();

            try
            {
                if (_disposed || _page.IsClosed)
                    return;

                var _cdp = await _manager.GetOrCreateSessionAsync(_page);

                //await _cdp.SendAsync("Emulation.setTouchEmulationEnabled",
                //    new Dictionary<string, object>
                //    {
                //        ["enabled"] = enabled,
                //        ["maxTouchPoints"] = maxTouchPoints
                //    });


                await _cdp.SendAsync("Emulation.setTouchEmulationEnabled",
                    new Dictionary<string, object>() {
                        {"enabled",enabled },
                        {"maxTouchPoints",maxTouchPoints },
                    });

                await _cdp.SendAsync("Emulation.setDeviceMetricsOverride", new Dictionary<string, object>() {
                    {"width",_ctx.Config.Sw },
                    {"height",_ctx.Config.Sh },
                    {"scale",1.0f },
                    {"screenWidth",_ctx.Config.Sw },
                    {"screenHeight",_ctx.Config.Sh },
                    {"positionX",0 },
                    {"positionY",0 },
                    {"deviceScaleFactor",_ctx.Config.DeviceScale },
                    {"mobile",enabled },
                });







                await _cdp.SendAsync("Emulation.setScrollbarsHidden",
                    new Dictionary<string, object>() {
                        {"hidden",enabled },
                    });


                //await CDPHelper.SetTouchEmulationEnabled(cdpSession, true, maxTouchPoints);
                //await CDPHelper.SetScrollbarsHidden(cdpSession, true);

                // 可选：如果你还需要鼠标事件转触摸事件，再打开这个。
                // 但你前面说要混用人工鼠标和代码 touch，这个不建议默认开。
                //
                // await _cdp.SendAsync("Emulation.setEmitTouchEventsForMouse",
                //     new Dictionary<string, object>
                //     {
                //         ["enabled"] = enabled,
                //         ["configuration"] = "mobile"
                //     });
            }
            catch (PlaywrightException)
            {
                // 导航、target 关闭、renderer 切换时可能失败。
                // 下次事件触发时重新创建 session。

            }
            catch
            {

            }
            finally
            {
                _lock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            _enabled = false;

            UnbindEvents();

            try
            {
                if (!_page.IsClosed)
                {
                    //await SendTouchEmulationAsync(false, _maxTouchPoints, "dispose");
                    await Task.CompletedTask;
                }
            }
            catch
            {
                // ignore
            }
            _lock.Dispose();
        }
    }
}
