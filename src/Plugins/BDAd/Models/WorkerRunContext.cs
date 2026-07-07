using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Plugins;
using BDAd.LandingPolicy;

namespace BDAd.Models
{
    public sealed class WorkerRunContext
    {
        public WorkerRunContext(TaskConfig config)
        {
            Config = config;
            StartTime = DateTime.Now;
            SwipeStyleProfile = HumanSwipeStyleProfile.CreateRandom();
        }

        public TaskConfig Config { get; }
        public DateTime StartTime { get; }
        public HumanSwipeStyleProfile SwipeStyleProfile { get; }

        public IPlaywright? Playwright { get; set; }
        public IBrowser? Browser { get; set; }
        public IBrowserContext? Context { get; set; }
        public IPage? Page { get; set; }
        public ICDPSession? CdpSession { get; set; }
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
        public void ResetPerPvState()
        {

            TriggerDownloadSign = 0;
            PageTriggerClick = false;
            JumpClick = false;
            PagesCount = 0;
            CurrentPageUrl = string.Empty;
        }
    }
}
