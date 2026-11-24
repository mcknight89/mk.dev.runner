using System.Diagnostics;
using System.Text;
using mk.dev.runner.core;
using mk.dev.runner.core.Logs;

namespace mk.dev.runner.cli;

/// <summary>The full-screen terminal dashboard — dev.mjs's layout plus memory/CPU per service.</summary>
internal sealed class Dashboard
{
    const int SW = 34;                 // sidebar width (incl. divider)
    const string R = "\x1b[0m";
    const string E = "\x1b";
    static readonly string[] Spinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    static readonly Dictionary<string, string> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cyan"] = "#38bdf8", ["green"] = "#4ade80", ["magenta"] = "#e879f9", ["yellow"] = "#facc15",
        ["blue"] = "#60a5fa", ["red"] = "#f87171", ["white"] = "#e2e8f0", ["orange"] = "#fb923c",
        ["teal"] = "#2dd4bf", ["purple"] = "#a78bfa", ["pink"] = "#f472b6", ["lime"] = "#a3e635",
    };

    static readonly string[] Help =
    [
        "Keys",
        "↑↓   select",
        "1-9  jump",
        "Spc  toggle on/off",
        "S    start / stop",
        "Ent  restart",
        "A    start all",
        "U    open in browser",
        "O    open log file",
        "[ ]  scroll line",
        "{ }  scroll ½ page",
        "Hom  top   End bottom",
        "B    build log",
        "R    clean rebuild",
        "^C   quit",
        "H    hide help",
    ];

    readonly ProjectRunner _runner;
    readonly Stream _out = Console.OpenStandardOutput();
    readonly object _renderGate = new();
    volatile bool _dirty = true;
    volatile bool _quit;
    int _selected;
    int _scroll;          // lines up from the bottom
    bool _viewBuild;
    bool _helpVisible;
    int _spin;
    int _cols, _rows;

    public Dashboard(ProjectRunner runner)
    {
        _runner = runner;
        _runner.Changed += () => _dirty = true;
        _runner.MetricsUpdated += () => _dirty = true;
        _runner.ServicesChanged += () => { _selected = 0; Hook(); _dirty = true; };
        _runner.BuildLog.LineAdded += _ => { if (_scroll == 0 && ShowingBuild) _dirty = true; };
        Hook();
    }

    void Hook()
    {
        foreach (var s in _runner.Services)
        {
            var idx = s.Index;
            s.Logs.LineAdded += _ => { if (idx == _selected && _scroll == 0 && !ShowingBuild) _dirty = true; };
        }
    }

    IReadOnlyList<ServiceRunner> Svcs => _runner.Services;
    ServiceRunner? Sel => _selected < Svcs.Count ? Svcs[_selected] : null;
    bool ShowingBuild => _viewBuild || Sel?.State == ServiceState.Building;
    LogBuffer VisibleLog => ShowingBuild || Sel == null ? _runner.BuildLog : Sel.Logs;

    public int Run()
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (!ConsoleNative.EnterRaw())
        {
            Console.Error.WriteLine("This needs an interactive Windows console (Windows Terminal, PowerShell or cmd).");
            return 1;
        }
        Write($"{E}[?1049h{E}[?25l{E}[?1000h{E}[?1006h{E}[2J{E}[H");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreTerminal();

        try { _runner.Start(); }
        catch (Exception ex)
        {
            RestoreTerminal();
            Console.Error.WriteLine($"\n{ex.Message}\n");
            return 1;
        }

        var input = new Thread(InputLoop) { IsBackground = true, Name = "input" };
        input.Start();

        var spinAt = Environment.TickCount64;
        while (!_quit)
        {
            Thread.Sleep(33);
            int c = SafeWidth(), r = SafeHeight();
            if (c != _cols || r != _rows) { _cols = c; _rows = r; _dirty = true; }
            if (Environment.TickCount64 - spinAt >= 120)
            {
                spinAt = Environment.TickCount64;
                _spin++;
                if (_runner.IsBuilding || Svcs.Any(s => s.IsBusy)) _dirty = true;
            }
            if (_dirty) { _dirty = false; Render(); }
        }

        Shutdown();
        return 0;
    }

    static int SafeWidth() { try { return Math.Max(SW + 20, Console.WindowWidth); } catch { return 160; } }
    static int SafeHeight() { try { return Math.Max(10, Console.WindowHeight); } catch { return 40; } }

    void Shutdown()
    {
        lock (_renderGate)
        {
            ConsoleNative.Restore();
            _restored = true;
            Write($"{E}[?1000l{E}[?1006l{E}[?1049l{E}[?25h{E}[2J{E}[H Stopping all dev servers…\r\n\r\n");
            foreach (var s in Svcs) Write($"  {E}[33m…{R}  {s.Name}\r\n");
            _runner.StopAll();
            Write($"\r\n  {E}[32mAll stopped.{R}\r\n");
        }
    }

    bool _restored;
    void RestoreTerminal()
    {
        if (_restored) return;
        _restored = true;
        try { Write($"{E}[?1000l{E}[?1006l{E}[?1049l{E}[?25h"); } catch { }
        ConsoleNative.Restore();
    }

    void Write(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        _out.Write(bytes, 0, bytes.Length);
        _out.Flush();
    }

    // ── input ───────────────────────────────────────────────────────────────

    void InputLoop()
    {
        while (!_quit)
        {
            var data = ConsoleNative.Read();
            if (data.Length == 0) { Thread.Sleep(20); continue; }
            lock (_renderGate) Handle(data);
            _dirty = true;
        }
    }

    void Handle(string data)
    {
        for (int i = 0; i < data.Length;)
        {
            char ch = data[i];
            string rest = data[i..];
            if (rest.StartsWith("\x1b[<"))
            {
                int end = rest.IndexOfAny(['M', 'm']);
                if (end > 0)
                {
                    var parts = rest[3..end].Split(';');
                    if (rest[end] == 'M' && int.TryParse(parts[0], out var btn))
                    {
                        if (btn == 64) Scroll(-3);
                        else if (btn == 65) Scroll(3);
                    }
                    i += end + 1;
                    continue;
                }
            }
            if (ch == '\x1b')
            {
                (string seq, string key)[] keys =
                [
                    ("\x1b[A", "up"), ("\x1bOA", "up"), ("\x1b[B", "down"), ("\x1bOB", "down"),
                    ("\x1b[5~", "pageup"), ("\x1b[6~", "pagedown"),
                    ("\x1b[H", "home"), ("\x1b[1~", "home"), ("\x1bOH", "home"),
                    ("\x1b[F", "end"), ("\x1b[4~", "end"), ("\x1bOF", "end"),
                    ("\x1b[C", "right"), ("\x1b[D", "left"), ("\x1bOC", "right"), ("\x1bOD", "left"),
                ];
                var hit = keys.FirstOrDefault(k => rest.StartsWith(k.seq));
                if (hit.seq != null) { Key(hit.key); i += hit.seq.Length; continue; }
                if (rest.StartsWith("\x1b["))
                {
                    int j = 2;
                    while (j < rest.Length && rest[j] is >= '0' and <= '?') j++;
                    i += Math.Min(rest.Length, j + 1);
                    continue;
                }
                i++;
                continue;
            }
            if (ch == '\x03') { _quit = true; return; }
            if (ch is '\r' or '\n') { Key("return"); i++; continue; }
            if (ch == ' ') { Key("space"); i++; continue; }
            Char(ch);
            i++;
        }
    }

    void Key(string key)
    {
        int n = Math.Max(1, Svcs.Count);
        switch (key)
        {
            case "up": Select((_selected - 1 + n) % n); break;
            case "down": Select((_selected + 1) % n); break;
            case "pageup": Scroll(-HalfPage); break;
            case "pagedown": Scroll(HalfPage); break;
            case "home": _scroll = int.MaxValue; break;
            case "end": _scroll = 0; break;
            case "return": if (Sel != null) _runner.RestartService(_selected); break;
            case "space": if (Sel != null) _runner.ToggleService(_selected); break;
        }
    }

    int HalfPage => Math.Max(1, _rows / 2);

    void Char(char ch)
    {
        switch (char.ToLowerInvariant(ch))
        {
            case >= '1' and <= '9': { int i = ch - '1'; if (i < Svcs.Count) Select(i); break; }
            case 'h': _helpVisible = !_helpVisible; break;
            case 'b': _viewBuild = !_viewBuild; _scroll = 0; break;
            case 'r': if (_runner.IsManaged) _runner.CleanRebuild(); else _runner.RestartAll(); break;
            case 'a': ThreadPool.QueueUserWorkItem(_ => { try { _runner.Start(); } catch { } }); break;
            case 's':
                if (Sel is { } s)
                {
                    if (s.HasProcess || s.IsBusy) _runner.StopService(s.Index);
                    else { var idx = s.Index; ThreadPool.QueueUserWorkItem(_ => { try { _runner.StartService(idx); } catch { } }); }
                }
                break;
            case 'u':
                if (Sel != null && _runner.UrlOf(Sel) is { } url) Open(url);
                break;
            case 'o':
                var target = ShowingBuild || Sel == null ? _runner.BuildLogFile : _runner.LogFileOf(Sel);
                if (target != null) try { Process.Start(new ProcessStartInfo("cmd", $"/c code \"{target}\"") { CreateNoWindow = true, UseShellExecute = false }); } catch { }
                break;
            case '[': Scroll(-1); break;
            case ']': Scroll(1); break;
            case '{': Scroll(-HalfPage); break;
            case '}': Scroll(HalfPage); break;
        }
    }

    static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    void Select(int i)
    {
        _selected = i;
        _scroll = 0;
    }

    void Scroll(int delta)
    {
        int max = Math.Max(0, VisibleLog.Count - _rows);
        _scroll = Math.Clamp(_scroll - delta, 0, max);
    }

    // ── drawing ─────────────────────────────────────────────────────────────

    void Render()
    {
        lock (_renderGate)
        {
            if (_quit) return;
            var sb = new StringBuilder(64 * 1024);
            sb.Append($"{E}[?25l");
            DrawHeader(sb);
            DrawSidebar(sb);
            DrawLog(sb);
            Write(sb.ToString());
        }
    }

    static (int r, int g, int b) Rgb(string hex)
    {
        int n = Convert.ToInt32(hex.TrimStart('#'), 16);
        return ((n >> 16) & 0xff, (n >> 8) & 0xff, n & 0xff);
    }
    static string Bg(string hex) { var (r, g, b) = Rgb(hex); return $"{E}[48;2;{r};{g};{b}m"; }
    static string Fg(string hex) { var (r, g, b) = Rgb(hex); return $"{E}[38;2;{r};{g};{b}m"; }
    static string Darken(string hex, double f)
    {
        var (r, g, b) = Rgb(hex);
        return $"#{(int)(r * f):x2}{(int)(g * f):x2}{(int)(b * f):x2}";
    }
    static void Goto(StringBuilder sb, int row, int col) => sb.Append($"{E}[{row};{col}H");

    static string Fit(string s, int w) => s.Length > w ? s[..w] : s.PadRight(w);

    void DrawHeader(StringBuilder sb)
    {
        Goto(sb, 1, 1);
        var accent = Sel?.Config.Color is { } c && Palette.TryGetValue(c, out var hx) ? hx : "#909090";
        bool failed = _runner.IsManaged && _runner.BuildState == BuildState.Failed;
        var bg = Bg(failed ? "#5a1216" : Darken(accent, 0.30));
        string glyph = "", gcol = Fg(accent);
        if (_runner.IsManaged)
        {
            switch (_runner.BuildState)
            {
                case BuildState.Building: glyph = Spinner[_spin % Spinner.Length]; gcol = $"{E}[35m"; break;
                case BuildState.Ok: glyph = "✓"; gcol = $"{E}[92m"; break;
                case BuildState.Failed: glyph = "✗"; gcol = $"{E}[91m{E}[1m"; break;
            }
        }
        if (_viewBuild) glyph = "≣";
        int g = glyph.Length > 0 ? 2 : 0;
        var title = Fit($" Dev · {_runner.Project.Name}", SW - 1 - g);
        sb.Append(bg).Append(Fg(accent)).Append($"{E}[1m").Append(title);
        if (g > 0) sb.Append(gcol).Append(glyph).Append(' ');
        sb.Append(R);
    }

    (string ch, string ansi) StateIcon(ServiceRunner s)
    {
        var spin = Spinner[_spin % Spinner.Length];
        return s.State switch
        {
            ServiceState.Running => ("●", $"{E}[32m"),
            ServiceState.Starting => (spin, $"{E}[36m"),
            ServiceState.Building => (spin, $"{E}[35m"),
            ServiceState.Restarting => (spin, $"{E}[33m"),
            ServiceState.Retrying => (spin, $"{E}[31m"),
            ServiceState.Crashed or ServiceState.Failed => ("✗", $"{E}[91m"),
            ServiceState.Stopped => ("○", $"{E}[90m"),
            ServiceState.Disabled => ("–", $"{E}[90m"),
            _ => (" ", ""),
        };
    }

    void DrawSidebar(StringBuilder sb)
    {
        var side = Bg("#2d2d2d");
        int nameW = SW - 1 - 4 - 6;
        int row = 2;
        for (int i = 0; i < Svcs.Count && row <= _rows; i++, row++)
        {
            var s = Svcs[i];
            Goto(sb, row, 1);
            var (ch, ansi) = StateIcon(s);
            bool sel = i == _selected;
            var bg = sel ? $"{E}[44m" : side;
            var fg = sel ? $"{E}[1m{E}[97m" : $"{E}[90m";
            var nameFg = sel ? $"{E}[1m{E}[97m"
                       : s.Config.Color is { } c && Palette.TryGetValue(c, out var hx) ? Fg(hx) : $"{E}[90m";
            var mem = Format.BytesShort(s.Metrics.WorkingSet).PadLeft(5);
            sb.Append(bg).Append(fg).Append(sel ? '>' : ' ').Append(' ')
              .Append(ansi).Append(ch).Append(bg).Append(nameFg).Append(' ')
              .Append(Fit(s.Name, nameW)).Append(sel ? fg : $"{E}[90m").Append(mem).Append(' ').Append(R);
        }

        // details for the selected service + totals
        var details = new List<string> { "" };
        if (Sel is { } cur)
        {
            var m = cur.Metrics;
            var port = _runner.PortOf(cur);
            details.Add($"{StateText(cur)}{(port is > 0 ? $"  port {port}" : "")}");
            if (cur.HasProcess)
            {
                details.Add($"RAM {Format.Bytes(m.WorkingSet)}  CPU {m.CpuPercent:0.#}%");
                details.Add($"PID {cur.Pid} · {m.ProcessCount} proc{(m.ProcessCount == 1 ? "" : "s")}");
                if (m.Ports.Count > 0) details.Add("listening " + string.Join(" ", m.Ports.Select(p => ":" + p)));
                if (cur.StartedAt is { } at) details.Add($"up {Format.Duration(DateTime.Now - at)}");
            }
        }
        details.Add("");
        details.Add($"Σ RAM {Format.Bytes(_runner.TotalWorkingSet)}  CPU {_runner.TotalCpu:0.#}%");

        var help = _helpVisible ? Help : ["H  help"];
        int helpTop = Math.Max(row + details.Count, _rows - help.Length + 1);

        foreach (var d in details)
        {
            if (row > _rows) break;
            Goto(sb, row++, 1);
            sb.Append(side).Append($"{E}[38;2;170;170;170m").Append(Fit(" " + d, SW - 1)).Append(R);
        }
        for (; row < helpTop && row <= _rows; row++)
        {
            Goto(sb, row, 1);
            sb.Append(side).Append(new string(' ', SW - 1)).Append(R);
        }
        for (int j = 0; j < help.Length && helpTop + j <= _rows; j++)
        {
            Goto(sb, helpTop + j, 1);
            bool isTitle = _helpVisible && (j == 0 || j == help.Length - 1);
            var fg = isTitle ? $"{E}[38;2;150;150;150m{E}[1m" : $"{E}[90m";
            sb.Append(side).Append(fg).Append(Fit(" " + help[j], SW - 1)).Append(R);
        }
        var div = Bg("#404040");
        for (int i = 1; i <= _rows; i++)
        {
            Goto(sb, i, SW);
            sb.Append(div).Append(' ').Append(R);
        }
    }

    static string StateText(ServiceRunner s) => s.State switch
    {
        ServiceState.Running => "running",
        ServiceState.Starting => "starting…",
        ServiceState.Building => "building…",
        ServiceState.Restarting => "restarting…",
        ServiceState.Retrying => "retrying…",
        ServiceState.Crashed => "crashed",
        ServiceState.Failed => "failed",
        ServiceState.Disabled => "disabled",
        _ => "stopped",
    };

    void DrawLog(StringBuilder sb)
    {
        var log = VisibleLog;
        int h = _rows, fullW = _cols - SW, left = SW + 1;
        int total = log.Count;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, total - h));
        int end = total - _scroll, start = Math.Max(0, end - h);
        var slice = log.Range(start, end - start);

        bool bar = total > h;
        int textW = bar ? fullW - 1 : fullW;
        int barCol = left + fullW - 1;
        int thumbTop = 0, thumbSize = h;
        if (bar)
        {
            thumbSize = Math.Max(1, (int)Math.Round((double)h * h / total));
            int scrollable = total - h;
            double frac = scrollable > 0 ? (double)start / scrollable : 0;
            thumbTop = (int)Math.Round((h - thumbSize) * frac);
        }

        for (int row = 0; row < h; row++)
        {
            Goto(sb, 1 + row, left);
            sb.Append($"{E}[40m{E}[K");
            if (row < slice.Count) sb.Append($"{E}[40m").Append(Truncate(slice[row].Text, textW));
            if (bar)
            {
                Goto(sb, 1 + row, barCol);
                bool thumb = row >= thumbTop && row < thumbTop + thumbSize;
                sb.Append($"{E}[40m").Append(thumb ? $"{E}[38;2;96;165;250m█" : $"{E}[38;2;68;68;68m│");
            }
            sb.Append(R);
        }
    }

    static string Truncate(string s, int max)
    {
        if (max <= 0) return "";
        if (Ansi.Strip(s).Length <= max) return s;
        var sb = new StringBuilder();
        int visible = 0;
        bool inEsc = false;
        foreach (var c in s)
        {
            if (c == '\x1b') inEsc = true;
            if (inEsc) { sb.Append(c); if (c == 'm') inEsc = false; continue; }
            if (visible >= max) break;
            sb.Append(c);
            visible++;
        }
        return sb.Append(R).ToString();
    }
}
