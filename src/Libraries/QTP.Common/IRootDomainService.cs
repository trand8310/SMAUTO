using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QTP.Common
{
    public interface IRootDomainService
    {
        Task InitializeAsync();
        bool TryGetRootDomain(string hostOrUrl, out string rootDomain);
    }
}
