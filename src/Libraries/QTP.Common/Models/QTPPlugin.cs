using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QTP.Common.Models
{
    public class QTPPlugin
    {
        public string Name { get; set; }
        public string ClassName { get; set; }
        public string FileName { get; set; }
        public Type type { get; set; }
    }
}
