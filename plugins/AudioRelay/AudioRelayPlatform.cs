using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Media.Audio;
using ToolBox.PluginSdk;

namespace AudioRelayPlugin;

internal enum AudioRelayTransportState
{
    Closed,
    Opened
}

internal interface IAudioRelayPlatform : IDisposable
{
    bool IsSupported { get; }

    event Action<AudioRelayTransportState>? StateChanged;

    ValueTask<AudioRelayDevice[]> FindDevicesAsync(CancellationToken cancellationToken);

    ValueTask ConnectAsync(string deviceId, CancellationToken cancellationToken);

    void Disconnect();
}

internal sealed class AudioRelayPlatformException : InvalidOperationException
{
    public AudioRelayPlatformException(string errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

internal sealed class WindowsAudioRelayPlatform : IAudioRelayPlatform
{
    private readonly object _gate = new();
    private AudioPlaybackConnection? _connection;
    private bool _disposed;

    public bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)
        && ApiInformation.IsTypePresent("Windows.Media.Audio.AudioPlaybackConnection");

    public event Action<AudioRelayTransportState>? StateChanged;

    public async ValueTask<AudioRelayDevice[]> FindDevicesAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();

        var devices = new Dictionary<string, AudioRelayDevice>(StringComparer.Ordinal);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DeviceWatcher watcher;
        try
        {
            watcher = DeviceInformation.CreateWatcher(AudioPlaybackConnection.GetDeviceSelector());
        }
        catch (FileNotFoundException)
        {
            return [];
        }

        TypedEventHandler<DeviceWatcher, DeviceInformation> added = (_, device) =>
        {
            if (!string.IsNullOrWhiteSpace(device.Id))
            {
                devices[device.Id] = new AudioRelayDevice(
                    device.Id,
                    string.IsNullOrWhiteSpace(device.Name) ? "Paired audio device" : device.Name);
            }
        };
        TypedEventHandler<DeviceWatcher, object> completed = (_, _) => completion.TrySetResult();
        TypedEventHandler<DeviceWatcher, object> stopped = (sender, _) =>
        {
            if (sender.Status == DeviceWatcherStatus.Aborted)
            {
                completion.TrySetException(new AudioRelayPlatformException(
                    "AUDIO_RELAY_DISCOVERY_ABORTED",
                    "Windows stopped Bluetooth audio discovery before enumeration completed."));
            }
        };

        watcher.Added += added;
        watcher.EnumerationCompleted += completed;
        watcher.Stopped += stopped;
        using var registration = cancellationToken.Register(() =>
        {
            completion.TrySetCanceled(cancellationToken);
            TryStopWatcher(watcher);
        });

        try
        {
            watcher.Start();
            await completion.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return devices.Values.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }
        finally
        {
            TryStopWatcher(watcher);
            watcher.Added -= added;
            watcher.EnumerationCompleted -= completed;
            watcher.Stopped -= stopped;
        }
    }

    public async ValueTask ConnectAsync(string deviceId, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        Disconnect();

        var connection = AudioPlaybackConnection.TryCreateFromId(deviceId)
            ?? throw new AudioRelayPlatformException(
                "AUDIO_RELAY_DEVICE_UNAVAILABLE",
                "Windows could not create an audio playback connection for this device.");
        lock (_gate)
        {
            _connection = connection;
        }

        try
        {
            await connection.StartAsync();
            cancellationToken.ThrowIfCancellationRequested();
            var result = await connection.OpenAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                throw new AudioRelayPlatformException(
                    GetOpenErrorCode(result.Status),
                    "Windows could not open the Bluetooth audio connection.");
            }

            StateChanged?.Invoke(AudioRelayTransportState.Opened);
        }
        catch
        {
            ReleaseConnection(connection);
            throw;
        }
    }

    public void Disconnect()
    {
        AudioPlaybackConnection? connection;
        lock (_gate)
        {
            connection = _connection;
            _connection = null;
        }

        connection?.Dispose();
        if (connection is not null)
        {
            StateChanged?.Invoke(AudioRelayTransportState.Closed);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disconnect();
        StateChanged = null;
    }

    private void ReleaseConnection(AudioPlaybackConnection connection)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_connection, connection))
            {
                _connection = null;
            }
        }

        connection.Dispose();
    }

    private void EnsureSupported()
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Phone audio relay requires Windows 10 version 2004 (build 19041) or later.");
        }
    }

    private static void TryStopWatcher(DeviceWatcher watcher)
    {
        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch
        {
        }
    }

    private static string GetOpenErrorCode(AudioPlaybackConnectionOpenResultStatus status)
    {
        return status switch
        {
            AudioPlaybackConnectionOpenResultStatus.RequestTimedOut => "AUDIO_RELAY_CONNECTION_TIMEOUT",
            AudioPlaybackConnectionOpenResultStatus.DeniedBySystem => "AUDIO_RELAY_CONNECTION_DENIED",
            _ => "AUDIO_RELAY_CONNECTION_FAILED"
        };
    }
}
