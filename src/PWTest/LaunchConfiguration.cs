using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PWTest;

internal static class LaunchConfiguration
{
    public const string DefaultArguments = """
--no-first-run --no-default-browser-check --show-avatar-button=never --force-prefers-no-reduced-motion --enable-unsafe-swiftshader --hide-bad-flags --hide-crashed-bubble --ignore-certificate-errors
--webgl-vendor="Google Inc. (Qualcomm)" --webgl-renderer="ANGLE (Qualcomm, Adreno (TM) 750, OpenGL ES 3.2)"
--hardware-concurrency=8 --device-memory=8 --js-memory-info="29400000|31200000|1130000000" --storage-quota="0|138500073060"
--user-agent="Mozilla/5.0 (Linux; U; Android 16; zh-cn; 23117RK66C Build/BP2A.250605.031.A3) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Mobile Safari/537.36"
--full-version=125.0.6422.136 --platform=Android --platform-version=16.0.0 --product-model="23117RK66C" --make="xiaomi"
--force-webrtc-ip-handling-policy --webrtc-ip-handling-policy=disable_non_proxied_udp --webrtc-ip=118.248.225.244
--window-position=0,0 --window-size=428,915 --device-pixel-ratio=2.625 --screen-size=412,915 --screen-avail-size=412,829 --screen-color-depth=24
--max-touch-points=5 --touch-events=enabled --netinfo=4g,cellular,200,1.7 --battery=0,Infinity,7200,0.65
--geolocation-permission=allow --ssl-grease-cipher=1231 --fingerprint=1233456 --enable-canvas-noise --enable-audio-noise --disable-pdf-viewer
""";

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CommandLineToArgvW(string commandLine, out int count);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);

    public static string[] Parse(string arguments, string profile)
    {
        // Parse arguments, never execute them through cmd.exe. Support batch line continuations.
        var text = Regex.Replace(arguments, @"\^[ \t]*\r?\n", " ")
            .Replace("\r", " ").Replace("\n", " ")
            .Replace("%PROFILE%", profile, StringComparison.OrdinalIgnoreCase);
        var ptr = CommandLineToArgvW("pwtest.exe " + text, out int count);
        if (ptr == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var parsed = Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr, i * IntPtr.Size)) ?? "").ToArray();
            if (parsed.Any(x => !x.StartsWith("--", StringComparison.Ordinal)))
                throw new ArgumentException("启动参数只能包含 -- 参数；浏览器路径和 URL 不要放在参数框中。");
            if (parsed.Any(x => x.Equals("--user-data-dir", StringComparison.OrdinalIgnoreCase) || x.StartsWith("--user-data-dir=", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("用户目录请填写在单独的用户目录框中，不要在启动参数中重复设置。");
            if (parsed.Any(x => x.Contains("%~dp0", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("%~dp0 是批处理变量，参数框中请使用绝对路径或 %PROFILE%。");
            return parsed;
        }
        finally { LocalFree(ptr); }
    }
}
