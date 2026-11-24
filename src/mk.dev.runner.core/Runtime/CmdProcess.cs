using System.Diagnostics;
using System.Text;
using mk.dev.runner.core.Native;

namespace mk.dev.runner.core.Runtime
{
    internal static class CmdProcess
    {
        public static ProcessStartInfo Info(string commandLine, string workingDirectory)
        {
            return new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/d /s /c \"{commandLine}\"",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
        }

        public static ServiceProcess Start(ProcessStartInfo info)
        {
            var job = new JobObject();
            var p = new Process { StartInfo = info, EnableRaisingEvents = true };
            try
            {
                p.Start();
            }
            catch
            {
                job.Dispose();
                p.Dispose();
                throw;
            }
            var sp = new ServiceProcess(p, job, job.TryAssign(p));
            try
            {
                p.StandardInput.Close();
            }
            catch (InvalidOperationException)
            {
            }
            return sp;
        }
    }
}
