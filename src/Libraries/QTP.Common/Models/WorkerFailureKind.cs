namespace QTP.Common.Models
{
    public enum WorkerFailureKind
    {
        None = 0,
        Canceled = 1,
        ProxyFailed = 2,
        PageCrashed = 3,
        BrowserStartFailed = 4,
        BrowserDisconnected = 5,
        NoBrowserContext = 6,
        PlaywrightException = 7,
        UnhandledException = 8,
        MainFlowFailed = 9
    }
}
