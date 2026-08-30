namespace BluetoothAudioReceiver.Core;

/// <summary>
/// How a connection state should read at a glance. The interface maps each tone to a colour, which
/// keeps the rule that decides "is this good or bad" out of the XAML and under test.
/// </summary>
public enum ConnectionTone
{
    Neutral,
    Progress,
    Ok,
    Warning,
}

public static class ConnectionToneMap
{
    public static ConnectionTone For(ConnectionState state) => state switch
    {
        ConnectionState.Connected => ConnectionTone.Ok,
        ConnectionState.Recovering => ConnectionTone.Warning,
        ConnectionState.Enabling or ConnectionState.Connecting => ConnectionTone.Progress,
        ConnectionState.WaitingForDevice or ConnectionState.Disabled => ConnectionTone.Neutral,
        _ => ConnectionTone.Neutral,
    };
}
