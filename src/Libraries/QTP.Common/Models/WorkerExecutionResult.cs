namespace QTP.Common.Models
{
    public sealed class WorkerExecutionResult
    {
        private WorkerExecutionResult(
            bool isSuccess,
            bool isPageTriggerClick,
            int pageAdsCount,
            WorkerFailureKind failureKind,
            string? failureReason)
        {
            IsSuccess = isSuccess;
            IsPageTriggerClick = isPageTriggerClick;
            PageAdsCount = pageAdsCount;
            FailureKind = failureKind;
            FailureReason = failureReason;
        }

        public bool IsSuccess { get; }

        public bool IsPageTriggerClick { get; }

        public int PageAdsCount { get; }

        public WorkerFailureKind FailureKind { get; }

        public string? FailureReason { get; }

        public bool HasFailure => !IsSuccess;

        public static WorkerExecutionResult Success(bool isPageTriggerClick, int pageAdsCount) =>
            new(true, isPageTriggerClick, pageAdsCount, WorkerFailureKind.None, null);

        public static WorkerExecutionResult Failure(
            WorkerFailureKind failureKind,
            string? failureReason = null,
            bool isPageTriggerClick = false,
            int pageAdsCount = 0) =>
            new(false, isPageTriggerClick, pageAdsCount, failureKind, failureReason);
    }
}
