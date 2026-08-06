using System.Windows.Input;

namespace CloudKeeperSN.App.ViewModels;

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ObservableObject, ICommand, IDisposable
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private CancellationTokenSource? _cancellation;
    private bool _isRunning;

    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public bool CanExecute(object? parameter) => !IsRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _cancellation = new CancellationTokenSource();
        IsRunning = true;
        NotifyCanExecuteChanged();
        try
        {
            await _execute(_cancellation.Token);
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            IsRunning = false;
            NotifyCanExecuteChanged();
        }
    }

    public void Cancel() => _cancellation?.Cancel();
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
    }
}

public sealed class AsyncParameterRelayCommand<T>(Func<T, CancellationToken, Task> execute) : ICommand, IDisposable where T : class
{
    private CancellationTokenSource? _cancellation;
    private bool _isRunning;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_isRunning && parameter is T;
    public async void Execute(object? parameter)
    {
        if (parameter is not T value || !CanExecute(parameter)) return;
        _cancellation = new CancellationTokenSource();
        _isRunning = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(value, _cancellation.Token); }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested) { }
        finally { _cancellation.Dispose(); _cancellation = null; _isRunning = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
    public void Dispose() { _cancellation?.Cancel(); _cancellation?.Dispose(); }
}
