using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;
using System.Windows.Forms;

using static Omnipad.HIDapi;

namespace Omnipad {
    public class JoyconManager {
        public bool EnableIMU = true;
        public bool EnableLocalize = false;

        private const ushort vendor_id = 0x57e;
        private const ushort product_l = 0x2006;
        private const ushort product_r = 0x2007;
        private const ushort product_pro = 0x2009;

        // Bluetooth HID devices have this service GUID in their path; USB ones don't.
        private const string BluetoothHidGuid = "00001124-0000-1000-8000-00805f9b34fb";

        public List<Joycon> j; // Array of all connected Joy-Cons
        static JoyconManager instance;

        public MainForm form;

        System.Timers.Timer controllerCheck;
        readonly object scanLock = new object();

        // One virtual Xbox pad per physical Pro Controller, keyed by its Bluetooth MAC.
        // It survives switching between USB and Bluetooth, so Steam keeps seeing the same controller.
        readonly Dictionary<string, VirtualPad> pads = new Dictionary<string, VirtualPad>();
        int lastSeenCount = -1;
        public bool AccessDenied; // Windows refused to open a controller (usually HidHide)
        readonly HashSet<string> manualPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // picked on the USB Devices page
        readonly HashSet<string> openFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Lets the user pick a device by hand on the USB Devices page.
        public void AddManual(string path) {
            lock (manualPaths) manualPaths.Add(path);
            openFailures.Remove(path);
            form.AppendTextBox("Trying " + path + " as a controller...\r\n");
            ThreadPool.QueueUserWorkItem(_ => CheckForNewControllers());
        }
        readonly HashSet<string> seenPaths = new HashSet<string>(); // for logging each device once

        readonly bool showAsXInput = Boolean.Parse(ConfigurationManager.AppSettings["ShowAsXInput"]);
        readonly bool toRumble = Boolean.Parse(ConfigurationManager.AppSettings["EnableRumble"]);
        readonly int graceSeconds = Int32.Parse(ConfigurationManager.AppSettings["ReconnectGraceSeconds"] ?? "30");

        public static JoyconManager Instance {
            get { return instance; }
        }

        public void Awake() {
            instance = this;
            j = new List<Joycon>();
            HIDapi.hid_init();
        }

        public void Start() {
            controllerCheck = new System.Timers.Timer(2000); // check every 2 seconds
            controllerCheck.Elapsed += CheckForNewControllersTime;
            controllerCheck.Start();
        }

