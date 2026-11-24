using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
        public string Value { get => _value; set { if (Set(ref _value, value)) Raise(nameof(DisplayValue)); } }
        public string Where { get => _where; set => Set(ref _where, value); }

        public bool Mask { get => _mask; set { if (Set(ref _mask, value)) Raise(nameof(DisplayValue)); } }

        public string DisplayValue
        {
            get => _mask && _value.Length > 0 ? new string('•', Math.Min(_value.Length, 24)) : _value;
            set { if (!_mask || value != DisplayValue) Value = value; }
        }
    }

    public sealed class EnvScopeItem
    {
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
        private readonly ProjectViewModel _vm;
        private readonly Project _project;
        private readonly List<EnvScopeItem> _scopes = [];
        private EnvScopeItem? _current;

        public EnvEditorWindow(ProjectViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            _project = vm.Runner!.Project;
            _project.Reload();
            Title = $"Environment — {_project.Name}";
            Heading.Text = $"Environment variables · {_project.Name}";

            _scopes.Add(Load(null, "All services", "every service gets these"));
            foreach (var s in _project.Config.Services)
                _scopes.Add(Load(s.Key, s.DisplayName, $"{s.Type} · {s.Key}"));
            foreach (var f in _project.Config.Globals.RestartOnChange ?? [".env"])
            {
                var full = Path.GetFullPath(Path.Combine(_project.RootDir, f));
                if (!File.Exists(full))
                    continue;
                _scopes.Add(new EnvScopeItem { Title = Path.GetFileName(full), Subtitle = "file · restarts dotnet services on save", FilePath = full });
            }
            Scopes.ItemsSource = _scopes;
            Scopes.SelectedIndex = 0;
        }

        private EnvScopeItem Load(string? serviceKey, string title, string subtitle)
        {
            var item = new EnvScopeItem { Title = title, Subtitle = subtitle, ServiceKey = serviceKey };
            foreach (var (k, v) in _project.GetEnv(serviceKey, EnvScope.Shared).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                item.Rows.Add(new EnvRow { Key = k, Value = v, Where = "Shared" });
            foreach (var (k, v) in _project.GetEnv(serviceKey, EnvScope.Local).OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                item.Rows.Add(new EnvRow { Key = k, Value = v, Where = "Local" });
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
                VarsPanel.Visibility = Visibility.Collapsed;
                FilePanel.Visibility = Visibility.Visible;
                if (_current.FileOriginal == null)
                {
                    try { _current.FileOriginal = File.ReadAllText(_current.FilePath!); }
                    catch (Exception ex) { _current.FileOriginal = ""; Status.Text = ex.Message; }
                }
                FileText.Text = _current.FileEdited ?? _current.FileOriginal;
                FileNote.Text = $"{_current.FilePath}\nYour apps read this file themselves. Saving it restarts the running dotnet services.";
                return;
            }

            VarsPanel.Visibility = Visibility.Visible;
            FilePanel.Visibility = Visibility.Collapsed;
            foreach (var r in _current.Rows) r.Mask = Mask.IsChecked == true;
            Grid.ItemsSource = _current.Rows;

            if (_current.ServiceKey == null)
            {
                InheritedTitle.Visibility = Inherited.Visibility = Visibility.Collapsed;
            }
            else
            {
                var top = _scopes[0].Rows.Where(r => r.Key.Trim().Length > 0)
                    .GroupBy(r => r.Key.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.FirstOrDefault(r => r.Where == "Local") ?? g.First())
                    .Where(r => !_current.Rows.Any(o => o.Key.Trim().Equals(r.Key.Trim(), StringComparison.OrdinalIgnoreCase)))
                    .Select(r => $"{r.Key}={(Mask.IsChecked == true ? "•••" : r.Value)}")
                    .ToList();
                InheritedTitle.Visibility = Inherited.Visibility = top.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                Inherited.Text = string.Join("   ", top);
            }
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
            foreach (var r in Grid.SelectedItems.OfType<EnvRow>().ToList()) _current.Rows.Remove(r);
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _current.IsFile)
                return;
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            int added = 0;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                if (line.StartsWith("export "))
                    line = line[7..].Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                if (val.Length >= 2 && (val[0] == '"' && val[^1] == '"' || val[0] == '\'' && val[^1] == '\''))
                    val = val[1..^1];
                var existing = _current.Rows.FirstOrDefault(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && r.Where == "Local");
                if (existing != null)
                    existing.Value = val;
                else _current.Rows.Add(new EnvRow { Key = key, Value = val, Where = "Local", Mask = Mask.IsChecked == true });
                added++;
            }
            Status.Text = added > 0 ? $"Pasted {added} variable(s) as Local." : "No KEY=VALUE lines on the clipboard.";
        }

        private void Mask_Changed(object sender, RoutedEventArgs e)
        {
            Grid.CommitEdit(DataGridEditingUnit.Row, true);
            ValueColumn.IsReadOnly = Mask.IsChecked == true;
            foreach (var s in _scopes) foreach (var r in s.Rows) r.Mask = Mask.IsChecked == true;
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
                        Status.Text = $"'{bad.Key}' in {scope.Title}: names can't contain spaces or '='.";
                        Scopes.SelectedItem = scope;
                        return;
                    }
                    foreach (var where in new[] { (EnvScope.Shared, "Shared"), (EnvScope.Local, "Local") })
                    {
                        var wanted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var r in rows.Where(r => r.Where == where.Item2)) wanted[r.Key.Trim()] = r.Value;
                        var current = _project.GetEnv(scope.ServiceKey, where.Item1);
                        if (current.Count == wanted.Count && current.All(kv => wanted.TryGetValue(kv.Key, out var v) && v == kv.Value))
                            continue;
                        _project.SetEnv(scope.ServiceKey, where.Item1, wanted);
                    }
                }
                foreach (var f in _scopes.Where(s => s.IsFile && s.FileEdited != null && s.FileEdited != s.FileOriginal))
                    File.WriteAllText(f.FilePath!, f.FileEdited);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Couldn't save", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _vm.Runner!.ApplyEnvChanges();
            Close();
        }
    }
}
