using BrowserVisionAgent;
using Microsoft.Playwright;
using System.Diagnostics;


namespace WinFormsApp1
{
    public partial class Form1 : Form
    {
        public int DebugPort = 9527;

        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;


            var chromePath = Path.Combine(
            baseDir,
            "file",
            "chrome-win",
            "145.0.7632.110",
            "chrome.exe");

            if (!File.Exists(chromePath))
            {
                Console.WriteLine($"Chrome不存在: {chromePath}");
                return;
            }

            // 单独的浏览器用户目录
            var userDataDir = Path.Combine(
                baseDir,
                "chrome-profile");

            Directory.CreateDirectory(userDataDir);
            // 启动 Chrome
            //var userDataDir = "--user-data-dir=\"{userDataDir}\"",

            var args = $"--user-data-dir=\"{userDataDir}\" --remote-debugging-port={DebugPort} --disable-field-trial-config --disable-background-networking --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-breakpad --no-default-browser-check --disable-dev-shm-usage --disable-edgeupdater --disable-features=AvoidUnnecessaryBeforeUnloadCheckSync,BoundaryEventDispatchTracksNodeRemoval,DestroyProfileOnBrowserClose,DialMediaRouteProvider,GlobalMediaControls,HttpsUpgrades,LensOverlay,MediaRouter,PaintHolding,ThirdPartyStoragePartitioning,Translate,AutoDeElevate,RenderDocument,OptimizationHints,msForceBrowserSignIn,msEdgeUpdateLaunchServicesPreferredVersion,DnsOverHttps,UseDnsHttpsSvcbAlpn --enable-features=CDPScreenshotNewSurface --disable-hang-monitor --disable-prompt-on-repost --disable-renderer-backgrounding --force-color-profile=srgb --no-first-run --password-store=basic --use-mock-keychain --no-service-autorun --export-tagged-pdf --disable-search-engine-choice-screen --edge-skip-compat-layer-relaunch --disable-infobars --disable-sync --disable-blink-features=AutomationControlled --disable-logging --disable-quic --use-fake-ui-for-media-stream --use-fake-device-for-media-stream --enable-unsafe-swiftshader --show-avatar-button=never --disable-http2-grease-settings --hide-bad-flags --hide-crashed-bubble --force-prefers-no-reduced-motion --virtual-clipboard --touch-events=enabled --user-agent=\"Mozilla/5.0 (Linux; Android 13; I2201 Build/TP1A.220624.014; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/120.0.6099.205 Mobile Safari/537.36\" --window-position=0,0 --window-size=411,914 --device-pixel-ratio=2.625 --screen-size=411,914 --screen-avail-size=411,802 --screen-color-depth=24 --incognito --enable-incognito-themes --platform=\"Android\" --screen-color-depth=24 --platform-version=\"13\" --full-version=120.0.6099.205 --make=\"vivo\" --disable-full-version-list --disable-brand-version-list --product-model=\"I2201\" --fingerprint=65125325 --ssl-grease-cipher=45504 --netinfo-type=wifi --netinfo-effective=4g --netinfo-rtt=265 --force-webrtc-ip-handling-policy --webrtc-ip-handling-policy=disable_non_proxied_udp --webgl-vendor=\"Google Inc. (Qualcomm)\" --webgl-renderer=\"ANGLE (Qualcomm, Adreno 730, OpenGL ES 3.2)\" --geolocation-permission=allow --hardware-concurrency=8 --device-memory=8 --js-memory-info=\"10000000|10000000|1136000000\" --max-touch-points=5 --storage-quota=\"0|291370581360\" about:blank";

            var psi = new ProcessStartInfo
            {
                FileName = chromePath,
                Arguments = string.Join(" ", args),
                UseShellExecute = false,
                CreateNoWindow = false
            };

            var process = Process.Start(psi);

            if (process == null)
            {
                Console.WriteLine("Chrome启动失败");
                return;
            }
        }

