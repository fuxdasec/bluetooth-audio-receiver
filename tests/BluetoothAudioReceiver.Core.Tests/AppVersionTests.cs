using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class AppVersionTests
{
    private static AppVersion Parse(string text)
    {
        Assert.True(AppVersion.TryParse(text, out var version), $"'{text}' should parse.");
        return version;
    }

    [Theory]
    [InlineData("v1.0.0", 1, 0, 0, null)]
    [InlineData("1.0.0", 1, 0, 0, null)]
    [InlineData("  V2.10.3  ", 2, 10, 3, null)]
    [InlineData("1.0.0+abc123def456", 1, 0, 0, null)]
    [InlineData("1.0.0-continuous.7+abc123def456", 1, 0, 0, "continuous.7")]
    [InlineData("1.0.0-continuous.7", 1, 0, 0, "continuous.7")]
    public void ParsesEveryShapeTheBuildProduces(string text, int major, int minor, int patch, string? preRelease)
    {
        var version = Parse(text);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(preRelease, version.PreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("continuous")]
    [InlineData("1.0.x")]
    [InlineData("-1.0.0")]
    [InlineData("1.0.0-")]
    [InlineData("+1.0.0")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-continuous.07")]
    public void RejectsWhatItCannotUnderstand(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out var version));
        Assert.Null(version);
    }

    [Theory]
    [InlineData("2.0.0", "1.9.9")]
    [InlineData("1.1.0", "1.0.9")]
    [InlineData("1.0.1", "1.0.0")]
    public void OrdersOnTheNumericCore(string newer, string older)
    {
        Assert.True(Parse(newer).IsNewerThan(Parse(older)));
        Assert.False(Parse(older).IsNewerThan(Parse(newer)));
    }

    [Fact]
    public void AStableReleaseOutranksTheContinuousBuildOfTheSameCore()
    {
        Assert.True(Parse("1.0.0").IsNewerThan(Parse("1.0.0-continuous.7+abc123")));
        Assert.False(Parse("1.0.0-continuous.7+abc123").IsNewerThan(Parse("1.0.0")));
    }

    [Fact]
    public void ContinuousBuildsOrderByTheirRunNumber()
    {
        Assert.True(Parse("1.0.0-continuous.10").IsNewerThan(Parse("1.0.0-continuous.7")));
    }

    [Theory]
    [InlineData("1.0.0-1", "1.0.0-alpha")]
    [InlineData("1.0.0-2", "1.0.0-10")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.beta.1")]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    public void PreReleaseIdentifiersOrderBySemVerRules(string lower, string higher)
    {
        Assert.True(Parse(lower).CompareTo(Parse(higher)) < 0);
        Assert.True(Parse(higher).CompareTo(Parse(lower)) > 0);
        Assert.True(Parse(higher).IsNewerThan(Parse(lower)));
        Assert.False(Parse(lower).IsNewerThan(Parse(higher)));
    }

    [Fact]
    public void BuildMetadataDoesNotAffectOrdering()
    {
        Assert.Equal(0, Parse("1.0.0+aaa").CompareTo(Parse("1.0.0+bbb")));
        Assert.False(Parse("1.0.0+aaa").IsNewerThan(Parse("1.0.0+bbb")));
    }

    [Fact]
    public void IsNewerThanNullIsTrue()
    {
        Assert.True(Parse("1.0.0").IsNewerThan(null));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.0.0-continuous.7+abc", "1.0.0-continuous.7")]
    public void ToStringDropsThePrefixAndTheMetadata(string text, string expected)
    {
        Assert.Equal(expected, Parse(text).ToString());
    }
}
