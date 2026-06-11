using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CammusPlugin.Devices;

namespace CammusPlugin.UI
{
    public partial class SettingsControl : UserControl
    {
        private static readonly Brush ConnectedBrush = (Brush)new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)).GetAsFrozen();
        private static readonly Brush DisconnectedBrush = (Brush)new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)).GetAsFrozen();

        private readonly DispatcherTimer _refreshTimer;

        // Set from CammusLog.EntryAdded (any thread); consumed on the UI timer tick
        // so the log TextBox is rebuilt at most ~4x/sec regardless of log volume.
        private volatile bool _logDirty = true;

        public SettingsControl()
        {
            InitializeComponent();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _refreshTimer.Tick += (s, e) => RefreshFromPlugin();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;

            RefreshFromPlugin();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            CammusLog.EntryAdded += OnLogEntryAdded;
            _logDirty = true;
            _refreshTimer.Start();
            RefreshDevices();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            CammusLog.EntryAdded -= OnLogEntryAdded;
            _refreshTimer.Stop();
        }

        private void OnLogEntryAdded() => _logDirty = true;

        private void RefreshFromPlugin()
        {
            var plugin = CammusPlugin.Instance;
            bool connected = plugin?.Connection.IsConnected ?? false;
            var model = plugin?.DetectedModel;

            StatusDot.Fill = connected ? ConnectedBrush : DisconnectedBrush;
            StatusText.Text = connected ? "Connected" : "Not connected";
            ModelText.Text = model?.DisplayName ?? (connected ? "Unknown" : "No device");

            if (plugin != null)
            {
                TelemetryText.Text =
                    $"lit={plugin.LastLit,2}   vel={plugin.LastVelocity,5} kph   gear={plugin.LastGear}";
            }

            if (_logDirty)
            {
                _logDirty = false;
                UpdateLogView();
            }
        }

        private void UpdateLogView()
        {
            LogText.Text = CammusLog.SnapshotText();
            if (AutoScrollCheck.IsChecked == true)
                LogText.ScrollToEnd();
        }

        // Enumeration touches HidSharp and the registry, which can block briefly;
        // run it off the UI thread and post the result back.
        private void RefreshDevices()
        {
            DevicesText.Text = "Scanning USB / HID devices…";
            RefreshDevicesButton.IsEnabled = false;

            Task.Run(() =>
            {
                string report;
                try
                {
                    report = CammusUsbDiagnostics.BuildHidDevicesReport()
                             + Environment.NewLine
                             + "──────────────────────────────────────────" + Environment.NewLine
                             + CammusUsbDiagnostics.BuildRegistryReport();
                }
                catch (Exception ex)
                {
                    report = $"Device scan failed: {ex.GetType().Name}: {ex.Message}";
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DevicesText.Text = report;
                    RefreshDevicesButton.IsEnabled = true;
                }));
            });
        }

        private void OnRefreshDevicesClick(object sender, RoutedEventArgs e) => RefreshDevices();

        private void OnCopyLogClick(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(CammusLog.SnapshotText()); }
            catch (Exception ex) { CammusLog.Warn($"[Cammus] Copy log failed: {ex.Message}"); }
        }

        private void OnClearLogClick(object sender, RoutedEventArgs e)
        {
            CammusLog.Clear();
            _logDirty = true;
        }

        private void OnTestButtonClick(object sender, RoutedEventArgs e)
        {
            var plugin = CammusPlugin.Instance;
            var spec = plugin?.DetectedModel;
            if (plugin == null || spec == null || !plugin.Connection.IsConnected)
            {
                StatusText.Text = "Not connected — cannot send test pattern";
                return;
            }

            try
            {
                plugin.SendLedUpdate(lit: 5, velocity: 60, gear: 3);
                StatusText.Text = "Test pattern sent (lit=5, 60 kph, gear 3)";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Test pattern error: {ex.Message}";
            }
        }
    }
}
