using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HidSharp;
using Microsoft.Win32;

namespace CammusPlugin.Devices
{
    // Read-only USB/HID inspection used by the diagnostics panel. Two views:
    //   * HidSharp enumeration — what the plugin can actually open and write to.
    //   * Windows registry (Enum\USB) — what the OS thinks is plugged in, even if
    //     HidSharp can't open it (held by another process, no HID interface, etc).
    // A device present in the registry but absent from the HidSharp list is the
    // classic "plugged in but unusable" case this panel exists to surface.
    internal static class CammusUsbDiagnostics
    {
        // All known Cammus wheels share this vendor ID.
        private const int CammusVid = 0x3416;
        private const string UsbEnumPath = @"SYSTEM\CurrentControlSet\Enum\USB";

        // One-line summary suitable for the startup log.
        public static string BuildStartupSummary()
        {
            int hidTotal = 0, cammusHid = 0, cammusRegistry = 0;
            try { hidTotal = DeviceList.Local.GetHidDevices().Count(); } catch { }
            try { cammusHid = DeviceList.Local.GetHidDevices(CammusVid).Count(); } catch { }
            try { cammusRegistry = CountCammusRegistryEntries(); } catch { }

            return $"[Cammus] USB diagnostics: {hidTotal} HID device(s) enumerated, " +
                   $"{cammusHid} matching Cammus VID 0x{CammusVid:X4}, " +
                   $"{cammusRegistry} Cammus entry(ies) in registry";
        }

        // Logs the startup summary plus, when nothing matched, the full device list
        // so the SimHub log captures the state at boot for support.
        public static void LogStartupReport()
        {
            CammusLog.Info(BuildStartupSummary());
            try
            {
                if (DeviceList.Local.GetHidDevices(CammusVid).Any()) return;
            }
            catch { }

            CammusLog.Warn("[Cammus] No Cammus HID device found at startup — dumping diagnostics:");
            foreach (var line in BuildHidDevicesReport().Split('\n'))
            {
                var trimmed = line.TrimEnd('\r');
                if (trimmed.Length > 0) CammusLog.Debug("[Cammus] " + trimmed);
            }
        }

        // Human-readable enumeration of every HID device HidSharp can see, with the
        // Cammus matches called out and a note on whether each can be opened.
        public static string BuildHidDevicesReport()
        {
            var sb = new StringBuilder();
            IReadOnlyList<HidDevice> devices;
            try
            {
                devices = DeviceList.Local.GetHidDevices().ToList();
            }
            catch (Exception ex)
            {
                return $"HID enumeration failed: {ex.GetType().Name}: {ex.Message}";
            }

            var cammus = devices.Where(d => SafeVid(d) == CammusVid).ToList();
            sb.AppendLine($"HID devices: {devices.Count} total, {cammus.Count} matching Cammus VID 0x{CammusVid:X4}");
            sb.AppendLine();

            if (cammus.Count > 0)
            {
                sb.AppendLine("== Cammus devices ==");
                foreach (var dev in cammus)
                    AppendHidDevice(sb, dev, probeOpen: true);
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("== No Cammus devices in the HID list ==");
                sb.AppendLine("(Check the registry section below — the wheel may be plugged in");
                sb.AppendLine(" but held by another process or exposing no HID interface.)");
                sb.AppendLine();
            }

            sb.AppendLine("== All HID devices ==");
            foreach (var dev in devices.OrderBy(SafeVid).ThenBy(SafePid))
                AppendHidDevice(sb, dev, probeOpen: false);

            return sb.ToString();
        }

