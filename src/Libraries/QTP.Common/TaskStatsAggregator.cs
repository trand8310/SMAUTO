using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QTP.Common;
using QTP.Common.Infrastructure;
using SixLabors.ImageSharp.Drawing;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;


namespace QTP
{
    #region Models

    public enum ProxyIpState
    {
        Fetched,
        Consumed
    }

    public record TaskEvent(int TaskId, StateType Type, int Count, string? Data = null);

    public record ProxyIpStatEvent(
        int TaskId,
        ProxyIpState State,
        string? Ip = null,
        int Count = 1
    );

    public record AdWord(
        [property: JsonProperty("category")] string Category,
        [property: JsonProperty("word")] string Word
    );
    public class TaskStats
    {
        public long Request;
        public long Start;
        public long DSP;
        public long Clickthrough;
        public long Success;
        public long Failure;
        public long Complete;
        public long HomepageTrigger;

        public long _deltaStart;
        public long _deltaDsp;
        public long _deltaClickthrough;

        public double ClickRatio => DSP == 0 ? 0 : (double)Clickthrough / DSP;
        public double HomepageTriggerRatio => DSP == 0 ? 0 : (double)HomepageTrigger / DSP;
        // 👉 给 flush 用：只拿增量
        public Dictionary<string, long> SnapshotAndResetDelta()
        {
            var start = Interlocked.Exchange(ref _deltaStart, 0);
            var dsp = Interlocked.Exchange(ref _deltaDsp, 0);
            var click = Interlocked.Exchange(ref _deltaClickthrough, 0);
            var dict = new Dictionary<string, long>();
            if (start > 0) dict["start"] = start;
            if (dsp > 0) dict["dsp"] = dsp;
            if (click > 0) dict["click"] = click;
            return dict;
        }
    }

    public record ProxyIpSnapshot(long Fetched, long Consumed, string[] ConsumedIps);
    public class ProxyIpStat
    {
        private long _fetched;
        private long _consumed;
        private readonly ConcurrentQueue<string> _consumedIps = new();
        public void AddFetched(long value = 1)
        {
            Interlocked.Add(ref _fetched, value);
        }
        public void AddConsumed(long value = 1)
        {
            Interlocked.Add(ref _consumed, value);
        }
        public void AddConsumedIp(string ip)
        {
            if (!string.IsNullOrEmpty(ip))
                _consumedIps.Enqueue(ip);
        }
        public ProxyIpSnapshot Snapshot()
        {
            var fetched = Interlocked.Read(ref _fetched);
            var consumed = Interlocked.Read(ref _consumed);
            var ips = _consumedIps.ToArray();
            return new ProxyIpSnapshot(fetched, consumed, ips);
        }
        public void Commit(ProxyIpSnapshot snapshot)
        {
            if (snapshot == null) return;
            Interlocked.Add(ref _fetched, -snapshot.Fetched);
            Interlocked.Add(ref _consumed, -snapshot.Consumed);
            int needRemove = snapshot.ConsumedIps.Length;
            while (needRemove-- > 0 && _consumedIps.TryDequeue(out _))
            {

            }
        }
        public bool IsEmpty()
        {
            return Interlocked.Read(ref _fetched) == 0
                && Interlocked.Read(ref _consumed) == 0
                && _consumedIps.IsEmpty;
        }
    }

    #endregion


    /// <summary>
    /// 本地统计
    /// </summary>
    public class LocalHourStats
    {
        public string HourKey { get; }

        // taskId -> (name -> count)
        public ConcurrentDictionary<int, ConcurrentDictionary<string, long>> Tasks { get; }

        public LocalHourStats(string hourKey)
        {
            HourKey = hourKey;
            Tasks = new ConcurrentDictionary<int, ConcurrentDictionary<string, long>>(
                Environment.ProcessorCount, 32);
        }
    }




    public class TaskStatsAggregator : IDisposable
    {
        #region Fields

