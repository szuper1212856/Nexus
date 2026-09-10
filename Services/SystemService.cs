using System;
using System.Diagnostics;
using System.Media;
using Microsoft.Win32;

namespace NEXUS.Services
{
    /// <summary>OS-level integration: run-at-login registration and alert sounds.</summary>
    public static class SystemService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "NEXUS";

        public static string ExecutablePath
        {
            get
            {
                try
                {
                    var path = Process.GetCurrentProcess().MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path)) return path;
                }
                catch { }
                return Environment.ProcessPath ?? "";
            }
        }

        public static bool IsStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        public static bool SetStartupEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                if (key == null) return false;
                if (enabled) key.SetValue(ValueName, "\"" + ExecutablePath + "\"");
                else key.DeleteValue(ValueName, false);
                return true;
            }
            catch { return false; }
        }

        public static void PlayAlert()
        {
            try { SystemSounds.Exclamation.Play(); } catch { }
        }

        public static void PlayChime()
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
        }
    }
}
