# KeyboardMouse

这是一个使用 `ToolBox.PluginSdk` 的输入测试 Sample，不是 Host 内置功能。

- 通过 `IPlugin` 管理启动、停止和生命周期资源；
- 输入观察范围限定在插件自己的测试面板契约中，不启用全局键盘钩子；
- `manifest.json` 声明 `outOfProcess`，通用 Host 会通过 `ToolBox.PluginWorker` 启用它；
- `KeyboardTestContract.cs` 是 Sample 自己的功能契约，不属于 SDK，也不需要 Host 修改。

从仓库根目录运行 `tools/Validate-Plugins.ps1` 可用本地 NuGet SDK 构建；使用 `tools/New-PluginPackage.ps1` 生成 `.tpk`。