        private readonly Channel<TaskEvent> _queue = Channel.CreateUnbounded<TaskEvent>();
        private readonly Channel<ProxyIpStatEvent> _proxyIpQueue = Channel.CreateUnbounded<ProxyIpStatEvent>();

        private readonly ConcurrentDictionary<int, TaskStats> _tasks = new();
        private readonly ConcurrentDictionary<int, bool> _dirtyTasks = new();

        private readonly ConcurrentDictionary<int, ProxyIpStat> _taskProxyIpStats = new();
        private readonly ConcurrentDictionary<int, bool> _dirtyProxyIpTasks = new();

        private readonly Channel<AdWord> _adWordQueue = Channel.CreateUnbounded<AdWord>();
        private readonly ConcurrentQueue<AdWord> _adWordBuffer = new();
        private bool _dirtyAdWords = false;

        private readonly TaskStats _totalStats = new(); // 全局总统计
        private bool _dirtyTotalStats = false; // 总统计脏标记

        // 分布式点击控制
        private readonly ConcurrentDictionary<int, TaskStats> _taskGlobalBaseline = new(); // 初始化全局量
        private readonly ConcurrentDictionary<int, double> _taskClickRates = new(); // 每任务点击率
        private readonly ConcurrentDictionary<int, double> _taskTriggerHomeRates = new(); // 触发首页输入广告词




        private readonly AdeHelper _adeHelper;
        private readonly AppSettings _appSettings;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new();

        private static int _maxConcurrentRequests = 5;
        private readonly int _retryCount = 3;
        private bool _disposed;

        #endregion

        public TaskStatsAggregator(AdeHelper adeHelper, AppSettings appSettings, ILogger<TaskStatsAggregator> logger)
        {
            _adeHelper = adeHelper;
            _appSettings = appSettings;
            _logger = logger;

            _ = Task.Run(() => ProcessQueueAsync(_cts.Token));
            _ = Task.Run(() => ProcessProxyIpQueueAsync(_cts.Token));
            _ = Task.Run(() => ProcessAdWordQueueAsync(_cts.Token));
            _ = Task.Run(() => FlushLoopAsync(_cts.Token));
        }

        #region Public API

        public void Enqueue(TaskEvent ev) => _queue.Writer.TryWrite(ev);

        public void EnqueueProxyIpFetched(int taskId, int count = 1)
        {
            _proxyIpQueue.Writer.TryWrite(new ProxyIpStatEvent(taskId, ProxyIpState.Fetched, null, count));
        }

        public void EnqueueProxyIpConsumed(int taskId, string ip, int count = 1)
        {
            _proxyIpQueue.Writer.TryWrite(new ProxyIpStatEvent(taskId, ProxyIpState.Consumed, ip, count));
        }
        public void EnqueueAdWord(string category, string word)
        {
            _adWordQueue.Writer.TryWrite(new AdWord(category, word));
        }
        public TaskStats? GetTaskStats(int taskId) => _tasks.TryGetValue(taskId, out var stats) ? stats : null;

        //public double GetClickRatio(int taskId) => _tasks.TryGetValue(taskId, out var stats) ? stats.ClickRatio : 0;
        public async Task<double> GetClickRatioAsync(int taskId, double taskCtr = 100)
        {
            // 初始化全局基线
            if (!_taskGlobalBaseline.ContainsKey(taskId))
            {
                var resp = await _adeHelper.GetTaskStatusAsync(taskId);
                var globalStats = new TaskStats();
                if (resp != null)
                {
                    globalStats.Start = resp.SelectToken("data.start")?.Value<int>() ?? 0;
                    globalStats.DSP = resp.SelectToken("data.dsp")?.Value<int>() ?? 0;
                    globalStats.Clickthrough = resp.SelectToken("data.click")?.Value<int>() ?? 0;
                }
                _taskGlobalBaseline[taskId] = globalStats;

                if (!_taskClickRates.ContainsKey(taskId))
                    _taskClickRates[taskId] = taskCtr; // 默认点击率，可改成从后台取
            }

            var baseline = _taskGlobalBaseline[taskId];
            var stats = _tasks.GetOrAdd(taskId, _ => new TaskStats());
            double rate = _taskClickRates[taskId];

            int totalDSP = (int)(baseline.DSP + stats.DSP);
            if (totalDSP == 0)
                return 0;
            int totalClick = (int)(baseline.Clickthrough + stats.Clickthrough);
            return totalClick / (double)totalDSP;

        }


