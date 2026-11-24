using System.Windows;

namespace mk.dev.runner.desktop.Views
{
    public partial class TextPromptWindow : Window
    {
        private TextPromptWindow(string title, string prompt, string value, string ok)
        {
            InitializeComponent();
            Title = title;
            Heading.Text = title;
            Prompt.Text = prompt;
            Value.Text = value;
            OkButton.Content = ok;
            Loaded += (_, _) => { Value.Focus(); Value.SelectAll(); };
        }

        public static string? Ask(Window? owner, string title, string prompt, string value, string ok = "Save")
        {
            var w = new TextPromptWindow(title, prompt, value, ok) { Owner = owner };
            return w.ShowDialog() == true && w.Value.Text.Trim() is { Length: > 0 } v ? v : null;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
