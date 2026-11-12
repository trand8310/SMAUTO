using HumanTouchP3.Input;
using Microsoft.Playwright;
using QTP.Common;
using QTP.Plugins;
using SMAd.Models;
using System.Runtime.Intrinsics.Arm;
namespace SMAd.LandingPolicy
{
    public sealed class AiSiteLandingPageStrategy : ILandingPageStrategy
    {
        private readonly SMAdTask _owner;

        public AiSiteLandingPageStrategy(SMAdTask owner)
        {
            _owner = owner;
        }

        public bool CanHandle(string url) => url.StartsWith("https://aisite.wejianzhan.com", StringComparison.OrdinalIgnoreCase);

        public async Task<FlowControl> HandleAsync(WorkerRunContext ctx, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await Task.Delay(CommonHelper.RandomRange(3500, 5500), token);
            var binding = ctx.ActivePage;
            if (binding == null || binding.Page.IsClosed) return FlowControl.Continue;
            var page = binding.Page;
            var noResult = await FindVisibleAsync(page.GetByText("抱歉，未能匹配到合适的课程"), token);
            if (noResult != null)
            {
                var refreshBtn = await FindVisibleAsync(page.Locator(".no-result-btn").GetByText("刷新"), token);
                if (refreshBtn != null)
                {
                    if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return FlowControl.Continue;
                    var tapped = await SmAdTouch.TapAsync(page, refreshBtn, token);
                    if (tapped) await Task.Delay(2000, token);
                }
                token.ThrowIfCancellationRequested();
                if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return FlowControl.Continue;
                if (await FindVisibleAsync(page.GetByText("抱歉，未能匹配到合适的课程"), token) != null)
                {
                    _owner.LogWriteLine("AI站点: 无结果提示仍可见，跳过后续操作");
                    return FlowControl.Continue;
                }
            }

            var closeResult = await ClosePopupAsync(ctx, token, message => _owner.LogWriteLine(message));
            if (closeResult == PopupCloseResult.NotConfirmed || closeResult == PopupCloseResult.PageChanged)
                return FlowControl.Continue;

            if (closeResult == PopupCloseResult.NotPresent)
            {
                if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return FlowControl.Continue;
                var openBtn = page.Locator(".animate-container svg image");
                var imageCount = await TryCountAsync(ctx, openBtn, token, message => _owner.LogWriteLine(message));
                if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return FlowControl.Continue;
                if (imageCount is > 0)
                {
                    var openClick = await _owner.ClickAndDetectNavigationAsync(ctx, openBtn.Nth(imageCount.Value - 1), token);
                    if (openClick.Navigated) return FlowControl.Continue;
                    if (openClick.Attempted) return FlowControl.Continue;
                    await Task.Delay(CommonHelper.RandomRange(2500, 3500), token);
                }

                if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return FlowControl.Continue;
                openBtn = page.Locator(".welcome-popup-open-button");
                if (await openBtn.CountAsync().WaitAsync(token) > 0 &&
                    new[] { 1, 2, 3, 7, 8, 9 }.Contains(CommonHelper.RandomRange(1, 10)))
                {
                    var clicked = await _owner.ClickAndDetectNavigationAsync(ctx, openBtn.First, token);
                    if (clicked.Navigated || clicked.Attempted) return FlowControl.Continue;
                }
            }


            await _owner.BrowseForAsync(ctx, 5, 8, token);
            if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return FlowControl.Continue;
            var offerItems = page.Locator(".ad-card-title,.ad-card-image,.ad-card-conv-btn");
            if (await offerItems.CountAsync() > 0)
            {
                int count = await offerItems.CountAsync();
                var offer = offerItems.Nth(CommonHelper.RandomRange(0, count));


                var movement = await ctx.human!.MoveToTargetAsync(
                    page,
                    binding.Session,
                    offer,
                    maxSwipes: 10,
                    cancellationToken: token);
                if (!movement.Ready)
                {
                    _owner.LogWriteLine($"AI站点: 课程目标未就绪，跳过点击: {movement.Status}, {movement.Reason}");
                    return FlowControl.Continue;
                }

                await Task.Delay(CommonHelper.RandomRange(1000, 1500), token);
                if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return FlowControl.Continue;
                var click = await _owner.ClickAndDetectNavigationAsync(ctx, offer, token);

                return FlowControl.Continue;
            }

            var jsClick = await _owner.TryRandomViewportClickableClickAsync(ctx, token);
            if (!jsClick.Attempted && !jsClick.Succeeded)
                await _owner.TryRandomLinkClickAsync(ctx, "a,img", token);

            return FlowControl.Continue;
        }

