namespace Orkeon.Studio.Wpf.ViewModels.Mvvm;

/// <summary>
/// Marshals a callback onto the thread that owns the bound collections. The child-process reader
/// raises output lines on a background thread; appending them to an <c>ObservableCollection</c>
/// bound to a WPF control is only legal on the dispatcher thread.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Queues <paramref name="action"/> for execution on the UI thread.</summary>
    void Post(Action action);
}

/// <summary>
/// Runs the callback inline on the calling thread. This is the default for the ViewModels and the
/// implementation the tests use, which is what makes them deterministic without a WPF dispatcher.
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static ImmediateUiDispatcher Instance { get; } = new();

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        action();
    }
}
