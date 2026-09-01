using QTP.Common;
using QTP.Plugins.Models;

namespace QTP.Plugins.LandingPolicy
{
    public sealed class AiSiteLandingPageStrategy : ILandingPageStrategy
    {
        private readonly Ali1688PCTask _owner;

        public AiSiteLandingPageStrategy(Ali1688PCTask owner)
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
