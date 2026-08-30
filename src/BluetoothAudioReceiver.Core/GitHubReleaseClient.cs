using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BluetoothAudioReceiver.Core;

/// <summary>
/// Reads the latest stable release from the GitHub API. Every failure is reported as "no version
/// information" so that a check can never disturb the Bluetooth path.
/// </summary>
public sealed class GitHubReleaseClient : IDisposable
{
    public const string RepositoryUrl = "https://github.com/fuxdasec/bluetooth-audio-receiver";

    public const string ReleasesPageUrl = RepositoryUrl + "/releases/latest";

    /// <summary>This endpoint already excludes prereleases and drafts, so the continuous channel never appears here.</summary>
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/fuxdasec/bluetooth-audio-receiver/releases/latest";

    private readonly DiagnosticsReport _diagnostics;
    private readonly HttpClient _client;

    /// <param name="userAgentVersion">
    /// Identifies the running build to GitHub. Passed in because the Core project has no access to the
    /// application assembly metadata.
    /// </param>
    /// <param name="handler">Overridden by the tests to exercise the failure paths.</param>
    public GitHubReleaseClient(
        DiagnosticsReport diagnostics,
        string? userAgentVersion = null,
        HttpMessageHandler? handler = null)
    {
        _diagnostics = diagnostics;
        // An injected handler belongs to the caller; disposing it here would break the caller's reuse.
        _client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _client.Timeout = TimeSpan.FromSeconds(15);
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        // GitHub rejects requests without a User-Agent.
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"BluetoothAudioReceiver/{(string.IsNullOrWhiteSpace(userAgentVersion) ? "unknown" : userAgentVersion)}");
    }

    public async Task<AppVersion?> GetLatestStableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetAsync(LatestReleaseUrl, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Normal while no stable release has been tagged.
                _diagnostics.Add("Update check: no stable release published yet.");
                return null;
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                _diagnostics.Add("Update check: the anonymous GitHub rate limit was reached.");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _diagnostics.Add($"Update check: GitHub answered {(int)response.StatusCode}.");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("tag_name", out var tag) ||
                tag.ValueKind != JsonValueKind.String ||
                !AppVersion.TryParse(tag.GetString(), out var version))
            {
                _diagnostics.Add("Update check: the release tag could not be read.");
                return null;
            }

            _diagnostics.Add($"Update check: the latest stable release is {version}.");
            return version;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Offline, DNS failure, timeout or malformed payload. None of them are worth an error dialog.
            _diagnostics.Add($"Update check failed: {exception.Message}");
            return null;
        }
    }

    public void Dispose() => _client.Dispose();
}
