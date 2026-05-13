using SMAd.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAd.LandingPolicy
{
    public interface ILandingPageStrategy
    {
        bool CanHandle(string url);
        Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token);
    }

}
