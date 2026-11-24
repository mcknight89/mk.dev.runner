using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Logs;
using mk.dev.runner.core.Native;
using mk.dev.runner.core.Runtime;

namespace mk.dev.runner.core
{
    public sealed partial class ProjectRunner
    {
        private void TriggerBuild(IEnumerable<int> affected, bool restore)
        {
            foreach (var i in affected) _pendingAffected.Add(i);
            if (restore)
                _pendingRestore = true;
            if (!_building)
                RunBuildCycle();
        }

        private void RunBuildCycle()
        {
            var sln = Project.SolutionPath;
            var affected = _pendingAffected.ToList();
            _pendingAffected.Clear();
            var restore = _pendingRestore;
            _pendingRestore = false;

            if (sln == null || !File.Exists(sln))
            {
                var msg = sln == null ? "no \"solution\" set in config globals — cannot build." : $"solution not found: {sln}";
                BuildLog.Add($"\x1b[31m[build] {msg}\x1b[0m");
                BuildState = BuildState.Failed;
                foreach (var i in affected)
                {
                    _services[i].State = ServiceState.Failed;
                    _services[i].Logs.Add($"\x1b[91m[build] {msg}\x1b[0m");
                }
                return;
            }

            _building = true;
            BuildState = BuildState.Building;
            lock (_buildErrors) _buildErrors.Clear();
            foreach (var i in affected)
            {
                var s = _services[i];
                s.CancelTimers();
                if (s.Proc is { } sp)
                {
                    KillLogged(s, sp, "restart", wait: true);
                    s.Proc = null;
                }
                s.State = ServiceState.Building;
            }
            BuildLog.Add($"\x1b[2m[build] stopped {affected.Count} service(s) to unlock DLLs, building{(restore ? "" : " --no-restore")}…\x1b[0m");

            var args = $"build \"{Path.GetFileName(sln)}\" -c Debug --verbosity minimal{(restore ? "" : " --no-restore")}";
            var psi = new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/d /s /c \"dotnet {args}\"",
                WorkingDirectory = Path.GetDirectoryName(sln)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var (k, v) in DotnetNoServers) psi.Environment[k] = v;

            var job = new JobObject();
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            try { p.Start(); }
            catch (Exception e)
            {
                job.Dispose();
                _building = false;
                BuildState = BuildState.Failed;
                BuildLog.Add($"\x1b[31m[build] could not start dotnet: {e.Message}\x1b[0m");
                return;
            }
            var bp = new ServiceProcess(p, job, job.TryAssign(p));
            _buildProc = bp;
            try { p.StandardInput.Close(); } catch { }

