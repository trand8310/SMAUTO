using MainClient.Common;
using MainClient.Logging;
using MainClient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using QTP;
using QTP.Common;
using QTP.Common.Infrastructure;
using QTP.Plugins;
using Serilog;
using Serilog.Events;
using System.Diagnostics;
using System.Xml.Linq;



namespace MainClient
{
    static class Program
    {
        private static readonly TimeSpan RestartCooldown = TimeSpan.FromMinutes(2);
        private static int _restartRequested;
        private static DateTime _lastRestartRequestUtc = DateTime.MinValue;
        private static PeriodicTimer? _errorDialogTimer;
        private static CancellationTokenSource? _errorDialogCts;

        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
 
            ApplicationConfiguration.Initialize();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (sender, e) =>
            {
                Log.Error(e.Exception, "Application ThreadException");
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                BrowserLifecycleLog.Write("application", "application.unhandled_exception", "AppDomain.UnhandledException", "客户端发生未处理异常，可能触发重启并中断浏览器执行",
                    new { e.IsTerminating, error = (e.ExceptionObject as Exception)?.ToString() });
                Log.Fatal(e.ExceptionObject as Exception, "UnhandledException");
                RestartApplication();
            };

            AppDomain.CurrentDomain.FirstChanceException += (sender, e) =>
            {
                //Log.Debug(e.Exception, "FirstChanceException");
            };

            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Log.Error(e.Exception, "TaskScheduler UnobservedTaskException");
                e.SetObserved();
            };


