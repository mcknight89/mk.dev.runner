using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using mk.dev.runner.core;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed partial class MainViewModel
    {
        private void WatchSettingsFile()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Dir);
                _settingsDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _settingsDebounce.Tick += (_, _) => { _settingsDebounce.Stop(); SyncFromDisk(); };
                _settingsWatcher = new FileSystemWatcher(AppSettings.Dir, Path.GetFileName(AppSettings.FilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                };
                FileSystemEventHandler h = (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => { _settingsDebounce.Stop(); _settingsDebounce.Start(); });
                _settingsWatcher.Changed += h;
                _settingsWatcher.Created += h;
                _settingsWatcher.Renamed += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => { _settingsDebounce.Stop(); _settingsDebounce.Start(); });
                _settingsWatcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        private void SyncFromDisk()
        {
            if (!File.Exists(AppSettings.FilePath))
                return;
            var fresh = AppSettings.Load();
            static string Key(ProjectEntry e) { try { return Path.GetFullPath(e.ConfigFile).ToLowerInvariant(); } catch { return e.ConfigFile.ToLowerInvariant(); } }
            var freshKeys = fresh.Projects.Select(Key).ToHashSet();
            var ourKeys = Settings.Projects.Select(Key).ToHashSet();

            foreach (var e in fresh.Projects.Where(e => !ourKeys.Contains(Key(e))))
            {
                Settings.Projects.Add(e);
                Projects.Add(new ProjectViewModel(this, AppSettings.LoadOne(e)));
            }
            foreach (var vm in Projects.Where(p => !freshKeys.Contains(Key(p.Entry)) && p.Runner?.SessionActive != true).ToList())
            {
                Settings.Projects.Remove(vm.Entry);
                Projects.Remove(vm);
                vm.Dispose();
            }
            foreach (var e in fresh.Projects)
                if (Settings.Projects.FirstOrDefault(o => Key(o) == Key(e)) is { } mine)
                    mine.AutoStart = e.AutoStart;

            Settings.StartWithWindows = fresh.StartWithWindows;
            Settings.StartMinimized = fresh.StartMinimized;
            Settings.CloseToTray = fresh.CloseToTray;
            Settings.Notifications = fresh.Notifications;
            Settings.TrayHintShown = fresh.TrayHintShown;
            Settings.UpdateChannel = fresh.UpdateChannel;
            Settings.CheckForUpdates = fresh.CheckForUpdates;

            if (Selected == null || !Projects.Contains(Selected))
                Selected = Projects.FirstOrDefault();
            Raise(nameof(HasNoProjects));
        }

        public AppSettings Settings { get; }
        public ObservableCollection<ProjectViewModel> Projects { get; } = [];

        private ProjectViewModel? _selected;
        public ProjectViewModel? Selected
        {
            get => _selected;
            set { if (Set(ref _selected, value)) { Raise(nameof(HasSelection)); value?.SelectedLog?.Pull(); } }
        }
        public bool HasSelection => Selected != null;
        public bool HasNoProjects => Projects.Count == 0;

        public Command AddProject { get; }
        public Command ImportFolder { get; }
        public Command RemoveProject { get; }
        public Command OpenSettings { get; }
        public Command InstallUpdate { get; }
        public Updates.Updater Updates { get; }

        public bool WindowVisible { get; set; } = true;

        public event Action<string, string, bool>? NoticeRaised;
        public event Action<string, bool, bool>? SummaryChanged;

        private void DoAddProject()
        {
            var dlg = new OpenFolderDialog { Title = "Add a project folder" };
            if (dlg.ShowDialog() != true)
                return;
            var folder = dlg.FolderName;
            try
            {
                Project project;
                bool openEditor = false;
                try { project = Settings.Add(folder); }
                catch (ConfigNotFoundException nf)
                {
                    string? file = null;
                    if (nf.Candidates.Count == 0)
                    {
                        var pick = ChoiceAsk($"There's no config file in {folder}.\n\nPick the config file if it has another name, or create {Project.FolderConfigName} and fill in the services.",
                                             "Pick the config file…", $"Create {Project.FolderConfigName}");
                        if (pick == 1)
                        {
                            file = Project.CreateNew(folder);
                            openEditor = true;
                        }
                        else if (pick == 0)
                            file = PickConfig(folder);
                    }
                    else
                    {
                        var names = string.Join("\n", nf.Candidates.Select(c => "  " + Path.GetFileName(c)));
                        if (ChoiceAsk($"{folder} has more than one config file:\n\n{names}\n\nPick the one to use.", "Pick the config file…") == 0)
                            file = PickConfig(folder);
                    }
                    if (file == null)
                        return;
                    project = Settings.Add(file);
                }
                SaveSettings();
                AddLoaded(project);
                if (openEditor)
                    Selected?.DoEditProject();
            }
            catch (Exception e) { MessageBox.Show(e.Message, "Couldn't add project"); }
        }

        private static int ChoiceAsk(string message, params string[] buttons)
        {
            return Views.ChoiceWindow.Ask("Add project", message, buttons);
        }

        private static string? PickConfig(string folder)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Pick the project's config file",
                InitialDirectory = folder,
                Filter = "Config files (*.json)|*.json|All files (*.*)|*.*",
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        }

        private void DoImportFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Add every project config in a folder and its subfolders" };
            if (dlg.ShowDialog() != true)
                return;
            var (added, errors) = Settings.ImportFolder(dlg.FolderName);
            SaveSettings();
            foreach (var name in added)
            {
                var e = Settings.Find(name);
                if (e != null && Projects.All(p => p.Entry != e))
                    Projects.Add(new ProjectViewModel(this, AppSettings.LoadOne(e)));
            }
            Raise(nameof(HasNoProjects));
            if (added.Count > 0)
                Selected = Projects.Last();
            var msg = added.Count == 0 ? "No new project configs found in that folder or its subfolders." : $"Added {string.Join(", ", added)}.";
            if (errors.Count > 0)
                msg += "\n\nSkipped:\n" + string.Join("\n", errors);
            MessageBox.Show(msg, "Import folder");
        }

        private void AddLoaded(Project project)
        {
            var entry = Settings.Find(project.ConfigPath) ?? Settings.Find(project.Name)!;
            var existing = Projects.FirstOrDefault(p => p.Entry == entry);
            if (existing == null)
            {
                existing = new ProjectViewModel(this, new LoadedProject(entry, project, null));
                Projects.Add(existing);
            }
            Raise(nameof(HasNoProjects));
            Selected = existing;
        }

        private void DoRemoveProject()
        {
            if (Selected is not { } p)
                return;
            if (MessageBox.Show($"Remove '{p.Name}' from the list?\n\nIts config files are left where they are.", "Remove project",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            Settings.Projects.Remove(p.Entry);
            SaveSettings();
            Projects.Remove(p);
            p.Dispose();
            Selected = Projects.FirstOrDefault();
            Raise(nameof(HasNoProjects));
        }
    }
}
