# 当前任务

状态：已完成（2026-09-05）

## 任务

- 编号：2026-09-05-audio-relay-v1
- 目标：交付基于 ToolBox v0.6.0 / `ToolBox.PluginSdk` 0.6.0、Plugin API major 1、Manifest v2、Worker protocol 1 的新版音频流转插件。
- 范围：只修改 `插件/`；保留 Windows A2DP 平台连接实现，更新正式版本 Manifest、公共 UI、测试、README 和包验证。
- 软件基线：ToolBox `codex/toolbox-0.6.0-plugin-docs`，commit `5662fe4`。

## 验证

- SDK 0.6.0 已从软件 commit `5662fe4` 生成到插件 `sdk/` feed。
- Release 还原、构建和测试通过：0 警告、0 错误；AudioRelay 10/10，KeyboardMouse 4/4。
- Manifest v2、Plugin API major 1、outOfProcess 和版本一致性检查通过；`git diff --check` 通过。
- 正式包 `artifacts/AudioRelay-v1/AudioRelay-1.0.0.tpk` 已签名并通过包校验；SHA-256：`93F87B40BDFE95DCB86F075F4545036B45215901E1C730B738802FE14D59F6AB`。
- 后续优化：`StartAsync` 幂等、停止失败可恢复、设备文案统一为中文，并新增重复启动回归测试；全部插件测试 15/15 通过。
- 交付优化：连接代次过滤旧 Windows 连接事件，连接打开增加 30 秒超时，补充连接事件清理；最终 AudioRelay 测试 11/11 通过。
- 最终签名包 `artifacts/AudioRelay-final/AudioRelay-1.0.0.tpk` 已通过校验；SHA-256：`4FBA474E1955DFA06A9B958C9622DFD9880F6EE1ABC140216C5FDD357B8F1F54`。

## 已知边界

- 仍只接收 Windows 蓝牙 A2DP 媒体音频，不处理 HFP 通话、麦克风或逐应用混音。
- 未将证书、私钥或签名材料加入 Git；签名脚本在 Windows PowerShell 5 不兼容 PKCS#8，已使用 PowerShell 7 完成签名。

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
