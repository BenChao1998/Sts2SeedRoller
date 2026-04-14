using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SeedUi.Commands;

internal sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<bool>? _canExecute;
    private readonly Func<Task> _executeAsync;

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        await _executeAsync().ConfigureAwait(false);
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
