using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.App;

/// <summary>
/// Schedules the release check away from the user interface thread and reports what it finds.
/// </summary>
public sealed class UpdateService : IAsyncDisposable
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly DiagnosticsReport _diagnostics;
    private readonly GitHubReleaseClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private Task? _loop;
    private int _disposed;

    public UpdateService(DiagnosticsReport diagnostics, GitHubReleaseClient client)
    {
        _diagnostics = diagnostics;
        _client = client;
    }

    /// <summary>Raised with the result of a completed check, including one that found nothing.</summary>
    public event EventHandler<UpdateCheckResult>? Checked;

    public AppVersion? CurrentVersion { get; } = AppVersionInfo.GetCurrentVersion();

    public void Start(Func<AppSettings> settingsAccessor)
    {
        ArgumentNullException.ThrowIfNull(settingsAccessor);
        if (_loop is not null)
        {
            return;
        }

        _loop = Task.Run(() => RunAsync(settingsAccessor, _lifetime.Token));
    }

    private async Task RunAsync(Func<AppSettings> settingsAccessor, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(StartupDelay, cancellationToken);
            using var timer = new PeriodicTimer(CheckInterval);
            var startup = true;
            do
            {
                var settings = settingsAccessor();
                if (settings.UpdateNotificationsEnabled &&
                    (!startup ||
                     UpdatePolicy.ShouldCheck(DateTimeOffset.UtcNow, settings.LastUpdateCheckUtc, CheckInterval)))
                {
                    await CheckAsync(settings, cancellationToken);
                }

                startup = false;
            }
            while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _diagnostics.Add($"The update scheduler stopped: {exception}");
        }
    }

    /// <summary>Runs a check now, ignoring the interval. Used by the tray menu item.</summary>
    public Task<UpdateCheckResult> CheckNowAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return CheckAsync(settings, cancellationToken);
    }

    private async Task<UpdateCheckResult> CheckAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _checkLock.WaitAsync(linked.Token);
        try
        {
            var latest = await _client.GetLatestStableAsync(linked.Token);
            var availability = UpdatePolicy.Evaluate(CurrentVersion, latest, settings.DismissedUpdateVersion);
            var result = new UpdateCheckResult(availability, latest is not null, DateTimeOffset.UtcNow);
            Checked?.Invoke(this, result);
            return result;
        }
        finally
        {
            _checkLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync();
        var loop = _loop;
        _loop = null;
        if (loop is not null)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _client.Dispose();
        _checkLock.Dispose();
        _lifetime.Dispose();
    }
}

/// <param name="Reached">Whether GitHub actually answered with a version.</param>
public sealed record UpdateCheckResult(UpdateAvailability Availability, bool Reached, DateTimeOffset CheckedAtUtc);
