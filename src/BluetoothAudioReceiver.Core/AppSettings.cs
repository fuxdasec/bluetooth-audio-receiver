namespace BluetoothAudioReceiver.Core;

public sealed record AppSettings
{
    public string? TargetDeviceId { get; init; }

    /// <summary>
    /// Remembered so the tray can name the source before the device watcher enumerates it.
    /// </summary>
    public string? TargetDeviceName { get; init; }

    public bool UpdateNotificationsEnabled { get; init; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    /// <summary>The published version the user asked not to be reminded about.</summary>
    public string? DismissedUpdateVersion { get; init; }
}
