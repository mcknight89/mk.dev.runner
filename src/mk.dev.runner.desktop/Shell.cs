using System.Diagnostics;
using System.Windows;

namespace mk.dev.runner.desktop
{
    public static class Shell
    {
        private static readonly Lazy<string?> _vsCode = new(FindVsCode);

        public static void Open(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Dev Runner");
            }
        }

        public static void OpenInEditor(string file)
        {
            if (_vsCode.Value == null)
            {
                Open(file);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"\"{_vsCode.Value}\" \"{file}\"\"") { UseShellExecute = false, CreateNoWindow = true });
            }
            catch
            {
                Open(file);
            }
        }

        private static string? FindVsCode()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("where", "code.cmd") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
                var first = p?.StandardOutput.ReadLine();
                p?.WaitForExit(3000);
                return string.IsNullOrWhiteSpace(first) ? null : first.Trim();
            }
            catch
            {
                return null;
            }
        }
    }
}
