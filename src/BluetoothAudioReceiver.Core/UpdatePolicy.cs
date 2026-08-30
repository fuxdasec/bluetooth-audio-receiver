namespace BluetoothAudioReceiver.Core;

/// <param name="IsNewer">Whether the published version is ahead of the running one.</param>
/// <param name="ShouldNotify">
/// Whether it warrants a tray notification. A version the user dismissed is still reported as
/// available so the window can keep showing it, but it no longer raises a balloon.
/// </param>
public sealed record UpdateAvailability(bool IsNewer, bool ShouldNotify, AppVersion? Latest)
{
    public static readonly UpdateAvailability None = new(false, false, null);
}

public static class UpdatePolicy
{
    public static bool ShouldCheck(DateTimeOffset now, DateTimeOffset? lastCheckUtc, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        if (lastCheckUtc is not { } last)
        {
            return true;
        }

        // A clock moved backwards would otherwise postpone every check indefinitely.
        return last > now || now - last >= interval;
    }

    public static UpdateAvailability Evaluate(AppVersion? current, AppVersion? latest, string? dismissedVersion)
    {
        if (current is null || latest is null || !latest.IsNewerThan(current))
        {
            return UpdateAvailability.None;
        }

        var dismissed = string.Equals(dismissedVersion, latest.ToString(), StringComparison.Ordinal);
        return new UpdateAvailability(IsNewer: true, ShouldNotify: !dismissed, latest);
    }
}