        // Walks HKLM\...\Enum\USB and reports every USB function present, with the
        // Cammus entries (VID_3416) expanded to show their instance details.
        public static string BuildRegistryReport()
        {
            var sb = new StringBuilder();
            RegistryKey? usb = null;
            try
            {
                usb = Registry.LocalMachine.OpenSubKey(UsbEnumPath, writable: false);
            }
            catch (Exception ex)
            {
                return $"Registry read failed ({UsbEnumPath}): {ex.GetType().Name}: {ex.Message}";
            }

            if (usb == null)
                return $"Registry key not found: HKLM\\{UsbEnumPath}";

            using (usb)
            {
                string[] functions;
                try { functions = usb.GetSubKeyNames(); }
                catch (Exception ex) { return $"Registry enumeration failed: {ex.Message}"; }

                var cammusFns = functions.Where(IsCammusFunction).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
                sb.AppendLine($"Registry USB functions: {functions.Length} total, {cammusFns.Count} Cammus (VID_3416)");
                sb.AppendLine($"(HKLM\\{UsbEnumPath})");
                sb.AppendLine();

                if (cammusFns.Count > 0)
                {
                    sb.AppendLine("== Cammus USB entries ==");
                    foreach (var fn in cammusFns)
                        AppendRegistryFunction(sb, usb, fn, expand: true);
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine("== No Cammus (VID_3416) entries in the registry ==");
                    sb.AppendLine("(The wheel is not enumerated by Windows — check the cable,");
                    sb.AppendLine(" power, and that it is in PC / SimHub mode.)");
                    sb.AppendLine();
                }

                sb.AppendLine("== All USB functions ==");
                foreach (var fn in functions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine("  " + fn + (IsCammusFunction(fn) ? "   <-- Cammus" : ""));
            }

            return sb.ToString();
        }

        private static void AppendHidDevice(StringBuilder sb, HidDevice dev, bool probeOpen)
        {
            int vid = SafeVid(dev), pid = SafePid(dev);
            var spec = CammusModelSpec.FromPid(pid);
            string tag = (vid == CammusVid)
                ? "  [CAMMUS" + (spec != null ? " " + spec.DisplayName : " unknown PID") + "]"
                : "";

            sb.AppendLine($"  VID 0x{vid:X4}  PID 0x{pid:X4}{tag}");
            sb.AppendLine($"    name : {SafeString(dev, d => d.GetFriendlyName())}");
            sb.AppendLine($"    mfg  : {SafeString(dev, d => d.GetManufacturer())}");
            sb.AppendLine($"    prod : {SafeString(dev, d => d.GetProductName())}");
            sb.AppendLine($"    path : {SafeString(dev, d => d.DevicePath)}");

            if (probeOpen)
            {
                string open;
                try
                {
                    open = dev.TryOpen(out var stream) && stream != null
                        ? "yes"
                        : "NO (open failed — likely held by another process)";
                    stream?.Dispose();
                }
                catch (Exception ex)
                {
                    open = $"NO ({ex.GetType().Name}: {ex.Message})";
                }
                sb.AppendLine($"    open : {open}");
            }
        }

        private static void AppendRegistryFunction(StringBuilder sb, RegistryKey usb, string functionName, bool expand)
        {
            sb.AppendLine("  " + functionName);
            if (!expand) return;

            RegistryKey? fnKey = null;
            try { fnKey = usb.OpenSubKey(functionName, writable: false); }
            catch (Exception ex) { sb.AppendLine($"    (open failed: {ex.Message})"); return; }
            if (fnKey == null) return;

            using (fnKey)
            {
                string[] instances;
                try { instances = fnKey.GetSubKeyNames(); }
                catch (Exception ex) { sb.AppendLine($"    (instance enum failed: {ex.Message})"); return; }

                foreach (var instance in instances)
                {
                    sb.AppendLine($"    instance: {instance}");
                    try
                    {
                        using (var instKey = fnKey.OpenSubKey(instance, writable: false))
                        {
                            if (instKey == null) continue;
                            AppendRegistryValue(sb, instKey, "FriendlyName");
                            AppendRegistryValue(sb, instKey, "DeviceDesc");
                            AppendRegistryValue(sb, instKey, "Mfg");
                            AppendRegistryValue(sb, instKey, "Service");
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"      (read failed: {ex.Message})");
                    }
                }
            }
        }

        private static void AppendRegistryValue(StringBuilder sb, RegistryKey key, string name)
        {
            object? value;
            try { value = key.GetValue(name); }
            catch { return; }
            if (value == null) return;
            // Many of these are "@oem;...,%desc%" indirection strings; show the raw
            // value — it's still enough to identify the device for diagnostics.
            sb.AppendLine($"      {name}: {value}");
        }

        private static int CountCammusRegistryEntries()
        {
            using (var usb = Registry.LocalMachine.OpenSubKey(UsbEnumPath, writable: false))
            {
                if (usb == null) return 0;
                return usb.GetSubKeyNames().Count(IsCammusFunction);
            }
        }

        private static bool IsCammusFunction(string functionName)
            => functionName.IndexOf("VID_3416", StringComparison.OrdinalIgnoreCase) >= 0;

        private static int SafeVid(HidDevice dev)
        {
            try { return dev.VendorID; } catch { return -1; }
        }

        private static int SafePid(HidDevice dev)
        {
            try { return dev.ProductID; } catch { return -1; }
        }

        private static string SafeString(HidDevice dev, Func<HidDevice, string> getter)
        {
            try
            {
                var s = getter(dev);
                return string.IsNullOrEmpty(s) ? "(none)" : s;
            }
            catch (Exception ex)
            {
                return $"(unavailable: {ex.GetType().Name})";
            }
        }
    }
}
