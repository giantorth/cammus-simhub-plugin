using System;
using GameReaderCommon;
using SimHub.Plugins;
using CammusPlugin.Devices;
using CammusPlugin.UI;

namespace CammusPlugin
{
    [PluginDescription("Bridge SimHub LED telemetry to Cammus brand racing wheels (C5 / C12).")]
    [PluginAuthor("GiantOrth")]
    [PluginName("Cammus Control")]
    public sealed class CammusPlugin : IPlugin, IDataPlugin, IWPFSettingsV2
    {
        internal static CammusPlugin? Instance { get; private set; }

        private readonly CammusHidConnection _connection = new CammusHidConnection();
        private CammusReportSender? _sender;
        private int _framesSinceReconnect;
        private const int ReconnectFrameInterval = 30;

        private volatile bool _gameActive;
        private bool _wasConnected;

        internal CammusHidConnection Connection => _connection;

        internal ushort LastVelocity { get; private set; }
        internal int LastGear { get; private set; }
        internal int LastLit { get; private set; }
        internal CammusModelSpec? DetectedModel => _connection.Model;

        // Display()-driven path: forced blank while no game is active, so a stray
        // late frame can't relight the wheel after we've cleared it.
        internal void SendLedUpdate(int lit)
        {
            if (!_gameActive) lit = 0;
            SendLedUpdate(lit, LastVelocity, LastGear);
        }

        // Explicit path (test button): sends exactly what it's given, game or not.
        internal void SendLedUpdate(int lit, ushort velocity, int gear)
        {
            LastLit = lit;
            _sender?.Submit(lit, velocity, gear);
        }

        public PluginManager? PluginManager { get; set; }

        public string LeftMenuTitle => "Cammus Control";

        public System.Windows.Media.ImageSource? PictureIcon => null;

        public void Init(PluginManager pluginManager)
        {
            Instance = this;

            CammusLog.Info("[Cammus] Plugin Init");

            try { CammusDeviceDefinitionDeployer.DeployAll(); }
            catch (Exception ex) { CammusLog.Error($"[Cammus] DeployAll threw: {ex.Message}"); }

            try { CammusUsbDiagnostics.LogStartupReport(); }
            catch (Exception ex) { CammusLog.Warn($"[Cammus] USB diagnostics threw: {ex.Message}"); }

            _sender = new CammusReportSender(_connection);
            _connection.TryConnect();
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (!_connection.IsConnected)
            {
                _framesSinceReconnect++;
                if (_framesSinceReconnect >= ReconnectFrameInterval)
                {
                    _framesSinceReconnect = 0;
                    _connection.TryConnect();
                }
            }
            else
            {
                _framesSinceReconnect = 0;
            }

            bool nowConnected = _connection.IsConnected;
            bool justConnected = nowConnected && !_wasConnected;
            _wasConnected = nowConnected;

            var nd = data.NewData;
            bool active = data.GameRunning && nd != null;

            if (active)
            {
                LastVelocity = ClampToUshort(nd!.SpeedKmh);
                LastGear = ParseGear(nd.Gear);
                _gameActive = true;
            }
            else
            {
                LastVelocity = 0;
                LastGear = 0;
                // Blank the wheel when the game stops (or on a fresh connect while
                // idle). Display() may not fire when no game is running, so the
                // clear is driven here. The sender dedups repeats.
                bool wasActive = _gameActive;
                _gameActive = false;
                if (wasActive || justConnected) SendLedUpdate(0);
            }
        }

        public void End(PluginManager pluginManager)
        {
            CammusLog.Info("[Cammus] Plugin End — closing HID");
            try { _sender?.Dispose(); } catch { }
            try { _connection.Close(); } catch { }
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        public System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
            => new SettingsControl();

        private static ushort ClampToUshort(double kmh)
        {
            if (kmh <= 0) return 0;
            if (kmh >= ushort.MaxValue) return ushort.MaxValue;
            return (ushort)Math.Round(kmh);
        }

        // Send the actual gear number; R/N/empty → 0 = "no gear" cell.
        private static int ParseGear(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return 0;
            if (int.TryParse(raw, out int g) && g > 0)
            {
                if (g > 9) g = 9;
                return g;
            }
            return 0;
        }
    }

}
