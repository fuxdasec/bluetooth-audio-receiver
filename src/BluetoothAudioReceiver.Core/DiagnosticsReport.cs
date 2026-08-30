using System.Text;

namespace BluetoothAudioReceiver.Core;

public sealed class DiagnosticsReport
{
    private readonly object _gate = new();
    private readonly Queue<string> _entries = new();
    private readonly int _capacity;

    public DiagnosticsReport(int capacity = 500)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public event EventHandler? Changed;
    public event Action<string>? EntryAdded;

    public void Add(string message)
    {
        // The events fire inside the lock so concurrent adds cannot deliver entries out of order.
        // The subscribers only enqueue work on the Dispatcher, so they never wait on us here.
        lock (_gate)
        {
            var entry = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}  {message}";
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }

            EntryAdded?.Invoke(entry);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public override string ToString()
    {
        lock (_gate)
        {
            var builder = new StringBuilder();
            foreach (var entry in _entries)
            {
                builder.AppendLine(entry);
            }

            return builder.ToString();
        }
    }
}
