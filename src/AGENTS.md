# SMAUTO 开发约定

## 适用范围与协作方式

本文件适用于 `src` 下的项目。更深目录的 `AGENTS.md` 可以补充局部规则；用户在当前会话中的明确要求优先于本文件。

- 使用中文交流，先说明结果，再说明改动、验证和实际限制。
- 对用户已经授权的工作，常规实现选择、可逆修改和验证直接推进，不重复询问。重大功能选择、破坏性操作或超出授权范围的操作再确认；不把等待或默认选项视为授权。
- 项目源码和文档的创建、修改、复制、移动、重命名直接执行，不逐项确认；删除操作遵守实际工具权限。普通源码编辑不视为破坏性操作，不能因为涉及多个文件而反复确认。
- 修改前检查相关代码、调用方、测试和工作区已有改动。保留用户修改，不重置工作区，不顺带重构无关模块。
- 默认在当前工作区完成任务，不擅自提交、推送、发布或部署。
- 优先用 `rg` 搜索；读取相关文档后，以当前源码和本次验证为依据。历史文档可能描述已移除的实现。

## 项目职责

| 路径 | 职责与修改边界 |
| --- | --- |
| `MainClient` | Windows WinForms 客户端，任务调度、并发控制、代理准备、配置与结果处理。通过项目引用和 `SmAdExecutor` 执行 SMAd，每次执行创建独立任务对象。 |
| `Plugins/SMAd` | 页面访问主流程、页面生命周期、站点策略、页面动作与执行结果。主要入口是 `SMAdTask`。 |
| `Libraries/QTP.Common` | 浏览器运行时、Chromium 进程、Playwright 驱动、资源回收、公共结果模型及代理检测。 |
| `Libraries/HumanTouchP2_Enhancedv3` | 当前使用的强化触屏实现，程序集为 `HumanTouchP2.Enhancedv3`，API 命名空间保留 `PlaywrightHumanInput`。 |
| `Libraries/HumanTouchP2_Enhanced`、`Libraries/HumanTouchP2` | 历史版本与迁移参考；新增强化功能放在 v3，不默认同步修改旧版本。 |
| `tests` | 回归与真实浏览器测试。根据改动选择对应项目。 |
| `docs` | 架构决策、行为说明、故障诊断及验证记录。 |

公共库与 SMAd 使用 .NET 8；MainClient 使用 `net8.0-windows`、WinForms 和 COM 引用。项目使用 Patchright，代码中的 API 仍采用 `Microsoft.Playwright` 命名空间，不擅自替换依赖或升级包。

## 浏览器、对象所有权与并发

- 使用现有 `BrowserRuntimeManager`、`BrowserRuntimeLease`、`ChromiumSessionManager` 和 Playwright provider/lease 管理资源，不在业务策略中另建独立的启动、连接和销毁链路。
- CDP 地址使用运行时获取的实际端点和自动端口机制，不恢复人工固定端口分配。
- 每次执行隔离配置、取消令牌、用户目录、页面状态和触屏会话。单次任务失败不得取消其他执行或关闭共享 Playwright 驱动。
- 页面和 CDP session 必须配对，使用 `WorkerRunContext.ActivePage` 的整体绑定；输入前检查绑定是否变化，不继续操作失效页面。
- CDP session 使用现有缓存、失效和释放机制。关注页面关闭、创建中释放、迟到创建和重复初始化，避免泄漏及重复监听。
- 单个目标初始化失败不得直接关闭整个浏览器；故障 session 与健康页面隔离。旧 session 的分离事件不能清掉同目标新 session 的状态，真正的 CDP 连接故障仍须传播。
- 当前原生屏幕配置下，新目标自动附加不暂停页面启动；输入前仍等待目标的触屏初始化完成，避免导航与初始化互相等待。
- 跨页面输入使用同一触屏会话的输入协调器。Tap、Swipe、文本输入以及导航观察期间的后台动作必须遵守现有互斥机制。
- 浏览器回收只针对本次执行拥有的资源，记录结束原因、进程退出确认、强制终止及清理错误。不要用全局进程名结束 Chromium 或 Node。
- 独立诊断日志使用 `BrowserLifecycleLog`，按 executionId 关联启动、原始停止来源、执行阶段和回收结果，位于程序目录 `logs/browser-lifecycle`。日志写入失败不得影响业务；不记录完整任务参数或带凭据的命令行。强制终止后的退出码不能单独判为原生崩溃。
- 用户正在运行或调试 MainClient 时，不为解决 DLL 锁定而结束其进程；使用独立构建输出目录。
- 用户目录清理遵守现有队列和文件占用重试机制。递归删除前确认绝对路径属于本次执行拥有的目录，不删除其他用户数据。

