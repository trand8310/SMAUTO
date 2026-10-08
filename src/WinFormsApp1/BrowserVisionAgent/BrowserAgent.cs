using Microsoft.Playwright;

namespace BrowserVisionAgent;

public sealed class BrowserAgent
{
    private readonly IPage _page;

    private readonly QwenVisionClient _ai;

    private readonly BrowserActionExecutor _executor;

    private readonly List<ActionHistory> _history = [];

    public BrowserAgent(
        IPage page,
        QwenVisionClient ai)
    {
        _page = page;
        _ai = ai;

        _executor =
            new BrowserActionExecutor(page);
    }

    public async Task RunAsync(
        string goal,
        int maxSteps = 100,
        CancellationToken cancellationToken = default)
    {
        for (var step = 1;
             step <= maxSteps;
             step++)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Console.WriteLine();
            Console.WriteLine(
                $"================ STEP {step} ================");

            // 页面CSS尺寸
            var size =
                await GetViewportSizeAsync();

            Console.WriteLine(
                $"页面尺寸：{size.Width} x {size.Height}");

            /*
             * Scale = Css 非常重要。
             *
             * AI看到的图片像素坐标
             * 和 Playwright Mouse.ClickAsync()
             * 使用的CSS坐标保持 1:1。
             */
            var screenshot =
                await _page.ScreenshotAsync(
                    new PageScreenshotOptions
                    {
                        Type = ScreenshotType.Png,

                        FullPage = false,

                        Scale =
                            ScreenshotScale.Css
                    });

            // 调AI
            var action =
                await _ai.DecideAsync(
                    screenshot,
                    size.Width,
                    size.Height,
                    goal,
                    _history,
                    cancellationToken);

            Console.WriteLine(
                $"AI动作：{action.Action}");

            Console.WriteLine(
                $"原因：{action.Reason}");

            Console.WriteLine(
                $"置信度：{action.Confidence}");

            if (string.Equals(
                    action.Action,
                    "finish",
                    StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    "AI判断任务已经完成");

                break;
            }

            // 执行动作
            await _executor.ExecuteAsync(
                action,
                size.Width,
                size.Height,
                cancellationToken);

            _history.Add(
                new ActionHistory
                {
                    Step = step,

                    Action = action.Action,

                    X = action.X,

                    Y = action.Y,

                    Direction =
                        action.Direction,

                    Result =
                        action.Reason
                });

            // 操作后稍微等待页面变化
            await Task.Delay(
                Random.Shared.Next(
                    600,
                    1100),
                cancellationToken);
        }
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
}