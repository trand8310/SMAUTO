
using System.Threading.Channels;

namespace MainClient.UiTask
{
    // =========================
    // PipelineRunner
    // =========================
    public class PipelineRunner<T>
    {
        private readonly Channel<T> _channel;
        private readonly Func<ChannelWriter<T>, CancellationToken, Task> _producer;
        private readonly Func<int, T, CancellationToken, Task> _consumer;
        private readonly int _consumerCount;
        private CancellationTokenSource? _cts;

        public event Action<long>? ProgressChanged;
        public event Action? Started;
        public event Action? Completed;
        public event Action? Canceled;
        public event Action<Exception>? Faulted;

        public PipelineRunner(int capacity, int consumerCount,
            Func<ChannelWriter<T>, CancellationToken, Task> producer,
            Func<int, T, CancellationToken, Task> consumer)
        {
            _channel = Channel.CreateBounded<T>(capacity);
            _consumerCount = consumerCount;
            _producer = producer;
            _consumer = consumer;
        }

        public async Task RunAsync(CancellationToken token)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            _cts = linkedCts;
            Started?.Invoke();

            long globalItemNumber = 0; // 全局消息编号

            try
            {
                // 启动 Producer
                var producerTask = Task.Run(() => _producer(_channel.Writer, linkedCts.Token));

                // 启动消费者
                var consumerTasks = new List<Task>();
                for (int i = 0; i < _consumerCount; i++)
                {
                    int consumerId = i; // 捕获循环变量
                    consumerTasks.Add(Task.Run(async () =>
                    {
                        await foreach (var item in _channel.Reader.ReadAllAsync(linkedCts.Token))
                        {
                            long itemNumber = Interlocked.Increment(ref globalItemNumber);

                            // 调用消费者，传递消费者编号和消息序号
                            await _consumer(consumerId, item, linkedCts.Token);

                            // 触发进度事件
                            ProgressChanged?.Invoke(itemNumber);
                        }
                    }));
                }

                // 等待全部完成
                await Task.WhenAll(consumerTasks.Append(producerTask));

                Completed?.Invoke();
            }
            catch (OperationCanceledException)
            {
                Canceled?.Invoke();
            }
            catch (Exception ex)
            {
                Faulted?.Invoke(ex);
            }
            finally
            {
                _cts = null;
            }
        }
    }
}

 