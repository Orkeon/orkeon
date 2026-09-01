namespace Orkeon.Studio.Wpf.ViewModels.Capture;

/// <summary>
/// The only seam a capture stop is allowed to see onto WPF.
/// <para>
/// Everything else a stop does is ViewModel work, which is what lets the whole catalogue — its
/// arrange bodies included — compile into the net10.0 test assembly and be asserted on Linux. A
/// stop that finds itself needing a <c>Window</c> should grow a method here instead.
/// </para>
/// <para>
/// Theme and language are deliberately absent: those are per-pass concerns the executor applies
/// once, and putting them here is what would make the catalogue acquire four copies of every entry.
/// </para>
/// </summary>
internal interface ICaptureSurface
{
    /// <summary>Brings <paramref name="screen"/> forward.</summary>
    Task ShowAsync(CaptureScreen screen);

    /// <summary>Puts the startup plate back on screen, posed.</summary>
    void ShowSplash();

    /// <summary>Takes the startup plate away, with no animation involved.</summary>
    void HideSplash();

    /// <summary>Opens the guided tour on one of its stops.</summary>
    void StartTourAt(int stepIndex);

    /// <summary>Closes the guided tour.</summary>
    void EndTour();

    /// <summary>Lets bindings, layout and the compositor catch up.</summary>
    Task SettleAsync();
}
