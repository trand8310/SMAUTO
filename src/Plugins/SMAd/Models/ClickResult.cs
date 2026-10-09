using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAd.Models
{
    public enum ClickOutcome { NotDispatched, NoEffect, Navigated, OpenedPage, Downloaded, VerifiedEffect, Failed, Cancelled }

    public sealed class ClickResult
    {
        public bool Attempted { get; private set; }
        public bool Navigated { get; private set; }
        public bool OpenedNewPage { get; private set; }
        public string? Reason { get; private set; }
        public bool Downloaded { get; private set; }
        public bool EffectVerified { get; private set; }
        public bool Succeeded => Navigated || Downloaded || EffectVerified;
        public ClickOutcome Outcome { get; private set; }

        public static ClickResult Fail(string? reason = null, bool attempted = false) => new() { Attempted = attempted, Reason = reason, Outcome = attempted ? ClickOutcome.Failed : ClickOutcome.NotDispatched };
        public static ClickResult Cancelled(bool attempted) => new() { Attempted = attempted, Reason = "Cancelled", Outcome = ClickOutcome.Cancelled };
        public static ClickResult DownloadSuccess() => new() { Attempted = true, Downloaded = true, Outcome = ClickOutcome.Downloaded };
        public static ClickResult VerifiedEffect() => new() { Attempted = true, EffectVerified = true, Outcome = ClickOutcome.VerifiedEffect };
        public static ClickResult NoNavigation(string? reason = null) => new() { Attempted = true, Reason = reason, Outcome = ClickOutcome.NoEffect };
        public static ClickResult SuccessSamePage() => new() { Attempted = true, Navigated = true, Outcome = ClickOutcome.Navigated };
        public static ClickResult SuccessNewPage() => new() { Attempted = true, Navigated = true, OpenedNewPage = true, Outcome = ClickOutcome.OpenedPage };
    }

}
