
using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Plugins.LandingPolicy;

namespace QTP.Plugins.Models;


public sealed class WorkerRunContext
{
    public WorkerRunContext(
        TaskConfig config,
        int? humanSeed = null)
    {
        Config = config;
        StartTime = DateTime.Now;

        HumanSeedValue =
            humanSeed ?? HumanSeed.CreateRandom();

        HumanProfile =
            HumanBehaviorProfileFactory.Create(
                HumanSeedValue);
    }

    public TaskConfig Config { get; }

    public DateTime StartTime { get; }

    /// <summary>
    /// 当前 Worker 的真人画像主种子。
    /// </summary>
    public int HumanSeedValue { get; }

    /// <summary>
    /// 当前 Worker 的稳定真人画像。
    /// 页面切换和 PV 重置时都不能重新生成。
    /// </summary>
    public HumanBehaviorProfile HumanProfile { get; }

    public HumanBrowserSession? HumanSession
    {
        get;
        private set;
    }

    public IPlaywright? Playwright { get; set; }

    public IBrowser? Browser { get; set; }

    public IBrowserContext? Context { get; set; }

    private IPage? _activePage;

    private ICDPSession? _activeCdpSession;

    /// <summary>
    /// 当前 Worker 的操作页。
    /// 优先返回显式激活页；如果激活页已关闭，则自动回退到
    /// BrowserContext 中最后一个未关闭的页面，确保后续人工操作落到当前页。
    /// </summary>
    public IPage? Page => GetCurrentPage();

    /// <summary>
    /// 当前操作页对应的 CDP Session。
    /// 该属性只返回已经创建好的 Session；需要确保 Session 存在时请调用
    /// <see cref="GetCurrentPageSessionAsync"/>。
    /// </summary>
    public ICDPSession? CdpSession => GetCurrentCdpSession();

    public CDPSessionManager? CdpManager { get; set; }

    public IPage? GetCurrentPage()
    {
        if (_activePage is { IsClosed: false })
            return _activePage;

        var page = Context?.Pages.LastOrDefault(x => !x.IsClosed);
        if (page != null)
            _activePage = page;

        return _activePage is { IsClosed: false } ? _activePage : null;
    }

    public ICDPSession? GetCurrentCdpSession()
    {
        var page = GetCurrentPage();
        if (page == null)
            return null;

        if (ReferenceEquals(page, _activePage) && _activeCdpSession != null)
            return _activeCdpSession;

        if (CdpManager != null && CdpManager.TryGetSession(page, out var session))
        {
            _activePage = page;
            _activeCdpSession = session;
            return session;
        }

        return ReferenceEquals(page, _activePage) ? _activeCdpSession : null;
    }

    public async Task<(IPage Page, ICDPSession CdpSession)> GetCurrentPageSessionAsync()
    {
        var page = GetCurrentPage()
            ?? throw new InvalidOperationException("当前没有可用页面。");

        return await GetPageSessionAsync(page).ConfigureAwait(false);
    }

    public async Task<(IPage Page, ICDPSession CdpSession)> GetPageSessionAsync(IPage page)
    {
        if (page == null)
            throw new ArgumentNullException(nameof(page));

        if (page.IsClosed)
            throw new InvalidOperationException("指定页面已经关闭。");

        if (CdpManager == null)
            throw new InvalidOperationException("CDP Session 管理器尚未初始化。");

        var session = await CdpManager.GetOrCreateSessionAsync(page).ConfigureAwait(false);
        SetActivePage(page, session);
        return (page, session);
    }

    public void SetActivePage(IPage page, ICDPSession? cdpSession = null)
    {
        if (page == null)
            throw new ArgumentNullException(nameof(page));

        if (page.IsClosed)
            throw new InvalidOperationException("指定页面已经关闭。");

        _activePage = page;
        _activeCdpSession = cdpSession;
    }

    public void ClearActivePage(IPage page)
    {
        if (!ReferenceEquals(_activePage, page))
            return;

        _activePage = null;
        _activeCdpSession = null;
    }

    public LandingPageStrategyDispatcher?
        LandingDispatcher
    { get; set; }

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

    public SemaphoreSlim CleanupLock { get; } =
        new(1, 1);

    public void InitializeHumanSession()
    {
        if (Context == null)
        {
            throw new InvalidOperationException(
                "BrowserContext 尚未初始化。");
        }

        HumanSession?.Dispose();

        HumanSession = new HumanBrowserSession(
            Context,
            HumanProfile,
            HumanSeedValue);
    }

    public void ResetPerPvState()
    {
        TriggerDownloadSign = 0;
        PageTriggerClick = false;
        JumpClick = false;
        PagesCount = 0;
        CurrentPageUrl = string.Empty;

        // 不重置：
        // HumanSeedValue
        // HumanProfile
        // HumanSession
    }
}