using BDAd.Models;


namespace BDAd.LandingPolicy
{
    public interface ILandingPageStrategy
    {
        bool CanHandle(string url);
        Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token);
    }

}
