using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using mk.dev.runner.core;
using mk.dev.runner.desktop.ViewModels;

namespace mk.dev.runner.desktop.Views
{
    public sealed class EnvRow : Observable
    {
        public EnvRow()
        {
            ToggleWhere = new Command(() => Where = Where == "Local" ? "Shared" : "Local");
        }

        public Command ToggleWhere { get; }

        private string _key = "", _value = "", _where = "Local";
        private bool _mask;

        public string Key { get => _key; set => Set(ref _key, value); }
        public string Value
        {
            get => _value; set
            {
                if (Set(ref _value, value))
                    Raise(nameof(DisplayValue));
            }
        }
        public string Where
        {
            get => _where; set
            {
                if (Set(ref _where, value))
                    Raise(nameof(WhereText));
            }
        }
        public string WhereText => $"{_where} ⇄";

        public bool Mask
        {
            get => _mask; set
            {
                if (Set(ref _mask, value))
                    Raise(nameof(DisplayValue));
            }
        }

        public string DisplayValue
        {
            get => _mask && _value.Length > 0 ? new string('•', Math.Min(_value.Length, 24)) : _value;
            set
            {
                if (!_mask || value != DisplayValue)
                    Value = value;
            }
        }
    }

    public sealed class EnvScopeItem
    {
        public string Id { get; init; } = "";
        public string Title { get; init; } = "";
        public string Subtitle { get; init; } = "";
        public string? ServiceKey { get; init; }
        public string? FilePath { get; init; }
        public bool IsFile => FilePath != null;
        public ObservableCollection<EnvRow> Rows { get; } = [];
        public string? FileOriginal { get; set; }
        public string? FileEdited { get; set; }
    }

    public partial class EnvEditorWindow : Window
    {
        private readonly UnsavedChanges _unsaved;
        private readonly ProjectViewModel _vm;
        private readonly Project _project;
        private readonly ObservableCollection<EnvScopeItem> _scopes = [];
        private EnvScopeItem? _current;
        private Point _dragStart;
        private EnvScopeItem? _dragItem;

