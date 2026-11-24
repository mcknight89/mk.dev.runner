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

namespace mk.dev.runner.desktop.Controls;

/// <summary>TextBlock.(AnsiText.Line) = a log line; renders its colour codes as coloured runs.</summary>
public static class AnsiText
{
    public static readonly DependencyProperty LineProperty = DependencyProperty.RegisterAttached(
        "Line", typeof(LogLine), typeof(AnsiText), new PropertyMetadata(default(LogLine), OnChanged));

    public static LogLine GetLine(DependencyObject d) => (LogLine)d.GetValue(LineProperty);
    public static void SetLine(DependencyObject d, LogLine v) => d.SetValue(LineProperty, v);

    static readonly Dictionary<int, SolidColorBrush> Brushes = [];
    static readonly SolidColorBrush TimeBrush = Make(0x52525b);

    static SolidColorBrush Make(int rgb)
    {
        var b = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        b.Freeze();
        return b;
    }

    static SolidColorBrush Get(int rgb)
    {
        if (!Brushes.TryGetValue(rgb, out var b)) Brushes[rgb] = b = Make(rgb);
        return b;
    }

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        var line = (LogLine)e.NewValue;
        tb.Inlines.Clear();
        if (line.Text == null) return;
        if (line.Time != default) tb.Inlines.Add(new Run(line.Time.ToString("HH:mm:ss ")) { Foreground = TimeBrush });
        foreach (var seg in Ansi.Parse(line.Text))
        {
            var run = new Run(seg.Text);
            if (seg.Fg is { } fg) run.Foreground = Get(fg);
            if (seg.Bg is { } bg) run.Background = Get(bg);
            if (seg.Bold) run.FontWeight = FontWeights.SemiBold;
            if (seg.Dim && seg.Fg == null) run.Foreground = TimeBrush;
            tb.Inlines.Add(run);
        }
    }
}

/// <summary>A log list that sticks to the bottom while you're at the bottom, and copies selected lines with Ctrl+C.</summary>
public sealed class LogListBox : ListBox
{
    ScrollViewer? _sv;
    bool _stick = true;

    public LogListBox()
    {
        SelectionMode = SelectionMode.Extended;
        Loaded += (_, _) =>
        {
            _sv = FindScroll(this);
            if (_sv != null) _sv.ScrollChanged += OnScrollChanged;
            ScrollToEnd();
        };
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopySelection()));
    }

    void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_sv == null) return;
        // Only a user scroll (not new content growing the extent) changes stickiness.
        if (e.ExtentHeightChange == 0) _stick = _sv.VerticalOffset >= _sv.ScrollableHeight - 0.5;
        else if (_stick) _sv.ScrollToEnd();
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        if (e.Action == NotifyCollectionChangedAction.Reset) _stick = true;
        if (_stick) ScrollToEnd();
    }

    public void ScrollToEnd()
    {
        _stick = true;
        Dispatcher.BeginInvoke(() => _sv?.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);
    }

    void CopySelection()
    {
        var lines = SelectedItems.OfType<LogLine>().OrderBy(l => l.Seq).Select(l => Ansi.Strip(l.Text));
        var text = string.Join(Environment.NewLine, lines);
        if (text.Length > 0) Clipboard.SetText(text);
    }

    public string AllText()
    {
        var sb = new StringBuilder();
        foreach (var l in Items.OfType<LogLine>()) sb.AppendLine(Ansi.Strip(l.Text));
        return sb.ToString();
    }

    static ScrollViewer? FindScroll(DependencyObject d)
    {
        if (d is ScrollViewer sv) return sv;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (FindScroll(VisualTreeHelper.GetChild(d, i)) is { } found) return found;
        return null;
    }
}

public sealed class BoolToVisibility : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        bool v = value switch { bool b => b, string s => !string.IsNullOrEmpty(s), null => false, _ => true };
        return v ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>A palette colour name ("cyan") → its brush.</summary>
public sealed class ColorNameToBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is string s && ViewModels.ServiceViewModel.Palette.TryGetValue(s, out var hex)
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))
            : System.Windows.Media.Brushes.Gray;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

public sealed class HexToBrush : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is string s ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(s)) : System.Windows.Media.Brushes.Gray;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
