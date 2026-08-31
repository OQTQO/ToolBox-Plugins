# KeyboardMouse

这是一个使用 `ToolBox.PluginSdk` 的输入测试 Sample，不是 Host 内置功能。

- 通过 `IPlugin` 管理启动、停止和生命周期资源；
- 输入观察范围限定在插件自己的测试面板契约中，不启用全局键盘钩子；
- `manifest.json` 使用 v2，声明 `outOfProcess` 和平台定义的 `host.ui.input-events` 能力，通用 Host 会通过 `ToolBox.PluginWorker` 启用它；
- 通过 SDK 的 `IPluginUiProvider` 暴露通用输入测试区域和计数状态，Host 不引用本插件类型；
- `KeyboardTestContract.cs` 是 Sample 自己的功能契约，不属于 SDK，也不需要 Host 修改。

从仓库根目录运行 `tools/Validate-Plugins.ps1` 可用本地 NuGet SDK 构建并使用临时测试密钥验证签名；正式 `.tpk` 必须使用受保护的稳定发布者私钥生成。
