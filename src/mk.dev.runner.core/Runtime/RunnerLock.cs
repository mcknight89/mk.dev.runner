using System.Diagnostics;
using System.Text.RegularExpressions;

namespace mk.dev.runner.core.Runtime;

public sealed class RunnerLockedException(string message) : Exception(message);

/// <summary>
/// One runner per project across the CLI, the desktop app and old dev.mjs windows — stops services fighting over ports.
/// The lock is an open file, so it frees itself if the runner dies.
/// </summary>
public sealed class RunnerLock : IDisposable
{
    readonly FileStream _fs;

    RunnerLock(FileStream fs) => _fs = fs;

    static string Safe(string name) => Regex.Replace(name, "[^a-z0-9]+", "_", RegexOptions.IgnoreCase);

    /// <param name="key">What is locked: the project's config file (Project.LockKey), so two projects with the same name don't clash.</param>
    /// <param name="checkDevMjs">Also refuse while an old dev.mjs window runs it (old-layout projects only).</param>
    public static RunnerLock Acquire(string key, string projectName, string appName, bool checkDevMjs = false)
    {
        if (checkDevMjs) CheckDevMjs(projectName);

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
                if (parts.Length == 2) who = $"{parts[1]} (PID {parts[0]})";
            }
            catch { }
            throw new RunnerLockedException($"'{projectName}' is already running in {who}. Only one runner per project is allowed.");
        }
    }

    /// <summary>dev.mjs writes %TEMP%\dev-runner-&lt;name&gt;.lock with its PID.</summary>
    static void CheckDevMjs(string projectName)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"dev-runner-{Safe(projectName)}.lock");
            if (!File.Exists(path)) return;
            if (!int.TryParse(File.ReadAllText(path).Trim(), out var pid)) return;
            using var p = Process.GetProcessById(pid);
            if (p.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase))
                throw new RunnerLockedException($"'{projectName}' is already running in dev.mjs (node PID {pid}). Close that window first.");
        }
        catch (RunnerLockedException) { throw; }
        catch { }
    }

    public void Dispose() => _fs.Dispose();
}
