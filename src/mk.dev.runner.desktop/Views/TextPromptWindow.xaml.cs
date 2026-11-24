using System.Windows;

namespace mk.dev.runner.desktop.Views;

/// <summary>Asks for one line of text (a project's name). Null when cancelled or left empty.</summary>
public partial class TextPromptWindow : Window
{
    TextPromptWindow(string title, string prompt, string value, string ok)
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

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
