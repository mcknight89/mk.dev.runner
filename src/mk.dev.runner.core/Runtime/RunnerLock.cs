using System.Diagnostics;
using System.Text.RegularExpressions;

namespace mk.dev.runner.core.Runtime
{
    public sealed class RunnerLockedException : Exception
    {
        public RunnerLockedException(string message) : base(message)
        {
        }
    }

    public sealed class RunnerLock : IDisposable
    {
        private readonly FileStream _fs;

        private RunnerLock(FileStream fs)
        {
            _fs = fs;
        }

        private static string Safe(string name)
        {
            return Regex.Replace(name, "[^a-z0-9]+", "_", RegexOptions.IgnoreCase);
        }

        public static RunnerLock Acquire(string key, string projectName, string appName, bool checkDevMjs = false)
        {
            if (checkDevMjs)
                CheckDevMjs(projectName);

            var dir = AppPaths.LocksDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, key + ".lock");
            try
            {
                var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1, FileOptions.DeleteOnClose);
                var bytes = System.Text.Encoding.UTF8.GetBytes($"{Environment.ProcessId}|{appName}");
                fs.Write(bytes);
                fs.Flush();
                return new RunnerLock(fs);
            }
            catch (IOException)
            {
                string who = "another runner";
                try
                {
                    using var r = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(r);
                    var parts = sr.ReadToEnd().Split('|');
                    if (parts.Length == 2)
                        who = $"{parts[1]} (PID {parts[0]})";
                }
                catch { }
                throw new RunnerLockedException($"'{projectName}' is already running in {who}. Only one runner per project is allowed.");
            }
        }

        private static void CheckDevMjs(string projectName)
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), $"dev-runner-{Safe(projectName)}.lock");
                if (!File.Exists(path))
                    return;
                if (!int.TryParse(File.ReadAllText(path).Trim(), out var pid))
                    return;
                using var p = Process.GetProcessById(pid);
                if (p.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase))
                    throw new RunnerLockedException($"'{projectName}' is already running in dev.mjs (node PID {pid}). Close that window first.");
            }
            catch (RunnerLockedException) { throw; }
            catch { }
        }

        public void Dispose()
        {
            _fs.Dispose();
        }
    }
}
