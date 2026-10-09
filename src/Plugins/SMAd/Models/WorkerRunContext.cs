using Microsoft.Playwright;
using PlaywrightHumanInput;
using QTP.Plugins;
using SMAd.LandingPolicy;

namespace SMAd.Models
{
    public sealed class WorkerRunContext
    {
        public WorkerRunContext(TaskConfig config)
        {
            Config = config;
            StartTime = DateTime.Now;
        }
        public TaskConfig Config { get; }
        public SMAd.DeviceEmulation.DeviceTargetController? DeviceTargets { get; set; }
        public SMAd.PageActions.PageActionExecutor? Actions;
        public readonly System.Collections.Concurrent.ConcurrentDictionary<Task, byte> PageLifecycleTasks = new();
        public readonly System.Collections.Concurrent.ConcurrentQueue<SMAd.PageActions.PageActionRecord> ActionRecords = new();
        public int CompletedPvs { get; set; }
        public int CompletedClickPvs { get; private set; }
        public bool ClickRequested { get; set; }
        public bool CurrentVisitReady { get; set; }
        private bool _currentVisitCompleted;
        public void CompleteCurrentVisit()
        {
            if (!CurrentVisitReady || _currentVisitCompleted || ProxyFailed ||
                (Page != null && PageHttpStatuses.TryGetValue(Page, out var status) && status >= 400)) return;
            _currentVisitCompleted = true;
            CompletedPvs++;
            if (PageTriggerClick) CompletedClickPvs++;
        }
        public string ActionLogPath { get; } = Path.Combine(AppContext.BaseDirectory, "logs", "page-actions", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.jsonl");
        private readonly object _recordSync = new();
        public void RecordAction(SMAd.PageActions.PageActionRecord record)
        {
            lock (_recordSync)
            {
                ActionRecords.Enqueue(record);
                while (ActionRecords.Count > 500) ActionRecords.TryDequeue(out _);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(ActionLogPath)!);
                    File.AppendAllText(ActionLogPath, System.Text.Json.JsonSerializer.Serialize(record) + Environment.NewLine);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        public DateTime StartTime { get; }

        public IPlaywright? Playwright { get; set; }
        public QTP.Common.BrowserRuntimeLease? BrowserSession { get; set; }
        public QTP.Common.BrowserStopKind? BrowserFailureKind { get; set; }
        public IBrowser? Browser { get; set; }
        public IBrowserContext? Context { get; set; }
        public sealed record PageBinding(IPage Page, ICDPSession Session);
        private PageBinding? _activePage;
        public PageBinding? ActivePage => Volatile.Read(ref _activePage);
        public IPage? Page => ActivePage?.Page;
        public ICDPSession? CdpSession => ActivePage?.Session;
        public void SetActivePage(IPage page, ICDPSession session)
        {
            SmAdTouch.Bind(page, session, Config.LinkedCts.Token, human);
            Volatile.Write(ref _activePage, new PageBinding(page, session));
        }
        public readonly System.Collections.Concurrent.ConcurrentDictionary<IPage, Lazy<Task>> PageInitializations = new();
        public Task? PageElementGuardTask;
        public CDPSessionManager? CdpManager { get; set; }

        public LandingPageStrategyDispatcher? LandingDispatcher { get; set; }

        public int DebugPort { get; set; }

        public int TriggerDownloadSign;
        private int _businessDownloadsEnabled;
        public bool BusinessDownloadsEnabled => Volatile.Read(ref _businessDownloadsEnabled) != 0;
        public void EnableBusinessDownloads() => Volatile.Write(ref _businessDownloadsEnabled, 1);
        public void DisableBusinessDownloads() => Volatile.Write(ref _businessDownloadsEnabled, 0);
        public int PageAdsCount { get; set; }
        public bool PageTriggerClick { get; set; }
        public bool JumpClick { get; set; }

        public int PagesCount { get; set; }
        public string CurrentPageUrl { get; set; } = "";
        public int PvIndex { get; set; }

        private bool _proxyFailed;
        public bool ProxyFailed { get => Volatile.Read(ref _proxyFailed); set => Volatile.Write(ref _proxyFailed, value); }
        public string? ProxyFailedReason { get; set; }
        public QTP.Common.ProxyFailureKind? ProxyFailure { get; private set; }
        private readonly object _proxyFailureSync = new();
        public bool TryMarkProxyFailure(QTP.Common.ProxyFailureKind kind, string reason)
        {
            lock (_proxyFailureSync)
            {
                if (ProxyFailed) return false;
                ProxyFailure = kind;
                ProxyFailedReason = reason;
                LastFailureReason = reason;
                BrowserFailureKind = QTP.Common.BrowserStopKind.ProxyFailed;
                ProxyFailed = true;
                return true;
            }
        }
        public readonly System.Collections.Concurrent.ConcurrentDictionary<IPage, int> PageHttpStatuses = new();
        public bool PageCrashed { get; set; }
        public string? LastFailureReason { get; set; }

        public int PageElementGuardStarted;

        public HumanTouchOperator human { get; set; } = new();

        public void ResetPerPvState()
        {
            DisableBusinessDownloads();
            CurrentVisitReady = false;
            _currentVisitCompleted = false;

            Interlocked.Exchange(ref TriggerDownloadSign, 0);
            PageTriggerClick = false;
            JumpClick = false;
            PagesCount = 0;
            CurrentPageUrl = string.Empty;
        }
    }

}
