using System.Windows;
using BluetoothAudioReceiver.Core;
using Microsoft.Win32;

namespace BluetoothAudioReceiver.App;

/// <summary>
/// Keeps the application palette aligned with the Windows app theme, including live switching.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightThemeValueName = "AppsUseLightTheme";

    private readonly System.Windows.Application _application;
    private readonly DiagnosticsReport _diagnostics;
    private bool? _appliedLightTheme;
    private int _disposed;

    public ThemeService(System.Windows.Application application, DiagnosticsReport diagnostics)
    {
        _application = application;
        _diagnostics = diagnostics;
    }

    public void Start()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // A theme switch arrives in the General category.
        if (e.Category == UserPreferenceCategory.General)
        {
            _application.Dispatcher.InvokeAsync(Apply);
        }
    }

    private void Apply()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var light = IsWindowsUsingLightTheme();
        if (_appliedLightTheme == light)
        {
            return;
        }

        try
        {
            var palette = new ResourceDictionary
            {
                Source = new Uri(light ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative),
            };

            var merged = _application.Resources.MergedDictionaries;
            if (merged.Count == 0)
            {
                merged.Add(palette);
            }
            else
            {
                merged[0] = palette;
            }

            _appliedLightTheme = light;
            _diagnostics.Add($"Interface theme set to {(light ? "light" : "dark")}.");
        }
        catch (Exception exception)
        {
            _diagnostics.Add($"Could not apply the interface theme: {exception.Message}");
        }
    }

    /// <summary>Defaults to light, which is also what Windows assumes when the value is absent.</summary>
    private bool IsWindowsUsingLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath, writable: false);
            return key?.GetValue(LightThemeValueName) is not int value || value != 0;
        }
        catch (Exception exception)
        {
            _diagnostics.Add($"Could not read the Windows theme preference: {exception.Message}");
            return true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
