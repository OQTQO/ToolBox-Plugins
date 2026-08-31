# 当前任务

状态：平台适配与 .NET 10 最终迁移完成；软件 0.4.0 已发布，插件发布候选等待远端验证。

## 任务

- 编号：2026-08-30-plugin-hardening
- 目标：服从 ToolBox 软件契约，修正发布版本一致性，补齐插件测试和包产物校验。
- 上游软件：ToolBox 0.4.0 / .NET 10 / Plugin API v1 / Manifest v2 / package format 2 / ToolBox.PluginSdk 0.4.0。
- 权威源：软件仓库的平台实现、兼容性测试、已发布 SDK 和协议文档。

## 约束

- 插件与软件冲突时修改插件，不给 Host 增加插件专用分支。
- 每个插件保持独立版本；仓库 Release tag 只表示发布批次。
- `.tpk` 必须声明平台能力、携带有效签名且不包含私有 ToolBox.PluginSdk DLL；不兼容旧包。

## 已完成

- 在插件 AI 入口和兼容矩阵中明确软件仓库是平台契约权威源。
- 上下文导出增加工作区规则、兼容矩阵、软件 Git HEAD 和 SDK 版本。
- SDK 版本集中到 `Directory.Build.props`。
- 修复 Release tag 覆盖所有插件 Manifest 版本的问题，保持插件独立版本。
- 构建后校验 Manifest、项目和程序集版本一致。
- 新增 KeyboardMouse 测试项目和 4 项状态/UI 输入测试。
- 新增 `.tpk` 路径、文件清单、SDK DLL 排除和 SHA-256 校验。
- CI 连续打包两次并比较 SHA-256，验证可复现性。
- CI 从中央 SDK 版本构造 DevKit URL，并用软件 Release 校验文件验证下载内容。
- 新增 `Invoke-ToolBoxHostSmokeTest.ps1`，隔离构建/发布真实 Host 和 Worker、生成两个插件包，并逐包验证安装、启用/UI 快照、停用和卸载。
- 两个插件迁移到 Manifest v2 与平台能力目录；插件没有定义自己的权限语义。
- `New-PluginPackage.ps1` 强制使用 DER 证书和 PKCS#8 RSA 私钥生成 `signature.json`；包校验验证证书有效期、发布者绑定和 RSA-SHA256 签名。
- Release workflow 从受保护 secret 读取证书与私钥，缺少签名材料会直接停止发布；普通验证使用一次性测试密钥，不产生可发布身份。
- Release workflow 复用验证脚本显式保留的隔离构建产物，不再依赖会被清理或不存在的项目 `bin` 目录。
- SDK 升级到 0.4.0；插件、测试和 CI 统一迁移到 .NET 10，旧插件二进制与未签名包不再兼容，符合“插件服从软件”的规则。
- 使用 `global.json` 固定 SDK 10.0.400；KeyboardMouse 升至 0.2.3，AudioRelay 升至 0.3.1，未保留 net8 或旧 SDK 分支。

## 验证结果

- `tools/Validate-Plugins.ps1`：在 .NET 10 / SDK 0.4.0 上通过。
- 严格构建：0 警告、0 错误。
- AudioRelay：10 项测试通过。
- KeyboardMouse：4 项测试通过。
- KeyboardMouse 0.2.3 与 AudioRelay 0.3.1 均通过双次可复现打包、证书/密钥配对和包内签名/哈希验证。
- `tools/Invoke-ToolBoxHostSmokeTest.ps1 -SoftwareRepository ..\软件`：通过；两个真实 `.tpk` 均完成 Host 全生命周期。联合验证的软件内容现已发布为 `v0.4.0`，提交 `e6a63a5e2c0471ec4929cf0c84016b7046ad3264`。
- Manifest v2 签名包复现性、签名验证和真实 Host TOFU 信任链路通过；插件测试仍为 KeyboardMouse 4 项、AudioRelay 10 项。
- GitHub workflow YAML 解析、上下文导出和 `git diff --check`：通过。

## 已知边界

- 本地跨仓库签名烟雾链路已完成；CI 从软件精确版本标签 `v0.4.0` 下载 DevKit 与校验清单，不依赖软件 `main`。
- GitHub Actions 仍使用主版本 tag，尚未固定为完整 commit SHA。

## 下一步

1. 通过远端 CI 后发布插件批次 `v0.4.0`。
2. 后续维护阶段将 GitHub Actions 固定到经过核验的完整 commit SHA。
3. 发布完成后归档本任务记录。
