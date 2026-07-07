
namespace BDAd
{
    using Microsoft.Playwright;

    public static class AdTagHelper
    {
        public static async Task<(ILocator? Anchor, string TagText)> TryGetAdTagAsync(ILocator item)
        {
            if (item == null)
                return (null, "广告");

            try
            {
                var anchors = item.Locator("a[data-url]");
                if (await anchors.CountAsync() <= 0)
                    return (null, "广告");

                var anchor = anchors.First;

                // 按优先顺序，全词匹配
                if (await HasExactTextAsync(item, "汇川广告"))
                    return (anchor, "汇川广告");

                if (await HasExactTextAsync(item, "品牌广告"))
                    return (anchor, "品牌广告");

                if (await HasExactTextAsync(item, "广告"))
                    return (anchor, "广告");

                return (anchor, "广告");
            }
            catch
            {
                return (null, "广告");
            }
        }

        private static async Task<bool> HasExactTextAsync(ILocator root, string text)
        {
            try
            {
                var locator = root.GetByText(text, new() { Exact = true });
                return await locator.CountAsync() > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
