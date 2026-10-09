using PlaywrightHumanInput;

internal static class V3Checks
{
    public static void Run(Action<bool, string> check)
    {
        check(typeof(HumanTouchEngine).Assembly.GetName().Name == "HumanTouchP2.Enhancedv3", "业务与测试实际使用 v3 程序集");
        var clock = new RhythmClock();
        var user = HumanUserProfile.CreateRandom(123);
        var a = new HumanTouchSession(user, null, 456, clock);
        var b = new HumanTouchSession(user, null, 456, clock);
        var planner = new HumanTapPlanner();
        bool reproducible = true, valid = true;
        for (int i = 0; i < 100; i++)
        {
            var pa = planner.Plan(a, 40, 50);
            var pb = planner.Plan(b, 40, 50);
            reproducible &= pa.HoldMs == pb.HoldMs && pa.Samples.Zip(pb.Samples).All(pair =>
                pair.First.TimeMs == pair.Second.TimeMs && pair.First.Force == pair.Second.Force && pair.First.RadiusX == pair.Second.RadiusX);
            valid &= pa.HoldMs is >= 45 and <= 160 && pa.Samples.All(s =>
                s.Point.X == 40 && s.Point.Y == 50 && s.Force > 0 && s.Force <= 1 && s.RadiusX > 0 && s.RadiusY > 0)
                && pa.Samples[0].TimeMs < pa.Samples[1].TimeMs && pa.Samples[1].TimeMs < pa.Samples[2].TimeMs
                && pa.Samples[1].Force > pa.Samples[0].Force && pa.Samples[1].Force > pa.Samples[2].Force
                && Math.Abs(pa.Samples[^1].TimeMs + pa.Gesture.EndHoldMs - pa.HoldMs) < .001;
        }
        check(reproducible, "固定画像与会话种子可复现 v3 点击接触计划");
        check(valid, "点击时序和压力有界且不产生位置拖动");
        var fast = new HumanTouchSession(new() { SpeedBias = 1.4, ReactionBias = .6 }, null, 789, clock);
        var slow = new HumanTouchSession(new() { SpeedBias = .7, ReactionBias = 1.6 }, null, 789, clock);
        double FastMean() => Enumerable.Range(0, 100).Average(_ => planner.Plan(fast, 40, 50).HoldMs);
        double SlowMean() => Enumerable.Range(0, 100).Average(_ => planner.Plan(slow, 40, 50).HoldMs);
        check(SlowMean() > FastMean() * 1.5, "反应与速度画像改变按压节奏");
        var rhythm = new HumanActionRhythm();
        a.RecordTap();
        check(a.TapCount == 1 && a.SwipeCount == 0 && !a.LastInputWasSwipe, "点击单独计数且不污染滑动计数");
        check(rhythm.NextPause(a) > TimeSpan.Zero, "连续输入保留短观察间隔");
        clock.Advance(TimeSpan.FromSeconds(2));
        check(rhythm.NextPause(a) == TimeSpan.Zero, "已有观察时间抵扣动作间隔");
        clock.JumpUtc(TimeSpan.FromDays(-1));
        check(rhythm.NextPause(a) == TimeSpan.Zero, "系统时间回拨不重加操作停顿");
        rhythm.Enabled = false;
        check(rhythm.NextPause(b) == TimeSpan.Zero, "上层可关闭 v3 自动操作节奏");
    }

    private sealed class RhythmClock : TimeProvider
    {
        private long _ticks;
        private DateTimeOffset _utc = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => _utc;
        public void Advance(TimeSpan value) { _ticks += value.Ticks; _utc += value; }
        public void JumpUtc(TimeSpan value) => _utc += value;
    }
}
