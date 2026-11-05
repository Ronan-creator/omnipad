using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using System.Threading;

namespace Omnipad {
    public class PnpDevice {
        public string Id;          // Windows instance id, e.g. USB\VID_057E&PID_2009\000000000001
        public string Name;
        public string Class;       // HIDClass, USB, Bluetooth, Keyboard, ...
        public string Status;
        public string Vid, Pid;
        public string Bus;         // USB, Bluetooth, HID
        public DateTime FirstSeen;
        public bool IsNew => (DateTime.Now - FirstSeen).TotalSeconds < 60;

        public bool IsNintendo => Vid == "057E";
        public bool IsGameController {
            get {
                if (IsNintendo) return true;
                string n = (Name ?? "").ToLowerInvariant();
                return n.Contains("game") || n.Contains("controller") || n.Contains("xbox") || n.Contains("joy") || n.Contains("pad");
            }
        }
        public string VidPid => Vid != null ? Vid + ":" + (Pid ?? "????") : "";
    }

    // Watches every USB, Bluetooth and HID device Windows knows about and reports what appears and disappears.
    // Refreshes when Windows says devices changed (WM_DEVICECHANGE) and every few seconds as a fallback.
    public class DeviceMonitor {
        static readonly Regex VidRx = new Regex(@"VID[_&]?(?:0002)?([0-9A-F]{4})", RegexOptions.IgnoreCase);
        static readonly Regex PidRx = new Regex(@"PID[_&]?([0-9A-F]{4})", RegexOptions.IgnoreCase);

        readonly Dictionary<string, PnpDevice> devices = new Dictionary<string, PnpDevice>(StringComparer.OrdinalIgnoreCase);
        readonly object sync = new object();
        int refreshing;
        bool first = true;
        Timer timer;

        public event Action<PnpDevice> Added;
        public event Action<PnpDevice> Removed;
        public event Action Refreshed;

        public List<PnpDevice> Snapshot() {
            lock (sync) return devices.Values.ToList();
        }

        public void Start() {
            timer = new Timer(_ => RefreshAsync(), null, 0, 5000);
        }

        public void Stop() {
            timer?.Dispose();
        }

        public void RefreshAsync() {
            if (Interlocked.Exchange(ref refreshing, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ => {
                try { Refresh(); } catch (Exception e) { MainForm.Log("Device monitor: " + e.Message); } finally { refreshing = 0; }
            });
        }

        void Refresh() {
            var now = new Dictionary<string, PnpDevice>(StringComparer.OrdinalIgnoreCase);
            using (var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name, PNPClass, Status FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%' OR DeviceID LIKE 'HID%' OR DeviceID LIKE 'BTHENUM%' OR DeviceID LIKE 'BTHLE%'")) {
                foreach (ManagementObject mo in searcher.Get()) {
                    string id = mo["DeviceID"] as string;
                    if (id == null) continue;
                    var d = new PnpDevice {
                        Id = id,
                        Name = (mo["Name"] as string) ?? "(unnamed device)",
                        Class = (mo["PNPClass"] as string) ?? "",
                        Status = (mo["Status"] as string) ?? "",
                    };
                    Match m = VidRx.Match(id);
                    if (m.Success) d.Vid = m.Groups[1].Value.ToUpperInvariant();
                    m = PidRx.Match(id);
                    if (m.Success) d.Pid = m.Groups[1].Value.ToUpperInvariant();
                    d.Bus = id.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ? "USB"
                          : id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) || id.Contains("00001124-0000-1000-8000-00805F9B34FB") ? "Bluetooth"
                          : "HID";
                    if (d.Bus == "HID" && id.IndexOf("00001124-0000-1000-8000-00805F9B34FB", StringComparison.OrdinalIgnoreCase) >= 0) d.Bus = "Bluetooth";
                    now[id] = d;
                }
            }

            var added = new List<PnpDevice>();
            var removed = new List<PnpDevice>();
            lock (sync) {
                foreach (var d in now.Values) {
                    if (devices.TryGetValue(d.Id, out PnpDevice old)) {
                        d.FirstSeen = old.FirstSeen;
                    } else {
                        d.FirstSeen = first ? DateTime.MinValue : DateTime.Now;
                        if (!first) added.Add(d);
                    }
                }
                foreach (var d in devices.Values)
                    if (!now.ContainsKey(d.Id)) removed.Add(d);
                devices.Clear();
                foreach (var d in now.Values) devices[d.Id] = d;
                first = false;
            }

            foreach (var d in added) Added?.Invoke(d);
            foreach (var d in removed) Removed?.Invoke(d);
            Refreshed?.Invoke();
        }
    }
}
