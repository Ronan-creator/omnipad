using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;

namespace Omnipad {
    // Remap any button: to another Xbox button, to nothing, or to an action (Playnite, keys, run something...).
    // "Default" follows the normal layout (and the Nintendo/Xbox label switch). Pro Controller and Joy-Con pairs.
    public static class Mapping {
        public static readonly (string id, string label)[] Pads = {
            ("A", "A"), ("B", "B"), ("X", "X"), ("Y", "Y"),
            ("Up", "D-pad up"), ("Down", "D-pad down"), ("Left", "D-pad left"), ("Right", "D-pad right"),
            ("L", "L"), ("R", "R"), ("ZL", "ZL"), ("ZR", "ZR"),
            ("Minus", "Minus (−)"), ("Plus", "Plus (+)"), ("LStick", "Left stick click"), ("RStick", "Right stick click"),
            ("Home", "Home"), ("Capture", "Capture") };

        public static readonly (string value, string text)[] Targets = {
            ("default", "Default"), ("none", "Nothing (turned off)"),
            ("x:A", "Xbox A"), ("x:B", "Xbox B"), ("x:X", "Xbox X"), ("x:Y", "Xbox Y"),
            ("x:LB", "Xbox LB"), ("x:RB", "Xbox RB"), ("x:LT", "Xbox LT"), ("x:RT", "Xbox RT"),
            ("x:Back", "Xbox View (Back)"), ("x:Start", "Xbox Menu (Start)"), ("x:LS", "Xbox left stick click"), ("x:RS", "Xbox right stick click"),
            ("x:Guide", "Xbox Guide button"), ("x:Up", "Xbox D-pad up"), ("x:Down", "Xbox D-pad down"), ("x:Left", "Xbox D-pad left"), ("x:Right", "Xbox D-pad right"),
            ("playnite-fullscreen", "Playnite: open Fullscreen mode"), ("playnite-desktop", "Playnite: open library"),
            ("close-game", "Close the current game (Alt+F4)"), ("screenshot", "Screenshot (the Capture action)"),
            ("omnipad", "Open Omnipad"), ("keys", "Press keys…"), ("run", "Run a program, shortcut or website…") };

        static readonly Dictionary<string, string> map = new Dictionary<string, string>(), arg = new Dictionary<string, string>();
        static bool active;

        static Mapping() {
            foreach (var p in Pads) {
                map[p.id] = ConfigurationManager.AppSettings["Map_" + p.id] ?? "default";
                arg[p.id] = ConfigurationManager.AppSettings["MapArg_" + p.id] ?? "";
            }
            Recalc();
        }
        static void Recalc() { bool a = false; foreach (var v in map.Values) if (v != "default") a = true; active = a; }

        public static string Get(string pad) { lock (map) return map[pad]; }
        public static string Arg(string pad) { lock (map) return arg[pad]; }
        public static void Set(string pad, string target, string argument) {
            lock (map) { map[pad] = target; arg[pad] = argument ?? ""; Recalc(); }
            MainForm.SaveSetting("Map_" + pad, target);
            MainForm.SaveSetting("MapArg_" + pad, argument ?? "");
        }
        public static bool IsDefault(string pad) => Get(pad) == "default";

        // What each button does by default, as an Xbox button (null = nothing on the Xbox pad).
        static string DefaultXbox(string pad) {
            bool labels = Prefs.SwapButtons;
            switch (pad) {
                case "A": return labels ? "A" : "B";
                case "B": return labels ? "B" : "A";
                case "X": return labels ? "X" : "Y";
                case "Y": return labels ? "Y" : "X";
                case "Up": case "Down": case "Left": case "Right": return pad;
                case "L": return "LB"; case "R": return "RB"; case "ZL": return "LT"; case "ZR": return "RT";
                case "Minus": return "Back"; case "Plus": return "Start";
                case "LStick": return "LS"; case "RStick": return "RS";
                case "Home": return "Guide";
                default: return null;   // Capture: handled by the Capture action
            }
        }