            void OnData(string? line)
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                if (BuildError.IsMatch(line))
                    lock (_buildErrors) _buildErrors.Add(Ansi.Strip(line).Trim());
                BuildLog.Add(line);
            }
            p.OutputDataReceived += (_, e) => OnData(e.Data);
            p.ErrorDataReceived += (_, e) => OnData(e.Data);
            p.Exited += (_, _) => Task.Run(() =>
            {
                var code = -1;
                try { p.WaitForExit(); code = p.ExitCode; } catch { }
                _q.Post(() => OnBuildExit(bp, code, affected));
            });
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }

        private void OnBuildExit(ServiceProcess bp, int code, List<int> affected)
        {
            try { bp.Job.Dispose(); bp.P.Dispose(); } catch { }
            if (_buildProc != bp)
                return;
            _buildProc = null;
            _building = false;

            if (code == 0)
            {
                BuildState = BuildState.Ok;
                var start = affected.Where(i => IsEnabled(_services[i]) && !_services[i].Stopped).ToList();
                BuildLog.Add($"\x1b[32m[build] ✓ ok — starting {start.Count} service(s)\x1b[0m");
                foreach (var i in start) StartCore(_services[i]);
            }
            else
            {
                BuildState = BuildState.Failed;
                var errs = LastBuildErrors.Distinct().ToList();
                var n = errs.Count;
                var shown = n > 0 ? errs.Take(12).ToList() : ["(no compiler errors parsed from output — see full build log)"];
                BuildLog.Add($"\x1b[41m\x1b[97m [build] FAILED — exit {code}, {n} error{(n == 1 ? "" : "s")} \x1b[0m");
                foreach (var e in shown) BuildLog.Add($"\x1b[31m  • {e}\x1b[0m");
                if (n > 12)
                    BuildLog.Add($"\x1b[31m  … and {n - 12} more\x1b[0m");
                foreach (var i in affected)
                {
                    var s = _services[i];
                    if (s.Stopped)
                        continue;
                    s.State = ServiceState.Failed;
                    s.Logs.Add($"\x1b[91m[build] FAILED — {n} error{(n == 1 ? "" : "s")}, not started:\x1b[0m");
                    foreach (var e in shown) s.Logs.Add($"\x1b[91m  • {e}\x1b[0m");
                    if (n > 12)
                        s.Logs.Add($"\x1b[91m  … and {n - 12} more\x1b[0m");
                }
                Notice?.Invoke(new RunnerNotice(Project.Name, null, $"build failed — {n} error{(n == 1 ? "" : "s")}"));
            }
            if (_pendingAffected.Count > 0)
                RunBuildCycle();
        }

        private void EnsureDepGraph(bool force = false)
        {
            if (!force && _depDirs.Count == _services.Length)
                return;
            _depDirs = _services.Select(s => s.Config.IsDotnet ? DepDirsFor(Project.ServiceDir(s.Config)) : new HashSet<string>()).ToList();
        }

        private static HashSet<string> DepDirsFor(string serviceDir)
        {
            var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            if (CsprojIn(serviceDir) is { } first)
                stack.Push(first);
            var re = new Regex("<ProjectReference\\s+Include\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            while (stack.Count > 0)
            {
                var cs = stack.Pop();
                if (!seen.Add(cs))
                    continue;
                dirs.Add(Path.GetDirectoryName(cs)!);
                string txt;
                try { txt = File.ReadAllText(cs); } catch { continue; }
                foreach (Match m in re.Matches(txt))
                    stack.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cs)!, m.Groups[1].Value.Replace('/', '\\'))));
            }
            return dirs;
        }

        private static string? CsprojIn(string dir)
        {
            try { return Directory.GetFiles(dir, "*.csproj").FirstOrDefault(); } catch { return null; }
        }

        private HashSet<int> AffectedServices(string absPath)
        {
            var hit = new HashSet<int>();
            for (int i = 0; i < _services.Length && i < _depDirs.Count; i++)
            {
                var s = _services[i];
                if (!s.Config.IsDotnet || !IsEnabled(s) || s.Stopped)
                    continue;
                foreach (var d in _depDirs[i])
                    if (absPath.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                        absPath.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { hit.Add(i); break; }
            }
            return hit;
        }

        private void StartSourceWatcher()
        {
            var sln = Project.SolutionPath;
            if (sln == null)
                return;
            var root = Path.GetDirectoryName(sln)!;
            _watchStartedAt = DateTime.Now;
            try
            {
                _srcWatcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    InternalBufferSize = 64 * 1024,
                };
                _srcWatcher.Changed += (_, e) => PostSourceChange(e.FullPath, false);
                _srcWatcher.Created += (_, e) => PostSourceChange(e.FullPath, false);
                _srcWatcher.Deleted += (_, e) => PostSourceChange(e.FullPath, true);
                _srcWatcher.Renamed += (_, e) =>
                {
                    PostSourceChange(e.OldFullPath, true);
                    PostSourceChange(e.FullPath, true);
                };
                _srcWatcher.Error += (_, e) => _q.Post(() => OnWatcherError(e.GetException()));
                _srcWatcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { BuildLog.Add($"\x1b[31m[watch] failed to watch {root}: {e.Message}\x1b[0m"); }
        }

        private void StopSourceWatcher()
        {
            _srcDebounce?.Dispose(); _srcDebounce = null;
            _srcWatcher?.Dispose(); _srcWatcher = null;
            _changed.Clear();
            _rebuildAll = false;
        }

        private void PostSourceChange(string abs, bool always)
        {
            if (WatchIgnore.IsMatch(abs) || !WatchExt.IsMatch(abs))
                return;
            _q.Post(() => OnSourceChanged(abs, always));
        }

        private void OnSourceChanged(string abs, bool always)
        {
            if (!SessionActive)
                return;
            if (!always)
            {
                DateTime mtime;
                try { mtime = File.GetLastWriteTime(abs); } catch { mtime = DateTime.Now; }
                var isNew = !_seenMtimes.TryGetValue(abs, out var prev) ? mtime > _watchStartedAt : mtime != prev;
                _seenMtimes[abs] = mtime;
                if (!isNew)
                    return;
            }
            _changed.Add(abs);
            DebounceFlush();
        }

        private void OnWatcherError(Exception e)
        {
            if (!SessionActive)
                return;
            BuildLog.Add($"\x1b[33m[watch] missed some file changes ({e.Message}), rebuilding everything\x1b[0m");
            _rebuildAll = true;
            DebounceFlush();
        }

        private void DebounceFlush()
        {
            _srcDebounce?.Dispose();
            _srcDebounce = _q.After(TimeSpan.FromSeconds(Math.Max(0.2, Project.Config.Globals.RebuildDebounceSec)), FlushChanges);
        }

        private void FlushChanges()
        {
            var paths = _changed.ToList();
            _changed.Clear();
            var all = _rebuildAll;
            _rebuildAll = false;
            if ((paths.Count == 0 && !all) || !SessionActive)
                return;
            var affected = new HashSet<int>();
            var restore = all;
            if (all)
                affected.UnionWith(_services.Where(x => x.Config.IsDotnet && IsEnabled(x) && !x.Stopped).Select(x => x.Index));
            foreach (var p in paths)
            {
                if (Regex.IsMatch(p, @"\.(csproj|props|targets)$", RegexOptions.IgnoreCase))
                    restore = true;
                affected.UnionWith(AffectedServices(p));
            }
            if (affected.Count == 0)
                return;
            foreach (var i in affected) _services[i].State = ServiceState.Building;
            var files = paths.Select(Path.GetFileName).ToList();
            var shown = files.Count == 0 ? "missed changes" : string.Join(", ", files.Take(6)) + (files.Count > 6 ? $" +{files.Count - 6} more" : "");
            BuildLog.Add($"\x1b[36m[watch] changed: {shown} → rebuild: {string.Join(", ", affected.Select(i => _services[i].Name))}\x1b[0m");
            TriggerBuild(affected, restore);
        }
    }
}
