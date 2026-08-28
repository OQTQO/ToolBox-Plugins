# AudioRelay

这是一个 Windows 蓝牙 A2DP 接收 Sample，不是 Host 内置功能。

- 通过 `ToolBox.PluginSdk` 使用资源 Lease 和插件生命周期；
- `AudioRelayPlatform.cs` 封装 Windows 音频平台依赖，设备发现和连接状态留在 Sample 内；
- `manifest.json` 将 `outOfProcess` 设为首选模式；为底层兼容性测试保留 `inProcess` 声明，但通用 Host 始终选择进程外 Worker；
- `AudioRelayContract.cs` 是 Sample 自己的功能契约，不属于 SDK，也不需要 Host 修改。

从仓库根目录运行 `tools/Validate-Plugins.ps1` 可用本地 NuGet SDK 构建；使用 `tools/New-PluginPackage.ps1` 生成 `.tpk`。
