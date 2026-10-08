using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BrowserVisionAgent;

public sealed class VisionAction
{
    /// <summary>
    /// click / scroll / wait / finish
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>
    /// 点击坐标
    /// </summary>
    public float? X { get; set; }

    public float? Y { get; set; }

    /// <summary>
    /// up / down
    /// </summary>
    public string? Direction { get; set; }

    /// <summary>
    /// 滚动距离
    /// </summary>
    public float? Distance { get; set; }

    /// <summary>
    /// 等待时间
    /// </summary>
    public int? Milliseconds { get; set; }

    /// <summary>
    /// AI判断原因，仅用于日志
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// 置信度
    /// </summary>
    public double? Confidence { get; set; }
}

public sealed class ActionHistory
{
    public int Step { get; set; }

    public string Action { get; set; } = "";

    public float? X { get; set; }

    public float? Y { get; set; }

    public string? Direction { get; set; }

    public string? Result { get; set; }
}
