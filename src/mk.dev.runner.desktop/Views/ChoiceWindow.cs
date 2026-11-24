using System.Windows;
using System.Windows.Controls;

namespace mk.dev.runner.desktop.Views
{
    public sealed class ChoiceWindow : Window
    {
        private int _choice = -1;

        private ChoiceWindow(string heading, string message, string[] buttons, bool cancel, bool problem)
        {
            Title = App.AppName;
            Style = (Style)Application.Current.FindResource("DarkWindow");
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MaxWidth = 560;
            MinWidth = 340;

            var body = new StackPanel { Margin = new Thickness(20) };
            if (heading.Length > 0)
            {
                body.Children.Add(new TextBlock
                {
                    Text = heading,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = problem ? Palette.Brush(Palette.Bad) : (System.Windows.Media.Brush)Application.Current.FindResource("Text"),
                    Margin = new Thickness(0, 0, 0, 8),
                });
            }
            if (message.Length > 0)
                body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Muted") });

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            for (int i = 0; i < buttons.Length; i++)
            {
                var n = i;
                var b = new Button { Content = buttons[i], Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 5, 14, 5) };
                if (i == 0)
                {
                    b.Style = (Style)Application.Current.FindResource("Primary");
                    b.IsDefault = true;
                    Loaded += (_, _) => b.Focus();
                }
                b.Click += (_, _) => { _choice = n; Close(); };
                row.Children.Add(b);
            }
            if (cancel)
                row.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 5, 14, 5) });
            body.Children.Add(row);
            Content = body;
        }

        public static int Ask(string heading, string message, params string[] buttons)
        {
            return Show(heading, message, buttons, cancel: true, problem: false);
        }

        public static bool Confirm(string heading, string message, string yes)
        {
            return Show(heading, message, [yes], cancel: true, problem: false) == 0;
        }

        public static void Tell(string heading, string message = "")
        {
            Show(heading, message, ["OK"], cancel: false, problem: false);
        }

        public static void Problem(string heading, string detail = "")
        {
            Show(heading, detail, ["OK"], cancel: false, problem: true);
        }

        private static int Show(string heading, string message, string[] buttons, bool cancel, bool problem)
        {
            var w = new ChoiceWindow(heading, message, buttons, cancel, problem) { Owner = ActiveWindow() };
            if (w.Owner == null)
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ShowDialog();
            return w._choice;
        }

        private static Window? ActiveWindow()
        {
            var windows = Application.Current.Windows.OfType<Window>().Where(x => x.IsVisible).ToList();
            return windows.FirstOrDefault(x => x.IsActive) ?? windows.FirstOrDefault(x => x == Application.Current.MainWindow) ?? windows.FirstOrDefault();
        }
    }
}
