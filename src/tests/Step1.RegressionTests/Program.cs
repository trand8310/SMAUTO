using System.Reflection;
using MainClient.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using HumanTouchP3.Input;
using HumanTouchP3;
using SMAd;
using QTP;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using QTP.Plugins;
using SMAd.Models;
using SMAd.PlaywrightHumanInput;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine($"PASS {name}");
}

var dev = JObject.Parse("{\"make\":\"Samsung\",\"model\":\"A\",\"androidid\":\"device-a\"}");
var other = (JObject)dev.DeepClone();
other["androidid"] = "device-b";
Check(StableSeed.Create(dev) == StableSeed.Create((JObject)dev.DeepClone()), "相同设备种子稳定");
Check(StableSeed.Create(dev) != StableSeed.Create(other), "不同设备身份区分种子");
var normalized = (JObject)dev.DeepClone();
normalized["make"] = " SAMSUNG ";
Check(StableSeed.Create(dev) == StableSeed.Create(normalized), "设备字段规范化");

var phone = JObject.Parse("{ua:'Mozilla Android Mobile',sw:1080,sh:2400,ratio:3,cpu:null,ram:null,storage:null}");
Check(DeviceValidation.TryPrepare(phone, 1, out _, out var repaired) && repaired &&
      (int)phone["cpu"]! == 8 && (string)phone["ram"]! == "8" && (int)phone["storage"]! == 128,
      "手机设备缺失字段采用 CPU 8、内存 8 和存储 128");
phone["cpu"] = 0; phone["storage"] = 0;
Check(DeviceValidation.TryPrepare(phone, 1, out _, out _) && (int)phone["cpu"]! == 8 && (int)phone["storage"]! == 128,
      "零值设备字段使用默认值");
var tablet = (JObject)phone.DeepClone(); tablet["cssWidth"] = 858; tablet["cssHeight"] = 938;
Check(!DeviceValidation.TryPrepare(tablet, 1, out _, out _), "平板尺寸设备被拒绝");
var ipad = (JObject)phone.DeepClone(); ipad["ua"] = "Mozilla iPad";
Check(!DeviceValidation.TryPrepare(ipad, 2, out _, out _) && !DeviceValidation.TryPrepare(phone, 7, out _, out _), "平板标识及 PC 被拒绝");
var invalidScreen = (JObject)phone.DeepClone(); invalidScreen["sw"] = 0;
Check(!DeviceValidation.TryPrepare(invalidScreen, 1, out _, out _), "无效屏幕数据被拒绝而不使用伪造屏幕");
var invalidDpr = (JObject)phone.DeepClone(); invalidDpr["dpr"] = 0;
Check(!DeviceValidation.TryPrepare(invalidDpr, 1, out _, out _), "无效 DPR 被拒绝");
var loggedDevice = (JObject)phone.DeepClone(); loggedDevice["password"] = "device-test-secret";
DeviceValidation.LogDevice(-998, 1, "device-test", loggedDevice);
var logEntry = File.ReadLines(BrowserLifecycleLog.CurrentFilePath).Last(x => x.Contains("device-test"));
Check(logEntry.Contains("\"device\"") && logEntry.Contains("\"sw\"") && !logEntry.Contains("device-test-secret"),
    "设备诊断记录字段并隐藏凭据");

var proportions = Enumerable.Range(0, 100).Select(TouchPostureSelector.FromRoll).ToArray();
Check(proportions.Count(p => p == HandPosture.RightHandOneHanded) == 69 &&
      proportions.Count(p => p == HandPosture.LeftHandOneHanded) == 13 &&
      proportions.Count(p => p == HandPosture.LeftHandHoldRightIndex) == 18 &&
      TouchPostureSelector.Resolve("LeftHandOneHanded", new Random(1)) == HandPosture.LeftHandOneHanded &&
      TouchPostureSelector.Resolve("LeftHandHoldRightIndex", new Random(1)) == HandPosture.LeftHandHoldRightIndex,
      "姿势随机权重为 69/13/18 且显式姿势优先");
var originalProfile = HumanUserProfile.CreateRandom(123, HumanHandedness.Left);
var calibratedProfile = PostureProfiles.Apply(HandPosture.LeftHandOneHanded, originalProfile);
Check(calibratedProfile.Seed == originalProfile.Seed && calibratedProfile.SpeedBias == originalProfile.SpeedBias &&
      calibratedProfile.Handedness == HumanHandedness.Left && calibratedProfile.VerticalUpStartXRatio == .18,
      "姿势校准保留设备的长期随机画像");

var clock = new TestClock();
var session = new HumanTouchSession(HumanUserProfile.CreateRandom(123), null, 456, clock);
session.RecordGesture(new HumanSwipeTrace { Intent = SwipeIntent.Reading, Mode = HumanSwipeMode.Reading });
var lastAction = session.LastActionUtc;
clock.Advance(TimeSpan.FromSeconds(3));
session.RecoverToNow();
double fatigue = session.ShortFatigue;
double attention = session.Attention;
session.RecoverToNow();
session.RecoverToNow();
Check(session.ShortFatigue == fatigue && session.Attention == attention, "同一时间重复恢复不重复结算");
Check(session.LastActionUtc == lastAction, "恢复不修改最近动作时间");
clock.JumpUtc(TimeSpan.FromDays(-1));
session.RecoverToNow();
Check(session.ShortFatigue == fatigue, "系统时钟回拨不改变恢复量");
clock.Advance(TimeSpan.FromSeconds(2));
session.RecoverToNow();
Check(Math.Abs(session.ShortFatigue - fatigue * Math.Exp(-2 / (18 / Math.Max(.45, session.UserProfile.RecoveryBias)))) < 1e-12,
    "后续恢复只结算新增时长");

