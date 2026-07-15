using BDAd.Models;
using PlaywrightHumanInput;
using QTP.Common;
using QTP.Plugins;
using System.Text.RegularExpressions;


namespace BDAd.LandingPolicy
{
    /// <summary>
    ///1688落地页处理策略
    /// </summary>
    public sealed class AliLandingPageStrategy : ILandingPageStrategy
    {
        private readonly BDAdTask _owner;

        public AliLandingPageStrategy(BDAdTask owner)
        {
            _owner = owner;
        }

        private static readonly Regex UrlRegex = new Regex(
            @"^https://([a-z0-9-]+\.)*1688\.com/",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public bool CanHandle(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return UrlRegex.IsMatch(url);
        }


        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
            return FlowControl.Continue;
        }
    }

}
