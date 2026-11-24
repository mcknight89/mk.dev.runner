using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using mk.dev.runner.core.Logs;

namespace mk.dev.runner.desktop.Controls
{
    public sealed class LogListBox : ListBox
    {
        private ScrollViewer? _sv;
        private bool _stick = true;

        public LogListBox()
        {
            SelectionMode = SelectionMode.Extended;
            Loaded += (_, _) =>
            {
                _sv = FindScroll(this);
                if (_sv != null)
                    _sv.ScrollChanged += OnScrollChanged;
                ScrollToEnd();
            };
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, _) => CopySelection()));
            ContextMenu = new ContextMenu
            {
                Items =
                {
                    new MenuItem { Header = "Copy", Command = ApplicationCommands.Copy, InputGestureText = "Ctrl+C" },
                    new MenuItem { Header = "Select all", Command = ApplicationCommands.SelectAll, InputGestureText = "Ctrl+A" },
                    new MenuItem { Header = "Copy the whole log", Command = new ViewModels.Command(() => CopyAll()) },
                },
            };
        }

        public void CopyAll()
        {
            var text = AllText();
            if (text.Length > 0)
                Clipboard.SetText(text);
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_sv == null)
                return;
            if (e.ExtentHeightChange == 0)
                _stick = _sv.VerticalOffset >= _sv.ScrollableHeight - 0.5;
            else if (_stick)
                _sv.ScrollToEnd();
        }

        protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
        {
            base.OnItemsChanged(e);
            if (e.Action == NotifyCollectionChangedAction.Reset)
                _stick = true;
            if (_stick)
                ScrollToEnd();
        }

        public void ScrollToEnd()
        {
            _stick = true;
            Dispatcher.BeginInvoke(() => _sv?.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void CopySelection()
        {
            var lines = SelectedItems.OfType<LogLine>().OrderBy(l => l.Seq).Select(l => Ansi.Strip(l.Text));
            var text = string.Join(Environment.NewLine, lines);
            if (text.Length > 0)
                Clipboard.SetText(text);
        }

        public string AllText()
        {
            var sb = new StringBuilder();
            foreach (var l in Items.OfType<LogLine>())
                sb.AppendLine(Ansi.Strip(l.Text));
            return sb.ToString();
        }

        private static ScrollViewer? FindScroll(DependencyObject d)
        {
            if (d is ScrollViewer sv)
                return sv;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                if (FindScroll(VisualTreeHelper.GetChild(d, i)) is { } found)
                    return found;
            return null;
        }
    }
}
