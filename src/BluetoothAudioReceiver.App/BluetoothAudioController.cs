using BluetoothAudioReceiver.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Media.Audio;

namespace BluetoothAudioReceiver.App;

public sealed class BluetoothAudioController : IAsyncDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan KickThrottle = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DisposeLockTimeout = TimeSpan.FromSeconds(5);

    private readonly DiagnosticsReport _diagnostics;
    private readonly ReconnectPolicy _reconnectPolicy = new();
    private readonly ConnectionHealthPolicy _healthPolicy = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly Dictionary<string, DeviceRecord> _devices = new(StringComparer.Ordinal);
    private readonly object _devicesGate = new();
    private readonly object _stateGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private DeviceWatcher? _watcher;
    private DeviceWatcher? _bluetoothWatcher;
    private Task? _healthWatchdog;
    private AudioPlaybackConnection? _connection;
    private long _connectionGeneration;
    private bool _connectionOpened;
    private bool _openInFlight;
    private bool _closedDuringOpen;
    private DateTimeOffset _lastKick = DateTimeOffset.MinValue;
    private CancellationTokenSource? _retry;
    private DeviceRecord? _target;
    private long _generation;
    private int _retryAttempt;
    private int _disposeStarted;

    public BluetoothAudioController(DiagnosticsReport diagnostics) => _diagnostics = diagnostics;

    public ConnectionStateMachine State { get; } = new();

    public event EventHandler? DevicesChanged;

    public IReadOnlyList<DeviceRecord> Devices
    {
        get
        {
            lock (_devicesGate)
            {
                return _devices.Values
                    .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
        }
    }

    public void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        State.Waiting();
        var watcher = DeviceInformation.CreateWatcher(AudioPlaybackConnection.GetDeviceSelector());
        watcher.Added += WatcherOnAdded;
        watcher.Removed += WatcherOnRemoved;
        watcher.EnumerationCompleted += WatcherOnEnumerationCompleted;
        _watcher = watcher;
        watcher.Start();
        _diagnostics.Add("A2DP source monitor started.");

        StartBluetoothWatcher();
        _healthWatchdog = Task.Run(() => RunHealthWatchdogAsync(_lifetime.Token));
    }

    /// <summary>
    /// Watches paired Bluetooth endpoints so that a phone coming back online triggers an immediate
    /// attempt instead of waiting for the next backoff tick.
    /// </summary>
    private void StartBluetoothWatcher()
    {
        try
        {
            var watcher = DeviceInformation.CreateWatcher(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                [IsConnectedProperty],
                DeviceInformationKind.AssociationEndpoint);
            watcher.Added += BluetoothWatcherOnAdded;
            watcher.Updated += BluetoothWatcherOnUpdated;
            _bluetoothWatcher = watcher;
            watcher.Start();
            _diagnostics.Add("Paired Bluetooth endpoint monitor started.");
        }
        catch (Exception exception)
        {
            // Losing this watcher only costs reconnection latency; the backoff still recovers.
            _diagnostics.Add($"Could not monitor paired Bluetooth endpoints: {exception.Message}");
        }
    }

    private void BluetoothWatcherOnAdded(DeviceWatcher sender, DeviceInformation device)
    {
        if (IsReportedConnected(device.Properties))
        {
            KickPendingRetry($"'{device.Name}' is connected");
        }
    }

    private void BluetoothWatcherOnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (IsReportedConnected(update.Properties))
        {
            KickPendingRetry("a paired Bluetooth endpoint connected");
        }
    }

    private static bool IsReportedConnected(IReadOnlyDictionary<string, object> properties) =>
        properties.TryGetValue(IsConnectedProperty, out var value) && value is true;

    /// <summary>
    /// Cancels the pending backoff delay and retries immediately. A kick caused by an unrelated
    /// endpoint only costs one attempt that the backoff would have made anyway.
    /// </summary>
    private void KickPendingRetry(string reason)
    {
        DeviceRecord? target;
        long generation;
        lock (_stateGate)
        {
            if (Volatile.Read(ref _disposeStarted) != 0)
            {
                return;
            }

            target = _target;
            if (target is null)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (now - _lastKick < KickThrottle)
            {
                return;
            }

            if (State.Snapshot.State is ConnectionState.Connected
                or ConnectionState.Enabling
                or ConnectionState.Connecting)
            {
                return;
            }

            _lastKick = now;
            CancelRetryLocked();
            _retryAttempt = 0;
            generation = ++_generation;
        }

        _diagnostics.Add($"Immediate A2DP attempt: {reason}.");
        _ = ConnectSafelyAsync(target, generation, force: true, _lifetime.Token);
    }

    private async Task RunHealthWatchdogAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(HealthCheckInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                DeviceRecord? target;
                long generation;
                bool? sessionIsOpen;
                lock (_stateGate)
                {
                    target = _target;
                    generation = _generation;
                    sessionIsOpen = _connection is null
                        ? null
                        : _connection.State == AudioPlaybackConnectionState.Opened;
                }

                if (!_healthPolicy.RequiresReconnect(State.Snapshot, sessionIsOpen, target is not null) ||
                    target is null)
                {
                    continue;
                }

                _diagnostics.Add("Health watchdog: the session is no longer open while connected.");
                QueueReconnect(target, generation, "watchdog detected a session that is not open");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _diagnostics.Add($"Health watchdog stopped: {exception}");
        }
    }

    public Task SelectAndConnectAsync(DeviceRecord target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        long generation;
        lock (_stateGate)
        {
            ThrowIfStopping();
            _target = target;
            generation = ++_generation;
            _retryAttempt = 0;
            CancelRetryLocked();
        }

        return ConnectCoreAsync(target, generation, force: true, cancellationToken);
    }

    public Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        DeviceRecord? target;
        long generation;
        lock (_stateGate)
        {
            ThrowIfStopping();
            target = _target;
            if (target is null)
            {
                State.Waiting();
                return Task.CompletedTask;
            }

            generation = ++_generation;
            _retryAttempt = 0;
            CancelRetryLocked();
        }

        return ConnectCoreAsync(target, generation, force: true, cancellationToken);
    }

    private void WatcherOnAdded(DeviceWatcher sender, DeviceInformation device)
    {
        var record = new DeviceRecord(
            device.Id,
            string.IsNullOrWhiteSpace(device.Name) ? "Unnamed device" : device.Name);
        lock (_devicesGate)
        {
            _devices[record.Id] = record;
        }

        _diagnostics.Add($"A2DP source found: {record.Name} [{record.Id}]");
        DevicesChanged?.Invoke(this, EventArgs.Empty);

        DeviceRecord? target;
        long generation;
        lock (_stateGate)
        {
            target = _target;
            generation = _generation;
        }

        if (target?.Id == record.Id &&
            State.Snapshot.State is ConnectionState.WaitingForDevice or ConnectionState.Recovering)
        {
            _ = ConnectSafelyAsync(target, generation, force: false, _lifetime.Token);
        }
    }

    private void WatcherOnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        DeviceRecord? removed;
        lock (_devicesGate)
        {
            _devices.Remove(update.Id, out removed);
        }

        if (removed is not null)
        {
            _diagnostics.Add($"A2DP source removed: {removed.Name}");
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        DeviceRecord? target;
        long generation;
        lock (_stateGate)
        {
            target = _target;
            generation = _generation;
        }

        if (target?.Id == update.Id)
        {
            QueueReconnect(target, generation, "device out of range");
        }
    }

    private void WatcherOnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        _diagnostics.Add($"A2DP enumeration completed; {Devices.Count} source(s) available.");
        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ConnectSafelyAsync(
        DeviceRecord target,
        long generation,
        bool force,
        CancellationToken cancellationToken)
    {
        try
        {
            await ConnectCoreAsync(target, generation, force, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !IsCurrent(target, generation))
        {
        }
        catch (Exception exception)
        {
            _diagnostics.Add($"Unexpected connection failure: {exception}");
            ScheduleReconnect(target, generation, exception.Message);
        }
    }

    private async Task ConnectCoreAsync(
        DeviceRecord target,
        long generation,
        bool force,
        CancellationToken cancellationToken)
    {
        AudioPlaybackConnection? ownedConnection = null;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetime.Token);
        var token = linkedCancellation.Token;
        await _connectionLock.WaitAsync(token);
        try
        {
            EnsureCurrent(target, generation, token);
            if (!force && IsCurrentConnection(target, generation))
            {
                _diagnostics.Add($"Duplicate A2DP request ignored for {target.Name}.");
                return;
            }

            DisposeConnection();
            State.Enabling();
            _diagnostics.Add($"Creating A2DP receiver for {target.Name}.");

            var connection = AudioPlaybackConnection.TryCreateFromId(target.Id);
            if (connection is null)
            {
                ScheduleReconnect(target, generation, "Windows did not create an AudioPlaybackConnection");
                return;
            }

            ownedConnection = connection;

            connection.StateChanged += ConnectionOnStateChanged;
            lock (_stateGate)
            {
                if (!IsCurrentLocked(target, generation))
                {
                    connection.StateChanged -= ConnectionOnStateChanged;
                    connection.Dispose();
                    return;
                }

                _connection = connection;
                _connectionGeneration = generation;
                _connectionOpened = false;
                _openInFlight = true;
                _closedDuringOpen = false;
            }

            await connection.StartAsync();
            EnsureCurrent(target, generation, token);

            int attempt;
            lock (_stateGate)
            {
                attempt = Math.Max(1, _retryAttempt + 1);
            }

            State.Connecting(attempt);
            var result = await connection.OpenAsync();
            EnsureCurrent(target, generation, token);
            if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
            {
                DisposeConnection(connection);
                ScheduleReconnect(target, generation, $"OpenAsync returned {result.Status}");
                return;
            }

            bool closedDuringOpen;
            lock (_stateGate)
            {
                if (!IsCurrentLocked(target, generation) || !ReferenceEquals(connection, _connection))
                {
                    connection.StateChanged -= ConnectionOnStateChanged;
                    connection.Dispose();
                    return;
                }

                closedDuringOpen = _closedDuringOpen;
                if (!closedDuringOpen)
                {
                    _retryAttempt = 0;
                    _connectionOpened = true;
                }
            }

            // Windows can report success for a session that it already closed. Accepting it here is
            // what leaves the receiver silent until the user reconnects by hand.
            if (closedDuringOpen)
            {
                DisposeConnection(connection);
                ScheduleReconnect(target, generation, "the session closed while it was being opened");
                return;
            }

            State.Connected();
            _diagnostics.Add($"A2DP opened for {target.Name}; OpenAsync={result.Status}.");
        }
        catch (OperationCanceledException)
        {
            if (ownedConnection is not null)
            {
                DisposeConnection(ownedConnection);
            }

            // A superseded request is not a failure: the newer selection takes over from here,
            // so only genuine cancellation (the caller or shutdown) reaches the caller.
            if (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
            {
                throw;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _diagnostics.Add($"A2DP error: {exception}");
            DisposeConnection();
            ScheduleReconnect(target, generation, exception.Message);
        }
        finally
        {
            lock (_stateGate)
            {
                _openInFlight = false;
            }

            _connectionLock.Release();
        }
    }

    private void ConnectionOnStateChanged(AudioPlaybackConnection sender, object args)
    {
        // This runs on a Windows event thread and DisposeConnection detaches the handler outside
        // the lock, so an in-flight event can still touch a disposed WinRT object; any exception
        // escaping here would take the whole process down.
        try
        {
            ConnectionOnStateChangedCore(sender);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ConnectionOnStateChangedCore(AudioPlaybackConnection sender)
    {
        DeviceRecord? target;
        long generation;
        bool openInFlight;
        var state = sender.State;
        lock (_stateGate)
        {
            if (!ReferenceEquals(sender, _connection) || _connectionGeneration != _generation)
            {
                _diagnostics.Add("Event from a stale A2DP session ignored.");
                return;
            }

            target = _target;
            generation = _generation;
            openInFlight = _openInFlight;
            if (state == AudioPlaybackConnectionState.Opened)
            {
                _connectionOpened = true;
                _retryAttempt = 0;
            }
            else if (state == AudioPlaybackConnectionState.Closed && openInFlight)
            {
                _closedDuringOpen = true;
            }
        }

        _diagnostics.Add($"Windows changed the A2DP state to {state}.");
        if (target is null)
        {
            return;
        }

        if (state == AudioPlaybackConnectionState.Opened)
        {
            State.Connected();
            return;
        }

        if (state != AudioPlaybackConnectionState.Closed)
        {
            return;
        }

        if (openInFlight)
        {
            // ConnectCoreAsync owns the connection lock; it fails the attempt through _closedDuringOpen.
            _diagnostics.Add("The session closed while it was being opened.");
            return;
        }

        QueueReconnect(target, generation, "connection closed by Windows");
    }

    private void QueueReconnect(DeviceRecord target, long generation, string reason) =>
        _ = ResetAndScheduleReconnectAsync(target, generation, reason);

    private async Task ResetAndScheduleReconnectAsync(DeviceRecord target, long generation, string reason)
    {
        try
        {
            await _connectionLock.WaitAsync(_lifetime.Token);
            try
            {
                if (!IsCurrent(target, generation))
                {
                    return;
                }

                DisposeConnection();
                ScheduleReconnect(target, generation, reason);
            }
            finally
            {
                _connectionLock.Release();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || !IsCurrent(target, generation))
        {
        }
        catch (ObjectDisposedException)
        {
            // The controller was disposed while this reconnection was being prepared.
        }
        catch (Exception exception)
        {
            _diagnostics.Add($"Failed to prepare A2DP reconnection: {exception}");
        }
    }

    private void ScheduleReconnect(DeviceRecord target, long generation, string reason)
    {
        CancellationToken token;
        int attempt;
        lock (_stateGate)
        {
            if (!IsCurrentLocked(target, generation))
            {
                return;
            }

            CancelRetryLocked();
            _retry = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            token = _retry.Token;
            attempt = ++_retryAttempt;
        }

        var delay = _reconnectPolicy.GetDelay(attempt);
        if (!IsCurrent(target, generation))
        {
            return;
        }

        State.Recovering(attempt, delay);
        _diagnostics.Add($"A2DP reconnection #{attempt} in {delay.TotalSeconds:0}s: {reason}");
        _ = RetryAfterDelayAsync(target, generation, delay, token);
    }

    private async Task RetryAfterDelayAsync(
        DeviceRecord target,
        long generation,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await ConnectCoreAsync(target, generation, force: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !IsCurrent(target, generation))
        {
        }
        catch (ObjectDisposedException)
        {
            // The controller was disposed while this retry was pending.
        }
    }

    private bool IsCurrentConnection(DeviceRecord target, long generation)
    {
        lock (_stateGate)
        {
            return IsCurrentLocked(target, generation) &&
                   _connection is not null &&
                   _connectionGeneration == generation &&
                   (_connectionOpened || _connection.State == AudioPlaybackConnectionState.Opened);
        }
    }

    private bool IsCurrent(DeviceRecord target, long generation)
    {
        lock (_stateGate)
        {
            return IsCurrentLocked(target, generation);
        }
    }

    private bool IsCurrentLocked(DeviceRecord target, long generation) =>
        Volatile.Read(ref _disposeStarted) == 0 &&
        generation == _generation &&
        string.Equals(target.Id, _target?.Id, StringComparison.Ordinal);

    private void EnsureCurrent(DeviceRecord target, long generation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsCurrent(target, generation))
        {
            throw new OperationCanceledException("The A2DP request was superseded.", cancellationToken);
        }
    }

    private void ThrowIfStopping() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);

    private void CancelRetryLocked()
    {
        var retry = _retry;
        _retry = null;
        if (retry is null)
        {
            return;
        }

        retry.Cancel();
        retry.Dispose();
    }

    private void DisposeConnection(AudioPlaybackConnection? expected = null)
    {
        AudioPlaybackConnection? connection;
        lock (_stateGate)
        {
            if (expected is not null && !ReferenceEquals(expected, _connection))
            {
                return;
            }

            connection = _connection;
            _connection = null;
            _connectionGeneration = 0;
            _connectionOpened = false;
            _healthPolicy.Reset();
        }

        if (connection is not null)
        {
            connection.StateChanged -= ConnectionOnStateChanged;
            connection.Dispose();
        }
    }

    private void StopBluetoothWatcher()
    {
        var watcher = _bluetoothWatcher;
        _bluetoothWatcher = null;
        if (watcher is null)
        {
            return;
        }

        watcher.Added -= BluetoothWatcherOnAdded;
        watcher.Updated -= BluetoothWatcherOnUpdated;
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            watcher.Stop();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        lock (_stateGate)
        {
            ++_generation;
            CancelRetryLocked();
        }

        _lifetime.Cancel();
        var healthWatchdog = _healthWatchdog;
        _healthWatchdog = null;
        if (healthWatchdog is not null)
        {
            try
            {
                await healthWatchdog;
            }
            catch (OperationCanceledException)
            {
            }
        }

        StopBluetoothWatcher();
        var watcher = _watcher;
        _watcher = null;
        if (watcher is not null)
        {
            watcher.Added -= WatcherOnAdded;
            watcher.Removed -= WatcherOnRemoved;
            watcher.EnumerationCompleted -= WatcherOnEnumerationCompleted;
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }

        // OpenAsync of WinRT accepts no cancellation, so a stuck session can hold this lock
        // forever; on timeout the teardown proceeds best-effort instead of blocking app exit.
        var lockAcquired = await _connectionLock.WaitAsync(DisposeLockTimeout);
        if (!lockAcquired)
        {
            _diagnostics.Add("Timed out waiting for the A2DP session to stop; disposing best-effort.");
        }

        try
        {
            DisposeConnection();
            State.Disabled();
        }
        finally
        {
            if (lockAcquired)
            {
                _connectionLock.Release();
            }

            _connectionLock.Dispose();
            _lifetime.Dispose();
        }
    }
}
