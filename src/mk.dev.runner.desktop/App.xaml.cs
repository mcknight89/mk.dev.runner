using System.Windows;
using mk.dev.runner.desktop.ViewModels;
using Velopack;

namespace mk.dev.runner.desktop;

public partial class App : Application
{
    const string Instance = "mk.dev.runner.desktop";
    static readonly string MutexName = Instance;
    static readonly string ShowEventName = Instance + ".show";

    /// <summary>Started again by an update: leave projects stopped (no auto-start).</summary>
    static bool _restartedAfterUpdate;

    /// <summary>
    /// Velopack runs first: during install, update and uninstall it handles its own steps and exits here.
    /// Uninstall also removes the Start with Windows entry.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => { try { StartupRegistration.Apply(false); } catch { } })
            .OnRestarted(_ => _restartedAfterUpdate = true)
            .Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    Mutex? _single;
    EventWaitHandle? _showEvent;
    MainViewModel? _vm;
    MainWindow? _window;
    Tray? _tray;
    bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One copy only: a second launch just brings the first one's window up.
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
        }) { IsBackground = true };
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
            if (_exiting) return;
            if (_vm.Settings.CloseToTray) { ce.Cancel = true; HideWindow(); TellStillRunning(); }
            else { ce.Cancel = true; ExitApp(); }
        };
        _window.StateChanged += (_, _) =>
        {
            if (_window.WindowState == WindowState.Minimized && _vm.Settings.CloseToTray) { HideWindow(); TellStillRunning(); }
        };
        MainWindow = _window;

        _tray = new Tray(_vm, ShowWindow, ExitApp);

        bool fromStartup = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (fromStartup && _vm.Settings.StartMinimized) _vm.WindowVisible = false;
        else ShowWindow();

        if (!_restartedAfterUpdate) _vm.AutoStartProjects();
        _vm.Updates.Start();
    }

    void ShowWindow()
    {
        if (_window == null) return;
        _vm!.WindowVisible = true;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true; _window.Topmost = false;
        _window.Focus();
    }

    void HideWindow()
    {
        _vm!.WindowVisible = false;
        _window?.Hide();
    }

    /// <summary>
    /// After ✕ (or minimise) hides the window: say the app is still in the tray. Every time while services
    /// are running; once ever when nothing is.
    /// </summary>
    void TellStillRunning()
    {
        if (_vm == null || _tray == null) return;
        int running = _vm.Projects.Sum(p => p.Services.Count(s => s.IsActive));
        if (running > 0)
        {
            _tray.Tip("Dev Runner is still running",
                $"{running} service{(running == 1 ? " keeps" : "s keep")} running. To stop everything, right-click the tray icon and choose Exit.");
            return;
        }
        if (_vm.Settings.TrayHintShown) return;
        _vm.Settings.TrayHintShown = true;
        _vm.SaveSettings();
        _tray.Tip("Dev Runner is still in the tray",
            "Closing the window keeps it here. Right-click the tray icon and choose Exit to quit, or turn this off in Settings.");
    }

    void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _window?.ShowStopping();
        // Last resort: never leave a half-exited app sitting in the background.
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
