using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using QTP;
using QTP.Common;
using QTP.Common.Models;
using QTP.Common.Plugins;
using QTP.Plugins;

namespace MainClient.Services;

/// <summary>每次执行创建独立 SMAd 对象，集中管理事件转发和最终资源清理。</summary>
public sealed class SmAdExecutor
{
    public const string ServiceName = "SMAd";
    private readonly Func<SMAdTask> _createTask;
    private readonly TaskStatsAggregator _aggregator;
    private readonly ChromiumSessionManager _processManager;
    private readonly BrowserRuntimeManager? _browserRuntime;
    private readonly ILogger<SmAdExecutor> _logger;

    public SmAdExecutor(Func<SMAdTask> createTask, TaskStatsAggregator aggregator,
        ChromiumSessionManager processManager, ILogger<SmAdExecutor> logger, BrowserRuntimeManager? browserRuntime = null)
    {
        _createTask = createTask;
        _aggregator = aggregator;
        _processManager = processManager;
        _browserRuntime = browserRuntime;
        _logger = logger;
    }

    public async Task<WorkerExecutionResult> ExecuteAsync(
        string executionId, JObject args, CancellationToken token,
        Action<PluginLogEventArgs>? onLog = null)
    {
        if (token.IsCancellationRequested)
            return new WorkerExecutionResult(WorkerExecutionStatus.Canceled, FailureReason: "任务已取消");

        IQTPService? service = null;
        EventHandler<PluginLogEventArgs> logHandler = (_, e) => onLog?.Invoke(e);
        EventHandler<TaskStateChangedEventArgs> stateHandler = (_, e) =>
            _aggregator.Enqueue(new TaskEvent(e.Id, e.Type, e.Count, e.Data));
        EventHandler<TaskAdWordEventArgs> adWordHandler = (_, e) =>
            _aggregator.EnqueueAdWord(e.Type, e.Word);

        WorkerExecutionResult result = new(WorkerExecutionStatus.Failed, FailureReason: "Execution did not start");
        try
        {
            token.ThrowIfCancellationRequested();
            service = _createTask();
            service.OnLogEventHandler += logHandler;
            service.OnStateChangedEventHandler += stateHandler;
            service.OnTaskAdWordEventHandler += adWordHandler;
            result = await service.ExecuteWorkerWithResultAsync(executionId, args, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result = new WorkerExecutionResult(WorkerExecutionStatus.Canceled, FailureReason: "任务已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SMAd execution failed. executionId={ExecutionId}", executionId);
            result = new WorkerExecutionResult(WorkerExecutionStatus.Failed, FailureReason: ex.Message)
                { BrowserStopKind = BrowserFailureClassifier.Classify(ex) };
        }
        finally
        {
            if (service != null)
            {
                service.OnLogEventHandler -= logHandler;
                service.OnStateChangedEventHandler -= stateHandler;
                service.OnTaskAdWordEventHandler -= adWordHandler;
            }

            // SMAd 先清理页面、CDP 和连接；这里兜底关闭该次执行的受管进程。
            try
            {
                if (_browserRuntime != null) await _browserRuntime.CloseSessionAsync(executionId);
                else await _processManager.CloseAsync(executionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Close SMAd session failed. executionId={ExecutionId}", executionId);
                result = result with { Status = WorkerExecutionStatus.Failed, FailureReason = $"Browser cleanup failed: {ex.Message}", BrowserStopKind = BrowserStopKind.CleanupFailed };
            }

            try
            {
                if (service is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (service is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Dispose SMAd failed. executionId={ExecutionId}", executionId); }
        }

        _logger.Log(result.Status == WorkerExecutionStatus.Failed ? LogLevel.Warning : LogLevel.Information,
            "SMAd execution finished. taskId={TaskId}, executionId={ExecutionId}, status={Status}, clicked={Clicked}, ads={Ads}, reason={Reason}, browserReason={BrowserReason}, exitConfirmed={ExitConfirmed}",
            args.SelectToken("task.id")?.ToString(), executionId, result.Status,
            result.PageTriggerClick, result.PageAdsCount, result.FailureReason, result.BrowserStopKind, result.BrowserReclaim?.ExitConfirmed);
        return result;
    }
}
