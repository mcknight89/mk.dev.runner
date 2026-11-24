using System.Windows;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed partial class ProjectViewModel
    {
        public void DoEditProject()
        {
            EditService(null);
        }

        public void EditService(string? key)
        {
            if (Runner is not { } runner)
                return;
            var w = new Views.ProjectEditorWindow(this, key) { Owner = Application.Current.MainWindow };
            w.ShowDialog();
            if (!w.Saved)
                return;
            RunBackground(runner.ReloadProject, runner.SessionActive ? "Applying changes…" : "", "The changes couldn't be applied");
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
                Views.ChoiceWindow.Tell($"There's already a project called {name}", "Pick another name.");
        }

        private void ReloadConfig()
        {
            if (Runner == null)
            {
                Reattach();
                return;
            }
            RunBackground(Runner.ReloadProject, Runner.SessionActive ? "Reloading…" : "", "The config couldn't be reloaded");
        }

        public void RunBackground(Action action, string text, string? failTitle = null)
        {
            _busy = true;
            BusyText = text;
            CommandManagerInvalidate();
            Task.Run(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Application.Current.Dispatcher.BeginInvoke(() =>
                        Views.ChoiceWindow.Problem(failTitle ?? $"{Name} couldn't start", e.Message));
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
