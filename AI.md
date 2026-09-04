# ToolBox-Plugins 仓库 AI 入口

## 项目定位

这是 ToolBox 的官方与朋友插件仓库。每个插件独立构建、独立打包、独立发布；新增插件不应修改 ToolBox Host、Core 或 Worker 源码。

## 先读什么

1. `docs/active-task.md`
2. `docs/compatibility.md`
3. 目标插件目录下的 `README.md`、`manifest.json` 和 `.csproj`
4. 相关测试，最后用 `rg` 定位实现

## 插件规则

- ToolBox 软件仓库是平台实现、SDK、Manifest、Worker 协议、安装格式和通用 UI 契约的唯一权威源。
- 插件与软件发生冲突时必须修改插件以适配软件，不得要求 Host 增加插件专用分支。
- 默认服从插件声明支持的 ToolBox Release、SDK 和 DevKit；适配未发布软件时必须在当前任务中记录准确的软件 commit 或 tag。
- 插件目标框架和 SDK 版本必须符合 `docs/compatibility.md`；当前仓库主线使用 .NET 10 / SDK 0.5.0，不保留旧框架分支。GitHub 上已有的 v0.4.0 插件包仍是历史发布资产，不代表当前主线基线。
- 第三方依赖只能通过 `ToolBox.PluginSdk` 和插件自己的依赖进入包。
- 必须实现 `IPlugin`，Manifest 必须合法并声明 `outOfProcess`。
- 插件 UI 只能使用 SDK 的通用 UI 协议，不引用 WPF 或 ToolBox Host 类型。
- 安装后默认不自动执行；`background` 只是描述信息。
- `.tpk` 必须使用 Manifest v2、package format 2、平台能力 ID 和有效发布者签名，且不得包含私有 `ToolBox.PluginSdk.dll`。
- 插件业务失败应反馈为插件自身状态或错误，不要求 Host 增加专用分支。

## 当前插件

| 目录 | Plugin ID | 说明 |
| --- | --- | --- |
| `plugins/AudioRelay` | `com.toolbox.audio-relay` | Windows 蓝牙 A2DP 媒体音频接收 |
| `plugins/KeyboardMouse` | `com.toolbox.keyboard-test` | 键盘和鼠标输入测试 |

## 新增插件清单

```text
plugins/<Name>/*.csproj
plugins/<Name>/manifest.json
plugins/<Name>/README.md
plugins/<Name>/IPlugin 实现
tests/<Name>.Tests/（有业务状态时必须增加）
发布目录和 .tpk 校验
```

不得通过 `..\..\软件` 引用平台源码。平台契约应通过已发布的 `ToolBox.PluginSdk` NuGet 使用。

## 验证命令

```powershell
dotnet test ToolBox-Plugins.sln --configuration Release
```

打包前确认 Manifest、运行模式、依赖文件、SHA-256 和包内文件清单。完成阶段后更新 `docs/active-task.md`；普通插件维护不修改软件仓库。
