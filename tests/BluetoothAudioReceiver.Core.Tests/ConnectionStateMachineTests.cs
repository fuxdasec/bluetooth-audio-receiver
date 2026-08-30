using System.Globalization;
using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class ConnectionStateMachineTests
{
    [Fact]
    public void SuccessfulConnectionResetsRetryCount()
    {
        var machine = new ConnectionStateMachine();

        machine.Waiting();
        machine.Enabling();
        machine.Connecting(3);
        machine.Connected();

        Assert.Equal(ConnectionState.Connected, machine.Snapshot.State);
        Assert.Equal(0, machine.Snapshot.RetryAttempt);
    }

    [Fact]
    public void RecoveringPreservesAttemptAndDelay()
    {
        var machine = new ConnectionStateMachine();

        machine.Recovering(2, TimeSpan.FromSeconds(5));

        Assert.Equal(ConnectionState.Recovering, machine.Snapshot.State);
        Assert.Equal(2, machine.Snapshot.RetryAttempt);
        Assert.Contains("5s", machine.Snapshot.Message);
    }

    [Fact]
    public void ChangedPublishesTheNewSnapshot()
    {
        var machine = new ConnectionStateMachine();
        ConnectionSnapshot? published = null;
        machine.Changed += (_, snapshot) => published = snapshot;

        machine.Recovering(1, TimeSpan.FromSeconds(1));

        Assert.Same(machine.Snapshot, published);
        Assert.Equal(ConnectionState.Recovering, published!.State);
    }

    [Fact]
    public void DisabledResetsTheRetryCountAndRestoresTheInitialMessage()
    {
        var machine = new ConnectionStateMachine();
        var initialMessage = machine.Snapshot.Message;

        machine.Connecting(3);
        machine.Disabled();

        Assert.Equal(ConnectionState.Disabled, machine.Snapshot.State);
        Assert.Equal(0, machine.Snapshot.RetryAttempt);
        Assert.Equal(initialMessage, machine.Snapshot.Message);
    }

    [Fact]
    public void WaitingResetsTheRetryCount()
    {
        var machine = new ConnectionStateMachine();

        machine.Connecting(2);
        machine.Waiting();

        Assert.Equal(ConnectionState.WaitingForDevice, machine.Snapshot.State);
        Assert.Equal(0, machine.Snapshot.RetryAttempt);
        Assert.False(string.IsNullOrWhiteSpace(machine.Snapshot.Message));
    }

    [Fact]
    public void WaitingUsesTheMessageItIsGiven()
    {
        var machine = new ConnectionStateMachine();

        machine.Waiting("custom message");

        Assert.Equal("custom message", machine.Snapshot.Message);
    }

    [Theory]
    [InlineData("en-US", "Receiver disabled.")]
    [InlineData("pt-BR", "Receptor desativado.")]
    [InlineData("pt-PT", "Receptor desativado.")]
    public void UsesTheCurrentUiCulture(string cultureName, string expectedMessage)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var machine = new ConnectionStateMachine();

            Assert.Equal(expectedMessage, machine.Snapshot.Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }
}
