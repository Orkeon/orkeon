using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace Orkeon.Studio.Wpf.ViewModels.Mvvm;

/// <summary>
/// Hand-written asynchronous <see cref="ICommand"/>. While the returned task is pending the command
/// reports <see cref="CanExecute"/> as <see langword="false"/>, so a click cannot start a second run.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isRunning;

    /// <summary>Creates a command from an asynchronous action that ignores the command parameter.</summary>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    /// <summary>Creates a command from an asynchronous action that receives the command parameter.</summary>
    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>Whether an invocation is still pending.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
                return;

            _isRunning = value;
            RaiseCanExecuteChanged();
        }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => !IsRunning && (_canExecute?.Invoke(parameter) ?? true);

    /// <inheritdoc />
    public void Execute(object? parameter) => _ = ExecuteAsync(parameter);

    /// <summary>Runs the command and returns the task, so tests can await the completion.</summary>
    public async Task ExecuteAsync(object? parameter = null)
    {
        if (!CanExecute(parameter))
            return;

        IsRunning = true;
        try
        {
            await _execute(parameter);
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Tells bound controls to ask <see cref="CanExecute"/> again.</summary>
    [SuppressMessage("Design", "CA1030",
        Justification = "The event already exists — CanExecuteChanged, imposed by ICommand. This is the " +
                        "raiser a ViewModel calls when a gating condition changes, which is the standard " +
                        "MVVM idiom; WPF's own CommandManager.InvalidateRequerySuggested is a WPF type and " +
                        "may not be referenced from these ViewModels.")]
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
