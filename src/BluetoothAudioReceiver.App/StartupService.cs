using BluetoothAudioReceiver.Core;
using Microsoft.Win32;

namespace BluetoothAudioReceiver.App;

public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BluetoothAudioReceiver";
    private readonly DiagnosticsReport _diagnostics;

    public StartupService(DiagnosticsReport diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var registeredCommand = key?.GetValue(ValueName) as string;
            if (registeredCommand is null)
            {
                return false;
            }

            var expectedCommand = GetStartupCommand();
            if (string.Equals(registeredCommand, expectedCommand, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            _diagnostics.Add(
                $"Startup entry points to a different executable; registered='{registeredCommand}', " +
                $"expected='{expectedCommand}'.");
            return false;
        }
        catch (Exception exception)
        {
            _diagnostics.Add($"Could not query startup status: {Describe(exception)}");
            return false;
        }
    }

    /// <summary>
    /// Some system APIs may return an exception with an empty
    /// <see cref="Exception.Message"/>. Include its type and HRESULT in that case.
    /// </summary>
    private static string Describe(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? $"{exception.GetType().Name}; HRESULT=0x{exception.HResult:X8}"
            : exception.Message;

    public (bool Enabled, string Message) SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                    ?? throw new InvalidOperationException("Could not open the current user's startup registry key.");
                key.SetValue(ValueName, GetStartupCommand(), RegistryValueKind.String);
                return (true, UiStrings.Get("StartupEnabled"));
            }

            using var existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            existingKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            return (false, UiStrings.Get("StartupDisabled"));
        }
        catch (Exception exception)
        {
            _diagnostics.Add($"Startup configuration failed: {Describe(exception)}");
            return (IsEnabled(), UiStrings.Get("StartupConfigurationFailed"));
        }
    }

    private static string GetStartupCommand()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("The current executable path is unavailable.");
        }

        return $"\"{executablePath}\" --background";
    }
}
