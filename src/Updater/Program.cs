
using System.Diagnostics;
using System.IO.Compression;
using System.Management;
using System.Text;

namespace Updater
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static async Task Main(string[] args)
        {
            try
            {
                ClearLocalChromeProcesses();
            }
            catch (Exception)
            {

            }

            var packagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages");
            if (!Directory.Exists(packagesDir))
            {
                Directory.CreateDirectory(packagesDir);
            }

            if (args.Length == 0)
            {
                var appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
                if (Directory.Exists(appDir))
                {
                    var exePath = Path.Combine(appDir, "MainClient.exe");
                    if (System.IO.File.Exists(exePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exePath,
                            WorkingDirectory = Path.GetDirectoryName(exePath)!,
                            UseShellExecute = false,
                            CreateNoWindow = false
                        });
                        Environment.Exit(0);
                        return;
                    }
                }
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm());

            }
            else
            {

                switch (args[0].ToLower())
                {
                    case "--update-version":
                        if (args.Length >= 3)
                        {
                            string appExePath = args[1];
                            string zipFilePath = args[2];
                            string currentVersion = args[3];
                            string targetVersion = args[4];

                            await WaitForExitAsync(appExePath);
                            string historyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history");
                            string appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
                            string tmpDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_tmp");
                            string bakDir = Path.Combine(historyDir, currentVersion);
                            if (!Directory.Exists(historyDir))
                            {
                                Directory.CreateDirectory(historyDir);
                            }

                            if (Directory.Exists(tmpDir))
                                Directory.Delete(tmpDir, true);
                            ZipFile.ExtractToDirectory(zipFilePath, tmpDir);
                            if (Directory.Exists(appDir))
                            {
                                try
                                {
                                    var files = new string[] { "appsettings.json", "appsettings.user.json" };
                                    foreach (var file in files)
                                    {
                                        var sourceFileName = Path.Combine(appDir, file);
                                        var destFileName = Path.Combine(tmpDir, file);
                                        if (System.IO.File.Exists(sourceFileName))
                                            File.Copy(sourceFileName, destFileName, overwrite: true);
                                    }
                                    //当更新的版本中不含浏览器目录时,把当前正运行的浏览器目眼光拷贝到新版本中
                                    if (!System.IO.Directory.Exists(Path.Combine(tmpDir, "File", "chrome-win")))
                                    {
                                        CopyDirectory(Path.Combine(appDir, "File", "chrome-win"), Path.Combine(tmpDir, "File", "chrome-win"));
                                    }

                                    if (Directory.Exists(bakDir))
                                        Directory.Delete(bakDir, true);
                                    Directory.Move(appDir, bakDir);
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(ex.Message);

                                }
                            }
                            // 3. 将临时目录移动为正式 app
                            Directory.Move(tmpDir, appDir);
                            //// 4. 删除备份（可选：启动成功后再删）
                            //if (Directory.Exists(bakDir))
                            //{
                            //    //Directory.Move(appDir, bakDir);
                            //    //Directory.Delete(bakDir, true);
                            //}

                            appExePath = Path.Combine(appDir, "MainClient.exe");
                            if (System.IO.File.Exists(appExePath))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = appExePath,
                                    Arguments = $"restart",
                                    WorkingDirectory = Path.GetDirectoryName(appExePath)!,
                                    UseShellExecute = false,
                                    CreateNoWindow = false
                                });
                                Environment.Exit(0);
                            }


                        }
                        else
                        {
                            //Console.WriteLine("错误：参数不足，正确用法 --switch-version <当前程序路径> <目标版本>");
                        }
                        return;

                    case "--switch-version":
                        if (args.Length >= 3)
                        {
                            string appExePath = args[1];
                            string currentVersion = args[2];
                            string targetVersion = args[3];
                            await WaitForExitAsync(appExePath);

                            string historyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history", targetVersion);
                            string appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
                            string tmpDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_tmp");
                            string bakDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_bak");
                            if (!Directory.Exists(historyDir))
                            {
                                Directory.CreateDirectory(historyDir);
                            }
                            CopyDirectory(historyDir, tmpDir);
                            //Directory.Move(historyDir, tmpDir);

                            if (Directory.Exists(appDir))
                            {
                                var files = new string[] { "appsettings.json", "appsettings.user.json" };
                                foreach (var file in files)
                                {
                                    var sourceFileName = Path.Combine(appDir, file);
                                    var destFileName = Path.Combine(tmpDir, file);
                                    if (System.IO.File.Exists(sourceFileName))
                                        File.Copy(sourceFileName, destFileName, overwrite: true);
                                }

                                if (Directory.Exists(bakDir))
                                    Directory.Delete(bakDir, true);
                                Directory.Move(appDir, bakDir);
                            }
                            // 3. 将临时目录移动为正式 app
                            Directory.Move(tmpDir, appDir);
                            // 4. 删除备份（可选：启动成功后再删）
                            if (Directory.Exists(bakDir))
                            {
                                Directory.Delete(bakDir, true);
                            }
                            appExePath = Path.Combine(appDir, "MainClient.exe");
                            if (System.IO.File.Exists(appExePath))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = appExePath,
                                    Arguments = $"restart",
                                    WorkingDirectory = Path.GetDirectoryName(appExePath)!,
                                    UseShellExecute = false,
                                    CreateNoWindow = false
                                });
                                Environment.Exit(0);
                            }


                        }
                        else
                        {
                            //Console.WriteLine("错误：参数不足，正确用法 --switch-version <当前程序路径> <目标版本>");
                        }
                        return;

                    case "--auto-start":
                        if (args.Length >= 1)
                        {
                            string appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
                            if (Directory.Exists(appDir))
                            {
                                var appExePath = Path.Combine(appDir, "MainClient.exe");
                                if (System.IO.File.Exists(appExePath))
                                {
                                    StringBuilder builder = new StringBuilder();
                                    for (int i = 1; i < args.Length; i++)
                                    {
                                        builder.Append($" {args[i]}");
                                    }

                                    Process.Start(new ProcessStartInfo
                                    {
                                        FileName = appExePath,
                                        Arguments = $"restart {builder.ToString()}",
                                        WorkingDirectory = Path.GetDirectoryName(appExePath)!,
                                        UseShellExecute = false,
                                        CreateNoWindow = false
                                    });
                                    Environment.Exit(0);
                                }
                            }
                        }


                        return;
                }
            }
        }



        static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }
        static void BackupDirectory(string sourceDir, string backupDir, string[]? excludeDirs = null, string[]? excludeFiles = null)
        {
            Directory.CreateDirectory(backupDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                if (excludeFiles != null && excludeFiles.Any(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
                    continue;
                File.Copy(file, Path.Combine(backupDir, fileName), true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                if (excludeDirs != null && excludeDirs.Any(d => d.Equals(dirName, StringComparison.OrdinalIgnoreCase)))
                    continue;
                BackupDirectory(dir, Path.Combine(backupDir, dirName), excludeDirs, excludeFiles);
            }
        }

        static void TryMove(string src, string dst)
        {
            if (!Directory.Exists(src)) return;
            TryDelete(dst); // 清理历史遗留
            Directory.Move(src, dst); // 这一步比 Delete 稳定很多
        }

        static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch
            {
                // 忽略，留给下次启动再清
            }
        }
        static async Task WaitForExitAsync(string exePath)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath));
            foreach (var p in processes)
            {
                try
                {
                    if (!p.HasExited)
                        await p.WaitForExitAsync();
                }
                catch { }
            }
        }




        static void ClearLocalChromeProcesses()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string[] targets = { "chrome.exe", "MainClient.exe" };
            using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, Name, ExecutablePath FROM Win32_Process"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    try
                    {
                        string name = obj["Name"]?.ToString();
                        string path = obj["ExecutablePath"]?.ToString();

                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path))
                            continue;

                        if (!targets.Contains(name, StringComparer.OrdinalIgnoreCase))
                            continue;

                        if (!path.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                            continue;

                        int pid = Convert.ToInt32(obj["ProcessId"]);

                        try
                        {
                            var proc = Process.GetProcessById(pid);
                            if (!proc.HasExited)
                            {
                                proc.Kill(true); // 杀子进程
                                proc.WaitForExit(3000);
                            }
                        }
                        catch { }
                    }
                    catch { }
                }
            }
        }



    }
}