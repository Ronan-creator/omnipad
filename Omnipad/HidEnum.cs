using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Omnipad {
    public class HidDevicePath {
        public string Path;
        public ushort Vid, Pid;
        public string Serial;
    }

    // Lists HID devices straight from Windows (SetupAPI), replacing hidapi's 2018-era hid_enumerate,
    // which found no controllers at all on some machines. hidapi is still used to open/read/write.
    public static class HidEnum {
        public const string HidInterfaceGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";
        static readonly Regex VidRx = new Regex(@"vid[_&](?:0002)?([0-9a-f]{4})", RegexOptions.IgnoreCase);
        static readonly Regex PidRx = new Regex(@"pid[_&]([0-9a-f]{4})", RegexOptions.IgnoreCase);

        public static List<HidDevicePath> Enumerate(ushort vendorId) {
            var result = new List<HidDevicePath>();
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);

            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == new IntPtr(-1)) {
                MainForm.Log("SetupDiGetClassDevs failed: " + new Win32Exception(Marshal.GetLastWin32Error()).Message);
                return result;
            }

            try {
                var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref iface); i++) {
                    string path = GetPath(set, ref iface);
                    if (path == null) continue;
                    var d = FromPath(path);
                    if (d == null || (vendorId != 0 && d.Vid != vendorId)) continue;
                    d.Serial = ReadSerial(path);
                    result.Add(d);
                }
            } finally {
                SetupDiDestroyDeviceInfoList(set);
            }
            return result;
        }

        public static HidDevicePath FromPath(string path) {
            Match v = VidRx.Match(path), p = PidRx.Match(path);
            if (!v.Success || !p.Success) return null;
            return new HidDevicePath {
                Path = path,
                Vid = Convert.ToUInt16(v.Groups[1].Value, 16),
                Pid = Convert.ToUInt16(p.Groups[1].Value, 16),
            };
        }

        // Windows instance id (as shown on the USB Devices page) -> device interface path that can be opened.
        public static string PathFromInstanceId(string id) {
            return @"\\?\" + id.Replace('\\', '#').ToLowerInvariant() + "#" + HidInterfaceGuid;
        }

        // Tries to open the device the same way hidapi does; returns null on success or the Windows error text.
        public static string ProbeOpen(string path) {
            using (SafeFileHandle h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero)) {
                if (!h.IsInvalid) return null;
                int err = Marshal.GetLastWin32Error();
                return new Win32Exception(err).Message + " (error " + err + ")";
            }
        }

        static string ReadSerial(string path) {
            using (SafeFileHandle h = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero)) {
                if (h.IsInvalid) return null;
                var buf = new StringBuilder(128);
                return HidD_GetSerialNumberString(h, buf, buf.Capacity * 2) ? buf.ToString() : null;
            }
        }

        static string GetPath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA iface) {
            int required;
            SetupDiGetDeviceInterfaceDetail(set, ref iface, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required <= 0) return null;
            IntPtr detail = Marshal.AllocHGlobal(required);
            try {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W
                if (!SetupDiGetDeviceInterfaceDetail(set, ref iface, detail, required, out required, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(detail + 4);
            } finally {
                Marshal.FreeHGlobal(detail);
            }
        }

        const int DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetSerialNumberString(SafeFileHandle h, StringBuilder buf, int len);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);
    }
}