        public EnvEditorWindow(ProjectViewModel vm)
        {
            InitializeComponent();
            InputBindings.Add(new KeyBinding(new Command(() => Save_Click(this, new RoutedEventArgs())), Key.S, ModifierKeys.Control));
            _unsaved = new UnsavedChanges(this);
            _vm = vm;
            _project = vm.Runner!.Project;
            _project.Reload();
            Title = $"Environment — {_project.Name}";
            Heading.Text = $"Environment · {_project.Name}";

            var items = new List<EnvScopeItem>();
            foreach (var f in _project.EnvFiles())
            {
                var dir = Path.GetDirectoryName(f)!;
                var owners = _project.Config.Services
                    .Where(s => _project.ServiceDir(s).Equals(dir, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.DisplayName)
                    .ToList();
                var where = dir.Equals(_project.RootDir, StringComparison.OrdinalIgnoreCase) ? "project root" : string.Join(", ", owners);
                items.Add(new EnvScopeItem
                {
                    Id = "file:" + Path.GetRelativePath(_project.RootDir, f).ToLowerInvariant(),
                    Title = Path.GetFileName(f),
                    Subtitle = $"file · {where}",
                    FilePath = f,
                });
            }
            items.Add(Load(null, "all", "All services", "set on every service's command"));
            foreach (var s in _project.Config.Services)
                items.Add(Load(s.Key, "service:" + s.Key.ToLowerInvariant(), s.DisplayName, $"set on {s.Key}'s command"));

            var order = _project.EnvOrder();
            foreach (var item in items.OrderBy(x => order.IndexOf(x.Id) is var i and >= 0 ? i : order.Count + items.IndexOf(x)))
                _scopes.Add(item);
            Scopes.ItemsSource = _scopes;
            Scopes.SelectedIndex = 0;
            if (_project.IsLegacy)
                Status.Text = $"Local values are kept in {Path.GetFileName(_project.LocalPath)}. Make sure your .gitignore covers it.";
            else if (_project.ConfigHasComments)
                Status.Text = $"Changing a Shared value rewrites {Path.GetFileName(_project.ConfigPath)} and removes the comments in it.";
            Mask.IsChecked = true;
        }

        private EnvScopeItem Load(string? serviceKey, string id, string title, string subtitle)
        {
            var item = new EnvScopeItem { Id = id, Title = title, Subtitle = subtitle, ServiceKey = serviceKey };
            foreach (var (k, v) in _project.GetEnv(serviceKey, EnvScope.Shared).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                item.Rows.Add(new EnvRow { Key = k, Value = v, Where = "Shared" });
            foreach (var (k, v) in _project.GetEnv(serviceKey, EnvScope.Local).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                item.Rows.Add(new EnvRow { Key = k, Value = v, Where = "Local" });
            item.Rows.CollectionChanged += (_, _) => ShowEmptyHint();
            return item;
        }

        private void Scopes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
            if (_current?.IsFile == true)
                _current.FileEdited = FileText.Text;
            _current = Scopes.SelectedItem as EnvScopeItem;
            if (_current == null)
                return;

            if (_current.IsFile)
            {
                ShowFile(_current);
                return;
            }

            VarsPanel.Visibility = Visibility.Visible;
            FilePanel.Visibility = Visibility.Collapsed;
            foreach (var r in _current.Rows)
                r.Mask = Mask.IsChecked == true;
            Grid.ItemsSource = _current.Rows;
            ShowEmptyHint();
            ShowInherited();
        }

        private void ShowFile(EnvScopeItem item)
        {
            VarsPanel.Visibility = Visibility.Collapsed;
            FilePanel.Visibility = Visibility.Visible;
            var exists = File.Exists(item.FilePath);
            if (item.FileOriginal == null)
            {
                try
                {
                    item.FileOriginal = exists ? File.ReadAllText(item.FilePath!) : "";
                }
                catch (Exception ex) { item.FileOriginal = ""; Status.Text = ex.Message; }
            }
            FileText.Text = item.FileEdited ?? item.FileOriginal;
            var watched = (_project.Config.Globals.RestartOnChange ?? [".env"])
                .Any(f => Path.GetFullPath(Path.Combine(_project.RootDir, f)).Equals(item.FilePath, StringComparison.OrdinalIgnoreCase));
            var note = !exists ? "It doesn't exist yet. Saving with text in it creates it."
                : watched ? "Your apps read this file themselves. Saving it restarts the running dotnet services."
                : "Your apps read this file themselves. Restart a service to pick up changes.";
            FileNote.Text = $"{item.FilePath}\n{note}";
        }

        private void ShowInherited()
        {
            if (_current?.ServiceKey == null)
            {
                InheritedTitle.Visibility = Inherited.Visibility = Visibility.Collapsed;
                return;
            }
            var all = _scopes.First(x => x.Id == "all");
            var top = all.Rows.Where(r => r.Key.Trim().Length > 0)
                .GroupBy(r => r.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.FirstOrDefault(r => r.Where == "Local") ?? g.First())
                .Where(r => !_current.Rows.Any(o => o.Key.Trim().Equals(r.Key.Trim(), StringComparison.OrdinalIgnoreCase)))
                .Select(r => $"{r.Key}={(Mask.IsChecked == true ? "•••" : r.Value)}")
                .ToList();
            InheritedTitle.Visibility = Inherited.Visibility = top.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Inherited.Text = string.Join("   ", top);
        }

        private void ShowEmptyHint()
        {
            EmptyHint.Visibility = _current is { IsFile: false, Rows.Count: 0 } ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteButton.IsEnabled = Grid.SelectedItems.Count > 0;
        }

        private void Scopes_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(Scopes);
            _dragItem = ItemUnder(e.OriginalSource);
        }

        private void Scopes_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed)
                return;
            var moved = e.GetPosition(Scopes) - _dragStart;
            if (Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            var item = _dragItem;
            _dragItem = null;
            DragDrop.DoDragDrop(Scopes, item, DragDropEffects.Move);
        }

