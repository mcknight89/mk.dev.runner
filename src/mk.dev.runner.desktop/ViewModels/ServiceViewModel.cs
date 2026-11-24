using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using mk.dev.runner.core;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed class ServiceViewModel : Observable
    {
        private readonly ProjectRunner _runner;
        private readonly ProjectViewModel _owner;

        public ServiceViewModel(ProjectViewModel owner, ProjectRunner runner, ServiceRunner service)
        {
            _owner = owner;
            _runner = runner;
            S = service;
            ColorHex = Palette.ServiceHex(service.Config.Color);
            ColorBrush = Palette.Brush(ColorHex);
            Log = new LogSourceViewModel(service.Name, () => service.Logs, ColorHex);

            StartStop = new Command(() =>
            {
                if (S.HasProcess || S.IsBusy)
                    _runner.StopService(S.Index);
                else
                    _owner.RunBackground(() => _runner.StartService(S.Index), "Starting…");
            });
            Restart = new Command(() => _runner.RestartService(S.Index), () => S.HasProcess || S.IsBusy);
            OpenUrl = new Command(() => { if (_runner.UrlOf(S) is { } u) Shell.Open(u); }, () => _runner.UrlOf(S) != null);
            OpenLog = new Command(() => new Views.LogFileWindow($"{S.Name} log", _runner.LogFileOf(S)).Show());
            OpenFolder = new Command(() => Shell.Open(_runner.Project.ServiceDir(S.Config)));
            Edit = new Command(() => _owner.EditService(S.Key));
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
        public Command Edit { get; }

        private ServiceState _state;
        public ServiceState State { get => _state; private set => Set(ref _state, value); }

        private string _stateText = "";
        public string StateText { get => _stateText; private set => Set(ref _stateText, value); }

        private Brush _stateBrush = Brushes.Gray;
        public Brush StateBrush { get => _stateBrush; private set => Set(ref _stateBrush, value); }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

        private bool _isActive;
        public bool IsActive { get => _isActive; private set => Set(ref _isActive, value); }

        private bool _enabled;
        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;
                _enabled = value;
                Raise();
                Raise(nameof(EnabledText));
                _runner.ToggleService(S.Index);
            }
        }

        public string EnabledText => _enabled ? "Enabled" : "Disabled";

        private string _portText = "";
        public string PortText { get => _portText; private set => Set(ref _portText, value); }

        private string _memText = "—";
        public string MemoryText { get => _memText; private set => Set(ref _memText, value); }

        private string _cpuText = "";
        public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }

        private string _detail = "";
        public string DetailText { get => _detail; private set => Set(ref _detail, value); }

        private string _procTip = "";
        public string ProcessTip { get => _procTip; private set => Set(ref _procTip, value); }

        private PointCollection _spark = [];
        public PointCollection Sparkline { get => _spark; private set => Set(ref _spark, value); }

        private string _peak = "";
        public string PeakText { get => _peak; private set => Set(ref _peak, value); }


        public void Refresh()
        {
            if (State != S.State)
                CommandManager.InvalidateRequerySuggested();
            State = S.State;
            IsBusy = S.IsBusy;
            IsActive = S.HasProcess || S.IsBusy;
            var en = _runner.IsEnabled(S);
            if (_enabled != en)
            {
                _enabled = en;
                Raise(nameof(Enabled));
                Raise(nameof(EnabledText));
            }
            (StateText, StateBrush) = S.State switch
            {
                ServiceState.Running when S.NotResponding => ($"Not responding · {S.LastProblem}", Palette.Brush(Palette.Warn)),
                ServiceState.Running => ("Running", Palette.Brush(Palette.Good)),
                ServiceState.Starting => ("Starting…", Palette.Brush(Palette.Info)),
                ServiceState.Building => ("Building…", Palette.Brush(Palette.Building)),
                ServiceState.Restarting => ("Restarting…", Palette.Brush(Palette.Warn)),
                ServiceState.Retrying => ("Retrying…", Palette.Brush(Palette.Retry)),
                ServiceState.Crashed => ("Crashed", Palette.Brush(Palette.Bad)),
                ServiceState.Failed => ("Build failed", Palette.Brush(Palette.Bad)),
                ServiceState.Disabled => ("Disabled", Palette.Brush(Palette.Off)),
                _ => ("Stopped", Palette.Brush(Palette.Faint)),
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
                MemoryText = "";
                CpuText = "";
                DetailText = S.State == ServiceState.Disabled ? "Disabled on this machine: Start all skips it"
                    : S.State is ServiceState.Crashed or ServiceState.Failed && S.LastProblem != null ? S.LastProblem
                    : TypeText + " · " + S.Config.Dir;
                ProcessTip = "Not running";
            }
            BuildSparkline();
        }

        private int _historyVersion = -1;

        private void BuildSparkline()
        {
            if (S.HistoryVersion == _historyVersion)
                return;
            _historyVersion = S.HistoryVersion;
            var h = S.MemoryHistory();
            const double W = 120, H = 28;
            if (h.Length < 2)
            {
                if (Sparkline.Count > 0)
                    Sparkline = [];
                PeakText = "";
                return;
            }
            var max = Math.Max(h.Max(), 1);
            var min = h.Min();
            var range = Math.Max(max - min, max * 0.1);
            var baseLine = Math.Max(0, max - range);
            var pts = new PointCollection(h.Length + 2);
            for (int i = 0; i < h.Length; i++)
            {
                var x = W * i / (h.Length - 1);
                var y = H - (h[i] - baseLine) / range * (H - 2) - 1;
                pts.Add(new Point(x, Math.Clamp(y, 0, H)));
            }
            pts.Freeze();
            Sparkline = pts;
            PeakText = $"peak {Format.Bytes(max)}";
        }
    }
}
