namespace BluetoothAudioReceiver.Core;

public enum TrayNotification
{
    None,
    Connected,
    ConnectionLost,
}

/// <summary>
/// Turns connection state transitions into tray balloons. Recovery is announced once per outage so
/// that the bounded backoff does not produce one balloon per attempt.
/// </summary>
public sealed class NotificationPolicy
{
    private readonly object _gate = new();
    private ConnectionState _last = ConnectionState.Disabled;
    private bool _wasConnected;
    private bool _lossAnnounced;

    public TrayNotification Evaluate(ConnectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var previous = _last;
            _last = snapshot.State;
            switch (snapshot.State)
            {
                case ConnectionState.Connected when previous != ConnectionState.Connected:
                    _wasConnected = true;
                    _lossAnnounced = false;
                    return TrayNotification.Connected;
                case ConnectionState.Connected:
                    _lossAnnounced = false;
                    return TrayNotification.None;
                // Without a prior connection a Recovering state is a failed first attempt, not a loss.
                case ConnectionState.Recovering when _wasConnected && !_lossAnnounced:
                    _lossAnnounced = true;
                    return TrayNotification.ConnectionLost;
                default:
                    return TrayNotification.None;
            }
        }
    }
}
