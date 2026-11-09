using System;
using Microsoft.Win32;

namespace Omnipad {
    // The name Windows gives Omnipad's virtual Xbox 360 controller in the Game Controllers panel (joy.cpl) and to
    // programs that list DirectInput devices (many emulators). Windows reads it from the OEM name for the
    // controller's USB ids (045E:028E, the Xbox 360 pad ViGEmBus emulates), per user - no admin needed.
    // XInput programs don't use names (they number the pads), and real wired Xbox 360 pads share these ids.
    public static class DeviceNames {
        const string Key = @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_045E&PID_028E";
        const string Default = "Xbox 360 Controller";

        public static string XboxName {
            get {
                try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) return (k?.GetValue("OEMName") as string) is string s && s.Length > 0 ? s : Default; }
                catch { return Default; }
            }
        }

        // Empty name = back to Windows' own name. Returns an error message, or null when it worked.
        public static string SetXboxName(string name) {
            try {
                using (var k = Registry.CurrentUser.CreateSubKey(Key)) {
                    if (string.IsNullOrWhiteSpace(name) || name == Default) k.DeleteValue("OEMName", false);
                    else k.SetValue("OEMName", name.Length > 60 ? name.Substring(0, 60) : name);
                }
                MainForm.Log("Xbox controller name: " + XboxName);
                return null;
            } catch (Exception e) { return e.Message; }
        }
    }
}
