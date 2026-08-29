using System.Globalization;
using ToolBox.PluginSdk;

namespace KeyboardTestPlugin;

public sealed class KeyboardTestPlugin : IKeyboardTestPlugin, IPluginUiProvider
{
    private KeyboardTestSettings _settings = KeyboardTestSettings.Default;
    private KeyboardTestSnapshot _snapshot = KeyboardTestSnapshot.Disabled(KeyboardTestSettings.Default);
    private bool _disposed;

    public string Id => "com.toolbox.keyboard-test";
    public KeyboardTestSnapshot Snapshot => _snapshot;
    public event Action<KeyboardTestSnapshot>? SnapshotChanged;

    public ValueTask StartAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var lease = context.Resources.Acquire(new ResourceKey("keyboard.test.surface"), ResourceAccessMode.Exclusive);
        try
        {
            context.LifetimeScope.Register(lease);
        }
        catch
        {
            lease.Dispose();
            throw;
        }

        _snapshot = _snapshot with { IsEnabled = true, Settings = _settings, LastInput = string.Empty, LastEventAtUtc = null };
        RaiseSnapshotChanged();
        return ValueTask.CompletedTask;
    }

    public ValueTask StopAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        _snapshot = _snapshot with { IsEnabled = false, Settings = _settings };
        RaiseSnapshotChanged();
        return ValueTask.CompletedTask;
    }

    public ValueTask ApplySettingsAsync(KeyboardTestSettings settings, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();
        _settings = settings;
        _snapshot = _snapshot with { Settings = settings };
        RaiseSnapshotChanged();
        return ValueTask.CompletedTask;
    }

    public PluginUiSnapshot GetSnapshot()
    {
        var snapshot = _snapshot;
        return new PluginUiSnapshot(
            string.IsNullOrWhiteSpace(snapshot.LastInput)
                ? "Click this area and press keys or mouse buttons to test input."
                : $"Last input: {snapshot.LastInput}",
            [
                new PluginUiValue("Key events", snapshot.KeyEventCount.ToString(CultureInfo.InvariantCulture)),
                new PluginUiValue("Mouse events", snapshot.MouseEventCount.ToString(CultureInfo.InvariantCulture))
            ],
            [],
            new PluginInputSurface(
                "Keyboard and mouse test surface",
                "Focus this area, then press a key or click to send input to the plugin."));
    }

    public ValueTask<PluginUiSnapshot> ExecuteAsync(
        string actionId,
        string? argument,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException($"Unknown KeyboardMouse action '{actionId}'.");
    }

    public ValueTask<PluginUiSnapshot> HandleInputAsync(
        PluginInputEvent input,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        switch (input.Type)
        {
            case PluginInputEventType.KeyDown:
            case PluginInputEventType.KeyUp:
                if (!string.IsNullOrWhiteSpace(input.Key))
                {
                    ObserveKey(input.Key, input.Type == PluginInputEventType.KeyDown);
                }

                break;
            case PluginInputEventType.MouseDown:
            case PluginInputEventType.MouseUp:
                if (Enum.TryParse<KeyboardTestMouseButton>(input.MouseButton, ignoreCase: true, out var button))
                {
                    ObserveMouse(button, input.Type == PluginInputEventType.MouseDown, input.X, input.Y);
                }

                break;
        }

        return ValueTask.FromResult(GetSnapshot());
    }

    public void ObserveKey(string key, bool isDown)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_snapshot.IsEnabled || string.IsNullOrWhiteSpace(key) || (!isDown && !_settings.IncludeKeyUpEvents))
        {
            return;
        }

        _snapshot = _snapshot with { KeyEventCount = _snapshot.KeyEventCount + 1, LastInput = $"{key} · {(isDown ? "down" : "up")}", LastEventAtUtc = DateTimeOffset.UtcNow };
        RaiseSnapshotChanged();
    }

    public void ObserveMouse(KeyboardTestMouseButton button, bool isDown, int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_snapshot.IsEnabled || !_settings.IncludeMouseEvents)
        {
            return;
        }

        _snapshot = _snapshot with
        {
            MouseEventCount = _snapshot.MouseEventCount + 1,
            LastInput = string.Create(CultureInfo.InvariantCulture, $"{button} · {(isDown ? "down" : "up")} · {x},{y}"),
            LastEventAtUtc = DateTimeOffset.UtcNow
        };
        RaiseSnapshotChanged();
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        _snapshot = _snapshot with { IsEnabled = false };
        SnapshotChanged = null;
        return ValueTask.CompletedTask;
    }

    private void RaiseSnapshotChanged() => SnapshotChanged?.Invoke(_snapshot);
}
