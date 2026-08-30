namespace BluetoothAudioReceiver.Core;

/// <summary>
/// Decides whether a periodic health check must force a reconnection.
/// Windows occasionally drops an <c>AudioPlaybackConnection</c> state change, which leaves the
/// application reporting <see cref="ConnectionState.Connected"/> over a session that carries no audio.
/// A reconnection is only requested after consecutive unhealthy observations so that a session that
/// is still settling is not torn down.
/// </summary>
public sealed class ConnectionHealthPolicy
{
    private readonly object _gate = new();
    private readonly int _strikesRequired;
    private int _strikes;

    public ConnectionHealthPolicy(int strikesRequired = 2)
    {
        if (strikesRequired < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(strikesRequired));
        }

        _strikesRequired = strikesRequired;
    }

    /// <param name="snapshot">The state the application currently reports.</param>
    /// <param name="sessionIsOpen">
    /// Whether Windows still reports the underlying session as open. <see langword="null"/> when no
    /// session object exists.
    /// </param>
    /// <param name="hasTarget">Whether a source device is selected.</param>
    public bool RequiresReconnect(ConnectionSnapshot snapshot, bool? sessionIsOpen, bool hasTarget)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Every other state either already has a retry pending or is not supposed to carry audio.
        var unhealthy = hasTarget && snapshot.State == ConnectionState.Connected && sessionIsOpen != true;
        lock (_gate)
        {
            if (!unhealthy)
            {
                _strikes = 0;
                return false;
            }

            if (++_strikes < _strikesRequired)
            {
                return false;
            }

            _strikes = 0;
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _strikes = 0;
        }
    }
}
