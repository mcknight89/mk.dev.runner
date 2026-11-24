using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Logs;
using mk.dev.runner.core.Native;
using mk.dev.runner.core.Runtime;

namespace mk.dev.runner.core
{
    public sealed class ProjectRunner : IDisposable
    {
        private static readonly Dictionary<string, string> DotnetNoServers = new()
        {
            ["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1",
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["UseSharedCompilation"] = "false",
        };

        private static readonly Regex WatchIgnore = new(@"[\\/](bin|obj|node_modules|\.git|\.vs|\.next|dev-logs|dev-out|TestResults)[\\/]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WatchExt = new(@"\.(cs|csproj|props|targets|razor|cshtml)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BuildError = new(@": error", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly SerialQueue _q;
        private ServiceRunner[] _services = [];
        private RunnerLock? _lock;
        private Timer? _metricsTimer;

        private bool _building;
        private ServiceProcess? _buildProc;
        private readonly HashSet<int> _pendingAffected = [];
        private bool _pendingRestore;
        private readonly List<string> _buildErrors = [];
        private List<HashSet<string>> _depDirs = [];
        private FileSystemWatcher? _srcWatcher;
        private Timer? _srcDebounce;
        private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _seenMtimes = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _watchStartedAt;

        private readonly List<FileSystemWatcher> _envWatchers = [];
        private Timer? _envDebounce;

        public ProjectRunner(Project project, string appName)
        {
            Project = project;
            AppName = appName;
            BuildLog = new LogBuffer(project.Config.Globals.LogBufferLines);
            _q = new SerialQueue($"runner:{project.Name}");
            _q.Error += e => BuildLog.Add($"\x1b[91m[runner] internal error: {e.Message}\x1b[0m");
            LoadServices();
        }

        public Project Project { get; }
        public string AppName { get; }
        public IReadOnlyList<ServiceRunner> Services => _services;
        public LogBuffer BuildLog { get; }
        public BuildState BuildState { get; private set; } = BuildState.Idle;
        public bool SessionActive { get; private set; }
        public bool IsBuilding => _building;
        public bool IsManaged => Project.IsManaged;
        public IReadOnlyList<string> LastBuildErrors { get { lock (_buildErrors) return [.. _buildErrors]; } }

        public event Action? Changed;
        public event Action? ServicesChanged;
        public event Action? MetricsUpdated;
        public event Action<RunnerNotice>? Notice;

        public bool IsEnabled(ServiceRunner s)
        {
            return Project.IsEnabled(s.Config);
        }

        public int? PortOf(ServiceRunner s)
        {
            return Project.PortOf(s.Config);
        }

        public string? UrlOf(ServiceRunner s)
        {
            return Project.UrlOf(s.Config);
        }

        public string LogFileOf(ServiceRunner s)
        {
            return Path.Combine(Project.LogsDir, AppPaths.Safe(s.Key) + ".log");
        }

        public string BuildLogFile => Path.Combine(Project.LogsDir, "_build.log");

        public bool AnyRunning => _services.Any(s => s.HasProcess || s.IsBusy) || _building;
        public long TotalWorkingSet => _services.Sum(s => s.Metrics.WorkingSet);
        public double TotalCpu => _services.Sum(s => s.Metrics.CpuPercent);


        public void Start()
        {
            _q.Invoke(() =>
        {
            if (!SessionActive)
                BeginSession();
            StartAllCore();
        });
        }

        public void StopAll()
        {
            _q.Invoke(EndSession);
        }

        public void StartService(int idx)
        {
            _q.Invoke(() =>
        {
            if (!SessionActive)
                BeginSession();
            var s = _services[idx];
            if (!IsEnabled(s)) { ToggleCore(s); return; }
            if (s.HasProcess)
                return;
            s.Stopped = false;
            s.Fails = 0;
            if (IsManaged && s.Config.IsDotnet) { EnsureDepGraph(); s.State = ServiceState.Building; TriggerBuild([idx], false); }
            else
                StartCore(s);
        });
        }

        public void StopService(int idx)
        {
            _q.Post(() => { StopCore(_services[idx]); RaiseChanged(); });
        }

        public void RestartService(int idx)
        {
            _q.Post(() =>
        {
            if (!SessionActive) { BeginSessionSafe(); if (!SessionActive) return; }
            RestartCore(_services[idx]);
        });
        }

        public void RestartAll()
        {
            _q.Post(() =>
        {
            if (!SessionActive)
                return;
            var managed = new HashSet<int>();
            foreach (var s in _services.Where(s => IsEnabled(s) && !s.Stopped))
            {
                if (IsManaged && s.Config.IsDotnet) { managed.Add(s.Index); s.State = ServiceState.Building; s.Fails = 0; }
                else
                    RestartCore(s);
            }
            if (managed.Count > 0)
                TriggerBuild(managed, false);
        });
        }

        public void ToggleService(int idx)
        {
            _q.Post(() => ToggleCore(_services[idx]));
        }

        public void SetPort(int idx, int? port)
        {
            _q.Post(() =>
        {
            Project.SetPort(_services[idx].Config, port);
            RaiseChanged();
        });
        }

        public void CleanRebuild()
        {
            _q.Post(() =>
        {
            if (!IsManaged)
                return;
            if (!SessionActive) { BeginSessionSafe(); if (!SessionActive) return; }
            BuildLog.Add("\x1b[36m[build] clean rebuild — flushing build servers…\x1b[0m");
            FlushBuildServers();
            EnsureDepGraph();
            var idxs = _services.Where(s => s.Config.IsDotnet && IsEnabled(s)).Select(s => s.Index).ToHashSet();
            foreach (var i in idxs) { _services[i].State = ServiceState.Building; _services[i].Stopped = false; }
            RaiseChanged();
            TriggerBuild(idxs, true);
        });
        }

        public void ApplyEnvChanges()
        {
            _q.Post(() =>
        {
            Project.Reload();
            foreach (var s in _services)
            {
                if (!s.HasProcess || s.StartedEnv == null)
                    continue;
                var now = UserEnv(s);
                if (SameMap(now, s.StartedEnv))
                    continue;
                s.Logs.Add("\x1b[33m[runner] environment changed — restarting…\x1b[0m");
                RestartCore(s);
            }
        });
        }

        public bool ReloadProject()
        {
            bool ok = false;
            _q.Invoke(() =>
            {
                if (SessionActive)
                    return;
                Project.Reload();
                BuildLog.MaxLines = Project.Config.Globals.LogBufferLines;
                LoadServices();
                ok = true;
            });
            if (ok)
                ServicesChanged?.Invoke();
            return ok;
        }


        private void BeginSessionSafe()
        {
            try { BeginSession(); }
            catch (Exception e) { Notice?.Invoke(new RunnerNotice(Project.Name, null, e.Message, true)); }
        }

        private void BeginSession()
        {
            _lock = RunnerLock.Acquire(Project.LockKey, Project.Name, AppName, checkDevMjs: Project.IsLegacy);
            try
            {
                Project.Reload();
                if (!SameServices()) { LoadServices(); ServicesChanged?.Invoke(); }
                BuildLog.MaxLines = Project.Config.Globals.LogBufferLines;

                var stamp = DateTime.Now.ToString("g");
                foreach (var s in _services)
                {
                    s.Logs.OpenFile(LogFileOf(s), $"{s.Name} ({Project.Name}): session {stamp}");
                    s.Fails = 0;
                    s.Stopped = false;
                    s.State = IsEnabled(s) ? ServiceState.Stopped : ServiceState.Disabled;
                }
                if (IsManaged)
                    BuildLog.OpenFile(BuildLogFile, $"build ({Project.Name}): session {stamp}");
                BuildState = BuildState.Idle;

                KillLeftovers();
                KillGhosts();

                SessionActive = true;
                StartEnvWatchers();
                _metricsTimer = new Timer(_ => _q.Post(SampleMetrics), null, 1000, 2000);

                if (IsManaged)
                {
                    EnsureDepGraph(force: true);
                    BuildLog.Add("\x1b[2m[build] flushing stale build servers…\x1b[0m");
                    FlushBuildServers();
                    if (Project.Config.Globals.AutoRebuild)
                    {
                        StartSourceWatcher();
                        BuildLog.Add("\x1b[36m[watch] auto-rebuild ON — watching *.cs/.csproj/.razor/.cshtml\x1b[0m");
                    }
                    else
                        BuildLog.Add("\x1b[90m[watch] auto-rebuild OFF — restart a service to rebuild it\x1b[0m");
                }
                RaiseChanged();
            }
            catch
            {
                _lock?.Dispose();
                _lock = null;
                SessionActive = false;
                throw;
            }
        }

        private void StartAllCore()
        {
            var dotnet = new HashSet<int>();
            foreach (var s in _services)
            {
                if (!IsEnabled(s))
                {
                    if (s.State != ServiceState.Disabled)
                    {
                        s.State = ServiceState.Disabled;
                        s.Logs.Add("\x1b[90m[runner] disabled — enable it to start.\x1b[0m");
                    }
                    continue;
                }
                if (s.HasProcess || s.IsBusy)
                    continue;
                s.Stopped = false;
                s.Fails = 0;
                if (IsManaged && s.Config.IsDotnet) { s.State = ServiceState.Building; dotnet.Add(s.Index); }
                else
                    StartCore(s);
            }
            if (dotnet.Count > 0)
            {
                BuildLog.Add($"\x1b[2m[build] building {Path.GetFileName(Project.SolutionPath ?? "solution")}…\x1b[0m");
                TriggerBuild(dotnet, true);
            }
            RaiseChanged();
        }

        private void EndSession()
        {
            if (!SessionActive)
                return;
            _metricsTimer?.Dispose(); _metricsTimer = null;
            StopSourceWatcher();
            StopEnvWatchers();

            _buildProc?.Kill("stop", wait: true);
            _buildProc = null;
            _building = false;
            _pendingAffected.Clear();

            foreach (var s in _services)
            {
                s.Stopped = true;
                s.CancelTimers();
                if (s.Proc is { } sp) { sp.Kill("stop", wait: true); s.Proc = null; }
            }
            foreach (var s in _services)
            {
                s.Logs.Add("\x1b[2m[runner] stopped.\x1b[0m");
                s.State = IsEnabled(s) ? ServiceState.Stopped : ServiceState.Disabled;
                s.Metrics = ServiceMetrics.Empty;
                s.ClearHistory();
            }

            KillLeftovers();
            KillGhosts();

            foreach (var s in _services) s.Logs.CloseFile();
            BuildLog.CloseFile();
            BuildState = BuildState.Idle;

            _lock?.Dispose();
            _lock = null;
            SessionActive = false;
            RaiseChanged();
            MetricsUpdated?.Invoke();
        }


        private void StartCore(ServiceRunner s)
        {
            if (!IsEnabled(s))
                return;
            s.Stopped = false;
            s.CancelTimers();
            s.State = ServiceState.Starting;
            s.Logs.Add($"\x1b[2m[runner] starting {s.Name}…\x1b[0m");
            RaiseChanged();

            var port = PortOf(s);
            FreePort(port, s);

            var dir = Project.ServiceDir(s.Config);
            if (!Directory.Exists(dir))
            {
                s.State = ServiceState.Crashed;
                s.Logs.Add($"\x1b[91m[runner] folder not found: {dir}\x1b[0m");
                Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, "folder not found", true));
                RaiseChanged();
                return;
            }

            string line;
            try { line = CommandLine(s, port); }
            catch (Exception e)
            {
                s.State = ServiceState.Crashed;
                s.Logs.Add($"\x1b[91m[runner] {e.Message}\x1b[0m");
                RaiseChanged();
                return;
            }

            var psi = new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/d /s /c \"{line}\"",
                WorkingDirectory = dir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.Environment["FORCE_COLOR"] = "1";
            psi.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";
            if (s.Config.IsDotnet)
            {
                foreach (var (k, v) in DotnetNoServers) psi.Environment[k] = v;
                if (port is > 0 && !IsManaged) psi.Environment["ASPNETCORE_URLS"] = $"http://localhost:{port}";
                psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            }
            else if (port is > 0)
                psi.Environment["PORT"] = port.ToString();

            var userEnv = UserEnv(s);
            foreach (var (k, v) in userEnv) psi.Environment[k] = v;

            var (ready, fail) = Patterns(s.Config);
            s.Ready = ready;
            s.Fail = fail;

            var job = new JobObject();
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            try { p.Start(); }
            catch (Exception e)
            {
                job.Dispose();
                s.State = ServiceState.Crashed;
                s.Logs.Add($"\x1b[91m[runner] failed to start: {e.Message}\x1b[0m");
                RaiseChanged();
                return;
            }
            var sp = new ServiceProcess(p, job, job.TryAssign(p));
            s.Proc = sp;
            s.StartedEnv = userEnv;
            try { p.StandardInput.Close(); } catch { }

            p.OutputDataReceived += (_, e) => OnLine(s, sp, e.Data);
            p.ErrorDataReceived += (_, e) => OnLine(s, sp, e.Data);
            p.Exited += (_, _) => OnProcessExited(s, sp);
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (p.HasExited)
                OnProcessExited(s, sp);

            var timeout = TimeSpan.FromSeconds(Math.Max(5, Project.Config.Globals.StartTimeoutSec));
            s.ReadyTimer = _q.After(timeout, () =>
            {
                if (s.Proc == sp && s.State is ServiceState.Starting or ServiceState.Restarting)
                {
                    s.State = ServiceState.Running;
                    RaiseChanged();
                }
            });
        }

        private void OnLine(ServiceRunner s, ServiceProcess sp, string? line)
        {
            if (line == null)
                return;
            s.Logs.Add(line);
            var plain = Ansi.Strip(line);
            if (s.Fail != null && s.Fail.IsMatch(plain))
            {
                _q.Post(() =>
                {
                    if (s.Proc != sp || s.State == ServiceState.Failed)
                        return;
                    s.State = ServiceState.Failed;
                    Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, "build failed — waiting for a file change", true));
                    RaiseChanged();
                });
            }
            else if (s.State is ServiceState.Starting or ServiceState.Restarting or ServiceState.Failed
                     && (s.Ready == null || s.Ready.IsMatch(plain)))
            {
                _q.Post(() =>
                {
                    if (s.Proc != sp || s.State is not (ServiceState.Starting or ServiceState.Restarting or ServiceState.Failed))
                        return;
                    s.State = ServiceState.Running;
                    s.ReadyTimer?.Dispose(); s.ReadyTimer = null;
                    RaiseChanged();
                });
            }
        }

