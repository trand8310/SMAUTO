using HumanTouchP3;
using HumanTouchP3.Input;
using Microsoft.Playwright;
using System.Net;
using System.Net.Sockets;

namespace PWTest;
internal static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new TestForm()); }
}
internal sealed class TestForm : Form
{
    readonly TextBox path = new() { Width = 570, PlaceholderText = "选择 Chromium chrome.exe" };
    readonly TextBox profile = new() { Width = 720, Text = Path.Combine(Path.GetTempPath(), "PWTest", Guid.NewGuid().ToString("N")) };
    readonly TextBox arguments = new() { Multiline = true, ScrollBars = ScrollBars.Both, Width = 970, Height = 145, WordWrap = false, Text = LaunchConfiguration.DefaultArguments };
    readonly TextBox log = new() { Multiline = true, ScrollBars = ScrollBars.Both, ReadOnly = true, Dock = DockStyle.Fill };
    readonly ComboBox strength = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly ComboBox posture = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    readonly FixtureServer server = new();
    readonly CancellationTokenSource shutdown = new();
    IPlaywright? playwright; IBrowser? browser; IBrowserContext? context; IPage? page; ICDPSession? cdp; TouchSession? touch;
    bool busy, closing;
    public TestForm()
    {
        Text = "PWTest - 触摸与惯性诊断"; Width = 1050; Height = 760;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 430, AutoScroll = true, Padding = new Padding(8) };
        Controls.Add(log); Controls.Add(top);
        strength.Items.AddRange(new object[] { "随机", "轻", "中", "强" }); strength.SelectedIndex = 0;
        posture.Items.AddRange(new object[] { "右手单手", "左手单手", "左手持机右手食指" }); posture.SelectedIndex = 0;
        posture.SelectedIndexChanged += (_, _) => { if (!busy) { touch = NewSession(); if (page != null && !page.IsClosed) Run(SyncPostureAsync); } };
        top.Controls.Add(new Label { Text = "浏览器路径:", AutoSize = true, Padding = new Padding(0,8,0,0) }); top.Controls.Add(path);
        Button(top,"选择文件", () => { using var dlg = new OpenFileDialog { Filter = "Chromium|*.exe|所有文件|*.*" }; if (dlg.ShowDialog() == DialogResult.OK) path.Text = dlg.FileName; });
        Button(top,"启动浏览器", () => Run(StartBrowserAsync));
        top.SetFlowBreak(top.Controls[^1], true);
        top.Controls.Add(new Label { Text = "用户目录:", AutoSize = true, Padding = new Padding(0,8,0,0) }); top.Controls.Add(profile);
        top.SetFlowBreak(profile, true);
        top.Controls.Add(new Label { Text = "Chromium 启动参数（可编辑；%PROFILE% 表示用户目录）:", AutoSize = true });
        top.SetFlowBreak(top.Controls[^1], true); top.Controls.Add(arguments); top.SetFlowBreak(arguments, true);
        top.Controls.Add(new Label { Text = "持机姿势:", AutoSize = true, Padding = new Padding(0,8,0,0) }); top.Controls.Add(posture);
        top.Controls.Add(new Label { Text = "甩动强度:", AutoSize = true, Padding = new Padding(0,8,0,0) }); top.Controls.Add(strength);
        Button(top,"应用姿势", () => { if (busy) { Write("请等待当前动作结束。"); return; } touch = NewSession(); Write("已更新姿势；用于后续动作。"); });
        Button(top,"开始新采集", () => Run(() => EvalAsync("() => window.diag.start('PWTest 手动采集')")));
        Button(top,"停止并导出", () => Run(SaveAsync));
        top.SetFlowBreak(top.Controls[^1], true);
        Button(top,"向上慢滑", () => Run(() => ActionAsync("向上慢滑", () => touch!.SwipeAsync(page!, cdp!, true, BrowseMode.Reading, shutdown.Token))));
        Button(top,"向下慢滑", () => Run(() => ActionAsync("向下慢滑", () => touch!.SwipeAsync(page!, cdp!, false, BrowseMode.Reading, shutdown.Token))));
        Button(top,"向上快甩", () => Run(() => ActionAsync("向上快甩", () => touch!.SwipeAsync(page!, cdp!, true, BrowseMode.Fast, shutdown.Token, SelectedStrength()))));
        Button(top,"向下快甩", () => Run(() => ActionAsync("向下快甩", () => touch!.SwipeAsync(page!, cdp!, false, BrowseMode.Fast, shutdown.Token, SelectedStrength()))));
        Button(top,"阅读 10 秒", () => Run(() => ActionAsync("阅读10秒", () => touch!.BrowseAsync(page!, cdp!, BrowseMode.Reading, TimeSpan.FromSeconds(10), shutdown.Token))));
        Button(top,"快速浏览 10 秒", () => Run(() => ActionAsync("快速浏览10秒", () => touch!.BrowseAsync(page!, cdp!, BrowseMode.Fast, TimeSpan.FromSeconds(10), shutdown.Token, SelectedStrength()))));
        Button(top,"点击测试按钮", () => Run(() => ActionAsync("点击", () => touch!.TapAsync(page!, cdp!, page!.Locator("#target"), shutdown.Token))));
        top.SetFlowBreak(top.Controls[^1], true);
        Button(top,"停留阅读 2 秒", () => Run(() => ActionAsync("停留阅读", async () => { await touch!.PauseReadingAsync(page!, cdp!, TimeSpan.FromSeconds(2), shutdown.Token); return "停留完成"; })));
        Button(top,"向上微调", () => Run(() => ActionAsync("向上微调", () => touch!.MicroAdjustAsync(page!, cdp!, true, shutdown.Token))));
        Button(top,"向下回看", () => Run(() => ActionAsync("向下回看", () => touch!.BackReviewAsync(page!, cdp!, shutdown.Token))));
        Button(top,"内部容器上滑", () => Run(() => ActionAsync("内部容器上滑", () => touch!.ScrollContainerAsync(page!, cdp!, page!.Locator("#inner"), true, shutdown.Token))));
        Button(top,"甩动并观察惯性", () => Run(() => ActionAsync("甩动并观察惯性", () => FlingObserveAsync(false))));
        Button(top,"甩动并测试制动", () => Run(() => ActionAsync("甩动并测试制动", () => FlingObserveAsync(true))));
        top.SetFlowBreak(top.Controls[^1], true);
        Button(top,"对比两个 JSON", Compare);
        Button(top,"复制本机地址", () => { Clipboard.SetText($"http://127.0.0.1:{server.Port}/"); });
        server.Start(); Write($"本机: http://127.0.0.1:{server.Port}/");
        foreach (var addr in Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))) Write($"手机同局域网访问: http://{addr}:{server.Port}/");
        Write("诊断页为临时只读服务；手机采集后下载 JSON，再复制到电脑进行对比。请只在可信网络使用，关闭程序即停止服务。");
        FormClosing += OnClosing;
    }
    void Button(FlowLayoutPanel parent, string name, Action action) { var b = new Button { Text = name, AutoSize = true, Margin = new Padding(4) }; b.Click += (_,_) => action(); parent.Controls.Add(b); }
    void Write(string message) { if (!IsDisposed) log.AppendText($"{DateTime.Now:HH:mm:ss} {message}\r\n"); }
    TouchSession NewSession() => new((HandPosture)posture.SelectedIndex);
    FlingStrength? SelectedStrength() => strength.SelectedIndex switch { 1 => FlingStrength.Soft, 2 => FlingStrength.Normal, 3 => FlingStrength.Strong, _ => null };
    async void Run(Func<Task> action) { if (busy || closing) { Write("已有操作正在执行。"); return; } busy = true; posture.Enabled = false; strength.Enabled = false; try { await action(); } catch (Exception ex) { Write("操作失败: " + ex.Message); } finally { busy = false; if (!IsDisposed) { posture.Enabled = true; strength.Enabled = true; } } }
    async Task StartBrowserAsync()
    {
        if (!File.Exists(path.Text)) throw new FileNotFoundException("请先选择存在的 Chromium 可执行文件。", path.Text);
        if (string.IsNullOrWhiteSpace(profile.Text)) throw new ArgumentException("请填写用户目录。");
        var profilePath = Path.GetFullPath(profile.Text.Trim());
        var args = LaunchConfiguration.Parse(arguments.Text, profilePath);
        await CloseBrowserAsync();
        playwright = await Playwright.CreateAsync();
        context = await playwright.Chromium.LaunchPersistentContextAsync(profilePath, new() {
            ExecutablePath = Path.GetFullPath(path.Text), Headless = false,
            Args = args, ViewportSize = ViewportSize.NoViewport
        });
        page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();
        cdp = await context.NewCDPSessionAsync(page); touch = NewSession();
        await page.GotoAsync($"http://127.0.0.1:{server.Port}/");
        await SyncPostureAsync();
        await page.EvaluateAsync("() => window.diag.start('PWTest 连续操作')");
        Write("已开始连续采集；可进行多次操作，需要导出时点击“停止并导出”。");
        var dimensions = await page.EvaluateAsync<string>("() => JSON.stringify({innerWidth, innerHeight, screen: {width:screen.width,height:screen.height,availWidth:screen.availWidth,availHeight:screen.availHeight}, dpr:devicePixelRatio, maxTouchPoints:navigator.maxTouchPoints})");
        Write("浏览器已打开本地诊断页；实际屏幕与视口: " + dimensions);
        Write("用户目录: " + profilePath);
    }
    async Task SyncPostureAsync()
    {
        if (page == null || page.IsClosed) return;
        var mode = posture.SelectedIndex switch { 1 => "left", 2 => "index", _ => "right" };
        await page.EvaluateAsync("mode => window.diag.setPosture(mode)", mode);
    }
    void EnsureReady() { if (page == null || page.IsClosed || cdp == null || touch == null) throw new InvalidOperationException("请先启动浏览器。"); }
    async Task EvalAsync(string code) { EnsureReady(); await page!.EvaluateAsync(code); Write("采集已开始。"); }
    async Task ActionAsync<T>(string label, Func<Task<T>> action)
    {
        EnsureReady();
        await SyncPostureAsync();
        var recording = await page!.EvaluateAsync<bool>("() => window.diag.isRecording()");
        if (recording) await page.EvaluateAsync("data => window.diag.markAction(data)", new { name = label, posture = posture.Text, phase = "begin", strength = strength.Text });
        else Write("当前采集已停止；本次动作不会写入记录。点击“开始新采集”可重新开始。");
        try
        {
            var result = await action();
            var traces = result switch
            {
                HumanSwipeTrace trace => new[] { trace },
                IReadOnlyList<HumanSwipeTrace> list => list.ToArray(),
                _ => Array.Empty<HumanSwipeTrace>()
            };
            var summaries = traces.Select(t => new { intent = t.Intent.ToString(), direction = t.Direction.ToString(), strength = t.FlingStrength?.ToString(), plannedReleaseVelocity = t.TargetReleaseVelocityPxPerSecond, plannedDurationMs = t.DurationMs, scrollChanged = t.ScrollChanged }).ToArray();
            object? observation = result is ScrollObservation or InertiaStopResult ? result : null;
            if (recording && !page.IsClosed)
                await page.EvaluateAsync("data => window.diag.markAction(data)", new { name = label, posture = posture.Text, phase = "end", strength = strength.Text, traces = summaries, observation });
            Write($"动作完成: {label}; 输入轨迹: {traces.Length}; 甩动强度: {string.Join(", ", traces.Where(t => t.FlingStrength != null).Select(t => t.FlingStrength))}; 结果: {result}");
        }
        catch
        {
            if (recording && !page.IsClosed)
                try { await page.EvaluateAsync("data => window.diag.markAction(data)", new { name = label, posture = posture.Text, phase = "failed" }); } catch { }
            throw;
        }
    }
    async Task<object> FlingObserveAsync(bool stop)
    {
        var trace = await touch!.FlingForObservationAsync(page!, cdp!, SelectedStrength(), shutdown.Token);
        if (trace == null) return "没有可滑动的目标";
        if (stop)
            return await touch.StopInertiaAsync(page!, cdp!, trace.StartX, trace.StartY, page!.Locator("#stopPad"), shutdown.Token);
        return await touch.ObserveScrollAsync(page!, cdp!, trace.StartX, trace.StartY, TimeSpan.FromSeconds(3), shutdown.Token);
    }
    async Task SaveAsync()
    {
        EnsureReady(); var json = await page!.EvaluateAsync<string>("() => JSON.stringify(window.diag.stop(), null, 2)");
        using var dlg = new SaveFileDialog { Filter = "JSON|*.json", FileName = $"pwtest-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dlg.ShowDialog() == DialogResult.OK) { await File.WriteAllTextAsync(dlg.FileName, json); Write("已保存: " + dlg.FileName); }
        else Write("未保存采集结果，可再次点击停止并导出。");
    }
    void Compare()
    {
        try { using var dlg = new OpenFileDialog { Filter = "JSON|*.json", Multiselect = true, Title = "选择两个采集文件" }; if (dlg.ShowDialog() != DialogResult.OK) return; if (dlg.FileNames.Length != 2) throw new InvalidOperationException("请选择两个文件。"); Write("对比结果:\r\n" + string.Join("\r\n\r\n", dlg.FileNames.Select(ResultComparison.Describe))); }
        catch (Exception ex) { Write("对比失败: " + ex.Message); }
    }
    async Task CloseBrowserAsync()
    {
        if (context != null) await context.CloseAsync();
        if (browser != null) await browser.CloseAsync();
        playwright?.Dispose(); context = null; browser = null; playwright = null; page = null; cdp = null; touch = null;
    }
    async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return; e.Cancel = true; closing = true; shutdown.Cancel(); server.Dispose();
        try { await CloseBrowserAsync(); } catch (Exception ex) { Write("关闭浏览器: " + ex.Message); }
        shutdown.Dispose(); Close();
    }
}