        public bool CanHomepageTrigger(int taskId)
        {
            if (_appSettings.HompageTrigger == 0)
                return false;

            var stats = _tasks.GetOrAdd(taskId, _ => new TaskStats());
            return stats.HomepageTriggerRatio < _appSettings.HompageTrigger;

        }


        /// <summary>
        /// 判断任务是否允许点击
        /// </summary>
        /// <param name="taskId"></param>
        /// <param name="taskCtr"></param>
        /// <returns></returns>
        public async Task<bool> CanClickthroughAsync(int taskId, double taskCtr = 100)
        {
            // 初始化全局基线
            if (!_taskGlobalBaseline.ContainsKey(taskId))
            {
                var resp = await _adeHelper.GetTaskStatusAsync(taskId);
                var globalStats = new TaskStats();
                if (resp != null)
                {
                    globalStats.Start = resp.SelectToken("data.start")?.Value<int>() ?? 0;
                    globalStats.DSP = resp.SelectToken("data.dsp")?.Value<int>() ?? 0;
                    globalStats.Clickthrough = resp.SelectToken("data.click")?.Value<int>() ?? 0;
                }
                _taskGlobalBaseline[taskId] = globalStats;

                if (!_taskClickRates.ContainsKey(taskId))
                    _taskClickRates[taskId] = taskCtr; // 默认点击率，可改成从后台取
            }

            var baseline = _taskGlobalBaseline[taskId];
            var stats = _tasks.GetOrAdd(taskId, _ => new TaskStats());
            double rate = _taskClickRates[taskId];
            if (rate == 0)
                return false;

            int totalDSP = (int)(baseline.DSP + stats.DSP);
            if (totalDSP == 0)
                return true;
            int totalClick = (int)(baseline.Clickthrough + stats.Clickthrough);
            if (totalClick == 0)
                return true;
            int targetClick = (int)(totalDSP * rate * 0.01);

            return totalClick < targetClick;
        }
        public TaskStats GetTotalStats() => _totalStats;


        #endregion

        #region Queue Processing

