using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace mk.dev.runner.desktop.Views
{
    internal sealed class UnsavedChanges
    {
        private readonly Window _window;
        private bool _watching;

        public UnsavedChanges(Window window, params Selector[] ignore)
        {
            _window = window;
            window.AddHandler(TextBoxBase.TextChangedEvent, new RoutedEventHandler((_, e) =>
            {
                if (e.OriginalSource is TextBox { IsKeyboardFocused: true })
                    Mark();
            }));
            window.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnToggle));
            window.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(OnToggle));
            window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(OnClick));
            window.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler((_, e) =>
            {
                if (e.OriginalSource is ComboBox combo && (combo.IsDropDownOpen || combo.IsKeyboardFocusWithin))
                    Mark();
            }));
            window.ContentRendered += (_, _) => _watching = true;
            window.Closing += OnClosing;
        }

        public bool Changed { get; private set; }
        public bool Saved { get; set; }

        public void Mark()
        {
            if (_watching)
                Changed = true;
        }

        private void OnToggle(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is CheckBox { Name: "Mask" })
                return;
            if (e.OriginalSource is FrameworkElement f && (f.IsMouseOver || f.IsKeyboardFocused))
                Mark();
        }

        private void OnClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Button { IsCancel: false, IsDefault: false } b && b.Content is string text
                && (text.StartsWith('+') || text.StartsWith("Delete") || text.StartsWith("Remove") || text.StartsWith("Paste") || text.StartsWith("Browse")
                    || text.EndsWith('⇄')))
                Mark();
            else if (e.OriginalSource is Button { ToolTip: string tip } && tip.StartsWith("Move"))
                Mark();
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!Changed || Saved)
                return;
            var answer = MessageBox.Show(_window, "You have changes that aren't saved. Close without saving them?", App.AppName,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                e.Cancel = true;
        }
    }
}
