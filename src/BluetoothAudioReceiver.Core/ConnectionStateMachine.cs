namespace BluetoothAudioReceiver.Core;

public sealed class ConnectionStateMachine
{
    private readonly object _gate = new();
    private ConnectionSnapshot _snapshot = new(ConnectionState.Disabled, UiStrings.Get("ReceiverDisabled"));

    public event EventHandler<ConnectionSnapshot>? Changed;

    public ConnectionSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void Waiting(string? message = null) =>
        Transition(ConnectionState.WaitingForDevice, message ?? UiStrings.Get("WaitingForSource"), retryAttempt: 0);

    public void Enabling() =>
        Transition(ConnectionState.Enabling, UiStrings.Get("EnablingReceiver"), 0);

    public void Connecting(int attempt) =>
        Transition(ConnectionState.Connecting, UiStrings.Get("OpeningConnection"), attempt);

    public void Connected() =>
        Transition(ConnectionState.Connected, UiStrings.Get("BluetoothConnected"), 0);

    public void Recovering(int attempt, TimeSpan delay) =>
        Transition(
            ConnectionState.Recovering,
            UiStrings.Format("ConnectionLost", delay.TotalSeconds),
            attempt);

    public void Disabled() => Transition(ConnectionState.Disabled, UiStrings.Get("ReceiverDisabled"), retryAttempt: 0);

    private void Transition(
        ConnectionState state,
        string message,
        int retryAttempt = 0)
    {
        // The event fires inside the lock so concurrent transitions cannot publish snapshots out of
        // order. The subscribers only enqueue work on the Dispatcher, so they never wait on us here.
        lock (_gate)
        {
            var next = new ConnectionSnapshot(state, message, retryAttempt);
            _snapshot = next;
            Changed?.Invoke(this, next);
        }
    }
}
