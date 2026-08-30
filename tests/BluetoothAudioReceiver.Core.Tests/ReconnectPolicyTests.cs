using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class ReconnectPolicyTests
{
    [Fact]
    public void DelayIncreasesAndThenCaps()
    {
        var policy = new ReconnectPolicy();

        Assert.Equal(TimeSpan.FromSeconds(1), policy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.GetDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(6));
        Assert.Equal(TimeSpan.FromSeconds(30), policy.GetDelay(100));
    }

    [Fact]
    public void AttemptMustBePositive()
    {
        var policy = new ReconnectPolicy();
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetDelay(0));
    }

    [Fact]
    public void AnEmptyDelayListIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ReconnectPolicy([]));
    }

    [Fact]
    public void ANegativeDelayIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ReconnectPolicy([TimeSpan.FromSeconds(-1)]));
    }

    [Fact]
    public void AZeroDelayIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ReconnectPolicy([TimeSpan.Zero]));
    }
}

