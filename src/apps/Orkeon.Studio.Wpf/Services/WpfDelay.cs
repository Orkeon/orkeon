using System.Diagnostics.CodeAnalysis;
using System.Windows.Threading;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// The real <see cref="IUiDelay"/>: one <see cref="DispatcherTimer"/> per scheduled
/// callback, all of them droppable at once. The timers tick on the dispatcher thread, so
/// a callback may touch the bound collections directly.
/// </summary>
public sealed class WpfDelay(Dispatcher dispatcher) : IUiDelay
{
    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    private readonly Dispatcher _dispatcher = dispatcher;

    [SuppressMessage("Minor Code Smell", "S3604:Member initializer values should not be redundant",
        Justification = "False positive on a primary constructor: the initializer IS the only "
                      + "assignment of the member, and removing it would leave it unset.")]
    private readonly List<DispatcherTimer> _pending = [];

    /// <inheritdoc />
    public void After(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _pending.Remove(timer);
            action();
        };

        _pending.Add(timer);
        timer.Start();
    }

    /// <inheritdoc />
    public void CancelPending()
    {
        foreach (var timer in _pending)
            timer.Stop();

        _pending.Clear();
    }
}
