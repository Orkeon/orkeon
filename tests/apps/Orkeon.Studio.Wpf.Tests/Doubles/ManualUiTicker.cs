using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.Tests.Doubles;

/// <summary>
/// An <see cref="IUiTicker"/> that beats only when the test says so: <see cref="Tick"/> is one
/// second passing on screen, with no timer and no sleep behind it.
/// </summary>
public sealed class ManualUiTicker : IUiTicker
{
    private Action? _tick;

    /// <summary>Whether a beat is started — the bar keeps one only while a run goes.</summary>
    public bool IsRunning => _tick is not null;

    /// <summary>The interval the last start asked for.</summary>
    public TimeSpan? Interval { get; private set; }

    public void StartBeat(TimeSpan interval, Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        _tick = tick;
        Interval = interval;
    }

    public void StopBeat() => _tick = null;

    /// <summary>Fires the beat, when one is started; a stopped ticker does nothing, like a stopped timer.</summary>
    public void Tick() => _tick?.Invoke();
}
