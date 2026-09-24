using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SMAd
{
    using System;
    using System.Text;

    public sealed class BrowserUiMetrics
    {
        public int StatusBarHeight { get; set; }
        public int NavigationBarHeight { get; set; }
        public int BrowserToolbarHeight { get; set; }

        public override string ToString()
        {
            return $"StatusBarHeight={StatusBarHeight}, " +
                   $"NavigationBarHeight={NavigationBarHeight}, " +
                   $"BrowserToolbarHeight={BrowserToolbarHeight}";
        }
    }

    public static class AndroidBrowserUiMatcher
    {
        // 加权池：
        // 24 -> 40%
        // 28 -> 40%
        // 32 -> 20%
        private static readonly int[] StatusBarHeightPool =
        {
            24, 24, 24, 24,
            28, 28, 28, 28,
            32, 32
        };

        // 24 -> 40%
        // 28 -> 30%
        // 32 -> 20%
        // 48 -> 10%
        private static readonly int[] NavigationBarHeightPool =
        {
            24, 24, 24, 24,
            28, 28, 28,
            32, 32,
            48
        };

        // 48 -> 20%
        // 52 -> 20%
        // 56 -> 60%
        private static readonly int[] BrowserToolbarHeightPool =
        {
            48, 48,
            52, 52,
            56, 56, 56, 56, 56, 56
        };

        public static BrowserUiMetrics Match(
            int width,
            int height,
            string? ua)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "width 和 height 必须大于 0");

            int physicalWidth = Math.Min(width, height);
            int physicalHeight = Math.Max(width, height);

            string normalizedUa =
                string.IsNullOrWhiteSpace(ua)
                    ? "default"
                    : ua.Trim().ToLowerInvariant();

            string key =
                $"{physicalWidth}x{physicalHeight}|{normalizedUa}";

            uint hash = StableHash32(key);

            // 同一个 hash 使用不同位段，
            // 避免三个值总是形成机械关联。
            int statusIndex =
                (int)(hash % StatusBarHeightPool.Length);

            int navigationIndex =
                (int)((hash >> 8) % NavigationBarHeightPool.Length);

            int toolbarIndex =
                (int)((hash >> 16) % BrowserToolbarHeightPool.Length);

            return new BrowserUiMetrics
            {
                StatusBarHeight =
                    StatusBarHeightPool[statusIndex],

                NavigationBarHeight =
                    NavigationBarHeightPool[navigationIndex],

                BrowserToolbarHeight =
                    BrowserToolbarHeightPool[toolbarIndex]
            };
        }

        private static uint StableHash32(string value)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            uint hash = offsetBasis;

            byte[] bytes = Encoding.UTF8.GetBytes(value);

            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= prime;
            }

            return hash;
        }
    }
}
