using System;
using System.Collections.Generic;
using System.Text;

namespace CammusPlugin
{
    internal enum CammusLogLevel
    {
        Debug,
        Info,
        Warn,
        Error,
    }

    internal readonly struct CammusLogEntry
    {
        public CammusLogEntry(DateTime timestamp, CammusLogLevel level, string message)
        {
            Timestamp = timestamp;
            Level = level;
            Message = message;
        }

        public DateTime Timestamp { get; }
        public CammusLogLevel Level { get; }
        public string Message { get; }

        public override string ToString()
            => $"{Timestamp:HH:mm:ss.fff} {Level.ToString().ToUpperInvariant(),-5} {Message}";
    }

    // Logs to SimHub's logger and mirrors every line into an in-memory ring buffer
    // so the plugin's own UI can show recent HID / diagnostics output without the
    // user having to dig through SimHub's log files.
    internal static class CammusLog
    {
        private const int MaxEntries = 500;

        private static readonly object _gate = new object();
        private static readonly Queue<CammusLogEntry> _entries = new Queue<CammusLogEntry>(MaxEntries);

        // Raised after a new entry is appended. UI subscribes to refresh on demand;
        // handlers must marshal to their own dispatcher.
        public static event Action? EntryAdded;

        public static void Info(string message)
        {
            Capture(CammusLogLevel.Info, message);
            try { SimHub.Logging.Current.Info(message); } catch { }
        }

        public static void Debug(string message)
        {
            Capture(CammusLogLevel.Debug, message);
            try { SimHub.Logging.Current.Debug(message); } catch { }
        }

        public static void Warn(string message)
        {
            Capture(CammusLogLevel.Warn, message);
            try { SimHub.Logging.Current.Warn(message); } catch { }
        }

        public static void Error(string message)
        {
            Capture(CammusLogLevel.Error, message);
            try { SimHub.Logging.Current.Error(message); } catch { }
        }

        // Snapshot of the current buffer, oldest first. Safe to call from any thread.
        public static CammusLogEntry[] Snapshot()
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }

        // Renders the current buffer as newline-separated text (for the log viewer / clipboard).
        public static string SnapshotText()
        {
            var sb = new StringBuilder();
            foreach (var entry in Snapshot())
                sb.AppendLine(entry.ToString());
            return sb.ToString();
        }

        public static void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
            RaiseEntryAdded();
        }

        private static void Capture(CammusLogLevel level, string message)
        {
            lock (_gate)
            {
                if (_entries.Count >= MaxEntries) _entries.Dequeue();
                _entries.Enqueue(new CammusLogEntry(DateTime.Now, level, message ?? string.Empty));
            }
            RaiseEntryAdded();
        }

        private static void RaiseEntryAdded()
        {
            try { EntryAdded?.Invoke(); } catch { }
        }
    }
}
