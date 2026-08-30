using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class UpdatePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private static AppVersion Version(string text)
    {
        Assert.True(AppVersion.TryParse(text, out var version));
        return version;
    }

    [Fact]
    public void TheFirstCheckAlwaysRuns()
    {
        Assert.True(UpdatePolicy.ShouldCheck(Now, lastCheckUtc: null, Interval));
    }

    [Fact]
    public void ChecksWaitForTheInterval()
    {
        Assert.False(UpdatePolicy.ShouldCheck(Now, Now - TimeSpan.FromHours(5), Interval));
        Assert.True(UpdatePolicy.ShouldCheck(Now, Now - Interval, Interval));
        Assert.True(UpdatePolicy.ShouldCheck(Now, Now - TimeSpan.FromHours(7), Interval));
    }

    [Fact]
    public void ACheckRecordedInTheFutureDoesNotBlockChecksForever()
    {
        Assert.True(UpdatePolicy.ShouldCheck(Now, Now + TimeSpan.FromDays(400), Interval));
    }

    [Fact]
    public void TheIntervalMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UpdatePolicy.ShouldCheck(Now, null, TimeSpan.Zero));
    }

    [Fact]
    public void ANewerReleaseIsReportedAndNotified()
    {
        var availability = UpdatePolicy.Evaluate(Version("1.0.0"), Version("1.1.0"), dismissedVersion: null);

        Assert.True(availability.IsNewer);
        Assert.True(availability.ShouldNotify);
        Assert.Equal("1.1.0", availability.Latest?.ToString());
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.1.0", "1.0.0")]
    public void TheSameOrAnOlderReleaseIsSilent(string current, string latest)
    {
        var availability = UpdatePolicy.Evaluate(Version(current), Version(latest), dismissedVersion: null);

        Assert.False(availability.IsNewer);
        Assert.False(availability.ShouldNotify);
        Assert.Null(availability.Latest);
    }

    [Fact]
    public void ADismissedVersionStaysAvailableButStopsNotifying()
    {
        var availability = UpdatePolicy.Evaluate(Version("1.0.0"), Version("1.1.0"), dismissedVersion: "1.1.0");

        Assert.True(availability.IsNewer);
        Assert.False(availability.ShouldNotify);
    }

    [Fact]
    public void DismissingOneVersionDoesNotSuppressTheNext()
    {
        var availability = UpdatePolicy.Evaluate(Version("1.0.0"), Version("1.2.0"), dismissedVersion: "1.1.0");

        Assert.True(availability.ShouldNotify);
    }

    [Fact]
    public void AContinuousBuildIsToldAboutTheMatchingStableRelease()
    {
        var availability = UpdatePolicy.Evaluate(
            Version("1.0.0-continuous.7+abc123"),
            Version("1.0.0"),
            dismissedVersion: null);

        Assert.True(availability.IsNewer);
    }

    [Fact]
    public void NothingIsReportedWhenEitherVersionIsUnknown()
    {
        Assert.False(UpdatePolicy.Evaluate(null, Version("1.1.0"), null).IsNewer);
        Assert.False(UpdatePolicy.Evaluate(Version("1.0.0"), null, null).IsNewer);
    }
}
