
using QTP.Common.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;



namespace QTP.Common
{
    public class CommonHelper
    {
        private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";




    /// <summary>
    /// 生成一个随机数
    /// </summary>
    /// <returns></returns>
    public static uint RandomNumber()
        {
            byte[] bytes = new byte[4];
            RandomNumberGenerator.Fill(bytes);
            uint value = BitConverter.ToUInt32(bytes, 0);
            return value;
        }
        public static string GenerateRandomText(int length)
        {
            if (length < 1)
                throw new ArgumentException("Length must be greater than 0", nameof(length));

            var result = new StringBuilder(length);
            var data = new byte[length];

            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(data);
            }
            for (int i = 0; i < length; i++)
            {
                var index = data[i] % Chars.Length;
                result.Append(Chars[index]);
            }
            return result.ToString();
        }

        public static TimeSpan GetRandomizedInterval(int minutes, int maxRandomSeconds)
        {
            return TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(Random.Shared.Next(-180, 180));
        }


        /// <summary>
        /// 随机生成一个满足百分比的数字
        /// </summary>
        /// <param name="probability"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static bool IsEventOccurring(double probability)
        {
            if (probability < 0 || probability > 1)
                throw new ArgumentOutOfRangeException(nameof(probability), "Probability must be between 0 and 1");
            double randomValue = Random.Shared.NextDouble();
            return randomValue < probability;
        }


        public static string HmacSha1Sign(byte[] input, byte[] key)
        {
            HMACSHA1 myhmacsha1 = new HMACSHA1(key);
            MemoryStream stream = new MemoryStream(input);
            return myhmacsha1.ComputeHash(stream).Aggregate("", (s, e) => s + String.Format("{0:x2}", e), s => s);
        }

