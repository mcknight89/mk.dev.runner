using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using mk.dev.runner.core;
using mk.dev.runner.core.Config;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed partial class ProjectViewModel : Observable, IDisposable
    {
        private readonly MainViewModel _main;

        public ProjectViewModel(MainViewModel main, LoadedProject loaded)
        {
            _main = main;
            Entry = loaded.Entry;
            Error = loaded.Error;
            Name = loaded.Project?.Name ?? Entry.DisplayName;

            if (loaded.Project is { } p)
                Attach(p);
            BuildServices();

            StartAll = new Command(() => RunBackground(() => Runner!.Start(), "Starting…"), () => Runner != null && !_busy && Services.Count > 0);
            StopAll = new Command(() => RunBackground(() => Runner!.StopAll(), "Stopping…"), () => Runner?.SessionActive == true && !_busy);
            RestartAll = new Command(() => Runner!.RestartAll(), () => Runner?.SessionActive == true);
            CleanRebuild = new Command(() => Runner!.CleanRebuild(), () => Runner?.IsManaged == true);
            EditEnv = new Command(() => _main.OpenEnvEditor(this), () => Runner != null);
            OpenFolder = new Command(() => Shell.Open(Runner?.Project.RootDir ?? Entry.Folder ?? Path.GetDirectoryName(Entry.ConfigFile)!));
            EditConfig = new Command(() => Shell.OpenInEditor(Entry.ConfigFile));
            OpenLogs = new Command(() =>
            {
                if (Runner == null)
                    return;
                Directory.CreateDirectory(Runner.Project.LogsDir);
                Shell.Open(Runner.Project.LogsDir);
            });
            ViewBuildLog = new Command(() => { if (Runner is { } r) new Views.LogFileWindow($"{Name} build log", r.BuildLogFile).Show(); },
                                       () => Runner?.IsManaged == true);
            Rename = new Command(DoRename);
            EditProject = new Command(DoEditProject, () => Runner != null && !Runner.SessionActive);
            Reload = new Command(ReloadConfig, () => Runner?.SessionActive != true);
            ClearLog = new Command(() => SelectedLog?.Clear());
            Remove = new Command(() => { _main.Selected = this; _main.RemoveProject.Execute(null); }, () => Runner?.SessionActive != true);
        }

        public ProjectEntry Entry { get; }
        public ProjectRunner? Runner { get; private set; }
        public string Name { get; private set; }
        public string? Error { get; private set; }
        public bool HasError => Error != null;

        public ObservableCollection<ServiceViewModel> Services { get; } = [];
        public ObservableCollection<LogSourceViewModel> LogSources { get; } = [];
        public LogSourceViewModel? BuildLog { get; private set; }

        private LogSourceViewModel? _selectedLog;
        public LogSourceViewModel? SelectedLog
        {
            get => _selectedLog;
            set
            {
                if (Set(ref _selectedLog, value))
                    value?.Pull();
            }
        }

        private ServiceViewModel? _selectedService;
        public ServiceViewModel? SelectedService
        {
            get => _selectedService;
            set
            {
                if (Set(ref _selectedService, value) && value != null)
                    SelectedLog = value.Log;
            }
        }

        public Command StartAll { get; }
        public Command StopAll { get; }
        public Command RestartAll { get; }
        public Command CleanRebuild { get; }
        public Command EditEnv { get; }
        public Command OpenFolder { get; }
        public Command EditConfig { get; }
        public Command OpenLogs { get; }
        public Command Reload { get; }
        public Command ViewBuildLog { get; }
        public Command Rename { get; }

        public Command ClearLog { get; }
        public Command Remove { get; }
        public Command EditProject { get; }

        public bool IsManaged => Runner?.IsManaged == true;
        public string ConfigPath => Runner?.Project.ConfigPath ?? Entry.ConfigFile;
        public string ModeText => Runner == null ? "" : Runner.Project.Config.Globals.Mode switch
        {
            DotnetMode.Managed => "dotnet: managed builds",
            DotnetMode.Run => "dotnet: run",
            _ => "dotnet: watch",
        };

        public bool AutoStart
        {
            get => Entry.AutoStart;
            set
            {
                if (Entry.AutoStart == value)
                    return;
                Entry.AutoStart = value;
                Raise();
                _main.SaveSettings();
            }
        }

        private bool _busy;
        private string _busyText = "";
        public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }

        private bool _active;
        public bool IsActive { get => _active; private set => Set(ref _active, value); }

        private string _status = "";
        public string StatusText { get => _status; private set => Set(ref _status, value); }

        private string _memText = "";
        public string MemoryText { get => _memText; private set => Set(ref _memText, value); }

        private Brush _dot = Brushes.Gray;
        public Brush StatusBrush { get => _dot; private set => Set(ref _dot, value); }

        private string _buildText = "";
        public string BuildText { get => _buildText; private set => Set(ref _buildText, value); }

        public int RunningCount => Services.Count(s => s.State == ServiceState.Running);
        public bool HasProblem => Services.Any(s => s.State is ServiceState.Crashed or ServiceState.Failed);

        private void BuildServices()
        {
            var keep = SelectedService?.Key;
            Services.Clear();
            LogSources.Clear();
            if (Runner != null)
            {
                foreach (var s in Runner.Services)
                {
                    var vm = new ServiceViewModel(this, Runner, s);
                    Services.Add(vm);
                    LogSources.Add(vm.Log);
                }
                if (Runner.IsManaged && BuildLog != null)
                    LogSources.Add(BuildLog);
            }
            SelectedService = Services.FirstOrDefault(s => s.Key == keep) ?? Services.FirstOrDefault();
            if (SelectedLog == null || !LogSources.Contains(SelectedLog))
                SelectedLog = LogSources.FirstOrDefault();
            Raise(nameof(IsManaged));
            Raise(nameof(ModeText));
        }

        private void Attach(Project p)
        {
            var r = new ProjectRunner(p, "DevRunner Desktop");
            r.Notice += n => _main.OnNotice(this, n);
            r.ServicesChanged += () => Application.Current.Dispatcher.BeginInvoke(BuildServices);
            Runner = r;
            BuildLog = new LogSourceViewModel("Build", () => r.BuildLog, "#c084fc");
            Raise(nameof(Runner));
            Raise(nameof(ConfigPath));
        }

        private void Reattach()
        {
            Runner?.Dispose();
            Runner = null;
            var lp = AppSettings.LoadOne(Entry);
            Error = lp.Error;
            if (lp.Project is { } p)
            {
                Attach(p);
                Name = p.Name;
            }
            Raise(nameof(Name));
            Raise(nameof(Error));
            Raise(nameof(HasError));
            BuildServices();
        }

        public void Refresh(bool pullLogs)
        {
            if (Runner == null)
            {
                StatusText = Error ?? "";
                StatusBrush = Palette.Brush(Palette.Bad);
                return;
            }
            foreach (var s in Services)
                s.Refresh();
            if (pullLogs)
                SelectedLog?.Pull();

            var total = Services.Count(s => s.Enabled);
            var running = RunningCount;
            var busy = Services.Any(s => s.IsBusy) || Runner.IsBuilding;
            IsActive = Runner.SessionActive && (running > 0 || busy || Services.Any(s => s.IsActive));
            var mem = Runner.TotalWorkingSet;
            MemoryText = mem > 0 ? Format.Bytes(mem) : "";

            if (!Runner.SessionActive)
            {
                StatusText = $"{Services.Count} service{(Services.Count == 1 ? "" : "s")} · stopped";
                StatusBrush = Palette.Brush(Palette.Off);
            }
            else if (HasProblem)
            {
                StatusText = $"{running}/{total} running · problem";
                StatusBrush = Palette.Brush(Palette.Bad);
            }
            else if (busy)
            {
                StatusText = $"{running}/{total} running · starting";
                StatusBrush = Palette.Brush(Palette.Info);
            }
            else
            {
                StatusText = $"{running}/{total} running";
                StatusBrush = Palette.Brush(running > 0 ? Palette.Good : Palette.Off);
            }

            BuildText = Runner.IsManaged ? Runner.BuildState switch
            {
                BuildState.Building => "Building…",
                BuildState.Ok => "Build ✓",
                BuildState.Failed => $"Build failed ({Runner.LastBuildErrors.Count})",
                _ => "",
            } : "";
        }

        public void Dispose()
        {
            Runner?.Dispose();
        }
    }
}
