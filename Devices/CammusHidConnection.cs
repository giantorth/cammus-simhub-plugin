using System;
using System.IO;
using System.Linq;
using HidSharp;

namespace CammusPlugin.Devices
{
    /// <summary>
    /// Thin HidSharp wrapper: scans for the first Cammus VID + (any of the
    /// candidate PIDs), opens a write stream, exposes a Write that marks
    /// itself disconnected on any I/O failure so the next DataUpdate can
    /// re-probe. No reconnection timer of its own — the plugin's per-frame
    /// loop handles re-detection.
    /// </summary>
    internal sealed class CammusHidConnection : IDisposable
    {
        private readonly object _gate = new object();
        private HidStream? _stream;
        private CammusModelSpec? _model;

        public bool IsConnected
        {
            get { lock (_gate) return _stream != null; }
        }

        public CammusModelSpec? Model
        {
            get { lock (_gate) return _model; }
        }

        /// <summary>
        /// Probe for any supported Cammus device and open it. Returns the
        /// matched spec on success, null if no device is currently attached
        /// or the open failed.
        /// </summary>
        public CammusModelSpec? TryConnect()
        {
            lock (_gate)
            {
                if (_stream != null) return _model;

                foreach (var spec in CammusModelSpec.All)
                {
                    HidDevice? dev;
                    try
                    {
                        dev = DeviceList.Local
                            .GetHidDevices(spec.Vid, spec.Pid)
                            .FirstOrDefault();
                    }
                    catch (Exception ex)
                    {
                        CammusLog.Warn($"[Cammus] HID enumeration failed: {ex.Message}");
                        continue;
                    }

                    if (dev == null) continue;

                    HidStream? stream = null;
                    try
                    {
                        if (!dev.TryOpen(out stream) || stream == null)
                        {
                            CammusLog.Warn(
                                $"[Cammus] Found {spec.DisplayName} (VID 0x{spec.Vid:X4} PID 0x{spec.Pid:X4}) " +
                                $"but TryOpen failed — likely held by another process");
                            continue;
                        }
                        stream.WriteTimeout = 250;
                        _stream = stream;
                        _model = spec;
                        CammusLog.Info(
                            $"[Cammus] Connected to {spec.DisplayName} " +
                            $"(VID 0x{spec.Vid:X4} PID 0x{spec.Pid:X4}, {spec.ReportSize}-byte reports)");
                        return spec;
                    }
                    catch (Exception ex)
                    {
                        try { stream?.Dispose(); } catch { }
                        CammusLog.Warn($"[Cammus] Open {spec.DisplayName} threw: {ex.Message}");
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Write a HID report. On any I/O failure the stream is closed and
        /// IsConnected goes false; the next TryConnect() will re-probe.
        /// </summary>
        public bool Write(byte[] report)
        {
            HidStream? local;
            lock (_gate)
            {
                local = _stream;
            }
            if (local == null) return false;

            try
            {
                local.Write(report);
                return true;
            }
            catch (Exception ex)
            {
                CammusLog.Warn($"[Cammus] HID write failed, marking disconnected: {ex.Message}");
                Close();
                return false;
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                try { _stream?.Dispose(); }
                catch (IOException) { /* swallow on disconnect */ }
                catch (Exception ex) { CammusLog.Warn($"[Cammus] Close threw: {ex.Message}"); }
                _stream = null;
                _model = null;
            }
        }

        public void Dispose() => Close();
    }
}
