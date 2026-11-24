using System.Windows;
using System.Windows.Controls;

namespace mk.dev.runner.desktop.Views
{
    public sealed class ChoiceWindow : Window
    {
        private int _choice = -1;

        private ChoiceWindow(string title, string message, string[] buttons)
        {
            Title = title;
            Style = (Style)Application.Current.FindResource("DarkWindow");
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            MaxWidth = 560;

            var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < buttons.Length; i++)
            {
                var n = i;
                var b = new Button { Content = buttons[i], Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 5, 14, 5) };
                if (i == 0)
                {
                    b.Style = (Style)Application.Current.FindResource("Primary");
                    b.IsDefault = true;
                }
                b.Click += (_, _) => { _choice = n; Close(); };
                row.Children.Add(b);
            }
            row.Children.Add(new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 5, 14, 5) });
            Content = new StackPanel { Margin = new Thickness(20), Children = { text, row } };
        }

        public static int Ask(string title, string message, params string[] buttons)
        {
            var w = new ChoiceWindow(title, message, buttons) { Owner = Application.Current.MainWindow };
            w.ShowDialog();
            return w._choice;
        }
    }
}
