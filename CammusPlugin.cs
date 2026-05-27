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
        // Re-probe at ~2 Hz when disconnected; SimHub fires DataUpdate at the
        // game's frame rate (~60 Hz typical) so a 30-frame gate amortises the
        // HID enumeration cost.
        private const int ReconnectFrameInterval = 30;

        internal CammusHidConnection Connection => _connection;

        /// <summary>Last value the LED driver sees in its Display() pass.</summary>
        internal ushort LastVelocity { get; private set; }
        /// <summary>Gear value as the Cammus firmware expects (1-based forward).</summary>
        internal int LastGear { get; private set; } = 1;
        internal CammusModelSpec? DetectedModel => _connection.Model;

        public PluginManager? PluginManager { get; set; }

        public string LeftMenuTitle => "Cammus Control";

        public System.Windows.Media.ImageSource? PictureIcon => null;

        public void Init(PluginManager pluginManager)
        {
            Instance = this;

            CammusLog.Info("[Cammus] Plugin Init");

            // Deploy both device templates eagerly. SimHub only instantiates
            // a device when its VID/PID is USB-attached, so writing both
            // templates is harmless and avoids a chicken-and-egg: detection-
            // driven deploy would never write anything until a wheel was
            // physically connected, which prevents inspection and means a
            // hot-plug requires a plugin restart for SimHub to pick up the
            // newly-written template.
            try { CammusDeviceDefinitionDeployer.DeployAll(); }
            catch (Exception ex) { CammusLog.Error($"[Cammus] DeployAll threw: {ex.Message}"); }

            // First connection attempt — non-fatal on failure, DataUpdate retries.
            _connection.TryConnect();
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            // Periodic reconnect probe when disconnected. Avoids enumerating
            // HID devices on every frame. No deploy needed here — both
            // templates were written in Init.
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

            // Cache telemetry for the LED driver's next Display() pass.
            // GameData.NewData is null during menus / when no game is active.
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

        // SpeedKmh is a double (kph). The Cammus firmware reads bytes[2..3]
        // (C5) / bytes[4..5] (C12) as a big-endian u16 — clamp into that.
        private static ushort ClampToUshort(double kmh)
        {
            if (kmh <= 0) return 0;
            if (kmh >= ushort.MaxValue) return ushort.MaxValue;
            return (ushort)Math.Round(kmh);
        }

        // GameData.Gear is a string per GameReaderCommon: "N", "R", "1".."N".
        // Firmware expects 1-based forward gears in bytes[4]/(gear-1). We map
        // reverse and neutral to 1 so (gear-1)=0 lights the firmware's
        // "no gear" cell (the monocoque source clamps the same way).
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
