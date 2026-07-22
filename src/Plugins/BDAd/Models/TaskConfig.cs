using Newtonsoft.Json.Linq;


namespace BDAd.Models
{
    public sealed class TaskConfig
    {
        public string UniqueId { get; set; } = "";
        public JObject TaskArgs { get; set; } = default!;
        public CancellationTokenSource LinkedCts { get; set; } = default!;
        public int Fingerprint { get; set; }
        public int TaskId { get; set; }
        public string TaskUrl { get; set; } = "";
        public int SleepMs { get; set; }
        public bool IsLocalAdWord { get; set; }
        public int PageLoadingTimeoutMs { get; set; }
        public int PageLoadedDelayMs { get; set; }
        public string UserAgent { get; set; } = "";
        public int Os { get; set; }
        public float DeviceScale { get; set; }
        public int Sw { get; set; }
        public int Sh { get; set; }
        public string WordName { get; set; } = "";
        public bool CleaningWords { get; set; }
        public bool NotTriggerDownload { get; set; }
        public bool PvsTriggerOne { get; set; }
        public int CurrentUV { get; set; }
        public string KernelVersion { get; set; } = "135";
        public int MaxTouchPoints { get; set; }
        public int ProcessIndex { get; set; }
        public bool IsTest { get; set; }
        public int TotalPV { get; set; }
    }
}
