namespace PlaywrightHumanInput;

using Microsoft.Playwright;
using System.Collections.Concurrent;

/// <summary>
/// 管理一个 BrowserContext 内的多个页面。
///
/// 每一个 IPage 对应一个独立的 HumanPageOperator，
/// 避免鼠标坐标、行为状态在不同页面之间相互污染。
/// </summary>
public sealed class HumanBrowserSession : IDisposable
{
    private readonly IBrowserContext _context;
    private readonly HumanBehaviorProfile _profile;

    private readonly ConcurrentDictionary<IPage, HumanPageOperator>
        _pageOperators = new();

    private readonly object _activePageLock = new();

    private IPage? _activePage;
    private bool _disposed;

    public HumanBrowserSession(
        IBrowserContext context,
        HumanBehaviorProfile? profile = null)
    {
        _context = context ??
                   throw new ArgumentNullException(nameof(context));

        _profile = profile ??
                   HumanBehaviorProfile.Normal();

        // 注册已经存在的页面。
        foreach (IPage page in context.Pages)
        {
            RegisterPage(page);
        }

        _activePage = context.Pages
            .LastOrDefault(p => !p.IsClosed);

        // 监听以后新创建的页面。
        _context.Page += OnContextPageCreated;
    }

    /// <summary>
    /// 当前激活页面。
    /// </summary>
    public IPage ActivePage
    {
        get
        {
            lock (_activePageLock)
            {
                if (_activePage == null || _activePage.IsClosed)
                {
                    _activePage = FindAvailablePage();
                }

                return _activePage ??
                       throw new InvalidOperationException(
                           "当前 BrowserContext 中没有可用页面。");
            }
        }
    }

    /// <summary>
    /// 当前页面对应的人工操作器。
    /// </summary>
    public HumanPageOperator Current
    {
        get
        {
            return For(ActivePage);
        }
    }

    /// <summary>
    /// 获取指定页面对应的操作器。
    /// 每个页面只会创建一个 HumanPageOperator。
    /// </summary>
    public HumanPageOperator For(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.IsClosed)
        {
            throw new InvalidOperationException(
                "不能为已经关闭的页面获取操作器。");
        }

        return RegisterPage(page);
    }

    /// <summary>
    /// 将指定页面设为当前页面。
    /// </summary>
    public async Task SwitchToAsync(
        IPage page,
        bool bringToFront = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        cancellationToken.ThrowIfCancellationRequested();

        if (page.IsClosed)
        {
            throw new InvalidOperationException(
                "不能切换到已经关闭的页面。");
        }

        RegisterPage(page);

        if (bringToFront)
        {
            await page.BringToFrontAsync();
        }

        lock (_activePageLock)
        {
            _activePage = page;
        }
    }

    /// <summary>
    /// 点击某个元素，并等待它打开新页面，然后自动切换过去。
    /// </summary>
    public async Task<IPage> ClickAndSwitchToPopupAsync(
        ILocator locator,
        int timeoutMilliseconds = 15_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locator);

        IPage sourcePage = ActivePage;
        HumanPageOperator sourceHuman = For(sourcePage);

        // 必须先创建等待任务，再点击。
        Task<IPage> popupTask = sourcePage.WaitForPopupAsync(
            new PageWaitForPopupOptions
            {
                Timeout = timeoutMilliseconds
            });

        await sourceHuman.ClickAsync(
            locator,
            cancellationToken: cancellationToken);

        IPage popup = await popupTask.WaitAsync(cancellationToken);

        RegisterPage(popup);

        await SwitchToAsync(
            popup,
            bringToFront: true,
            cancellationToken);

        await WaitForPageReadyAsync(
            popup,
            cancellationToken);

        return popup;
    }

    /// <summary>
    /// 执行一个动作，并等待 BrowserContext 创建任意新页面。
    ///
    /// 适合某些新页面不是当前页面直接 popup 的场景。
    /// </summary>
    public async Task<IPage> RunAndSwitchToNewPageAsync(
        Func<Task> trigger,
        int timeoutMilliseconds = 15_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var pageCreated =
            new TaskCompletionSource<IPage>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, IPage page)
        {
            pageCreated.TrySetResult(page);
        }

        _context.Page += Handler;

        try
        {
            await trigger();

            Task<IPage> timeoutTask =
                pageCreated.Task.WaitAsync(
                    TimeSpan.FromMilliseconds(timeoutMilliseconds),
                    cancellationToken);

            IPage newPage = await timeoutTask;

            RegisterPage(newPage);

            await SwitchToAsync(
                newPage,
                bringToFront: true,
                cancellationToken);

            await WaitForPageReadyAsync(
                newPage,
                cancellationToken);

            return newPage;
        }
        finally
        {
            _context.Page -= Handler;
        }
    }

    /// <summary>
    /// 切换回指定页面。
    /// </summary>
    public Task SwitchBackAsync(
        IPage page,
        CancellationToken cancellationToken = default)
    {
        return SwitchToAsync(
            page,
            bringToFront: true,
            cancellationToken);
    }

    private HumanPageOperator RegisterPage(IPage page)
    {
        return _pageOperators.GetOrAdd(
            page,
            currentPage =>
            {
                currentPage.Close += OnPageClosed;

                // 每个页面生成不同随机种子，
                // 但共用同一个行为风格 Profile。
                int randomSeed = Random.Shared.Next();

                return new HumanPageOperator(
                    currentPage,
                    _profile,
                    randomSeed);
            });
    }

    private void OnContextPageCreated(
        object? sender,
        IPage page)
    {
        RegisterPage(page);

        // 这里不自动切换。
        // 因为广告页或后台页也可能触发 Context.Page。
        // 应由 ClickAndSwitchToPopupAsync 显式切换。
    }

    private void OnPageClosed(
        object? sender,
        IPage closedPage)
    {
        closedPage.Close -= OnPageClosed;

        _pageOperators.TryRemove(
            closedPage,
            out _);

        lock (_activePageLock)
        {
            if (ReferenceEquals(_activePage, closedPage))
            {
                _activePage = FindAvailablePage();
            }
        }
    }

    private IPage? FindAvailablePage()
    {
        return _context.Pages
            .LastOrDefault(page => !page.IsClosed);
    }

    private static async Task WaitForPageReadyAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions
                {
                    Timeout = 15_000
                });
        }
        catch (TimeoutException)
        {
            // 某些页面存在长连接或持续加载，
            // DOM 已经可以使用时不必终止整个流程。
        }

        await Task.Delay(
            Random.Shared.Next(300, 900),
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _context.Page -= OnContextPageCreated;

        foreach (IPage page in _pageOperators.Keys)
        {
            page.Close -= OnPageClosed;
        }

        _pageOperators.Clear();
    }
}
