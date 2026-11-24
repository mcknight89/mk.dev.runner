using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using mk.dev.runner.desktop.ViewModels;
using Application = System.Windows.Application;

namespace mk.dev.runner.desktop
{
    internal static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Dev Runner";
        private const string OldValueName = "mk.dev.runner";

        public static void Apply(bool enabled)
        {
            if (App.IsDev)
                return;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null)
                return;
            if (key.GetValue(OldValueName) != null)
                key.DeleteValue(OldValueName);
            if (enabled)
                key.SetValue(ValueName, Command);
            else if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName);
        }

        private static string Command => $"\"{Environment.ProcessPath}\" --tray";

        public static void KeepPathCurrent(bool enabled)
        {
            if (!enabled || App.IsDev)
                return;
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key != null && key.GetValue(ValueName) as string != Command)
                    Apply(true);
            }
            catch { }
        }
    }
}
