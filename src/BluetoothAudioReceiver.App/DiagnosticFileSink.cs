using System.IO;
using System.Threading.Channels;
using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.App;

public sealed class DiagnosticFileSink : IDisposable
{
    private const long MaximumLogBytes = 2 * 1024 * 1024;
    private readonly DiagnosticsReport _diagnostics;
    private readonly string _currentPath;
    private readonly string _previousPath;
    private readonly Channel<string> _pending = Channel.CreateUnbounded<string>();
    private readonly Task _writer;

    public DiagnosticFileSink(DiagnosticsReport diagnostics)
    {
        _diagnostics = diagnostics;
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothAudioReceiver");
        Directory.CreateDirectory(directory);
        _currentPath = Path.Combine(directory, "receiver.log");
        _previousPath = Path.Combine(directory, "receiver.previous.log");
        _writer = Task.Run(WritePendingAsync);
        _diagnostics.EntryAdded += OnEntryAdded;
    }

    // Entries come from any thread, including the Bluetooth event threads; writing the file is
    // left to the dedicated writer so a logging burst never delays the caller.
    private void OnEntryAdded(string entry) => _pending.Writer.TryWrite(entry);

    private async Task WritePendingAsync()
    {
        await foreach (var entry in _pending.Reader.ReadAllAsync())
        {
            try
            {
                WriteLine(entry);
            }
            catch
            {
                // Logging must never interrupt Bluetooth recovery. Avoid logging this
                // failure back into DiagnosticsReport, which would recurse.
            }
        }
    }

    private void WriteLine(string entry)
    {
        if (File.Exists(_currentPath) && new FileInfo(_currentPath).Length >= MaximumLogBytes)
        {
            File.Move(_currentPath, _previousPath, true);
        }

        File.AppendAllText(_currentPath, entry + Environment.NewLine);
    }

    public void Dispose()
    {
        _diagnostics.EntryAdded -= OnEntryAdded;
        _pending.Writer.Complete();
        try
        {
            // Flush everything still queued before the process tears down.
            _writer.Wait();
        }
        catch
        {
            // Best effort; the writer already swallows its own I/O failures.
        }
    }
}
