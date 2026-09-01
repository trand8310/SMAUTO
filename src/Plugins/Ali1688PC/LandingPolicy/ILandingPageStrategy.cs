
using QTP.Plugins.Models;

namespace QTP.Plugins.LandingPolicy
{
    public interface ILandingPageStrategy
    {
        bool CanHandle(string url);
        Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token);
    }

}
