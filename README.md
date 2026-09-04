# ToolBox Plugins

这是 [ToolBox](https://github.com/OQTQO/ToolBox) 的官方与朋友插件仓库。插件源码独立于 ToolBox 主仓库维护，提交插件不需要修改 ToolBox Host、Core 或 Worker。

新对话或新插件开发先阅读 [`AI.md`](AI.md)；SDK 版本和运行边界见 [`docs/compatibility.md`](docs/compatibility.md)，朋友提交规范见 [`docs/contribution.md`](docs/contribution.md)。

## 当前插件

| 插件 | 目录 | 说明 |
| --- | --- | --- |
| KeyboardMouse | [`plugins/KeyboardMouse`](plugins/KeyboardMouse) | 键盘和鼠标输入测试 |
| AudioRelay | [`plugins/AudioRelay`](plugins/AudioRelay) | Windows 蓝牙 A2DP 音频接收 |

每个插件都是独立的 .NET 项目，只依赖 `ToolBox.PluginSdk`。插件必须实现 `IPlugin`、提供 `manifest.json`，并通过 `outOfProcess` Worker 运行。

## 开发环境

安装 `global.json` 指定的 .NET 10 SDK。从 [ToolBox v0.5.0 Release](https://github.com/OQTQO/ToolBox/releases/tag/v0.5.0) 下载 `ToolBox-PluginDevKit-0.5.0.zip`，解压后把其中的 `ToolBox.PluginSdk.0.5.0.nupkg` 放入本仓库的 `sdk/` 目录。当前仓库主线和新插件开发统一使用 ToolBox v0.5.0 / SDK 0.5.0；GitHub 已发布的 v0.4.0 包仍作为历史资产保留。然后运行：

```powershell
dotnet restore .\ToolBox-Plugins.sln --configfile .\NuGet.config
pwsh -File .\tools\Validate-Plugins.ps1
```

若本机同时检出 ToolBox 软件仓库，可运行真实 Host 跨仓库烟雾测试：

```powershell
pwsh -File .\tools\Invoke-ToolBoxHostSmokeTest.ps1 `
  -SoftwareRepository ..\软件
```

脚本在隔离目录中构建并发布 Host/Worker、生成两个真实 `.tpk`，然后由 `ToolBox.Host.exe` 逐包验证安装、启用、通用 UI 快照、停用和卸载；插件必须适配软件契约，测试不为具体插件修改 Host 行为。

本地打包示例：

```powershell
pwsh -File .\tools\New-PluginPackage.ps1 `
  -RuntimeDirectory .\plugins\KeyboardMouse\bin\Release\net10.0 `
  -ManifestPath .\plugins\KeyboardMouse\manifest.json `
  -OutputDirectory .\artifacts `
  -SigningCertificatePath .\publisher.cer `
  -SigningPrivateKeyPath .\publisher.pk8
```

## 提交插件

朋友可以 Fork 本仓库，在 `plugins/<PluginName>/` 下新增插件，补充自己的 `README.md`，然后提交 Pull Request。GitHub Actions 会构建变更插件并生成 `.tpk`；合并后由维护者发布 GitHub Release。

ToolBox 用户从 Release 下载 `.tpk`，在软件中选择安装即可。当前不提供自动更新，插件更新通过新的 GitHub Release 手动下载和安装。

每个插件保持自己的 Manifest、项目和程序集版本；仓库 Release tag 表示一次发布批次，不会覆盖所有插件的版本。完整验证会检查版本一致性、能力声明、包内文件、哈希与 RSA-SHA256 签名，并通过两次打包结果的 SHA-256 验证可复现性。正式发布私钥只能通过受保护的 CI secret 提供，不能提交到仓库。
