using System.Windows;
using mk.dev.runner.core;
using mk.dev.runner.desktop.ViewModels;

namespace mk.dev.runner.desktop.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly MainViewModel _vm;

        public SettingsWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            var s = vm.Settings;
            StartWithWindows.IsChecked = s.StartWithWindows;
            StartMinimized.IsChecked = s.StartMinimized;
            CloseToTray.IsChecked = s.CloseToTray;
            Notifications.IsChecked = s.Notifications;
            CheckForUpdates.IsChecked = s.CheckForUpdates;
            DevChannel.IsChecked = vm.Updates.WantsDev;
            StartMinimized.IsEnabled = s.StartWithWindows;
            foreach (var box in new[] { StartWithWindows, StartMinimized, CloseToTray, Notifications, CheckForUpdates, DevChannel })
            {
                box.Checked += Changed;
                box.Unchecked += Changed;
            }

            var u = vm.Updates;
            Version.Text = $"Version {u.CurrentVersion}" + (u.IsInstalled ? "" : " (not installed)");
            CheckNow.IsEnabled = u.IsInstalled;
            UpdateStatus.Text = u.Status;
            u.PropertyChanged += OnUpdates;
            Closed += (_, _) => u.PropertyChanged -= OnUpdates;
        }

        private void OnUpdates(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdateStatus.Text = _vm.Updates.Status;
            UpdateStatus.ToolTip = string.IsNullOrEmpty(_vm.Updates.StatusDetail) ? null : _vm.Updates.StatusDetail;
            CheckNow.IsEnabled = _vm.Updates.IsInstalled && !_vm.Updates.Busy;
        }

        private async void CheckNow_Click(object sender, RoutedEventArgs e)
        {
            await _vm.Updates.CheckAsync(manual: true);
        }

        private void Changed(object sender, RoutedEventArgs e)
        {
            var s = _vm.Settings;
            if (sender == StartWithWindows)
            {
                try
                {
                    StartupRegistration.Apply(StartWithWindows.IsChecked == true);
                }
                catch (Exception ex)
                {
                    ChoiceWindow.Problem("Start with Windows couldn't be changed", ex.Message);
                    StartWithWindows.IsChecked = s.StartWithWindows;
                    return;
                }
                s.StartWithWindows = StartWithWindows.IsChecked == true;
                StartMinimized.IsEnabled = s.StartWithWindows;
            }
            s.StartMinimized = StartMinimized.IsChecked == true;
            s.CloseToTray = CloseToTray.IsChecked == true;
            s.Notifications = Notifications.IsChecked == true;
            s.CheckForUpdates = CheckForUpdates.IsChecked == true;
            var channel = DevChannel.IsChecked == true ? "dev" : "stable";
            if (channel != (_vm.Updates.WantsDev ? "dev" : "stable"))
            {
                s.UpdateChannel = channel;
                _vm.Updates.ChannelChanged();
            }
            _vm.SaveSettings();
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            Shell.Open(AppSettings.Dir);
        }
    }
}
