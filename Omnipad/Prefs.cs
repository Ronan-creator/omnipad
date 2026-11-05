using System;
using System.Configuration;

namespace Omnipad {
    // Settings that can change while Omnipad runs (the Settings page updates these and saves them).
    public static class Prefs {
        public static int AutoSleepMinutes = Int("AutoSleepMinutes", 15);     // Bluetooth only; 0 = never
        public static string HomeLight = Str("HomeLight", "bright");            // bright | dim | off
        public static bool PlayerLights = Bool("PlayerLights", true);           // show the Xbox player slot on the controller LEDs
        public static string GyroMode = Str("GyroMode", "off");                 // off | always | zl | zr | toggle
        public static int GyroSensitivity = Int("GyroSensitivity", 12);         // mouse pixels per degree turned
        public static bool GyroInvertY = Bool("GyroInvertY", false);

        static string Str(string k, string d) { string v = ConfigurationManager.AppSettings[k]; return string.IsNullOrEmpty(v) ? d : v; }
        static int Int(string k, int d) { int v; return int.TryParse(ConfigurationManager.AppSettings[k], out v) ? v : d; }
        static bool Bool(string k, bool d) { bool v; return bool.TryParse(ConfigurationManager.AppSettings[k], out v) ? v : d; }
    }
}