        internal static async Task<Microsoft.Playwright.ILocator?> FindVisibleAsync(
            Microsoft.Playwright.ILocator candidates, CancellationToken token)
        {
            int count = await candidates.CountAsync().WaitAsync(token);
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested();
                var candidate = candidates.Nth(i);
                if (await candidate.IsVisibleAsync().WaitAsync(token)) return candidate;
            }
            return null;
        }

        internal enum PopupCloseResult { NotPresent, Closed, NotConfirmed, PageChanged }

        internal static async Task<PopupCloseResult> ClosePopupAsync(WorkerRunContext ctx, CancellationToken token, Action<string> log)
        {
            token.ThrowIfCancellationRequested();
            var binding = ctx.ActivePage;
            if (binding == null || binding.Page.IsClosed) return PopupCloseResult.PageChanged;
            var page = binding.Page;
            var close = await FindVisibleAsync(page.Locator(
                ".gt-jmy-h5-dialog .close-btn, .close-area .close-icon, .layui-layer .layui-layer-close"), token);
            if (close == null) return PopupCloseResult.NotPresent;
            if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return PopupCloseResult.PageChanged;

            // Prefer verifying the actual dialog; some legacy close controls lack a known container.
            var container = close.Locator("xpath=ancestor::*[contains(concat(' ', normalize-space(@class), ' '), ' gt-jmy-h5-dialog ') or contains(concat(' ', normalize-space(@class), ' '), ' layui-layer ')][1]");
            bool hasContainer = await container.CountAsync().WaitAsync(token) > 0;
            bool dispatched = await SmAdTouch.TapAsync(page, close, token);
            if (!dispatched)
            {
                log("AI站点: 弹窗关闭按钮未派发，停止后续页面操作");
                return PopupCloseResult.NotConfirmed;
            }
            if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return PopupCloseResult.PageChanged;
            try
            {
                await (hasContainer ? container : close).WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3000 }).WaitAsync(token);
                if (page.IsClosed || !ReferenceEquals(ctx.ActivePage, binding)) return PopupCloseResult.PageChanged;
                if (!hasContainer)
                {
                    log("AI站点: 关闭按钮已消失，但未识别弹窗容器，停止后续页面操作");
                    return PopupCloseResult.NotConfirmed;
                }
                log("AI站点: 弹窗容器已关闭");
                return PopupCloseResult.Closed;
            }
            catch (TimeoutException) when (!token.IsCancellationRequested)
            {
                log("AI站点: 关闭已派发但弹窗仍可见，停止后续页面操作");
                return PopupCloseResult.NotConfirmed;
            }
        }

        internal static async Task<int?> TryCountAsync(WorkerRunContext ctx, Microsoft.Playwright.ILocator locator,
            CancellationToken token, Action<string> log)
        {
            int? count = null;
            var binding = ctx.ActivePage;
            log($"AI站点: 查找动画图片开始, executionId={ctx.Config.UniqueId}");
            await SMAd.PageActions.OptionalPageOperation.RunAsync(ctx, "AI站点动画图片查找", async () =>
            {
                count = await locator.CountAsync().WaitAsync(TimeSpan.FromSeconds(5), token);
                log($"AI站点: 动画图片匹配数={count}, executionId={ctx.Config.UniqueId}");
            }, token, message => log($"{message}, executionId={ctx.Config.UniqueId}"));
            return binding != null && ReferenceEquals(ctx.ActivePage, binding) ? count : null;
        }
    }
}
