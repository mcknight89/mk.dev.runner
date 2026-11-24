using System.Collections;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using mk.dev.runner.core.Logs;
using mk.dev.runner.desktop.ViewModels;

namespace mk.dev.runner.desktop.Controls
{
    public partial class LogView : UserControl
    {
        public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(
            nameof(Lines), typeof(IList), typeof(LogView), new PropertyMetadata(null, (d, _) => ((LogView)d).Attach()));

        public static readonly DependencyProperty ClearCommandProperty = DependencyProperty.Register(
            nameof(ClearCommand), typeof(ICommand), typeof(LogView), new PropertyMetadata(null, (d, _) => ((LogView)d).UpdateButtons()));

        public static readonly DependencyProperty FilePathProperty = DependencyProperty.Register(
            nameof(FilePath), typeof(string), typeof(LogView), new PropertyMetadata(null, (d, _) => ((LogView)d).UpdateButtons()));

        public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
            nameof(EmptyText), typeof(string), typeof(LogView), new PropertyMetadata("No output yet.", (d, _) => ((LogView)d).UpdateEmpty()));

        public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(
            nameof(SearchText), typeof(string), typeof(LogView), new PropertyMetadata(""));

        public static readonly DependencyProperty WrapModeProperty = DependencyProperty.Register(
            nameof(WrapMode), typeof(TextWrapping), typeof(LogView), new PropertyMetadata(TextWrapping.NoWrap));

        private static readonly Regex ErrorLine = new(@"\b(error|errors|fail|failed|failure|exception|fatal|crash|crashed|unhandled)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NoErrors = new(@"\b0 (error|errors|failed)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private ListCollectionView? _view;
        private INotifyCollectionChanged? _watched;
        private List<LogLine> _matches = [];
        private int _current = -1;
        private bool _matchesStale;

        public LogView()
        {
            FindCommand = new Command(FocusSearch);
            NextCommand = new Command(() => Step(+1));
            PreviousCommand = new Command(() => Step(-1));
            InitializeComponent();
            InputBindings.Add(new KeyBinding(FindCommand, Key.F, ModifierKeys.Control));
            InputBindings.Add(new KeyBinding(NextCommand, Key.F3, ModifierKeys.None));
            InputBindings.Add(new KeyBinding(PreviousCommand, Key.F3, ModifierKeys.Shift));
            UpdateButtons();
            UpdateEmpty();
        }

        public IList? Lines
        {
            get => (IList?)GetValue(LinesProperty);
            set => SetValue(LinesProperty, value);
        }

        public ICommand? ClearCommand
        {
            get => (ICommand?)GetValue(ClearCommandProperty);
            set => SetValue(ClearCommandProperty, value);
        }

        public string? FilePath
        {
            get => (string?)GetValue(FilePathProperty);
            set => SetValue(FilePathProperty, value);
        }

        public string EmptyText
        {
            get => (string)GetValue(EmptyTextProperty);
            set => SetValue(EmptyTextProperty, value);
        }

        public string SearchText
        {
            get => (string)GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        public TextWrapping WrapMode
        {
            get => (TextWrapping)GetValue(WrapModeProperty);
            set => SetValue(WrapModeProperty, value);
        }

        public ICommand FindCommand { get; }
        public ICommand NextCommand { get; }
        public ICommand PreviousCommand { get; }

        public void FocusSearch()
        {
            Search.Focus();
            Search.SelectAll();
        }

        private void Attach()
        {
            if (_watched != null)
                _watched.CollectionChanged -= OnLinesChanged;
            _watched = Lines as INotifyCollectionChanged;
            if (_watched != null)
                _watched.CollectionChanged += OnLinesChanged;

            _view = Lines == null ? null : new ListCollectionView(Lines);
            ApplyFilter();
            List.ItemsSource = _view;
            _matchesStale = true;
            UpdateCount();
            UpdateEmpty();
        }

        private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            _matchesStale = true;
            UpdateEmpty();
            if (SearchText.Length > 0)
                Dispatcher.BeginInvoke(UpdateCount, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void ApplyFilter()
        {
            if (_view == null)
                return;
            _view.Filter = ErrorsOnly.IsChecked == true ? IsErrorLine : null;
        }

        private static bool IsErrorLine(object item)
        {
            if (item is not LogLine line)
                return false;
            var plain = Ansi.Strip(line.Text);
            return ErrorLine.IsMatch(plain) && !NoErrors.IsMatch(plain);
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            ApplyFilter();
            _matchesStale = true;
            UpdateCount();
            UpdateEmpty();
        }

        private void Wrap_Changed(object sender, RoutedEventArgs e)
        {
            var wrap = Wrap.IsChecked == true;
            WrapMode = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            ScrollViewer.SetHorizontalScrollBarVisibility(List, wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
        }

        private void Search_TextChanged(object sender, TextChangedEventArgs e)
        {
            SearchText = Search.Text;
            _current = -1;
            _matchesStale = true;
            UpdateCount();
        }

        private void Search_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Step(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : +1);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && Search.Text.Length > 0)
            {
                Search.Clear();
                e.Handled = true;
            }
        }

        private void FindMatches()
        {
            if (!_matchesStale)
                return;
            _matchesStale = false;
            _matches = [];
            if (_view == null || SearchText.Length == 0)
                return;
            foreach (var item in _view)
                if (item is LogLine line && Ansi.Strip(line.Text).Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    _matches.Add(line);
            if (_current >= _matches.Count)
                _current = _matches.Count - 1;
        }

        private void Step(int direction)
        {
            FindMatches();
            if (_matches.Count == 0)
            {
                UpdateCount();
                return;
            }
            _current = _current < 0
                ? (direction > 0 ? 0 : _matches.Count - 1)
                : (_current + direction + _matches.Count) % _matches.Count;
            var line = _matches[_current];
            List.SelectedItem = line;
            List.ScrollIntoView(line);
            UpdateCount();
        }

        private void UpdateCount()
        {
            FindMatches();
            if (SearchText.Length == 0)
                MatchCount.Text = "";
            else if (_matches.Count == 0)
                MatchCount.Text = "No matches";
            else
                MatchCount.Text = _current >= 0 ? $"{_current + 1} of {_matches.Count}"
                    : _matches.Count == 1 ? "1 match" : $"{_matches.Count} matches";
        }

        private void UpdateEmpty()
        {
            var empty = _view == null || _view.Count == 0;
            EmptyHint.Text = ErrorsOnly.IsChecked == true && Lines is { Count: > 0 } ? "No error lines." : EmptyText;
            EmptyHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateButtons()
        {
            ClearButton.Visibility = ClearCommand == null ? Visibility.Collapsed : Visibility.Visible;
            EditorButton.Visibility = string.IsNullOrEmpty(FilePath) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            List.CopyAll();
        }

        private void Bottom_Click(object sender, RoutedEventArgs e)
        {
            List.ScrollToEnd();
        }

        private void Editor_Click(object sender, RoutedEventArgs e)
        {
            if (FilePath != null)
                Shell.OpenInEditor(FilePath);
        }
    }
}
