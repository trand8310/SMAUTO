

using Microsoft.Playwright;
using System.Collections.Concurrent;


namespace PlaywrightHumanInput;
public sealed class HumanBrowserSession : IDisposable
{
    private readonly IBrowserContext _context;
    private readonly HumanBehaviorProfile _profile;
    private readonly int _rootSeed;

    private readonly ConcurrentDictionary<
        IPage,
        Lazy<HumanPageOperator>> _pageOperators = new();

    private int _pageStreamIndex;
    private bool _disposed;

    public HumanBrowserSession(
        IBrowserContext context,
        HumanBehaviorProfile profile,
        int rootSeed)
    {
        _context = context ??
                   throw new ArgumentNullException(
                       nameof(context));

        _profile = profile ??
                   throw new ArgumentNullException(
                       nameof(profile));

        _rootSeed = rootSeed;

        foreach (IPage page in context.Pages)
        {
            RegisterPage(page);
        }

        _context.Page += OnContextPageCreated;
    }

    public HumanPageOperator For(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.IsClosed)
        {
            throw new InvalidOperationException(
                "页面已经关闭。");
        }

        return RegisterPage(page);
    }

    private HumanPageOperator RegisterPage(IPage page)
    {
        Lazy<HumanPageOperator> lazy =
            _pageOperators.GetOrAdd(
                page,
                currentPage =>
                    new Lazy<HumanPageOperator>(
                        () =>
                        {
                            int streamIndex =
                                Interlocked.Increment(
                                    ref _pageStreamIndex);

                            int pageSeed =
                                HumanSeed.Derive(
                                    _rootSeed,
                                    streamIndex);

                            currentPage.Close +=
                                OnPageClosed;

                            return new HumanPageOperator(
                                currentPage,
                                _profile,
                                pageSeed);
                        },
                        LazyThreadSafetyMode
                            .ExecutionAndPublication));

        return lazy.Value;
    }

    private void OnContextPageCreated(
        object? sender,
        IPage page)
    {
        RegisterPage(page);
    }

    private void OnPageClosed(
        object? sender,
        IPage page)
    {
        page.Close -= OnPageClosed;

        _pageOperators.TryRemove(
            page,
            out _);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _context.Page -= OnContextPageCreated;

        foreach (var item in _pageOperators)
        {
            item.Key.Close -= OnPageClosed;
        }

        _pageOperators.Clear();
    }
}