namespace BluetoothAudioReceiver.Core;

public sealed record ConnectionSnapshot
{
    public ConnectionSnapshot(ConnectionState state, string message, int retryAttempt = 0)
    {
        State = state;
        Message = message;
        RetryAttempt = retryAttempt;
    }

    public ConnectionState State { get; init; }
    public string Message { get; init; }
    public int RetryAttempt { get; init; }
}
