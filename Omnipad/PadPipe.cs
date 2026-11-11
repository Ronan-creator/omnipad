using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace Omnipad {
    // Local control channel for OmniBoy (the Playnite console frontend): list the controllers and change who is Player 1.
    // One line in, reply lines out, then "END". Commands: LIST, FIRST <slot 0-3>, LAYOUT labels|positions|default, VERSION.
    public static class PadPipe {
        public const string Name = "omnipad-control";

        public static void Start() {
            var t = new Thread(Loop) { IsBackground = true, Name = "Omnipad control pipe" };
            t.Start();
        }

        static void Loop() {
            while (true) {
                try {
                    // Omnipad may run as admin while Playnite doesn't, so signed-in users get read/write on the pipe.
                    var sec = new PipeSecurity();
                    sec.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
                    sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, sec)) {
                        pipe.WaitForConnection();
                        var reader = new StreamReader(pipe);
                        var writer = new StreamWriter(pipe) { AutoFlush = true };
                        string line = (reader.ReadLine() ?? "").Trim();
                        foreach (var reply in Handle(line)) writer.WriteLine(reply);
                        writer.WriteLine("END");
                        try { pipe.WaitForPipeDrain(); } catch { }
                    }
                } catch (Exception e) {
                    MainForm.Log("Control pipe: " + e.Message);
                    Thread.Sleep(1000);
                }
            }
        }

        // OmniBoy switches the button layout per game (Nintendo positions for Nintendo games, say) without touching
        // the saved setting; "default" puts the saved one back.
        static bool overriding, saved;
        // the layout was changed by hand in Omnipad: that choice wins, nothing to put back later
        public static void ForgetOverride() { overriding = false; }
        static string Layout(string mode) {
            if (mode == "default") {
                if (overriding) { Prefs.SwapButtons = saved; overriding = false; }
                return "OK";
            }
            if (mode != "labels" && mode != "positions") return "ERR use labels, positions or default";
            if (!overriding) { saved = Prefs.SwapButtons; overriding = true; }
            Prefs.SwapButtons = mode == "labels";
            MainForm.Log("Button layout for this game: " + (Prefs.SwapButtons ? "Xbox labels" : "Nintendo positions"));
            return "OK";
        }

        static IEnumerable<string> Handle(string cmd) {
            var mgr = Program.mgr;
            if (mgr == null) return new[] { "ERR not ready" };
            if (cmd == "LIST") return mgr.Describe();
            if (cmd == "VERSION") return new[] { Application.ProductVersion };
            if (cmd.StartsWith("LAYOUT ")) return new[] { Layout(cmd.Substring(7).Trim().ToLowerInvariant()) };
            if (cmd.StartsWith("FIRST ") && int.TryParse(cmd.Substring(6).Trim(), out int slot) && slot >= 0 && slot <= 3)
                return new[] { mgr.MakeFirst(slot) };
            return new[] { "ERR unknown command" };
        }
    }
}
