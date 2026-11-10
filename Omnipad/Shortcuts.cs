using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Omnipad {
    // Capture button -> screenshot / clip, hold Home -> Steam Big Picture (or open Omnipad).
    // A short Home press still reaches games and Steam as the Xbox Guide button.
    public static class Shortcuts {
        public static string CaptureAction = Setting("CaptureAction", "steam");    // none | steam | windows | clip
        public static string HomeHoldAction = Setting("HomeHoldAction", "bigpicture"); // none | bigpicture | omnipad
        const int HoldMs = 700;

        // Long-hold shortcuts on Capture and Minus (default 5 s): switch PC/TV, run anything, press keys...
        public static int HoldSeconds = int.TryParse(Setting("HoldSeconds", "5"), out int hs) ? hs : 5;
        static readonly Dictionary<string, string> holdAction = new Dictionary<string, string> {
            ["CaptureHold"] = Setting("CaptureHoldAction", "none"), ["MinusHold"] = Setting("MinusHoldAction", "none") };
        static readonly Dictionary<string, string> holdTarget = new Dictionary<string, string> {
            ["CaptureHold"] = Setting("CaptureHoldTarget", ""), ["MinusHold"] = Setting("MinusHoldTarget", "") };
        public static string Action(string key) { lock (holdAction) return holdAction[key]; }
        public static string Target(string key) { lock (holdAction) return holdTarget[key]; }
        public static void SetAction(string key, string v) { lock (holdAction) holdAction[key] = v; }
        public static void SetTarget(string key, string v) { lock (holdAction) holdTarget[key] = v; }

        class State {
            public bool capture, home, minus; public DateTime homeDown, capDown, minusDown;
            public bool homeFired, capFired, minusFired;
        }
        static readonly Dictionary<Joycon, State> states = new Dictionary<Joycon, State>();

        static string Setting(string key, string def) {
            string v = ConfigurationManager.AppSettings[key];
            return string.IsNullOrEmpty(v) ? def : v;
        }

        // Called from each controller's poll thread after its buttons are updated.
        public static void Process(Joycon jc) {
            State st;
            lock (states) {
                if (!states.TryGetValue(jc, out st)) states[jc] = st = new State();
            }

            // Capture is on the Pro Controller and the left Joy-Con; Home on the Pro Controller and the right Joy-Con.
            // (In a pair, the other Joy-Con mirrors Home, so only read it where it physically is.)
            if ((jc.isPro || jc.isLeft) && Mapping.IsDefault("Capture")) {
                bool cap = jc.GetButton(Joycon.Button.CAPTURE);
                bool capHold = Action("CaptureHold") != "none";
                if (cap && !st.capture) {
                    st.capDown = DateTime.Now; st.capFired = false;
                    if (!capHold) Capture();                         // no hold shortcut: act on press, as before
                }
                if (cap && capHold && !st.capFired && (DateTime.Now - st.capDown).TotalSeconds >= HoldSeconds) {
                    st.capFired = true; RunHold("CaptureHold", jc);
                }
                if (!cap && st.capture && capHold && !st.capFired) Capture();   // with a hold shortcut, a tap acts on release
                st.capture = cap;
            }
            if (jc.isPro || jc.isLeft) {
                // Minus stays Xbox Back for games; holding it long enough also runs its shortcut.
                bool minus = jc.GetButton(Joycon.Button.MINUS);
                if (minus && !st.minus) { st.minusDown = DateTime.Now; st.minusFired = false; }
                if (minus && !st.minusFired && Action("MinusHold") != "none" && (DateTime.Now - st.minusDown).TotalSeconds >= HoldSeconds) {
                    st.minusFired = true; RunHold("MinusHold", jc);
                }
                st.minus = minus;
            }
            if ((jc.isPro || !jc.isLeft) && Mapping.IsDefault("Home")) {
                bool home = jc.GetButton(Joycon.Button.HOME);
                if (home && !st.home) { st.homeDown = DateTime.Now; st.homeFired = false; }
                if (home && !st.homeFired && (DateTime.Now - st.homeDown).TotalMilliseconds >= HoldMs) {
                    st.homeFired = true;
                    HomeHeld();
                }
                st.home = home;
            }
        }

        public static void Forget(Joycon jc) {
            lock (states) states.Remove(jc);
        }

        static void Capture() {
            switch (CaptureAction) {
                case "steam": Keys(VK_F12); break;                       // Steam's screenshot key (in games with the Steam overlay)
                case "windows": Keys(VK_LWIN, VK_MENU, VK_SNAPSHOT); break; // Game Bar screenshot of the active game/window
                case "clip": Keys(VK_LWIN, VK_MENU, (ushort)'G'); break;    // Game Bar: save the last 30 seconds
            }
        }

        static void HomeHeld() {
            switch (HomeHoldAction) {
                case "playnite": Playnite.OpenFullscreen(); break;
                case "playnite-desktop": Playnite.OpenDesktop(); break;
                case "bigpicture":
                    try { System.Diagnostics.Process.Start("steam://open/bigpicture"); } catch (Exception e) { MainForm.Log("Big Picture: " + e.Message); }
                    break;
                case "omnipad":
                    Program.Form?.ShowFromController();
                    break;
            }
        }

        static bool onTv;   // for "switch between PC screen and TV"

        static void RunHold(string key, Joycon jc) {
            string action = Action(key), target = Target(key);
            try { jc.SetRumble(160.0f, 320.0f, 0.7f, 250); } catch { }   // buzz: "done"
            MainForm.Log("Hold " + (key == "CaptureHold" ? "Capture" : "Minus") + ": " + action + (target.Length > 0 ? " " + target : ""));
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                try {
                    switch (action) {
                        case "toggle-display": onTv = !onTv; Display(onTv ? "/external" : "/internal"); break;
                        case "tv": onTv = true; Display("/external"); break;
                        case "pc": onTv = false; Display("/internal"); break;
                        case "extend": Display("/extend"); break;
                        case "bigpicture": System.Diagnostics.Process.Start("steam://open/bigpicture"); break;
                        case "playnite-fullscreen": Playnite.OpenFullscreen(); break;
                        case "playnite-desktop": Playnite.OpenDesktop(); break;
                        case "close-game": Playnite.CloseGame(); break;
                        case "omnipad": Program.Form?.ShowFromController(); break;
                        case "run":
                            if (target.Length == 0) return;
                            System.Diagnostics.Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(target)) { UseShellExecute = true });
                            break;
                        case "keys":
                            var vks = ParseKeys(target);
                            if (vks == null) MainForm.Log("Couldn't understand the keys \"" + target + "\" (try e.g. Win+P or Ctrl+Alt+T)");
                            else Keys(vks);
                            break;
                    }
                } catch (Exception e) { MainForm.Log("Hold shortcut failed: " + e.Message); }
            });
        }

        // Windows' own display switcher (the same as Win+P): /internal = PC screen only, /external = second
        // screen (TV) only, /extend, /clone.
        static void Display(string mode) {
            System.Diagnostics.Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "DisplaySwitch.exe"), mode) { UseShellExecute = false, CreateNoWindow = true });
        }

        // "Ctrl+Alt+T", "Win+P", "Alt+Enter", "F12"... -> virtual-key codes.
        public static ushort[] ParseKeys(string text) {
            var list = new List<ushort>();
            foreach (var raw in text.Split('+')) {
                string k = raw.Trim().ToLowerInvariant();
                if (k.Length == 0) continue;
                switch (k) {
                    case "ctrl": case "control": list.Add(0x11); continue;
                    case "alt": list.Add(0x12); continue;
                    case "shift": list.Add(0x10); continue;
                    case "win": case "windows": case "super": list.Add(0x5B); continue;
                    case "esc": case "escape": list.Add(0x1B); continue;
                    case "enter": case "return": list.Add(0x0D); continue;
                    case "space": list.Add(0x20); continue;
                    case "tab": list.Add(0x09); continue;
                    case "del": case "delete": list.Add(0x2E); continue;
                    case "prtsc": case "printscreen": list.Add(0x2C); continue;
                    case "up": list.Add(0x26); continue;
                    case "down": list.Add(0x28); continue;
                    case "left": list.Add(0x25); continue;
                    case "right": list.Add(0x27); continue;
                    case "volup": list.Add(0xAF); continue;
                    case "voldown": list.Add(0xAE); continue;
                    case "mute": list.Add(0xAD); continue;
                    case "playpause": list.Add(0xB3); continue;
                }
                if (k.Length == 1 && char.IsLetterOrDigit(k[0])) { list.Add((ushort)char.ToUpperInvariant(k[0])); continue; }
                if (k[0] == 'f' && int.TryParse(k.Substring(1), out int f) && f >= 1 && f <= 24) { list.Add((ushort)(0x6F + f)); continue; }
                System.Windows.Forms.Keys named;
                if (Enum.TryParse(raw.Trim(), true, out named)) { list.Add((ushort)named); continue; }
                return null;
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        // ---- keyboard injection ----
        const ushort VK_F12 = 0x7B, VK_LWIN = 0x5B, VK_MENU = 0x12, VK_SNAPSHOT = 0x2C;

        public static void Press(ushort[] keys) { Keys(keys); }
        public static void CaptureNow() { Capture(); }
        // Hold keys down / let them go (for buttons mapped to keys).
        public static void KeysState(ushort[] keys, bool down) {
            var inputs = new List<INPUT>();
            if (down) foreach (var k in keys) inputs.Add(Key(k, false));
            else for (int i = keys.Length - 1; i >= 0; i--) inputs.Add(Key(keys[i], true));
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        static void Keys(params ushort[] keys) {
            var inputs = new List<INPUT>();
            foreach (var k in keys) inputs.Add(Key(k, false));
            for (int i = keys.Length - 1; i >= 0; i--) inputs.Add(Key(keys[i], true));
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        }

        static INPUT Key(ushort vk, bool up) {
            return new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? 2u : 0u } } };
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint n, INPUT[] inputs, int size);
    }
}
