using Newtonsoft.Json;
using System.Diagnostics;


namespace Updater
{
    public class ProgressEventArgs : EventArgs
    {
        public double Progress { get; }
        public string Message { get; }
        public ProgressEventArgs(double progress, string message = "")
        {
            Progress = progress;
            Message = message;
        }
    }

    public class FileVersionInfo
    {
        public string File { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Text => Path.GetFileNameWithoutExtension(File).Replace("SMAD_", "");
    }

    public class VersionResponse
    {
        public bool Success { get; set; }
        public string Runtime_Version { get; set; } = string.Empty;
        public List<FileVersionInfo> Data { get; set; } = new();
    }



    public class FileUpdater
    {
        private readonly HttpClient _httpClient;

        public event EventHandler<ProgressEventArgs>? ProgressChanged;
        private void OnProgressChanged(double progress, string message = "")
        {
            ProgressChanged?.Invoke(this, new ProgressEventArgs(progress, message));
        }

        public FileUpdater()
        {
            _httpClient = new HttpClient();
        }
        public async Task<VersionResponse?> GetMainVersionListAsync(string updateUrl, CancellationToken token = default)
        {
            try
            {
                var baseUrl = new Uri(updateUrl).GetLeftPart(UriPartial.Authority);
                var url = $"{baseUrl}/update.php?action=get_main_version&_t={DateTime.Now.Ticks}";
                using var response = await _httpClient.GetAsync(url, token);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(token);
                return JsonConvert.DeserializeObject<VersionResponse>(content);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #region 下载方法
        public string ComputeSha256(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = sha256.ComputeHash(fs);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        public async Task<string> DownloadFileAsync(FileVersionInfo selectedVersion, CancellationToken token = default)
        {
            string destinationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"packages", selectedVersion.File);
            // 1️⃣ 检查本地是否存在
            if (File.Exists(destinationPath))
            {
                string localHash = ComputeSha256(destinationPath);
                if (string.Equals(localHash, selectedVersion.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    OnProgressChanged(100, "文件已存在且校验通过，无需重新下载");
                    return destinationPath;
                }
                else
                {
                    File.Delete(destinationPath);
                    OnProgressChanged(0, "本地文件存在但 hash 不匹配，将重新下载");
                }
            }
            var baseUrl = new Uri(Properties.Updater.Default.Url).GetLeftPart(UriPartial.Authority);
            var downloadUrl = $"{baseUrl}/upload/{selectedVersion.File}";
            string fileName = Path.GetFileName(downloadUrl);
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var canReportProgress = totalBytes != -1;

            using var contentStream = await response.Content.ReadAsStreamAsync(token);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            OnProgressChanged(0, "开始下载...");

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, token);
                totalRead += bytesRead;
                if (canReportProgress)
                {
                    double percent = (totalRead * 1.0 / totalBytes) * 100;
                    OnProgressChanged(percent, $"下载中... {percent:F1}%");
                }
            }

            OnProgressChanged(100, "下载完成");
            return destinationPath;
        }
        #endregion

        #region 更新方法
        private void CopyDirectory(string sourceDir, string destDir, string[]? excludeDirs = null)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(file);
                File.Copy(file, Path.Combine(destDir, fileName), true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dir);
                if (excludeDirs != null && excludeDirs.Any(d => d.Equals(dirName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                CopyDirectory(dir, Path.Combine(destDir, dirName), excludeDirs);
            }
        }



        public void UpdateFromZip(string zipFilePath)
        {
            if (!File.Exists(zipFilePath))
            {
                OnProgressChanged(0, $"更新包不存在: {zipFilePath}");
                return;
            }

            try
            {
                //OnProgressChanged(0, "开始备份当前文件...");

                //string versionBackupDir = Path.Combine(HistoryDirectory, DateTime.Now.ToString("yyyyMMdd_HHmmss"), $"SMAD_v{AppSettings.AppVertion}");
                //Directory.CreateDirectory(versionBackupDir);
                //foreach (var file in Directory.GetFiles(BaseDirectory))
                //{
                //    string fileName = Path.GetFileName(file);
                //    File.Copy(file, Path.Combine(versionBackupDir, fileName), true);
                //}
                //foreach (var dir in Directory.GetDirectories(BaseDirectory))
                //{
                //    string dirName = Path.GetFileName(dir);

                //    // 排除指定顶级目录
                //    if (dirName.Equals("logs", StringComparison.OrdinalIgnoreCase) ||
                //        dirName.Equals("history", StringComparison.OrdinalIgnoreCase) ||
                //        dirName.Equals("temp", StringComparison.OrdinalIgnoreCase))
                //        continue;
                //    string destDir = Path.Combine(versionBackupDir, dirName);
                //    if (dirName.Equals("File", StringComparison.OrdinalIgnoreCase))
                //    {
                //        CopyDirectory(dir, destDir, excludeDirs: new[] { "Cache" });
                //    }
                //    else
                //    {
                //        CopyDirectory(dir, destDir);
                //    }
                //}
                //OnProgressChanged(20, "备份完成");
                //OnUpdateHelper(zipFilePath);
                //// 构造 UpdateHelper 路径
                //string helperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UpdateHelper.exe");
                //// 参数：当前程序路径 + 更新 zip 文件路径
                //string args = $"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName}\" \"{zipFilePath}\"";
                //try
                //{
                //    System.Diagnostics.Process.Start(helperPath, args);
                //    OnProgressChanged(25, "启动更新程序...");

                //    //System.Windows.Forms.Application.Exit(0);
                //}
                //catch (Exception ex)
                //{
                //    OnProgressChanged(0, $"启动更新程序失败: {ex.Message}");
                //}




                //// 清理当前目录
                //OnProgressChanged(30, "清理当前目录...");
                //foreach (var file in Directory.GetFiles(BaseDirectory))
                //{
                //    string fileName = Path.GetFileName(file);
                //    if (fileName.Equals("backup", StringComparison.OrdinalIgnoreCase) ||
                //        fileName.Equals("temp", StringComparison.OrdinalIgnoreCase))
                //        continue;

                //    File.SetAttributes(file, FileAttributes.Normal);
                //    File.Delete(file);
                //}

                //foreach (var dir in Directory.GetDirectories(BaseDirectory))
                //{
                //    string dirName = Path.GetFileName(dir);
                //    if (dirName.Equals("backup", StringComparison.OrdinalIgnoreCase) ||
                //        dirName.Equals("temp", StringComparison.OrdinalIgnoreCase))
                //        continue;

                //    Directory.Delete(dir, true);
                //}

                //OnProgressChanged(50, "清理完成");

                //// 解压 ZIP 文件
                //OnProgressChanged(60, "解压更新包...");
                //ZipFile.ExtractToDirectory(zipFilePath, BaseDirectory);
                //OnProgressChanged(100, "更新完成");

            }
            catch (Exception ex)
            {
                OnProgressChanged(0, $"更新失败: {ex.Message}");
            }
        }
        #endregion

        #region 辅助方法
        public static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }
        #endregion



    }
}
