using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAd
{
    public class SMAdHelper
    {

        public static async Task<ILocator?> WaitVisibleLocatorAsync(
        IEnumerable<ILocator> locators,
        CancellationToken token,
        int timeoutMs = 10000,
        int intervalMs = 250)
        {
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                token.ThrowIfCancellationRequested();

                foreach (var locator in locators)
                {
                    try
                    {
                        var first = locator.First;
                        if (await first.CountAsync() > 0 && await first.IsVisibleAsync())
                        {
                            return first;
                        }
                    }
                    catch
                    {
                    }
                }

                await Task.Delay(intervalMs, token);
            }

            return null;
        }
    }
}
