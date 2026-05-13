namespace QTP.Extensions
{
    static public class TaskExtensions
    {
        /// <summary>
        /// 任务超时的有值版本
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="task"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        /// <exception cref="TimeoutException"></exception>
        public static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
        {
            var delayTask = Task.Delay(timeout);

            var completed = await Task.WhenAny(task, delayTask);

            if (completed == delayTask)
            {
                throw new TimeoutException($"Operation timeout: {timeout.TotalSeconds}s");
            }

            return await task;
        }
        /// <summary>
        /// 无返回值版本
        /// </summary>
        /// <param name="task"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        /// <exception cref="TimeoutException"></exception>
        public static async Task WithTimeout(Task task, TimeSpan timeout)
        {
            var delayTask = Task.Delay(timeout);

            var completed = await Task.WhenAny(task, delayTask);

            if (completed == delayTask)
            {
                throw new TimeoutException($"Operation timeout: {timeout.TotalSeconds}s");
            }

            await task;
        }





        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<bool>();
            using (token.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
            {
                if (task != await Task.WhenAny(task, tcs.Task))
                {
                    throw new OperationCanceledException(token);
                }
            }
            return await task; // 如果没被取消，返回结果
        }
        public static async Task<TResult> TimeoutAfter<TResult>(this Task<TResult> task, TimeSpan timeout)
        {
            using (var timeoutCancellationTokenSource = new CancellationTokenSource())
            {
                var completedTask = await Task.WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token));
                if (completedTask == task)
                {
                    timeoutCancellationTokenSource.Cancel();
                    return await task;
                }
                else
                {
                    throw new TimeoutException($"{nameof(TimeoutAfter)}: The operation has timed out after {timeout:mm\\:ss}");
                }
            }
        }
    }
}
