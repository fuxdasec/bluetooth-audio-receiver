using System.Text.Json;
using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"bar-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task FirstRunReturnsDefaults()
    {
        var path = GetPath();
        using var store = new SettingsStore(new DiagnosticsReport(), path);

        var settings = await store.LoadAsync();

        Assert.False(File.Exists(path));
        Assert.Null(settings.TargetDeviceId);
        Assert.Null(settings.TargetDeviceName);
        Assert.True(settings.UpdateNotificationsEnabled);
        Assert.Null(settings.LastUpdateCheckUtc);
        Assert.Null(settings.DismissedUpdateVersion);
    }

    [Fact]
    public async Task LoadsLegacyJsonAndIgnoresRemovedProperties()
    {
        var path = GetPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, """
            {
              "TargetDeviceId": "device-id",
              "TargetDeviceName": "Old phone name",
              "StartWithWindows": true
            }
            """);
        using var store = new SettingsStore(new DiagnosticsReport(), path);

        var settings = await store.LoadAsync();

        Assert.Equal("device-id", settings.TargetDeviceId);
        Assert.Equal("Old phone name", settings.TargetDeviceName);
    }

    [Fact]
    public async Task SettingsFileWrittenWithoutTheDeviceNameStillLoads()
    {
        var path = GetPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, """{ "TargetDeviceId": "device-id" }""");
        using var store = new SettingsStore(new DiagnosticsReport(), path);

        var settings = await store.LoadAsync();

        Assert.Equal("device-id", settings.TargetDeviceId);
        Assert.Null(settings.TargetDeviceName);
    }

    [Fact]
    public async Task UpdateSettingsRoundTrip()
    {
        var path = GetPath();
        using var store = new SettingsStore(new DiagnosticsReport(), path);
        var checkedAt = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        await store.SaveAsync(new AppSettings
        {
            UpdateNotificationsEnabled = false,
            LastUpdateCheckUtc = checkedAt,
            DismissedUpdateVersion = "1.1.0",
        });
        var settings = await store.LoadAsync();

        Assert.False(settings.UpdateNotificationsEnabled);
        Assert.Equal(checkedAt, settings.LastUpdateCheckUtc);
        Assert.Equal("1.1.0", settings.DismissedUpdateVersion);
    }

    [Fact]
    public async Task UpdateNotificationsDefaultToEnabledInAFileThatPredatesThem()
    {
        var path = GetPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, """{ "TargetDeviceId": "device-id" }""");
        using var store = new SettingsStore(new DiagnosticsReport(), path);

        var settings = await store.LoadAsync();

        Assert.True(settings.UpdateNotificationsEnabled);
        Assert.Null(settings.LastUpdateCheckUtc);
        Assert.Null(settings.DismissedUpdateVersion);
    }

    [Fact]
    public async Task DeviceNameRoundTrips()
    {
        var path = GetPath();
        using var store = new SettingsStore(new DiagnosticsReport(), path);

        await store.SaveAsync(new AppSettings { TargetDeviceId = "device-id", TargetDeviceName = "Phone" });
        var settings = await store.LoadAsync();

        Assert.Equal("device-id", settings.TargetDeviceId);
        Assert.Equal("Phone", settings.TargetDeviceName);
    }

    [Fact]
    public async Task InvalidJsonReturnsDefaultsAndAddsDiagnostic()
    {
        var path = GetPath();
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(path, "{invalid");
        var diagnostics = new DiagnosticsReport();
        using var store = new SettingsStore(diagnostics, path);

        var settings = await store.LoadAsync();

        Assert.Null(settings.TargetDeviceId);
        Assert.Contains("Invalid settings file", diagnostics.ToString());
    }

    [Fact]
    public async Task ConcurrentSavesAlwaysProduceCompleteJson()
    {
        var path = GetPath();
        using var store = new SettingsStore(new DiagnosticsReport(), path);

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(index => store.SaveAsync(new AppSettings { TargetDeviceId = $"device-{index}" })));

        await using var stream = File.OpenRead(path);
        var saved = await JsonSerializer.DeserializeAsync<AppSettings>(stream);
        Assert.NotNull(saved?.TargetDeviceId);
        Assert.StartsWith("device-", saved.TargetDeviceId);
        Assert.False(File.Exists(path + ".tmp"));
    }

    private string GetPath() => Path.Combine(_directory, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
