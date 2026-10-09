# SMAd ExecuteWorker 页面操作分析

分析对象：当前 SMAd、HumanTouchP2.Enhancedv3 及其页面辅助类。采用静态调用链审查；本轮未修改业务代码、未运行真实站点。行号对应分析时的源码，后续修改后可能变化。

## 结论

底层已经有真实 CDP Touch、输入锁、触点清理、画像与点击节奏。当前主要问题在页面动作的编排和结果判断：输入完成、页面变化、任务完成三个层次还没有清楚区分。继续增加随机轨迹不能解决这些问题。

建议优先顺序：动作结果与导航观察 → 就绪/滚动稳定/定位 → 输入事务与取消预算 → 任务结果 → 站点策略拆分 → 节奏和诊断统一。

此前记录的 145 无界面内核问题仍是实际验证限制：原始 CDP 触摸也会导致后续脚本读取超时。已有模型和接口回归通过不能替代实际业务页面验证。见 SMAd-CDP改进记录.md 第 7 项。

## 实际执行链

1. ExecuteWorkerAsync（895）：兼容入口，转调 ExecuteWorkerWithResultAsync，最后转换元组。
2. ExecuteWorkerWithResultAsync（916）：解析配置、建立 LinkedCts、创建 WorkerRunContext 和落地页策略；取 Playwright；启动 Chromium、连接 CDP；取首个 BrowserContext；配置页面并安装生命周期监听。
3. RunMainFlowAsync（1128）：从设备信息生成长期画像和会话；建立 HumanTouchOperator；绑定页面输入；进入 PV 循环。
4. 每轮 EnsureSinglePageAsync（1911）：关闭后续页面、选第一个页面、激活配对，然后导航到 about:blank。
5. PrepareEntryAsync（1947）：决定入口 URL、是否首页搜索、获取查询词。
6. NavigateToEntryAsync（2005）：导航入口，等待 DOMContentLoaded；超时后部分情况下仍继续；上报入口阶段。
7. 首页路径执行 ExecuteHomepageTriggerAsync（2036）：点输入框、键盘输入、点搜索、等待 URL。其他路径执行固定等待和条件浏览。
8. 浏览结果页、读取页面内容与统计，随后按现有业务策略执行目标移动、点击和跳转判断。这里审查结果正确性，不建议调整广告点击比例或生成策略。
9. HandleLandingPageAsync（2485）：进入 UMob、AiSite、AiStudy、Ali 或默认策略；策略内部仍调用大量 SMAdTask 站点方法。
10. ExecuteTaskSleepPhaseAsync（3133）：执行部分站点后处理、记录 Success、处理下一轮条件、登录/下载退出、停留期间浏览。
11. CompleteSuccess（1395）及 BuildWorkerResult：生成任务结果；finally 取消后台、分离 CDP、关闭浏览器与进程。

## 优先级 P1：影响结果正确性和页面操作的缺陷

### 1. 点击失败与已执行但未导航混在一起

位置：SMAdTask.cs:3981、4023；Models/ClickResult.cs:17。

TapAsync 返回 false 时，目前返回 ClickResult.NoNavigation；但 NoNavigation 将 Attempted 设置为 true。遮挡、禁用、布局变化导致根本没发触摸时，上层仍收到“尝试过点击”。另一方面，通用 catch 返回 Fail，可能丢掉触摸已经完成但后置验证出错的信息。

改进：区分 NotDispatched、DispatchedNoEffect、EffectConfirmed、TimedOut、Canceled。记录输入是否发出、触点是否释放和页面效果是否确认；不能单靠 Navigated 评价全部动作。

这是前轮点击入口迁移后尚未完成的结果语义闭环。

### 2. 导航判断依赖 URL 前缀与页面总数

位置：SMAdTask.cs:3986、3996、4002；IElementHandle 重载具有同样实现。

