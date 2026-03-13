using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Updater
{
    internal static class Program
    {
        private static readonly object _logLock = new();

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static async Task Main(string[] args)
        {
            ClearLocalChromeProcesses();
            var appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
            if (System.IO.Directory.Exists(appDir))
            {
                var handleExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sysinternals", "handle.exe");
                if (File.Exists(handleExePath))
                {
                    HandleExeHelper.UnlockDirectoryByHandleExe(handleExePath, appDir, new string[] { "smaide.exe" });
                }
            }
            EnsureRdpClipRunning();
            var packagesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages");
            if (!Directory.Exists(packagesDir))
            {
                Directory.CreateDirectory(packagesDir);
            }
 

            if (args.Length == 0)
            {

                if (Directory.Exists(appDir))
                {
                    var exePath = Path.Combine(appDir, "MainClient.exe");
                    if (System.IO.File.Exists(exePath))
                    {
                        LaunchAndExit(exePath);
                        return;
                    }
                }

                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm());
                return;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "--update-version":
                    await HandleUpdateVersionAsync(args);
                    return;

                case "--switch-version":
                    await HandleSwitchVersionAsync(args);
                    return;

                case "--auto-start":
                    HandleAutoStart(args);
                    return;
            }
        }




        private static void EnsureRdpClipRunning()
        {
            try
            {
                if (Process.GetProcessesByName("rdpclip").Length > 0)
                    return;

                string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string rdpclipPath = Path.Combine(system32, "rdpclip.exe");

                if (!File.Exists(rdpclipPath))
                    return;

                Process.Start(new ProcessStartInfo
                {
                    FileName = rdpclipPath,
                    WorkingDirectory = system32,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch
            {
            }
        }

        private static void StartRdpClip()
        {
            try
            {
                string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string rdpclipPath = Path.Combine(system32, "rdpclip.exe");
                Process.Start(new ProcessStartInfo
                {
                    FileName = rdpclipPath,
                    WorkingDirectory = system32,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch { }
        }


        private static void RestartRdpClip()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("rdpclip"))
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(true);
                            p.WaitForExit(2000);
                        }
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
                Thread.Sleep(300);
                string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string rdpclipPath = Path.Combine(system32, "rdpclip.exe");

                Process.Start(new ProcessStartInfo
                {
                    FileName = rdpclipPath,
                    WorkingDirectory = system32,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch { }
        }

        private static void RestartExplorer()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(true);
                            p.WaitForExit(3000);
                        }
                    }
                    catch { }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }

                Thread.Sleep(500);

                string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string explorerPath = Path.Combine(windowsDir, "explorer.exe");

                Process.Start(new ProcessStartInfo
                {
                    FileName = explorerPath,
                    WorkingDirectory = windowsDir,
                    UseShellExecute = false,
                    CreateNoWindow = false
                });
            }
            catch { }
        }


        static async Task HandleUpdateVersionAsync(string[] args)
        {
            if (args.Length < 5)
                return;

            string appExePath = args[1];
            string zipFilePath = args[2];
            string currentVersion = SafePathName(args[3]);
            string targetVersion = SafePathName(args[4]);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string historyDir = Path.Combine(baseDir, "history");
            string appDir = Path.Combine(baseDir, "app");
            string tmpDir = Path.Combine(baseDir, "app_tmp");
            string bakDir = Path.Combine(historyDir, $"{currentVersion}_{DateTime.Now:yyyyMMdd_HHmmss}");

            try
            {
                Log("========== --update-version start ==========");
                Log($"appExePath={appExePath}");
                Log($"zipFilePath={zipFilePath}");
                Log($"currentVersion={currentVersion}");
                Log($"targetVersion={targetVersion}");

                if (!File.Exists(zipFilePath))
                    throw new FileNotFoundException("更新压缩包不存在", zipFilePath);

                Directory.CreateDirectory(historyDir);

                await WaitForExitAsync(appExePath);

                // 等完主程序退出后，再做一次定向清锁
                ClearLockApp(appDir);

                DeleteDirectoryWithRetry(tmpDir, retryCount: 10, delayMs: 500);

                ZipFile.ExtractToDirectory(zipFilePath, tmpDir);
                NormalizeExtractedRoot(tmpDir);

                if (Directory.Exists(appDir))
                {
                    CopyPreservedFiles(appDir, tmpDir, new[] { "appsettings.json", "appsettings.user.json" });

                    // 当更新的版本中不含浏览器目录时，把当前版本浏览器目录拷贝到新版本中
                    var newChromeDir = Path.Combine(tmpDir, "File", "chrome-win");
                    var oldChromeDir = Path.Combine(appDir, "File", "chrome-win");
                    if (!Directory.Exists(newChromeDir) && Directory.Exists(oldChromeDir))
                    {
                        CopyDirectory(oldChromeDir, newChromeDir);
                    }

                    DeleteDirectoryWithRetry(bakDir, retryCount: 5, delayMs: 500);
                    MoveDirectoryWithRetry(appDir, bakDir, retryCount: 10, delayMs: 700);
                }

                if (Directory.Exists(appDir) || File.Exists(appDir))
                    throw new IOException($"旧 app 目录仍存在，无法继续更新: {appDir}");

                try
                {
                    MoveDirectoryWithRetry(tmpDir, appDir, retryCount: 10, delayMs: 700);
                }
                catch
                {
                    TryRollback(bakDir, appDir);
                    throw;
                }

                appExePath = Path.Combine(appDir, "MainClient.exe");
                if (File.Exists(appExePath))
                {
                    LaunchAndExit(appExePath, "restart");
                }

                Log("========== --update-version success ==========");
            }
            catch (Exception ex)
            {
                Log("========== --update-version failed ==========");
                Log(ex.ToString());
                MessageBox.Show(ex.ToString(), "更新失败");
            }
        }

        static async Task HandleSwitchVersionAsync(string[] args)
        {
            if (args.Length < 4)
                return;

            string appExePath = args[1];
            string currentVersion = SafePathName(args[2]);
            string targetVersion = SafePathName(args[3]);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string historyVersionDir = Path.Combine(baseDir, "history", targetVersion);
            string appDir = Path.Combine(baseDir, "app");
            string tmpDir = Path.Combine(baseDir, "app_tmp");
            string bakDir = Path.Combine(baseDir, "app_bak");

            try
            {
                Log("========== --switch-version start ==========");
                Log($"appExePath={appExePath}");
                Log($"currentVersion={currentVersion}");
                Log($"targetVersion={targetVersion}");

                await WaitForExitAsync(appExePath);

                ClearLockApp(appDir);

                if (!Directory.Exists(historyVersionDir))
                    throw new DirectoryNotFoundException($"目标历史版本目录不存在: {historyVersionDir}");

                DeleteDirectoryWithRetry(tmpDir, retryCount: 10, delayMs: 500);
                DeleteDirectoryWithRetry(bakDir, retryCount: 10, delayMs: 500);

                CopyDirectory(historyVersionDir, tmpDir);

                if (Directory.Exists(appDir))
                {
                    CopyPreservedFiles(appDir, tmpDir, new[] { "appsettings.json", "appsettings.user.json" });
                    MoveDirectoryWithRetry(appDir, bakDir, retryCount: 10, delayMs: 700);
                }

                if (Directory.Exists(appDir) || File.Exists(appDir))
                    throw new IOException($"旧 app 目录仍存在，无法继续切换版本: {appDir}");

                try
                {
                    MoveDirectoryWithRetry(tmpDir, appDir, retryCount: 10, delayMs: 700);
                }
                catch
                {
                    TryRollback(bakDir, appDir);
                    throw;
                }

                // 切换成功后再删备份
                DeleteDirectoryWithRetry(bakDir, retryCount: 5, delayMs: 500, ignoreFailure: true);

                appExePath = Path.Combine(appDir, "MainClient.exe");
                if (File.Exists(appExePath))
                {
                    LaunchAndExit(appExePath, "restart");
                }

                Log("========== --switch-version success ==========");
            }
            catch (Exception ex)
            {
                Log("========== --switch-version failed ==========");
                Log(ex.ToString());
                MessageBox.Show(ex.ToString(), "切换版本失败");
            }
        }

        static void HandleAutoStart(string[] args)
        {
            if (args.Length < 1)
                return;

            string appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
            if (!Directory.Exists(appDir))
                return;

            var appExePath = Path.Combine(appDir, "MainClient.exe");
            if (!File.Exists(appExePath))
                return;

            StringBuilder builder = new StringBuilder();
            for (int i = 1; i < args.Length; i++)
            {
                if (builder.Length > 0)
                    builder.Append(' ');

                builder.Append(args[i]);
            }

            LaunchAndExit(appExePath, $"restart {builder}".Trim());
        }

        static void ClearLockApp(string appDir)
        {
            try
            {
                var lockers = FileLockHelper.GetLockingProcesses(appDir);
                foreach (var p in lockers)
                {
                    try
                    {
                        if (p.HasExited)
                            continue;

                        // 避免把自己 smaide 干掉
                        if (string.Equals(p.ProcessName, "smaide", StringComparison.OrdinalIgnoreCase))
                            continue;

                        Log($"Killing locking process: {p.Id} - {p.ProcessName}");
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        Log($"Kill process failed: {ex}");
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"ClearLockApp failed: {ex}");
            }
        }

        static void LaunchAndExit(string exePath, string? arguments = null)
        {
            if (!File.Exists(exePath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments ?? string.Empty,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}");
                return;
            }

            Environment.Exit(0);
        }

        static void CopyDirectory(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException($"源目录不存在: {sourceDir}");

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
            if (!Directory.Exists(src))
                return;

            TryDelete(dst);
            Directory.Move(src, dst);
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
            }
        }

        static async Task WaitForExitAsync(string exePath)
        {
            string exeFullPath;
            try
            {
                exeFullPath = Path.GetFullPath(exePath);
            }
            catch
            {
                exeFullPath = exePath;
            }

            string processName = Path.GetFileNameWithoutExtension(exePath);
            var sw = Stopwatch.StartNew();

            while (sw.Elapsed < TimeSpan.FromSeconds(45))
            {
                bool stillRunning = false;
                var processes = Process.GetProcessesByName(processName);

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.HasExited)
                            continue;

                        string? processPath = null;
                        try
                        {
                            processPath = p.MainModule?.FileName;
                        }
                        catch
                        {
                        }

                        // 取不到路径时，保守处理：认为它可能是目标进程
                        if (string.IsNullOrWhiteSpace(processPath))
                        {
                            stillRunning = true;
                            continue;
                        }

                        if (PathsEqual(processPath, exeFullPath))
                        {
                            stillRunning = true;
                        }
                    }
                    catch
                    {
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { }
                    }
                }

                if (!stillRunning)
                    return;

                await Task.Delay(1000);
            }

            // 超时后，再尝试杀一次同路径的主程序
            var killTargets = Process.GetProcessesByName(processName);
            foreach (var p in killTargets)
            {
                try
                {
                    if (p.HasExited)
                        continue;

                    string? processPath = null;
                    try
                    {
                        processPath = p.MainModule?.FileName;
                    }
                    catch
                    {
                    }

                    if (string.IsNullOrWhiteSpace(processPath) || PathsEqual(processPath, exeFullPath))
                    {
                        p.Kill(entireProcessTree: true);
                        p.WaitForExit(3000);
                    }
                }
                catch
                {
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
        }

        static void ClearLocalChromeProcesses()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string[] targets = { "chrome", "MainClient", "node" };
            foreach (var name in targets)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        string? exePath = null;

                        try
                        {
                            exePath = process.MainModule?.FileName;
                        }
                        catch
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(exePath))
                            continue;

                        if (!exePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!process.HasExited)
                        {
                            process.Kill(true);
                            process.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }




        static void CopyPreservedFiles(string sourceAppDir, string targetTmpDir, IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                var sourceFileName = Path.Combine(sourceAppDir, file);
                var destFileName = Path.Combine(targetTmpDir, file);

                if (!File.Exists(sourceFileName))
                    continue;

                var parent = Path.GetDirectoryName(destFileName);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(sourceFileName, destFileName, overwrite: true);
            }
        }

        static void DeleteDirectoryWithRetry(string dir, int retryCount, int delayMs, bool ignoreFailure = false)
        {
            if (!Directory.Exists(dir))
                return;

            Exception? lastEx = null;

            for (int i = 1; i <= retryCount; i++)
            {
                try
                {
                    RemoveReadOnlyAttributes(dir);
                    Directory.Delete(dir, true);
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Log($"DeleteDirectoryWithRetry failed {i}/{retryCount}: {dir}");
                    Log(ex.Message);
                    Thread.Sleep(delayMs);
                }
            }

            if (!ignoreFailure)
                throw new IOException($"删除目录失败: {dir}", lastEx);
        }

        static void MoveDirectoryWithRetry(string source, string dest, int retryCount, int delayMs)
        {
            Exception? lastEx = null;

            for (int i = 1; i <= retryCount; i++)
            {
                try
                {
                    if (!Directory.Exists(source))
                        throw new DirectoryNotFoundException($"源目录不存在: {source}");

                    if (Directory.Exists(dest) || File.Exists(dest))
                        throw new IOException($"目标已存在: {dest}");

                    Directory.Move(source, dest);
                    return;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Log($"MoveDirectoryWithRetry failed {i}/{retryCount}: {source} -> {dest}");
                    Log(ex.Message);
                    Thread.Sleep(delayMs);
                }
            }

            throw new IOException($"移动目录失败: {source} -> {dest}", lastEx);
        }

        static void TryRollback(string bakDir, string appDir)
        {
            try
            {
                if (!Directory.Exists(appDir) && Directory.Exists(bakDir))
                {
                    Directory.Move(bakDir, appDir);
                    Log("Rollback success.");
                }
            }
            catch (Exception ex)
            {
                Log($"Rollback failed: {ex}");
            }
        }

        static void NormalizeExtractedRoot(string tmpDir)
        {
            var dirs = Directory.GetDirectories(tmpDir);
            var files = Directory.GetFiles(tmpDir);

            if (files.Length == 0 && dirs.Length == 1)
            {
                string innerDir = dirs[0];
                bool looksLikeAppRoot =
                    File.Exists(Path.Combine(innerDir, "MainClient.exe")) ||
                    Directory.Exists(Path.Combine(innerDir, "File")) ||
                    File.Exists(Path.Combine(innerDir, "appsettings.json"));

                if (!looksLikeAppRoot)
                    return;

                string normalizedDir = tmpDir + "_normalized_" + Guid.NewGuid().ToString("N");
                Directory.Move(innerDir, normalizedDir);
                Directory.Delete(tmpDir, true);
                Directory.Move(normalizedDir, tmpDir);
            }
        }

        static void RemoveReadOnlyAttributes(string dir)
        {
            if (!Directory.Exists(dir))
                return;

            foreach (var path in Directory.GetFileSystemEntries(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attr = File.GetAttributes(path);
                    if ((attr & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);
                    }
                }
                catch
                {
                }
            }

            try
            {
                var attr = File.GetAttributes(dir);
                if ((attr & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(dir, attr & ~FileAttributes.ReadOnly);
                }
            }
            catch
            {
            }
        }

        static bool PathsEqual(string a, string b)
        {
            string pa = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string pb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(pa, pb, StringComparison.OrdinalIgnoreCase);
        }

        static string SafePathName(string text)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(c, '_');
            }
            return text.Trim();
        }

        static void Log(string message)
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(logDir);

                string logPath = Path.Combine(logDir, $"updater_{DateTime.Now:yyyyMMdd}.log");
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";

                lock (_logLock)
                {
                    File.AppendAllText(logPath, line, Encoding.UTF8);
                }
            }
            catch
            {
            }
        }
    }
}