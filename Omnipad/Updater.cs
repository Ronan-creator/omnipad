using System;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace Omnipad {
    // Once a day, asks GitHub for the latest Omnipad release and reports it if it's newer than this build.
    public static class Updater {
        public const string Repo = "Ronan-creator/omnipad";
        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version;
        public static Version Latest { get; private set; }
        public static string ReleaseUrl { get; private set; }
        public static bool UpdateAvailable => Latest != null && Latest > Current;
        public static event Action Found;

        public static void CheckInBackground() {
            if (Repo.StartsWith("OWNER/")) return;
            ThreadPool.QueueUserWorkItem(_ => {
                try {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    using (var wc = new WebClient()) {
                        wc.Headers[HttpRequestHeader.UserAgent] = "Omnipad/" + Current;
                        wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                        string json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                        Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                        Match url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github.com/[^\"]+/releases/tag/[^\"]+)\"");
                        if (!tag.Success) return;
                        Version v;
                        if (!Version.TryParse(tag.Groups[1].Value, out v)) return;
                        if (v.Build < 0) v = new Version(v.Major, v.Minor, 0);
                        Latest = v;
                        ReleaseUrl = url.Success ? url.Groups[1].Value : "https://github.com/" + Repo + "/releases/latest";
                        MainForm.Log("Update check: latest " + v + ", this is " + Current);
                        if (UpdateAvailable) Found?.Invoke();
                    }
                } catch (Exception e) {
                    MainForm.Log("Update check failed: " + e.Message);
                }
            });
        }
    }
}
