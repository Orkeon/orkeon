namespace Orkeon.Studio.Wpf.ViewModels.Shell;

/// <summary>
/// One stop of the guided tour, as data.
/// </summary>
/// <param name="TargetName">The <c>x:Name</c> to spotlight; null, or a name drawn nowhere on screen, for a centred card.</param>
/// <param name="TitleKey">Resource key of the title.</param>
/// <param name="BodyKey">Resource key of the body.</param>
public sealed record GuidedTourStep(string? TargetName, string TitleKey, string BodyKey);

/// <summary>
/// The three stops of the tour, held here rather than welded into the window's code-behind.
/// <para>
/// The tour shows the novice the task, on the screen where it is done: write the need
/// (<c>NeedBox</c>), let the assistant compose and try it (<c>StepRail</c>, the step rail of
/// the create screen), then find the adopted teams (<c>NavTeams</c>). The first two targets
/// exist only on the create screen at step 1: the window brings that screen up before measuring.
/// </para>
/// <para>
/// Two consumers read this one list: the window, which projects it into the overlay's own step
/// type, and the screenshot campaign, which projects it into one capture stop per step. A fourth
/// stop therefore arrives in the collection for free instead of being silently missing from it.
/// </para>
/// </summary>
public static class GuidedTourCatalog
{
    /// <summary>Targets that live on the create screen at step 1 and need it brought up first.</summary>
    public static IReadOnlySet<string> CreateScreenTargets { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "NeedBox", "StepRail" };

    /// <summary>The stops, in the order the tour walks them.</summary>
    public static IReadOnlyList<GuidedTourStep> Steps { get; } =
    [
        new("NeedBox", "Studio.Shell.TourStep1Title", "Studio.Shell.TourStep1Body"),
        new("StepRail", "Studio.Shell.TourStep2Title", "Studio.Shell.TourStep2Body"),
        new("NavTeams", "Studio.Shell.TourStep3Title", "Studio.Shell.TourStep3Body"),
    ];
}
