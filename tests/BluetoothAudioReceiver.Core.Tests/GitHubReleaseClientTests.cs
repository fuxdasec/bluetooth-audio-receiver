using System.Net;
using System.Text;
using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class GitHubReleaseClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = (request, _) => Task.FromResult(responder(request));

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) =>
            _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return _responder(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static async Task<(AppVersion? Version, string Log)> RunAsync(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var diagnostics = new DiagnosticsReport();
        using var client = new GitHubReleaseClient(diagnostics, "1.0.0", new StubHandler(responder));

        var version = await client.GetLatestStableAsync();

        return (version, diagnostics.ToString());
    }

    [Fact]
    public async Task ReadsTheTagOfTheLatestRelease()
    {
        var (version, log) = await RunAsync(_ => Json(HttpStatusCode.OK, """
            { "tag_name": "v1.4.2", "html_url": "https://example.invalid/attacker" }
            """));

        Assert.Equal("1.4.2", version?.ToString());
        Assert.Contains("1.4.2", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFoundMeansNoReleaseHasBeenPublished()
    {
        var (version, log) = await RunAsync(_ => Json(HttpStatusCode.NotFound, """{ "message": "Not Found" }"""));

        Assert.Null(version);
        Assert.Contains("no stable release", log, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RateLimitingIsReportedWithoutThrowing(HttpStatusCode status)
    {
        var (version, log) = await RunAsync(_ => Json(status, """{ "message": "API rate limit exceeded" }"""));

        Assert.Null(version);
        Assert.Contains("rate limit", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OtherStatusCodesAreReportedWithoutThrowing()
    {
        var (version, log) = await RunAsync(_ => Json(HttpStatusCode.InternalServerError, "{}"));

        Assert.Null(version);
        Assert.Contains("500", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BeingOfflineIsNotAnError()
    {
        var (version, log) = await RunAsync(_ => throw new HttpRequestException("No such host is known."));

        Assert.Null(version);
        Assert.Contains("Update check failed", log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "tag_name": "not-a-version" }""")]
    [InlineData("""{ "tag_name": 42 }""")]
    [InlineData("{ }")]
    [InlineData("not json at all")]
    public async Task AnUnusablePayloadYieldsNoVersion(string body)
    {
        var (version, _) = await RunAsync(_ => Json(HttpStatusCode.OK, body));

        Assert.Null(version);
    }

    [Fact]
    public async Task TheRequestCarriesTheHeadersGitHubRequires()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """{ "tag_name": "v1.0.0" }"""));
        using var client = new GitHubReleaseClient(new DiagnosticsReport(), "1.0.0", handler);

        await client.GetLatestStableAsync();

        var request = handler.LastRequest;
        Assert.NotNull(request);
        Assert.Contains("BluetoothAudioReceiver/1.0.0", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
        Assert.Contains(request.Headers.Accept, header => header.MediaType == "application/vnd.github+json");
        Assert.Equal("2022-11-28", Assert.Single(request.Headers.GetValues("X-GitHub-Api-Version")));
        Assert.Equal(
            "https://api.github.com/repos/fuxdasec/bluetooth-audio-receiver/releases/latest",
            request.RequestUri?.ToString());
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        // The stub honours the token so the client's cancellation filter is the one exercised,
        // not the pre-cancelled token rejection inside HttpClient.
        var handler = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Json(HttpStatusCode.OK, """{ "tag_name": "v1.0.0" }""");
        });
        using var client = new GitHubReleaseClient(new DiagnosticsReport(), "1.0.0", handler);
        using var cancellation = new CancellationTokenSource();

        var request = client.GetLatestStableAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }

    [Fact]
    public void TheReleasesPageAddressIsAConstantOnGitHub()
    {
        // The button must never navigate to a URL supplied by the release payload.
        Assert.Equal(
            "https://github.com/fuxdasec/bluetooth-audio-receiver/releases/latest",
            GitHubReleaseClient.ReleasesPageUrl);
    }
}
