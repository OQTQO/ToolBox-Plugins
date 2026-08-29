# ToolBox Plugins

这是 [ToolBox](https://github.com/OQTQO/ToolBox) 的官方与朋友插件仓库。插件源码独立于 ToolBox 主仓库维护，提交插件不需要修改 ToolBox Host、Core 或 Worker。

## 当前插件

| 插件 | 目录 | 说明 |
| --- | --- | --- |
| KeyboardMouse | [`plugins/KeyboardMouse`](plugins/KeyboardMouse) | 键盘和鼠标输入测试 |
| AudioRelay | [`plugins/AudioRelay`](plugins/AudioRelay) | Windows 蓝牙 A2DP 音频接收 |

每个插件都是独立的 .NET 项目，只依赖 `ToolBox.PluginSdk`。插件必须实现 `IPlugin`、提供 `manifest.json`，并通过 `outOfProcess` Worker 运行。

## 开发环境

从 ToolBox 主仓库的 GitHub Release 下载 `ToolBox-PluginDevKit`，解压后把其中的 `ToolBox.PluginSdk.0.2.2.nupkg` 放入本仓库的 `sdk/` 目录。然后运行：

```powershell
dotnet restore .\ToolBox-Plugins.sln --configfile .\NuGet.config
dotnet build .\ToolBox-Plugins.sln --configuration Release --no-restore
dotnet test .\ToolBox-Plugins.sln --configuration Release --no-build --no-restore
```

本地打包示例：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\New-PluginPackage.ps1 `
  -RuntimeDirectory .\plugins\KeyboardMouse\bin\Release\net8.0 `
  -ManifestPath .\plugins\KeyboardMouse\manifest.json `
  -OutputDirectory .\artifacts
```

## 提交插件

朋友可以 Fork 本仓库，在 `plugins/<PluginName>/` 下新增插件，补充自己的 `README.md`，然后提交 Pull Request。GitHub Actions 会构建变更插件并生成 `.tpk`；合并后由维护者发布 GitHub Release。

ToolBox 用户从 Release 下载 `.tpk`，在软件中选择安装即可。当前不提供自动更新，插件更新通过新的 GitHub Release 手动下载和安装。
