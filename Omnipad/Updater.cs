using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace Omnipad {
    // Checks GitHub for a newer Omnipad (at start and every 6 hours) and installs it in one click, or by itself:
    // with "Install updates automatically" on, it waits until no controller is in use, then downloads the
    // installer, runs it silently and Omnipad comes back on the new version. Nothing to download by hand.
    public static class Updater {
        public const string Repo = "Ronan-creator/omnipad";
        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;
        public static Version Latest { get; private set; }
        public static string ReleaseUrl { get; private set; }
        public static string SetupUrl { get; private set; }
        public static bool UpdateAvailable => Latest != null && Latest > Current;
        public static bool Installing { get; private set; }
        public static bool AutoInstall = Bool("AutoUpdate", true);
        public static event Action Found;
        public static event Action<string> Status;     // progress text for the UI

        static System.Threading.Timer timer;
        static bool Bool(string k, bool d) { bool v; return bool.TryParse(ConfigurationManager.AppSettings[k], out v) ? v : d; }

        public static void CheckInBackground() {
            if (timer != null) return;
            timer = new System.Threading.Timer(_ => Check(), null, TimeSpan.FromSeconds(20), TimeSpan.FromHours(6));
        }

        // "Check for updates" button: look now, and if there's a newer version install it straight away.
        public static void CheckAndInstallNow() {
            if (Installing) return;
            ThreadPool.QueueUserWorkItem(_ => {
                Status?.Invoke("Checking for updates…");
                Check();
                if (UpdateAvailable) Install();
                else Status?.Invoke(Latest == null ? "Couldn't reach GitHub - check your internet connection." : "You're up to date (v" + Current.ToString(3) + ").");
            });
        }

        static void Check() {
            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient()) {
                    wc.Headers[HttpRequestHeader.UserAgent] = "Omnipad/" + Current;
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    string json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                    Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                    Match url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github.com/[^\"]+/releases/tag/[^\"]+)\"");
                    Match setup = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"(https://github.com/[^\"]+/Omnipad-Setup\\.exe)\"");
                    if (!tag.Success) return;
                    Version v;
                    if (!Version.TryParse(tag.Groups[1].Value, out v)) return;
                    if (v.Build < 0) v = new Version(v.Major, v.Minor, 0);
                    bool isNew = Latest == null || v > Latest;
                    Latest = v;
                    ReleaseUrl = url.Success ? url.Groups[1].Value : "https://github.com/" + Repo + "/releases/latest";
                    SetupUrl = setup.Success ? setup.Groups[1].Value : null;
                    MainForm.Log("Update check: latest " + v + ", this is " + Current);
                    if (UpdateAvailable && isNew) Found?.Invoke();
                }
            } catch (Exception e) {
                MainForm.Log("Update check failed: " + e.Message);
            }
        }

        // Called now and then by the UI: installs on its own once nothing is using a controller.
        public static void MaybeAutoInstall(bool controllersInUse) {
            if (AutoInstall && UpdateAvailable && !Installing && !controllersInUse && SetupUrl != null) Install(true);
        }

        // Download the installer and run it silently. It closes Omnipad, updates the files (settings are kept)
        // and starts Omnipad again.
        public static void Install(bool automatic = false) {
            if (Installing) return;
            if (SetupUrl == null) { try { Process.Start(ReleaseUrl ?? ("https://github.com/" + Repo + "/releases/latest")); } catch { } return; }
            Installing = true;
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    Status?.Invoke("Downloading Omnipad " + Latest + "…");
                    string file = Path.Combine(Path.GetTempPath(), "Omnipad-Setup-" + Latest + ".exe");
                    using (var wc = new WebClient()) {
                        wc.Headers[HttpRequestHeader.UserAgent] = "Omnipad/" + Current;
                        wc.DownloadFile(SetupUrl, file);
                    }
                    // Sanity check: a real Windows program, not an error page.
                    var head = new byte[2];
                    using (var fs = File.OpenRead(file)) fs.Read(head, 0, 2);
                    if (head[0] != 'M' || head[1] != 'Z' || new FileInfo(file).Length < 100 * 1024) throw new Exception("the download wasn't a valid installer");
                    MainForm.Log((automatic ? "Installing update automatically: " : "Installing update: ") + Latest);
                    Status?.Invoke("Installing Omnipad " + Latest + "…");
                    Process.Start(new ProcessStartInfo(file, "/S /UPDATE") { UseShellExecute = false });
                    Thread.Sleep(300);
                    Program.Form?.ExitForUpdate();
                } catch (Exception e) {
                    Installing = false;
                    MainForm.Log("Update failed: " + e.Message);
                    Status?.Invoke("Update failed: " + e.Message);
                }
            });
        }
    }
}
