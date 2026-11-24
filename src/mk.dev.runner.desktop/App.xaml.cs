using System.Windows;
using mk.dev.runner.core;
using mk.dev.runner.desktop.ViewModels;
using Velopack;

namespace mk.dev.runner.desktop
{
    public partial class App : Application
    {
        private const string Instance = "mk.dev.runner.desktop";

        public static bool IsDev { get; private set; }
        public static string AppName => IsDev ? "Dev Runner DEV" : "Dev Runner";
        private static string MutexName => IsDev ? Instance + ".dev" : Instance;
        private static string ShowEventName => MutexName + ".show";

        private static bool _restartedAfterUpdate;

        [STAThread]
        private static void Main(string[] args)
        {
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ => { try { StartupRegistration.Apply(false); } catch { } })
                .OnRestarted(_ => _restartedAfterUpdate = true)
                .Run();
            IsDev = !Updates.Updater.Installed();
            if (IsDev)
                UseDevSettings();
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }

        private static void UseDevSettings()
        {
            AppPaths.SettingsName = AppPaths.FolderName + ".dev";
            if (File.Exists(AppSettings.FilePath))
                return;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "sample", ".dev.json")))
                dir = dir.Parent;
            if (dir == null)
                return;
            try
            {
                var settings = new AppSettings();
                settings.Add(Path.Combine(dir.FullName, "sample"), "Sample");
                settings.Save();
            }
            catch (Exception e)
            {
                MessageBox.Show($"Couldn't add the sample project: {e.Message}", AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private Mutex? _single;
        private EventWaitHandle? _showEvent;
        private MainViewModel? _vm;
        private MainWindow? _window;
        private Tray? _tray;
        private bool _exiting;
        private bool _toldRunning;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DarkTitleBar.ForAllWindows();

            _single = new Mutex(true, MutexName, out var first);
            if (!first)
            {
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                Shutdown();
                return;
            }
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var listener = new Thread(() =>
            {
                while (_showEvent.WaitOne()) Dispatcher.BeginInvoke(ShowWindow);
            })
            { IsBackground = true };
            listener.Start();

            DispatcherUnhandledException += (_, ex) =>
            {
                MessageBox.Show(ex.Exception.Message, "Dev Runner — unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            _vm = new MainViewModel();
            StartupRegistration.KeepPathCurrent(_vm.Settings.StartWithWindows);
            _window = new MainWindow(_vm);
            _window.Closing += (_, ce) =>
            {
                if (_exiting)
                    return;
                ce.Cancel = true;
                if (_vm.Settings.CloseToTray)
                {
                    HideWindow();
                    TellStillRunning();
                    return;
                }
                var running = _vm.Projects.Sum(p => p.Services.Count(s => s.IsActive));
                if (running > 0 && MessageBox.Show(_window, $"Closing stops {running} running service{(running == 1 ? "" : "s")}. Close anyway?", AppName,
                        MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
                ExitApp();
            };
            _window.StateChanged += (_, _) =>
            {
                if (_window.WindowState == WindowState.Minimized && _vm.Settings.CloseToTray) { HideWindow(); TellStillRunning(); }
            };
            MainWindow = _window;

            _tray = new Tray(_vm, ShowWindow, ExitApp);

            var fromStartup = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            if (fromStartup && _vm.Settings.StartMinimized)
                _vm.WindowVisible = false;
            else
                ShowWindow();

            if (!_restartedAfterUpdate && !IsDev)
                _vm.AutoStartProjects();
            _vm.Updates.Start();
        }

        private void ShowWindow()
        {
            if (_window == null)
                return;
            _vm!.WindowVisible = true;
            if (!_window.IsVisible)
                _window.Show();
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.Topmost = true; _window.Topmost = false;
            _window.Focus();
        }

        private void HideWindow()
        {
            _vm!.WindowVisible = false;
            _window?.Hide();
        }

        private void TellStillRunning()
        {
            if (_vm == null || _tray == null)
                return;
            var running = _vm.Projects.Sum(p => p.Services.Count(s => s.IsActive));
            if (running > 0)
            {
                if (_toldRunning)
                    return;
                _toldRunning = true;
                _tray.Tip("Dev Runner is still running",
                    $"{running} service{(running == 1 ? " keeps" : "s keep")} running. To stop everything, right-click the tray icon and choose Exit.");
                return;
            }
            if (_vm.Settings.TrayHintShown)
                return;
            _vm.Settings.TrayHintShown = true;
            _vm.SaveSettings();
            _tray.Tip("Dev Runner is still in the tray",
                "Closing the window keeps it here. Right-click the tray icon and choose Exit to quit, or turn this off in Settings.");
        }

        private void ExitApp()
        {
            if (_exiting)
                return;
            _exiting = true;
            _window?.ShowStopping();
            new Thread(() => { Thread.Sleep(60_000); Environment.Exit(0); }) { IsBackground = true }.Start();
            Task.Run(() =>
            {
                try { _vm?.StopEverything(); }
                catch { }
                finally
                {
                    Dispatcher.BeginInvoke(() =>
                    {
                        try { _tray?.Dispose(); _vm?.Dispose(); _window?.Close(); }
                        catch { }
                        finally { Shutdown(); }
                    });
                }
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            try { _single?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }
    }
}
