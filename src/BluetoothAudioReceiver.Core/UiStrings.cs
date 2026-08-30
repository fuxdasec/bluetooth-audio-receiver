using System.Globalization;
using System.Resources;

namespace BluetoothAudioReceiver.Core;

internal static class UiStrings
{
    private static readonly ResourceManager Resources = new(
        "BluetoothAudioReceiver.Core.Resources.UiStrings",
        typeof(UiStrings).Assembly);

    public static string Get(string name) =>
        Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;

    public static string Format(string name, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(name), arguments);
}
