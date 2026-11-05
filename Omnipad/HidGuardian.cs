using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using Microsoft.Win32;

namespace Omnipad {
    // HidGuardian is the older device-blocking driver that BetterJoy v5 used (HidHide replaced it). If it is installed,
    // every controller BetterJoy ever saw is on its "affected" list, and Windows answers "Access is denied" to any
    // program that isn't on its whitelist - which is why Omnipad couldn't open the controller while v5 could.
    // Two ways to talk to it: the HidCerberus web service (if running) and the driver's registry keys (needs admin).
    public static class HidGuardian {
        const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\HidGuardian";
        const string ParamsKey = ServiceKey + @"\Parameters";
        const string Api = "http://localhost:26762/api/v1/hidguardian/";

        static readonly int Pid = Process.GetCurrentProcess().Id;
        static bool whitelistedViaApi, whitelistedViaRegistry;

        public static bool Installed {
            get {
                try { using (var k = Registry.LocalMachine.OpenSubKey(ServiceKey)) return k != null; } catch { return false; }
            }
        }

        public static string[] AffectedDevices {
            get {
                try {
                    using (var k = Registry.LocalMachine.OpenSubKey(ParamsKey))
                        return (k?.GetValue("AffectedDevices") as string[]) ?? new string[0];
                } catch { return new string[0]; }
            }
        }

        // Lets Omnipad through HidGuardian for as long as it runs (what BetterJoy v5 did on start-up).
        public static void Init(MainForm form) {
            if (!Installed) {
                MainForm.Log("HidGuardian: not installed.");
                return;
            }

            string[] affected = AffectedDevices;
            MainForm.Log("HidGuardian: installed. Blocked devices: " + (affected.Length == 0 ? "(none)" : string.Join(", ", affected)));
            form.AppendTextBox("Found HidGuardian (installed by old BetterJoy). It blocks controllers from every app it doesn't know - letting Omnipad through.\r\n");

            whitelistedViaApi = Call("whitelist/add/" + Pid);

            try {
                using (var k = Registry.LocalMachine.CreateSubKey(ParamsKey + @"\Whitelist\" + Pid))
                    whitelistedViaRegistry = k != null;
                MainForm.Log("HidGuardian: whitelisted process " + Pid + " in the registry.");
            } catch (Exception e) {
                MainForm.Log("HidGuardian: registry whitelist failed (" + e.Message + ").");
            }

            if (!whitelistedViaApi && !whitelistedViaRegistry)
                form.AppendTextBox("Couldn't get past HidGuardian - restart Omnipad as administrator.\r\n");
        }

        // Empties HidGuardian's block list so Steam, games and Omnipad all see the controller again. Needs admin.
        // Takes effect for each device after it is unplugged and plugged back in (or the PC restarts).
        public static bool ClearBlockList(out string message) {
            bool apiOk = Call("affected/purge/");
            bool regOk = false;
            try {
                using (var k = Registry.LocalMachine.OpenSubKey(ParamsKey, true)) {
                    if (k != null) {
                        k.SetValue("AffectedDevices", new string[0], RegistryValueKind.MultiString);
                        regOk = true;
                    }
                }
            } catch (Exception e) {
                MainForm.Log("HidGuardian: could not clear block list in registry (" + e.Message + ").");
            }

            if (apiOk || regOk) {
                message = "Cleared HidGuardian's block list. Unplug and replug the controller (or turn Bluetooth off and on).";
                return true;
            }
            message = "Couldn't clear HidGuardian's block list - restart Omnipad as administrator.";
            return false;
        }

        public static void Shutdown() {
            if (whitelistedViaApi) Call("whitelist/remove/" + Pid);
            if (whitelistedViaRegistry) {
                try { Registry.LocalMachine.DeleteSubKeyTree(ParamsKey + @"\Whitelist\" + Pid, false); } catch { }
            }
        }

        static bool Call(string path) {
            try {
                var req = (HttpWebRequest)WebRequest.Create(Api + path);
                req.Timeout = 1500;
                using (var resp = (HttpWebResponse)req.GetResponse()) {
                    MainForm.Log("HidCerberus " + path + " -> " + (int)resp.StatusCode);
                    return (int)resp.StatusCode < 300;
                }
            } catch (Exception e) {
                MainForm.Log("HidCerberus " + path + " -> not reachable (" + e.Message + ")");
                return false;
            }
        }
    }
}