        // The physical buttons, by Nintendo name, for a Pro Controller or a Joy-Con pair (null for a single Joy-Con).
        static bool[] Read(Joycon jc) {
            Func<Joycon.Button, bool> b = jc.GetButton;
            if (jc.isPro) return new[] {
                b(Joycon.Button.A), b(Joycon.Button.B), b(Joycon.Button.X), b(Joycon.Button.Y),
                b(Joycon.Button.DPAD_UP), b(Joycon.Button.DPAD_DOWN), b(Joycon.Button.DPAD_LEFT), b(Joycon.Button.DPAD_RIGHT),
                b(Joycon.Button.SHOULDER_1), b(Joycon.Button.SHOULDER2_1), b(Joycon.Button.SHOULDER_2), b(Joycon.Button.SHOULDER2_2),
                b(Joycon.Button.MINUS), b(Joycon.Button.PLUS), b(Joycon.Button.STICK), b(Joycon.Button.STICK2),
                b(Joycon.Button.HOME), b(Joycon.Button.CAPTURE) };
            if (jc.other == null) return null;
            if (jc.isLeft) return new[] {   // left Joy-Con drives the pad; its buttons array has the right one's merged in
                b(Joycon.Button.A), b(Joycon.Button.B), b(Joycon.Button.X), b(Joycon.Button.Y),
                b(Joycon.Button.DPAD_UP), b(Joycon.Button.DPAD_DOWN), b(Joycon.Button.DPAD_LEFT), b(Joycon.Button.DPAD_RIGHT),
                b(Joycon.Button.SHOULDER_1), b(Joycon.Button.SHOULDER2_1), b(Joycon.Button.SHOULDER_2), b(Joycon.Button.SHOULDER2_2),
                b(Joycon.Button.MINUS), b(Joycon.Button.PLUS), b(Joycon.Button.STICK), b(Joycon.Button.STICK2),
                b(Joycon.Button.HOME), b(Joycon.Button.CAPTURE) };
            return new[] {                   // right Joy-Con drives the pad: its own face buttons sit in the D-pad slots
                b(Joycon.Button.DPAD_RIGHT), b(Joycon.Button.DPAD_DOWN), b(Joycon.Button.DPAD_UP), b(Joycon.Button.DPAD_LEFT),
                b(Joycon.Button.X), b(Joycon.Button.B), b(Joycon.Button.Y), b(Joycon.Button.A),
                b(Joycon.Button.SHOULDER2_1), b(Joycon.Button.SHOULDER_1), b(Joycon.Button.SHOULDER2_2), b(Joycon.Button.SHOULDER_2),
                b(Joycon.Button.MINUS), b(Joycon.Button.PLUS), b(Joycon.Button.STICK2), b(Joycon.Button.STICK),
                b(Joycon.Button.HOME), jc.other.GetButton(Joycon.Button.CAPTURE) };
        }

        static readonly Dictionary<Joycon, bool[]> last = new Dictionary<Joycon, bool[]>();
        static readonly Dictionary<string, Xbox360Buttons> xb = new Dictionary<string, Xbox360Buttons> {
            ["A"] = Xbox360Buttons.A, ["B"] = Xbox360Buttons.B, ["X"] = Xbox360Buttons.X, ["Y"] = Xbox360Buttons.Y,
            ["LB"] = Xbox360Buttons.LeftShoulder, ["RB"] = Xbox360Buttons.RightShoulder, ["Back"] = Xbox360Buttons.Back, ["Start"] = Xbox360Buttons.Start,
            ["LS"] = Xbox360Buttons.LeftThumb, ["RS"] = Xbox360Buttons.RightThumb, ["Guide"] = Xbox360Buttons.Guide,
            ["Up"] = Xbox360Buttons.Up, ["Down"] = Xbox360Buttons.Down, ["Left"] = Xbox360Buttons.Left, ["Right"] = Xbox360Buttons.Right };

        // Rebuilds the Xbox report's buttons from the mapping. Called after the normal report is built.
        public static void Apply(Joycon jc) {
            if (!active) return;
            var p = Read(jc);
            if (p == null) return;
            bool[] prev;
            lock (last) { if (!last.TryGetValue(jc, out prev)) last[jc] = prev = new bool[Pads.Length]; }
            var r = jc.report;
            for (int i = 0; i < r.Buttons.Length; i++) r.Buttons[i] = false;
            bool lt = false, rt = false;
            for (int i = 0; i < Pads.Length; i++) {
                string pad = Pads[i].id, t = Get(pad);
                if (t == "default") { string d = DefaultXbox(pad); t = d == null ? "none" : "x:" + d; }
                if (t.StartsWith("x:")) {
                    if (!p[i]) continue;
                    string x = t.Substring(2);
                    if (x == "LT") lt = true; else if (x == "RT") rt = true; else r.SetButtonState(xb[x], true);
                } else if (t != "none" && p[i] != prev[i]) {
                    Action(t, Arg(pad), p[i]);
                }
                prev[i] = p[i];
            }
            r.SetAxis(Xbox360Axes.LeftTrigger, (short)(lt ? short.MaxValue : 0));
            r.SetAxis(Xbox360Axes.RightTrigger, (short)(rt ? short.MaxValue : 0));
        }

        public static void Forget(Joycon jc) { lock (last) last.Remove(jc); }

        // Actions run on press; "keys" holds the keys down while the button is held (so it works as a game key too).
        static void Action(string t, string a, bool down) {
            if (t == "keys") {
                var keys = Shortcuts.ParseKeys(a);
                if (keys != null) Shortcuts.KeysState(keys, down);
                return;
            }
            if (!down) return;
            System.Threading.ThreadPool.QueueUserWorkItem(_ => {
                try {
                    switch (t) {
                        case "playnite-fullscreen": Playnite.OpenFullscreen(); break;
                        case "playnite-desktop": Playnite.OpenDesktop(); break;
                        case "close-game": Playnite.CloseGame(); break;
                        case "screenshot": Shortcuts.CaptureNow(); break;
                        case "omnipad": Program.Form?.ShowFromController(); break;
                        case "run":
                            if (a.Length > 0) Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(a)) { UseShellExecute = true });
                            break;
                    }
                } catch (Exception e) { MainForm.Log("Button action failed: " + e.Message); }
            });
        }
    }
}
