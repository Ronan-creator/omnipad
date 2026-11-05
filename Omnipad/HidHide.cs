using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace Omnipad {
    // Talks to HidHide (https://github.com/nefarius/HidHide) through its command-line tool.
    // HidHide can hide the real controller from Steam and games so they only see Omnipad's Xbox pad.
    public static class HidHide {
        static readonly string[] CliPaths = {
            @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
            @"C:\Program Files\Nefarius Software Solutions e.U\HidHide\x64\HidHideCLI.exe",
        };

        static string cli;
        static readonly HashSet<string> hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static bool Installed => cli != null;
        public static bool Enabled { get; private set; }   // hiding controllers automatically
        public static bool Allowed { get; private set; }   // BetterJoy is on HidHide's allow list
        public static event Action Changed;

        public static void Init(MainForm form, bool useHiding) {
            foreach (string p in CliPaths)
                if (File.Exists(p)) { cli = p; break; }

            if (cli == null) {
                form.AppendTextBox("HidHide is not installed. Steam will also see the real controller.\r\n");
                return;
            }

            // Changing HidHide needs administrator rights; without them every command fails (exit 31).
            if (!Program.IsAdmin) {
                form.AppendTextBox("HidHide is installed. Omnipad isn't running as administrator, so it can't change HidHide's settings.\r\n");
                return;
            }

            // Always put Omnipad on the allow list, so it can see controllers even if they are hidden
            // (an earlier build could hide them from itself).
            if (ClientOpen) {
                form.AppendTextBox("The HidHide Configuration Client window is open. HidHide only talks to one program at a time - close that window, then restart Omnipad.\r\n");
                return;
            }

            Run("--app-clean"); // drop allow-list entries for exe paths that no longer exist (old builds)
            Allowed = Run("--app-reg \"" + Application.ExecutablePath + "\"");
            if (!Allowed)
                form.AppendTextBox("HidHide refused Omnipad's settings (details in Omnipad.log). Close the HidHide Configuration Client if it is open, then restart Omnipad.\r\n");

            if (useHiding && Allowed)
                SetEnabled(true);
            else
                RestoreEverything(); // a previous build may have left hiding switched on

            LogState();
            form.AppendTextBox(Enabled ? "HidHide on: Steam and games only see Omnipad's Xbox controller.\r\n"
                                       : "HidHide installed but hiding is off.\r\n");
        }

        public static void SetEnabled(bool on) {
            if (cli == null) return;
            if (on) {
                Enabled = Run("--cloak-on");
                foreach (Joycon j in Program.mgr?.j ?? new List<Joycon>())
                    Hide(j.path);
            } else {
                RestoreEverything();
            }
            Changed?.Invoke();
        }

        // hidapi path: \\?\hid#vid_057e&pid_2009#8&1f0a2b3c&0&0000#{4d1e55b2-...}
        // instance id:  HID\VID_057E&PID_2009\8&1F0A2B3C&0&0000
        public static string InstanceIdFromPath(string path) {
            string[] parts = path.Split('#');
            if (parts.Length < 3) return null;
            return ("HID\\" + parts[1] + "\\" + parts[2]).ToUpperInvariant();
        }

        public static void Hide(string path) {
            if (!Enabled || !Allowed) return;
            string id = InstanceIdFromPath(path);
            if (id != null) HideId(id);
        }

        public static bool HideId(string id) {
            if (cli == null || hidden.Contains(id)) return false;
            if (!Run("--dev-hide \"" + id + "\"")) return false;
            hidden.Add(id);
            return true;
        }

        public static bool UnhideId(string id) {
            if (cli == null) return false;
            hidden.Remove(id);
            return Run("--dev-unhide \"" + id + "\"");
        }

        // Turns HidHide's hiding off completely and un-hides every Nintendo device Windows knows about.
        // Uses device IDs from Windows (WMI) as well, because a hidden device can't be found through hidapi.
        public static void RestoreEverything(IEnumerable<string> knownIds = null) {
            if (cli == null) return;
            Run("--inv-off"); // "inverse cloak" would flip the allow list
            if (!Run("--cloak-off"))
                MainForm.Log("Could not switch HidHide hiding off (needs administrator).");
            Enabled = false;
            if (knownIds != null)
                foreach (string id in knownIds) UnhideId(id);
            UnhideAllNintendo();
            Changed?.Invoke();
        }

        // Unhides every Nintendo HID device Windows lists.
        public static void UnhideAllNintendo() {
            if (cli == null) return;
            foreach (var d in HidEnum.Enumerate(0x057e)) {
                string id = InstanceIdFromPath(d.Path);
                if (id != null) UnhideId(id);
            }
            foreach (string id in new List<string>(hidden)) UnhideId(id);
        }

        // On exit, give the controllers back so Steam can use them while BetterJoy isn't running.
        public static void UnhideAll() {
            foreach (string id in new List<string>(hidden))
                UnhideId(id);
        }

        // HidHide's control device accepts only one program at a time. While the HidHide Configuration Client
        // window is open, every command from Omnipad fails (exit 31).
        public static bool ClientOpen => Process.GetProcessesByName("HidHideClient").Length > 0;

        // Writes HidHide's current state to the log, so a blocked controller can be explained.
        public static void LogState() {
            if (cli == null) return;
            foreach (string q in new[] { "--cloak-state", "--inv-state", "--app-list", "--dev-list" })
                Run(q);
        }

        static bool Run(string args) {
            if (ClientOpen) {
                MainForm.Log("HidHideCLI " + args + " skipped: HidHide Configuration Client is open and blocks other programs.");
                return false;
            }
            try {
                var psi = new ProcessStartInfo(cli, args) {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (var p = Process.Start(psi)) {
                    string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(5000)) return false;
                    bool ok = p.ExitCode == 0;
                    MainForm.Log("HidHideCLI " + args + " -> " + (ok ? "ok" : "exit " + p.ExitCode) +
                                 (output.Trim().Length > 0 ? "\r\n    " + output.Trim().Replace("\n", "\n    ") : ""));
                    return ok;
                }
            } catch (Exception e) {
                MainForm.Log("HidHideCLI " + args + " failed: " + e.Message);
                return false;
            }
        }
    }
}
