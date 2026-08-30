using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using BluetoothAudioReceiver.Core;

namespace BluetoothAudioReceiver.App;

public partial class MainWindow : Window
{
    /// <summary>Log height plus its header and margins, added to the window when it opens.</summary>
    private const double DiagnosticsExtraHeight = 300;

    private readonly AppHost _host;
    private double? _heightBeforeExpanding;
    private bool _allowClose;
    private bool _updatingCheckboxes;

    public MainWindow(AppHost host)
    {
        InitializeComponent();
        var displayVersion = AppVersionInfo.GetDisplayVersion();
        VersionText.Text = displayVersion;
        Title = $"Bluetooth Audio Receiver {displayVersion}";
        _host = host;
        _host.ConnectionState.Changed += ConnectionStateOnChanged;
        _host.SettingsChanged += SettingsOnChanged;
        _host.EndpointsChanged += EndpointsOnChanged;
        _host.DevicesChanged += DevicesOnChanged;
        _host.UpdateChecked += UpdateOnChecked;
        Loaded += MainWindowOnLoaded;
    }

    public event EventHandler? HideRequested;

    public void AllowClose() => _allowClose = true;

    private void MainWindowOnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindowOnLoaded;
        RefreshDevices();
        RenderState(_host.ConnectionState.Snapshot);
        RenderEndpoints();
        RenderSettings();
        RenderDiagnostics();
        // EntryAdded arrives after the initial render, so each new entry is appended instead of
        // rebuilding the whole log; the subscription is here to avoid duplicates from entries
        // recorded before the window loaded.
        _host.Diagnostics.EntryAdded += DiagnosticsOnEntryAdded;
        RenderUpdate();
    }

    // Actions

    private async void ConnectButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (DevicesCombo.SelectedItem is not DeviceRecord device)
        {
            // The button is disabled in this state; this is only a guard.
            return;
        }

        await RunUiActionAsync(() => _host.SelectDeviceAsync(device));
    }

    private async void ReconnectButtonOnClick(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(_host.ReconnectAsync);

    private async void RefreshEndpointsButtonOnClick(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync(_host.RefreshEndpointsAsync);

    private void DevicesComboOnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateActionAvailability();

    private void CopyDiagnosticsButtonOnClick(object sender, RoutedEventArgs e)
    {
        if (TryCopyToClipboard(_host.Diagnostics.ToString()))
        {
            DiagnosticsStatusText.Text = UiStrings.Get("Copied");
            return;
        }

        DiagnosticsStatusText.Text = UiStrings.Get("CopyFailed");
    }

    private void UpdateDownloadButtonOnClick(object sender, RoutedEventArgs e) =>
        OpenInBrowser(AppHost.ReleasesPageUrl);

    private void RepositoryLinkOnClick(object sender, RoutedEventArgs e) =>
        OpenInBrowser(AppHost.RepositoryUrl);

    private async void UpdateDismissButtonOnClick(object sender, RoutedEventArgs e)
    {
        var available = _host.AvailableUpdate;
        if (available is not null)
        {
            await RunUiActionAsync(() => _host.DismissUpdateAsync(available));
        }

        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    private async void UpdateNotificationsCheckOnClick(object sender, RoutedEventArgs e)
    {
        if (_updatingCheckboxes)
        {
            return;
        }

        await RunUiActionAsync(() => _host.SetUpdateNotificationsAsync(UpdateNotificationsCheck.IsChecked == true));
    }

    private void StartWithWindowsCheckOnClick(object sender, RoutedEventArgs e)
    {
        if (_updatingCheckboxes)
        {
            return;
        }

        var requested = StartWithWindowsCheck.IsChecked == true;
        try
        {
            ClearMessage();
            var result = _host.SetStartWithWindows(requested);
            if (result.Enabled != requested)
            {
                ShowMessage(result.Message, isError: true);
            }
        }
        catch (Exception exception)
        {
            _host.Diagnostics.Add($"UI action failed: {exception}");
            ShowMessage(exception.Message, isError: true);
        }
        finally
        {
            RenderSettings();
        }
    }

    private void HideButtonOnClick(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// The log lives in an auto sized row, so the window has to make room for it. Without this the
    /// cards absorb the loss and the log opens clipped.
    /// </summary>
    private void DiagnosticsExpanderOnExpanded(object sender, RoutedEventArgs e)
    {
        DiagnosticsText.ScrollToEnd();
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        _heightBeforeExpanding = Height;
        Height = Math.Min(Height + DiagnosticsExtraHeight, workArea.Height);
        if (Top + Height > workArea.Bottom)
        {
            Top = Math.Max(workArea.Top, workArea.Bottom - Height);
        }
    }

    private void DiagnosticsExpanderOnCollapsed(object sender, RoutedEventArgs e)
    {
        if (_heightBeforeExpanding is not { } previous)
        {
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            Height = previous;
        }

        _heightBeforeExpanding = null;
    }

    private async Task RunUiActionAsync(Func<Task> action)
    {
        try
        {
            ClearMessage();
            await action();
        }
        catch (Exception exception)
        {
            _host.Diagnostics.Add($"UI action failed: {exception}");
            ShowMessage(exception.Message, isError: true);
        }
    }

    /// <summary>
    /// Both callers pass a compile time constant. No address from a network response reaches this.
    /// </summary>
    private void OpenInBrowser(string url)
    {
        try
        {
            ClearMessage();
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            _host.Diagnostics.Add($"Could not open '{url}': {exception.Message}");
            var copied = TryCopyToClipboard(url);
            ShowMessage(copied ? UiStrings.Format("UrlCopied", url) : url, isError: !copied);
        }
    }

    private bool TryCopyToClipboard(string text)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            return true;
        }
        catch (Exception exception)
        {
            _host.Diagnostics.Add($"Could not use the clipboard: {exception.Message}");
            return false;
        }
    }

    // Messages, replacing the modal dialogs

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageDot.SetResourceReference(
            Shape.FillProperty,
            isError ? "StatusErrorBrush" : "AccentBrush");
        MessageBar.Visibility = Visibility.Visible;
    }

    private void ClearMessage()
    {
        MessageText.Text = string.Empty;
        MessageBar.Visibility = Visibility.Collapsed;
    }

    // Rendering

    private void ConnectionStateOnChanged(object? sender, ConnectionSnapshot snapshot) =>
        Dispatcher.InvokeAsync(() => RenderState(snapshot));

    private void DiagnosticsOnEntryAdded(string entry) =>
        Dispatcher.InvokeAsync(() => AppendDiagnostic(entry));

    private void SettingsOnChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(RenderSettings);

    private void EndpointsOnChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(RenderEndpoints);

    private void DevicesOnChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(RefreshDevices);

    private void UpdateOnChecked(object? sender, UpdateCheckResult e) =>
        Dispatcher.InvokeAsync(RenderUpdate);

    private void RefreshDevices()
    {
        var selectedId = (DevicesCombo.SelectedItem as DeviceRecord)?.Id ?? _host.Settings.TargetDeviceId;
        DevicesCombo.ItemsSource = _host.Devices;
        DevicesCombo.SelectedItem = _host.Devices.FirstOrDefault(device => device.Id == selectedId);
        UpdateActionAvailability();
    }

    private void RenderState(ConnectionSnapshot snapshot)
    {
        // Deliberately not RefreshDevices: a Recovering transition arrives on every backoff tick, and
        // reassigning ItemsSource would close the dropdown under a user picking another source.
        UpdateActionAvailability();
        StateText.Text = UiStrings.Describe(snapshot.State);
        StateDetailsText.Text = snapshot.Message;

        // A resource reference rather than a fixed brush, so the dot follows a theme switch.
        StatusDot.SetResourceReference(
            Shape.FillProperty,
            BrushKeyFor(ConnectionToneMap.For(snapshot.State)));
    }

    private static string BrushKeyFor(ConnectionTone tone) => tone switch
    {
        ConnectionTone.Ok => "StatusOkBrush",
        ConnectionTone.Warning => "StatusWarningBrush",
        ConnectionTone.Progress => "AccentBrush",
        _ => "StatusNeutralBrush",
    };

    private void RenderSourceName()
    {
        var name = (DevicesCombo.SelectedItem as DeviceRecord)?.Name
                   ?? _host.Settings.TargetDeviceName;
        SourceNameText.Text = string.IsNullOrWhiteSpace(name) ? UiStrings.Get("TrayNoDevice") : name;
    }

    private void RenderEndpoints() => RenderEndpointText.Text = _host.Endpoints.RenderName;

    private void RenderSettings()
    {
        _updatingCheckboxes = true;
        StartWithWindowsCheck.IsChecked = _host.StartWithWindowsEnabled;
        UpdateNotificationsCheck.IsChecked = _host.Settings.UpdateNotificationsEnabled;
        _updatingCheckboxes = false;
        // Deliberately not RefreshDevices: background settings writes (e.g. the update check
        // timestamp) fire SettingsChanged, and reassigning ItemsSource would close the dropdown
        // under the user. The device list refreshes on load and on DevicesChanged instead.
    }

    /// <summary>Initial full render; after this, entries arrive one by one through EntryAdded.</summary>
    private void RenderDiagnostics()
    {
        DiagnosticsText.Text = _host.Diagnostics.ToString();
        if (DiagnosticsExpander.IsExpanded)
        {
            DiagnosticsText.ScrollToEnd();
        }
    }

    /// <summary>
    /// A burst of entries from background threads would otherwise rebuild up to 500 lines of
    /// text once per entry on the Dispatcher.
    /// </summary>
    private void AppendDiagnostic(string entry)
    {
        DiagnosticsText.AppendText(entry + Environment.NewLine);
        if (DiagnosticsExpander.IsExpanded)
        {
            DiagnosticsText.ScrollToEnd();
        }
    }

    private void RenderUpdate()
    {
        var available = _host.AvailableUpdate;
        if (available is null)
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
            return;
        }

        UpdateBannerText.Text = UiStrings.Format("UpdateAvailableFormat", available.ToString());
        UpdateBanner.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Disabling the actions is what removes the "select a source" dialog: the interface states the
    /// precondition instead of interrupting after the fact.
    /// </summary>
    private void UpdateActionAvailability()
    {
        ConnectButton.IsEnabled = DevicesCombo.SelectedItem is DeviceRecord;
        ConnectButton.ToolTip = ConnectButton.IsEnabled ? null : UiStrings.Get("SelectSource");
        ReconnectButton.IsEnabled = !string.IsNullOrWhiteSpace(_host.Settings.TargetDeviceId);
        RenderSourceName();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            HideRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _host.ConnectionState.Changed -= ConnectionStateOnChanged;
        _host.Diagnostics.EntryAdded -= DiagnosticsOnEntryAdded;
        _host.SettingsChanged -= SettingsOnChanged;
        _host.EndpointsChanged -= EndpointsOnChanged;
        _host.DevicesChanged -= DevicesOnChanged;
        _host.UpdateChecked -= UpdateOnChecked;
        base.OnClosing(e);
    }
}
