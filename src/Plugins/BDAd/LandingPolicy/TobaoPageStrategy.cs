using BDAd.Models;
using QTP.Common;
using QTP.Plugins;
using System.Text.RegularExpressions;


namespace BDAd.LandingPolicy
{
    /// <summary>
    ///淘宝落地页处理策略
    /// </summary>
    public sealed class TobaoPageStrategy : ILandingPageStrategy
    {
        private readonly BDAdTask _owner;

        public TobaoPageStrategy(BDAdTask owner)
        {
            _owner = owner;
        }
        public bool CanHandle(string url) => url.StartsWith("https://uland.taobao.com/", StringComparison.OrdinalIgnoreCase);


        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(800, 1200), token);
            return FlowControl.Continue;
        }
    }

}
