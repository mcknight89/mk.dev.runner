using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using mk.dev.runner.core;

namespace mk.dev.runner.desktop.ViewModels;

public sealed class ServiceViewModel : Observable
{
    internal static readonly Dictionary<string, string> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cyan"] = "#38bdf8", ["green"] = "#4ade80", ["magenta"] = "#e879f9", ["yellow"] = "#facc15",
        ["blue"] = "#60a5fa", ["red"] = "#f87171", ["white"] = "#e2e8f0", ["orange"] = "#fb923c",
        ["teal"] = "#2dd4bf", ["purple"] = "#a78bfa", ["pink"] = "#f472b6", ["lime"] = "#a3e635",
    };

    readonly ProjectRunner _runner;
    readonly ProjectViewModel _owner;

    public ServiceViewModel(ProjectViewModel owner, ProjectRunner runner, ServiceRunner service)
    {
        _owner = owner;
        _runner = runner;
        S = service;
        ColorHex = service.Config.Color is { } c && Palette.TryGetValue(c, out var hx) ? hx : "#a1a1aa";
        ColorBrush = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(ColorHex)));
        Log = new LogSourceViewModel(service.Name, () => service.Logs, ColorHex);

        StartStop = new Command(() =>
        {
            if (S.HasProcess || S.IsBusy) _runner.StopService(S.Index);
            else _owner.RunGuarded(() => _runner.StartService(S.Index));
        });
        Restart = new Command(() => _runner.RestartService(S.Index));
        OpenUrl = new Command(() => { if (_runner.UrlOf(S) is { } u) Open(u); }, () => _runner.UrlOf(S) != null);
        OpenLog = new Command(() => OpenInEditor(_runner.LogFileOf(S)));
        OpenFolder = new Command(() => Open(_runner.Project.ServiceDir(S.Config)));
        Refresh();
    }

    public ServiceRunner S { get; }
    public LogSourceViewModel Log { get; }
    public string Name => S.Name;
    public string Key => S.Key;
    public string TypeText => S.Config.Type.ToLowerInvariant();
    public string ColorHex { get; }
    public Brush ColorBrush { get; }

    public Command StartStop { get; }
    public Command Restart { get; }
    public Command OpenUrl { get; }
    public Command OpenLog { get; }
    public Command OpenFolder { get; }

    ServiceState _state;
    public ServiceState State { get => _state; private set => Set(ref _state, value); }

    string _stateText = "";
    public string StateText { get => _stateText; private set => Set(ref _stateText, value); }

    Brush _stateBrush = Brushes.Gray;
    public Brush StateBrush { get => _stateBrush; private set => Set(ref _stateBrush, value); }

    bool _isBusy;
    public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

    bool _isActive;
    /// <summary>Has a process or is on its way to one (the start/stop button shows Stop).</summary>
    public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }

    bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Raise();
            Raise(nameof(EnabledText));
            _runner.ToggleService(S.Index);
        }
    }

    public string EnabledText => _enabled ? "Enabled" : "Disabled";

    string _portText = "";
    public string PortText { get => _portText; private set => Set(ref _portText, value); }

    string _memText = "—";
    public string MemoryText { get => _memText; private set => Set(ref _memText, value); }

    string _cpuText = "";
    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }

    string _detail = "";
    public string DetailText { get => _detail; private set => Set(ref _detail, value); }

    string _procTip = "";
    public string ProcessTip { get => _procTip; private set => Set(ref _procTip, value); }

    PointCollection _spark = [];
    public PointCollection Sparkline { get => _spark; private set => Set(ref _spark, value); }

    string _peak = "";
    public string PeakText { get => _peak; private set => Set(ref _peak, value); }

    public long WorkingSet => S.Metrics.WorkingSet;

    public void Refresh()
    {
        State = S.State;
        IsBusy = S.IsBusy;
        IsActive = S.HasProcess || S.IsBusy;
        var en = _runner.IsEnabled(S);
        if (_enabled != en) { _enabled = en; Raise(nameof(Enabled)); Raise(nameof(EnabledText)); }
        (StateText, StateBrush) = S.State switch
        {
            ServiceState.Running => ("Running", B("#4ade80")),
            ServiceState.Starting => ("Starting…", B("#38bdf8")),
            ServiceState.Building => ("Building…", B("#c084fc")),
            ServiceState.Restarting => ("Restarting…", B("#facc15")),
            ServiceState.Retrying => ("Retrying…", B("#fb923c")),
            ServiceState.Crashed => ("Crashed", B("#f87171")),
            ServiceState.Failed => ("Build failed", B("#f87171")),
            ServiceState.Disabled => ("Disabled", B("#52525b")),
            _ => ("Stopped", B("#71717a")),
        };

        var port = _runner.PortOf(S);
        PortText = port is > 0 ? $":{port}" : "";

        var m = S.Metrics;
        if (S.HasProcess && m.WorkingSet > 0)
        {
            MemoryText = Format.Bytes(m.WorkingSet);
            CpuText = $"{m.CpuPercent:0.#}% CPU";
            var up = S.StartedAt is { } at ? Format.Duration(DateTime.Now - at) : "";
            DetailText = $"PID {S.Pid} · {m.ProcessCount} proc{(m.ProcessCount == 1 ? "" : "s")} · up {up}" +
                         (m.Ports.Count > 0 ? " · listening " + string.Join(" ", m.Ports.Select(p => ":" + p)) : "");
            ProcessTip = string.Join("\n", m.Processes.Select(p => $"{p.Name,-22} {p.Pid,7}   {Format.Bytes(p.WorkingSet),8}   private {Format.Bytes(p.PrivateBytes)}"));
        }
        else
        {
            MemoryText = "—";
            CpuText = "";
            DetailText = S.State == ServiceState.Disabled ? "Disabled on this machine: Start all skips it" : TypeText + " · " + S.Config.Dir;
            ProcessTip = "Not running";
        }
        BuildSparkline();
    }

    void BuildSparkline()
    {
        var h = S.MemoryHistory();
        const double W = 120, H = 28;
        if (h.Length < 2) { if (Sparkline.Count > 0) Sparkline = []; PeakText = ""; return; }
        long max = Math.Max(h.Max(), 1);
        long min = h.Min();
        double range = Math.Max(max - min, max * 0.1);
        double baseLine = Math.Max(0, max - range);
        var pts = new PointCollection(h.Length + 2);
        for (int i = 0; i < h.Length; i++)
        {
            double x = W * i / (h.Length - 1);
            double y = H - (h[i] - baseLine) / range * (H - 2) - 1;
            pts.Add(new Point(x, Math.Clamp(y, 0, H)));
        }
        pts.Freeze();
        Sparkline = pts;
        PeakText = $"peak {Format.Bytes(max)}";
    }

    static readonly Dictionary<string, SolidColorBrush> BrushCache = [];
    static SolidColorBrush B(string hex)
    {
        if (!BrushCache.TryGetValue(hex, out var b))
            BrushCache[hex] = b = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));
        return b;
    }

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    static readonly Lazy<string?> VsCode = new(() =>
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("where", "code.cmd")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true });
            var first = p?.StandardOutput.ReadLine();
            p?.WaitForExit(3000);
            return string.IsNullOrWhiteSpace(first) ? null : first.Trim();
        }
        catch { return null; }
    });

    /// <summary>Opens a file in VS Code when it's installed (like dev.mjs's O key), else the default app.</summary>
    internal static void OpenInEditor(string file)
    {
        if (VsCode.Value is { } code)
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"\"{code}\" \"{file}\"\"") { UseShellExecute = false, CreateNoWindow = true });
                return;
            }
            catch { }
        }
        Open(file);
    }

    internal static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(e.Message, "Dev Runner"); }
    }
}
