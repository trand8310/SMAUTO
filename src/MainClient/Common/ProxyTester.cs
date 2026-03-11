using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MainClient.Common
{
    public class ProxyTester
    {
        private readonly List<string> _testUrls;
        private readonly TimeSpan _timeout;

        public ProxyTester(IEnumerable<string> testUrls = null, int timeoutSeconds = 15)
        {
            _testUrls = testUrls?.ToList() ?? new List<string>
            {
                "http://117.21.200.221/api/dash/ipinfo.php",
                "http://ip-api.com/json/?lang=zh-CN",
                "https://ipinfo.io/json",
            };
            _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        /// <summary>
        /// 测试单个代理是否可用（任意一个站点成功即为有效）
        /// </summary>
        public async Task<ProxyTestResult> TestAsync(string proxyAddress)
        {
            var tasks = _testUrls.Select(url => TryRequestAsync(url, proxyAddress)).ToArray();

            while (tasks.Length > 0)
            {
                var finished = await Task.WhenAny(tasks);
                var result = await finished;

                if (result.IsValid)
                    return result;

                tasks = tasks.Where(t => t != finished).ToArray(); // 移除失败的
            }

            return new ProxyTestResult
            {
                Proxy = proxyAddress,
                IsValid = false,
                ErrorMessage = "全部测试站点请求失败"
            };
        }

        /// <summary>
        /// 并行测试多个代理（批量检测）
        /// </summary>
        public async Task<List<ProxyTestResult>> TestManyAsync(IEnumerable<string> proxies, int maxDegreeOfParallelism = 10)
        {
            var results = new List<ProxyTestResult>();
            var throttler = new SemaphoreSlim(maxDegreeOfParallelism);

            var tasks = proxies.Select(async proxy =>
            {
                await throttler.WaitAsync();
                try
                {
                    var result = await TestAsync(proxy);
                    lock (results)
                        results.Add(result);
                }
                finally
                {
                    throttler.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results;
        }

        /// <summary>
        /// 实际执行请求
        /// </summary>
        private async Task<ProxyTestResult> TryRequestAsync(string url, string proxyAddress)
        {
            var result = new ProxyTestResult { Proxy = proxyAddress, SuccessUrl = url };
            var sw = Stopwatch.StartNew();

            try
            {
                var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy(proxyAddress),
                    UseProxy = true,
                    UseCookies = false,
                };

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = _timeout;
                    var response = await client.GetAsync(url);
                    sw.Stop();
                    result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                    result.StatusCode = response.StatusCode;
                    result.IsValid = response.IsSuccessStatusCode;
                    result.Data = await response.Content.ReadAsStringAsync();
                    if (!result.IsValid)
                        result.ErrorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
       

                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.IsValid = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }
    }

    public class ProxyTestResult
    {
        public string Proxy { get; set; } = "";
        public bool IsValid { get; set; }
        public string SuccessUrl { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public HttpStatusCode? StatusCode { get; set; }
        public string ErrorMessage { get; set; }
        public string Data { get; set; }
    }
}