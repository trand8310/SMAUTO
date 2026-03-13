

namespace MainClient
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Management;
    using System.Threading;

    public static class SystemCleaner
    {
        public static void RestartComputer()
        {
            var result = MessageBox.Show(
                "确定要重启计算机吗？",
                "系统提示",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /f /t 0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
        }
        /// <summary>
        /// 注销系统
        /// </summary>
        public static void LogoutComputer()
        {
            var result = MessageBox.Show(
                "确定要注销计算机吗？",
                "系统提示",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/l",
                    UseShellExecute = true
                });
            }
        }



        /// <summary>
        /// 重启 Explorer 并清理图标/缩略图缓存
        /// </summary>
        public static void RestartExplorerAndClearCache()
        {
            try
            {
                // ===============================
                // 1️⃣ 结束 Explorer（WMI方式）
                // ===============================
                using (var searcher =
                    new ManagementObjectSearcher(
                        "SELECT ProcessId, Name FROM Win32_Process WHERE Name='explorer.exe'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        try
                        {
                            int pid = Convert.ToInt32(obj["ProcessId"]);

                            try
                            {
                                var proc = Process.GetProcessById(pid);

                                if (!proc.HasExited)
                                {
                                    proc.Kill(true);
                                    proc.WaitForExit(3000);
                                }
                            }
                            catch { }
                        }
                        catch { }
                    }
                }

                // 等待 Explorer 完全退出
                Thread.Sleep(800);

                // ===============================
                // 2️⃣ 删除缓存文件
                // ===============================
                string localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

                string explorerDir = Path.Combine(
                    localAppData,
                    @"Microsoft\Windows\Explorer");

                if (Directory.Exists(explorerDir))
                {
                    var files = Directory.GetFiles(explorerDir)
                        .Where(f =>
                        {
                            string name = Path.GetFileName(f);
                            return name.StartsWith("thumbcache", StringComparison.OrdinalIgnoreCase)
                                || name.StartsWith("iconcache", StringComparison.OrdinalIgnoreCase);
                        });

                    foreach (var file in files)
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // 被系统占用时忽略
                        }
                    }
                }

                // ===============================
                // 3️⃣ 重新启动 Explorer
                // ===============================
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                });
            }
            catch
            {
                // 可按需记录日志
            }
        }

        public static void RestartExplorerAndRdpclip()
        {
            try
            {
                // =========================================
                // 1️⃣ 结束 Explorer
                // =========================================
                KillProcessByName("explorer.exe");
                Thread.Sleep(800);
                // =========================================
                // 2️⃣ 清理 Explorer 缓存
                // =========================================
                ClearExplorerCache();
                // =========================================
                // 3️⃣ 重启 Explorer
                // =========================================
                StartExplorer();
                // =========================================
                // 4️⃣ 重启 rdpclip（剪贴板）
                // =========================================
                RestartRdpClip();
            }
            catch
            {
                // 可记录日志
            }
        }

        // ===============================
        // 杀进程（WMI）
        // ===============================
        private static void KillProcessByName(string processName)
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId FROM Win32_Process WHERE Name='{processName}'");

            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    int pid = Convert.ToInt32(obj["ProcessId"]);
                    var proc = Process.GetProcessById(pid);

                    if (!proc.HasExited)
                    {
                        proc.Kill(true);
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }

        // ===============================
        // 清理 Explorer 缓存
        // ===============================
        private static void ClearExplorerCache()
        {
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            string explorerDir = Path.Combine(
                localAppData,
                @"Microsoft\Windows\Explorer");

            if (Directory.Exists(explorerDir))
            {
                var files = Directory.GetFiles(explorerDir)
                    .Where(f =>
                    {
                        string name = Path.GetFileName(f);
                        return name.StartsWith("thumbcache", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith("iconcache", StringComparison.OrdinalIgnoreCase);
                    });

                foreach (var file in files)
                {
                    try { File.Delete(file); }
                    catch { }
                }
            }

            // IconCache.db
            string iconCacheDb = Path.Combine(localAppData, "IconCache.db");
            try
            {
                if (File.Exists(iconCacheDb))
                    File.Delete(iconCacheDb);
            }
            catch { }
        }



        /// <summary>
        /// 启动Explorer
        /// </summary>
        private static void StartExplorer()
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
                    Arguments = "/factory,{682159D9-C321-47CA-B3F1-30E36B9C5E5C}",
                    UseShellExecute = true
                });
                //Process.Start(new ProcessStartInfo
                //{
                //    FileName = explorerPath,
                //    WorkingDirectory = windowsDir,
                //    UseShellExecute = false,
                //    CreateNoWindow = false
                //});
            }
            catch { }
        }


        /// <summary>
        /// 重启explorer
        /// </summary>
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

        /// <summary>
        /// 重启 rdpclip
        /// </summary>
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
    }
}
