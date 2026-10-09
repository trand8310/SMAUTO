using Newtonsoft.Json.Linq;
using QTP.Common.Models;

namespace QTP.Common;

/// <summary>可选的新接口，不改变既有 IQTPService 插件契约。</summary>
public interface IWorkerResultService
{
    Task<WorkerExecutionResult> ExecuteWorkerWithResultAsync(
        string uniqueId, JObject taskArgs, CancellationToken token);
}

public static class WorkerResultServiceExtensions
{
    public static async Task<WorkerExecutionResult> ExecuteWorkerWithResultAsync(
        this IQTPService service, string uniqueId, JObject taskArgs, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            if (service is IWorkerResultService resultService)
                return await resultService.ExecuteWorkerWithResultAsync(uniqueId, taskArgs, token);

            var (success, clicked, ads) = await service.ExecuteWorkerAsync(uniqueId, taskArgs, token);
            return new WorkerExecutionResult(
                success ? WorkerExecutionStatus.Succeeded : WorkerExecutionStatus.Failed,
                clicked, ads, success ? null : "旧版插件返回执行失败，未提供原因");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new WorkerExecutionResult(WorkerExecutionStatus.Canceled, FailureReason: "任务已取消");
        }
    }
}