        bool ControllerAlreadyAdded(string path) {
            foreach (Joycon v in j)
                if (string.Equals(v.path, path, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static string MacFromSerial(string serial) {
            if (serial == null) return null;
            string s = serial.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
            if (s.Length != 12 || !s.All(Uri.IsHexDigit) || s == "000000000000" || s == "000000000001") return null;
            return s;
        }

        void CleanUp() { // removes dropped controllers from list
            foreach (Joycon v in j.Where(x => x.state == Joycon.state_.DROPPED).ToList()) {
                if (v.other != null)
                    v.other.other = null; // The other of the other is the joycon itself

                VirtualPad pad = v.xin;
                v.Detach();

                if (pad != null && pad.Mac != null && pads.ContainsKey(pad.Mac)) {
                    // Keep the virtual pad alive for a while so a reconnect (USB <-> Bluetooth) is seamless.
                    pad.SendNeutral();
                    pad.Owner = null;
                    pad.OrphanedSince = DateTime.Now;
                } else if (pad != null) {
                    pad.Dispose();
                }
                v.xin = null;

                j.Remove(v);
                Shortcuts.Forget(v);
                GyroMouse.Forget(v);
                form.AppendTextBox("Removed dropped controller. Can be reconnected.\r\n");
                form.ControllersChanged();
            }

            foreach (VirtualPad pad in pads.Values.ToList()) {
                if (pad.OrphanedSince.HasValue && (DateTime.Now - pad.OrphanedSince.Value).TotalSeconds > graceSeconds) {
                    pad.Dispose();
                    pads.Remove(pad.Mac);
                    form.AppendTextBox("Controller did not come back - removed its virtual pad.\r\n");
                }
            }
        }

        void CheckForNewControllersTime(Object source, ElapsedEventArgs e) {
            if (Config.Value("ProgressiveScan")) {
                CheckForNewControllers();
            }
        }

        public void CheckForNewControllers() {
            // The timer fires every 2s; a scan that is still attaching a controller must not overlap the next one.
            if (!Monitor.TryEnter(scanLock)) return;
            try {
                Scan();
            } catch (Exception ex) {
                form.AppendTextBox("Scan error: " + ex.Message + "\r\n");
                MainForm.Log(ex.ToString());
            } finally {
                Monitor.Exit(scanLock);
            }
        }

        void Scan() {
            CleanUp();

            bool foundNew = false;

            var devices = HidEnum.Enumerate(vendor_id);
            lock (manualPaths)
                foreach (string mp in manualPaths)
                    if (!devices.Any(d => string.Equals(d.Path, mp, StringComparison.OrdinalIgnoreCase)))
                        devices.Add(HidEnum.FromPath(mp) ?? new HidDevicePath { Path = mp, Pid = product_pro });

            foreach (var dev in devices) {
                ushort pid = dev.Pid;
                bool manual;
                lock (manualPaths) manual = manualPaths.Contains(dev.Path);
                if (seenPaths.Add(dev.Path))
                    MainForm.Log(string.Format("Found HID device vid={0:X4} pid={1:X4} serial={2} manual={3} path={4}", dev.Vid, pid, dev.Serial, manual, dev.Path));
                if (pid != product_l && pid != product_r && pid != product_pro) {
                    if (!manual) continue;
                    pid = product_pro; // picked by hand: treat unknown Nintendo-protocol pads as a Pro Controller
                }
                if (ControllerAlreadyAdded(dev.Path)) continue;

                bool isUSB = dev.Path.IndexOf(BluetoothHidGuid, StringComparison.OrdinalIgnoreCase) < 0;
                string mac = isUSB ? null : MacFromSerial(dev.Serial);

                // Same controller is already running over USB: leave the Bluetooth side alone.
                if (mac != null && j.Any(x => x.isUSB && x.mac == mac)) continue;

                IntPtr handle = HIDapi.hid_open_path(dev.Path);
                if (handle == IntPtr.Zero) {
                    if (openFailures.Add(dev.Path)) {
                        string why = HidEnum.ProbeOpen(dev.Path) ?? "hidapi refused it, but Windows could open it";
                        if (why.Contains("error 5)")) AccessDenied = true;
                        form.AppendTextBox("Found a controller but could not open it: " + why + "\r\n");
                    }
                    continue;
                }
                openFailures.Remove(dev.Path);
                AccessDenied = false;
                HIDapi.hid_set_nonblocking(handle, 1);

                bool isLeft = pid != product_r;
                string kind = pid == product_l ? "Left Joy-Con" : pid == product_r ? "Right Joy-Con" : "Pro controller";
                form.AppendTextBox(kind + " connected (" + (isUSB ? "USB" : "Bluetooth") + ").\r\n");

                Joycon jc = new Joycon(handle, EnableIMU, EnableLocalize & EnableIMU, 0.05f, isLeft, dev.Path, j.Count, pid == product_pro, isUSB);
                jc.mac = mac;
                jc.form = form;
                if (pid != product_pro)
                    jc.CreateOwnPad();

                j.Add(jc);
                foundNew = true;
            }

            if (foundNew) form.ControllersChanged();
            if (devices.Count != lastSeenCount) {
                lastSeenCount = devices.Count;
                MainForm.Log("Scan: Windows lists " + devices.Count + " Nintendo HID device(s).");
            }

            int found = 0;
            int minPadID = 10;
            foreach (Joycon v in j) { // current system is designed for a maximum of two joycons connected to the PC
                if (!v.isPro) {
                    found++;
                    minPadID = Math.Min(v.PadId, minPadID);
                }
                v.LED = (byte)(0x1 << Math.Min(v.PadId, 3));
            }

            if (found == 2 && foundNew) {
                form.AppendTextBox("Both joycons successfully found.\r\n");
                Joycon temp = null;
                foreach (Joycon v in j) {
                    if (!v.isPro) {
                        v.LED = (byte)(0x1 << minPadID);

                        if (temp == null)
                            temp = v;
                        else {
                            temp.other = v;
                            v.other = temp;

                            if (temp.xin != null) {
                                temp.xin.Dispose();
                                temp.xin = null;
                            }
                        }
                    }
                } // Join up the two joycons
            }

            foreach (Joycon jc in j.ToList()) { // Connect device straight away
                if (jc.state != Joycon.state_.NOT_ATTACHED || jc.retired) continue;

                try {
                    jc.Attach(leds_: jc.LED);
                    if (jc.isPro)
                        AssignPad(jc);
                    else if (jc.xin != null)
                        jc.xin.Connect();
                    jc.Begin();
                    form.AppendTextBox("Controller ready" + (jc.mac != null ? " (" + jc.mac + ")" : "") + ".\r\n");
                    // Hide the real device from Steam/games only once we have it open and running.
                    HidHide.Hide(jc.path);
                    form.ControllersChanged();
                } catch (Exception ex) {
                    form.AppendTextBox("Failed to start controller: " + ex.Message + "\r\n");
                    MainForm.Log(ex.ToString());
                    jc.state = Joycon.state_.DROPPED;
                }
            }
        }

        // Gives a Pro Controller connection its virtual pad, reusing the pad of the same physical controller if it has one.
        void AssignPad(Joycon jc) {
            if (jc.mac != null) {
                foreach (Joycon old in j.Where(o => o != jc && o.isPro && o.mac == jc.mac).ToList()) {
                    form.AppendTextBox("Same controller is now on " + (jc.isUSB ? "USB" : "Bluetooth") + " - switching over.\r\n");
                    old.Retire();
                    j.Remove(old);
                    Shortcuts.Forget(old);
                    form.ControllersChanged();
                }
            }

            if (!showAsXInput) return;

            VirtualPad pad = null;
            if (jc.mac != null)
                pads.TryGetValue(jc.mac, out pad);

            if (pad == null) {
                pad = new VirtualPad(Program.emClient) { Mac = jc.mac };
                if (toRumble) {
                    VirtualPad p = pad;
                    pad.Rumble += (large, small) => p.Owner?.ReceiveRumble(large, small);
                }
                {
                    VirtualPad p2 = pad;
                    pad.Slot += n => p2.Owner?.SetPlayerSlot(n);
                }
                if (jc.mac != null)
                    pads[jc.mac] = pad;
                pad.Connect();
            } else {
                form.AppendTextBox("Reusing the same virtual controller.\r\n");
            }

            pad.Owner = jc;
            pad.OrphanedSince = null;
            jc.xin = pad;
            if (pad.LastSlot >= 0) jc.SetPlayerSlot(pad.LastSlot); // keep the same player light after a USB/Bluetooth switch
        }

        public void Update() {
            for (int i = 0; i < j.Count; ++i)
                j[i].Update();
        }

        public void OnApplicationQuit() {
            controllerCheck?.Stop();
            // Don't block forever if a scan is mid-way (it may be waiting on the UI thread we're on).
            bool locked = Monitor.TryEnter(scanLock, 2000);
            try {
                foreach (Joycon v in j.ToList()) {
                    VirtualPad pad = v.xin;
                    v.Detach();
                    pad?.Dispose();
                }
                foreach (VirtualPad pad in pads.Values)
                    pad.Dispose();
                pads.Clear();
            } finally {
                if (locked) Monitor.Exit(scanLock);
            }
            HIDapi.hid_exit();
        }
    }
}