        private async Task ProcessQueueAsync(CancellationToken token)
        {
            var buffer = new List<TaskEvent>();

            try
            {
                while (!token.IsCancellationRequested)
                {
                    var ev = await _queue.Reader.ReadAsync(token);
                    buffer.Add(ev);
                    while (_queue.Reader.TryRead(out var e)) buffer.Add(e);

                    foreach (var e in buffer)
                    {
                        var stats = _tasks.GetOrAdd(e.TaskId, _ => new TaskStats());

                        switch (e.Type)
                        {
                            case StateType.Request:
                                Interlocked.Add(ref stats.Request, e.Count);
                                Interlocked.Add(ref _totalStats.Request, e.Count);
                                _dirtyTotalStats = true;
                                break;
                            case StateType.Start:

                                Interlocked.Add(ref stats.Start, e.Count);
                                Interlocked.Add(ref stats._deltaStart, e.Count);
                                Interlocked.Add(ref _totalStats.Start, e.Count);
                                Interlocked.Add(ref _totalStats._deltaStart, e.Count);
                                _dirtyTotalStats = true;
                                break;
                            case StateType.DSP:

                                Interlocked.Add(ref stats.DSP, e.Count);
                                Interlocked.Add(ref stats._deltaDsp, e.Count);
                                Interlocked.Add(ref _totalStats.DSP, e.Count);
                                Interlocked.Add(ref _totalStats._deltaDsp, e.Count);
                                _dirtyTotalStats = true;
                                break;
                            case StateType.Clickthrough:
                                Interlocked.Add(ref stats.Clickthrough, e.Count);
                                Interlocked.Add(ref stats._deltaClickthrough, e.Count);
                                Interlocked.Add(ref _totalStats.Clickthrough, e.Count);
                                Interlocked.Add(ref _totalStats._deltaClickthrough, e.Count);
                                _dirtyTotalStats = true;
                                break;
                            case StateType.Success:
                                Interlocked.Add(ref stats.Success, e.Count);
                                Interlocked.Add(ref _totalStats.Success, e.Count);
                                _dirtyTotalStats = true;
                                break;
                            case StateType.Failure:
                                Interlocked.Add(ref stats.Failure, e.Count);
                                Interlocked.Add(ref _totalStats.Failure, e.Count);
                                _dirtyTotalStats = true;
                                break;
                            case StateType.Complete:
                                Interlocked.Add(ref stats.Complete, e.Count);
                                Interlocked.Add(ref _totalStats.Complete, e.Count);
                                _dirtyTotalStats = true;
                                break;
                            case StateType.X5Sec:
                                // _logger.LogWarning($"x5sec ip={e.Data}");
                                _logger.LogX5Sec($"x5sec ip={e.Data}");
                                break;
                            case StateType.HomepageTrigger:
                                Interlocked.Add(ref stats.HomepageTrigger, e.Count);
                                Interlocked.Add(ref _totalStats.HomepageTrigger, e.Count);
                                break;
                        }

                        _dirtyTasks[e.TaskId] = true;
                    }

                    buffer.Clear();
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ProcessProxyIpQueueAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var ev = await _proxyIpQueue.Reader.ReadAsync(token);
                    var stat = _taskProxyIpStats.GetOrAdd(ev.TaskId, _ => new ProxyIpStat());
                    if (ev.State == ProxyIpState.Fetched)
                        stat.AddFetched(ev.Count);
                    else
                    {
                        stat.AddConsumed(ev.Count);
                        if (!string.IsNullOrEmpty(ev.Ip))
                            stat.AddConsumedIp(ev.Ip);
                    }
                    _dirtyProxyIpTasks[ev.TaskId] = true;
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ProcessAdWordQueueAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var adWord = await _adWordQueue.Reader.ReadAsync(token);
                    _adWordBuffer.Enqueue(adWord);
                    _dirtyAdWords = true;
                }
            }
            catch (OperationCanceledException) { }
        }
        #endregion

        #region Flush (每秒统一上传)

        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(_maxConcurrentRequests);

