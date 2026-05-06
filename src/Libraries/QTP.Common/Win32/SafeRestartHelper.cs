using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QTP.Common.Win32
{
    public static class SafeRestartHelper
    {
        private static readonly object RestartLock = new();
        private static bool RestartRequested;

        public static void RequestSystemRestart(string reason)
        {
            try
            {
                Log.Fatal("准备重启系统。Reason={Reason}", reason);

                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /f /t 5 /c \"系统检测到持续性内存/页面文件压力过高，自动重启\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "触发系统重启失败");
            }
        }

        public static bool ForceRestart(int delaySeconds = 0)
        {
            try
            {
                if (delaySeconds < 0)
                    delaySeconds = 0;

                lock (RestartLock)
                {
                    if (RestartRequested)
                        return true;

                    RestartRequested = true;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = $"/r /f /t {delaySeconds}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi);

                return true;
            }
            catch (Exception ex)
            {
                lock (RestartLock)
                {
                    RestartRequested = false;
                }

                Console.WriteLine("重启失败：" + ex.Message);
                return false;
            }
        }

        public static bool CancelRestart()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/a",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi);

                lock (RestartLock)
                {
                    RestartRequested = false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("取消重启失败：" + ex.Message);
                return false;
            }
        }

    }
}
