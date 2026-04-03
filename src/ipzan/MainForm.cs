using System;
using System.DirectoryServices.ActiveDirectory;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Text.Json;

namespace ipzan
{
    public partial class MainForm : Form
    {
        /// <summary>
        /// AES-ECB + PKCS7 加密，并输出 hex 字符串
        /// </summary>
        private static string EncryptAesEcbPkcs7ToHex(string plainText, string keyText)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(keyText);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

            using var aes = Aes.Create();
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = keyBytes;

            using ICryptoTransform encryptor = aes.CreateEncryptor();
            byte[] encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return ConvertToHex(encryptedBytes);
        }

        /// <summary>
        /// byte[] 转十六进制字符串
        /// </summary>
        private static string ConvertToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2")); // 小写 hex
            }
            return sb.ToString();
        }



        private static string? _hostCache;
        private static readonly SemaphoreSlim _host_lock = new(1, 1);
        public static async Task<string> GetHostAsync()
        {
            // 快速路径（无锁）
            if (!string.IsNullOrWhiteSpace(_hostCache))
                return _hostCache;
            await _host_lock.WaitAsync();
            try
            {
                // 双重检查
                if (!string.IsNullOrWhiteSpace(_hostCache))
                    return _hostCache;
                // ① 先尝试本机公网 IPv4
                try
                {
                    var localIp = NetUtil
                        .GetPublicIPv4Addresses()
                        .FirstOrDefault();

                    if (!string.IsNullOrWhiteSpace(localIp))
                    {
                        _hostCache = localIp;
                        return _hostCache;
                    }
                }
                catch { }
                // ② 请求外部接口获取公网 IP
                try
                {
                    var realIp = await RealIpHelper.GetRealIpAsync();
                    if (!string.IsNullOrWhiteSpace(realIp))
                    {
                        _hostCache = realIp;
                        return _hostCache;
                    }
                }
                catch { }
                // ③ 最终兜底
                _hostCache = "";
                return _hostCache;
            }
            finally
            {
                _host_lock.Release();
            }
        }


        public MainForm()
        {
            InitializeComponent();
        }

        private async Task<string?> get_whiteList(string no, string userId)
        {
            string url = $"https://service.ipzan.com/whiteList-get?no={no}&userId={userId}";
            using var httpClient = new HttpClient();
            try
            {
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();

            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine("请求失败：" + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("发生异常：" + ex.Message);
            }
            return null;
        }
        public static async Task<string?> DeleteWhiteListAsync(string no, string userId, string ip)
        {
            //https://service.ipzan.com/whiteList-del?no=xxxxx&userId=xxxxx&ip=xxxxx
            string url = $"https://service.ipzan.com/whiteList-del?no={no}&userId={userId}&ip={ip}";
            using var httpClient = new HttpClient();
            try
            {
                var response = await httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();

            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine("请求失败：" + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("发生异常：" + ex.Message);
            }
            return null;
        }



        public static async Task<string> AddWhiteListAsync(string no, string ip, string loginPassword, string packageKey, string signKey)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string data = $"{loginPassword}:{packageKey}:{timestamp}";
            string sign = EncryptAesEcbPkcs7ToHex(data, signKey);
            using var client = new HttpClient();

            var body = new
            {
                no,
                ip,
                sign
            };
            var json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await client.PostAsync("https://service.ipzan.com/whiteList-add", content);
            var result = await resp.Content.ReadAsStringAsync();
            resp.EnsureSuccessStatusCode();
            return result;
        }




        //https://service.ipzan.com/whiteList-add
        private void button1_Click(object sender, EventArgs e)
        {
            
            var userId = textBox2.Text;
            var url = textBox3.Text;
            var ip = textBox1.Text;
            var loginPassword = textBox4.Text;
            var secret = textBox5.Text;
            Task.Run(async () =>
            {
                var dict = UrlHelper.ParseQueryParams(url);
                var no = dict["no"];
                var packageKey = dict["secret"];

                string result = await AddWhiteListAsync(
                                no,
                                ip,
                                loginPassword,
                                packageKey,
                                secret
                            );

                this.BeginInvoke(() =>
                {
                    textBox6.Text = result;
                });



            });
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            Task.Run(async () =>
            {
                var ipaddr = await GetHostAsync();
                this.BeginInvoke(() =>
                {
                    textBox1.Text = ipaddr;

                });

            });
        }

        private void button2_Click(object sender, EventArgs e)
        {
            var userId = textBox2.Text;
            var url = textBox3.Text;
            var ip = textBox1.Text;
            Task.Run(async () =>
            {
                var dict = UrlHelper.ParseQueryParams(url);
                var no = dict["no"];
                var result = await DeleteWhiteListAsync(
                                no,
                                userId,
                                ip);

                this.BeginInvoke(() =>
                {
                    textBox6.Text = result ?? "无";
                });



            });

        }
    }
}
