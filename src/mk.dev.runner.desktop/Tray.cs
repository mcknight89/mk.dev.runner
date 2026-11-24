using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using mk.dev.runner.desktop.ViewModels;
using Application = System.Windows.Application;

namespace mk.dev.runner.desktop;

/// <summary>The notification-area icon: status colour, summary tooltip, per-project start/stop menu, crash popups.</summary>
internal sealed class Tray : IDisposable
{
    readonly NotifyIcon _icon;
    readonly MainViewModel _vm;
    readonly Action _show;
    readonly Action _exit;
    readonly Icon _idle, _running, _problem;
    string _state = "";

    public Tray(MainViewModel vm, Action show, Action exit)
    {
        _vm = vm;
        _show = show;
        _exit = exit;
        _idle = Icons.Make(Color.FromArgb(0x71, 0x71, 0x7a));
        _running = Icons.Make(Color.FromArgb(0x4a, 0xde, 0x80));
        _problem = Icons.Make(Color.FromArgb(0xf8, 0x71, 0x71));

        _icon = new NotifyIcon { Icon = _idle, Text = "Dev Runner", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _icon.ContextMenuStrip.Opening += (_, e) => { BuildMenu(); e.Cancel = false; };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _show(); };
        _icon.BalloonTipClicked += (_, _) => _show();

        vm.SummaryChanged += OnSummary;
        vm.NoticeRaised += (title, text, isError) =>
            _icon.ShowBalloonTip(5000, title, text, isError ? ToolTipIcon.Error : ToolTipIcon.Info);
    }

    void OnSummary(string text, bool running, bool problem)
    {
        _icon.Text = text.Length > 127 ? text[..124] + "…" : text;
        var state = problem ? "problem" : running ? "running" : "idle";
        if (state == _state) return;
        _state = state;
        _icon.Icon = problem ? _problem : running ? _running : _idle;
    }

    void BuildMenu()
    {
        var m = _icon.ContextMenuStrip!;
        m.Items.Clear();
        m.Items.Add(new ToolStripMenuItem("Open Dev Runner", null, (_, _) => _show()) { Font = new Font(m.Font, System.Drawing.FontStyle.Bold) });
        if (_vm.Updates.HasUpdate)
            m.Items.Add(new ToolStripMenuItem($"Update to {_vm.Updates.Available}…", null, (_, _) => { _show(); _vm.InstallUpdate.Execute(null); }));
        m.Items.Add(new ToolStripSeparator());
        if (_vm.Projects.Count == 0) m.Items.Add(new ToolStripMenuItem("No projects yet") { Enabled = false });
        foreach (var p in _vm.Projects)
        {
            var item = new ToolStripMenuItem($"{p.Name}   {p.StatusText}{(p.MemoryText != "" ? " · " + p.MemoryText : "")}");
            if (p.Runner == null) { item.Enabled = false; m.Items.Add(item); continue; }
            item.DropDownItems.Add("Start all", null, (_, _) => p.StartAll.Execute(null));
            var stop = item.DropDownItems.Add("Stop all", null, (_, _) => p.StopAll.Execute(null));
            stop.Enabled = p.Runner.SessionActive;
            if (p.Runner.IsManaged) item.DropDownItems.Add("Clean rebuild", null, (_, _) => p.CleanRebuild.Execute(null));
            item.DropDownItems.Add(new ToolStripSeparator());
            foreach (var s in p.Services)
            {
                var si = new ToolStripMenuItem($"{s.Name}   {s.StateText}{(s.MemoryText != "—" ? " · " + s.MemoryText : "")}");
                si.DropDownItems.Add(s.IsActive ? "Stop" : "Start", null, (_, _) => s.StartStop.Execute(null));
                si.DropDownItems.Add("Restart", null, (_, _) => s.Restart.Execute(null)).Enabled = p.Runner.SessionActive && s.Enabled;
                if (s.OpenUrl.CanExecute(null)) si.DropDownItems.Add("Open in browser", null, (_, _) => s.OpenUrl.Execute(null));
                item.DropDownItems.Add(si);
            }
            item.DropDownItems.Add(new ToolStripSeparator());
            item.DropDownItems.Add("Environment…", null, (_, _) => { _show(); _vm.Selected = p; p.EditEnv.Execute(null); });
            m.Items.Add(item);
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Exit (stops everything)", null, (_, _) => _exit());
    }

    /// <summary>A plain information popup from the tray icon.</summary>
    public void Tip(string title, string text) => _icon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _idle.Dispose(); _running.Dispose(); _problem.Dispose();
    }
}

internal static class Icons
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr h);

    /// <summary>A rounded dark tile with a play arrow; the dot shows status.</summary>
    public static Icon Make(Color status, int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size;
            using var tile = RoundRect(new RectangleF(1, 1, s - 2, s - 2), s * 0.22f);
            using (var bg = new SolidBrush(Color.FromArgb(0x1e, 0x3a, 0x5f))) g.FillPath(bg, tile);
            using (var edge = new Pen(Color.FromArgb(0x60, 0xa5, 0xfa), s / 16)) g.DrawPath(edge, tile);
            var tri = new[] { new PointF(s * 0.32f, s * 0.24f), new PointF(s * 0.32f, s * 0.76f), new PointF(s * 0.74f, s * 0.5f) };
            using (var fg = new SolidBrush(Color.FromArgb(0xe4, 0xe4, 0xe7))) g.FillPolygon(fg, tri);
            float d = s * 0.36f;
            using (var ring = new SolidBrush(Color.FromArgb(0x18, 0x18, 0x1b))) g.FillEllipse(ring, s - d - 0.5f, s - d - 0.5f, d, d);
            using (var dot = new SolidBrush(status)) g.FillEllipse(dot, s - d + s * 0.05f, s - d + s * 0.05f, d - s * 0.12f, d - s * 0.12f);
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }

    static GraphicsPath RoundRect(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static System.Windows.Media.ImageSource ToImageSource(Icon icon) =>
        System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty,
            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
}

internal static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Dev Runner";
    const string OldValueName = "mk.dev.runner";

    public static void Apply(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;
        if (key.GetValue(OldValueName) != null) key.DeleteValue(OldValueName);
        if (enabled) key.SetValue(ValueName, Command);
        else if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName);
    }

    static string Command => $"\"{Environment.ProcessPath}\" --tray";

    /// <summary>
    /// Called on every start: when Start with Windows is on, points the entry at this exe, so moving or
    /// rebuilding the app folder fixes itself the next time it's opened.
    /// </summary>
    public static void KeepPathCurrent(bool enabled)
    {
        if (!enabled) return;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key != null && key.GetValue(ValueName) as string != Command) Apply(true);
        }
        catch { }
    }
}
