

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Playwright;

namespace BrowserVisionAgent;
public sealed class AutoVisionAgent
{
    private readonly IPage _page;
    private readonly HttpClient _http;

    private readonly List<string> _history = new();

    private string? _lastScreenshotHash;
    private AiAction? _lastAction;

    public AutoVisionAgent(
        IPage page,
        string aiBaseUrl)
    {
        _page = page;

        _http = new HttpClient
        {
            BaseAddress = new Uri(
                aiBaseUrl.TrimEnd('/') + "/"),

            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    /// <summary>
    /// 只传运行时间。
    /// AI自动看页面、等待、点击、滑动、回复。
    /// </summary>
    public async Task RunAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var endAt = DateTime.UtcNow + duration;

        Console.WriteLine(
            $"AI Agent开始运行，持续：{duration}");

        while (DateTime.UtcNow < endAt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // 1. 等待页面变化/稳定
                var changed =
                    await WaitForMeaningfulChangeAsync(
                        maxWait: TimeSpan.FromSeconds(8),
                        cancellationToken);

                if (!changed)
                {
                    Console.WriteLine(
                        "页面没有明显变化，继续等待...");

                    continue;
                }

                // 2. 页面尺寸
                var (width, height) =
                    await GetViewportSizeAsync();

                // 3. 正式截图给AI
                var screenshot =
                    await TakeScreenshotAsync();

                // 4. AI决策
                var action =
                    await DecideAsync(
                        screenshot,
                        width,
                        height,
                        cancellationToken);

                Console.WriteLine(
                    $"AI Action={action.Action}");

                // 防止连续重复动作
                if (IsRepeatedAction(action))
                {
                    Console.WriteLine(
                        "检测到重复动作，暂不执行，等待页面变化。");

                    await Task.Delay(
                        1200,
                        cancellationToken);

                    continue;
                }

                // 5. 执行
                await ExecuteAsync(
                    action,
                    width,
                    height,
                    cancellationToken);

                // 6. 记录
                AddHistory(action);

                _lastAction = action;

                if (string.Equals(
                        action.Action,
                        "finish",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(
                        "AI判断当前流程完成。");

                    break;
                }

                // 操作后给页面一点反应时间
                await Task.Delay(
                    Random.Shared.Next(500, 1000),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Agent异常：{ex.Message}");

                await Task.Delay(
                    1500,
                    cancellationToken);
            }
        }

        Console.WriteLine("AI Agent结束。");
    }

    /// <summary>
    /// 页面变化检测：
    /// 没变化就不调用AI。
    /// </summary>
    private async Task<bool> WaitForMeaningfulChangeAsync(
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        var start =
            DateTime.UtcNow;

        while (DateTime.UtcNow - start < maxWait)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var screenshot =
                await _page.ScreenshotAsync(
                    new PageScreenshotOptions
                    {
                        FullPage = false,
                        Type = ScreenshotType.Jpeg,

                        // 用于变化检测，没必要太高质量
                        Quality = 35,

                        Scale = ScreenshotScale.Css
                    });

            var hash =
                ComputeHash(screenshot);

            // 第一次必然认为发生变化
            if (_lastScreenshotHash == null)
            {
                _lastScreenshotHash = hash;
                return true;
            }

            if (!string.Equals(
                    hash,
                    _lastScreenshotHash,
                    StringComparison.Ordinal))
            {
                _lastScreenshotHash = hash;

                // 页面刚变化，稍微等一下避免截到动画中间状态
                await Task.Delay(
                    500,
                    cancellationToken);

                return true;
            }

            await Task.Delay(
                700,
                cancellationToken);
        }

        return false;
    }

    private async Task<byte[]> TakeScreenshotAsync()
    {
        return await _page.ScreenshotAsync(
            new PageScreenshotOptions
            {
                FullPage = false,
                Type = ScreenshotType.Png,

                // 保证 AI 坐标与 Playwright CSS 坐标一致
                Scale = ScreenshotScale.Css
            });
    }

    private async Task<(int Width, int Height)>
        GetViewportSizeAsync()
    {
        var width =
            await _page.EvaluateAsync<int>(
                "() => window.innerWidth");

        var height =
            await _page.EvaluateAsync<int>(
                "() => window.innerHeight");

        return (width, height);
    }

    private async Task<AiAction> DecideAsync(
        byte[] screenshot,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var base64 =
            Convert.ToBase64String(screenshot);

        var historyText =
            _history.Count == 0
                ? "无"
                : string.Join(
                    "\n",
                    _history.TakeLast(5));

        var prompt = $$"""
    你是一个通用网页视觉操作Agent。

    当前页面截图大小：
    width={{width}}
    height={{height}}

    最近操作历史：
    {{historyText}}

    你需要根据当前页面上下文自主决定下一步动作。

    允许动作：

    click
    {
      "action":"click",
      "x":100,
      "y":200
    }

    reply
    {
      "action":"reply",
      "inputX":100,
      "inputY":700,
      "sendX":350,
      "sendY":700,
      "text":"需要发送的内容"
    }

    scroll
    {
      "action":"scroll",
      "direction":"down",
      "distance":300
    }

    wait
    {
      "action":"wait",
      "milliseconds":1500
    }

    finish
    {
      "action":"finish"
    }

    规则：

    - 根据当前页面上下文自然应答。
    - 页面提出问题且存在输入框时，优先reply。
    - 存在明确按钮/选项时，使用click。
    - 当前屏幕看不到下一步时，使用scroll。
    - 页面正在加载或等待对方回复时，使用wait。
    - 不要重复已经执行成功的动作。
    - 不要点击无关导航、广告或明显无关内容。
    - reply文字要结合页面上下文生成。
    - 点击坐标使用截图像素坐标。
    - 只返回JSON。
    - 不要Markdown。
    - 不要解释。
    """;

        var request = new
        {
            model = "qwen3-vl",

            messages = new object[]
            {
            new
            {
                role = "system",
                content = prompt
            },

            new
            {
                role = "user",

                content = new object[]
                {
                    new
                    {
                        type = "text",
                        text =
                            "请查看当前页面，并决定下一步操作。"
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
            await _http.PostAsJsonAsync(
                "v1/chat/completions",
                request,
                cancellationToken);

        var raw =
            await response.Content
                .ReadAsStringAsync(
                    cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception(
                $"AI请求失败：{raw}");
        }

        using var doc =
            JsonDocument.Parse(raw);

        var content =
            doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new Exception(
                "AI没有返回内容");
        }

        content =
            ExtractJson(content);

        return JsonSerializer.Deserialize<AiAction>(
                   content,
                   new JsonSerializerOptions
                   {
                       PropertyNameCaseInsensitive = true
                   })
               ?? throw new Exception(
                   $"AI返回JSON解析失败：{content}");
    }

    private async Task ExecuteAsync(
        AiAction action,
        int width,
        int height,
        CancellationToken cancellationToken)
    {
        var type =
            action.Action?
                .Trim()
                .ToLowerInvariant();

        switch (type)
        {
            case "click":
                {
                    if (!action.X.HasValue ||
                        !action.Y.HasValue)
                    {
                        throw new Exception(
                            "click缺少坐标");
                    }

                    ValidatePoint(
                        action.X.Value,
                        action.Y.Value,
                        width,
                        height);

                    await _page.Mouse.MoveAsync(
                        action.X.Value,
                        action.Y.Value,
                        new MouseMoveOptions
                        {
                            Steps = Random.Shared.Next(2, 5)
                        });

                    await Task.Delay(
                        Random.Shared.Next(50, 120),
                        cancellationToken);

                    await _page.Mouse.ClickAsync(
                        action.X.Value,
                        action.Y.Value);

                    break;
                }

            case "reply":
                {
                    if (!action.InputX.HasValue ||
                        !action.InputY.HasValue ||
                        string.IsNullOrWhiteSpace(action.Text))
                    {
                        throw new Exception(
                            "reply参数不完整");
                    }

                    ValidatePoint(
                        action.InputX.Value,
                        action.InputY.Value,
                        width,
                        height);

                    await _page.Mouse.ClickAsync(
                        action.InputX.Value,
                        action.InputY.Value);

                    await Task.Delay(
                        100,
                        cancellationToken);

                    await _page.Keyboard.PressAsync(
                        "Control+A");

                    await _page.Keyboard.PressAsync(
                        "Backspace");

                    await _page.Keyboard.TypeAsync(
                        action.Text,
                        new KeyboardTypeOptions
                        {
                            Delay =
                                Random.Shared.Next(20, 50)
                        });

                    await Task.Delay(
                        Random.Shared.Next(200, 500),
                        cancellationToken);

                    if (action.SendX.HasValue &&
                        action.SendY.HasValue)
                    {
                        ValidatePoint(
                            action.SendX.Value,
                            action.SendY.Value,
                            width,
                            height);

                        await _page.Mouse.ClickAsync(
                            action.SendX.Value,
                            action.SendY.Value);
                    }
                    else
                    {
                        await _page.Keyboard.PressAsync(
                            "Enter");
                    }

                    break;
                }

            case "scroll":
                {
                    var distance =
                        Math.Clamp(
                            action.Distance ?? 300,
                            100,
                            600);

                    var dy =
                        string.Equals(
                            action.Direction,
                            "up",
                            StringComparison.OrdinalIgnoreCase)
                        ? -distance
                        : distance;

                    await _page.Mouse.WheelAsync(
                        0,
                        dy);

                    break;
                }

            case "wait":
                {
                    var ms =
                        Math.Clamp(
                            action.Milliseconds ?? 1500,
                            300,
                            10000);

                    await Task.Delay(
                        ms,
                        cancellationToken);

                    break;
                }

            case "finish":
                break;

            default:
                throw new Exception(
                    $"未知动作：{action.Action}");
        }
    }

    /// <summary>
    /// 防止模型连续重复执行相同操作。
    /// </summary>
    private bool IsRepeatedAction(
        AiAction action)
    {
        if (_lastAction == null)
            return false;

        if (!string.Equals(
                action.Action,
                _lastAction.Action,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (action.Action.Equals(
                "click",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!action.X.HasValue ||
                !action.Y.HasValue ||
                !_lastAction.X.HasValue ||
                !_lastAction.Y.HasValue)
            {
                return false;
            }

            // 坐标很接近也视为重复点击
            return
                Math.Abs(
                    action.X.Value -
                    _lastAction.X.Value) < 8
                &&
                Math.Abs(
                    action.Y.Value -
                    _lastAction.Y.Value) < 8;
        }

        if (action.Action.Equals(
                "reply",
                StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(
                action.Text,
                _lastAction.Text,
                StringComparison.Ordinal);
        }

        return false;
    }

    private void AddHistory(
        AiAction action)
    {
        var item =
            action.Action.ToLowerInvariant() switch
            {
                "click" =>
                    $"点击 ({action.X},{action.Y})",

                "reply" =>
                    $"回复：{action.Text}",

                "scroll" =>
                    $"滚动：{action.Direction} {action.Distance}",

                "wait" =>
                    "等待",

                "finish" =>
                    "完成",

                _ =>
                    action.Action
            };

        _history.Add(item);

        if (_history.Count > 10)
        {
            _history.RemoveAt(0);
        }
    }

    private static string ComputeHash(
        byte[] data)
    {
        return Convert.ToHexString(
            SHA256.HashData(data));
    }

    private static void ValidatePoint(
        float x,
        float y,
        int width,
        int height)
    {
        if (x < 0 ||
            y < 0 ||
            x >= width ||
            y >= height)
        {
            throw new Exception(
                $"坐标越界：({x},{y})，" +
                $"Viewport={width}x{height}");
        }
    }

    private static string ExtractJson(
        string text)
    {
        text = text.Trim();

        var start =
            text.IndexOf('{');

        var end =
            text.LastIndexOf('}');

        if (start >= 0 &&
            end > start)
        {
            return text[start..(end + 1)];
        }

        return text;
    }
}

public sealed class AiAction
{
    public string Action { get; set; } = "";

    // click
    public float? X { get; set; }

    public float? Y { get; set; }

    // reply
    public float? InputX { get; set; }

    public float? InputY { get; set; }

    public float? SendX { get; set; }

    public float? SendY { get; set; }

    public string? Text { get; set; }

    // scroll
    public string? Direction { get; set; }

    public float? Distance { get; set; }

    // wait
    public int? Milliseconds { get; set; }
}
