using System;
using System.Drawing;
using BA63Driver.Interfaces;
using BA63Driver.Mapper;
using SerialDash;
using SimHub.Plugins.OutputPlugins.GraphicalDash.LedModules;
using SimHub.Plugins.OutputPlugins.GraphicalDash.PSE;

namespace CammusPlugin.Devices
{
    /// <summary>
    /// Virtual <see cref="ILedDeviceManager"/> injected into SimHub's LED
    /// pipeline for a Cammus wheel. The injection's purpose is twofold:
    /// (a) report Connected so SimHub enables its LED-effects UI, and
    /// (b) receive computed <see cref="Color"/> arrays each frame so we can
    /// collapse them to the single intensity value the Cammus firmware
    /// accepts (lit-count for C5, percent for C12) and write the HID
    /// report — combined with cached velocity/gear pulled from the plugin's
    /// most recent DataUpdate.
    /// </summary>
    internal sealed class CammusLedDeviceManager : ILedDeviceManager
    {
        private readonly CammusModelSpec _spec;
        private LedDeviceState _lastState = new LedDeviceState(
            Array.Empty<Color>(), Array.Empty<Color>(), Array.Empty<Color>(),
            Array.Empty<Color>(), Array.Empty<Color>(),
            1.0, 1.0, 1.0, 1.0);
        private bool _wasConnected;

        public LedModuleSettings LedModuleSettings { get; set; } = null!;
        public LedDeviceState LastState => _lastState;

        public event EventHandler? BeforeDisplay;
        public event EventHandler? AfterDisplay;
        public event EventHandler? OnConnect;
#pragma warning disable CS0067 // OnError is required by ILedDeviceManager but currently unused.
        public event EventHandler? OnError;
#pragma warning restore CS0067
        public event EventHandler? OnDisconnect;

        public CammusLedDeviceManager(CammusModelSpec spec)
        {
            _spec = spec;
        }

        public bool IsConnected()
            => CammusPlugin.Instance?.Connection.IsConnected ?? false;

        public string GetSerialNumber() => $"CAMMUS-{_spec.Wheel}-VIRTUAL";

        public string GetFirmwareVersion() => "1.0";

        public object GetDriverInstance() => this;

        public void Close() { }

        public void ResetDetection() { }

        public void SerialPortCanBeScanned(object sender, SerialDashController.ScanArgs e) { }

        public IPhysicalMapper GetPhysicalMapper() => new NeutralLedsMapper();

        public ILedDriverBase? GetLedDriver() => null;

        /// <summary>
        /// Called every frame by SimHub's LED pipeline. We materialise the
        /// computed Color[], count non-black entries (that is the "effective
        /// fill level" SimHub's redline curve produced), combine with the
        /// plugin's most recently cached velocity/gear, and write one HID
        /// report. simhub.md note line 673: do NOT early-return on an empty
        /// led array — raw overrides arrive via rawState in Exclusive mode.
        /// </summary>
        public void Display(
            Func<Color[]> leds,
            Func<Color[]> buttons,
            Func<Color[]> encoders,
            Func<Color[]> matrix,
            Func<Color[]> rawState,
            bool forceRefresh,
            Func<object>? extraData = null,
            double rpmBrightness = 1.0,
            double buttonsBrightness = 1.0,
            double encodersBrightness = 1.0,
            double matrixBrightness = 1.0)
        {
            BeforeDisplay?.Invoke(this, EventArgs.Empty);

            try
            {
                var ledColors = leds?.Invoke() ?? Array.Empty<Color>();
                var buttonColors = buttons?.Invoke() ?? Array.Empty<Color>();
                var encoderColors = encoders?.Invoke() ?? Array.Empty<Color>();
                var matrixColors = matrix?.Invoke() ?? Array.Empty<Color>();
                var rawColors = rawState?.Invoke() ?? Array.Empty<Color>();

                _lastState = new LedDeviceState(
                    ledColors, buttonColors, encoderColors, matrixColors, rawColors,
                    rpmBrightness, buttonsBrightness, encodersBrightness, matrixBrightness);

                var plugin = CammusPlugin.Instance;
                if (plugin == null) return;
                if (!plugin.Connection.IsConnected) return;

                // In SimHub's Exclusive (Individual-LEDs-only) mode the logical
                // ledColors arrive empty and the raw overrides come on rawState;
                // merge so user effects in either mode reach the wheel.
                Color[] source = ledColors;
                if (rawColors.Length > 0)
                {
                    int len = Math.Max(source.Length, Math.Min(rawColors.Length, _spec.LedCount));
                    var merged = new Color[len];
                    for (int i = 0; i < len; i++)
                    {
                        Color baseColor = i < source.Length ? source[i] : Color.Black;
                        if (i < rawColors.Length)
                        {
                            var raw = rawColors[i];
                            if (raw.R != 0 || raw.G != 0 || raw.B != 0)
                                baseColor = raw;
                        }
                        merged[i] = baseColor;
                    }
                    source = merged;
                }

                // Count non-black colors weighted by per-frame rpmBrightness so
                // a user setting SimHub's LED brightness to 0 still produces a
                // dark wheel (lit==0). Threshold > 0 keeps every meaningful
                // hue, including dim red at the bottom of a redline curve.
                double brightness = rpmBrightness < 0 ? 0 : (rpmBrightness > 1 ? 1 : rpmBrightness);
                int lit = 0;
                int max = Math.Min(source.Length, _spec.LedCount);
                for (int i = 0; i < max; i++)
                {
                    var c = source[i];
                    double r = c.R * brightness;
                    double g = c.G * brightness;
                    double b = c.B * brightness;
                    if (r + g + b > 0.5) lit++;
                }

                ushort vel = plugin.LastVelocity;
                int gear = plugin.LastGear;
                var report = _spec.BuildReport(lit, vel, gear);
                plugin.Connection.Write(report);
            }
            catch (Exception ex)
            {
                CammusLog.Error($"[Cammus] Display() threw: {ex.Message}");
            }
            finally
            {
                AfterDisplay?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Called every frame from the device extension's DataUpdate. Fires
        /// OnConnect/OnDisconnect on transitions so SimHub resumes/pauses
        /// the Display() callback path (without these events SimHub will not
        /// notice a reconnect — simhub.md line 648).
        /// </summary>
        internal void UpdateConnectionState()
        {
            bool connected = IsConnected();
            if (connected == _wasConnected) return;
            _wasConnected = connected;
            if (connected) OnConnect?.Invoke(this, EventArgs.Empty);
            else           OnDisconnect?.Invoke(this, EventArgs.Empty);
        }
    }
}
