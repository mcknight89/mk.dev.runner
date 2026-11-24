using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using mk.dev.runner.core.Logs;

namespace mk.dev.runner.desktop.Controls
{
    public static class AnsiText
    {
        public static readonly DependencyProperty LineProperty = DependencyProperty.RegisterAttached(
            "Line", typeof(LogLine), typeof(AnsiText), new PropertyMetadata(default(LogLine), OnChanged));

        public static readonly DependencyProperty HighlightProperty = DependencyProperty.RegisterAttached(
            "Highlight", typeof(string), typeof(AnsiText), new PropertyMetadata(null, OnChanged));

        private static readonly SolidColorBrush TimeBrush = Palette.Brush(0x52525b);
        private static readonly SolidColorBrush MatchBrush = Palette.Brush("#8a6d1a");

        public static LogLine GetLine(DependencyObject d)
        {
            return (LogLine)d.GetValue(LineProperty);
        }

        public static void SetLine(DependencyObject d, LogLine v)
        {
            d.SetValue(LineProperty, v);
        }

        public static string? GetHighlight(DependencyObject d)
        {
            return (string?)d.GetValue(HighlightProperty);
        }

        public static void SetHighlight(DependencyObject d, string? v)
        {
            d.SetValue(HighlightProperty, v);
        }

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb)
                return;
            var line = GetLine(tb);
            var term = GetHighlight(tb);
            tb.Inlines.Clear();
            if (line.Text == null)
                return;

            if (line.Time != default)
                tb.Inlines.Add(new Run(line.Time.ToString("HH:mm:ss ")) { Foreground = TimeBrush });

            var segments = Ansi.Parse(line.Text);
            var matches = Matches(string.Concat(segments.Select(s => s.Text)), term);
            var at = 0;
            foreach (var seg in segments)
            {
                foreach (var (text, matched) in Split(seg.Text, at, matches))
                    tb.Inlines.Add(MakeRun(text, seg, matched));
                at += seg.Text.Length;
            }
        }

        private static Run MakeRun(string text, Ansi.Segment seg, bool matched)
        {
            var run = new Run(text);
            if (seg.Fg is { } fg)
                run.Foreground = Palette.Brush(fg);
            if (seg.Bg is { } bg)
                run.Background = Palette.Brush(bg);
            if (seg.Bold)
                run.FontWeight = FontWeights.SemiBold;
            if (seg.Dim && seg.Fg == null)
                run.Foreground = TimeBrush;
            if (matched)
                run.Background = MatchBrush;
            return run;
        }

        private static List<(int start, int end)> Matches(string plain, string? term)
        {
            var found = new List<(int, int)>();
            if (string.IsNullOrEmpty(term))
                return found;
            var i = 0;
            while ((i = plain.IndexOf(term, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                found.Add((i, i + term.Length));
                i += term.Length;
            }
            return found;
        }

        private static IEnumerable<(string text, bool matched)> Split(string text, int offset, List<(int start, int end)> matches)
        {
            var pos = 0;
            foreach (var (start, end) in matches)
            {
                var from = Math.Max(start - offset, pos);
                var to = Math.Min(end - offset, text.Length);
                if (to <= pos || from >= text.Length)
                    continue;
                if (from > pos)
                    yield return (text[pos..from], false);
                yield return (text[from..to], true);
                pos = to;
            }
            if (pos < text.Length)
                yield return (text[pos..], false);
        }
    }
}
