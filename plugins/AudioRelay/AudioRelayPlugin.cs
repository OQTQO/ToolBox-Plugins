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
                    "This PC needs Windows 10 version 2004 or later for Bluetooth audio receiving.",
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
        var canSearch = _started && snapshot.Status is (AudioRelayStatus.Ready or AudioRelayStatus.Error);
        if (canSearch)
        {
            actions.Add(new PluginUiAction(
                "search",
                "Search paired phones",
                Description: "Run a complete Windows Bluetooth audio device discovery."));
            actions.Add(new PluginUiAction(
                "refresh",
                "Refresh device list",
                Description: "Discover paired phones again and update the current list."));
            actions.AddRange(snapshot.Devices.Select(device => new PluginUiAction(
                "connect",
                $"Receive from {device.Name}",
                device.Id,
                Description: "Open an A2DP media audio connection to this phone.")));
        }

        if (_started && snapshot.Status is (AudioRelayStatus.Connecting or AudioRelayStatus.Streaming))
        {
            actions.Add(new PluginUiAction(
                "disconnect",
                "Stop receiving",
                Description: "Close the current phone audio connection."));
        }

        return new PluginUiSnapshot(
            snapshot.StatusMessage,
            [
                new PluginUiValue("Route", snapshot.Status.ToString()),
                new PluginUiValue("Selected phone", snapshot.SelectedDeviceName ?? "None"),
                new PluginUiValue("Paired sources", snapshot.Devices.Length.ToString(CultureInfo.InvariantCulture)),
                new PluginUiValue("Last operation", snapshot.LastOperation ?? "None")
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
            case "search":
                await SearchDevicesAsync(cancellationToken).ConfigureAwait(false);
                break;
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
                _started = false;
                PublishFailure(
                    "AUDIO_RELAY_STOP_FAILED",
                    "Phone audio receiving could not be stopped cleanly.",
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
                ? "Searching for paired phones that support Bluetooth media audio…"
                : "Refreshing paired Bluetooth audio devices…",
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
                ? "No paired phone is available. Pair it in Windows Bluetooth settings, then search again."
                : $"Found {devices.Length} paired Bluetooth audio source(s).";

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
                "The selected phone is no longer available. Search or refresh the paired device list.");
        }

        var current = Snapshot;
        if (current.Status is AudioRelayStatus.Connecting or AudioRelayStatus.Streaming)
        {
            throw new InvalidOperationException(
                "A phone audio connection is already active. Disconnect it before connecting another phone.");
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
                    StatusMessage = "The connection attempt was canceled. The phone remains available to connect again.",
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
            _platform.Disconnect();
            Publish(current with
            {
                Status = AudioRelayStatus.Ready,
                StatusMessage = "Phone audio receiving stopped. The paired device remains available.",
                ErrorCode = null,
                LastOperation = "Disconnect"
            });
        }
        catch (Exception exception)
        {
            PublishFailure(
                "AUDIO_RELAY_DISCONNECT_FAILED",
                "Phone audio receiving could not be stopped cleanly.",
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

    private void OnPlatformStateChanged(AudioRelayTransportState state)
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
                StatusMessage = $"Receiving media audio from {current.SelectedDeviceName ?? "the phone"}. PC audio remains in the Windows mix.",
                ErrorCode = null,
                LastOperation = "Connect"
            });
        }
        else if (state == AudioRelayTransportState.Closed && current.Status is (AudioRelayStatus.Streaming or AudioRelayStatus.Connecting))
        {
            Publish(current with
            {
                Status = AudioRelayStatus.Ready,
                StatusMessage = "The phone closed the Bluetooth audio connection. Select it and connect again.",
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
            throw new InvalidOperationException("Enable the Phone Audio Relay plugin before using it.");
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
