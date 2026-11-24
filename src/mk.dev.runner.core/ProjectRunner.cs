using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using mk.dev.runner.core.Config;
using mk.dev.runner.core.Logs;
using mk.dev.runner.core.Native;
using mk.dev.runner.core.Runtime;

namespace mk.dev.runner.core
{
    public sealed partial class ProjectRunner : IDisposable
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