        private void Scopes_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(typeof(EnvScopeItem)) is not EnvScopeItem dragged)
                return;
            var target = ItemUnder(e.OriginalSource);
            var to = target == null ? _scopes.Count - 1 : _scopes.IndexOf(target);
            var from = _scopes.IndexOf(dragged);
            if (from < 0 || to < 0 || from == to)
                return;
            _scopes.Move(from, to);
            _unsaved.Mark();
            Scopes.SelectedItem = dragged;
        }

        private static EnvScopeItem? ItemUnder(object source)
        {
            var d = source as DependencyObject;
            while (d != null && d is not ListBoxItem)
                d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return (d as ListBoxItem)?.DataContext as EnvScopeItem;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _current.IsFile)
                return;
            var row = new EnvRow { Key = "", Value = "", Where = "Local" };
            _current.Rows.Add(row);
            Grid.SelectedItem = row;
            Grid.ScrollIntoView(row);
            Grid.CurrentCell = new DataGridCellInfo(row, Grid.Columns[0]);
            Grid.BeginEdit();
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _current.IsFile)
                return;
            foreach (var r in Grid.SelectedItems.OfType<EnvRow>().ToList())
                _current.Rows.Remove(r);
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _current.IsFile)
                return;
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            var added = 0;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                if (line.StartsWith("export "))
                    line = line[7..].Trim();
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                if (val.Length >= 2 && (val[0] == '"' && val[^1] == '"' || val[0] == '\'' && val[^1] == '\''))
                    val = val[1..^1];
                var existing = _current.Rows.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && r.Where == "Local");
                if (existing != null)
                    existing.Value = val;
                else
                    _current.Rows.Add(new EnvRow { Key = key, Value = val, Where = "Local", Mask = Mask.IsChecked == true });
                added++;
            }
            Status.Foreground = (System.Windows.Media.Brush)FindResource("Muted");
            Status.Text = added == 0 ? "No KEY=VALUE lines on the clipboard."
                : added == 1 ? "Pasted 1 variable as Local." : $"Pasted {added} variables as Local.";
        }

        private void Mask_Changed(object sender, RoutedEventArgs e)
        {
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
            ValueColumn.IsReadOnly = Mask.IsChecked == true;
            foreach (var s in _scopes)
                foreach (var r in s.Rows)
                    r.Mask = Mask.IsChecked == true;
            Scopes_SelectionChanged(this, null!);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
            if (_current?.IsFile == true)
                _current.FileEdited = FileText.Text;

            try
            {
                foreach (var scope in _scopes.Where(s => !s.IsFile))
                {
                    var rows = scope.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Key)).ToList();
                    var bad = rows.FirstOrDefault(r => r.Key.Trim().Any(char.IsWhiteSpace) || r.Key.Contains('='));
                    if (bad != null)
                    {
                        Status.Foreground = (System.Windows.Media.Brush)FindResource("Bad");
                        Status.Text = $"'{bad.Key}' in {scope.Title}: names can't contain spaces or '='.";
                        Scopes.SelectedItem = scope;
                        return;
                    }
                    foreach (var where in new[] { (EnvScope.Shared, "Shared"), (EnvScope.Local, "Local") })
                    {
                        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in rows.Where(r => r.Where == where.Item2))
                            wanted[r.Key.Trim()] = r.Value;
                        var current = _project.GetEnv(scope.ServiceKey, where.Item1);
                        if (current.Count == wanted.Count && current.All(kv => wanted.TryGetValue(kv.Key, out var v) && v == kv.Value))
                            continue;
                        _project.SetEnv(scope.ServiceKey, where.Item1, wanted);
                    }
                }
                foreach (var f in _scopes.Where(s => s.IsFile && s.FileEdited != null && s.FileEdited != s.FileOriginal))
                    File.WriteAllText(f.FilePath!, f.FileEdited);
                _project.SetEnvOrder(_scopes.Select(x => x.Id).ToList());
            }
            catch (Exception ex)
            {
                ChoiceWindow.Problem("The variables couldn't be saved", ex.Message);
                return;
            }

            _unsaved.Saved = true;
            _vm.Runner!.ApplyEnvChanges();
            Close();
        }
    }
}
