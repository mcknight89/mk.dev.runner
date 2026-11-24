namespace mk.dev.runner.core.Logs;

public readonly record struct LogLine(long Seq, DateTime Time, string Text);

/// <summary>
/// Thread-safe ring of log lines (colour codes kept), mirrored to the session's log file while a session
/// runs (LogFiles: "HH:mm:ss.fff text", colours kept).
/// Each line has a sequence number so viewers can ask for "everything after N".
/// </summary>
public sealed class LogBuffer
{
    readonly object _gate = new();
    readonly List<LogLine> _lines = [];
    long _seq;
    StreamWriter? _file;

    public int MaxLines { get; set; }

    public LogBuffer(int maxLines = 2000) => MaxLines = Math.Max(100, maxLines);

    public event Action<LogLine>? LineAdded;

    public long LastSeq { get { lock (_gate) return _seq; } }

    public int Count { get { lock (_gate) return _lines.Count; } }

    public void Add(string text)
    {
        text = Ansi.Sanitize(text);
        LogLine line;
        lock (_gate)
        {
            line = new LogLine(++_seq, DateTime.Now, text);
            _lines.Add(line);
            int over = _lines.Count - MaxLines;
            if (over > 0) _lines.RemoveRange(0, over);
            try { _file?.WriteLine(LogFiles.Format(line)); } catch { }
        }
        LineAdded?.Invoke(line);
    }

    /// <summary>Lines with Seq greater than <paramref name="afterSeq"/>. <paramref name="reset"/> is true when lines were dropped in between.</summary>
    public List<LogLine> Since(long afterSeq, out bool reset)
    {
        lock (_gate)
        {
            reset = _lines.Count > 0 && _lines[0].Seq > afterSeq + 1 && afterSeq != 0;
            int i = _lines.FindIndex(l => l.Seq > afterSeq);
            return i < 0 ? [] : _lines.GetRange(i, _lines.Count - i);
        }
    }

    public List<LogLine> Snapshot()
    {
        lock (_gate) return [.. _lines];
    }

    /// <summary>Lines [start, start+count) counted from the oldest kept line.</summary>
    public List<LogLine> Range(int start, int count)
    {
        lock (_gate)
        {
            start = Math.Clamp(start, 0, _lines.Count);
            count = Math.Clamp(count, 0, _lines.Count - start);
            return _lines.GetRange(start, count);
        }
    }

    public void Clear()
    {
        lock (_gate) _lines.Clear();
    }

    public void OpenFile(string path, string header)
    {
        lock (_gate)
        {
            CloseFileLocked();
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                _file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    AutoFlush = true,
                };
                _file.WriteLine("# " + header);
                Path = path;
            }
            catch { _file = null; }
        }
    }

    /// <summary>The file this run is written to, or null when none is open.</summary>
    public string? Path { get; private set; }

    public void CloseFile()
    {
        lock (_gate) CloseFileLocked();
    }

    void CloseFileLocked()
    {
        try { _file?.Dispose(); } catch { }
        _file = null;
        Path = null;
    }
}
