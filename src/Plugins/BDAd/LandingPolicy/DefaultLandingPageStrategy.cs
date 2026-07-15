using BDAd.Models;
using PlaywrightHumanInput;
using QTP.Common;
using QTP.Plugins;

namespace BDAd.LandingPolicy
{
    /// <summary>
    /// 默认的落地页处理策略
    /// </summary>
    public sealed class DefaultLandingPageStrategy : ILandingPageStrategy
    {
        private readonly BDAdTask _owner;

        public DefaultLandingPageStrategy(BDAdTask owner)
        {
            _owner = owner;
        }

        public bool CanHandle(string url) => true;

        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
            return FlowControl.Continue;
        }
    }

}
