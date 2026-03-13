

using System;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Policy;
using System.Xml.Linq;

namespace Updater
{
    public partial class MainForm : Form
    {

        private readonly string PackagesDirectory;
        private readonly FileUpdater _fileUpdater;

        public MainForm()
        {
            InitializeComponent();
            _fileUpdater = new FileUpdater();
            PackagesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages");
            Directory.CreateDirectory(PackagesDirectory);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {



            var updateUrl = Properties.Updater.Default.Url;
            Task.Run(async () =>
            {
                var versionList = await _fileUpdater.GetMainVersionListAsync(updateUrl);
                if (versionList != null && versionList.Success)
                {
                    var _fileList = versionList.Data;

                    this.BeginInvoke(() =>
                    {
                        comboBox_VersionList.DataSource = _fileList;
                        comboBox_VersionList.DisplayMember = "Text";
                        comboBox_VersionList.ValueMember = "File";
                        if (_fileList.Count > 0)
                            comboBox_VersionList.SelectedIndex = 0;
                    });
                }
            });


            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Length > 0)
            {
                string zipPath = args[0];
                MessageBox.Show(zipPath);
            }

        }




        private void btnUpdate_Click(object sender, EventArgs e)
        {
            Properties.Updater.Default.Url = textBox_ApiUrl.Text;
            Properties.Updater.Default.Save();
            if (comboBox_VersionList.SelectedItem == null || !(comboBox_VersionList.SelectedItem is FileVersionInfo))
            {
                MessageBox.Show("请先选择要更新的版本！");
                return;
            }
            var selectedFile = comboBox_VersionList.SelectedItem as FileVersionInfo;
            if (selectedFile == null)
            {
                MessageBox.Show("请先选择要更新的版本！");
                return;
            }

            btnUpdate.Enabled = false;
            progressBar1.AutoSize = false;
            progressBar1.Width = 300;
            progressBar1.Visible = true;
            Task.Run(async () =>
            {
                double _lastReportedProgress = 0;
                double MinProgressStep = 1;
                DateTime _lastProgressUpdate = System.DateTime.Now;
                double ProgressUpdateIntervalMs = 1000;
                EventHandler<ProgressEventArgs> handler = (s, e) =>
                {

                    bool isProgressTooSmall = Math.Abs(e.Progress - _lastReportedProgress) < MinProgressStep;
                    bool isTooSoon = (DateTime.Now - _lastProgressUpdate).TotalMilliseconds < ProgressUpdateIntervalMs;
                    bool notFinished = e.Progress < 100;
                    if (isProgressTooSmall && isTooSoon && notFinished)
                    {
                        return;
                    }
                    _lastReportedProgress = e.Progress;
                    _lastProgressUpdate = DateTime.Now;
                    this.InvokeOnUiThreadIfRequired(() =>
                    {
                        progressBar1.Value = (int)Math.Min(Math.Max(e.Progress, 0), 100);
                    });
                };
                _fileUpdater.ProgressChanged -= handler;
                _fileUpdater.ProgressChanged += handler;
                var zipFilePath = await _fileUpdater.DownloadFileAsync(selectedFile);
                //var localHash = _fileUpdater.ComputeSha256(zipFilePath);
                //if (!string.Equals(localHash, selectedFile.Hash, StringComparison.OrdinalIgnoreCase))
                //{
                //    throw new Exception("文件校验失败");
                //}

                //var version = Path.GetFileNameWithoutExtension(file).Replace("SMAD_", "");
                var versionDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{selectedFile.Text}");

                var appDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
                if (!System.IO.Directory.Exists(appDir))
                    System.IO.Directory.CreateDirectory(appDir);
                ZipFile.ExtractToDirectory(zipFilePath, appDir, true);
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
                }

                this.InvokeOnUiThreadIfRequired(() =>
                {

                    btnUpdate.Enabled = true;
                    progressBar1.Width = 60;
                    progressBar1.Visible = false;
                });
            });



        }

        private void textBox_ApiUrl_Leave(object sender, EventArgs e)
        {
            var old_url = Properties.Updater.Default.Url;
            Properties.Updater.Default.Url = textBox_ApiUrl.Text;
            Properties.Updater.Default.Save();
            if (!string.IsNullOrWhiteSpace(textBox_ApiUrl.Text) && !textBox_ApiUrl.Text.Equals(old_url))
            {
                var updateUrl = Properties.Updater.Default.Url;
                Task.Run(async () =>
                {
                    var list = await _fileUpdater.GetMainVersionListAsync(updateUrl);
                });

            }



        }
    }
}