        private static async Task WaitForCdpAsync(int port, TimeSpan timeout)
        {
            using var http = new HttpClient();
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                try
                {
                    var response = await http.GetAsync($"http://127.0.0.1:{port}/json/version");

                    if (response.IsSuccessStatusCode)
                        return;
                }
                catch
                {
                    // Chrome还没起来
                }
                await Task.Delay(200);
            }
            throw new TimeoutException($"等待CDP端口 {port} 超时");
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Task.Run(async () =>
            {

                /*
           * 等待CDP
           */
                await WaitForCdpAsync(
                    DebugPort,
                    TimeSpan.FromSeconds(15));

                // Playwright
                using var playwright = await Playwright.CreateAsync();

                var browser = await playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{DebugPort}");

                var context = browser.Contexts.First();

                IPage page;

                if (context.Pages.Count > 0)
                {
                    page = context.Pages[0];
                }
                else
                {
                    page = await context.NewPageAsync();
                }

                Console.WriteLine($"已连接页面: {page.Url}");

                // 打开一个页面测试
                var url = "https://ada.baidu.com/site/wjz1d6o6c/fl?imid=401881cfce2df02beb25fc3b858632b3&bd_bxst=EiaKSUKNp2QJP3jX00cD0r7Kd6iSKlrH06000KpLJnQFdtMwo1cYY2Z2_T5BVqZgzIAE000000000000cD77PDwKrjbkn1T4fH7APWPArRnswHKDPWmdnDnLwW7atUgPU5w2EW_j0000D3Rl_iD0000JeDqERfD00KD0m0Dps_M2GVxFYpOj3oxwnWHfCTJkdPo5Ltjo8Sbls_M2GVxFYpOj3oxwnWHfCTJkdPo5Ltjo8SbVdtMwoe2ezVyLYiLRv_Ox1rA4JUXCs_hq3oevVevs800000jOOOOOOOOOOL1yImb&trinfo=XzF9uvNFUhuEcWGech7MuiclnzsBIvNGuv9YcWCYrisBuLFEIg0BrWDzPzsBug9scWCknj0zxisBTAk9UBclnH6zPHfznjT3PisBIy4GIaclnHn3rjTznW6vPW0_cMIGUhuEcWCkPjf4nWTkPj6LPHnsQaFWIaclcWTdcBsBuAPYcWCBwDuRcBsBpyw-miclnHfYP10Ln1c1nHRdQaF9uvRBrM_Bug9spyfBrWDsnMY_chIbiy4hUzclXzF-XZKGuaclnH0zxisBThNMpyq8cWGechPEUhuGuAN8mvRBrW0_chN3TA-bcWCknjFqxf#QD=YF5-KD-JSNJ&bd_vid=rHc1rHDknHDYn16zrHbsrHTdndtznWFxn-tknjKxP7tkPjfLnjT1nWnkPHR&fid=rHc1rHDknHDYn16zrHbsrHTdndtznWFxn6&ch=4&ch=4";


                await page.GotoAsync(url);
                await Task.Delay(5000);

                var ai = new QwenVisionClient("http://211.154.24.179:18090");
                var agent = new BrowserAgent(page, ai);

                /*
                * 这里是目标
                */
                const string goal = """
                自动完成当前网页中的交互流程。

                根据页面当前显示的内容，
                选择合理的按钮或选项继续。

                如果下一步内容不在当前屏幕，
                则向上或向下滑动查找。

                页面正在加载或等待回复时，
                应等待。

                当页面流程已经完成时返回 finish。
                """;

                using var cts =
                    new CancellationTokenSource();

                Console.CancelKeyPress +=
                    (_, e) =>
                    {
                        e.Cancel = true;
                        cts.Cancel();
                    };

                await agent.RunAsync(
                    goal,
                    maxSteps: 100,
                    cancellationToken:
                        cts.Token);


                //Console.WriteLine("Playwright连接成功");





                //Console.ReadLine();


            });

        }

        private void button3_Click(object sender, EventArgs e)
        {




        }
    }
}
