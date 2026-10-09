

namespace SMAd.PlaywrightHumanInput
{
    using Newtonsoft.Json.Linq;
    using System.Security.Cryptography;
    using System.Text;

    public static class StableSeed
    {

        public static int Create(JObject dev)
        {
            string make =
                Normalize(dev.SelectToken("make")?.Value<string>());

            string model =
                Normalize(dev.SelectToken("model")?.Value<string>());

            string imei =
                Normalize(dev.SelectToken("imei")?.Value<string>());

            string androidId =
                Normalize(dev.SelectToken("androidid")?.Value<string>());

            string mac =
                NormalizeMac(
                    dev.SelectToken("mac")?.Value<string>());

            string oaid =
                Normalize(dev.SelectToken("oaid")?.Value<string>());

            string idfa =
                Normalize(dev.SelectToken("idfa")?.Value<string>());

            // 优先使用相对稳定的设备身份
            string identity;

            if (!string.IsNullOrWhiteSpace(imei))
            {
                identity = $"imei:{imei}";
            }
            else if (!string.IsNullOrWhiteSpace(androidId))
            {
                identity = $"androidid:{androidId}";
            }
            else if (!string.IsNullOrWhiteSpace(mac))
            {
                identity = $"mac:{mac}";
            }
            else if (!string.IsNullOrWhiteSpace(oaid))
            {
                identity = $"oaid:{oaid}";
            }
            else if (!string.IsNullOrWhiteSpace(idfa))
            {
                identity = $"idfa:{idfa}";
            }
            else
            {
                identity = "unknown";
            }

            string value =
                $"{make}|" +
                $"{model}|" +
                $"{identity}";

            byte[] bytes =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value));

            return BitConverter.ToInt32(bytes, 0)
                   & 0x7FFFFFFF;
        }

        private static string Normalize(string? value)
        {
            return (value ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
        }

        private static string NormalizeMac(string? value)
        {
            return Normalize(value)
                .Replace(":", "")
                .Replace("-", "");
        }
    }


}
