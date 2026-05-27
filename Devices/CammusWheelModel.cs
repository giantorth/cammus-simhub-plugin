using System;

namespace CammusPlugin.Devices
{
    public enum CammusWheel
    {
        Unknown = 0,
        C5 = 1,
        C12 = 2,
    }

    /// <summary>
    /// Per-model hardware spec + HID report builder. The Cammus protocol
    /// (reverse-engineered in monocoque commit a810044) packs RPM + velocity
    /// + gear into a single fixed-size HID report; the firmware decides how
    /// to drive its onboard LEDs from that.
    /// </summary>
    internal sealed class CammusModelSpec
    {
        public CammusWheel Wheel { get; }
        public int Vid { get; }
        public int Pid { get; }
        public int LedCount { get; }
        public int ReportSize { get; }
        public string DescriptorUniqueId { get; }
        public string DeviceFolderName { get; }
        public string DisplayName { get; }

        private CammusModelSpec(
            CammusWheel wheel, int vid, int pid, int ledCount, int reportSize,
            string descriptorUniqueId, string deviceFolderName, string displayName)
        {
            Wheel = wheel;
            Vid = vid;
            Pid = pid;
            LedCount = ledCount;
            ReportSize = reportSize;
            DescriptorUniqueId = descriptorUniqueId;
            DeviceFolderName = deviceFolderName;
            DisplayName = displayName;
        }

        public static readonly CammusModelSpec C5 = new CammusModelSpec(
            CammusWheel.C5, 0x3416, 0x1021,
            ledCount: 9, reportSize: 14,
            descriptorUniqueId: "e1b082bd-ee26-4a70-bdee-cffdf5637117",
            deviceFolderName: "Cammus C5",
            displayName: "Cammus C5");

        // LedCount assumption — see plan: confirm against physical hardware
        // (the C12 firmware itself accepts a percentage so the count only
        // affects the SimHub effects UI).
        public static readonly CammusModelSpec C12 = new CammusModelSpec(
            CammusWheel.C12, 0x3416, 0x1023,
            ledCount: 10, reportSize: 16,
            descriptorUniqueId: "4726f206-5377-4510-b50a-9c38dec2bee5",
            deviceFolderName: "Cammus C12",
            displayName: "Cammus C12");

        public static readonly CammusModelSpec[] All = { C5, C12 };

        public static CammusModelSpec? FromPid(int pid)
        {
            foreach (var spec in All)
                if (spec.Pid == pid) return spec;
            return null;
        }

        public static CammusModelSpec? FromDescriptorUniqueId(string? uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            foreach (var spec in All)
                if (string.Equals(spec.DescriptorUniqueId, uid, StringComparison.OrdinalIgnoreCase))
                    return spec;
            return null;
        }

        /// <summary>
        /// Build the HID report for this model.
        /// <paramref name="lit"/> is the count of non-black LEDs (0..LedCount).
        /// </summary>
        public byte[] BuildReport(int lit, ushort velocity, int gear)
        {
            var bytes = new byte[ReportSize];
            // gear-1 underflows on input 0 — caller is responsible for
            // mapping "no gear"/"R"/"N" to 1 (so gear-1=0 hits the firmware's
            // "no gear" cell). Clamp here defensively.
            if (gear < 1) gear = 1;

            switch (Wheel)
            {
                case CammusWheel.C5:
                    BuildC5(bytes, lit, velocity, gear);
                    break;
                case CammusWheel.C12:
                    BuildC12(bytes, lit, velocity, gear);
                    break;
            }
            return bytes;
        }

        // C5: 14 bytes. bytes[1] is a lit-LED count 0..9; sending 10 makes the
        // whole strip blink (firmware-side shift signal). When SimHub's effects
        // light every available LED we promote lit==LedCount → 10 so the user's
        // chosen redline color reaches the wheel as the all-blink shift.
        private void BuildC5(byte[] bytes, int lit, ushort velocity, int gear)
        {
            if (lit < 0) lit = 0;
            if (lit > LedCount) lit = LedCount;
            int wireLit = (lit >= LedCount) ? 10 : lit;

            bytes[0] = 0xFC;
            bytes[1] = (byte)wireLit;
            bytes[2] = (byte)((velocity >> 8) & 0xFF);
            bytes[3] = (byte)(velocity & 0xFF);
            bytes[4] = (byte)((gear - 1) & 0xFF);
        }

        // C12: 16 bytes. bytes[3] is RPM percent 0..100. We approximate the
        // monocoque source's rpm/maxrpm*100 by deriving the percent from the
        // count of lit LEDs in SimHub's computed effect — gives the user's
        // configured redline curve a natural mapping without needing the raw
        // RPM here.
        private void BuildC12(byte[] bytes, int lit, ushort velocity, int gear)
        {
            if (lit < 0) lit = 0;
            if (lit > LedCount) lit = LedCount;
            int pct = LedCount > 0 ? (100 * lit) / LedCount : 0;
            if (pct > 100) pct = 100;

            bytes[0] = 0xFA;
            bytes[1] = 0xFB;
            bytes[2] = 0xD4;
            bytes[3] = (byte)pct;
            bytes[4] = (byte)((velocity >> 8) & 0xFF);
            bytes[5] = (byte)(velocity & 0xFF);
            bytes[6] = (byte)((gear - 1) & 0xFF);
        }
    }
}
