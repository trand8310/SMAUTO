using SMAd.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAd.LandingPolicy
{
    public sealed class LandingPageStrategyDispatcher
    {
        private readonly List<ILandingPageStrategy> _strategies;

        public LandingPageStrategyDispatcher(IEnumerable<ILandingPageStrategy> strategies)
        {
            _strategies = strategies.ToList();
        }

        public async Task<FlowControl> DispatchAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();

            var url = ctx.Page?.Url ?? string.Empty;
            foreach (var strategy in _strategies)
            {
                if (strategy.CanHandle(url))
                {
                    var result = FlowControl.Continue;
                    await SMAd.PageActions.OptionalPageOperation.RunAsync(ctx, strategy.GetType().Name,
                        async () => result = await strategy.HandleAsync(ctx, token), token,
                        message => ctx.human.Options.Log?.Invoke(message));
                    return result;
                }
            }

            return FlowControl.Continue;
        }
    }
}
