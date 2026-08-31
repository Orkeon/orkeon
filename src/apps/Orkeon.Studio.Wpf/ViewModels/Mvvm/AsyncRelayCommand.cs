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

    /// <summary>
    /// Where a fault raised by a command body is reported. <see cref="Execute"/> is called by
    /// WPF and cannot return a task, so without this the exception lands in a discarded
    /// <see cref="Task"/> and is dropped without a log, a dialog, or a debugger break — the
    /// button simply stops responding. That is not hypothetical: a markup error in the chat
    /// panel's storyboard threw out of a property setter and presented as a Compose button
    /// that answered every click with nothing at all.
    /// <para>
    /// A plain delegate rather than an event, and no WPF type in sight: these ViewModels are
    /// compiled into the plain net10.0 test assembly, which has no WPF. The application wires
    /// it in its composition root; tests may set it to capture faults, and leaving it unset
    /// keeps the old lenient behaviour.
    /// </para>
    /// </summary>
    public static Action<Exception>? FaultHandler { get; set; }

    /// <inheritdoc />
    public void Execute(object? parameter) => _ = ReportFaultsAsync(parameter);

    private async Task ReportFaultsAsync(object? parameter)
    {
        try
        {
            await ExecuteAsync(parameter);
        }
        catch (Exception ex) when (FaultHandler is not null)
        {
            FaultHandler(ex);
        }
    }

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
