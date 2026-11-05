using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Omnipad {
    // Starts Omnipad in the tray when you sign in.
    // Preferred: a Task Scheduler task with "highest privileges", so Omnipad starts elevated without a UAC prompt
    // (it needs that to get past HidGuardian / change HidHide). Creating it needs admin once.
    // Fallback when not admin: the per-user Run key (starts without admin rights).
    public static class Autostart {
        const string TaskName = "Omnipad";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool TaskExists => Schtasks("/Query /TN \"" + TaskName + "\"") == 0;

        public static bool RunKeyExists {
            get {
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k?.GetValue(TaskName) != null; } catch { return false; }
            }
        }

        public static bool Enabled => TaskExists || RunKeyExists;

        // Returns a short description of what was done, for the log.
        public static string Enable() {
            string exe = Application.ExecutablePath;
            if (Program.IsAdmin) {
                string xml = Path.Combine(Path.GetTempPath(), "omnipad-task.xml");
                File.WriteAllText(xml, TaskXml(exe), System.Text.Encoding.Unicode);
                int code = Schtasks("/Create /TN \"" + TaskName + "\" /XML \"" + xml + "\" /F");
                try { File.Delete(xml); } catch { }
                if (code == 0) {
                    SetRunKey(null); // the task replaces the Run key
                    return "Omnipad will start in the tray when you sign in (with admin rights, no prompt).";
                }
                MainForm.Log("schtasks /Create failed with " + code + "; using the Run key instead.");
            }
            SetRunKey("\"" + exe + "\" --minimized");
            return "Omnipad will start in the tray when you sign in." +
                   (Program.IsAdmin ? "" : " (Turn this off and on again while running as administrator to start with admin rights.)");
        }

        public static void Disable() {
            SetRunKey(null);
            if (TaskExists && Schtasks("/Delete /TN \"" + TaskName + "\" /F") != 0)
                MainForm.Log("Could not delete the start-up task (needs administrator).");
        }

        // If the exe was moved, point the start-up entry at the new location.
        public static void Repair() {
            try {
                if (RunKeyExists) SetRunKey("\"" + Application.ExecutablePath + "\" --minimized");
                if (TaskExists && Program.IsAdmin) Enable();
            } catch { }
        }

        static void SetRunKey(string value) {
            try {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) {
                    if (value == null) k.DeleteValue(TaskName, false);
                    else k.SetValue(TaskName, value);
                }
            } catch (Exception e) {
                MainForm.Log("Run key: " + e.Message);
            }
        }

        static int Schtasks(string args) {
            try {
                var psi = new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) {
                    p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                    return p.ExitCode;
                }
            } catch {
                return -1;
            }
        }

        // No 72-hour time limit, runs on battery, starts 5s after sign-in so Bluetooth is up.
        static string TaskXml(string exe) {
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            return @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Starts Omnipad (Switch controllers as Xbox controllers) in the tray.</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + SecurityElement.Escape(user) + @"</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>" + SecurityElement.Escape(user) + @"</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>" + SecurityElement.Escape(exe) + @"</Command><Arguments>--minimized</Arguments><WorkingDirectory>" + SecurityElement.Escape(Path.GetDirectoryName(exe)) + @"</WorkingDirectory></Exec></Actions>
</Task>";
        }
    }
}
