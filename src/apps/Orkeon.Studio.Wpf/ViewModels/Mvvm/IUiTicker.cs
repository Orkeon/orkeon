namespace Orkeon.Studio.Wpf.ViewModels.Mvvm;

/// <summary>
/// A steady beat on the UI thread, for a reading that moves between events: a run's elapsed
/// time goes on while the run says nothing, so the status bar refreshes it on a beat of its own
/// (STUDIO-34).
/// <para>
/// Kept apart from <see cref="IUiDelay"/> on purpose. The tests' delay runs its callback on the
/// way in, and a beat re-armed through it would never return; a real timer in the ViewModels
/// would make the suite wall-clock dependent. One beat per ticker — the smallest surface that
/// covers the need, like the delay's schedule-and-forget pair.
/// </para>
/// </summary>
public interface IUiTicker
{
    /// <summary>
    /// Calls <paramref name="tick"/> every <paramref name="interval"/> until <see cref="StopBeat"/>.
    /// A start while a beat is running replaces it.
    /// </summary>
    void StartBeat(TimeSpan interval, Action tick);

    /// <summary>Stops the beat; nothing is called after this returns.</summary>
    void StopBeat();
}

/// <summary>
/// Never beats. The default for the ViewModels, and what the tests and the screenshot campaign
/// keep: a reading that moved on its own between two assertions — or two shots — is a flake.
/// </summary>
public sealed class NullUiTicker : IUiTicker
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static NullUiTicker Instance { get; } = new();

    /// <inheritdoc />
    public void StartBeat(TimeSpan interval, Action tick) => ArgumentNullException.ThrowIfNull(tick);

    /// <inheritdoc />
    public void StopBeat()
    {
        // Nothing was ever beating.
    }
}
