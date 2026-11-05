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

        class State { public bool capture, home; public DateTime homeDown; public bool homeFired; }
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
            if (jc.isPro || jc.isLeft) {
                bool cap = jc.GetButton(Joycon.Button.CAPTURE);
                if (cap && !st.capture) Capture();
                st.capture = cap;
            }
            if (jc.isPro || !jc.isLeft) {
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
                case "bigpicture":
                    try { System.Diagnostics.Process.Start("steam://open/bigpicture"); } catch (Exception e) { MainForm.Log("Big Picture: " + e.Message); }
                    break;
                case "omnipad":
                    Program.Form?.ShowFromController();
                    break;
            }
        }

        // ---- keyboard injection ----
        const ushort VK_F12 = 0x7B, VK_LWIN = 0x5B, VK_MENU = 0x12, VK_SNAPSHOT = 0x2C;

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
