using System.Collections.ObjectModel;
using mk.dev.runner.core.Logs;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed class LogSourceViewModel(string name, Func<LogBuffer> buffer, string? colorHex) : Observable
    {
        private long _lastSeq;
        private LogBuffer? _bound;

        public string Name { get; } = name;
        public string? ColorHex { get; } = colorHex;
        public ObservableCollection<LogLine> Lines { get; } = [];

        public event Action? Appended;

        public void Pull()
        {
            var buf = buffer();
            if (!ReferenceEquals(buf, _bound)) { _bound = buf; _lastSeq = 0; Lines.Clear(); }
            var fresh = buf.Since(_lastSeq, out var reset);
            if (reset) { Lines.Clear(); fresh = buf.Snapshot(); }
            if (fresh.Count == 0)
                return;
            foreach (var l in fresh) Lines.Add(l);
            _lastSeq = fresh[^1].Seq;
            int over = Lines.Count - buf.MaxLines;
            for (int i = 0; i < over; i++) Lines.RemoveAt(0);
            Appended?.Invoke();
        }

        public void Clear()
        {
            var buf = buffer();
            _lastSeq = buf.LastSeq;
            Lines.Clear();
        }
    }
}
