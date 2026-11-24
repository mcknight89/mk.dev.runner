using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using mk.dev.runner.core;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed partial class MainViewModel : Observable, IDisposable
    {
        private readonly DispatcherTimer _timer;
        private int _tick;

        public MainViewModel()
        {
            Settings = AppSettings.Load();
            if (Settings.LoadProblem is { } problem)
                Application.Current?.Dispatcher.BeginInvoke(() => MessageBox.Show(problem, "Dev runner settings", MessageBoxButton.OK, MessageBoxImage.Warning));
            foreach (var lp in Settings.LoadProjects()) Projects.Add(new ProjectViewModel(this, lp));
            Selected = Projects.FirstOrDefault();

            AddProject = new Command(DoAddProject);
            ImportFolder = new Command(DoImportFolder);
            RemoveProject = new Command(DoRemoveProject, () => Selected != null && Selected.Runner?.SessionActive != true);
            OpenSettings = new Command(() => new Views.SettingsWindow(this) { Owner = Application.Current.MainWindow }.ShowDialog());

            Updates = new Updates.Updater(Settings);
            InstallUpdate = new Command(async () =>
            {
                var running = Projects.Sum(p => p.Services.Count(s => s.IsActive));
                if (running > 0 && MessageBox.Show(Application.Current.MainWindow,
                        $"Updating restarts Dev Runner and stops the {running} running service{(running == 1 ? "" : "s")}. They stay stopped afterwards.\n\nUpdate to {Updates.Available} now?",
                        "Update Dev Runner", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                await Updates.InstallAsync(() => Task.Run(StopEverything));
            }, () => Updates.HasUpdate && !Updates.Busy);

            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
            WatchSettingsFile();
        }

        private FileSystemWatcher? _settingsWatcher;
        private DispatcherTimer? _settingsDebounce;

        private void Refresh()
        {
            _tick++;
            foreach (var p in Projects) p.Refresh(WindowVisible && p == Selected);
            if (_tick % 4 == 0)
            {
                var active = Projects.Where(p => p.IsActive).ToList();
                var mem = active.Sum(p => p.Runner?.TotalWorkingSet ?? 0);
                var text = active.Count == 0 ? "Dev Runner — nothing running"
                    : $"Dev Runner — {string.Join(", ", active.Select(p => p.Name))} · {Format.Bytes(mem)}";
                SummaryChanged?.Invoke(text, active.Count > 0, Projects.Any(p => p.HasProblem));
            }
        }

        public void OnNotice(ProjectViewModel p, RunnerNotice n)
        {
            if (!Settings.Notifications || !n.IsError)
                return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
                NoticeRaised?.Invoke(n.Service == null ? p.Name : $"{p.Name} · {n.Service}", n.Message, n.IsError));
        }

        public void SaveSettings()
        {
            try { Settings.Save(); }
            catch (Exception e) { MessageBox.Show(e.Message, "Couldn't save settings"); }
        }

        public void AutoStartProjects()
        {
            foreach (var p in Projects.Where(p => p.Entry.AutoStart && p.Runner != null)) p.StartAll.Execute(null);
        }

        public void OpenEnvEditor(ProjectViewModel p)
        {
            if (p.Runner == null)
                return;
            var w = new Views.EnvEditorWindow(p) { Owner = Application.Current.MainWindow };
            w.ShowDialog();
        }

        public void StopEverything()
        {
            _timer.Stop();
            Parallel.ForEach(Projects.Where(p => p.Runner?.SessionActive == true).ToList(), p =>
            {
                try { p.Runner!.StopAll(); } catch { }
            });
        }

        public void Dispose()
        {
            _timer.Stop();
            _settingsWatcher?.Dispose();
            foreach (var p in Projects) p.Dispose();
        }
    }
}
