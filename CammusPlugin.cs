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
        private int _framesSinceReconnect;
        private const int ReconnectFrameInterval = 30;

        internal CammusHidConnection Connection => _connection;

        internal ushort LastVelocity { get; private set; }
        internal int LastGear { get; private set; } = 1;
        internal int LastLit { get; private set; }
        internal CammusModelSpec? DetectedModel => _connection.Model;

        internal void SendLedUpdate(int lit)
            => SendLedUpdate(lit, LastVelocity, LastGear);

        internal void SendLedUpdate(int lit, ushort velocity, int gear)
        {
            var spec = _connection.Model;
            if (spec == null) return;
            if (!_connection.IsConnected) return;

            LastLit = lit;
            LastVelocity = velocity;
            LastGear = gear;
            var report = spec.BuildReport(lit, velocity, gear);
            _connection.Write(report);
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

            var nd = data.NewData;
            if (nd != null)
            {
                LastVelocity = ClampToUshort(nd.SpeedKmh);
                LastGear = ParseGear(nd.Gear);
            }
        }

        public void End(PluginManager pluginManager)
        {
            CammusLog.Info("[Cammus] Plugin End — closing HID");
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

        // R/N → 1 so firmware sees (gear-1)=0 as the "no gear" cell.
        private static int ParseGear(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return 1;
            if (int.TryParse(raw, out int g) && g > 0)
            {
                if (g > 9) g = 9;
                return g;
            }
            return 1;
        }
    }

}
