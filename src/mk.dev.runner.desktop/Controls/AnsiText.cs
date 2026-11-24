using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using mk.dev.runner.core.Logs;

namespace mk.dev.runner.desktop.Controls
{
    public static class AnsiText
    {
        public static readonly DependencyProperty LineProperty = DependencyProperty.RegisterAttached(
            "Line", typeof(LogLine), typeof(AnsiText), new PropertyMetadata(default(LogLine), OnChanged));

        public static LogLine GetLine(DependencyObject d)
        {
            return (LogLine)d.GetValue(LineProperty);
        }

        public static void SetLine(DependencyObject d, LogLine v)
        {
            d.SetValue(LineProperty, v);
        }

        private static readonly SolidColorBrush TimeBrush = Palette.Brush(0x52525b);

        private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb)
                return;
            var line = (LogLine)e.NewValue;
            tb.Inlines.Clear();
            if (line.Text == null)
                return;
            if (line.Time != default)
                tb.Inlines.Add(new Run(line.Time.ToString("HH:mm:ss ")) { Foreground = TimeBrush });
            foreach (var seg in Ansi.Parse(line.Text))
            {
                var run = new Run(seg.Text);
                if (seg.Fg is { } fg)
                    run.Foreground = Palette.Brush(fg);
                if (seg.Bg is { } bg)
                    run.Background = Palette.Brush(bg);
                if (seg.Bold)
                    run.FontWeight = FontWeights.SemiBold;
                if (seg.Dim && seg.Fg == null)
                    run.Foreground = TimeBrush;
                tb.Inlines.Add(run);
            }
        }
    }
}
