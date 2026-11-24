using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using mk.dev.runner.core;

namespace mk.dev.runner.desktop.ViewModels;

public sealed class ProjectViewModel : Observable, IDisposable
{
    readonly MainViewModel _main;

    public ProjectViewModel(MainViewModel main, LoadedProject loaded)
    {
        _main = main;
        Entry = loaded.Entry;
        Error = loaded.Error;
        Name = loaded.Project?.Name ?? Entry.DisplayName;

        if (loaded.Project is { } p) Attach(p);
        BuildServices();

        StartAll = new Command(() => RunGuarded(() => Runner!.Start()), () => Runner != null && !_busy);
        StopAll = new Command(() => RunBackground(() => Runner!.StopAll(), "Stopping…"), () => Runner?.SessionActive == true && !_busy);
        RestartAll = new Command(() => Runner!.RestartAll(), () => Runner?.SessionActive == true);
        CleanRebuild = new Command(() => Runner!.CleanRebuild(), () => Runner?.IsManaged == true);
        EditEnv = new Command(() => _main.OpenEnvEditor(this), () => Runner != null);
        OpenFolder = new Command(() => ServiceViewModel.Open(Runner?.Project.RootDir ?? Entry.Folder ?? Path.GetDirectoryName(Entry.ConfigFile)!));
        EditConfig = new Command(() => ServiceViewModel.OpenInEditor(Entry.ConfigFile));
        OpenLogs = new Command(() =>
        {
            if (Runner == null) return;
            Directory.CreateDirectory(Runner.Project.LogsDir);
            ServiceViewModel.Open(Runner.Project.LogsDir);
        });
        ViewBuildLog = new Command(() => { if (Runner is { } r) ServiceViewModel.OpenInEditor(r.BuildLogFile); },
                                   () => Runner?.IsManaged == true);
        Rename = new Command(DoRename);
        EditProject = new Command(DoEditProject, () => Runner != null);
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

    LogSourceViewModel? _selectedLog;
    public LogSourceViewModel? SelectedLog
    {
        get => _selectedLog;
        set { if (Set(ref _selectedLog, value)) value?.Pull(); }
    }

    ServiceViewModel? _selectedService;
    public ServiceViewModel? SelectedService
    {
        get => _selectedService;
        set { if (Set(ref _selectedService, value) && value != null) SelectedLog = value.Log; }
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

    /// <summary>An old-layout project (a dev.&lt;name&gt;.config.json): offer to move it into its folder.</summary>
    public Command ClearLog { get; }
    public Command Remove { get; }
    public Command EditProject { get; }

    public bool IsManaged => Runner?.IsManaged == true;
    public string ConfigPath => Runner?.Project.ConfigPath ?? Entry.ConfigFile;
    public string ModeText => Runner == null ? "" : Runner.Project.Config.Globals.DotnetMode switch
    {
        "managed" => "dotnet: managed builds",
        "run" => "dotnet: run",
        _ => "dotnet: watch",
    };

    public bool AutoStart
    {
        get => Entry.AutoStart;
        set { if (Entry.AutoStart == value) return; Entry.AutoStart = value; Raise(); _main.SaveSettings(); }
    }

    bool _busy;
    string _busyText = "";
    public string BusyText { get => _busyText; private set => Set(ref _busyText, value); }

    bool _active;
    public bool IsActive { get => _active; private set => Set(ref _active, value); }

    string _status = "";
    public string StatusText { get => _status; private set => Set(ref _status, value); }

    string _memText = "";
    public string MemoryText { get => _memText; private set => Set(ref _memText, value); }

    Brush _dot = Brushes.Gray;
    public Brush StatusBrush { get => _dot; private set => Set(ref _dot, value); }

    string _buildText = "";
    public string BuildText { get => _buildText; private set => Set(ref _buildText, value); }

    public int RunningCount => Services.Count(s => s.State == ServiceState.Running);
    public bool HasProblem => Services.Any(s => s.State is ServiceState.Crashed or ServiceState.Failed);

    void BuildServices()
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
            if (Runner.IsManaged && BuildLog != null) LogSources.Add(BuildLog);
        }
        SelectedService = Services.FirstOrDefault(s => s.Key == keep) ?? Services.FirstOrDefault();
        if (SelectedLog == null || !LogSources.Contains(SelectedLog)) SelectedLog = LogSources.FirstOrDefault();
        Raise(nameof(IsManaged));
        Raise(nameof(ModeText));
    }

    /// <summary>Runs this project (a new runner, after loading or after the config moved).</summary>
    void Attach(Project p)
    {
        var r = new ProjectRunner(p, "DevRunner Desktop");
        r.Notice += n => _main.OnNotice(this, n);
        r.ServicesChanged += () => Application.Current.Dispatcher.BeginInvoke(BuildServices);
        Runner = r;
        BuildLog = new LogSourceViewModel("Build", () => r.BuildLog, "#c084fc");
        Raise(nameof(Runner)); Raise(nameof(ConfigPath));
    }

