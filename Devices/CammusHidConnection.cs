using System;
using System.IO;
using System.Linq;
using HidSharp;

namespace CammusPlugin.Devices
{
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