        private async Task FlushLoopAsync(CancellationToken token)
        {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    var flushTasks = new List<Task>();

                    // ===== Task Stats Flush =====
                    var dirtySnapshot = _dirtyTasks.Keys.ToArray();
                    foreach (var taskId in dirtySnapshot)
                    {
                        if (_tasks.TryGetValue(taskId, out var stats))
                        {
                            var metrics = stats.SnapshotAndResetDelta();
                            if (metrics.Count == 0)
                            {
                                _dirtyTasks.TryRemove(taskId, out _);
                                continue;
                            }

                            flushTasks.Add(Task.Run(async () =>
                            {
                                await _semaphore.WaitAsync(token);
                                try
                                {
                                    await RetryAsync(() => _adeHelper.UpdateTaskStatusAsync(taskId, metrics, token), _retryCount, token);
                                    _dirtyTasks.TryRemove(taskId, out _); // ✅ 成功后清理
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            }, token));
                        }
                    }

                    // ===== Proxy IP Flush =====
                    var dirtyProxyIpSnapshot = _dirtyProxyIpTasks.Keys.ToArray();
                    foreach (var taskId in dirtyProxyIpSnapshot)
                    {
                        if (_taskProxyIpStats.TryGetValue(taskId, out var stat))
                        {
                            var snapshot = stat.Snapshot();
                            if (snapshot.Fetched == 0 && snapshot.Consumed == 0 && snapshot.ConsumedIps.Length == 0)
                            {
                                _dirtyProxyIpTasks.TryRemove(taskId, out _);
                                return;
                            }

                            var metrics = new Dictionary<string, long>();
                            if (snapshot.Fetched > 0) metrics["fetched"] = snapshot.Fetched;
                            if (snapshot.Consumed > 0) metrics["consumed"] = snapshot.Consumed;
                            var consumedIps = snapshot.ConsumedIps.ToList();

                            flushTasks.Add(Task.Run(async () =>
                            {
                                await _semaphore.WaitAsync(token);
                                try
                                {
                                    await RetryAsync(() =>
                                        _adeHelper.UpdateProxyIpStatAsync(
                                            taskId,
                                            metrics,
                                            consumedIps,
                                            token),
                                        _retryCount,
                                        token);
                                    stat.Commit(snapshot);
                                    _dirtyProxyIpTasks.TryRemove(taskId, out _);
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            }, token));
                        }
                    }

                    // ===== Total Stats Flush =====
                    if (_dirtyTotalStats)
                    {
                        var metrics = _totalStats.SnapshotAndResetDelta();
                        if (metrics.Count > 0)
                        {
                            flushTasks.Add(Task.Run(async () =>
                            {
                                await _semaphore.WaitAsync(token);
                                try
                                {
                                    await RetryAsync(() => _adeHelper.UpdateHostStatusAsync(metrics, token), _retryCount, token);
                                    _dirtyTotalStats = false; // ✅ 成功后才置 false
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            }, token));
                        }
                        else
                        {
                            _dirtyTotalStats = false;
                        }
                    }

                    // ===== AdWord Flush =====
                    if (_dirtyAdWords)
                    {
                        var toUpload = new List<AdWord>();
                        while (_adWordBuffer.TryDequeue(out var adWord))
                            toUpload.Add(adWord);

                        if (toUpload.Count > 0)
                        {
                            flushTasks.Add(Task.Run(async () =>
                            {
                                await _semaphore.WaitAsync(token);
                                try
                                {
                                    await RetryAsync(() => _adeHelper.UpdateAdWordsAsync(toUpload, token), _retryCount, token);
                                    _dirtyAdWords = false; // ✅ 成功后才置 false
                                }
                                finally
                                {
                                    _semaphore.Release();
                                }
                            }, token));
                        }
                        else
                        {
                            _dirtyAdWords = false;
                        }
                    }

                    // ===== 等待所有 flush 完成 =====
                    if (flushTasks.Count > 0)
                        await Task.WhenAll(flushTasks);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                timer.Dispose();
            }
        }

        #endregion

        private async Task RetryAsync(Func<Task> func, int retryCount, CancellationToken token)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    await func();
                    return;
                }
                catch when (attempt++ < retryCount)
                {
                    await Task.Delay(500, token);
                }
            }
        }



        #region 时间缓存（UTC + 北京时间）

        private static string _cachedHourKey;
        private static long _cachedHourTicks;

