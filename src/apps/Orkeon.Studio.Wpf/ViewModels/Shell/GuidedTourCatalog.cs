namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// One stop of the guided tour, as data.
/// </summary>
/// <param name="TargetName">The <c>x:Name</c> to spotlight; null for the centred closing step.</param>
/// <param name="TitleKey">Resource key of the title.</param>
/// <param name="BodyKey">Resource key of the body.</param>
public sealed record GuidedTourStep(string? TargetName, string TitleKey, string BodyKey);

/// <summary>
/// The five stops of the tour, held here rather than welded into the window's code-behind.
/// <para>
/// Two consumers read this one list: the window, which projects it into the overlay's own step
/// type, and the screenshot campaign, which projects it into one capture stop per step. A sixth
/// stop therefore arrives in the collection for free instead of being silently missing from it.
/// </para>
/// </summary>
public static class GuidedTourCatalog
{
    /// <summary>The stops, in the order the tour walks them.</summary>
    public static IReadOnlyList<GuidedTourStep> Steps { get; } =
    [
        new("ModeSwitch", "Studio.Shell.TourStep1Title", "Studio.Shell.TourStep1Body"),
        new("NavGroupTeams", "Studio.Shell.TourStep2Title", "Studio.Shell.TourStep2Body"),
        new("NavGroupWork", "Studio.Shell.TourStep3Title", "Studio.Shell.TourStep3Body"),
        new("NavGroupEnv", "Studio.Shell.TourStep4Title", "Studio.Shell.TourStep4Body"),
        new(null, "Studio.Shell.TourStep5Title", "Studio.Shell.TourStep5Body"),
    ];
}