## 页面操作与触屏规则

- 业务触屏操作优先走 v3、`SmAdTouch` 和现有 `PageActionExecutor`，不绕过协调器随意发送输入。
- JavaScript 可用于只读的定位、命中测试、遮挡检查和滚动状态采样；不以 `element.click()`、直接修改滚动位置等方式替代要求的原生触屏动作。
- 定位不等于可点击。检查元素连接状态、尺寸、可见区域、滚动容器裁剪、遮挡和稳定性；坐标必须与实际输入所用主视口一致，注意 iframe 坐标。
- 移动到目标后检查操作结果；未到位不能继续假定点击成功。目标失效时重新定位应有明确预算。
- 爱采购、1688 及推荐词采用当前站点策略中的模式：触屏浏览并阅读后，从当前视口可见且未遮挡的候选随机选一个直接点击；不重新引入寻找屏外目标的长滚动链路。
- 点击输入派发完成、导航发生、页面就绪和业务成功分别记录。不要把输入完成直接当成访问、跳转或业务成功。
- 点击效果不确定时，不自动重放点击。下载、同页面导航、新页面和已验证 DOM 效果按各自结果处理。
- 导航或新页面就绪后，按现有规则随机观察 `2000–6000` 毫秒，再开始后续动作。观察期遵守输入锁，任务取消、页面关闭和绑定变化应及时退出。
- 导航观察时间不挤占导航执行预算；排队等待不应消耗动作本身的执行超时。避免站点策略再叠加相同的导航等待。
- 成功检测到没有广告标记时，保留快速完成路径，跳过后续浏览、点击和停留；检测失败不能当成没有广告。不要擅自移除入口页面加载等待。

## 超时、取消与结果

- 区分调用方取消、执行生命周期到期、局部动作超时、页面崩溃、CDP/驱动断开和代理故障。保留原始原因及发生阶段。
- 所有长等待和异步操作传递适当的取消令牌。局部超时仅在页面、浏览器和执行仍健康时恢复；不能吞掉全局取消或资源失效。
- 可选页面动作优先通过 `OptionalPageOperation` 处理失败。普通动作无效果不自动标为浏览器故障；无法滑动时可按已有策略静态阅读剩余停留时间。
- `CompletedPvs`、`CompletedClickPvs`、`ClickRequested` 和实际点击结果独立表达。同一 PV 只结算一次，不伪造点击，不把未就绪页面计为有效访问。
- 详细结果通过 `WorkerExecutionResult` 返回，保留旧元组入口兼容性。业务统计事件继续由原有流程发出，不因增加结果字段重复上报。
- 使用现有动作日志记录派发、结果、耗时、导航就绪及观察时间。诊断日志不包含代理密码、凭据或其他秘密。

## 代理、HTTP 响应与下载

- 代理错误分类统一使用 `ProxyFailureClassifier`，并检查任务是否启用代理。
- 主导航以请求/主框架关系判断，不以尚未更新的 `page.Url == request.Url` 判断。Context 级网络监听覆盖新页面初始化前的请求和响应。
- 新页面最初的请求可能尚无 Frame，网络事件处理不得因此抛出异常到 Playwright dispatcher。明确代理错误独立分类；初始响应无法归属页面时，在页面就绪后补查主文档 HTTP 状态，不能把错误页计为成功。
- 代理连接、认证、配置失败与目标路径失败分别记录。`TargetRoute` 表示当前代理路径无法完成目标访问，不证明代理对所有站点均失效。
- 普通超时、DNS、连接重置、空响应及源站认证不直接认定代理失效；第三方资源的目标路径失败不自动结束整个任务。
- 确认代理故障时保留首次分类和原因，停止本次执行，以 `BrowserStopKind.ProxyFailed` 回收，并通过 `WorkerExecutionResult.ProxyFailure` 通知 MainClient 停止该消费任务剩余 UV。
- 主文档 HTTP 407 在代理模式下作为认证故障处理。403、429、404 和 5xx 作为页面错误，不自动判定整个代理失效。
- HTTP 错误页面不能计为入口就绪、有效访问或成功跳转；跳过其后续点击、可选操作和停留。`RequestFailed` 和异常捕获不能替代响应状态检查。
- 代理预检查与 Chromium 使用同一地址和协议解析，尊重上游取消。连通性检测与获取 IP 元数据分开，不能因关闭元数据选项而跳过代理预检查。
- 预检查成功不保证后续目标访问成功。`IpTtl` 仍是现有任务时间限制，不冒充供应商实际过期时间；不擅自增加自动换 IP 或无界重试。
- 入口页面就绪前的下载仅记录诊断，不计业务下载；就绪后才启用业务计数。入口转下载最多按已有规则在同浏览器重试一次。
- 入口导航超时及已限定的短暂网络错误可在同浏览器重试一次；明确代理错误和无下载证据的导航中断不进入普通重试。不能把这条恢复规则扩展为重复点击。
- `ERR_ABORTED` 必须有下载事件或附件响应证据才能按下载恢复，不能把所有导航中断当下载。反复转下载不能计为有效访问。

