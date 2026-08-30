using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.Core.Tests;

public sealed class ConnectionToneMapTests
{
    [Theory]
    [InlineData(ConnectionState.Connected, ConnectionTone.Ok)]
    [InlineData(ConnectionState.Recovering, ConnectionTone.Warning)]
    [InlineData(ConnectionState.Enabling, ConnectionTone.Progress)]
    [InlineData(ConnectionState.Connecting, ConnectionTone.Progress)]
    [InlineData(ConnectionState.WaitingForDevice, ConnectionTone.Neutral)]
    [InlineData(ConnectionState.Disabled, ConnectionTone.Neutral)]
    public void MapsEachStateToItsTone(ConnectionState state, ConnectionTone expected)
    {
        Assert.Equal(expected, ConnectionToneMap.For(state));
    }

    [Fact]
    public void OnlyTheConnectedStateReadsAsSuccess()
    {
        var successes = Enum.GetValues<ConnectionState>()
            .Where(state => ConnectionToneMap.For(state) == ConnectionTone.Ok)
            .ToArray();

        Assert.Equal([ConnectionState.Connected], successes);
    }

    [Fact]
    public void EveryDeclaredStateIsCoveredByTheTheory()
    {
        // A state added later must be given a tone deliberately rather than falling through.
        var covered = new[]
        {
            ConnectionState.Connected,
            ConnectionState.Recovering,
            ConnectionState.Enabling,
            ConnectionState.Connecting,
            ConnectionState.WaitingForDevice,
            ConnectionState.Disabled,
        };

        Assert.Equal(
            Enum.GetValues<ConnectionState>().OrderBy(state => state),
            covered.OrderBy(state => state));
    }
}
