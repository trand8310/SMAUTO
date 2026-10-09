# 页面动作重写

页面输入、观察效果和任务结果分开实现。MainClient 继续直接调用 SMAd；业务项目引用 HumanTouchP2.Enhancedv3。旧 Enhanced 不接收本轮增强改动。

## 动作与结果

- PageActionExecutor 在输入前监听来源页面的 Popup、Download、FrameNavigated；URL 比较使用完整相等判断，支持相同 URL 重载。
- 动作共享一个等待期限。新页通过 DOMContentLoaded 和可见 body 检查后才激活；站点可传入关键 DOM 效果判据。body 就绪不代表业务数据已经加载。
- ClickOutcome 分别表达未派发、没有确认效果、导航、新页、下载、DOM 效果、失败、取消。Attempted 不能替代业务成功。
- 搜索清空输入、逐字符输入并确认最终值，提交只执行一次。未确认结果不盲目重放。

## 输入、定位与滚动

- HumanInputCoordinator 按 HumanTouchSession 串行化跨页面的触摸、键盘、直接滚动；保留 CDP 会话的触点锁和取消清理。
- 操作前检查活动页；后台弹窗关闭经过动作执行器，确认消失后才记录已关闭。导航期间后台关闭在动作边界排队。
- BoundingBox 统一使用主视口坐标；可点击候选保留 SourceFrame 身份，不依靠重复的 frame URL 定位。
- 滚动跟踪同一个 DOM 容器及所属 frame；连续 3 次、间隔 80ms 的采样稳定后验证方向和距离。稳定等待最多 2.5 秒。
- MoveToTargetAsync 返回 Ready、NotFound、Disabled、NoProgress、TimedOut、PageChanged；优先在目标滚动祖先内移动。旧 MoveToElement 接口兼容。
- 直接滚动降级默认关闭，可通过 SmAdTouch.AllowDirectScrollFallback 开启；开启时仍经过锁、稳定等待和诊断。
- DOM 读取和输入增加期限；取消向上传播。逐字符输入的原生请求结束后才释放锁，避免遗留后台键盘输入。

## 任务与站点

- 每轮状态在入口导航前重置，避免清掉导航期间发生的下载。
- 入口失败、导航超时、验证/登录拦截、所有轮次都未完成返回失败；停留阶段异常不再转换为成功。
- CompletedPvs 表示已完成轮次。多 PV 保留原策略提前结束规则；结束循环本身不代表完成。
- 初始化任务被跟踪，失败缓存可移除，关闭页面移除缓存；退出时取消并等待后台，再释放会话、浏览器、进程。
- OfferTargetResolver 只按真实 host 返回已知站点选择器，未知站点返回空。原解析过程中隐式浏览、咨询输入、发送、body/iframe 点击已去除。
- 通用后处理保留阅读，不自动点击任意“同意、确认、继续”容器。原明确站点策略的提交入口单独存在，未运行第三方提交来验证。
- 加载观察时间按配置毫秒执行；停留和持续浏览改用 Stopwatch。

## 记录与验证

动作追加到 AppContext.BaseDirectory/logs/page-actions/*.jsonl，包括意图、前后 URL、派发、结果、耗时和失败原因；触摸记录附 v3 实际坐标和接触轨迹。内存仅留最近 500 条，文件保留全部已记录动作。WorkerExecutionResult 增加 CompletedPvs、ActionLogPath，元组接口兼容。

回归覆盖未派发、URL 前缀变化、同 URL 导航、popup、下载、DOM 效果、未就绪、取消、监听清理、活动页变更、host 匹配、输入互斥、惯性稳定、容器身份和坐标。

实际验证限制：此前自定义 Chromium 145 无界面内核的原始 CDP touch 后脚本读取超时尚未解决。回归和构建成功不说明第三方点击、提交、下载已验证；未自动运行这些副作用动作。

## 本轮验证结果

- CdpInput.RegressionTests：59 项通过，包括页面动作结果、取消、监听清理、输入协调和滚动稳定。
- Step1.RegressionTests：24 项通过，执行结果和 MainClient 调用保持兼容。
- Chromium 145 本地 HTML：4 项真实 DOM 验证通过，涵盖 iframe 容器、主视口坐标、稳定采样和节点身份；没有触发真实站点操作。
- Visual Studio MSBuild 构建 MainClient 及其项目引用成功；git diff --check 无空白错误。