## 屏幕与视口

- 当前方案由定制 Chromium 在原生层处理屏幕、可用屏幕、DPR、视口和移动布局。生产 SMAd 不重新引入 `Emulation.setDeviceMetricsOverride`。
- 不增加 JavaScript Screen/Window 属性覆盖或尺寸回退。只读检测和记录实际值可以保留。
- 区分物理像素、CSS 像素、Windows DIP、外窗尺寸、内容视口和可用屏幕。外窗尺寸应围绕目标内容视口推导，不能把它们当作同一个值。
- 现有命令行参数依赖定制内核实现，标准 Chromium 不保证支持其全部语义。连接 CDP、初始化 TouchEmulator 或设置实际窗体边界不等于已完成原生移动布局。
- 显示行为验证应覆盖首个脚本、新标签、弹窗、再次导航和实际窗体边界。不同内核版本的结果不能互相替代。

## 构建与验证

从 `src` 执行，优先构建受影响的项目。MainClient 有 COM 引用，使用 Visual Studio 的 MSBuild；以下路径是当前环境示例，其他环境应先定位安装的 MSBuild。

```powershell
$msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild MainClient/MainClient.csproj /t:Build /p:Configuration=Debug /p:OutputPath=../Build/TapDiagnostics/ /m /v:minimal /nologo
& $msbuild tests/CdpInput.RegressionTests/CdpInput.RegressionTests.csproj /t:Build /p:Configuration=Debug /m /v:minimal /nologo
& './tests/CdpInput.RegressionTests/bin/Debug/net8.0/CdpInput.RegressionTests.exe'
```

真实触屏、移动及网络错误夹具测试需提供已存在的 Chromium 可执行文件：

```powershell
& './tests/CdpInput.RegressionTests/bin/Debug/net8.0/CdpInput.RegressionTests.exe' '../pc_chrome.packed/135.0.7049.119/chrome.exe' --movement
```

| 改动类型 | 优先验证 |
| --- | --- |
| 任务结果、适配和执行入口 | `tests/Step1.RegressionTests` |
| Tap、Swipe、页面绑定、导航观察、代理分类、HTTP/下载 | `tests/CdpInput.RegressionTests`，需要时增加真实浏览器 `--movement` |
| 生命周期、并发、取消、TTL、回收 | `tests/BrowserRuntime.RegressionTests`；压力与触屏诊断选项见其 README |
| CDP 自动端点与连接 | `tests/ChromiumConnection.SmokeTests` |
| 原生屏幕、视口与窗口 | `tests/DeviceDisplay.RegressionTests`；使用对应定制内核 |

- 对高风险行为增加能验证故障和边界的测试，不写仅复述实现的测试。纯文档或低影响可逆修改不要求新增测试。
- 测试使用隔离用户目录、受控页面或本地网络夹具；不默认访问业务站点、触发广告点击或使用真实代理供应商。
- 测试通过后，只有新修改、失败或未解决问题才重复或扩大验证。报告实际执行的命令、结果和未验证范围，不固定要求某个历史检查数量。
- Chromium 135 的本地夹具已验证触屏；部分 Chromium 145 headless 曾出现原始触屏后的脚本超时。纯模型测试、生命周期测试或进程退出码通过，不代表该内核的触屏业务兼容性通过；需要检查触屏诊断结果。

## 相关说明

- `docs/SMAd-局部动作失败与访问结果-20261009.md`
- `docs/SMAd-入口导航与下载计数-20261009.md`
- `docs/SMAd-代理故障与页面错误处理-20261009.md`
- `docs/SMAd-浏览器退出诊断与页面故障隔离-20261012.md`
- `docs/SMAd-浏览器独立生命周期日志-20261013.md`
- `Libraries/HumanTouchP2_Enhancedv3/README.md`
- `tests/*/README.md`

调整上述架构或行为约定时，同步更新相关说明及本文件，避免规则与源码长期不一致。
