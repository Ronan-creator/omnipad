using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Numerics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Timers;

using System.Net.NetworkInformation;
using System.Diagnostics;

using static Omnipad.HIDapi;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using System.Net;
using System.Configuration;
using System.Net.Http;
using System.IO;
using System.Windows.Forms;

using System.ServiceProcess;

namespace Omnipad {
    // Custom timer class because system timers have a limit of 15.6ms
    class HighResTimer {
        double interval = 0;
        double frequency = 0;

        Thread thread;

        public delegate void ActionDelegate();
        ActionDelegate func;

        bool run = false;

        public HighResTimer(double f, ActionDelegate a) {
            frequency = f;
            interval = 1.0 / f;

            func = a;
        }

        public void Start() {
            run = true;
            thread = new Thread(new ThreadStart(Run));
            thread.IsBackground = true;
            thread.Start();
        }

        void Run() {
            while (run) {
                func();
                int timeToSleep = (int)(interval * 1000);
                Thread.Sleep(timeToSleep);
            }
        }

        public void Stop() {
            run = false;
        }
    }

    class Program {
        public static PhysicalAddress btMAC = new PhysicalAddress(new byte[] { 0, 0, 0, 0, 0, 0 });
        public static UdpServer server;
        static double pollsPerSecond = 120.0;

        public static ViGEmClient emClient;

        private static readonly HttpClient client = new HttpClient();

        public static JoyconManager mgr;
        static HighResTimer timer;

        static MainForm form;
        public static MainForm Form => form;
        public static bool JustUpdated;

        public static void Start() {
            MainForm.Log("---- Omnipad starting ----");
            // HidGuardian (used by v5) is discontinued; HidHide replaces it.
            HidGuardian.Init(form); // must run before the first scan, or the controller is "Access denied"
            HidHide.Init(form, Boolean.Parse(ConfigurationManager.AppSettings["UseHidHide"] ?? "false"));

            try {
                emClient = new ViGEmClient(); // Manages emulated XInput
            } catch (Exception e) {
                MainForm.Log("ViGEm error: " + e);
                MessageBox.Show("Could not connect to the ViGEmBus driver, so no virtual Xbox controller can be created.\n\nInstall ViGEmBus (https://github.com/nefarius/ViGEmBus/releases), restart, and try again.\n\n" + e.Message, "Omnipad");
                Environment.Exit(1);
            }

            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces()) {
                // Get local BT host MAC
                if (nic.NetworkInterfaceType != NetworkInterfaceType.FastEthernetFx && nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211) {
                    if (nic.Name.Split()[0] == "Bluetooth") {
                        btMAC = nic.GetPhysicalAddress();
                    }
                }
            }

            mgr = new JoyconManager();
            mgr.form = form;
            mgr.Awake();
            mgr.CheckForNewControllers();
            mgr.Start();

            server = new UdpServer(mgr.j);
            server.form = form;

            server.Start(IPAddress.Parse(ConfigurationManager.AppSettings["IP"]), Int32.Parse(ConfigurationManager.AppSettings["Port"]));
            timer = new HighResTimer(pollsPerSecond, new HighResTimer.ActionDelegate(mgr.Update));
            timer.Start();

            form.AppendTextBox("Ready.\r\n");
        }

        public static void Stop() {
            server.Stop();
            timer.Stop();
            mgr.OnApplicationQuit();
            HidHide.UnhideAll();
            HidGuardian.Shutdown();

        }

        public static bool IsAdmin {
            get {
                using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
                    return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        static Mutex singleInstance;
        public static bool StartMinimized;

        // Relaunches Omnipad elevated (needed to change HidHide settings) and closes this copy.
        public static void RestartAsAdmin() {
            try {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--restarted") { Verb = "runas", UseShellExecute = true });
            } catch {
                return; // user said no to the UAC prompt
            }
            singleInstance?.ReleaseMutex();
            singleInstance = null;
            Application.Exit();
        }

        [STAThread] // required for the clipboard and other Windows UI features
        static void Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Only one copy may own the controller. When restarting as admin, wait for the old copy to close.
            singleInstance = new Mutex(false, "Omnific.Omnipad.SingleInstance");
            bool restarted = args.Contains("--restarted");
            if (!singleInstance.WaitOne(restarted ? 10000 : 0)) {
                MessageBox.Show("Omnipad is already running. Look for its icon in the system tray (next to the clock).", "Omnipad");
                return;
            }

            // An older BetterJoy (or BetterJoy-based build) running at the same time fights over the controller.
            var others = Process.GetProcesses().Where(p => p.Id != Process.GetCurrentProcess().Id &&
                (p.ProcessName.Equals("BetterJoyForCemu", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("BetterJoy", StringComparison.OrdinalIgnoreCase) ||
                 p.ProcessName.Equals("Hardline", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("Omnipad", StringComparison.OrdinalIgnoreCase))).ToList();
            if (others.Count > 0 &&
                MessageBox.Show("BetterJoy (or another copy of Omnipad) is still running and will fight Omnipad for the controller.\n\nClose it now?", "Omnipad", MessageBoxButtons.YesNo) == DialogResult.Yes) {
                foreach (var p in others) {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }

            bool cfgMin;
            JustUpdated = args.Contains("--updated");
            StartMinimized = args.Contains("--minimized") || (bool.TryParse(ConfigurationManager.AppSettings["StartMinimized"], out cfgMin) && cfgMin);

            form = new MainForm();
            Application.Run(form);
        }
    }
}