using Orkeon.Studio.Wpf.ViewModels.Mvvm;

namespace Orkeon.Studio.Wpf.ViewModels.Capture.Worlds;

/// <summary>
/// The assistant's timed beats, under the campaign's control.
/// <para>
/// Immediate by default, like the tests' own delay — a campaign that waited on a wall clock would
/// photograph whichever beat happened to have landed. But a state that exists only BETWEEN two
/// beats is still a screen: the assistant-is-thinking beat is the sweeping hairline, the pulsing halo and
/// the three dots, and it lasts exactly as long as the pause does. Holding lets a stop stand in
/// that pause; releasing lets the thread finish its sentence.
/// </para>
/// </summary>
internal sealed class CaptureUiDelay : IUiDelay
{
    private readonly List<Action> _held = [];

    /// <summary>While true, callbacks queue instead of running.</summary>
    public bool Hold { get; set; }

    /// <inheritdoc />
    public void After(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (Hold)
            _held.Add(action);
        else
            action();
    }

    /// <inheritdoc />
    public void CancelPending() => _held.Clear();

    /// <summary>Runs everything that was held, in order, and stops holding.</summary>
    public void Release()
    {
        Hold = false;

        var held = _held.ToArray();
        _held.Clear();

        foreach (var action in held)
            action();
    }
}
