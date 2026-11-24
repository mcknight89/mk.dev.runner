using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Microsoft.Win32;
using mk.dev.runner.core;
using mk.dev.runner.desktop.ViewModels;

namespace mk.dev.runner.desktop.Views
{
    public sealed class ServiceRow : Observable
    {
        public ServiceRow(ServiceDefinition d)
        {
            D = d;
        }

        public ServiceDefinition D { get; }

        public string Name
        {
            get => D.Name; set
            {
                D.Name = value;
                Raise();
                Raise(nameof(Title));
            }
        }
        public string Key
        {
            get => D.Key; set
            {
                D.Key = value;
                Raise();
                Raise(nameof(Title));
            }
        }
        public string Type
        {
            get => D.Type; set
            {
                D.Type = value;
                Raise();
                Raise(nameof(Subtitle));
                Raise(nameof(PortHint));
                TypeChanged?.Invoke();
            }
        }
        public string Dir
        {
            get => D.Dir; set
            {
                D.Dir = value;
                Raise();
                Raise(nameof(Subtitle));
            }
        }
        public string? Color
        {
            get => D.Color; set
            {
                D.Color = value;
                Raise();
            }
        }
        public bool Enabled
        {
            get => D.Enabled; set
            {
                D.Enabled = value;
                Raise();
            }
        }
        public string? Script
        {
            get => D.Script; set
            {
                D.Script = value;
                Raise();
            }
        }
        public string? Cmd
        {
            get => D.Cmd; set
            {
                D.Cmd = value;
                Raise();
            }
        }
        public string? Args
        {
            get => D.Args; set
            {
                D.Args = value;
                Raise();
            }
        }
        public string Health
        {
            get => D.Health; set
            {
                D.Health = value;
                Raise();
                Raise(nameof(HealthValueVisible));
                Raise(nameof(HealthValueLabel));
                Raise(nameof(HealthValueHint));
                Raise(nameof(HealthHint));
            }
        }
        public string? HealthValue
        {
            get => D.HealthValue; set
            {
                D.HealthValue = value;
                Raise();
                Raise(nameof(HealthValueHint));
            }
        }
        public Visibility HealthValueVisible => D.Health is "http" or "log" ? Visibility.Visible : Visibility.Collapsed;
        public string HealthValueLabel => D.Health == "http" ? "Path" : "Log line to wait for (regex)";
        public string HealthValueHint => D.Health == "http"
            ? $"Added to the address, e.g. /health. Checks {CheckedUrl ?? "nothing until there's a port or address"}."
            : "Empty = the usual startup line for its type.";
        public string HealthHint => D.Health switch
        {
            "port" => "Running once something answers on the port. Checked every 10 s after that.",
            "http" => "Running once the address answers without an error. Checked every 10 s after that.",
            "log" => "Running once a log line matches.",
            "none" => "Running as soon as it starts. Nothing is checked.",
            _ => "Running once the port answers or the usual startup line appears. Checked every 10 s after that. Works for most apps.",
        };
        public string? Url
        {
            get => D.Url; set
            {
                D.Url = value;
                Raise();
                RaiseAddress();
            }
        }

        private string? DefaultAddress => D.Port is { } p ? $"http://localhost:{p}" : null;
        private string? Address => string.IsNullOrWhiteSpace(D.Url) ? DefaultAddress : D.Url.Replace("{port}", D.Port?.ToString() ?? "");
        private string? CheckedUrl
        {
            get
            {
                var path = D.HealthValue?.Trim() ?? "";
                if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return path.Replace("{port}", D.Port?.ToString() ?? "");
                if (Address == null || path.Length == 0)
                    return Address;
                return Address.TrimEnd('/') + (path.StartsWith('/') ? path : "/" + path);
            }
        }
        public string PortHint => D.Type == "dotnet"
            ? "Dev Runner starts the app on this port (it sets ASPNETCORE_URLS). Empty = the app picks."
            : "Dev Runner passes it to the app as PORT. Empty = none.";
        public string AddressHint => string.IsNullOrWhiteSpace(D.Url)
            ? $"What Browser opens and the URL check uses. Empty = {DefaultAddress ?? "none until there's a port"}."
            : "What Browser opens and the URL check uses. {port} becomes the port.";

        private void RaiseAddress()
        {
            Raise(nameof(AddressHint));
            Raise(nameof(HealthValueHint));
        }

