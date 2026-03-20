using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAd
{
    using Microsoft.Playwright;
    using System.Text.RegularExpressions;

    public static class AdTagHelper
    {
        private static readonly Regex ExactAdTagRegex =
            new(@"^(广告|品牌广告|汇川广告)$", RegexOptions.Compiled);

        public static async Task<(ILocator? Anchor, string TagText)> TryGetAdTagAsync(ILocator item)
        {
            if (item == null)
                return (null, "广告");

            var anchors = item.Locator("a[data-url]:visible");
            int anchorCount = await SafeCountAsync(anchors);
            if (anchorCount <= 0)
                return (null, "广告");

            ILocator? fallbackAnchor = null;
            string? fallbackTagText = null;

            for (int i = 0; i < anchorCount; i++)
            {
                var anchor = anchors.Nth(i);

                // ① 精准匹配：广告 / 品牌广告 / 汇川广告
                var exactTag = anchor.Locator("*:visible").Filter(new()
                {
                    HasTextRegex = ExactAdTagRegex
                });

                if (await SafeCountAsync(exactTag) > 0)
                {
                    var text = await SafeInnerTextAsync(exactTag.First);
                    text = NormalizeText(text);

                    if (!string.IsNullOrWhiteSpace(text))
                        return (anchor, text);
                }

                // ② .cpc-adtext
                var cpcTag = anchor.Locator(".cpc-adtext:visible");
                if (await SafeCountAsync(cpcTag) > 0)
                {
                    var text = await SafeInnerTextAsync(cpcTag.First);
                    text = NormalizeText(text);

                    if (!string.IsNullOrWhiteSpace(text))
                        return (anchor, text);
                }

                // ③ 模糊匹配：内部任意可见子节点包含“广告”，且文本长度 <= 6
                var fuzzyText = await TryFindShortAdTextAsync(anchor);
                if (!string.IsNullOrWhiteSpace(fuzzyText))
                {
                    // 先记一个兜底，继续往后扫也可以；
                    // 如果你想“找到第一个就返回”，这里直接 return 即可
                    fallbackAnchor ??= anchor;
                    fallbackTagText ??= fuzzyText;
                }
            }

            if (fallbackAnchor != null && !string.IsNullOrWhiteSpace(fallbackTagText))
                return (fallbackAnchor, fallbackTagText!);

            return (null, "广告");
        }

        private static async Task<string?> TryFindShortAdTextAsync(ILocator anchor)
        {
            try
            {
                // 先查常见小标签节点
                var innerNodes = anchor.Locator("span:visible, em:visible, i:visible, b:visible, strong:visible, small:visible");

                int innerCount = await SafeCountAsync(innerNodes);
                for (int j = 0; j < innerCount; j++)
                {
                    var node = innerNodes.Nth(j);
                    var text = NormalizeText(await SafeInnerTextAsync(node));

                    if (IsShortAdText(text))
                        return text;
                }

                // 上面没找到，再放宽到所有可见后代节点
                var allVisibleNodes = anchor.Locator("*:visible");
                int allCount = await SafeCountAsync(allVisibleNodes);

                for (int j = 0; j < allCount; j++)
                {
                    var node = allVisibleNodes.Nth(j);
                    var text = NormalizeText(await SafeInnerTextAsync(node));

                    if (IsShortAdText(text))
                        return text;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool IsShortAdText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("广告", StringComparison.Ordinal) && text.Length <= 6;
        }

        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\t", "")
                .Trim();
        }

        private static async Task<int> SafeCountAsync(ILocator locator)
        {
            try
            {
                return await locator.CountAsync();
            }
            catch
            {
                return 0;
            }
        }

        private static async Task<string?> SafeInnerTextAsync(ILocator locator)
        {
            try
            {
                return await locator.InnerTextAsync();
            }
            catch
            {
                return null;
            }
        }
    }
}
