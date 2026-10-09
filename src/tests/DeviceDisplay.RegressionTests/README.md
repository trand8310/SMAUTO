# Device display regression checks

Build with the same .NET/MSBuild environment as SMAd. Run without arguments for configuration checks, or supply a local Chromium executable for real browser checks:

```powershell
dotnet run --project tests/DeviceDisplay.RegressionTests -- ../pc_chrome.packed/135.0.7049.119/chrome.exe
dotnet run --project tests/DeviceDisplay.RegressionTests -- ../pc_chrome.packed/135.0.7049.119/chrome.exe --headed
```

Uses a loopback HTTP fixture and owned browser profiles below `artifacts/<id>`. No external business website is accessed. The page's first script captures screen size, available screen, DPR and visible viewport before tests inspect it. Checks cover main navigation, popup tabs, independent popup windows, subsequent navigation, new pages and actual native window bounds. There is no JS Screen property fallback. Standard kernels are tested with available height equal to screen height, and a mismatched available height must fail without changing native properties.

Append `--require-native-avail` to require available height 867 with screen height 915. SMAd no longer sends CDP device metrics overrides. Browser checks require a custom kernel that implements screen, available-area, DPR and viewport/mobile configuration natively; stock kernels cannot satisfy this fixture. Historical reports with NativeOnly=true indicate absence of JS property fallback, and may still have used the now-removed CDP metrics override. Reports from older runs without `NativeOnly=true` may include the now-removed JS fallback.

