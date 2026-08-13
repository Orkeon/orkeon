using System.Windows.Threading;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// The WPF implementation of <see cref="IUiDispatcher"/>. The child-process reader raises output
/// lines on a background thread; this is what moves them onto the thread that owns the bound
/// collections.
/// </summary>
public sealed class WpfDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    /// <summary>Wraps the dispatcher of the thread that owns the window.</summary>
    public WpfDispatcher(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
            action();
        else
            _ = _dispatcher.BeginInvoke(action);
    }
}
