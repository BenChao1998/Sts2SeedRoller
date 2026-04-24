using System;
using System.Threading.Tasks;
using System.Windows;
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
        await _executeAsync();
    }

    public void RaiseCanExecuteChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        dispatcher.Invoke(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
    }
}
