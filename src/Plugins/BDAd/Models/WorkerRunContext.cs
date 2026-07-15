using BDAd.LandingPolicy;
using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Plugins;

namespace BDAd.Models
{
    using Microsoft.Playwright;

    public sealed class WorkerRunContext : IAsyncDisposable
    {
        private IPage? _page;

        public WorkerRunContext(TaskConfig config)
        {
            Config = config;
            StartTime = DateTime.Now;
        }

        public TaskConfig Config { get; }

        public DateTime StartTime { get; }

        public IPlaywright? Playwright { get; set; }

        public IBrowser? Browser { get; set; }

        public IBrowserContext? Context { get; set; }

        /// <summary>
        /// 当前业务页面。
        /// 建议不要在外部直接赋值，统一调用 SwitchPageAsync。
        /// </summary>
        public IPage? Page
        {
            get => _page;
            private set => _page = value;
        }

        /// <summary>
        /// 当前 BrowserContext 对应的真人操作会话。
        /// 一个 BrowserContext 创建一个即可。
        /// </summary>
        public HumanBrowserSession? HumanSession { get; private set; }

        /// <summary>
        /// 当前页面对应的真人操作器。
        /// </summary>
        public HumanPageOperator Human
        {
            get
            {
                if (HumanSession == null)
                {
                    throw new InvalidOperationException(
                        "HumanBrowserSession 尚未初始化。");
                }

                if (Page == null || Page.IsClosed)
                {
                    throw new InvalidOperationException(
                        "当前没有可操作的页面。");
                }

                return HumanSession.For(Page);
            }
        }

        public ICDPSession? CdpSession { get; private set; }

        public CDPSessionManager? CdpManager { get; set; }

        public LandingPageStrategyDispatcher? LandingDispatcher { get; set; }

        public int TriggerDownloadSign;

        public int PageAdsCount { get; set; }

        public bool PageTriggerClick { get; set; }

        public bool JumpClick { get; set; }

        public int PagesCount { get; set; }

        public string CurrentPageUrl { get; set; } = "";

        public int PvIndex { get; set; }

        public bool ProxyFailed { get; set; }

        public string? ProxyFailedReason { get; set; }

        public bool PageCrashed { get; set; }

        public string? LastFailureReason { get; set; }

        public int PageElementGuardStarted;

        public SemaphoreSlim CleanupLock { get; } = new(1, 1);

        /// <summary>
        /// BrowserContext 创建完成后初始化真人操作会话。
        /// </summary>
        public async Task InitializeHumanSessionAsync(
            IPage initialPage,
            HumanBehaviorProfile? profile = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(initialPage);

            if (Context == null)
            {
                throw new InvalidOperationException(
                    "必须先创建 BrowserContext。");
            }

            HumanSession?.Dispose();

            HumanSession = new HumanBrowserSession(
                Context,
                profile ?? HumanBehaviorProfile.Normal());

            await SwitchPageAsync(
                initialPage,
                bringToFront: false,
                recreateCdpSession: true,
                cancellationToken);
        }

        /// <summary>
        /// 统一切换当前页面。
        /// 同时更新 Page、HumanSession 和 CDP Session。
        /// </summary>
        public async Task SwitchPageAsync(
            IPage page,
            bool bringToFront = true,
            bool recreateCdpSession = true,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(page);

            if (page.IsClosed)
            {
                throw new InvalidOperationException(
                    "不能切换到已经关闭的页面。");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (HumanSession != null)
            {
                await HumanSession.SwitchToAsync(
                    page,
                    bringToFront,
                    cancellationToken);
            }
            else if (bringToFront)
            {
                await page.BringToFrontAsync();
            }

            Page = page;
            CurrentPageUrl = page.Url;

            if (recreateCdpSession)
            {
                await RecreateCdpSessionAsync(page);
            }
        }

        /// <summary>
        /// 点击并等待打开新页面，成功后自动切换。
        /// </summary>
        public async Task<IPage> ClickAndSwitchToPopupAsync(
            ILocator locator,
            int timeoutMilliseconds = 15_000,
            CancellationToken cancellationToken = default)
        {
            if (HumanSession == null)
            {
                throw new InvalidOperationException(
                    "HumanBrowserSession 尚未初始化。");
            }

            IPage popup =
                await HumanSession.ClickAndSwitchToPopupAsync(
                    locator,
                    timeoutMilliseconds,
                    cancellationToken);

            // HumanBrowserSession 已经切换了内部 ActivePage，
            // 这里同步 WorkerRunContext 的 Page 和 CDP Session。
            await SwitchPageAsync(
                popup,
                bringToFront: false,
                recreateCdpSession: true,
                cancellationToken);

            return popup;
        }

        private async Task RecreateCdpSessionAsync(IPage page)
        {
            if (Context == null)
            {
                CdpSession = null;
                return;
            }

            // ICDPSession 是绑定具体页面 Target 的。
            // 页面切换后，旧的 CDP Session 不应该继续使用。
            if (CdpSession != null)
            {
                try
                {
                    await CdpSession.DetachAsync();
                }
                catch
                {
                    // 页面关闭或 Target 消失时，Detach 可能失败。
                }

                CdpSession = null;
            }

            CdpSession = await Context.NewCDPSessionAsync(page);
        }

        public void ResetPerPvState()
        {
            TriggerDownloadSign = 0;
            PageTriggerClick = false;
            JumpClick = false;
            PagesCount = 0;
            CurrentPageUrl = string.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            await CleanupLock.WaitAsync();

            try
            {
                HumanSession?.Dispose();
                HumanSession = null;

                if (CdpSession != null)
                {
                    try
                    {
                        await CdpSession.DetachAsync();
                    }
                    catch
                    {
                    }

                    CdpSession = null;
                }

                if (Context != null)
                {
                    try
                    {
                        await Context.CloseAsync();
                    }
                    catch
                    {
                    }

                    Context = null;
                }

                if (Browser != null)
                {
                    try
                    {
                        await Browser.CloseAsync();
                    }
                    catch
                    {
                    }

                    Browser = null;
                }

                Playwright?.Dispose();
                Playwright = null;

                Page = null;
            }
            finally
            {
                CleanupLock.Release();
                CleanupLock.Dispose();
            }
        }
    }
}
