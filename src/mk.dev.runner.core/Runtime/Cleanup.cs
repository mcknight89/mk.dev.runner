using System.Diagnostics;
using System.Management;
using mk.dev.runner.core.Native;

namespace mk.dev.runner.core.Runtime
{
    internal static class Cleanup
    {
        public static List<string> FreePort(int port, ICollection<int>? keep = null)
        {
            var killed = new List<string>();
            if (port <= 0)
                return killed;
            foreach (var pid in TcpListeners.PidsOnPort(port))
            {
                if (pid == Environment.ProcessId || keep?.Contains(pid) == true)
                    continue;
                try
                {
                    using var p = Process.GetProcessById(pid);
                    var name = p.ProcessName;
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(3000);
                    killed.Add($"{name} ({pid})");
                }
                catch { }
            }
            return killed;
        }

        public static int KillGhosts(IEnumerable<string> dirs, string projectRoot)
        {
            var root = Trim(projectRoot);
            var targets = dirs.Select(Trim)
                .Where(d => d.Length > 3 && !d.Equals(root, StringComparison.OrdinalIgnoreCase) && Path.GetPathRoot(d) != d)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targets.Count == 0)
                return 0;

            int count = 0;
            try
            {
                using var search = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='dotnet.exe' OR Name='MSBuild.exe' OR Name='VBCSCompiler.exe'");
                foreach (ManagementObject mo in search.Get())
                {
                    using (mo)
                    {
                        var cl = mo["CommandLine"] as string;
                        if (string.IsNullOrEmpty(cl))
                            continue;
                        var pid = Convert.ToInt32(mo["ProcessId"]);
                        if (pid == Environment.ProcessId)
                            continue;
                        if (!targets.Any(t => Mentions(cl, t)))
                            continue;
                        try
                        {
                            using var p = Process.GetProcessById(pid);
                            p.Kill(entireProcessTree: true);
                            count++;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return count;
        }

        private static string Trim(string p)
        {
            return Path.GetFullPath(p).TrimEnd('\\', '/');
        }

        private static bool Mentions(string commandLine, string dir)
        {
            var cl = commandLine.Replace('/', '\\');
            int i = 0;
            while ((i = cl.IndexOf(dir, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = i + dir.Length;
                if (end == cl.Length || cl[end] is '\\' or '"' or ' ' or '\'')
                    return true;
                i = end;
            }
            return false;
        }
    }
}
