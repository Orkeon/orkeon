namespace Orkeon.Studio.Wpf.ViewModels.Mvvm;

/// <summary>
/// Runs a callback later, on the UI thread. The chat thread needs it: the assistant's
/// beats are timed (the thinking pause, the 1.5 s the closing bubble stays before the
/// wizard moves on), and a raw <c>Task.Delay</c> would make the ViewModels wall-clock
/// dependent — the suite compiles them into a plain net10.0 assembly and runs them
/// inline, so a real timer there means a slow, flaky test.
/// <para>
/// Deliberately the smallest surface that covers the mock's two primitives: schedule,
/// and forget everything scheduled (its <c>ivAfter</c> / <c>ivClear</c> pair).
/// </para>
/// </summary>
public interface IUiDelay
{
    /// <summary>Queues <paramref name="action"/> for <paramref name="delay"/> from now.</summary>
    void After(TimeSpan delay, Action action);

    /// <summary>Drops every callback not yet fired — a Stop must not be undone a second later.</summary>
    void CancelPending();
}

/// <summary>
/// Runs the callback immediately, ignoring the delay. The default for the ViewModels and
/// what the tests use: the whole interview plays out synchronously, so a test reads the
/// thread's final state without waiting for anything.
/// </summary>
public sealed class ImmediateUiDelay : IUiDelay
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static ImmediateUiDelay Instance { get; } = new();

    /// <inheritdoc />
    public void After(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        action();
    }

    /// <inheritdoc />
    public void CancelPending()
    {
        // Nothing is ever pending: every callback ran on the way in.
    }
}
