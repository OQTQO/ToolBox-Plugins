using AudioRelayPlugin;
using ToolBox.PluginSdk;
using Xunit;

namespace AudioRelay.Tests;

public sealed class AudioRelayPluginTests
{
    [Fact]
    public async Task WindowsPlatformProbesPairedSourcesWithoutOpeningAConnection()
    {
        using var platform = new WindowsAudioRelayPlatform();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        Assert.True(platform.IsSupported);

        var devices = await platform.FindDevicesAsync(timeout.Token);

        Assert.NotNull(devices);
        Assert.All(devices, device =>
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Id));
            Assert.False(string.IsNullOrWhiteSpace(device.Name));
        });
    }

    [Fact]
    public async Task PluginDiscoversConnectsDisconnectsAndStopsCleanly()
    {
        var platform = new FakeAudioRelayPlatform(
            [new AudioRelayDevice("phone-1", "Pixel Test Phone")]);
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);
        var snapshots = new List<AudioRelaySnapshot>();
        plugin.SnapshotChanged += snapshots.Add;

        await plugin.StartAsync(context, CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Ready, plugin.Snapshot.Status);
        var device = Assert.Single(plugin.Snapshot.Devices);
        Assert.Equal("Pixel Test Phone", device.Name);
        Assert.Equal("audio.bluetooth.a2dp-sink", context.Resources.LastKey?.Value);
        Assert.Equal(ResourceAccessMode.Exclusive, context.Resources.LastAccessMode);

        await plugin.ConnectAsync(device.Id, CancellationToken.None);

        Assert.Equal("phone-1", platform.ConnectedDeviceId);
        Assert.Equal(AudioRelayStatus.Streaming, plugin.Snapshot.Status);
        Assert.Contains("Windows mix", plugin.Snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);

        await plugin.DisconnectAsync(CancellationToken.None);

        Assert.Null(platform.ConnectedDeviceId);
        Assert.Equal(AudioRelayStatus.Ready, plugin.Snapshot.Status);

        await plugin.StopAsync(CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Disabled, plugin.Snapshot.Status);
        Assert.Contains(snapshots, snapshot => snapshot.Status == AudioRelayStatus.Refreshing);
        Assert.Contains(snapshots, snapshot => snapshot.Status == AudioRelayStatus.Streaming);
    }

    [Fact]
    public async Task UiProviderExposesSearchRefreshPerDeviceConnectAndDisconnectActions()
    {
        var platform = new FakeAudioRelayPlatform(
        [
            new AudioRelayDevice("phone-2", "Zeta Phone"),
            new AudioRelayDevice("phone-1", "Alpha Phone")
        ]);
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);

        var readyActions = plugin.GetSnapshot().Actions;
        Assert.Contains(readyActions, action => action.Id == "search");
        Assert.Contains(readyActions, action => action.Id == "refresh");
        var connectAction = Assert.Single(
            readyActions,
            action => action.Id == "connect" && action.Argument == "phone-1");

        await plugin.ExecuteAsync("search", null, CancellationToken.None);
        await plugin.ExecuteAsync(connectAction.Id, connectAction.Argument, CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Streaming, plugin.Snapshot.Status);
        Assert.Contains(plugin.GetSnapshot().Actions, action => action.Id == "disconnect");

        await plugin.ExecuteAsync("disconnect", null, CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Ready, plugin.Snapshot.Status);
        Assert.Contains(plugin.GetSnapshot().Actions, action => action.Id == "refresh");
        Assert.Equal(2, platform.DiscoveryCount);
    }

    [Fact]
    public async Task SearchNormalizesDevicesAndPreservesThenClearsSelection()
    {
        var platform = new FakeAudioRelayPlatform(
        [
            new AudioRelayDevice("phone-2", "Zeta Phone"),
            new AudioRelayDevice("phone-1", "Alpha Phone"),
            new AudioRelayDevice("phone-1", "Duplicate Phone")
        ]);
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);

        Assert.Equal(["Alpha Phone", "Zeta Phone"], plugin.Snapshot.Devices.Select(device => device.Name));
        await plugin.ConnectAsync("phone-1", CancellationToken.None);
        await plugin.DisconnectAsync(CancellationToken.None);

        platform.Devices =
        [
            new AudioRelayDevice("phone-1", "Alpha Phone"),
            new AudioRelayDevice("phone-3", "New Phone")
        ];
        await plugin.RefreshDevicesAsync(CancellationToken.None);
        Assert.Equal("phone-1", plugin.Snapshot.SelectedDeviceId);

        platform.Devices = [new AudioRelayDevice("phone-3", "New Phone")];
        await plugin.SearchDevicesAsync(CancellationToken.None);
        Assert.Null(plugin.Snapshot.SelectedDeviceId);
    }

    [Fact]
    public async Task DiscoveryFailureDuringStartLeavesPluginRunningAndRetryable()
    {
        var platform = new FakeAudioRelayPlatform([new AudioRelayDevice("phone-1", "Test Phone")])
        {
            DiscoveryException = new AudioRelayPlatformException(
                "AUDIO_RELAY_DISCOVERY_DENIED",
                "Bluetooth discovery was denied by Windows.")
        };
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Error, plugin.Snapshot.Status);
        Assert.Equal("AUDIO_RELAY_DISCOVERY_DENIED", plugin.Snapshot.ErrorCode);
        Assert.Contains(plugin.GetSnapshot().Actions, action => action.Id == "search");

        platform.DiscoveryException = null;
        await plugin.SearchDevicesAsync(CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Ready, plugin.Snapshot.Status);
        Assert.Single(plugin.Snapshot.Devices);
    }

    [Fact]
    public async Task CancelingSearchStopsTheUnderlyingDiscovery()
    {
        var platform = new FakeAudioRelayPlatform([new AudioRelayDevice("phone-1", "Test Phone")]);
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);
        await plugin.StartAsync(context, CancellationToken.None);

        platform.BlockDiscoveryUntilCanceled = true;
        using var cancellation = new CancellationTokenSource();
        var searchTask = plugin.SearchDevicesAsync(cancellation.Token).AsTask();
        await platform.DiscoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => searchTask);
        Assert.Equal(0, platform.ActiveDiscoveryCount);
        Assert.Equal(AudioRelayStatus.Ready, plugin.Snapshot.Status);
    }

    [Fact]
    public async Task UnsupportedWindowsStateIsVisibleWithoutDiscovery()
    {
        var platform = new FakeAudioRelayPlatform([]) { IsSupported = false };
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);

        Assert.Equal(AudioRelayStatus.Unsupported, plugin.Snapshot.Status);
        Assert.Equal("AUDIO_RELAY_WINDOWS_UNSUPPORTED", plugin.Snapshot.ErrorCode);
        Assert.Equal(0, platform.DiscoveryCount);
    }

    [Fact]
    public async Task ConnectionFailurePreservesPlatformErrorCodeAndAllowsRetry()
    {
        var platform = new FakeAudioRelayPlatform([new AudioRelayDevice("phone-1", "Test Phone")])
        {
            ConnectException = new AudioRelayPlatformException(
                "AUDIO_RELAY_CONNECTION_TIMEOUT",
                "The Bluetooth connection timed out.")
        };
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);

        await Assert.ThrowsAsync<AudioRelayPlatformException>(
            () => plugin.ConnectAsync("phone-1", CancellationToken.None).AsTask());

        Assert.Equal(AudioRelayStatus.Error, plugin.Snapshot.Status);
        Assert.Equal("AUDIO_RELAY_CONNECTION_TIMEOUT", plugin.Snapshot.ErrorCode);

        platform.ConnectException = null;
        await plugin.ConnectAsync("phone-1", CancellationToken.None);
        Assert.Equal(AudioRelayStatus.Streaming, plugin.Snapshot.Status);
    }

    [Fact]
    public async Task DisconnectFailurePreservesErrorStateInsteadOfReportingReady()
    {
        var platform = new FakeAudioRelayPlatform([new AudioRelayDevice("phone-1", "Test Phone")]);
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);
        await plugin.ConnectAsync("phone-1", CancellationToken.None);
        platform.DisconnectException = new AudioRelayPlatformException(
            "AUDIO_RELAY_DISCONNECT_FAILED",
            "The connection could not be closed.");

        await Assert.ThrowsAsync<AudioRelayPlatformException>(
            () => plugin.DisconnectAsync(CancellationToken.None).AsTask());

        Assert.Equal(AudioRelayStatus.Error, plugin.Snapshot.Status);
        Assert.Equal("AUDIO_RELAY_DISCONNECT_FAILED", plugin.Snapshot.ErrorCode);
        platform.DisconnectException = null;
    }

    [Fact]
    public async Task PhoneClosingTheTransportReturnsPluginToReady()
    {
        var platform = new FakeAudioRelayPlatform(
            [new AudioRelayDevice("phone-2", "Android Phone")]);
        await using var plugin = new AudioRelayPlugin.AudioRelayPlugin(platform);
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);
        await plugin.ConnectAsync("phone-2", CancellationToken.None);

        platform.CloseFromPhone();

        Assert.Equal(AudioRelayStatus.Ready, plugin.Snapshot.Status);
        Assert.Contains("closed", plugin.Snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeAudioRelayPlatform(AudioRelayDevice[] devices) : IAudioRelayPlatform
    {
        public AudioRelayDevice[] Devices { get; set; } = devices;

        public bool IsSupported { get; set; } = true;

        public Exception? DiscoveryException { get; set; }

        public Exception? ConnectException { get; set; }

        public Exception? DisconnectException { get; set; }

        public bool BlockDiscoveryUntilCanceled { get; set; }

        public string? ConnectedDeviceId { get; private set; }

        public int DiscoveryCount { get; private set; }

        public int ActiveDiscoveryCount { get; private set; }

        public TaskCompletionSource DiscoveryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<AudioRelayTransportState>? StateChanged;

        public async ValueTask<AudioRelayDevice[]> FindDevicesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DiscoveryCount++;
            ActiveDiscoveryCount++;
            try
            {
                if (DiscoveryException is not null)
                {
                    throw DiscoveryException;
                }

                if (BlockDiscoveryUntilCanceled)
                {
                    DiscoveryStarted.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                return Devices.ToArray();
            }
            finally
            {
                ActiveDiscoveryCount--;
            }
        }

        public ValueTask ConnectAsync(string deviceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ConnectException is not null)
            {
                throw ConnectException;
            }

            ConnectedDeviceId = deviceId;
            StateChanged?.Invoke(AudioRelayTransportState.Opened);
            return ValueTask.CompletedTask;
        }

        public void Disconnect()
        {
            if (DisconnectException is not null)
            {
                throw DisconnectException;
            }

            ConnectedDeviceId = null;
        }

        public void CloseFromPhone()
        {
            ConnectedDeviceId = null;
            StateChanged?.Invoke(AudioRelayTransportState.Closed);
        }

        public void Dispose()
        {
            ConnectedDeviceId = null;
            StateChanged = null;
        }
    }

    private sealed class TestPluginContext : IPluginContext, IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();

        public TestPluginContext(string pluginId)
        {
            PluginId = pluginId;
            LifetimeScope = new TestLifetimeScope(_lifetime.Token);
            Resources = new TestResourceManager(pluginId);
            Services = new TestServiceBroker();
        }

        public string PluginId { get; }

        public CancellationToken LifetimeToken => _lifetime.Token;

        public IPluginLifetimeScope LifetimeScope { get; }

        public TestResourceManager Resources { get; }

        IResourceManager IPluginContext.Resources => Resources;

        public IServiceBroker Services { get; }

        public void Dispose()
        {
            _lifetime.Cancel();
            ((TestLifetimeScope)LifetimeScope).Dispose();
            _lifetime.Dispose();
        }
    }

    private sealed class TestLifetimeScope(CancellationToken lifetimeToken) : IPluginLifetimeScope, IDisposable
    {
        private readonly List<IDisposable> _resources = [];

        public CancellationToken LifetimeToken { get; } = lifetimeToken;

        public bool IsStopping => LifetimeToken.IsCancellationRequested;

        public void Track(Task backgroundTask)
        {
        }

        public IDisposable Register(IDisposable resource)
        {
            _resources.Add(resource);
            return resource;
        }

        public IDisposable Register(IAsyncDisposable resource)
        {
            var registration = new AsyncDisposableRegistration(resource);
            _resources.Add(registration);
            return registration;
        }

        public IDisposable Register(Func<CancellationToken, ValueTask> cleanup)
        {
            var registration = new CleanupRegistration(cleanup);
            _resources.Add(registration);
            return registration;
        }

        public void Dispose()
        {
            for (var index = _resources.Count - 1; index >= 0; index--)
            {
                _resources[index].Dispose();
            }

            _resources.Clear();
        }
    }

    private sealed class TestResourceManager(string ownerPluginId) : IResourceManager
    {
        public ResourceKey? LastKey { get; private set; }

        public ResourceAccessMode? LastAccessMode { get; private set; }

        public IResourceLease Acquire(ResourceKey key, ResourceAccessMode accessMode)
        {
            LastKey = key;
            LastAccessMode = accessMode;
            return new TestResourceLease(key, accessMode, ownerPluginId);
        }
    }

    private sealed class TestResourceLease(
        ResourceKey key,
        ResourceAccessMode accessMode,
        string ownerPluginId) : IResourceLease
    {
        public ResourceKey Key { get; } = key;

        public ResourceAccessMode AccessMode { get; } = accessMode;

        public string OwnerPluginId { get; } = ownerPluginId;

        public bool IsReleased { get; private set; }

        public void Dispose() => IsReleased = true;
    }

    private sealed class TestServiceBroker : IServiceBroker
    {
        public ValueTask<IServiceLease<T>> AcquireAsync<T>(
            string serviceKey,
            CancellationToken cancellationToken = default)
            where T : class
        {
            throw new NotSupportedException();
        }
    }

    private sealed class AsyncDisposableRegistration(IAsyncDisposable resource) : IDisposable
    {
        public void Dispose() => resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class CleanupRegistration(Func<CancellationToken, ValueTask> cleanup) : IDisposable
    {
        public void Dispose() => cleanup(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }
}
