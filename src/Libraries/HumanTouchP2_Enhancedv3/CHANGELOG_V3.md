# v3

- 从当前 Enhanced 独立复制源码，不共享编译文件，不复制 bin/obj。
- 新程序集 HumanTouchP2.Enhancedv3；保留调用接口与命名空间。
- MainClient、SMAd 项目引用切换到 v3；加入解决方案 Libraries 分组。
- HumanTapPlanner：用户/设备一致的按压时序和接触压力包络。
- HumanActionRhythm：点击与滑动共用输入间隔，抵扣已有观察时间。
- HumanTouchSession：独立 TapCount、输入空闲时间和最后输入类型。
- HumanTapTrace：记录完成派发的实际时长与接触参数。
- 原来的轨迹、页面上下文、取消清理、会话串行化等实现继承自 Enhanced。
