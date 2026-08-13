using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace Orkeon.Studio.Wpf.ViewModels.Mvvm;

/// <summary>
/// Hand-written synchronous <see cref="ICommand"/>.
/// <see cref="ICommand"/> lives in <c>System.ObjectModel</c>, part of the base framework, so it is
/// available to the plain <c>net10.0</c> test assembly; nothing here pulls in WPF.
/// Re-evaluation is explicit (<see cref="RaiseCanExecuteChanged"/>) rather than routed through
/// <c>CommandManager</c>, which is a WPF type.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>Creates a command from an action that ignores the command parameter.</summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = _ => execute();
        _canExecute = canExecute is null ? null : _ => canExecute();
    }

    /// <summary>Creates a command from an action that receives the command parameter.</summary>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);

        _execute = execute;
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            _execute(parameter);
    }

    /// <summary>Tells bound controls to ask <see cref="CanExecute"/> again.</summary>
    [SuppressMessage("Design", "CA1030",
        Justification = "The event already exists — CanExecuteChanged, imposed by ICommand. This is the " +
                        "raiser a ViewModel calls when a gating condition changes, which is the standard " +
                        "MVVM idiom; WPF's own CommandManager.InvalidateRequerySuggested is a WPF type and " +
                        "may not be referenced from these ViewModels.")]
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
