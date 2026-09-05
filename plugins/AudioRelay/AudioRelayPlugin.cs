using System.Globalization;
using ToolBox.PluginSdk;

namespace AudioRelayPlugin;

public sealed class AudioRelayPlugin : IAudioRelayPlugin, IPluginUiProvider
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly IAudioRelayPlatform _platform;
    private AudioRelaySnapshot _snapshot = AudioRelaySnapshot.Disabled();
    private bool _started;
    private bool _disposed;
    private int _connectionGeneration;

    public AudioRelayPlugin()
        : this(new WindowsAudioRelayPlatform())
    {
    }

    internal AudioRelayPlugin(IAudioRelayPlatform platform)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _platform.StateChanged += OnPlatformStateChanged;
    }

    public string Id => "com.toolbox.audio-relay";

    public AudioRelaySnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return CloneSnapshot(_snapshot);
            }
        }
    }

    public event Action<AudioRelaySnapshot>? SnapshotChanged;

    public async ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_started)
            {
                return;
            }
            var lease = context.Resources.Acquire(
                new ResourceKey("audio.bluetooth.a2dp-sink"),
                ResourceAccessMode.Exclusive);
            try
            {
                context.LifetimeScope.Register(lease);
            }
            catch
            {
                lease.Dispose();
                throw;
            }

            _started = true;
            if (!_platform.IsSupported)
            {
                Publish(new AudioRelaySnapshot(
                    AudioRelayStatus.Unsupported,
                    [],
                    null,
                    null,
                    "此电脑需要 Windows 10 版本 2004（build 19041）或更高版本才能接收蓝牙音频。",
                    "AUDIO_RELAY_WINDOWS_UNSUPPORTED",
                    "Start"));
                return;
            }

            try
            {
                await SearchDevicesCoreAsync("Start", cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _started = false;
                Publish(AudioRelaySnapshot.Disabled());
                throw;
            }
            catch (Exception exception)
            {
                // A temporary discovery failure must not make the Worker unusable.
                // The plugin remains running so the user can press Search again.
                PublishFailure(
                    "AUDIO_RELAY_DISCOVERY_FAILED",
                    "Windows could not enumerate paired Bluetooth audio devices.",
                    exception,
                    "Start");
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        return StopCoreAsync(cancellationToken);
    }

    public ValueTask SearchDevicesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ExecuteSerializedAsync(
            () => SearchDevicesCoreAsync("Search", cancellationToken),
            cancellationToken);
    }

    public ValueTask RefreshDevicesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ExecuteSerializedAsync(
            () => SearchDevicesCoreAsync("Refresh", cancellationToken),
            cancellationToken);
    }

    public ValueTask ConnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        return ExecuteSerializedAsync(
            () => ConnectCoreAsync(deviceId, cancellationToken),
            cancellationToken);
    }

    public ValueTask DisconnectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return ExecuteSerializedAsync(
            () => DisconnectCoreAsync(cancellationToken),
            cancellationToken);
    }

    public PluginUiSnapshot GetSnapshot()
    {
        var snapshot = Snapshot;
        var actions = new List<PluginUiAction>();
        var canRefresh = _started && snapshot.Status is (AudioRelayStatus.Ready or AudioRelayStatus.Error);
        if (canRefresh)
        {
            actions.Add(new PluginUiAction(
                "refresh",
                "刷新",
                Description: "重新读取已配对的媒体音频设备。"));
            actions.AddRange(snapshot.Devices.Select(device => new PluginUiAction(
                "connect",
                $"连接 {device.Name}",
                device.Id,
                Description: $"连接 {device.Name} 并开始接收音频。")));
        }

        if (_started && (snapshot.Status is (AudioRelayStatus.Connecting or AudioRelayStatus.Streaming)
            || snapshot.Status == AudioRelayStatus.Error && snapshot.SelectedDeviceId is not null))
        {
            actions.Add(new PluginUiAction(
                "disconnect",
                "断开连接",
                Description: "停止接收当前设备的音频。"));
        }

        return new PluginUiSnapshot(
            snapshot.StatusMessage,
            [
                new PluginUiValue("连接状态", ToDisplayStatus(snapshot.Status)),
                new PluginUiValue("当前设备", snapshot.SelectedDeviceName ?? "未选择"),
                new PluginUiValue("已配对设备", $"{snapshot.Devices.Length.ToString(CultureInfo.InvariantCulture)} 台"),
                new PluginUiValue("音频方向", "设备 → 电脑")
            ],
            actions,
            null);
    }

    public async ValueTask<PluginUiSnapshot> ExecuteAsync(
        string actionId,
        string? argument,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        switch (actionId)
        {
            case "refresh":
                await RefreshDevicesAsync(cancellationToken).ConfigureAwait(false);
                break;
            case "connect":
                await ConnectAsync(
                    argument ?? throw new ArgumentException("A device id is required.", nameof(argument)),
                    cancellationToken).ConfigureAwait(false);
                break;
            case "disconnect":
                await DisconnectAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                throw new InvalidOperationException($"Unknown AudioRelay action '{actionId}'.");
        }

        return GetSnapshot();
    }

    private static string ToDisplayStatus(AudioRelayStatus status) => status switch
    {
        AudioRelayStatus.Ready => "未连接",
        AudioRelayStatus.Connecting => "正在连接",
        AudioRelayStatus.Streaming => "已连接",
        AudioRelayStatus.Refreshing => "正在刷新",
        AudioRelayStatus.Unsupported => "不受支持",
        AudioRelayStatus.Error => "需要处理",
        _ => "未启用"
    };

    public ValueTask<PluginUiSnapshot> HandleInputAsync(
        PluginInputEvent input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetSnapshot());
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _started = false;
            _platform.StateChanged -= OnPlatformStateChanged;
            try
            {
                _platform.Dispose();
            }
            finally
            {
                Publish(AudioRelaySnapshot.Disabled());
                SnapshotChanged = null;
            }
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }

    private async ValueTask StopCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_started)
            {
                Publish(AudioRelaySnapshot.Disabled());
                return;
            }

            try
            {
                _platform.Disconnect();
                _started = false;
                Publish(AudioRelaySnapshot.Disabled());
            }
            catch (Exception exception)
            {
                PublishFailure(
                    "AUDIO_RELAY_STOP_FAILED",
                    "蓝牙音频连接未能正常停止。",
                    exception,
                    "Stop");
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async ValueTask SearchDevicesCoreAsync(
        string operation,
        CancellationToken cancellationToken)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();
        var previous = Snapshot;
        Publish(previous with
        {
            Status = AudioRelayStatus.Refreshing,
            StatusMessage = operation == "Search"
                ? "正在搜索支持媒体音频的已配对蓝牙设备…"
                : "正在刷新已配对的蓝牙音频设备…",
            ErrorCode = null,
            LastOperation = operation
        });

        try
        {
            var devices = NormalizeDevices(
                await _platform.FindDevicesAsync(cancellationToken).ConfigureAwait(false));
            var selected = devices.FirstOrDefault(device => string.Equals(
                device.Id,
                previous.SelectedDeviceId,
                StringComparison.Ordinal));
            var message = devices.Length == 0
                ? "没有发现可用设备。请先在 Windows 蓝牙设置中完成配对，然后点击“刷新”。"
                : $"已发现 {devices.Length} 台已配对蓝牙音频设备。";

            Publish(new AudioRelaySnapshot(
                AudioRelayStatus.Ready,
                devices,
                selected?.Id,
                selected?.Name,
                message,
                null,
                operation));
        }
        catch (OperationCanceledException)
        {
            if (_started)
            {
                Publish(Snapshot with
                {
                    Status = AudioRelayStatus.Ready,
                    StatusMessage = "Device discovery was canceled. The previous device list is still available.",
                    ErrorCode = null,
                    LastOperation = $"{operation} canceled"
                });
            }

            throw;
        }
        catch (Exception exception)
        {
            PublishFailure(
                "AUDIO_RELAY_DISCOVERY_FAILED",
                "Windows could not enumerate paired Bluetooth audio devices.",
                exception,
                operation);
            throw;
        }
    }

    private async ValueTask ConnectCoreAsync(
        string deviceId,
        CancellationToken cancellationToken)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();
        var selected = Snapshot.Devices.SingleOrDefault(device => string.Equals(
            device.Id,
            deviceId,
            StringComparison.Ordinal));
        if (selected is null)
        {
            throw new InvalidOperationException(
                "所选设备已不可用，请刷新已配对设备列表后重试。");
        }

        var current = Snapshot;
        if (current.Status is AudioRelayStatus.Connecting or AudioRelayStatus.Streaming)
        {
            throw new InvalidOperationException(
                "已有蓝牙音频连接，请先断开当前设备。");
        }

        Publish(current with
        {
            Status = AudioRelayStatus.Connecting,
            SelectedDeviceId = selected.Id,
            SelectedDeviceName = selected.Name,
            StatusMessage = $"Opening Bluetooth media audio from {selected.Name}…",
            ErrorCode = null,
            LastOperation = "Connect"
        });

        try
        {
            await _platform.ConnectAsync(selected.Id, cancellationToken).ConfigureAwait(false);
            var afterConnect = Snapshot;
            if (afterConnect.Status == AudioRelayStatus.Connecting)
            {
                Publish(afterConnect with
                {
                    Status = AudioRelayStatus.Streaming,
                    StatusMessage = $"Receiving media audio from {selected.Name}. PC audio continues through the normal Windows mix.",
                    ErrorCode = null,
                    LastOperation = "Connect"
                });
            }
        }
        catch (OperationCanceledException)
        {
            if (_started)
            {
                Publish(Snapshot with
                {
                    Status = AudioRelayStatus.Ready,
                    StatusMessage = "连接操作已取消，可以再次尝试连接该设备。",
                    ErrorCode = null,
                    LastOperation = "Connect canceled"
                });
            }

            throw;
        }
        catch (Exception exception)
        {
            PublishFailure(
                "AUDIO_RELAY_CONNECTION_FAILED",
                $"Could not receive audio from {selected.Name}.",
                exception,
                "Connect");
            throw;
        }
    }

    private ValueTask DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        cancellationToken.ThrowIfCancellationRequested();
        var current = Snapshot;

        try
        {
            _connectionGeneration++;
            _platform.Disconnect();
            Publish(current with
            {
                Status = AudioRelayStatus.Ready,
                StatusMessage = "蓝牙音频接收已停止，已配对设备仍可用。",
                ErrorCode = null,
                LastOperation = "Disconnect"
            });
        }
        catch (Exception exception)
        {
            PublishFailure(
                "AUDIO_RELAY_DISCONNECT_FAILED",
                "蓝牙音频连接未能正常断开。",
                exception,
                "Disconnect");
            throw;
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask ExecuteSerializedAsync(
        Func<ValueTask> operation,
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnPlatformStateChanged(AudioRelayTransportState state, int generation)
    {
        var current = Snapshot;
        if (!_started)
        {
            return;
        }

        if (state == AudioRelayTransportState.Opened && current.Status == AudioRelayStatus.Connecting)
        {
            Publish(current with
            {
                Status = AudioRelayStatus.Streaming,
                StatusMessage = $"正在接收来自 {current.SelectedDeviceName ?? "该设备"} 的媒体音频。电脑声音仍会通过 Windows 混音输出。",
                ErrorCode = null,
                LastOperation = "Connect"
            });
        }
        else if (state == AudioRelayTransportState.Closed && current.Status is (AudioRelayStatus.Streaming or AudioRelayStatus.Connecting))
        {
            Publish(current with
            {
                Status = AudioRelayStatus.Ready,
                StatusMessage = "设备已关闭蓝牙音频连接，请重新选择设备并连接。",
                ErrorCode = null,
                LastOperation = "Remote disconnect"
            });
        }
    }

    private void PublishFailure(
        string fallbackErrorCode,
        string message,
        Exception exception,
        string operation)
    {
        var errorCode = exception is AudioRelayPlatformException platformException
            ? platformException.ErrorCode
            : fallbackErrorCode;
        Publish(Snapshot with
        {
            Status = AudioRelayStatus.Error,
            StatusMessage = $"{message} {exception.Message}",
            ErrorCode = errorCode,
            LastOperation = operation
        });
    }

    private void Publish(AudioRelaySnapshot snapshot)
    {
        AudioRelaySnapshot published;
        lock (_gate)
        {
            _snapshot = CloneSnapshot(snapshot);
            published = CloneSnapshot(_snapshot);
        }

        SnapshotChanged?.Invoke(published);
    }

    private void EnsureStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException("请先启用音频流转插件。");
        }
    }

    private static AudioRelayDevice[] NormalizeDevices(IEnumerable<AudioRelayDevice> devices)
    {
        return devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(device => device.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static AudioRelaySnapshot CloneSnapshot(AudioRelaySnapshot snapshot) => snapshot with
    {
        Devices = [.. snapshot.Devices]
    };
}
