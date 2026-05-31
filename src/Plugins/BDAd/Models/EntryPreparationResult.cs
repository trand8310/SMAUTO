using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BDAd.Models
{
    public sealed class EntryPreparationResult
    {
        public bool Success { get; set; }
        public bool EndTask { get; set; }
        public bool IsHomepageTrigger { get; set; }
        public string? QueryWord { get; set; }
        public string? FirstPageUrl { get; set; }
    }
}
