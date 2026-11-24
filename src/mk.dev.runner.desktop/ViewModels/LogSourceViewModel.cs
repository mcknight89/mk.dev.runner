using mk.dev.runner.core.Logs;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed class LogSourceViewModel : Observable
    {
        private readonly Func<LogBuffer> _buffer;
        private long _lastSeq;
        private LogBuffer? _bound;

        public LogSourceViewModel(string name, Func<LogBuffer> buffer, string? colorHex)
        {
            Name = name;
            ColorHex = colorHex;
            _buffer = buffer;
        }

        public string Name { get; }
        public string? ColorHex { get; }
        public LogLines Lines { get; } = [];

        public void Pull()
        {
            var buf = _buffer();
            if (!ReferenceEquals(buf, _bound))
            {
                _bound = buf;
                _lastSeq = 0;
                Lines.Clear();
            }
            var fresh = buf.Since(_lastSeq, out var reset);
            if (reset)
            {
                Lines.Clear();
                fresh = buf.Snapshot();
            }
            if (fresh.Count == 0)
                return;
            Lines.AddMany(fresh);
            _lastSeq = fresh[^1].Seq;
            var over = Lines.Count - buf.MaxLines;
            if (over > buf.MaxLines / 10)
                Lines.RemoveFirst(over);
        }

        public void Clear()
        {
            _lastSeq = _buffer().LastSeq;
            Lines.Clear();
        }
    }
}
