using System.Windows.Threading;
using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Services;

/// <summary>
/// The real <see cref="IUiTicker"/>: one <see cref="DispatcherTimer"/>, ticking on the dispatcher
/// thread, so the beat may touch bound properties directly. Background priority: a clock on the
/// status bar must never hold up input or layout.
/// </summary>
public sealed class WpfTicker : IUiTicker
{
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _timer;

    /// <summary>Beats on <paramref name="dispatcher"/>'s thread.</summary>
    public WpfTicker(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public void StartBeat(TimeSpan interval, Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        StopBeat();
        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = interval };
        timer.Tick += (_, _) => tick();
        _timer = timer;
        timer.Start();
    }

    /// <inheritdoc />
    public void StopBeat()
    {
        _timer?.Stop();
        _timer = null;
    }
}
