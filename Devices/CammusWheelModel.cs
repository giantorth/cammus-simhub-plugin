using System;

namespace CammusPlugin.Devices
{
    public enum CammusWheel
    {
        Unknown = 0,
        C5 = 1,
        C12 = 2,
    }

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

        // C12 LedCount is a UI-only assumption — firmware takes a 0..100 percent, not a count.
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

        public byte[] BuildReport(int lit, ushort velocity, int gear)
        {
            // bytes[0] is the HID report ID (0); HidSharp/Windows strips it before the wire.
            // The protocol payload (0xFC.. / 0xFA..) starts at bytes[1].
            var bytes = new byte[ReportSize + 1];
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

        private void BuildC5(byte[] bytes, int lit, ushort velocity, int gear)
        {
            if (lit < 0) lit = 0;
            if (lit > LedCount) lit = LedCount;
            // 10 == firmware-side all-LED blink (shift signal).
            int wireLit = (lit >= LedCount) ? 10 : lit;

            bytes[1] = 0xFC;
            bytes[2] = (byte)wireLit;
            bytes[3] = (byte)((velocity >> 8) & 0xFF);
            bytes[4] = (byte)(velocity & 0xFF);
            bytes[5] = (byte)((gear - 1) & 0xFF);
        }

        private void BuildC12(byte[] bytes, int lit, ushort velocity, int gear)
        {
            if (lit < 0) lit = 0;
            if (lit > LedCount) lit = LedCount;
            int pct = LedCount > 0 ? (100 * lit) / LedCount : 0;
            if (pct > 100) pct = 100;

            bytes[1] = 0xFA;
            bytes[2] = 0xFB;
            bytes[3] = 0xD4;
            bytes[4] = (byte)pct;
            bytes[5] = (byte)((velocity >> 8) & 0xFF);
            bytes[6] = (byte)(velocity & 0xFF);
            bytes[7] = (byte)((gear - 1) & 0xFF);
        }
    }
}
