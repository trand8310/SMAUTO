
using System.Diagnostics;
using System.Net;

namespace QTP.Common
{
    public class ProxyTester
    {
        private readonly List<string> _testUrls;
        private readonly TimeSpan _timeout;

        public ProxyTester(IEnumerable<string>? testUrls = null, int timeoutSeconds = 15)
        {
            if (testUrls == null || !testUrls.Any())
            {
                _testUrls = new List<string>
                {
                    "http://211.154.24.179:9000/api/dash/ipinfo.php",
                    "http://117.21.200.221/api/dash/ipinfo.php",
                    "http://ip-api.com/json/?lang=zh-CN",
                    "https://ipinfo.io/json",
                };
            }
            else
            {
                _testUrls = testUrls.ToList();
            }

            _timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        public async Task<ProxyTestResult> TestAsync(string? proxyAddress = null,
            CancellationToken cancellationToken = default, string? protocol = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(proxyAddress))
                proxyAddress = ProxyFailureClassifier.Address(proxyAddress, protocol);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            var tasks = _testUrls
                .Select(url => TryRequestAsync(url, proxyAddress, cts.Token))
                .ToList();
            var failures = new List<string>();

            try
            {
                while (tasks.Count > 0)
                {
                    var finished = await Task.WhenAny(tasks);
                    tasks.Remove(finished);
                    var result = await finished;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (result.IsValid)
                        return result;
                    failures.Add($"{result.SuccessUrl}: {result.ErrorMessage}");
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new ProxyTestResult
                {
                    Proxy = proxyAddress ?? "",
                    IsValid = false,
                    ErrorMessage = "全部测试站点请求失败: " + string.Join(" | ", failures)
                };
            }
            finally
            {
                cts.Cancel();
                await Task.WhenAll(tasks);
            }
        }

        public async Task<List<ProxyTestResult>> TestManyAsync(IEnumerable<string?> proxies, int maxDegreeOfParallelism = 10)
        {
            var results = new List<ProxyTestResult>();
            using var throttler = new SemaphoreSlim(maxDegreeOfParallelism);

            var tasks = proxies.Select(async proxy =>
            {
                await throttler.WaitAsync();
                try
                {
                    var result = await TestAsync(proxy);
                    lock (results)
                    {
                        results.Add(result);
                    }
                }
                finally
                {
                    throttler.Release();
                }
            });

            await Task.WhenAll(tasks);
            return results;
        }

        private async Task<ProxyTestResult> TryRequestAsync(string url, string? proxyAddress, CancellationToken cancellationToken)
        {
            var result = new ProxyTestResult
            {
                Proxy = proxyAddress ?? "",
                SuccessUrl = url
            };

            var sw = Stopwatch.StartNew();

            try
            {
                var handler = new HttpClientHandler
                {
                    UseCookies = false
                };

                if (!string.IsNullOrWhiteSpace(proxyAddress))
                {
                    handler.Proxy = new WebProxy(proxyAddress);
                    handler.UseProxy = true;
                }
                else
                {
                    handler.Proxy = null;
                    handler.UseProxy = false;
                }

                using var client = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };

                using var response = await client.GetAsync(url, cancellationToken);
                result.Data = await response.Content.ReadAsStringAsync(cancellationToken);

                sw.Stop();
                result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                result.StatusCode = response.StatusCode;
                result.IsValid = response.IsSuccessStatusCode;

                if (!result.IsValid)
                    result.ErrorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                result.IsValid = false;
                result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                result.ErrorMessage = "请求已取消或超时";
            }
            catch (Exception ex)
            {
                sw.Stop();
                result.IsValid = false;
                result.ElapsedMilliseconds = sw.ElapsedMilliseconds;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }
    }

    public class ProxyTestResult
    {
        public string Proxy { get; set; } = "";
        public bool IsValid { get; set; }
        public string? SuccessUrl { get; set; }
        public long ElapsedMilliseconds { get; set; }
        public HttpStatusCode? StatusCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Data { get; set; }
    }
}
