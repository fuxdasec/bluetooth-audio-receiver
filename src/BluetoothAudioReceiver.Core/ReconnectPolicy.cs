namespace BluetoothAudioReceiver.Core;

public sealed class ReconnectPolicy
{
    private static readonly TimeSpan[] DefaultDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30),
    ];

    private readonly IReadOnlyList<TimeSpan> _delays;

    public ReconnectPolicy(IReadOnlyList<TimeSpan>? delays = null)
    {
        _delays = delays ?? DefaultDelays;
        if (_delays.Count == 0 || _delays.Any(delay => delay <= TimeSpan.Zero))
        {
            throw new ArgumentException("At least one positive delay is required.", nameof(delays));
        }
    }

    public TimeSpan GetDelay(int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt));
        }

        return _delays[Math.Min(attempt - 1, _delays.Count - 1)];
    }
}

