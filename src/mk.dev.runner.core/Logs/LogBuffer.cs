namespace mk.dev.runner.core.Logs
{
    public readonly record struct LogLine(long Seq, DateTime Time, string Text);

    public sealed class LogBuffer
    {
        private readonly object _gate = new();
        private readonly List<LogLine> _lines = [];
        private long _seq;
        private StreamWriter? _file;
        private Timer? _flush;

        public int MaxLines { get; set; }

        public LogBuffer(int maxLines = 2000)
        {
            MaxLines = Math.Max(100, maxLines);
        }

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
                var over = _lines.Count - MaxLines;
                if (over > 0)
                    _lines.RemoveRange(0, over);
                try { _file?.WriteLine(LogFiles.Format(line)); } catch { }
            }
        }

        public List<LogLine> Since(long afterSeq, out bool reset)
        {
            lock (_gate)
            {
                reset = _lines.Count > 0 && _lines[0].Seq > afterSeq + 1 && afterSeq != 0;
                var i = _lines.FindIndex(l => l.Seq > afterSeq);
                return i < 0 ? [] : _lines.GetRange(i, _lines.Count - i);
            }
        }

        public List<LogLine> Snapshot()
        {
            lock (_gate) return [.. _lines];
        }


        public void Clear()
        {
            lock (_gate) _lines.Clear();
        }

        public void OpenFile(string path, string header)
        {
            string? error = null;
            lock (_gate)
            {
                CloseFileLocked();
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                    _file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                    {
                        AutoFlush = false,
                    };
                    _file.WriteLine("# " + header);
                    _file.Flush();
                    _flush = new Timer(_ => Flush(), null, 1000, 1000);
                    Path = path;
                }
                catch (Exception e)
                {
                    _file = null;
                    error = e.Message;
                }
            }
            if (error != null)
                Add($"\x1b[91m[runner] couldn't write the log file {path}: {error}\x1b[0m");
        }

        public string? Path { get; private set; }

        public void CloseFile()
        {
            lock (_gate) CloseFileLocked();
        }

        private void Flush()
        {
            lock (_gate)
            {
                try { _file?.Flush(); }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
            }
        }

        private void CloseFileLocked()
        {
            _flush?.Dispose();
            _flush = null;
            try { _file?.Dispose(); } catch { }
            _file = null;
            Path = null;
        }
    }
}
