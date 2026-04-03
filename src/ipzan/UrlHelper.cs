
namespace ipzan
{
    using System;
    using System.Collections.Generic;

    public static class UrlHelper
    {
        public static Dictionary<string, string> ParseQueryParams(string url)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var uri = new Uri(url);
            string query = uri.Query.TrimStart('?');

            if (string.IsNullOrWhiteSpace(query))
                return result;

            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in pairs)
            {
                var kv = pair.Split('=', 2);
                string key = Uri.UnescapeDataString(kv[0]);
                string value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";

                result[key] = value;
            }

            return result;
        }
    }
}
