using System.Net.Http.Json;
using System.Text.Json;

namespace BrowserVisionAgent;

public sealed class QwenVisionClient
{
    private readonly HttpClient _httpClient;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public QwenVisionClient(string baseUrl)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    public async Task<VisionAction> DecideAsync(
        byte[] screenshot,
        int width,
        int height,
        string goal,
        IReadOnlyList<ActionHistory>? history = null,
        CancellationToken cancellationToken = default)
    {
        var base64 = Convert.ToBase64String(screenshot);

        var historyText = BuildHistory(history);

        var systemPrompt = $$"""
        你是一个浏览器视觉操作 Agent。

        你将收到当前 Chromium 页面的真实截图。

        你的任务：
        根据当前截图、任务目标和最近操作历史，
        判断下一步最合适的浏览器操作。

        当前截图尺寸：

        width={{width}}
        height={{height}}

        坐标规则：

        左上角为 (0,0)
        x 向右增加
        y 向下增加

        click 返回的 x/y 必须严格使用当前截图中的像素坐标。

        你只能选择以下动作：

        1. click

        {
          "action":"click",
          "x":123,
          "y":456,
          "reason":"原因",
          "confidence":0.95
        }

        2. scroll

        {
          "action":"scroll",
          "direction":"down",
          "distance":350,
          "reason":"原因",
          "confidence":0.95
        }

        direction 只能是：

        up
        down

        3. wait

        {
          "action":"wait",
          "milliseconds":1000,
          "reason":"页面正在加载或等待回复",
          "confidence":0.95
        }

        4. finish

        {
          "action":"finish",
          "reason":"目标已经完成",
          "confidence":0.95
        }

        操作规则：

        - 优先推进当前任务。
        - 页面存在明显下一步按钮或选项时使用 click。
        - 如果目标元素当前不可见，使用 scroll。
        - 页面正在加载、等待回复或状态正在变化时使用 wait。
        - 不要重复点击已经完成的选项。
        - 不要点击广告、无关链接和无关导航。
        - 不要随意返回上一页。
        - 不确定时优先 wait，而不是随机点击。
        - 点击尽量选择目标控件中心区域。
        - 不允许返回 JavaScript。
        - 不允许返回 CSS Selector。
        - 不允许返回 Markdown。
        - 不允许返回 ```json。
        - 只允许返回一个 JSON 对象。
        """;

        var userPrompt = $$"""
        当前任务目标：

        {{goal}}

        最近操作历史：

        {{historyText}}

        请查看当前截图，
        判断下一步操作。
        """;

        var request = new
        {
            model = "qwen3-vl",

            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = systemPrompt
                },

                new
                {
                    role = "user",

                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = userPrompt
                        },

                        new
                        {
                            type = "image_url",

                            image_url = new
                            {
                                url =
                                    $"data:image/png;base64,{base64}"
                            }
                        }
                    }
                }
            },

            temperature = 0,

            max_tokens = 128
        };

        using var response =
            await _httpClient.PostAsJsonAsync(
                "v1/chat/completions",
                request,
                cancellationToken);

        var responseText =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Qwen API 请求失败：{response.StatusCode}\n{responseText}");
        }

        using var document =
            JsonDocument.Parse(responseText);

        var content = document
            .RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException(
                "Qwen3-VL 没有返回内容");
        }

        Console.WriteLine();
        Console.WriteLine("===== AI 原始返回 =====");
        Console.WriteLine(content);
        Console.WriteLine("=======================");

        content = ExtractJson(content);

        var action =
            JsonSerializer.Deserialize<VisionAction>(
                content,
                _jsonOptions);

        if (action == null)
        {
            throw new InvalidOperationException(
                $"无法解析 AI 返回：{content}");
        }

        return action;
    }

    private static string BuildHistory(
        IReadOnlyList<ActionHistory>? history)
    {
        if (history == null || history.Count == 0)
            return "无";

        // 只发送最近5步，防止上下文不断增加
        var items = history
            .TakeLast(5)
            .Select(x =>
                $"Step={x.Step}, Action={x.Action}, " +
                $"X={x.X}, Y={x.Y}, " +
                $"Direction={x.Direction}, " +
                $"Result={x.Result}");

        return string.Join("\n", items);
    }

    private static string ExtractJson(string text)
    {
        text = text.Trim();

        if (text.StartsWith("```"))
        {
            var index = text.IndexOf('\n');

            if (index >= 0)
            {
                text = text[(index + 1)..];
            }

            if (text.EndsWith("```"))
            {
                text = text[..^3];
            }
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start >= 0 &&
            end > start)
        {
            return text[start..(end + 1)];
        }

        return text;
    }
}