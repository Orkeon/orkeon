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

    /// <summary>
    /// Scrolls the screen until what presents <paramref name="content"/> (a ViewModel the screen
    /// shows: a row of a list, the form of a selection) is in view. Only the scroll position
    /// changes. A stop asks for it when its state sits under the fold and no gesture of the app
    /// brings it up — a section further down the page, a tab switched on a selection already
    /// made; a state a selection reaches is the app's to bring into view (R24), never this.
    /// Every stop starts at the top of its screen: <see cref="ShowAsync"/> scrolls back there.
    /// </summary>
    Task BringIntoViewAsync(object content);

    /// <summary>Lets bindings, layout and the compositor catch up.</summary>
    Task SettleAsync();
}
