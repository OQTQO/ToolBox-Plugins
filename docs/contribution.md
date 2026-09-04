# 插件提交规范

朋友可以 Fork `ToolBox-Plugins`，以 ToolBox v0.5.0 / `ToolBox.PluginSdk` 0.5.0 为开发基线，在 `plugins/<PluginName>/` 新增插件，并提供 Manifest、README、项目文件和必要测试。SDK 包和开发文档从 [ToolBox v0.5.0 Release](https://github.com/OQTQO/ToolBox/releases/tag/v0.5.0) 获取。

## 提交前检查

- 只通过 `ToolBox.PluginSdk` 使用平台契约。
- 不引用 `软件/` 的源码、Host、Core、Worker 或 WPF 页面。
- Manifest 的 ID、版本、入口程序集和 `outOfProcess` 设置有效。
- 启动、停止、崩溃和业务错误不会让 Worker 无法诊断。
- `.tpk` 不包含私有 `ToolBox.PluginSdk.dll`。
- 本地构建、测试和打包均通过。
- README 写清楚功能边界、使用条件和已知限制。

## Pull Request 范围

一个 Pull Request 尽量只包含一个插件或一个插件维护任务。平台能力不足时，应先在 ToolBox 软件仓库提出平台问题，不要在插件中复制平台实现。