        private string? _portText;
        public string PortText
        {
            get => _portText ?? D.Port?.ToString() ?? "";
            set
            {
                _portText = value;
                D.Port = int.TryParse(value.Trim(), out var p) ? p : null;
                PortInvalid = value.Trim().Length > 0 && !int.TryParse(value.Trim(), out _);
                Raise();
                Raise(nameof(Subtitle));
                RaiseAddress();
            }
        }
        public bool PortInvalid { get; private set; }

        public string Title => string.IsNullOrWhiteSpace(Name) ? (string.IsNullOrWhiteSpace(Key) ? "New service" : Key) : Name;
        public string Subtitle => string.Join(" · ", new[] { Type, Dir is "." or "" ? null : Dir, D.Port is { } p ? $":{p}" : null }.Where(x => x != null));

        public event Action? TypeChanged;
    }

    public partial class ProjectEditorWindow : Window
    {
        private readonly UnsavedChanges _unsaved;
        private readonly ProjectViewModel _vm;
        private readonly Project _project;
        private readonly ProjectDefinition _def;
        private readonly ObservableCollection<ServiceRow> _rows = [];

        public bool Saved { get; private set; }

        public ProjectEditorWindow(ProjectViewModel vm, string? serviceKey = null)
        {
            InitializeComponent();
            InputBindings.Add(new KeyBinding(new Command(() => Save_Click(this, new RoutedEventArgs())), Key.S, ModifierKeys.Control));
            _unsaved = new UnsavedChanges(this);
            _vm = vm;
            _project = vm.Runner!.Project;
            _def = _project.ReadDefinition();

            Title = $"Edit project — {vm.Name}";
            Heading.Text = $"Edit {vm.Name}";
            FilePath.Text = $"Saves to {_project.ConfigPath}" + (_project.ConfigHasComments ? ". Saving removes the comments in it." : "")
                + (vm.Runner.SessionActive ? ". Running services you change restart when you save." : "");
            ProjectName.Text = vm.Name;
            DotnetMode.ItemsSource = Project.DotnetModes;
            DotnetMode.SelectedItem = Project.DotnetModes.Contains(_def.DotnetMode) ? _def.DotnetMode : "watch";
            Solution.Text = _def.Solution ?? "";
            AutoRetry.IsChecked = _def.AutoRetry;
            MaxRetries.Text = _def.MaxRetries.ToString();
            AutoRebuild.IsChecked = _def.AutoRebuild;
            TypeBox.ItemsSource = Project.ServiceTypes;
            ColorBox.ItemsSource = Project.Colors;
            HealthBox.ItemsSource = Project.HealthChecks;

            foreach (var s in _def.Services)
                _rows.Add(Hook(new ServiceRow(s)));
            List.ItemsSource = _rows;
            var start = _rows.FirstOrDefault(r => r.Key.Equals(serviceKey, StringComparison.OrdinalIgnoreCase));
            if (start != null)
            {
                List.SelectedItem = start;
                Heading.Text = $"Edit {start.Title}";
                Title = $"Edit {start.Title} — {vm.Name}";
            }
            else
                List.SelectedIndex = _rows.Count > 0 ? 0 : -1;
            ShowSelected();
            UpdateModeFields();
        }

        private ServiceRow Hook(ServiceRow r)
        {
            r.TypeChanged += ShowTypeFields;
            return r;
        }

        private ServiceRow? Selected => List.SelectedItem as ServiceRow;

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ShowSelected();
        }

        private void ShowSelected()
        {
            var r = Selected;
            Form.DataContext = r;
            var has = r != null;
            NoService.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            ServiceForm.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            Dispatcher.BeginInvoke(FormScroll.ScrollToHome, System.Windows.Threading.DispatcherPriority.Loaded);
            if (has)
                ShowTypeFields();
        }

        private void ShowTypeFields()
        {
            if (Selected is not { } r)
                return;
            NpmFields.Visibility = r.Type == "npm" ? Visibility.Visible : Visibility.Collapsed;
            CommandFields.Visibility = r.Type == "command" ? Visibility.Visible : Visibility.Collapsed;
            ArgsHint.Text = r.Type switch
            {
                "dotnet" => "Extra arguments for your app, after --. Usually empty.",
                "npm" => "Extra arguments for the script, after --. Usually empty.",
                _ => "Arguments for the command, e.g. server.js --verbose",
            };
        }

