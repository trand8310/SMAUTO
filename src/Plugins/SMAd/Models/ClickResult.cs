using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAd.Models
{

    public sealed class ClickResult
    {
        public bool Attempted { get; private set; }
        public bool Navigated { get; private set; }
        public bool OpenedNewPage { get; private set; }

        public static ClickResult Fail() => new() { Attempted = false, Navigated = false, OpenedNewPage = false };
        public static ClickResult NoNavigation() => new() { Attempted = true, Navigated = false, OpenedNewPage = false };
        public static ClickResult SuccessSamePage() => new() { Attempted = true, Navigated = true, OpenedNewPage = false };
        public static ClickResult SuccessNewPage() => new() { Attempted = true, Navigated = true, OpenedNewPage = true };
    }

}
