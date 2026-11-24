using System.Drawing;
using System.Windows.Forms;
using mk.dev.runner.desktop.ViewModels;

namespace mk.dev.runner.desktop
{
    internal sealed class Tray : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly MainViewModel _vm;
        private readonly Action _show;
        private readonly Action _exit;
        private readonly Icon _idle, _running, _problem;
        private string _state = "";

        public Tray(MainViewModel vm, Action show, Action exit)
        {
            _vm = vm;
            _show = show;
            _exit = exit;
            _idle = Icons.Make(Color.FromArgb(0x71, 0x71, 0x7a));
            _running = Icons.Make(Color.FromArgb(0x4a, 0xde, 0x80));
            _problem = Icons.Make(Color.FromArgb(0xf8, 0x71, 0x71));

            _icon = new NotifyIcon { Icon = _idle, Text = App.AppName, Visible = true, ContextMenuStrip = new ContextMenuStrip { Renderer = new TrayMenuRenderer() } };
            _icon.ContextMenuStrip.Opening += (_, e) => BuildMenu();
            _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _show(); };
            _icon.BalloonTipClicked += (_, _) =>
            {
                _show();
                vm.ShowLastNotice();
            };

            vm.SummaryChanged += OnSummary;
            vm.NoticeRaised += (title, text) => _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.Error);
        }

        private void OnSummary(string text, bool running, bool problem)
        {
            _icon.Text = text.Length > 127 ? text[..124] + "…" : text;
            var state = problem ? "problem" : running ? "running" : "idle";
            if (state == _state)
                return;
            _state = state;
            _icon.Icon = problem ? _problem : running ? _running : _idle;
        }

        private void BuildMenu()
        {
            var m = _icon.ContextMenuStrip!;
            m.Items.Clear();
            m.Items.Add(new ToolStripMenuItem("Open Dev Runner", null, (_, _) => _show()) { Font = new Font(m.Font, System.Drawing.FontStyle.Bold) });
            if (_vm.Updates.HasUpdate)
                m.Items.Add(new ToolStripMenuItem($"Update to {_vm.Updates.Available}…", null, (_, _) => { _show(); _vm.InstallUpdate.Execute(null); }));
            m.Items.Add(new ToolStripSeparator());
            if (_vm.Projects.Count == 0)
                m.Items.Add(new ToolStripMenuItem("No projects yet") { Enabled = false });
            foreach (var p in _vm.Projects)
            {
                var item = new ToolStripMenuItem($"{p.Name}   {p.StatusText}{(p.MemoryText != "" ? " · " + p.MemoryText : "")}");
                if (p.Runner == null)
                {
                    item.Enabled = false;
                    m.Items.Add(item);
                    continue;
                }
                item.DropDownItems.Add("Start all", null, (_, _) => p.StartAll.Execute(null));
                var stop = item.DropDownItems.Add("Stop all", null, (_, _) => p.StopAll.Execute(null));
                stop.Enabled = p.Runner.SessionActive;
                if (p.Runner.IsManaged)
                    item.DropDownItems.Add("Clean rebuild", null, (_, _) => p.CleanRebuild.Execute(null));
                item.DropDownItems.Add(new ToolStripSeparator());
                foreach (var s in p.Services)
                {
                    var si = new ToolStripMenuItem($"{s.Name}   {s.StateText}{(s.MemoryText != "—" ? " · " + s.MemoryText : "")}");
                    si.DropDownItems.Add(s.IsActive ? "Stop" : "Start", null, (_, _) => s.StartStop.Execute(null));
                    si.DropDownItems.Add("Restart", null, (_, _) => s.Restart.Execute(null)).Enabled = s.Restart.CanExecute(null);
                    if (s.OpenUrl.CanExecute(null))
                        si.DropDownItems.Add("Open in browser", null, (_, _) => s.OpenUrl.Execute(null));
                    item.DropDownItems.Add(si);
                }
                item.DropDownItems.Add(new ToolStripSeparator());
                item.DropDownItems.Add("Environment…", null, (_, _) => { _show(); _vm.Selected = p; p.EditEnv.Execute(null); });
                m.Items.Add(item);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Report a bug…", null, (_, _) => BugReport.Open(_vm.Updates.CurrentVersion));
            m.Items.Add("Exit (stops everything)", null, (_, _) => _exit());
        }

        public void Tip(string title, string text)
        {
            _icon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            _idle.Dispose();
            _running.Dispose();
            _problem.Dispose();
        }
    }
}
