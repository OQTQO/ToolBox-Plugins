# AudioRelay

这是一个独立的 Windows 蓝牙 A2DP 手机媒体音频接收插件，不是 ToolBox Host 内置功能。

## 功能

- 搜索已经与 Windows 配对、并支持远程音频播放的手机；
- 刷新当前设备列表；
- 连接指定手机并接收媒体音频；
- 断开当前手机；
- 显示搜索、连接、断开、手机主动断开和错误状态；
- 连接失败后保留可重试状态，不需要重启 ToolBox。

ToolBox 使用 SDK 的 `IPluginUiProvider` 渲染通用状态卡片和操作按钮。插件不引用 WPF，Host 也不引用本插件类型。

## 使用条件

- Windows 10 version 2004（build 19041）或更高版本；
- 电脑有可用的蓝牙适配器和驱动；
- 手机已经在 Windows“设置 → 蓝牙和设备”中完成配对；
- 手机允许媒体音频通过蓝牙输出；
- ToolBox 在接收期间保持运行。

“搜索”只会重新发现已经配对且支持 `AudioPlaybackConnection` 的设备，不会替用户完成蓝牙配对。没有设备时，请先在 Windows 蓝牙设置中完成配对，再点击搜索。

## 使用流程

1. 在 ToolBox 中安装 `PhoneAudioRelay-<version>.tpk`；
2. 启用 `Phone Audio Relay`；
3. 点击 `Search paired phones`；
4. 在设备按钮中选择手机并点击连接；
5. 在手机上播放媒体，声音会进入电脑当前的 Windows 音频混音；
6. 结束时点击 `Stop receiving`，或停用插件。

启用时插件会执行一次初始搜索，但不会自动连接手机。`Refresh device list` 会再次搜索并尽量保留当前设备。手机主动断开后，可以直接再次连接。

## 音频边界

当前只处理手机媒体音频（A2DP）。不处理电话/HFP、手机麦克风、电脑录音、逐应用音量、混音比例或自动启动。手机音量、Windows 主音量、蓝牙编解码、延迟和音质仍由手机、适配器、驱动与 Windows 协商决定。

## 构建和打包

从仓库根目录运行：

```powershell
pwsh -File .\tools\Validate-Plugins.ps1
```

使用通用脚本生成 `.tpk`：

```powershell
pwsh -File .\tools\New-PluginPackage.ps1 `
  -RuntimeDirectory .\plugins\AudioRelay\bin\Release\net10.0-windows10.0.19041.0 `
  -ManifestPath .\plugins\AudioRelay\manifest.json `
  -Version 0.3.1 `
  -PackageName PhoneAudioRelay-0.3.1.tpk `
  -OutputDirectory .\artifacts `
  -SigningCertificatePath .\publisher.cer `
  -SigningPrivateKeyPath .\publisher.pk8
```

生成的包包含 Manifest v2、能力声明、插件运行时、SHA-256 文件清单和 RSA-SHA256 发布者签名，不携带私有 `ToolBox.PluginSdk.dll`。
