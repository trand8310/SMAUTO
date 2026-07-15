using PlaywrightHumanInput;
using QTP.Common;
using QTP.Plugins;
using BDAd.Models;

namespace BDAd.LandingPolicy
{
    public sealed class UMobLandingPageStrategy : ILandingPageStrategy
    {
        private readonly BDAdTask _owner;

        public UMobLandingPageStrategy(BDAdTask owner)
        {
            _owner = owner;
        }

        public bool CanHandle(string url) => url.StartsWith("https://site.u-mob.cn/", StringComparison.OrdinalIgnoreCase);

        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
            return FlowControl.Continue;
        }
    }

}
