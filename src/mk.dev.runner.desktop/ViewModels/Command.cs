using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed class Command : ICommand
    {
        private readonly Action<object?> _run;
        private readonly Func<object?, bool>? _can;

        public Command(Action<object?> run, Func<object?, bool>? can = null)
        {
            _run = run;
            _can = can;
        }

        public Command(Action run, Func<bool>? can = null) : this(_ => run(), can == null ? null : _ => can())
        {
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? p)
        {
            return _can?.Invoke(p) ?? true;
        }

        public void Execute(object? p)
        {
            _run(p);
        }
    }
}