        private static readonly long HourTicks = TimeSpan.TicksPerHour;
        private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);

        private static string GetHourKey()
        {
            var utcNow = DateTime.UtcNow;
            var currentHourTicks = utcNow.Ticks / HourTicks * HourTicks;

            if (_cachedHourKey != null && _cachedHourTicks == currentHourTicks)
                return _cachedHourKey;

            var beijingTime = new DateTime(currentHourTicks, DateTimeKind.Utc).Add(BeijingOffset);
            var newKey = beijingTime.ToString("yyyyMMddHH");

            _cachedHourKey = newKey;
            _cachedHourTicks = currentHourTicks;

            return newKey;
        }

        #endregion

        #region 本地统计

        private LocalHourStats _localStats = new LocalHourStats(GetHourKey());

        private static readonly IReadOnlyDictionary<string, long> EmptyDict =
            new Dictionary<string, long>();

        /// <summary>
        /// 增加统计
        /// </summary>
        public void AddLocalMetric(int taskId, string name, long value = 1)
        {
            if (taskId == 0 || string.IsNullOrEmpty(name))
                return;

            var hour = GetHourKey();

            while (true)
            {
                var current = _localStats;

                if (current.HourKey == hour)
                {
                    // 获取 task 字典
                    var taskDict = current.Tasks.GetOrAdd(taskId,
                        _ => new ConcurrentDictionary<string, long>(8, 16));

                    // 更新指标
                    taskDict.AddOrUpdate(name, value, (_, old) => old + value);

                    return;
                }

                // 切换小时（CAS）
                var newStats = new LocalHourStats(hour);

                if (Interlocked.CompareExchange(ref _localStats, newStats, current) == current)
                {
                    var taskDict = newStats.Tasks.GetOrAdd(taskId,
                        _ => new ConcurrentDictionary<string, long>(8, 16));

                    taskDict.TryAdd(name, value);
                    return;
                }
            }
        }

        /// <summary>
        /// 获取某个任务全部统计
        /// </summary>
        public IReadOnlyDictionary<string, long> GetAllLocalMetric(int taskId)
        {
            if (taskId == 0)
                return EmptyDict;

            var hour = GetHourKey();
            var stats = _localStats;

            if (stats.HourKey != hour)
                return EmptyDict;

            return stats.Tasks.TryGetValue(taskId, out var dict)
                ? dict
                : EmptyDict;
        }

        /// <summary>
        /// 获取某个任务某个指标
        /// </summary>
        public long GetLocalMetric(int taskId, string name)
        {
            if (taskId == 0 || string.IsNullOrEmpty(name))
                return 0;

            var hour = GetHourKey();
            var stats = _localStats;

            if (stats.HourKey != hour)
                return 0;

            if (!stats.Tasks.TryGetValue(taskId, out var dict))
                return 0;

            return dict.TryGetValue(name, out var value) ? value : 0;
        }

        /// <summary>
        /// 获取某个任务多个指标
        /// </summary>
        public Dictionary<string, long> GetLocalMetrics(int taskId, params string[] names)
        {
            var result = new Dictionary<string, long>();

            if (taskId == 0 || names == null || names.Length == 0)
                return result;


            foreach (var name in names)
            {
                result[name] = 0;
            }


            var hour = GetHourKey();
            var stats = _localStats;

            if (stats.HourKey != hour)
                return result;

            if (!stats.Tasks.TryGetValue(taskId, out var dict))
                return result;

            foreach (var name in names)
            {
                result[name] = dict.TryGetValue(name, out var value) ? value : 0;
            }

            return result;
        }

        /// <summary>
        /// 获取某个任务占比
        /// </summary>
        public double GetStatRatio(int taskId, params string[] names)
        {
            if (taskId == 0 || names == null || names.Length == 0)
                return 0;

            var hour = GetHourKey();
            var stats = _localStats;

            if (stats.HourKey != hour)
                return 0;

            if (!stats.Tasks.TryGetValue(taskId, out var dict))
                return 0;

            var set = new HashSet<string>(names);

            long total = 0;
            long part = 0;

            foreach (var kv in dict)
            {
                total += kv.Value;

                if (set.Contains(kv.Key))
                    part += kv.Value;
            }

            if (total == 0)
                return 0;

            return (double)part / total;
        }

        #endregion



        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _queue.Writer.Complete();
            _proxyIpQueue.Writer.Complete();
        }
    }

}