        private void OnProcessExited(ServiceRunner s, ServiceProcess sp)
        {
            Task.Run(() =>
            {
                int code = -1;
                try
                {
                    if (sp.InJob)
                        sp.Job.Terminate();
                    sp.P.WaitForExit(2000);
                    code = sp.P.ExitCode;
                }
                catch { }
                _q.Post(() => HandleExit(s, sp, code));
            });
        }

        private readonly HashSet<ServiceProcess> _exited = [];

        private void HandleExit(ServiceRunner s, ServiceProcess sp, int code)
        {
            if (!_exited.Add(sp))
                return;
            if (_exited.Count > 256)
                _exited.Clear();
            try { sp.Job.Dispose(); } catch { }
            try { sp.P.Dispose(); } catch { }

            bool current = s.Proc == sp;
            if (current) { s.Proc = null; s.ReadyTimer?.Dispose(); s.ReadyTimer = null; s.Metrics = ServiceMetrics.Empty; }

            if (sp.KillReason == "restart") { RaiseChanged(); return; }

            if (sp.KillReason == "stop" || s.Stopped)
            {
                if (s.Proc == null && s.State is not (ServiceState.Building or ServiceState.Retrying or ServiceState.Starting))
                    s.State = IsEnabled(s) ? ServiceState.Stopped : ServiceState.Disabled;
                if (s.Proc == null && SessionActive)
                    s.Logs.Add("\x1b[2m[runner] stopped.\x1b[0m");
                RaiseChanged();
                return;
            }
            if (!current)
                return;

            var g = Project.Config.Globals;
            if (g.AutoRetry)
            {
                var ran = DateTime.Now - sp.StartedAt;
                s.Fails = ran.TotalMilliseconds < 8000 ? s.Fails + 1 : 0;
                int max = Math.Max(1, g.MaxRetries);
                if (s.Fails >= max)
                {
                    s.State = ServiceState.Crashed;
                    s.Logs.Add($"\x1b[91m[runner] crashed {s.Fails}× — stopped. Fix it, then restart.\x1b[0m");
                    Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, $"crashed {s.Fails} times — stopped", true));
                }
                else
                {
                    s.State = ServiceState.Retrying;
                    var baseDelay = Math.Max(0.5, g.RetryDelaySec);
                    var delay = s.Fails == 0 ? baseDelay : Math.Min(baseDelay * Math.Pow(2, s.Fails - 1), 30);
                    s.Logs.Add($"\x1b[90m[runner] exited ({code}) — retry {Math.Max(1, s.Fails)}/{max} in {delay:0.#}s…\x1b[0m");
                    s.RetryTimer = _q.After(TimeSpan.FromSeconds(delay), () =>
                    {
                        if (!s.Stopped && s.State == ServiceState.Retrying && SessionActive)
                            StartCore(s);
                    });
                }
            }
            else
            {
                s.State = ServiceState.Crashed;
                s.Logs.Add($"\x1b[91m[runner] exited ({code}) — stopped. Read the error above, then restart.\x1b[0m");
                Notice?.Invoke(new RunnerNotice(Project.Name, s.Name, $"exited with code {code}", true));
            }
            RaiseChanged();
        }

        private void StopCore(ServiceRunner s)
        {
            s.Stopped = true;
            s.CancelTimers();
            s.State = IsEnabled(s) ? ServiceState.Stopped : ServiceState.Disabled;
            if (s.Proc is { } sp) { sp.Kill("stop", wait: false); s.Proc = null; }
            s.Metrics = ServiceMetrics.Empty;
            FreePort(PortOf(s), s);
        }

        private void RestartCore(ServiceRunner s)
        {
            s.Fails = 0;
            s.Stopped = false;
            if (IsManaged && s.Config.IsDotnet)
            {
                s.Logs.Add("\x1b[2m[runner] rebuild + restart…\x1b[0m");
                s.State = ServiceState.Building;
                EnsureDepGraph();
                RaiseChanged();
                TriggerBuild([s.Index], false);
                return;
            }
            s.Logs.Add("\x1b[2m[runner] restarting…\x1b[0m");
            s.CancelTimers();
            if (s.Proc is { } sp) { sp.Kill("restart", wait: true); s.Proc = null; }
            StartCore(s);
        }

        private void ToggleCore(ServiceRunner s)
        {
            var enable = !IsEnabled(s);
            Project.SetEnabled(s.Config, enable);
            if (enable)
            {
                s.Fails = 0;
                if (!SessionActive) { s.State = ServiceState.Stopped; RaiseChanged(); return; }
                s.Stopped = false;
                if (IsManaged && s.Config.IsDotnet)
                {
                    EnsureDepGraph();
                    s.State = ServiceState.Building;
                    RaiseChanged();
                    TriggerBuild([s.Index], false);
                }
                else
                    StartCore(s);
            }
            else
            {
                StopCore(s);
                s.State = ServiceState.Disabled;
                s.Logs.Add("\x1b[90m[runner] disabled.\x1b[0m");
            }
            RaiseChanged();
        }


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
                RaiseChanged();
                return;
            }

            _building = true;
            BuildState = BuildState.Building;
            lock (_buildErrors) _buildErrors.Clear();
            foreach (var i in affected)
            {
                var s = _services[i];
                s.CancelTimers();
                if (s.Proc is { } sp) { sp.Kill("restart", wait: true); s.Proc = null; }
                FreePort(PortOf(s), s);
                s.State = ServiceState.Building;
            }
            BuildLog.Add($"\x1b[2m[build] stopped {affected.Count} service(s) to unlock DLLs, building{(restore ? "" : " --no-restore")}…\x1b[0m");
            RaiseChanged();

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
                RaiseChanged();
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
                int code = -1;
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
                int n = errs.Count;
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
                Notice?.Invoke(new RunnerNotice(Project.Name, null, $"build failed — {n} error{(n == 1 ? "" : "s")}", true));
            }
            RaiseChanged();
            if (_pendingAffected.Count > 0)
                RunBuildCycle();
        }

        private void FlushBuildServers()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("dotnet", "build-server shutdown")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Project.RootDir,
                });
                if (p != null && !p.WaitForExit(20000))
                    p.Kill(true);
            }
            catch { }
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
                FileSystemEventHandler h = (_, e) => _q.Post(() => OnSourceChanged(e.FullPath));
                _srcWatcher.Changed += h;
                _srcWatcher.Created += h;
                _srcWatcher.Renamed += (_, e) => _q.Post(() => OnSourceChanged(e.FullPath));
                _srcWatcher.EnableRaisingEvents = true;
            }
            catch (Exception e) { BuildLog.Add($"\x1b[31m[watch] failed to watch {root}: {e.Message}\x1b[0m"); }
        }

        private void StopSourceWatcher()
        {
            _srcDebounce?.Dispose(); _srcDebounce = null;
            _srcWatcher?.Dispose(); _srcWatcher = null;
            _changed.Clear();
        }

        private void OnSourceChanged(string abs)
        {
            if (!SessionActive || WatchIgnore.IsMatch(abs) || !WatchExt.IsMatch(abs))
                return;
            DateTime mtime;
            try { mtime = File.GetLastWriteTime(abs); } catch { mtime = DateTime.Now; }
            var isNew = !_seenMtimes.TryGetValue(abs, out var prev) ? mtime > _watchStartedAt : mtime != prev;
            _seenMtimes[abs] = mtime;
            if (!isNew)
                return;
            _changed.Add(abs);
            _srcDebounce?.Dispose();
            _srcDebounce = _q.After(TimeSpan.FromSeconds(Math.Max(0.2, Project.Config.Globals.RebuildDebounceSec)), FlushChanges);
        }

        private void FlushChanges()
        {
            var paths = _changed.ToList();
            _changed.Clear();
            if (paths.Count == 0 || !SessionActive)
                return;
            var affected = new HashSet<int>();
            bool restore = false;
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
            var shown = string.Join(", ", files.Take(6)) + (files.Count > 6 ? $" +{files.Count - 6} more" : "");
            BuildLog.Add($"\x1b[36m[watch] changed: {shown} → rebuild: {string.Join(", ", affected.Select(i => _services[i].Name))}\x1b[0m");
            RaiseChanged();
            TriggerBuild(affected, restore);
        }


        private void StartEnvWatchers()
        {
            var files = Project.Config.Globals.RestartOnChange ?? [".env"];
            foreach (var f in files.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                var full = Path.GetFullPath(Path.Combine(Project.RootDir, f));
                var dir = Path.GetDirectoryName(full);
                if (dir == null || !Directory.Exists(dir))
                    continue;
                try
                {
                    var w = new FileSystemWatcher(dir, Path.GetFileName(full))
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    };
                    FileSystemEventHandler h = (_, _) => _q.Post(OnEnvFileChanged);
                    w.Changed += h; w.Created += h;
                    w.Renamed += (_, _) => _q.Post(OnEnvFileChanged);
                    w.EnableRaisingEvents = true;
                    _envWatchers.Add(w);
                }
                catch { }
            }
        }

        private void StopEnvWatchers()
        {
            _envDebounce?.Dispose(); _envDebounce = null;
            foreach (var w in _envWatchers) w.Dispose();
            _envWatchers.Clear();
        }

        private void OnEnvFileChanged()
        {
            _envDebounce?.Dispose();
            _envDebounce = _q.After(TimeSpan.FromMilliseconds(300), () =>
            {
                if (!SessionActive)
                    return;
                var managed = new HashSet<int>();
                foreach (var s in _services.Where(s => s.Config.IsDotnet && IsEnabled(s) && !s.Stopped))
                {
                    s.Logs.Add("\x1b[33m[runner] .env changed — restarting…\x1b[0m");
                    if (IsManaged) { managed.Add(s.Index); s.State = ServiceState.Building; }
                    else
                        RestartCore(s);
                }
                if (managed.Count > 0)
                    TriggerBuild(managed, false);
                RaiseChanged();
            });
        }


        private void SampleMetrics()
        {
            if (!SessionActive)
                return;
            List<(int Pid, int Port)> listeners;
            try { listeners = TcpListeners.All(); } catch { listeners = []; }
            var now = DateTime.Now;
            int cores = Environment.ProcessorCount;

            foreach (var s in _services)
            {
                if (s.Proc is not { } sp)
                {
                    if (s.Metrics != ServiceMetrics.Empty)
                        s.Metrics = ServiceMetrics.Empty;
                    continue;
                }
                int[] pids;
                if (sp.InJob)
                    pids = sp.Job.ProcessIds();
                else { try { pids = [sp.P.Id]; } catch { pids = []; } }

                var samples = new List<ProcessSample>(pids.Length);
                foreach (var pid in pids)
                    // conhost is the hidden console window each service gets, not part of the app
                    if (ProcessInfo.Sample(pid) is { } ps && !ps.Name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase))
                        samples.Add(ps);

                double cpuPct = 0;
                if (sp.InJob)
                {
                    var cpu = sp.Job.TotalCpu();
                    var wall = (now - sp.LastSampleAt).TotalSeconds;
                    if (wall > 0.2)
                        cpuPct = Math.Clamp((cpu - sp.LastCpu).TotalSeconds / (wall * cores) * 100, 0, 100);
                    sp.LastCpu = cpu;
                    sp.LastSampleAt = now;
                }

                var pidSet = pids.ToHashSet();
                var ports = listeners.Where(l => pidSet.Contains(l.Pid)).Select(l => l.Port).Distinct().Order().ToList();
                var ws = samples.Sum(x => x.WorkingSet);
                s.Metrics = new ServiceMetrics(ws, samples.Sum(x => x.PrivateBytes), cpuPct,
                                               samples.OrderByDescending(x => x.WorkingSet).ToList(), ports);
                s.PushHistory(ws, cpuPct);
            }
            MetricsUpdated?.Invoke();
        }


        private void LoadServices()
        {
            int max = Project.Config.Globals.LogBufferLines;
            var old = _services;
            _services = Project.Config.Services.Select((c, i) =>
            {
                var existing = old.FirstOrDefault(o => o.Key.Equals(c.Key, StringComparison.OrdinalIgnoreCase) && o.Index == i);
                if (existing != null) { existing.Config = c; existing.Logs.MaxLines = max; return existing; }
                return new ServiceRunner(i, c, max);
            }).ToArray();
            foreach (var s in _services)
                s.State = IsEnabled(s) ? ServiceState.Stopped : ServiceState.Disabled;
            _depDirs = [];
        }

        private bool SameServices()
        {
            return _services.Length == Project.Config.Services.Count &&
            _services.Zip(Project.Config.Services).All(z => z.First.Key.Equals(z.Second.Key, StringComparison.OrdinalIgnoreCase));
        }

        private Dictionary<string, string> UserEnv(ServiceRunner s)
        {
            var fresh = Project.Service(s.Key);
            if (fresh != null)
                s.Config = fresh;
            return Project.EffectiveEnv(s.Config);
        }

        private static bool SameMap(Dictionary<string, string> a, Dictionary<string, string> b)
        {
            return a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
        }

        private string CommandLine(ServiceRunner s, int? port)
        {
            var c = s.Config;
            var g = Project.Config.Globals;
            var args = new List<string>();
            string exe;
            if (c.IsNpm)
            {
                exe = "npm";
                args.AddRange(["run", string.IsNullOrWhiteSpace(c.Script) ? "dev" : c.Script!]);
                if (c.Args is { Count: > 0 }) { args.Add("--"); args.AddRange(c.Args); }
            }
            else if (c.IsDotnet)
            {
                exe = "dotnet";
                var mode = g.DotnetMode.ToLowerInvariant();
                args.AddRange(mode switch
                {
                    "managed" => ["run", "--no-build", "--no-restore"],
                    "run" => ["run"],
                    _ => ["watch", "--no-hot-reload"],
                });
                var extra = new List<string>();
                if (mode == "managed" && port is > 0) extra.AddRange(["--urls", $"http://localhost:{port}"]);
                if (c.Args is { Count: > 0 })
                    extra.AddRange(c.Args);
                if (extra.Count > 0) { args.Add("--"); args.AddRange(extra); }
            }
            else
            {
                exe = string.IsNullOrWhiteSpace(c.Cmd) ? throw new InvalidOperationException($"service '{c.Key}' has no \"cmd\".") : c.Cmd!;
                if (c.Args != null)
                    args.AddRange(c.Args);
            }
            return string.Join(' ', new[] { Quote(exe) }.Concat(args.Select(Quote)));
        }

        private static string Quote(string a)
        {
            if (a.Length == 0)
                return "\"\"";
            if (a.IndexOfAny([' ', '\t', '"', '&', '|', '<', '>', '^', '(', ')']) < 0)
                return a;
            return "\"" + a.Replace("\"", "\\\"") + "\"";
        }

        private static (Regex? ready, Regex? fail) Patterns(ServiceConfig c)
        {
            Regex? ready = null;
            if (!string.IsNullOrWhiteSpace(c.ReadyPattern))
            {
                try { ready = new Regex(c.ReadyPattern!, RegexOptions.IgnoreCase); } catch { }
            }
            ready ??= c.IsNpm ? new Regex(@"ready in|- local:|started server|compiled|localhost:\d+", RegexOptions.IgnoreCase)
                    : c.IsDotnet ? new Regex(@"now listening on|application started", RegexOptions.IgnoreCase)
                    : null;
            var fail = c.IsDotnet ? new Regex(@"waiting for a file to change|build failed", RegexOptions.IgnoreCase) : null;
            return (ready, fail);
        }

        private void FreePort(int? port, ServiceRunner? owner)
        {
            if (port is not > 0)
                return;
            var killed = Cleanup.FreePort(port.Value);
            if (killed.Count > 0 && owner != null)
                owner.Logs.Add($"\x1b[33m[runner] freed port {port}: killed {string.Join(", ", killed)}\x1b[0m");
        }

        private void KillLeftovers()
        {
            foreach (var s in _services) FreePort(PortOf(s), null);
        }

        private void KillGhosts()
        {
            var dirs = _services.Where(s => s.Config.IsDotnet).Select(s => Project.ServiceDir(s.Config));
            var n = Cleanup.KillGhosts(dirs, Project.RootDir);
            if (n > 0)
                BuildLog.Add($"\x1b[33m[runner] swept {n} stray dotnet process(es)\x1b[0m");
        }

        private void RaiseChanged()
        {
            Changed?.Invoke();
        }

        public void Dispose()
        {
            try { StopAll(); } catch { }
            _q.Dispose();
        }
    }
}
