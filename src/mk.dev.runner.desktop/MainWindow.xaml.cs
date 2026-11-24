using System.Windows;
using System.Windows.Controls;
using mk.dev.runner.desktop.ViewModels;

namespace mk.dev.runner.desktop
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            var icon = Icons.Make(System.Drawing.Color.FromArgb(0x60, 0xa5, 0xfa), 48);
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

        private void CopyLog_Click(object sender, RoutedEventArgs e)
        {
            var text = Log.AllText();
            if (text.Length > 0)
                Clipboard.SetText(text);
        }

        private void Bottom_Click(object sender, RoutedEventArgs e)
        {
            Log.ScrollToEnd();
        }
    }
}
