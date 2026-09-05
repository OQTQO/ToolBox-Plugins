# 当前任务

状态：已完成（2026-09-04）

## 任务

- 编号：2026-09-04-cross-repo-plugin-sdk-0-5-0
- 目标：让插件仓库 `main` 成为基于 ToolBox v0.5.0 / `ToolBox.PluginSdk` 0.5.0 的可复用开发基线，供其他开发者按 GitHub 文档创建和维护插件。
- 上游软件：ToolBox v0.5.0，标签提交 `9d1559e4df3da77b9911ae937580e73f85356e1c`；当前软件 `main` 为 `d3c63af`。
- 范围：SDK 本地 NuGet 包、集中版本属性、插件开发 README、贡献规范、兼容矩阵、AI 入口、恢复脚本和任务记录；不改变两个插件自身版本号和业务契约。
- 变更基线：本地提交 `713204a` 及其后的本任务提交均位于当前 `main`，未提交修改已全部纳入本任务。
- 分支决定：保留当前 `main`，已分别提交并推送；用户已明确授权同步 GitHub。
- 验收：SDK 0.5.0 可还原，插件构建/测试/包校验通过，恢复摘要不再报告 SDK 版本漂移，GitHub `main` 包含完整开发说明。

## 决策

- 现有 GitHub 发布包仍准确标记为 v0.4.0 / SDK 0.4.0；本次更新的是仓库 `main` 的开发基线，不把旧包改称 v0.5.0。
- 两个插件的独立版本号保持 KeyboardMouse 0.2.3、AudioRelay 0.3.1；是否发布新的插件包批次另按发布任务处理。
- SDK 0.5.0 从 ToolBox v0.5.0 DevKit 获取并校验，不直接引用软件源码。

## 验证

- 已下载并按 ToolBox v0.5.0 `SHA256SUMS-v0.5.0.txt` 校验 DevKit；SDK nupkg 已放入本地 `sdk/` feed。
- `dotnet restore ToolBox-Plugins.sln`：通过；因环境网络策略使用已验证缓存和公开 NuGet 依赖。
- 严格构建：SDK 0.5.0、两个插件和两个测试项目 0 警告、0 错误。
- 插件测试：KeyboardMouse 4/4、AudioRelay 10/10 通过。
- 双次确定性打包：两个插件包 SHA-256 一致；Manifest、签名、包哈希、路径安全和 SDK DLL 排除校验通过。
- `Invoke-ToolBoxHostSmokeTest.ps1 -SoftwareRepository ..\软件`：通过；两个插件均完成真实 Host 安装、启用/UI、停用和卸载。
- 恢复脚本和 `git diff --check`：通过；上下文已显示软件与插件 SDK 均为 0.5.0。
- 插件仓库提交 `6032d08`（含此前的 `713204a`）已推送到 GitHub `origin/main`。

## 已知边界

- 本次不自动发布新的插件 GitHub Release；已有 v0.4.0 包继续作为历史发布资产。
- Worker 仍是进程隔离而非 Windows 权限沙箱；商城和自动更新不在本任务范围。
- 两个仓库继续独立提交、验证和发布；插件不得直接引用软件源码。

## 下一步

1. 其他开发者从 GitHub `main` 和 ToolBox v0.5.0 DevKit 开始开发新插件。
2. 新插件包发布另建发布任务；现有 v0.4.0 Release 资产保持不变。
