using PlaywrightHumanInput;
using QTP.Common;
using QTP.Plugins;
using BDAd.Models;

namespace BDAd.LandingPolicy
{
    public sealed class AiSiteLandingPageStrategy : ILandingPageStrategy
    {
        private readonly BDAdTask _owner;

        public AiSiteLandingPageStrategy(BDAdTask owner)
        {
            _owner = owner;
        }

        public bool CanHandle(string url) => url.StartsWith("https://aisite.wejianzhan.com", StringComparison.OrdinalIgnoreCase);

        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(3000, 5000), token);
            return FlowControl.Continue;
        }
    }
}
