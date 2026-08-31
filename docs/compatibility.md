# ToolBox 插件兼容矩阵

本文件记录插件仓库与 ToolBox 平台的公开兼容边界。ToolBox 软件仓库是平台契约的权威源，具体行为以目标软件 Release、发布的 SDK/DevKit、兼容性测试和 `docs/plugin-api-v1.md` 为准；发生冲突时插件适配软件。

| 项目 | 当前要求 |
| --- | --- |
| SDK 包 | `ToolBox.PluginSdk` 0.4.0 |
| 目标框架 | .NET 10；插件必须按目标 ToolBox Release 重建 |
| Plugin API | Major 1 |
| 运行模式 | 必须支持 `outOfProcess` |
| UI | SDK 通用 UI 协议；不引用 WPF 或 Host 类型 |
| Manifest | v2，必须声明软件平台定义的能力 ID |
| 包格式 | package format 2，包含 `manifest.json`、`package.json`、`signature.json` 和运行时文件 |
| 发布者签名 | RSA-SHA256；发布者 ID 与证书指纹由软件 TOFU 信任策略绑定 |
| SDK 私有 DLL | 不得复制进最终 `.tpk` |
| 启动行为 | 安装后默认 Disabled，用户手动启用 |

## 跨仓库升级顺序

1. 软件仓库修改 SDK 或协议，并通过兼容性测试。
2. 发布新的 SDK/DevKit 和平台版本。
3. 插件仓库更新 NuGet 版本，构建、测试和打包。
4. 在插件 README 或发布说明中记录最低 ToolBox 版本。

联合开发尚未发布的平台能力时，插件任务记录必须写明软件仓库 commit 或 tag；未记录时不得默认追随软件仓库 `main` 的临时行为。

## 当前限制

net8 插件、旧 Manifest v1、package format 1 和未签名包不兼容。能力声明用于安装审查和策略数据，但当前 Worker 仍不是 Windows 权限沙箱；商城和自动更新也不在当前范围。

发布前除插件单元测试和包结构校验外，应使用 `tools/Invoke-ToolBoxHostSmokeTest.ps1` 对目标软件源码或 Release 执行真实 Host 安装、Worker 启停、通用 UI 快照和卸载验证。联合开发使用未发布软件时，任务记录必须写明软件 HEAD，并明确工作区是否包含未提交平台变更；出现冲突时以软件实现与兼容性测试为准修改插件。
