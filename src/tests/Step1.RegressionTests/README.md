# 第一步回归检查

在 src 目录执行：

```powershell
dotnet run --project tests/Step1.RegressionTests/Step1.RegressionTests.csproj
```

无需启动浏览器或连接业务接口。检查设备种子稳定性与身份区分、
会话恢复的单调时钟及重复结算、旧插件结果适配、取消和失败原因映射。

SMAd 保留 ExecuteWorkerAsync 元组入口，新增 ExecuteWorkerWithResultAsync。
MainClient 通过 IQTPService 扩展入口调用；实现 IWorkerResultService 的插件提供详细结果，
仅实现旧接口的插件由适配器转换，并为 false 结果提供回退原因。
MainClient 现在直接引用 SMAd，调用 SmAdExecutor 创建并执行独立任务对象。
新增检查覆盖执行前取消、参数失败、日志转发与解除订阅、每次执行独立对象以及工厂失败。

取消结果表示调用方取消；代理异常、页面崩溃引起的内部取消仍表示失败。
结果记录不额外发送统计事件，既有业务统计仍由插件事件负责。