            var appSettings = new AppSettings();
            UserConfigService.Init(appSettings);
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile("appsettings.user.json", optional: true, reloadOnChange: true)
                .Build();
            configuration.GetSection("AppSettings").Bind(appSettings);

            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);


            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                // ✅ X5Sec 专用日志
                .WriteTo.Logger(lc => lc
                    .Filter.ByIncludingOnly(e =>
                        e.Properties.ContainsKey("LogType") &&
                        e.Properties["LogType"].ToString().Contains("X5Sec"))
                    .WriteTo.File(
                        Path.Combine(logDir, "x5sec-.log"),
                        rollingInterval: RollingInterval.Day))
                // ✅ Playwright 并发追踪专用日志（按消息前缀分流）
                //.WriteTo.Logger(lc => lc
                //    .Filter.ByIncludingOnly(e => e.RenderMessage().Contains("[PWTRACE]"))
                //    .WriteTo.File(
                //        Path.Combine(logDir, "playwright-.log"),
                //        rollingInterval: RollingInterval.Day))
                // ✅ 普通日志（排除 X5Sec）
                .WriteTo.Logger(lc => lc
                    .Filter.ByExcluding(e =>
                        (e.Properties.ContainsKey("LogType") &&
                        e.Properties["LogType"].ToString().Contains("X5Sec")) ||
                        e.RenderMessage().Contains("[PWTRACE]"))
                    .WriteTo.File(
                        Path.Combine(logDir, "app-.log"),
                        rollingInterval: RollingInterval.Day))
                //.WriteTo.File(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "app-.log"), rollingInterval: RollingInterval.Day)
                .WriteTo.Sink<UiLogSink>()
                .CreateLogger();

            BrowserLifecycleLog.Write("application", "application.started", "MainClient.Program", "客户端启动，独立浏览器生命周期日志已启用",
                new { logFile = BrowserLifecycleLog.CurrentFilePath });
            if (BrowserLifecycleLog.LastWriteError is { } logError) Log.Warning("Browser lifecycle log unavailable: {Error}", logError);
            else Log.Information("浏览器独立诊断日志: {LogFile}", BrowserLifecycleLog.CurrentFilePath);

            var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            var parentDir = Directory.GetParent(baseDir)?.FullName ?? AppDomain.CurrentDomain.BaseDirectory;
            var packagesDir = Path.Combine(parentDir, "packages");
            if (!Directory.Exists(packagesDir))
            {
                Directory.CreateDirectory(packagesDir);
            }


            var builder = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.Configure<QTP.Common.AdeOptions>(opt =>
                    {
                        opt.AppVersion = AppConsts.AppVersion;
                    });

                    services.AddSingleton(appSettings);
                    services.AddHttpClient();
                    services.AddSingleton<IPlaywrightProvider, PlaywrightProvider>();
                    services.AddSingleton<FileUpdater>(sp =>
                    {
                        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                        var logger = sp.GetRequiredService<ILogger<FileUpdater>>();
                        var httpClient = httpClientFactory.CreateClient();

                        return new FileUpdater(httpClient, logger);
                    });

                    services.AddSingleton<ChineseNameGenerator>();
                    services.AddSingleton<ChromiumSessionManager>(sp =>
                    {
                        var manager = new ChromiumSessionManager();
                        manager.Reclaimed += result =>
                        {
                            Log.Information("Chromium reclaimed: {ExecutionId}, reason={Reason}, exit={ExitConfirmed}, forced={Forced}, exitCode={ExitCode}, error={Error}",
                                result.ExecutionId, result.Reason, result.ExitConfirmed, result.Forced, result.ExitCode, result.Error);
                            if (BrowserLifecycleLog.LastWriteError is { } journalError)
                                Log.Warning("Browser lifecycle log write failed: {LogFile}, error={Error}", BrowserLifecycleLog.CurrentFilePath, journalError);
                            if (result.Reason != BrowserStopKind.Completed && !string.IsNullOrWhiteSpace(result.DiagnosticTail))
                                Log.Warning("Chromium diagnostics: {ExecutionId}, {Diagnostics}", result.ExecutionId, result.DiagnosticTail);
                        };
                        manager.ProfileCleaned += result =>
                        {
                            if (!result.Deleted) Log.Warning("Browser profile cleanup failed: {Profile}, attempts={Attempts}, error={Error}", result.UserDataDir, result.Attempts, result.Error);
                        };
                        return manager;
                    });
                    services.AddSingleton<IBrowserProcessManager>(sp => sp.GetRequiredService<ChromiumSessionManager>());
                    services.AddSingleton<BrowserRuntimeManager>(sp =>
                    {
                        var runtime = new BrowserRuntimeManager(sp.GetRequiredService<IPlaywrightProvider>(), sp.GetRequiredService<IBrowserProcessManager>(),
                            new(Math.Max(1, appSettings.MaximumConcurrency), Math.Max(1, appSettings.BrowserLaunchConcurrency)));
                        runtime.Reclaimed += result => Log.Information("Browser session reclaimed: {ExecutionId}, reason={Reason}, exit={ExitConfirmed}, cleanupErrors={CleanupErrors}",
                            result.ExecutionId, result.Reason, result.ExitConfirmed, result.CleanupErrors);
                        return runtime;
                    });
                    services.AddSingleton<TaskStatsAggregator>();
                    services.AddSingleton<AdeHelper>();
                    services.AddSingleton<IpHelper>();
                    services.AddSingleton<ProxyTester>();
                    services.AddTransient<SMAdTask>();
                    services.AddSingleton<Func<SMAdTask>>(sp => () => sp.GetRequiredService<SMAdTask>());
                    services.AddSingleton<SmAdExecutor>();
                    services.AddTransient<MainForm>();

                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                })
                .UseSerilog();


            var host = builder.Build();
            StartErrorDialogGuard();
            try
            {
                Application.Run(host.Services.GetRequiredService<MainForm>());
            }
            finally
            {
                BrowserLifecycleLog.Write("application", "application.stopping", "MainClient.Program.finally", "客户端窗口退出，开始释放所属浏览器运行时");
                StopErrorDialogGuard();
                // ApplicationExit async handlers are not awaited by WinForms.
                // The form defers closing until tasks drain; complete host disposal here.
                try
                {
                    Task.Run(async () =>
                    {
                        await host.Services.GetRequiredService<BrowserRuntimeManager>().DisposeAsync();
                        if (host is IAsyncDisposable asyncHost) await asyncHost.DisposeAsync();
                        else host.Dispose();
                    }).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    BrowserLifecycleLog.Write("application", "application.shutdown_failed", "MainClient.Program.finally", "客户端资源回收异常", new { error = ex.ToString() });
                    Log.Fatal(ex, "Application resource shutdown failed");
                    Environment.ExitCode = 1;
                }
            }
        }

        static void RestartApplication()
        {
            if (Interlocked.Exchange(ref _restartRequested, 1) == 1)
            {
                Log.Warning("RestartApplication skipped: restart already requested.");
                return;
            }

            var utcNow = DateTime.UtcNow;
            var elapsed = utcNow - _lastRestartRequestUtc;
            if (elapsed >= TimeSpan.Zero && elapsed < RestartCooldown)
            {
                Log.Warning("RestartApplication skipped due to cooldown. Elapsed={ElapsedSeconds}s", elapsed.TotalSeconds);
                return;
            }

            _lastRestartRequestUtc = utcNow;
            BrowserLifecycleLog.Write("application", "application.restart_requested", "MainClient.Program.RestartApplication", "客户端请求重启；当前进程将退出");

            try
            {
                var exePath = Application.ExecutablePath;
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "restart",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "RestartApplication failed");
            }
            finally
            {

                Environment.Exit(1);
            }
        }

        private static void StartErrorDialogGuard()
        {
            StopErrorDialogGuard();

            _errorDialogCts = new CancellationTokenSource();
            _errorDialogTimer = new PeriodicTimer(TimeSpan.FromSeconds(8));
            var token = _errorDialogCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (await _errorDialogTimer.WaitForNextTickAsync(token))
                    {
                        CommonHelper.ClearAllErrorMsgDialog();
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error dialog guard stopped unexpectedly.");
                }
            }, token);
        }

        private static void StopErrorDialogGuard()
        {
            try
            {
                _errorDialogCts?.Cancel();
            }
            catch
            {
            }

            _errorDialogTimer?.Dispose();
            _errorDialogTimer = null;

            _errorDialogCts?.Dispose();
            _errorDialogCts = null;
        }
    }
}
