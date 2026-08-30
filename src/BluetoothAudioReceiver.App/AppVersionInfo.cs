using System.Reflection;
using System.Runtime.InteropServices;
using BluetoothAudioReceiver.Core;
using Microsoft.Win32;

namespace BluetoothAudioReceiver.App;

public static class AppVersionInfo
{
    public static string GetDisplayVersion()
    {
        var assembly = typeof(AppVersionInfo).Assembly;
        var assemblyVersion = assembly.GetName().Version?.ToString(3) ?? "unknown";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assemblyVersion;
        // Dev builds append "+<commit-sha>"; like AppVersion.TryParse, the display drops build
        // metadata because it does not order.
        var metadataIndex = informationalVersion.IndexOf('+');
        if (metadataIndex >= 0)
        {
            informationalVersion = informationalVersion[..metadataIndex];
        }

        return $"v{informationalVersion}";
    }

    /// <summary>
    /// The running version in comparable form. Falls back to the assembly version when the
    /// informational version carries something the parser does not recognise.
    /// </summary>
    public static AppVersion? GetCurrentVersion()
    {
        var assembly = typeof(AppVersionInfo).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (AppVersion.TryParse(informationalVersion, out var version))
        {
            return version;
        }

        return AppVersion.TryParse(assembly.GetName().Version?.ToString(3), out var fallback)
            ? fallback
            : null;
    }

    public static string GetStartupDescription()
    {
        var assembly = typeof(AppVersionInfo).Assembly;
        var assemblyVersion = assembly.GetName().Version?.ToString() ?? "unknown";
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? assemblyVersion;

        return $"Application started: version={informationalVersion}; assembly={assemblyVersion}; " +
               $"architecture={RuntimeInformation.ProcessArchitecture}; " +
               $"Windows={GetWindowsVersion()}.";
    }

    private static string GetWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var displayVersion = key?.GetValue("DisplayVersion") as string;
            var build = key?.GetValue("CurrentBuildNumber") as string;
            var revision = key?.GetValue("UBR");
            if (!string.IsNullOrWhiteSpace(build) && revision is not null)
            {
                return string.IsNullOrWhiteSpace(displayVersion)
                    ? $"{build}.{revision}"
                    : $"{displayVersion} build {build}.{revision}";
            }
        }
        catch
        {
            // Environment.OSVersion is a safe fallback if registry metadata is unavailable.
        }

        return Environment.OSVersion.Version.ToString();
    }
}
