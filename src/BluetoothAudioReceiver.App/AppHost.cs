using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.App;

public sealed class AppHost : IAsyncDisposable
{
    private readonly BluetoothAudioController _controller;
    private readonly AudioEndpointService _endpointService;
    private readonly SettingsStore _settingsStore;
    private readonly StartupService _startupService;
    private readonly UpdateService _updateService;

    /// <summary>
    /// Settings are read, modified and saved from the user interface thread and from the update
    /// loop. Without this gate the two interleave and the loser's fields are dropped from memory and
    /// from settings.json, which loses the remembered source.
    /// </summary>
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private int _autoConnectRunning;
    private int _disposed;

    public AppHost(
        BluetoothAudioController controller,
        AudioEndpointService endpointService,
        SettingsStore settingsStore,
        StartupService startupService,
        UpdateService updateService,
        DiagnosticsReport diagnostics)
    {
        _controller = controller;
        _endpointService = endpointService;
        _settingsStore = settingsStore;
        _startupService = startupService;
        _updateService = updateService;
        Diagnostics = diagnostics;

        _controller.DevicesChanged += ControllerOnDevicesChanged;
        _updateService.Checked += UpdateServiceOnChecked;
    }

    public event EventHandler? SettingsChanged;
    public event EventHandler? EndpointsChanged;
    public event EventHandler? DevicesChanged;
    public event EventHandler<UpdateCheckResult>? UpdateChecked;

    public DiagnosticsReport Diagnostics { get; }
    public ConnectionStateMachine ConnectionState => _controller.State;
    public IReadOnlyList<DeviceRecord> Devices => _controller.Devices;
    public AppSettings Settings { get; private set; } = new();
    public AudioEndpoints Endpoints { get; private set; } = new("Loading...");
    public bool StartWithWindowsEnabled { get; private set; }

    /// <summary>The published version when it is ahead of the running one, otherwise null.</summary>
    public AppVersion? AvailableUpdate { get; private set; }

    public static string ReleasesPageUrl => GitHubReleaseClient.ReleasesPageUrl;

    public static string RepositoryUrl => GitHubReleaseClient.RepositoryUrl;

    public async Task InitializeAsync()
    {
        Settings = await _settingsStore.LoadAsync();
        Endpoints = await _endpointService.GetDefaultsAsync();
        StartWithWindowsEnabled = _startupService.IsEnabled();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        EndpointsChanged?.Invoke(this, EventArgs.Empty);
        _controller.StartWatching();
        _updateService.Start(() => Settings);
    }

    public Task<UpdateCheckResult> CheckForUpdatesAsync() => _updateService.CheckNowAsync(Settings);

    /// <summary>Stops notifying about this version. The window keeps reporting it.</summary>
    public Task DismissUpdateAsync(AppVersion version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return MutateSettingsAsync(current => current with { DismissedUpdateVersion = version.ToString() });
    }

    public Task SetUpdateNotificationsAsync(bool enabled) =>
        MutateSettingsAsync(current => current with { UpdateNotificationsEnabled = enabled });

    private void UpdateServiceOnChecked(object? sender, UpdateCheckResult result)
    {
        AvailableUpdate = result.Availability.IsNewer ? result.Availability.Latest : null;
        UpdateChecked?.Invoke(this, result);
        if (result.Reached)
        {
            _ = RecordCheckTimeAsync(result.CheckedAtUtc);
        }
    }

    private async Task RecordCheckTimeAsync(DateTimeOffset checkedAtUtc)
    {
        try
        {
            await MutateSettingsAsync(current => current with { LastUpdateCheckUtc = checkedAtUtc });
        }
        catch (Exception exception)
        {
            Diagnostics.Add($"Could not record the update check time: {exception.Message}");
        }
    }

    public async Task SelectDeviceAsync(DeviceRecord device)
    {
        ArgumentNullException.ThrowIfNull(device);
        await MutateSettingsAsync(current => current with
        {
            TargetDeviceId = device.Id,
            TargetDeviceName = device.Name,
        });
        await _controller.SelectAndConnectAsync(device);
    }

    /// <summary>Serialises every read, modify and save of <see cref="Settings"/>.</summary>
    private async Task MutateSettingsAsync(Func<AppSettings, AppSettings> mutate)
    {
        await _settingsGate.WaitAsync();
        try
        {
            Settings = mutate(Settings);
            await _settingsStore.SaveAsync(Settings);
        }
        finally
        {
            _settingsGate.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task ReconnectAsync() => _controller.ReconnectAsync();

    public async Task RefreshEndpointsAsync()
    {
        Endpoints = await _endpointService.GetDefaultsAsync();
        EndpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    public (bool Enabled, string Message) SetStartWithWindows(bool enabled)
    {
        var result = _startupService.SetEnabled(enabled);
        StartWithWindowsEnabled = result.Enabled;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private void ControllerOnDevicesChanged(object? sender, EventArgs e)
    {
        DevicesChanged?.Invoke(this, EventArgs.Empty);
        if (string.IsNullOrWhiteSpace(Settings.TargetDeviceId))
        {
            return;
        }

        var target = Devices.FirstOrDefault(device => device.Id == Settings.TargetDeviceId);
        if (target is null || Interlocked.Exchange(ref _autoConnectRunning, 1) != 0)
        {
            return;
        }

        _ = AutoConnectAsync(target);
    }

    private async Task AutoConnectAsync(DeviceRecord target)
    {
        try
        {
            if (ConnectionState.Snapshot.State != BluetoothAudioReceiver.Core.ConnectionState.Connected)
            {
                await _controller.SelectAndConnectAsync(target);
            }
        }
        catch (Exception exception)
        {
            Diagnostics.Add($"Automatic connection failed: {exception}");
        }
        finally
        {
            Interlocked.Exchange(ref _autoConnectRunning, 0);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _controller.DevicesChanged -= ControllerOnDevicesChanged;
        _updateService.Checked -= UpdateServiceOnChecked;
        try
        {
            try
            {
                await _updateService.DisposeAsync();
            }
            finally
            {
                // The controller owns the watchers and the AudioPlaybackConnection; it must be
                // released even when the update service fails to stop cleanly.
                await _controller.DisposeAsync();
            }
        }
        finally
        {
            _settingsStore.Dispose();
            _settingsGate.Dispose();
        }
    }
}
