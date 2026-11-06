using System.Text.Json;
using System.Text.Json.Nodes;

namespace PWTest;
internal static class ResultComparison
{
    public static string Describe(string file)
    {
        var root = JsonNode.Parse(File.ReadAllText(file)) ?? throw new InvalidDataException("JSON 为空");
        if ((string?)root["schema"] != "pwtest-touch-v1") throw new InvalidDataException("不是 PWTest 采集文件");
        var ev = root["events"]?.AsArray() ?? throw new InvalidDataException("缺少事件");
        var touches = ev.Where(e => (string?)e?["type"] == "touchmove").ToArray();
        var ends = ev.Where(e => (string?)e?["type"] == "touchend").ToArray();
        var scrolls = ev.Where(e => (string?)e?["type"] == "scroll").ToArray();
        double? afterDistance = null, afterMs = null;
        if (ends.Length > 0)
        {
            double end = (double?)ends[^1]?["at"] ?? 0;
            var before = scrolls.LastOrDefault(e => ((double?)e?["at"] ?? 0) <= end);
            string target = (string?)before?["target"] ?? "document";
            var after = scrolls.Where(e => ((double?)e?["at"] ?? 0) > end && (string?)e?["target"] == target).ToArray();
            string key = target == "inner" ? "innerY" : "windowY";
            if (after.Length > 0 && ends[^1]?[key] != null) { afterDistance = ((double?)after[^1]?[key] ?? 0) - ((double?)ends[^1]?[key] ?? 0); afterMs = ((double?)after[^1]?["at"] ?? 0) - end; }
        }
        var pointer = ev.Where(e => (string?)e?["type"] == "pointermove").ToArray();
        var pressures = pointer.Select(e => (double?)e?["pressure"]).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var trusted = ev.Count(e => (bool?)e?["trusted"] == true);
        var gaps = touches.Zip(touches.Skip(1), (a,b) => ((double?)b?["at"] ?? 0) - ((double?)a?["at"] ?? 0)).Where(x => x >= 0).Order().ToArray();
        var view = root["environment"]?["viewport"];
        return $"文件: {Path.GetFileName(file)}\r\n标签: {root["label"]}\r\n视口: {view?["width"]} × {view?["height"]}; DPR: {root["environment"]?["dpr"]}\r\nTouchMove: {touches.Length}; PointerMove: {pointer.Length}; Scroll: {scrolls.Length}; 丢弃: {root["dropped"]}\r\n记录为 trusted 的事件: {trusted}; 指针压力范围: {(pressures.Length == 0 ? "无" : $"{pressures.Min():F2}–{pressures.Max():F2}")}\r\nTouchMove 间隔中位数: {(gaps.Length == 0 ? "无" : gaps[gaps.Length / 2].ToString("F1") + " ms")}\r\n最后一次抬手后滚动: {(afterDistance?.ToString("F1") ?? "无数据")} px; 末次滚动事件距抬手: {(afterMs?.ToString("F0") ?? "无数据")} ms\r\n说明: 按最后一次 touchend 粗略估计，滚动事件的时间和位置受调度影响；没有抬手后样本时显示无数据。";
    }
}
