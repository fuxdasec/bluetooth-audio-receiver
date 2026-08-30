using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class NotificationPolicyTests
{
    private static ConnectionSnapshot Snapshot(ConnectionState state) => new(state, state.ToString());

    [Fact]
    public void ReachingConnectedNotifiesOnce()
    {
        var policy = new NotificationPolicy();

        Assert.Equal(TrayNotification.Connected, policy.Evaluate(Snapshot(ConnectionState.Connected)));
        Assert.Equal(TrayNotification.None, policy.Evaluate(Snapshot(ConnectionState.Connected)));
    }

    [Fact]
    public void OnlyTheFirstRecoveringAttemptNotifies()
    {
        var policy = new NotificationPolicy();
        policy.Evaluate(Snapshot(ConnectionState.Connected));

        Assert.Equal(TrayNotification.ConnectionLost, policy.Evaluate(Snapshot(ConnectionState.Recovering)));
        Assert.Equal(TrayNotification.None, policy.Evaluate(Snapshot(ConnectionState.Connecting)));
        Assert.Equal(TrayNotification.None, policy.Evaluate(Snapshot(ConnectionState.Recovering)));
        Assert.Equal(TrayNotification.None, policy.Evaluate(Snapshot(ConnectionState.Connecting)));
        Assert.Equal(TrayNotification.None, policy.Evaluate(Snapshot(ConnectionState.Recovering)));
    }

    [Fact]
    public void RecoveringNotifiesAgainAfterAConnection()
    {
        var policy = new NotificationPolicy();
        policy.Evaluate(Snapshot(ConnectionState.Connected));
        policy.Evaluate(Snapshot(ConnectionState.Recovering));
        policy.Evaluate(Snapshot(ConnectionState.Recovering));

        Assert.Equal(TrayNotification.Connected, policy.Evaluate(Snapshot(ConnectionState.Connected)));
        Assert.Equal(TrayNotification.ConnectionLost, policy.Evaluate(Snapshot(ConnectionState.Recovering)));
    }

    [Fact]
    public void RecoveringAsTheFirstStateIsSilent()
    {
        var policy = new NotificationPolicy();

        Assert.Equal(TrayNotification.None, policy.Evaluate(Snapshot(ConnectionState.Connecting)));
        Assert.Equal(TrayNotification.None, policy.Evaluate(Snapshot(ConnectionState.Recovering)));
        Assert.Equal(TrayNotification.None, policy.Evaluate(Snapshot(ConnectionState.Recovering)));
    }

    [Fact]
    public void RecoveringAfterAnEstablishedConnectionAnnouncesTheLoss()
    {
        var policy = new NotificationPolicy();
        policy.Evaluate(Snapshot(ConnectionState.Connected));

        Assert.Equal(TrayNotification.ConnectionLost, policy.Evaluate(Snapshot(ConnectionState.Recovering)));
    }

    [Theory]
    [InlineData(ConnectionState.Enabling)]
    [InlineData(ConnectionState.Connecting)]
    [InlineData(ConnectionState.WaitingForDevice)]
    [InlineData(ConnectionState.Disabled)]
    public void IntermediateStatesAreSilent(ConnectionState state)
    {
        var policy = new NotificationPolicy();

        Assert.Equal(TrayNotification.None, policy.Evaluate(Snapshot(state)));
    }
}
