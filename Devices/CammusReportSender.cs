using System;
using System.Threading;

namespace CammusPlugin.Devices
{
    // Single-slot coalescing HID writer. Display()/DataUpdate submit the latest
    // desired (lit, velocity, gear) snapshot; the writer thread sends only the
    // freshest value, deduped, off SimHub's threads. Because the mailbox holds
    // one value (newest wins), writes can never queue and lag behind the game.
    internal sealed class CammusReportSender : IDisposable
    {
        // Floor between writes; the blocking HID Write also self-paces to the
        // device's drain rate. Newer submits coalesce into the mailbox meanwhile.
        private const int MinWriteIntervalMs = 8;

        private readonly CammusHidConnection _connection;
        private readonly Thread _thread;
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly object _gate = new object();

        private volatile bool _running = true;

        // Desired state (the mailbox).
        private int _lit;
        private ushort _velocity;
        private int _gear = 1;
        private bool _dirty;

        // Last values actually written, for dedup. Sentinels force a first write.
        private int _sentLit = int.MinValue;
        private ushort _sentVelocity;
        private int _sentGear = int.MinValue;

        public CammusReportSender(CammusHidConnection connection)
        {
            _connection = connection;
            _thread = new Thread(Loop) { IsBackground = true, Name = "CammusHidWriter" };
            _thread.Start();
        }

        public void Submit(int lit, ushort velocity, int gear)
        {
            lock (_gate)
            {
                _lit = lit;
                _velocity = velocity;
                _gear = gear;
                _dirty = true;
            }
            _signal.Set();
        }

        private void Loop()
        {
            while (_running)
            {
                _signal.WaitOne(200);
                if (!_running) break;

                int lit; ushort velocity; int gear;
                lock (_gate)
                {
                    if (!_dirty) continue;
                    lit = _lit; velocity = _velocity; gear = _gear;
                    _dirty = false;
                }

                var spec = _connection.Model;
                if (spec == null || !_connection.IsConnected)
                {
                    // Force a re-send of whatever is current once we reconnect.
                    _sentLit = int.MinValue;
                    _sentGear = int.MinValue;
                    continue;
                }

                if (lit == _sentLit && velocity == _sentVelocity && gear == _sentGear)
                    continue;

                var report = spec.BuildReport(lit, velocity, gear);
                if (_connection.Write(report))
                {
                    _sentLit = lit; _sentVelocity = velocity; _sentGear = gear;
                    Thread.Sleep(MinWriteIntervalMs);
                }
            }
        }

        public void Dispose()
        {
            _running = false;
            _signal.Set();
            try { _thread.Join(500); } catch { }
            try { _signal.Dispose(); } catch { }
        }
    }
}
