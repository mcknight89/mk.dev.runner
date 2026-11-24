using System.Windows;

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
                Views.ChoiceWindow.Tell($"Stop {Name} first", "Use Stop all, then edit it.");
                return;
            }
            var w = new Views.ProjectEditorWindow(this) { Owner = Application.Current.MainWindow };
            w.ShowDialog();
            if (!w.Saved)
                return;
            try
            {
                Runner.ReloadProject();
            }
            catch (Exception e)
            {
                Views.ChoiceWindow.Problem("The project couldn't be reloaded", e.Message);
            }
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
                Views.ChoiceWindow.Tell($"There's already a project called {name}", "Pick another name.");
        }

        private void ReloadConfig()
        {
            if (Runner == null)
            {
                Reattach();
                return;
            }
            try
            {
                if (!Runner.ReloadProject())
                    Views.ChoiceWindow.Tell("Stop the project first", "Then reload its config.");
            }
            catch (Exception e)
            {
                Views.ChoiceWindow.Problem("The config couldn't be reloaded", e.Message);
            }
        }

        public void RunBackground(Action action, string text)
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
                        Views.ChoiceWindow.Problem($"{Name} couldn't start", e.Message));
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
