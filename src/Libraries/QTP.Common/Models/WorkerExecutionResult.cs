namespace QTP.Common.Models;

public enum WorkerExecutionStatus
{
    Succeeded,
    Failed,
    Canceled
}

/// <summary>单次插件执行结果；取消与业务失败分别表达。</summary>
public sealed record WorkerExecutionResult(
    WorkerExecutionStatus Status,
    bool PageTriggerClick = false,
    int PageAdsCount = 0,
    string? FailureReason = null)
{
    public bool IsSuccess => Status == WorkerExecutionStatus.Succeeded;
    public ProxyFailureKind? ProxyFailure { get; init; }
    public int CompletedPvs { get; init; }
    public bool ClickRequested { get; init; }
    public int CompletedClickPvs { get; init; }
    public string? ActionLogPath { get; init; }
    public BrowserStopKind? BrowserStopKind { get; init; }
    public BrowserReclaimResult? BrowserReclaim { get; init; }

    public (bool, bool, int) ToLegacyTuple() => (IsSuccess, PageTriggerClick, PageAdsCount);
}
