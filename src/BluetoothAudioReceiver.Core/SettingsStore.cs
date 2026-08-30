using System.Text.Json;

namespace BluetoothAudioReceiver.Core;

public sealed class SettingsStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly DiagnosticsReport _diagnostics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private volatile bool _disposed;

    public SettingsStore(DiagnosticsReport diagnostics, string? path = null)
    {
        _diagnostics = diagnostics;
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothAudioReceiver",
            "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path))
            {
                return new AppSettings();
            }

            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? new AppSettings();
        }
        catch (JsonException exception)
        {
            _diagnostics.Add($"Invalid settings file; using defaults: {exception.Message}");
            return new AppSettings();
        }
        catch (IOException exception)
        {
            _diagnostics.Add($"Could not read the settings file; using defaults: {exception.Message}");
            return new AppSettings();
        }
        catch (UnauthorizedAccessException exception)
        {
            _diagnostics.Add($"Access to the settings file was denied; using defaults: {exception.Message}");
            return new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("The settings path does not contain a directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = _path + ".tmp";
            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                File.Move(temporaryPath, _path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Take the gate so in-flight loads and saves release it before it is disposed; otherwise
        // their finally blocks would hit an ObjectDisposedException during shutdown.
        _gate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }
        finally
        {
            _gate.Release();
        }

        _gate.Dispose();
    }
}
