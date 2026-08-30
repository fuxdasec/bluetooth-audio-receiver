using BluetoothAudioReceiver.Core;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace BluetoothAudioReceiver.App;

public sealed record AudioEndpoints(string RenderName);

public sealed class AudioEndpointService
{
    private readonly DiagnosticsReport _diagnostics;

    public AudioEndpointService(DiagnosticsReport diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public async Task<AudioEndpoints> GetDefaultsAsync()
    {
        try
        {
            var renderId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
            var renderName = await ResolveNameAsync(renderId, UiStrings.Get("NoDefaultOutput"));
            _diagnostics.Add($"Default Windows output: '{renderName}'.");
            return new AudioEndpoints(renderName);
        }
        catch (Exception exception)
        {
            // Without an output device (Remote Desktop, no sound card) the query throws instead
            // of returning an empty id; the fallback name keeps the app usable.
            var fallback = UiStrings.Get("NoDefaultOutput");
            _diagnostics.Add($"Could not query the default Windows output: {exception.Message}");
            return new AudioEndpoints(fallback);
        }
    }

    private static async Task<string> ResolveNameAsync(string id, string fallback)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return fallback;
        }

        var device = await DeviceInformation.CreateFromIdAsync(id);
        return string.IsNullOrWhiteSpace(device?.Name) ? fallback : device.Name;
    }
}
