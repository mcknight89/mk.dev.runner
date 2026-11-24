using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using mk.dev.runner.core;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed partial class ProjectViewModel
    {
        public void DoEditProject()
        {
            if (Runner == null)
                return;
            if (Runner.SessionActive)
            {
                MessageBox.Show(Application.Current.MainWindow, $"Stop {Name} first (Stop all), then edit it.", "Edit project",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var w = new Views.ProjectEditorWindow(this) { Owner = Application.Current.MainWindow };
            w.ShowDialog();
            if (!w.Saved)
                return;
            try { Runner.ReloadProject(); }
            catch (Exception e) { MessageBox.Show(e.Message, "Couldn't reload the project"); }
            BuildServices();
        }

        public bool TryRename(string name)
        {
            if (_main.Projects.Any(p => p != this && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                return false;
            Entry.Name = name;
            Name = name;
            Runner?.Project.SetDisplayName(name);
            Raise(nameof(Name));
            _main.SaveSettings();
            return true;
        }

        private void DoRename()
        {
            var name = Views.TextPromptWindow.Ask(Application.Current.MainWindow, "Rename project",
                "What the runner calls it. Only the list's name changes: nothing in the project's folder does.", Name);
            if (name == null || name == Name)
                return;
            if (!TryRename(name))
                MessageBox.Show($"There's already a project called '{name}'.", "Rename project");
        }

        private void ReloadConfig()
        {
            if (Runner == null) { Reattach(); return; }
            try
            {
                if (!Runner.ReloadProject())
                    MessageBox.Show("Stop the project before reloading its config.", "Dev Runner");
            }
            catch (Exception e) { MessageBox.Show(e.Message, "Couldn't reload config"); }
        }

        public void RunGuarded(Action action)
        {
            RunBackground(action, "Starting…");
        }

        private void RunBackground(Action action, string text)
        {
            _busy = true;
            BusyText = text;
            CommandManagerInvalidate();
            Task.Run(() =>
            {
                try { action(); }
                catch (Exception e)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                        MessageBox.Show(Application.Current.MainWindow, e.Message, $"{Name} — couldn't start", MessageBoxButton.OK, MessageBoxImage.Warning));
                }
                finally
                {
                    Application.Current.Dispatcher.BeginInvoke(() => { _busy = false; BusyText = ""; CommandManagerInvalidate(); });
                }
            });
        }

        private static void CommandManagerInvalidate()
        {
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
}
