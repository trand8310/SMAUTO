using BDAd.LandingPolicy;
using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Plugins;

namespace BDAd.Models;



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

    public IPage? Page { get; private set; }

    public ICDPSession? CdpSession { get; private set; }

    public CDPSessionManager? CdpManager { get; set; }

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