- URL 等待使用“不相等”，最终判断却使用 StartsWith。旧 URL 为 /list，新 URL 为 /list/detail 或新增参数时，已经变化仍可能报告没有导航。
- 先在旧页面等待 URL，之后才检查新页面。正常打开新页面但旧页面不跳转，也会先耗费最长 10 秒。
- Pages.Count 增加后直接取 Pages[^1]，没有验证它是否是当前动作产生的窗口；一关一开、净数量不变时又会漏掉。
- 新页激活只完成会话初始化，没有验证目标 URL 和关键内容是否就绪。
- 所有异常压缩为 Fail，缺少判断失败的原因。

改进：在动作前捕获活动页面、URL 和关联事件；按动作预期观察同页导航、该页 popup、下载或 DOM 状态变化，共用一个等待期限。Popup 应关联来源页面，不以 Context 中最后一个页面代替。纯聚焦、切换标签、关闭弹窗不应进入固定导航等待。

Playwright 提供将动作与 popup 观察结合的机制：[RunAndWaitForPopupAsync](https://playwright.dev/dotnet/docs/api/class-page#page-run-and-wait-for-popup)。实际封装应保留 CDP Touch 派发，并在未知结果类型时协调多个观察器，不能每种事件各串行等待 10 秒。

### 3. 首页搜索可能没有成功，却报告搜索完成

位置：SMAdTask.cs:2058、2061、2076、2088、2090。

输入框和按钮的 Tap 返回值没有检查；没有确认输入框的聚焦状态、最终输入值；URL 等待超时被吞掉后仍返回 true。重试也没有先清理旧输入，部分输入成功后失败可能在下一次追加内容。站点其他输入/发送分支有同类问题。

改进：输入动作应独立封装聚焦、清空、输入、值确认；搜索动作应验证结果页面的关键状态。对提交动作，先确认上次结果再决定能否重试，避免对有副作用的动作盲目重放。

### 4. 页面加载就绪与观察停顿混用

位置：SMAdTask.cs:2011、2017、2020；1320—1326；ExecuteHomepageTriggerAsync:2091。

导航超时后，非指定域名路径没有进一步验证，也可能返回 true。DOMContentLoaded 只作为导航阶段的信号，现有流程没有验证异步数据、加载遮罩、目标容器等操作前提。固定等待既可能不够，也可能在页面已就绪后多等。

PageLoadedDelayMs 的实现也不符合严格时间预算：即使配置小于 3 秒，仍随机等 3—10 秒；restMs 大于 500 时执行 2—5 次浏览，而不是使用剩余毫秒数。

改进：把就绪等待、人的观察停顿和整体预算分开。页面就绪根据站点关键元素/加载状态判断，脚本失败不能被当成“尚未加载”；就绪后再由 v3 决定短停顿。配置必须明确是上限、最短停留还是目标停留。

### 5. 滚动验证不是滚动稳定检测

位置：v3/Playwright/ScrollTargetResolver.cs:82—95；v3/HumanTouchEngine.cs:131—175；SMAdTask.cs:2742、2889、3042、3802、4113。

当前松手后等待 45ms，只比较滚动偏移是否变化。惯性可能还在继续；随后读元素坐标和点击，目标位置仍会移动。

此外，GetAtPointAsync 在滚动前后重新按同一屏幕位置找容器，可能找到不同的容器；DidScrollAsync 没有核对 Key，可能比较了不同对象的偏移。MoveToElement 返回轨迹列表，不能明确表示目标已经可点击。

不少业务分支在触屏移动不成功后调用 ScrollIntoViewIfNeededAsync，绕过轨迹与触屏锁，也没有记录“采用了直接滚动降级”。

改进：对同一个滚动容器连续采样，确认偏移和目标布局在限定时间内稳定；移动返回明确的 Ready、ReachedBoundary、NoProgress、TimedOut 等结果。只在目标可操作时继续点击；直接滚动应为显式配置的降级，统一受输入协调器管理。

Playwright 的稳定性检查也要求跨连续动画帧观察元素边界：[Auto-waiting](https://playwright.dev/dotnet/docs/actionability)。自定义 CDP 输入需要自己补齐相应检查，调用 SendAsync 不会自动获得 Locator.TapAsync 的整套动作前检查。

### 6. iframe 坐标和候选来源没有完整贯通

位置：v3/Playwright/ScrollTargetResolver.cs:98—119；SMAdTask.cs:3855、3905；PlaywrightClickableHelper.cs:ClickableNodeInfo。

GetElementRectAsync 使用元素所在 frame 的 getBoundingClientRect；MoveToElement 却以主页面视口决定滚动方向。在 iframe 中，两者不在同一坐标系。

ClickableNodeInfo 保存了 FrameUrl，但部分调用仍用 ctx.Page.Locator(node.Selector) 在主文档重新定位，可能找不到 iframe 元素或找到主文档同名元素。用 URL 区分 frame 也不够稳：可能存在多个同 URL 或 about:blank frame。

改进：候选保留 frame 身份及所属页面，在原 frame 中重新解析目标；统一主视口坐标并分别处理外层页面、frame 和内层容器的滚动。不要只点整个 iframe 元素来代替其中的目标。

官方说明 BoundingBoxAsync 返回主 frame 视口坐标，子 frame 的 getBoundingClientRect 则不同：[BoundingBoxAsync](https://playwright.dev/dotnet/docs/api/class-locator#locator-bounding-box)。

### 7. 已有触屏锁，但没有完整的页面输入事务

位置：ProcessingPageElementTask:257—321；SmAdTouch；v3/Cdp/CdpTouchRuntime；首页输入与各 ScrollIntoView 分支。

目前锁保护同一 CDP 会话内完整触摸，不保护一整段“定位 → 清弹窗 → 聚焦 → 输入 → 验证”。后台弹窗可能在键盘输入时改变焦点；直接滚动也可以与触屏操作交错。活动页切换后，已捕获旧页的后台任务仍可能继续操作旧页。同一 HumanTouchSession 在多个页面会话间共享，但每个会话的锁不能统一保护共享画像状态。

后台关闭弹窗未检查 Tap 返回值，也没有等弹窗消失，却记录“已关闭”。后台因页面关闭退出后，其他页面未必会重新启动它。

改进：Worker 级页面动作协调器串行化会影响焦点/布局的操作；执行前核对活动配对及版本号。弹窗检测可以并发，实际处理应在动作边界执行；处理后确认消失。导航观察器应预先安装，等待网络/页面结果时采用明确的锁释放规则，避免锁住必须处理的弹窗。

### 8. 取消与超时没有贯穿所有页面操作

位置：SMAdTask.cs:1326、2679—2715、2817、3946；各宽泛 catch；v3 的 DOM 查询。

部分 BrowseTimesAsync 未传 token，多处 Task.Delay 未传 token。RetryPolicy 本身会传播取消，但它调用的页面操作及站点内层 catch 仍可能忽略取消。自定义 DOM 查询缺少统一等待上限，已知内核脚本问题尤其会放大这一点。

改进：任务预算、动作预算、取消信号统一贯穿；先单独传播 OperationCanceledException，再处理可恢复错误。脚本查询失败、页面关闭、定位失败应保留不同原因。WaitAsync 只限制调用方等待，不等于取消底层 Playwright 请求，超时后的资源状态和清理也要设计。

## 优先级 P1/P2：任务结果、策略与维护问题

### 9. 任务成功条件过宽

位置：RunMainFlowAsync:1213—1219、1285、1350、1391；PrepareEntryAsync:1988—1994；ExecuteTaskSleepPhaseAsync:3150、3227—3234。

获取入口失败可能以 CompleteSuccess 结束；所有 PV 都 continue 后仍 CompleteSuccess；停留阶段先发 Success，后面的操作异常又被转换为普通结束。

应明确“执行完毕”“按策略跳过”“页面目标达成”“失败”分别是什么。计数应依据已确认的阶段结果；至少记录实际完成的页面轮次、跳过原因与失败原因。是否要求所有 PV 成功需要按业务定义，不能由一个循环结束直接推断。

### 10. 站点解析函数存在隐式动作和导航

位置：ResolveOfferItemsAsync:2636—2998；各 LandingPageStrategy。

一个“找候选元素”的方法同时浏览、填写、发送、点击及跳转。外层策略可能继续使用旧 URL 分支、旧 Locator 或旧 frame 来源；排查时难以知道到底在哪个函数改变了页面。

改进：拆分纯解析、可恢复的页面准备、明确的动作执行。导航返回新页面配对，进入新的识别阶段；返回的目标要携带所属页面/frame，并在执行前再次验证。先提取重复动作，再逐个移动站点逻辑，不必一次重写全部策略。

### 11. 通用候选范围过宽

位置：TryHandleAllAsync:3759—3767；ResolveOfferItemsAsync 中 body/iframe 降级；GetCurrentViewportClickableElementsAsync:4123。

广泛匹配 div/span 的“确认、继续、允许”等文本可能命中大容器或非预期业务按钮。整个 body 或 iframe 可见不意味着它就是可操作目标。遍历 document 的全部元素也会增加 DOM 查询开销，长列表下尤为明显。

改进：按页面动作意图限定目标，优先有明确语义的按钮、链接、输入控件；未知站点缺少可靠目标时返回不可操作。提交、发送、下载等副作用动作不能作为无差别兜底。DOM 检测按需要采集并保留数量上限。

### 12. 节奏和动作记录仍分散

位置：SMAdTask.cs:1320、1345、1347、2299、3193、3219；v3/HumanTouchOperator.cs；CommonHelper.RandomRange。

v3 会话随机控制触点与部分浏览决策；业务层还有 Random.Shared 和 Guid.NewGuid，整体流程不能只凭 SessionSeed 复现。外层固定等待、BrowseTimes 的观察间隔、MoveToElement 的间隔和 v3 Rhythm 混合存在。

停留计时使用 DateTime.Now，在调整系统时间时可能改变时长。v3 的会话恢复已经使用单调时钟，但任务层和 BrowseFor 的期限还没有统一。

改进：明确每段等待的责任；观察时间、页面就绪时间和动作间隔分开记录。选择/观察等无副作用模拟决策使用注入的会话随机源；实际效果判断保持确定性。时长预算统一使用 Stopwatch/TimeProvider。不要以无规则随机目标或随机等待代替明确页面状态。

### 13. 初始化故障与句柄清理

InitPageAsync:1804 缓存 Lazy<Task>，失败后不删除坏项；HandleContextPageAsync:1693 又吞掉初始化异常。PageInitializations 长期保存关闭页面，退出时也没有完整等待初始化任务收束。

GetCurrentViewportClickableElementsAsync:4123 创建数组 JSHandle 和多个元素句柄；调用链未明确所有权与释放。元素还可能在遍历时因页面刷新/虚拟列表重建而失效。

改进：只在可以安全重试的初始化阶段移除故障缓存，事件注册和触屏配置分别幂等；记录并跟踪后台初始化任务，关闭时解除监听并等待收束。候选优先返回可重新解析的描述符，必须使用句柄时明确 async disposal。

## 建议分工

- SMAdTask：任务配置、业务决策、策略选择和任务结果。
- SMAd 页面动作执行器（建议新增）：活动页事务、导航/popup/下载观察、输入确认、动作预算、弹窗协作及结果记录。
- HumanTouchP2.Enhancedv3：触摸模型、节奏、目标移动、容器滚动稳定检测、坐标转换和底层安全清理。
- 站点策略：明确页面结构、目标描述和预期效果，不直接复制整套点击/等待/重试逻辑。

## 后续验收场景

1. 目标被遮挡：不派发触摸，结果不是 Attempted=true。
2. 单纯聚焦/关闭弹窗：以 DOM 效果验收，不等 URL 改变。
3. 同页新增 query/hash/path、同 URL 刷新、SPA 内容改变：按预期动作给出正确结果。
4. 原页不跳转而产生 popup、其他后台窗口同时产生、一关一开：只接管当前动作相关页面。
5. 惯性滚动、内层容器、嵌套 iframe、虚拟列表：在正确容器稳定后重新定位。
6. 后台弹窗出现于输入期间：不会改变另一动作的焦点或造成错误成功日志。
7. 页面加载慢、脚本请求挂起、初始化失败、任务取消：在预算内返回明确状态并完成清理。
8. 全部页面轮次失败/按策略跳过：任务结果和统计不混为页面目标成功。

真实浏览器场景需要独立的本地测试页先验证；不应依赖第三方站点随机表现判断基础组件是否正确。
