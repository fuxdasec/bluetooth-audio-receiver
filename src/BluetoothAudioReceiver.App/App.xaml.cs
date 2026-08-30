using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using BluetoothAudioReceiver.Core;
using Microsoft.Win32;

namespace BluetoothAudioReceiver.App;

public partial class App : System.Windows.Application
{
    private const string InstanceName = @"Local\BluetoothAudioReceiver";
    private const string ShowWindowSignalName = @"Local\BluetoothAudioReceiver.Show";

    /// <summary>Grants foreground rights to any process, as documented for ASFW_ANY.</summary>
    private const int AllowAnyProcess = -1;

    /// <summary>Windows silently ignores a tray tooltip longer than this.</summary>
    private const int TrayTextLimit = 63;

    private static readonly TimeSpan TrayRetryDelay = TimeSpan.FromSeconds(1);
    private const int TrayCreationAttempts = 5;

    private readonly NotificationPolicy _notificationPolicy = new();
    private System.Drawing.Icon? _applicationIcon;
    private NotifyIcon? _trayIcon;
    private MainWindow? _window;
    private AppHost? _host;
    private DiagnosticFileSink? _fileSink;
    private ThemeService? _themeService;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showWindowSignal;
    private RegisteredWaitHandle? _showWindowWait;
    private bool _balloonOpensReleasesPage;
    private int _exitStarted;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!TryClaimSingleInstance())
        {
            // A second copy would fight the first one over the same AudioPlaybackConnection.
            Shutdown();
            return;
        }

        var diagnostics = new DiagnosticsReport();
        try
        {
            _fileSink = new DiagnosticFileSink(diagnostics);
            diagnostics.Add(AppVersionInfo.GetStartupDescription());
            _themeService = new ThemeService(this, diagnostics);
            _themeService.Start();
            var settingsStore = new SettingsStore(diagnostics);
            var controller = new BluetoothAudioController(diagnostics);
            var endpointService = new AudioEndpointService(diagnostics);
            var startupService = new StartupService(diagnostics);
            var releaseClient = new GitHubReleaseClient(
                diagnostics,
                AppVersionInfo.GetCurrentVersion()?.ToString());
            var updateService = new UpdateService(diagnostics, releaseClient);

            _host = new AppHost(
                controller,
                endpointService,
                settingsStore,
                startupService,
                updateService,
                diagnostics);
            _window = new MainWindow(_host);
            _window.HideRequested += (_, _) => _window.Hide();
            RegisterShowWindowSignal();

            _applicationIcon = GetApplicationIcon();
            await CreateTrayIconAsync(diagnostics);
            _host.ConnectionState.Changed += ConnectionStateOnChanged;
            _host.UpdateChecked += HostOnUpdateChecked;
            SystemEvents.PowerModeChanged += SystemEventsOnPowerModeChanged;

            await _host.InitializeAsync();
            RenderTrayState(_host.ConnectionState.Snapshot);
            if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                ShowWindow();
            }
        }
        catch (Exception exception)
        {
            diagnostics.Add($"Startup failed: {exception}");
            System.Windows.MessageBox.Show(
                exception.Message,
                "Bluetooth Audio Receiver",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await ExitAsync();
        }
    }

    /// <summary>
    /// Returns <see langword="false"/> when another copy already runs; that copy is asked to show its
    /// window so a second launch behaves like reopening the running receiver.
    /// </summary>
    private bool TryClaimSingleInstance()
    {
        try
        {
            _showWindowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowSignalName);
            _instanceMutex = new Mutex(false, InstanceName);
            try
            {
                if (!_instanceMutex.WaitOne(TimeSpan.Zero, exitContext: false))
                {
                    TryAllowForeground();
                    _showWindowSignal.Set();
                    _instanceMutex.Dispose();
                    _instanceMutex = null;
                    _showWindowSignal.Dispose();
                    _showWindowSignal = null;
                    return false;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous owner died without releasing it; this instance now owns it.
            }
        }
        catch (Exception)
        {
            // Without the guard a duplicate copy is possible, but refusing to start would be
            // worse. The handles created so far are not owned/acquired, so drop them here;
            // ReleaseSingleInstance would otherwise leak or release a mutex it never waited on.
            _instanceMutex?.Dispose();
            _instanceMutex = null;
            _showWindowSignal?.Dispose();
            _showWindowSignal = null;
            return true;
        }

        return true;
    }

    /// <summary>
    /// Registered only after the window exists. The signal is auto-reset, so a launch that arrives
    /// during startup stays pending here and is honoured as soon as the wait is registered.
    /// </summary>
    private void RegisterShowWindowSignal()
    {
        if (_showWindowSignal is null || _showWindowWait is not null)
        {
            return;
        }

        _showWindowWait = ThreadPool.RegisterWaitForSingleObject(
            _showWindowSignal,
            (_, _) => Dispatcher.InvokeAsync(ShowWindow),
            state: null,
            Timeout.InfiniteTimeSpan,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// A background process cannot raise its own window; without this grant from the process the user
    /// just launched, Windows only flashes the taskbar button.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>
    /// At logon the shell may not accept a tray icon yet, so creation is retried before giving up.
    /// </summary>
    private async Task CreateTrayIconAsync(DiagnosticsReport diagnostics)
    {
        for (var attempt = 1; attempt <= TrayCreationAttempts; attempt++)
        {
            try
            {
                var trayIcon = new NotifyIcon
                {
                    Text = "Bluetooth Audio Receiver",
                    Icon = _applicationIcon ?? System.Drawing.SystemIcons.Application,
                    Visible = true,
                    ContextMenuStrip = BuildTrayMenu(),
                };
                trayIcon.MouseClick += TrayIconOnMouseClick;
                trayIcon.DoubleClick += (_, _) => ShowWindow();
                trayIcon.BalloonTipClicked += TrayIconOnBalloonTipClicked;
                _trayIcon = trayIcon;
                return;
            }
            catch (Exception exception)
            {
                diagnostics.Add($"Tray icon attempt {attempt} failed: {exception.Message}");
                if (attempt == TrayCreationAttempts)
                {
                    ShowWindow();
                    return;
                }

                await Task.Delay(TrayRetryDelay);
            }
        }
    }

    private void TrayIconOnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ShowWindow();
        }
    }

    private ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        var header = new ToolStripMenuItem(UiStrings.Get("TrayNoDevice")) { Enabled = false };
        var startup = new ToolStripMenuItem(UiStrings.StartWithWindows) { CheckOnClick = false };
        startup.Click += (_, _) => ToggleStartWithWindows();
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiStrings.Get("Open"), null, (_, _) => ShowWindow());
        menu.Items.Add(UiStrings.Reconnect, null, (_, _) =>
            _ = RunBackgroundActionAsync(() => _host?.ReconnectAsync() ?? Task.CompletedTask, "reconnect"));
        menu.Items.Add(UiStrings.Get("UpdateCheckNow"), null, (_, _) => _ = CheckForUpdatesAsync());
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiStrings.Get("Exit"), null, (_, _) => _ = ExitAsync());
        menu.Opening += (_, _) =>
        {
            var deviceName = _host?.Settings.TargetDeviceName;
            header.Text = string.IsNullOrWhiteSpace(deviceName)
                ? UiStrings.Get("TrayNoDevice")
                : UiStrings.Format("TrayDeviceHeader", deviceName);
            startup.Checked = _host?.StartWithWindowsEnabled == true;
        };
        return menu;
    }

    private void TrayIconOnBalloonTipClicked(object? sender, EventArgs e)
    {
        if (_balloonOpensReleasesPage)
        {
            OpenReleasesPage();
            return;
        }

        ShowWindow();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            var result = await _host.CheckForUpdatesAsync();
            if (result.Availability is { IsNewer: true, Latest: { } latest })
            {
                // An explicit request deserves an answer even for a version the user dismissed;
                // dismissing silences the automatic reminder, not a question the user just asked.
                if (!result.Availability.ShouldNotify)
                {
                    ShowBalloon(
                        UiStrings.Get("UpdateCheckNow"),
                        UiStrings.Format("UpdateAvailableFormat", latest.ToString()),
                        ToolTipIcon.Info,
                        opensReleasesPage: true);
                }

                return;
            }

            ShowBalloon(
                UiStrings.Get("UpdateCheckNow"),
                UiStrings.Get(result.Reached ? "UpdateUpToDate" : "UpdateCheckFailed"),
                ToolTipIcon.Info);
        }
        catch (Exception exception)
        {
            _host.Diagnostics.Add($"Manual update check failed: {exception}");
        }
    }

    private void HostOnUpdateChecked(object? sender, UpdateCheckResult e) =>
        Dispatcher.InvokeAsync(() =>
        {
            if (!e.Availability.ShouldNotify || e.Availability.Latest is not { } latest)
            {
                return;
            }

            ShowBalloon(
                UiStrings.Get("UpdateCheckNow"),
                UiStrings.Format("UpdateAvailableFormat", latest.ToString()),
                ToolTipIcon.Info,
                opensReleasesPage: true);
        });

    private void OpenReleasesPage()
    {
        try
        {
            // A constant address; nothing from the release payload reaches this call.
            Process.Start(new ProcessStartInfo(AppHost.ReleasesPageUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _host?.Diagnostics.Add($"Could not open the releases page: {exception.Message}");
        }
    }

    private void ToggleStartWithWindows()
    {
        if (_host is null)
        {
            return;
        }

        try
        {
            var requested = !_host.StartWithWindowsEnabled;
            var result = _host.SetStartWithWindows(requested);
            if (result.Enabled != requested)
            {
                ShowBalloon(UiStrings.Get("StartupTitle"), result.Message, ToolTipIcon.Warning);
            }
        }
        catch (Exception exception)
        {
            _host.Diagnostics.Add($"Tray startup toggle failed: {exception}");
        }
    }

    private void ConnectionStateOnChanged(object? sender, ConnectionSnapshot snapshot) =>
        Dispatcher.InvokeAsync(() => RenderTrayState(snapshot));

    private void RenderTrayState(ConnectionSnapshot snapshot)
    {
        if (_trayIcon is null)
        {
            return;
        }

        var label = UiStrings.Describe(snapshot.State);
        var tooltip = UiStrings.Format("TrayTooltipFormat", label);
        _trayIcon.Text = tooltip.Length > TrayTextLimit ? tooltip[..TrayTextLimit] : tooltip;

        var deviceName = _host?.Settings.TargetDeviceName ?? string.Empty;
        switch (_notificationPolicy.Evaluate(snapshot))
        {
            case TrayNotification.Connected:
                ShowBalloon(label, UiStrings.Format("NotifyConnected", deviceName), ToolTipIcon.Info);
                break;
            case TrayNotification.ConnectionLost:
                ShowBalloon(label, UiStrings.Format("NotifyConnectionLost", deviceName), ToolTipIcon.Warning);
                break;
        }
    }

    private void ShowBalloon(string title, string text, ToolTipIcon icon, bool opensReleasesPage = false)
    {
        _balloonOpensReleasesPage = opensReleasesPage;
        try
        {
            _trayIcon?.ShowBalloonTip(5000, title, text, icon);
        }
        catch (Exception exception)
        {
            _host?.Diagnostics.Add($"Could not show a tray notification: {exception.Message}");
        }
    }

    private void ShowWindow()
    {
        if (_window is null || Interlocked.CompareExchange(ref _exitStarted, 0, 0) != 0)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == System.Windows.WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();

        // Activate alone is unreliable when the request came from the other process.
        _window.Topmost = true;
        _window.Topmost = false;
        _window.Focus();
    }

    private static void TryAllowForeground()
    {
        try
        {
            AllowSetForegroundWindow(AllowAnyProcess);
        }
        catch (Exception)
        {
            // The window still opens; it may just not come to the front.
        }
    }

    private async void SystemEventsOnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume || _host is null)
        {
            return;
        }

        _host.Diagnostics.Add("Windows resumed from sleep; requesting reconnection.");
        await RunBackgroundActionAsync(_host.ReconnectAsync, "reconnect after sleep");
    }

    private async Task RunBackgroundActionAsync(Func<Task> action, string description)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _host?.Diagnostics.Add($"Failed to {description}: {exception}");
        }
    }

    private async Task ExitAsync()
    {
        if (Interlocked.Exchange(ref _exitStarted, 1) != 0)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= SystemEventsOnPowerModeChanged;
        if (_host is not null)
        {
            _host.ConnectionState.Changed -= ConnectionStateOnChanged;
            _host.UpdateChecked -= HostOnUpdateChecked;
        }

        try
        {
            if (_host is not null)
            {
                await _host.DisposeAsync();
            }
        }
        catch (Exception exception)
        {
            _host?.Diagnostics.Add($"Resource shutdown failed: {exception}");
        }
        finally
        {
            _host = null;
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            ReleaseSingleInstance();
            _themeService?.Dispose();
            _themeService = null;
            _fileSink?.Dispose();
            _fileSink = null;
            _applicationIcon?.Dispose();
            _applicationIcon = null;
            _window?.AllowClose();
            Shutdown();
        }
    }

    private void ReleaseSingleInstance()
    {
        _showWindowWait?.Unregister(null);
        _showWindowWait = null;
        _showWindowSignal?.Dispose();
        _showWindowSignal = null;
        if (_instanceMutex is null)
        {
            return;
        }

        try
        {
            _instanceMutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread; disposing is still correct.
        }

        _instanceMutex.Dispose();
        _instanceMutex = null;
    }

    private static System.Drawing.Icon? GetApplicationIcon()
    {
        try
        {
            return string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? null
                : System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
        }
        catch
        {
            return null;
        }
    }
}
