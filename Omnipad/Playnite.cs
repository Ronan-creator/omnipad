using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Omnipad {
    // Playnite (playnite.link) - the game launcher. Omnipad can open its Fullscreen (controller) mode or its
    // library window from any button, and close the running game to get back to it.
    public static class Playnite {
        const string Fullscreen = "Playnite.FullscreenApp.exe", Desktop = "Playnite.DesktopApp.exe";

        // Where Playnite is: default per-user install, the uninstall entry, or a running copy (portable installs).
        public static string Folder {
            get {
                try {
                    var running = Process.GetProcessesByName("Playnite.DesktopApp").Concat(Process.GetProcessesByName("Playnite.FullscreenApp")).FirstOrDefault();
                    if (running != null) { string f = Path.GetDirectoryName(running.MainModule.FileName); if (Has(f)) return f; }
                } catch { }
                string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playnite");
                if (Has(local)) return local;
                foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                    foreach (var name in new[] { "Playnite", "Playnite_is1", "{8A9B4E7A-3B7B-4E4A-9C51-0E8C6F0B3F73}_is1" })
                        try {
                            using (var k = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + name)) {
                                string f = (k?.GetValue("InstallLocation") as string)?.TrimEnd('\\');
                                if (Has(f)) return f;
                            }
                        } catch { }
                return null;
            }
        }
        static bool Has(string folder) => !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, Desktop));
        public static bool Installed => Folder != null;

        // Starting a Playnite exe while it's running just brings that window to the front.
        public static void OpenFullscreen() => Start(Fullscreen);
        public static void OpenDesktop() => Start(Desktop);

        static void Start(string exe) {
            string f = Folder;
            if (f == null) { MainForm.Log("Playnite isn't installed (or Omnipad can't find it)."); return; }
            Process.Start(new ProcessStartInfo(Path.Combine(f, exe)) { UseShellExecute = true, WorkingDirectory = f });
        }

        // Close whatever game is in front (Alt+F4), unless that's Playnite itself.
        public static void CloseGame() {
            try {
                var h = GetForegroundWindow();
                GetWindowThreadProcessId(h, out uint pid);
                string name = Process.GetProcessById((int)pid).ProcessName;
                if (name.StartsWith("Playnite", StringComparison.OrdinalIgnoreCase) || name == "explorer") return;
                MainForm.Log("Closing " + name);
            } catch { }
            Shortcuts.Press(new ushort[] { 0x12, 0x73 });   // Alt+F4
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    }
}
