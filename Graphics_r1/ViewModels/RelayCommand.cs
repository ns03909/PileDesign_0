using System;
using System.Windows.Input;

namespace PileDesign.ViewModels
{
    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _action;
        private readonly Predicate<object?>? _can;
        public RelayCommand(Action<object?> action, Predicate<object?>? can = null)
        {
            _action = action; _can = can;
        }
        public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _action(parameter);
        public event EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}