        public static string ComputeSha1Hash(string input)
        {
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hashBytes = sha1.ComputeHash(inputBytes);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        public static long UnixTimeNow()
        {
            return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds();
        }
        public static long UnixTimeNowSecond()
        {
            return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
        }

        public static string CreateMD5(string input)
        {
            using (var md5 = MD5.Create())
            {
                var result = md5.ComputeHash(Encoding.ASCII.GetBytes(input));
                var strResult = BitConverter.ToString(result);
                return strResult.Replace("-", "").ToLower();
            }
        }

        public static string MD5Hash(string input)
        {
            using (var md5 = MD5.Create())
            {
                var result = md5.ComputeHash(Encoding.ASCII.GetBytes(input));
                var strResult = BitConverter.ToString(result);
                return strResult.Replace("-", "");
            }
        }

        /// <summary>
        /// 比如，要获取-1000~+1000范围的随机数，总的数量为2001个，这样就可以通过代码
        /// Random(Guid.NewGuid().GetHashCode()).Next()%2001 使得到的结果限制在0-2000范围，再减去1000, 结果就是-1000~+1000之间了。
        /// </summary>
        /// <param name="min"></param>
        /// <param name="max"></param>
        /// <returns></returns>
        public static int RandomRange(int min, int max)
        {
            return Random.Shared.Next(min, max);
        }

        /// <summary>
        /// 返回[min, max)之间的随机整数
        /// </summary>
        public static int NextInt(int min, int max)
        {
            return Random.Shared.Next(min, max);
        }
        public static Int64 NextInt64(Int64 min, Int64 max)
        {
            return Random.Shared.NextInt64(min, max);
        }
 

        public static double NextDouble()
        {
            return Random.Shared.NextDouble();
        }

        public static double NextDouble(double min, double max)
        {
            return min + Random.Shared.NextDouble() * (max - min);
        }

        public static Int16 Get16BitHash(string s)
        {
            return (Int16)(s.GetHashCode() & 0xFFFF);
        }

        public static string ComputeHash(string input)
        {
            byte[] bytes = Encoding.Default.GetBytes(input);
            var iSHA = SHA1.Create();
            bytes = iSHA.ComputeHash(bytes);
            StringBuilder buf = new StringBuilder();
            foreach (byte b in bytes)
            {
                buf.AppendFormat("{0:x2}", b);
            }
            return buf.ToString().ToUpper();
        }

        private static string _localIpAddress = string.Empty;
        public static string GetHostName()
        {
            if (string.IsNullOrWhiteSpace(_localIpAddress))
            {
                try
                {
                    var hostinfo = Dns.GetHostName();
                    IPHostEntry iPHostEntry = Dns.GetHostEntry(hostinfo);
                    var addressV = iPHostEntry.AddressList.FirstOrDefault(q => q.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);//ip4地址
                    if (addressV != null)
                    {
                        _localIpAddress = addressV.ToString();
                    }
                    else
                    {
                        _localIpAddress = "";
                    }
                }
                catch (Exception)
                {
                    _localIpAddress = "";
                }
            }
            return _localIpAddress;

        }


        public static void CopyFilesRecursively(DirectoryInfo source, DirectoryInfo target)
        {
            foreach (DirectoryInfo dir in source.GetDirectories())
            {
                CopyFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
            }

            foreach (FileInfo file in source.GetFiles())
            {
                file.CopyTo(Path.Combine(target.FullName, file.Name), true);
            }
        }

        public static long CreateIMEI(long imei)
        {
            var current = imei;
            var checksum = 0;
            for (int i = 0; i < 7; i++)
            {
                var d1 = (int)(current % 10) * 2;
                current = current / 10;
                var d0 = (int)(current % 10);
                current = current / 10;
                checksum += +d0 + d1 / 10 + d1 % 10;
            }
            checksum = 10 - (checksum % 10);
            if (checksum == 10)
                checksum = 0;
            return imei * 10 + checksum;
        }






        public static string CreateDeviceUUID()
        {
            Guid result = Guid.NewGuid();
            byte[] guidBytes = result.ToByteArray();
            for (int i = 0; i < 8; i++)
            {
                byte t = guidBytes[15 - i];
                guidBytes[15 - i] = guidBytes[i];
                guidBytes[i] = t;
            }

            return new Guid(guidBytes).ToString();
        }

        /// <summary>  
        /// 根据GUID获取16位的唯一字符串  
        /// </summary>  
        /// <param name=\"guid\"></param>  
        /// <returns></returns>  
        public static string GuidTo16String()
        {
            long i = 1;
            foreach (byte b in Guid.NewGuid().ToByteArray())
                i *= ((int)b + 1);
            return string.Format("{0:x}", i - DateTime.Now.Ticks);
        }
        /// <summary>  
        /// 根据GUID获取19位的唯一数字序列  
        /// </summary>  
        /// <returns></returns>  
        public static long GuidToLongID()
        {
            byte[] buffer = Guid.NewGuid().ToByteArray();
            return BitConverter.ToInt64(buffer, 0);
        }
        public static string GetRandomWifiMacAddress()
        {
            var random = new Random();
            var buffer = new byte[6];
            random.NextBytes(buffer);
            buffer[0] = 02;
            var result = string.Concat(buffer.Select(x => string.Format("{0}", x.ToString("X2"))).ToArray());
            return result.ToUpper().Insert(2, "-");
        }
        public static string GetRandomMacAddress()
        {
            var random = new Random();
            var buffer = new byte[6];
            random.NextBytes(buffer);
            var result = String.Concat(buffer.Select(x => string.Format("{0}:", x.ToString("X2"))).ToArray());
            return result.TrimEnd(':');
        }


        public static int GetOS(string userAgent)
        {
            var tmp = userAgent.ToLower();
            if (tmp.Contains("android"))
                return 0;//Android
            else if (tmp.ToLower().Contains("windows phone"))
                return 2;//Windows Phone
            else if (tmp.Contains("iphone") || tmp.Contains("ipad"))
                return 1;//Iphone
            return 3;
        }

        public static void ClearProcesses(string[] processNames, string baseDir = null)
        {
            if (processNames.Count() > 0)
            {
                var Processes = Process.GetProcesses().Where(w => processNames.Contains(w.ProcessName));
                foreach (Process item in Processes)
                {
                    if (!item.HasExited)
                    {
                        try
                        {
                            item.Kill();
                        }
                        catch (Exception ex)
                        {
                            KillProcExec(item.Id);
                            Debug.WriteLine(ex.Message);
                        }

                    }
                }
            }


        }


        public static Process ExecCmd()
        {
            Process p = null;
            try
            {
                p = new Process();
                p.StartInfo.FileName = "cmd.exe";
                p.StartInfo.UseShellExecute = false;        //是否使用操作系统shell启动
                p.StartInfo.RedirectStandardInput = true;   //接受来自调用程序的输入信息
                p.StartInfo.RedirectStandardOutput = true;  //由调用程序获取输出信息
                p.StartInfo.RedirectStandardError = true;   //重定向标准错误输出
                p.StartInfo.CreateNoWindow = true;          //不显示程序窗口
            }
            catch (Exception)
            {
                throw;
            }
            return p;
        }
        public static void KillProcExec(int procId)
        {
            string cmd = string.Format("taskkill /f /t /im {0}", procId); //强制结束指定进程
            Process ps = null;
            try
            {
                ps = ExecCmd();
                ps.Start();
                ps.StandardInput.WriteLine(cmd + "&exit");
            }
            catch
            {

            }
            finally
            {
                ps.Close();
            }
        }


        public static long IpToInt(string ip)
        {
            string[] items = ip.Split('.');
            return long.Parse(items[0]) << 24
                    | long.Parse(items[1]) << 16
                    | long.Parse(items[2]) << 8
                    | long.Parse(items[3]);
        }



        public static void DeleteDownloadDir(string targetDir, string[] extensions)
        {
            if (!Directory.Exists(targetDir))
                return;

            foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    // 判断文件扩展名是否在指定的扩展名数组中
                    if (extensions.Contains(Path.GetExtension(file).ToLower()))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                        Console.WriteLine($"删除文件: {file}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"删除失败: {file} - {ex.Message}");
                }
            }
        }


