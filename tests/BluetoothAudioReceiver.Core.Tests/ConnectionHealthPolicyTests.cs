using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class ConnectionHealthPolicyTests
{
    private static ConnectionSnapshot Snapshot(ConnectionState state) => new(state, state.ToString());

    [Fact]
    public void ConnectedOverAClosedSessionReconnectsAfterTheRequiredStrikes()
    {
        var policy = new ConnectionHealthPolicy(strikesRequired: 2);

        Assert.False(policy.RequiresReconnect(Snapshot(ConnectionState.Connected), sessionIsOpen: false, hasTarget: true));
        Assert.True(policy.RequiresReconnect(Snapshot(ConnectionState.Connected), sessionIsOpen: false, hasTarget: true));
    }

    [Fact]
    public void ConnectedWithoutASessionObjectIsAlsoUnhealthy()
    {
        var policy = new ConnectionHealthPolicy(strikesRequired: 1);

        Assert.True(policy.RequiresReconnect(Snapshot(ConnectionState.Connected), sessionIsOpen: null, hasTarget: true));
    }

    [Fact]
    public void AHealthyObservationClearsTheStrikes()
    {
        var policy = new ConnectionHealthPolicy(strikesRequired: 2);

        Assert.False(policy.RequiresReconnect(Snapshot(ConnectionState.Connected), sessionIsOpen: false, hasTarget: true));
        Assert.False(policy.RequiresReconnect(Snapshot(ConnectionState.Connected), sessionIsOpen: true, hasTarget: true));
        Assert.False(policy.RequiresReconnect(Snapshot(ConnectionState.Connected), sessionIsOpen: false, hasTarget: true));
    }

    [Theory]
    [InlineData(ConnectionState.Recovering)]
    [InlineData(ConnectionState.Connecting)]
    [InlineData(ConnectionState.Enabling)]
    [InlineData(ConnectionState.WaitingForDevice)]
    [InlineData(ConnectionState.Disabled)]
    public void OnlyTheConnectedStateIsPoliced(ConnectionState state)
    {
        var policy = new ConnectionHealthPolicy(strikesRequired: 1);

        Assert.False(policy.RequiresReconnect(Snapshot(state), sessionIsOpen: false, hasTarget: true));
    }

    [Fact]
    public void NothingHappensWithoutASelectedSource()
    {
        var policy = new ConnectionHealthPolicy(strikesRequired: 1);

        Assert.False(policy.RequiresReconnect(Snapshot(ConnectionState.Connected), sessionIsOpen: false, hasTarget: false));
    }

    [Fact]
    public void ResetClearsThePendingStrikes()
    {
        var policy = new ConnectionHealthPolicy(strikesRequired: 2);

        Assert.False(policy.RequiresReconnect(Snapshot(ConnectionState.Connected), sessionIsOpen: false, hasTarget: true));
        policy.Reset();
        Assert.False(policy.RequiresReconnect(Snapshot(ConnectionState.Connected), sessionIsOpen: false, hasTarget: true));
    }

    [Fact]
    public void StrikesRequiredMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConnectionHealthPolicy(0));
    }
}
