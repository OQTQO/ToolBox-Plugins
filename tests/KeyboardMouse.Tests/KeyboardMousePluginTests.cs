using KeyboardTestPlugin;
using ToolBox.PluginSdk;
using Xunit;

namespace KeyboardMouse.Tests;

public sealed class KeyboardMousePluginTests
{
    [Fact]
    public async Task StartAcquiresExclusiveSurfaceAndPublishesEnabledState()
    {
        await using var plugin = new KeyboardTestPlugin.KeyboardTestPlugin();
        using var context = new TestPluginContext(plugin.Id);

        await plugin.StartAsync(context, CancellationToken.None);

        Assert.True(plugin.Snapshot.IsEnabled);
        Assert.Equal("keyboard.test.surface", context.Resources.LastKey?.Value);
        Assert.Equal(ResourceAccessMode.Exclusive, context.Resources.LastAccessMode);
        Assert.NotNull(plugin.GetSnapshot().InputSurface);
    }

    [Fact]
    public async Task InputHonorsSettingsAndUpdatesSnapshot()
    {
        await using var plugin = new KeyboardTestPlugin.KeyboardTestPlugin();
        using var context = new TestPluginContext(plugin.Id);
        await plugin.StartAsync(context, CancellationToken.None);

        plugin.ObserveKey("A", isDown: true);
        plugin.ObserveKey("A", isDown: false);
        plugin.ObserveMouse(KeyboardTestMouseButton.Left, isDown: true, x: 12, y: 34);

        Assert.Equal(1, plugin.Snapshot.KeyEventCount);
        Assert.Equal(1, plugin.Snapshot.MouseEventCount);
        Assert.Contains("12,34", plugin.Snapshot.LastInput, StringComparison.Ordinal);

        await plugin.ApplySettingsAsync(
            new KeyboardTestSettings(IncludeKeyUpEvents: true, IncludeMouseEvents: false),
            CancellationToken.None);
        plugin.ObserveKey("B", isDown: false);
        plugin.ObserveMouse(KeyboardTestMouseButton.Right, isDown: true, x: 1, y: 2);

        Assert.Equal(2, plugin.Snapshot.KeyEventCount);
        Assert.Equal(1, plugin.Snapshot.MouseEventCount);
        Assert.Contains("B", plugin.Snapshot.LastInput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenericUiInputReturnsUpdatedCounters()
    {
        await using var plugin = new KeyboardTestPlugin.KeyboardTestPlugin();
        using var context = new TestPluginContext(plugin.Id);
        await plugin.StartAsync(context, CancellationToken.None);

        var snapshot = await plugin.HandleInputAsync(
            new PluginInputEvent(PluginInputEventType.KeyDown, Key: "Enter"),
            CancellationToken.None);

        Assert.Equal("1", Assert.Single(snapshot.Values, value => value.Label == "Key events").Value);
        Assert.Contains("Enter", snapshot.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopDisablesInputObservation()
    {
        await using var plugin = new KeyboardTestPlugin.KeyboardTestPlugin();
        using var context = new TestPluginContext(plugin.Id);
        await plugin.StartAsync(context, CancellationToken.None);
        await plugin.StopAsync(CancellationToken.None);

        plugin.ObserveKey("A", isDown: true);

        Assert.False(plugin.Snapshot.IsEnabled);
        Assert.Equal(0, plugin.Snapshot.KeyEventCount);
    }

    private sealed class TestPluginContext : IPluginContext, IDisposable
    {
        private readonly CancellationTokenSource _lifetime = new();

        public TestPluginContext(string pluginId)
        {
            PluginId = pluginId;
            LifetimeScope = new TestLifetimeScope(_lifetime.Token);
            Resources = new TestResourceManager(pluginId);
        }

        public string PluginId { get; }
        public CancellationToken LifetimeToken => _lifetime.Token;
        public IPluginLifetimeScope LifetimeScope { get; }
        public TestResourceManager Resources { get; }
        IResourceManager IPluginContext.Resources => Resources;
        public IServiceBroker Services { get; } = new UnsupportedServiceBroker();

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
        public void Track(Task backgroundTask) { }

        public IDisposable Register(IDisposable resource)
        {
            _resources.Add(resource);
            return resource;
        }

        public IDisposable Register(IAsyncDisposable resource)
        {
            var registration = new AsyncRegistration(resource);
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

    private sealed class TestResourceLease(ResourceKey key, ResourceAccessMode accessMode, string ownerPluginId) : IResourceLease
    {
        public ResourceKey Key { get; } = key;
        public ResourceAccessMode AccessMode { get; } = accessMode;
        public string OwnerPluginId { get; } = ownerPluginId;
        public bool IsReleased { get; private set; }
        public void Dispose() => IsReleased = true;
    }

    private sealed class UnsupportedServiceBroker : IServiceBroker
    {
        public ValueTask<IServiceLease<T>> AcquireAsync<T>(string serviceKey, CancellationToken cancellationToken = default)
            where T : class => throw new NotSupportedException();
    }

    private sealed class AsyncRegistration(IAsyncDisposable resource) : IDisposable
    {
        public void Dispose() => resource.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class CleanupRegistration(Func<CancellationToken, ValueTask> cleanup) : IDisposable
    {
        public void Dispose() => cleanup(CancellationToken.None).AsTask().GetAwaiter().GetResult();
    }
}
