# ToolBox 插件兼容矩阵

本文件记录插件仓库与 ToolBox 平台的公开兼容边界，具体契约以软件仓库发布的 SDK 和 `docs/plugin-api-v1.md` 为准。

| 项目 | 当前要求 |
| --- | --- |
| SDK 包 | `ToolBox.PluginSdk` 0.2.2 |
| Plugin API | Major 1 |
| 运行模式 | 必须支持 `outOfProcess` |
| UI | SDK 通用 UI 协议；不引用 WPF 或 Host 类型 |
| 包格式 | `.tpk`，包含合法 Manifest、运行时文件和 `package.json` |
| SDK 私有 DLL | 不得复制进最终 `.tpk` |
| 启动行为 | 安装后默认 Disabled，用户手动启用 |

## 跨仓库升级顺序

1. 软件仓库修改 SDK 或协议，并通过兼容性测试。
2. 发布新的 SDK/DevKit 和平台版本。
3. 插件仓库更新 NuGet 版本，构建、测试和打包。
4. 在插件 README 或发布说明中记录最低 ToolBox 版本。

## 当前限制

当前不提供签名验证、权限控制、沙箱、商城和自动更新。SHA-256 只用于包完整性校验，不代表发布者身份。
