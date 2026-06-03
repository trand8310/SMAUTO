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
                    return await strategy.HandleAsync(ctx, token);
            }

            return FlowControl.Continue;
        }
    }
}
