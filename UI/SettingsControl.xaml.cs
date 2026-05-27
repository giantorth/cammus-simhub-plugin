using System;
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

        public SettingsControl()
        {
            InitializeComponent();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250),
            };
            _refreshTimer.Tick += (s, e) => RefreshFromPlugin();
            Loaded += (s, e) => _refreshTimer.Start();
            Unloaded += (s, e) => _refreshTimer.Stop();

            RefreshFromPlugin();
        }

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
                    $"vel={plugin.LastVelocity,5} kph   gear={plugin.LastGear}";
            }
        }

        // Send a synthetic report so the user can verify the wire is alive
        // without launching a game. lit=5, vel=60, gear=3. Uses the spec's
        // own report builder so the byte layout stays single-sourced.
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
                var report = spec.BuildReport(lit: 5, velocity: 60, gear: 3);
                bool ok = plugin.Connection.Write(report);
                StatusText.Text = ok ? "Test pattern sent" : "Test pattern write failed";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Test pattern error: {ex.Message}";
            }
        }
    }
}