    /// <summary>Loads the project from its entry again (a retry after an error, or after the config moved).</summary>
    void Reattach()
    {
        Runner?.Dispose();
        Runner = null;
        var lp = AppSettings.LoadOne(Entry);
        Error = lp.Error;
        if (lp.Project is { } p) { Attach(p); Name = p.Name; }
        Raise(nameof(Name)); Raise(nameof(Error)); Raise(nameof(HasError));
        BuildServices();
    }

    /// <summary>The app's editor for this project's settings and services (the config file). Only while it's stopped.</summary>
    public void DoEditProject()
    {
        if (Runner == null) return;
        if (Runner.SessionActive)
        {
            MessageBox.Show(Application.Current.MainWindow, $"Stop {Name} first (Stop all), then edit it.", "Edit project",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var w = new Views.ProjectEditorWindow(this) { Owner = Application.Current.MainWindow };
        w.ShowDialog();
        if (!w.Saved) return;
        try { Runner.ReloadProject(); }
        catch (Exception e) { MessageBox.Show(e.Message, "Couldn't reload the project"); }
        BuildServices();
    }

    /// <summary>Changes the name in the list. False when another project already has it.</summary>
    public bool TryRename(string name)
    {
        if (_main.Projects.Any(p => p != this && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))) return false;
        Entry.Name = name;
        Name = name;
        Runner?.Project.SetDisplayName(name);
        Raise(nameof(Name));
        _main.SaveSettings();
        return true;
    }

    void DoRename()
    {
        var name = Views.TextPromptWindow.Ask(Application.Current.MainWindow, "Rename project",
            "What the runner calls it. Only the list's name changes: nothing in the project's folder does.", Name);
        if (name == null || name == Name) return;
        if (_main.Projects.Any(p => p != this && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show($"There's already a project called '{name}'.", "Rename project");
            return;
        }
        Entry.Name = name;
        Name = name;
        Runner?.Project.SetDisplayName(name);
        Raise(nameof(Name));
        _main.SaveSettings();
    }

    void ReloadConfig()
    {
        if (Runner == null) { Reattach(); return; }   // retry a project that failed to load
        try
        {
            if (!Runner.ReloadProject()) MessageBox.Show("Stop the project before reloading its config.", "Dev Runner");
        }
        catch (Exception e) { MessageBox.Show(e.Message, "Couldn't reload config"); }
    }

    /// <summary>Runs a start-type action off the UI thread and shows lock/other errors.</summary>
    public void RunGuarded(Action action) => RunBackground(action, "Starting…");

    void RunBackground(Action action, string text)
    {
        _busy = true;
        BusyText = text;
        CommandManagerInvalidate();
        Task.Run(() =>
        {
            try { action(); }
            catch (Exception e)
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                    MessageBox.Show(Application.Current.MainWindow, e.Message, $"{Name} — couldn't start", MessageBoxButton.OK, MessageBoxImage.Warning));
            }
            finally
            {
                Application.Current.Dispatcher.BeginInvoke(() => { _busy = false; BusyText = ""; CommandManagerInvalidate(); });
            }
        });
    }

    static void CommandManagerInvalidate() => System.Windows.Input.CommandManager.InvalidateRequerySuggested();

    /// <summary>Called by the UI timer.</summary>
    public void Refresh(bool pullLogs)
    {
        if (Runner == null)
        {
            StatusText = Error ?? "";
            StatusBrush = Brushes.IndianRed;
            return;
        }
        foreach (var s in Services) s.Refresh();
        if (pullLogs) SelectedLog?.Pull();

        int total = Services.Count(s => s.Enabled);
        int running = RunningCount;
        bool busy = Services.Any(s => s.IsBusy) || Runner.IsBuilding;
        IsActive = Runner.SessionActive && (running > 0 || busy || Services.Any(s => s.IsActive));
        long mem = Runner.TotalWorkingSet;
        MemoryText = mem > 0 ? Format.Bytes(mem) : "";

        if (!Runner.SessionActive) { StatusText = $"{Services.Count} service{(Services.Count == 1 ? "" : "s")} · stopped"; StatusBrush = Brush("#52525b"); }
        else if (HasProblem) { StatusText = $"{running}/{total} running · problem"; StatusBrush = Brush("#f87171"); }
        else if (busy) { StatusText = $"{running}/{total} running · starting"; StatusBrush = Brush("#38bdf8"); }
        else { StatusText = $"{running}/{total} running"; StatusBrush = Brush(running > 0 ? "#4ade80" : "#52525b"); }

        BuildText = Runner.IsManaged ? Runner.BuildState switch
        {
            BuildState.Building => "Building…",
            BuildState.Ok => "Build ✓",
            BuildState.Failed => $"Build failed ({Runner.LastBuildErrors.Count})",
            _ => "",
        } : "";
    }

    static readonly Dictionary<string, SolidColorBrush> Cache = [];
    static SolidColorBrush Brush(string hex)
    {
        if (!Cache.TryGetValue(hex, out var b))
        {
            b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            Cache[hex] = b;
        }
        return b;
    }

    public void Dispose() => Runner?.Dispose();
}
