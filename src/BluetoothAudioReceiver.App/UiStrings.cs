using System.Globalization;
using System.Resources;
using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.App;

public static class UiStrings
{
    private static readonly ResourceManager Resources = new(
        "BluetoothAudioReceiver.App.Resources.UiStrings",
        typeof(UiStrings).Assembly);

    public static string Get(string name) =>
        Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    public static string Format(string name, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(name), arguments);

    public static string Subtitle => Get(nameof(Subtitle));
    public static string SourceTooltip => Get(nameof(SourceTooltip));
    public static string Connect => Get(nameof(Connect));
    public static string Reconnect => Get(nameof(Reconnect));
    public static string Refresh => Get(nameof(Refresh));
    public static string Diagnostics => Get(nameof(Diagnostics));
    public static string CopyDiagnostics => Get(nameof(CopyDiagnostics));
    public static string StartWithWindows => Get(nameof(StartWithWindows));
    public static string HideToTray => Get(nameof(HideToTray));
    public static string UpdateDownload => Get(nameof(UpdateDownload));
    public static string UpdateDismiss => Get(nameof(UpdateDismiss));
    public static string UpdateNotifications => Get(nameof(UpdateNotifications));
    public static string RepositoryTooltip => Get(nameof(RepositoryTooltip));
    public static string SourceTitle => Get(nameof(SourceTitle));
    public static string PreferencesTitle => Get(nameof(PreferencesTitle));
    public static string SourceLabel => Get(nameof(SourceLabel));
    public static string OutputLabel => Get(nameof(OutputLabel));

    /// <summary>Short label shared by the window, the tray tooltip, and the tray notifications.</summary>
    public static string Describe(ConnectionState state) => state switch
    {
        ConnectionState.Connected => Get("Connected"),
        ConnectionState.Recovering => Get("Reconnecting"),
        ConnectionState.WaitingForDevice => Get("WaitingForDevice"),
        ConnectionState.Disabled => Get("Disabled"),
        _ => Get("PreparingConnection"),
    };
}