        public static void DeleteTempDir(string targetDir)
        {
            if (!Directory.Exists(targetDir))
                return;

            Regex numberDirRegex = new Regex(@"^\d+$");
            foreach (var dir in Directory.GetDirectories(targetDir))
            {
                string dirName = Path.GetFileName(dir);
                // 只处理纯数字目录
                if (!numberDirRegex.IsMatch(dirName))
                    continue;
                try
                {
                    // 1. 删除该目录下所有文件
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch { /* 忽略被占用文件 */ }
                    }
                    // 2. 删除该目录下所有子目录（不删除 dir 本身）
                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        try
                        {
                            Directory.Delete(subDir, true);
                        }
                        catch { }
                    }
                    Console.WriteLine($"已清空目录：{dir}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"清空失败：{dir}，原因：{ex.Message}");
                }
            }
        }


        static List<string> GetTopLevelPlaywrightDirs(string rootPath, string prefix)
        {
            var result = new List<string>();
            try
            {
                foreach (var dir in Directory.GetDirectories(rootPath))
                {
                    string dirName = Path.GetFileName(dir);

                    if (dirName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(dir);
                    }
                    else
                    {
                        result.AddRange(GetTopLevelPlaywrightDirs(dir, prefix));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"无法访问目录 {rootPath}: {ex.Message}");
            }
            return result;
        }
        public static void DeletePlaywrightDirs(string tempPath, string prefix = "playwright-")
        {
            if (!Directory.Exists(tempPath))
                return;
            try
            {
                var dirsToDelete = GetTopLevelPlaywrightDirs(tempPath, prefix);
                foreach (var dir in dirsToDelete)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
            }
        }

        public static void DeleteCacheFile(string cachePath)
        {
            if (Directory.Exists(cachePath))
            {
                var dirsToDelete = Directory.GetDirectories(cachePath, "*", SearchOption.TopDirectoryOnly);
                foreach (var dir in dirsToDelete)
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch
                    {

                    }
                }
            }
        }


        public static void DeleteCookieFile(string dirRoot)
        {
            try
            {
                string[] rootDirs = Directory.GetDirectories(dirRoot);
                string[] rootFiles = Directory.GetFiles(dirRoot);
                foreach (string s2 in rootFiles)
                {
                    if (s2.Contains("Cookies"))
                    {
                        File.Delete(s2);
                    }
                }
                foreach (string s1 in rootDirs)
                {
                    DeleteCookieFile(s1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message.ToString());
            }
        }




        public static void ClearAllErrorMsgDialog()
        {
            string[] allTitles = [
                "node.exe - 应用程序错误",
                "WerFault.exe - 应用程序错误",
                "chrome.exe - 应用程序错误",
                "chrome.exe - 系统错误",
            ];
            // 枚举所有窗口
            UnsafeNativeMethods.EnumWindows((hWnd, lParam) =>
            {
                string title = UnsafeNativeMethods.GetWindowTitle(hWnd);
                if (allTitles.Contains(title))
                {
                    UnsafeNativeMethods.SendMessage(hWnd, UnsafeNativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                return true; // 继续枚举下一个窗口
            }, IntPtr.Zero);
        }

        public static void ClearErrorMsgDialog(string title)
        {
            try
            {
                var _wndRes = UnsafeNativeMethods.FindWindowByCaption(IntPtr.Zero, title);
                if (_wndRes != IntPtr.Zero)
                {
                    UnsafeNativeMethods.SendMessage(_wndRes, UnsafeNativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }

        }

        public static void ClearCacheFile()
        {
            #region 删除物理文件
            ////for (int parallelIndex = 1; parallelIndex <= setting.MaximumParallel; parallelIndex++)
            ////{
            ////    try
            ////    {
            ////        Directory.Delete(System.IO.Path.Combine(System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase, "chrome", "User Data", parallelIndex.ToString()), recursive: true);
            ////    }
            ////    catch (Exception ex)
            ////    {
            ////        Console.WriteLine(ex.Message);
            ////    }
            ////    try
            ////    {
            ////        CommonHelper.DeleteCookieFile(System.IO.Path.Combine(System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase, "chrome", "User Data", parallelIndex.ToString()));
            ////    }
            ////    catch (Exception ex)
            ////    {
            ////        Console.WriteLine(ex.Message);
            ////    }
            ////}
            #endregion
        }



        public static void ClearCacheFile(int processIndex)
        {
            #region 删除物理文件

            try
            {
                string cachePath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.SetupInformation.ApplicationBase, "chrome", "User Data", processIndex.ToString());
                if (System.IO.Directory.Exists(cachePath))
                    Directory.Delete(cachePath, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            #endregion
        }

        /// <summary>
        /// 注释: 清除所有Chrome和ChromeDriver进程
        /// </summary>
        public static void KillAllChromeProcess()
        {
            try
            {
                List<Process> list = new List<Process>();
                list.AddRange(Process.GetProcessesByName("chrome").Where(w => w.MainModule.FileName.StartsWith(AppDomain.CurrentDomain.BaseDirectory)));
                foreach (var process in list)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception)
                    {


                    }
                }
            }
            catch (Exception)
            {


            }

        }


        public static void ClearLocalChromeProcesses()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string[] targets = { "chrome.exe","node.exe" };
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