IQTPService legacy = new LegacyPlugin();
var result = await legacy.ExecuteWorkerWithResultAsync("test", new JObject(), CancellationToken.None);
Check(result.IsSuccess && result.PageTriggerClick && result.PageAdsCount == 7, "旧插件结果适配且字段保留");
Check(result.ToLegacyTuple() == (true, true, 7), "结果可转换为旧元组");
((LegacyPlugin)legacy).Success = false;
result = await legacy.ExecuteWorkerWithResultAsync("test", new JObject(), CancellationToken.None);
Check(result.Status == WorkerExecutionStatus.Failed && !string.IsNullOrEmpty(result.FailureReason), "旧插件失败具有明确状态和回退原因");
using var cts = new CancellationTokenSource();
cts.Cancel();
int calls = ((LegacyPlugin)legacy).Calls;
result = await legacy.ExecuteWorkerWithResultAsync("test", new JObject(), cts.Token);
Check(result.Status == WorkerExecutionStatus.Canceled && ((LegacyPlugin)legacy).Calls == calls, "已取消任务不启动插件");
result = await ((IWorkerResultService)legacy).ExecuteWorkerWithResultAsync("test", new JObject(), cts.Token);
Check(result.Status == WorkerExecutionStatus.Canceled, "基类新入口直接调用也支持取消");

// 单独验证业务失败与外部取消的映射，不启动浏览器。
var build = typeof(SMAdTask).GetMethod("BuildWorkerResult", BindingFlags.Static | BindingFlags.NonPublic)!;
WorkerExecutionResult Build(WorkerRunContext? ctx, bool success = false, bool canceled = false, string? reason = null)
    => (WorkerExecutionResult)build.Invoke(null, new object?[] { ctx, success, canceled, reason })!;
var context = new WorkerRunContext(new TaskConfig())
{
    ProxyFailed = true, ProxyFailedReason = "proxy test", PageTriggerClick = true, PageAdsCount = 3
};
result = Build(context);
Check(result.Status == WorkerExecutionStatus.Failed && result.FailureReason == "proxy test" && result.PageAdsCount == 3,
    "代理异常保留原因和已发生的业务数据");
Check(Build(context, canceled: true).Status == WorkerExecutionStatus.Canceled, "外部取消独立于内部故障");
context.ProxyFailed = false;
context.PageCrashed = true;
context.LastFailureReason = "crash test";
Check(Build(context).FailureReason == "crash test", "页面故障保留原因");
Check(Build(context, success: true).FailureReason == null, "成功不携带残留失败原因");
Check(Build(null, reason: "startup test").FailureReason == "startup test", "初始化失败提供回退原因");
// 错误参数在浏览器启动前失败，用于验证新执行服务的生命周期。
await using (var processes = new ChromiumSessionManager())
{
    var settings = new AppSettings();
    var stats = new TaskStatsAggregator(null!, settings, NullLogger<TaskStatsAggregator>.Instance);
    var created = new List<SMAdTask>();
    SMAdTask CreateTask()
    {
        // 该场景不访问网络、设备或 Playwright；只使用结果和事件接口。
        var task = new SMAdTask(null!, stats, processes, null!, null!, settings);
        created.Add(task);
        return task;
    }
    var executor = new SmAdExecutor(CreateTask, stats, processes, NullLogger<SmAdExecutor>.Instance);
    int logCount = 0;
    result = await executor.ExecuteAsync("canceled", new JObject(), cts.Token, _ => logCount++);
    Check(result.Status == WorkerExecutionStatus.Canceled && created.Count == 0, "执行服务取消时不创建 SMAd");
    result = await executor.ExecuteAsync("invalid-1", new JObject(), CancellationToken.None, _ => logCount++);
    Check(result.Status == WorkerExecutionStatus.Failed && !string.IsNullOrEmpty(result.FailureReason), "执行服务转发 SMAd 参数失败结果");
    Check(logCount > 0, "执行服务转发 SMAd 日志");
    int completedLogs = logCount;
    created[0].LogWriteLine("after execution");
    Check(logCount == completedLogs, "失败执行结束后解除日志订阅");
    await executor.ExecuteAsync("invalid-2", new JObject(), CancellationToken.None);
    Check(created.Count == 2 && !ReferenceEquals(created[0], created[1]), "每次执行创建独立 SMAd 对象");
    var brokenFactory = new SmAdExecutor(() => throw new InvalidOperationException("factory-test"),
        stats, processes, NullLogger<SmAdExecutor>.Instance);
    result = await brokenFactory.ExecuteAsync("factory-failure", new JObject(), CancellationToken.None);
    Check(result.Status == WorkerExecutionStatus.Failed && result.FailureReason == "factory-test", "对象创建失败返回结构化结果");
    Check(processes.Count == 0, "无浏览器场景清理不会创建受管进程");
}
Console.WriteLine($"All {checks} checks passed.");

sealed class TestClock : TimeProvider
{
    private long _ticks;
    private DateTimeOffset _utc = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => _utc;
    public void Advance(TimeSpan duration) { _ticks += duration.Ticks; _utc += duration; }
    public void JumpUtc(TimeSpan duration) => _utc += duration;
}

sealed class LegacyPlugin : QTPServiceBase
{
    public LegacyPlugin() : base(new AppSettings()) { }
    public override string Title => "legacy-test";
    public bool Success { get; set; } = true;
    public int Calls { get; private set; }
    public override Task<(bool, bool, int)> ExecuteWorkerAsync(string id, JObject args, CancellationToken token)
    {
        Calls++;
        return Task.FromResult((Success, true, 7));
    }
}
