using MainClient.Common;
using MainClient.Logging;
using MainClient.LogViewer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using QTP;
using QTP.Common;
using QTP.Common.Infrastructure;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Diagnostics;



namespace MainClient
{
    static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
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
                // ✅ 普通日志（排除 X5Sec）
                .WriteTo.Logger(lc => lc
                    .Filter.ByExcluding(e =>
                        e.Properties.ContainsKey("LogType") &&
                        e.Properties["LogType"].ToString().Contains("X5Sec"))
                    .WriteTo.File(
                        Path.Combine(logDir, "app-.log"),
                        rollingInterval: RollingInterval.Day))
                //.WriteTo.File(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "app-.log"), rollingInterval: RollingInterval.Day)
                .WriteTo.Sink<UiLogSink>()
                .CreateLogger();

            var packagesDir = Path.Combine(Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName!, "packages");
            if (!Directory.Exists(packagesDir))
            {
                Directory.CreateDirectory(packagesDir);
            }


            var builder = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(appSettings);
                    services.AddHttpClient();
                    services.AddSingleton<FileUpdater>(sp =>
                    {
                        var _httpClient = sp.GetRequiredService<HttpClient>();
                        var _logger = sp.GetRequiredService<ILogger<FileUpdater>>();
                        return new FileUpdater(_httpClient, _logger);
                    });
                    
                    services.AddSingleton<ChineseNameGenerator>();
                    services.AddSingleton<ChromiumSessionManager>();
                    services.AddSingleton<TaskStatsAggregator>();
                    services.AddSingleton<AdeHelper>();
                    services.AddSingleton<IpHelper>();
                    services.AddTransient<MainForm>();

                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                })
                .UseSerilog();


            var host = builder.Build();

            ApplicationConfiguration.Initialize();

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += (sender, e) =>
            {
                Log.Error(e.Exception, "Application ThreadException");
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
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

            Application.ApplicationExit += async (sender, e) =>
            {
                CommonHelper.KillAllChromeProcess();
                var pw = host.Services.GetService<IPlaywright>();
                if (pw is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
            };

            Application.Run(host.Services.GetRequiredService<MainForm>());
        }

        static void RestartApplication()
        {
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
    }
}
