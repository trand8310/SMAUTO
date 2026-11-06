namespace SMAd.PageActions;

internal static class PlaywrightTaskWait
{
    public static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken token)
    {
        try { return await task.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { _ = ObserveAsync(task); throw; }
    }
    public static async Task WithCancellation(Task task, CancellationToken token)
    {
        try { await task.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { _ = ObserveAsync(task); throw; }
    }
    private static async Task ObserveAsync(Task task) { try { await task.ConfigureAwait(false); } catch { } }
}
