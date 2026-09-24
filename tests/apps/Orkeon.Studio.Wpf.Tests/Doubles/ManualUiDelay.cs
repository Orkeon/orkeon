using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// An <see cref="IUiDelay"/> whose time passes when the test says so. The immediate delay plays
/// every callback on the way in, so there is never a pause to supersede — and «the CLI is asked
/// once the typing stops, not once per key» is exactly a pause being superseded.
/// </summary>
public sealed class ManualUiDelay : IUiDelay
{
    private readonly List<Action> _pending = [];

    /// <summary>How many callbacks wait for the time to pass.</summary>
    public int Pending => _pending.Count;

    /// <summary>The delays asked for, in order — dropped ones included.</summary>
    public List<TimeSpan> Requested { get; } = [];

    /// <inheritdoc />
    public void After(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        Requested.Add(delay);
        _pending.Add(action);
    }

    /// <inheritdoc />
    public void CancelPending() => _pending.Clear();

    /// <summary>Lets the time pass: every pending callback runs, in order.</summary>
    public void Elapse()
    {
        var due = _pending.ToList();
        _pending.Clear();
        foreach (var action in due)
            action();
    }
}
