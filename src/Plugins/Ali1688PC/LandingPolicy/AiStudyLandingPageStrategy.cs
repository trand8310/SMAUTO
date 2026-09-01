using QTP.Common;
using QTP.Plugins.Models;

namespace QTP.Plugins.LandingPolicy
{
    public sealed class AiStudyLandingPageStrategy : ILandingPageStrategy
    {
        private readonly Ali1688PCTask _owner;

        public AiStudyLandingPageStrategy(Ali1688PCTask owner)
        {
            _owner = owner;
        }

        public bool CanHandle(string url) => url.StartsWith("https://aistudy.baidu.com/", StringComparison.OrdinalIgnoreCase);

        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
            return FlowControl.Continue;
        }
    }
}
