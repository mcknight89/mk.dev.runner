using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using mk.dev.runner.core;
using mk.dev.runner.desktop.ViewModels;

namespace mk.dev.runner.desktop
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            _vm = vm;
            RestoreLayout();
            IsVisibleChanged += (_, _) =>
            {
                if (!IsVisible)
                    SaveLayout();
            };
            Closing += (_, _) => SaveLayout();
            InputBindings.Add(new KeyBinding(Log.FindCommand, Key.F, ModifierKeys.Control));
            PreviewKeyDown += (_, e) => e.Handled = Shortcut(vm, e);
            Title = App.AppName;
            AppTitle.Text = App.AppName;
            var icon = Icons.Make(null, 48);
            Icon = Icons.ToImageSource(icon);
        }

        public void ShowStopping()
        {
            Stopping.Visibility = Visibility.Visible;
        }

        private void More_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { ContextMenu: { } menu } b)
            {
                menu.PlacementTarget = b;
                menu.DataContext = b.DataContext;
                menu.IsOpen = true;
            }
        }

        private static bool Shortcut(MainViewModel vm, KeyEventArgs e)
        {
            if (e.Key != Key.F5 || vm.Selected is not { } project)
                return false;
            var command = Keyboard.Modifiers switch
            {
                ModifierKeys.None => project.StartAll,
                ModifierKeys.Shift => project.StopAll,
                ModifierKeys.Control | ModifierKeys.Shift => project.RestartAll,
                _ => null,
            };
            if (command == null || !command.CanExecute(null))
                return false;
            command.Execute(null);
            return true;
        }

        private void RestoreLayout()
        {
            if (_vm.Settings.Window is not { } w || w.Width < MinWidth || w.Height < MinHeight)
                return;
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            if (!screen.IntersectsWith(new Rect(w.Left, w.Top, w.Width, w.Height)))
                return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = w.Left;
            Top = w.Top;
            Width = w.Width;
            Height = w.Height;
            if (w.Maximized)
                WindowState = WindowState.Maximized;
            if (w.CardsHeight > 0)
                CardsRow.Height = new GridLength(w.CardsHeight);
        }

        private void SaveLayout()
        {
            var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (bounds.IsEmpty)
                return;
            _vm.Settings.Window = new WindowLayout
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = WindowState == WindowState.Maximized,
                CardsHeight = CardsRow.Height.IsAbsolute ? CardsRow.Height.Value : 0,
            };
            _vm.SaveSettings();
        }
    }
}
