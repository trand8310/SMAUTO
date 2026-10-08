using Microsoft.Playwright;

namespace BrowserVisionAgent;

public sealed class BrowserActionExecutor
{
    private readonly IPage _page;

    public BrowserActionExecutor(IPage page)
    {
        _page = page;
    }

    public async Task ExecuteAsync(
        VisionAction action,
        int pageWidth,
        int pageHeight,
        CancellationToken cancellationToken = default)
    {
        var type =
            action.Action
                .Trim()
                .ToLowerInvariant();

        switch (type)
        {
            case "click":

                await ClickAsync(
                    action,
                    pageWidth,
                    pageHeight);

                break;

            case "scroll":

                await ScrollAsync(
                    action,
                    pageWidth,
                    pageHeight);

                break;

            case "wait":

                await WaitAsync(
                    action,
                    cancellationToken);

                break;

            case "finish":

                break;

            default:

                throw new InvalidOperationException(
                    $"未知AI动作：{action.Action}");
        }
    }

    private async Task ClickAsync(
        VisionAction action,
        int width,
        int height)
    {
        if (action.X == null ||
            action.Y == null)
        {
            throw new InvalidOperationException(
                "AI返回click，但缺少x/y坐标");
        }

        var x = action.X.Value;
        var y = action.Y.Value;

        // 防止AI返回越界坐标
        if (x < 0 ||
            y < 0 ||
            x >= width ||
            y >= height)
        {
            throw new InvalidOperationException(
                $"AI坐标越界：({x},{y})，页面={width}x{height}");
        }

        Console.WriteLine(
            $"点击：({x}, {y})");

        // 先移动，再点击
        await _page.Mouse.MoveAsync(
            x,
            y,
            new MouseMoveOptions
            {
                Steps = Random.Shared.Next(2, 5)
            });

        await Task.Delay(
            Random.Shared.Next(50, 150));

        await _page.Mouse.ClickAsync(
            x,
            y,
            new MouseClickOptions
            {
                Delay = Random.Shared.Next(50, 130)
            });
    }

    private async Task ScrollAsync(
        VisionAction action,
        int width,
        int height)
    {
        var direction =
            action.Direction?
                .Trim()
                .ToLowerInvariant()
            ?? "down";

        var distance =
            Math.Clamp(
                action.Distance ?? 350,
                100,
                height * 0.75f);

        Console.WriteLine(
            $"滑动：{direction}, distance={distance}");

        await SwipeAsync(
            direction,
            distance,
            width,
            height);
    }

    /// <summary>
    /// 模拟手指拖动
    /// </summary>
    private async Task SwipeAsync(
        string direction,
        float distance,
        int width,
        int height)
    {
        var startX =
            width * 0.5f +
            Random.Shared.Next(-20, 21);

        float startY;
        float endY;

        if (direction == "up")
        {
            // 想查看页面上方：
            // 手指向下拖
            startY = height * 0.35f;
            endY = Math.Min(
                height * 0.85f,
                startY + distance);
        }
        else
        {
            // 想查看页面下方：
            // 手指向上拖
            startY = height * 0.75f;
            endY = Math.Max(
                height * 0.20f,
                startY - distance);
        }

        await _page.Mouse.MoveAsync(
            startX,
            startY);

        await Task.Delay(
            Random.Shared.Next(50, 120));

        await _page.Mouse.DownAsync();

        const int steps = 25;

        for (var i = 1; i <= steps; i++)
        {
            var t =
                (float)i / steps;

            // smoothstep
            var smooth =
                t * t * (3f - 2f * t);

            var y =
                startY +
                (endY - startY) * smooth;

            var jitterX =
                Random.Shared.Next(-2, 3);

            await _page.Mouse.MoveAsync(
                startX + jitterX,
                y);

            await Task.Delay(
                Random.Shared.Next(8, 18));
        }

        await _page.Mouse.UpAsync();
    }

    private static async Task WaitAsync(
        VisionAction action,
        CancellationToken cancellationToken)
    {
        var ms = Math.Clamp(
            action.Milliseconds ?? 1000,
            200,
            10000);

        Console.WriteLine(
            $"等待：{ms} ms");

        await Task.Delay(
            ms,
            cancellationToken);
    }
}