        private void DotnetMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            UpdateModeFields();
        }

        private void UpdateModeFields()
        {
            var managed = DotnetMode.SelectedItem as string == "managed";
            Solution.IsEnabled = managed;
            AutoRebuild.IsEnabled = managed;
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            var n = _rows.Count + 1;
            var key = "service";
            while (_rows.Any(r => r.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                key = $"service{n++}";
            var used = _rows.Select(r => r.Color).ToHashSet();
            var row = Hook(new ServiceRow(new ServiceDefinition
            {
                Key = key,
                Name = "New service",
                Dir = ".",
                Type = "dotnet",
                Color = Project.Colors.FirstOrDefault(c => !used.Contains(c)) ?? "cyan",
            }));
            _rows.Add(row);
            List.SelectedItem = row;
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not { } r)
                return;
            if (!ChoiceWindow.Confirm($"Remove {r.Title} from this project?", "", "Remove"))
                return;
            var i = _rows.IndexOf(r);
            _rows.Remove(r);
            List.SelectedIndex = Math.Min(i, _rows.Count - 1);
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            MoveSelected(-1);
        }

        private void Down_Click(object sender, RoutedEventArgs e)
        {
            MoveSelected(+1);
        }

        private void MoveSelected(int delta)
        {
            if (Selected is not { } r)
                return;
            int i = _rows.IndexOf(r), j = i + delta;
            if (j < 0 || j >= _rows.Count)
                return;
            _rows.Move(i, j);
            List.SelectedItem = r;
        }

        private void BrowseDir_Click(object sender, RoutedEventArgs e)
        {
            if (Selected is not { } r)
                return;
            var dlg = new OpenFolderDialog { Title = "The service's folder", InitialDirectory = _project.RootDir };
            if (dlg.ShowDialog(this) != true)
                return;
            var rel = Path.GetRelativePath(_project.RootDir, dlg.FolderName).Replace('\\', '/');
            r.Dir = rel.Length == 0 ? "." : rel;
            if (string.IsNullOrWhiteSpace(r.Name) || r.Name == "New service")
                r.Name = Path.GetFileName(dlg.FolderName.TrimEnd('\\'));
            if (r.Key.StartsWith("service"))
                r.Key = Path.GetFileName(dlg.FolderName.TrimEnd('\\')).Split('.').Last().ToLowerInvariant();
            GuessType(r, dlg.FolderName);
        }

        private static void GuessType(ServiceRow r, string folder)
        {
            try
            {
                if (Directory.GetFiles(folder, "*.csproj").Length > 0)
                    r.Type = "dotnet";
                else if (File.Exists(Path.Combine(folder, "package.json")))
                    r.Type = "npm";
            }
            catch { }
        }

        private void BrowseSolution_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "The solution to build",
                InitialDirectory = _project.RootDir,
                Filter = "Solutions (*.sln;*.slnx)|*.sln;*.slnx|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(this) != true)
                return;
            Solution.Text = Path.GetRelativePath(_project.RootDir, dlg.FileName).Replace('\\', '/');
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = ProjectName.Text.Trim();
            if (name.Length == 0)
            {
                Status.Text = "The project needs a name.";
                return;
            }
            if (!int.TryParse(MaxRetries.Text.Trim(), out var retries) || retries < 1)
            {
                Status.Text = "Restart attempts must be a number, 1 or more.";
                return;
            }
            if (_rows.FirstOrDefault(r => r.PortInvalid) is { } badPort)
            {
                Status.Text = $"{badPort.Title}: the port must be a number.";
                List.SelectedItem = badPort;
                return;
            }

            _def.DotnetMode = DotnetMode.SelectedItem as string ?? "watch";
            _def.Solution = Solution.Text;
            _def.AutoRetry = AutoRetry.IsChecked == true;
            _def.MaxRetries = retries;
            _def.AutoRebuild = AutoRebuild.IsChecked == true;
            _def.Services = _rows.Select(r => r.D).ToList();

            var problems = Project.Validate(_def);
            if (problems.Count > 0)
            {
                Status.Text = string.Join(Environment.NewLine, problems);
                return;
            }

            try
            {
                _project.SaveDefinition(_def);
                if (name != _vm.Name && !_vm.TryRename(name))
                {
                    Status.Text = $"There's already a project called '{name}'.";
                    return;
                }
            }
            catch (Exception ex)
            {
                Status.Text = ex.Message;
                return;
            }
            Saved = true;
            _unsaved.Saved = true;
            Close();
        }
    }
}
