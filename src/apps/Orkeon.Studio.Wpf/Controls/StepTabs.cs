using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>The state mark of a <see cref="StepTab"/> (refonte R14): one meaning per mark.</summary>
public enum StepTabState
{
    /// <summary>An empty circle: optional, or nothing in it yet.</summary>
    Empty,

    /// <summary>A green check: the section is done.</summary>
    Done,

    /// <summary>An amber dot: an action of the user's is expected there — the step's wait point (R13).</summary>
    Expected,

    /// <summary>An amber dot and the subtitle in the warning tone: something there needs fixing.</summary>
    Warning,

    /// <summary>A green dot: the section was checked and holds.</summary>
    Verified,
}

/// <summary>
/// One tab of a <see cref="StepTabs"/> classeur (R14): its state mark, its title (the Header)
/// and a 12 px state subtitle under it. Its look is the implicit style in Themes/Components.xaml.
/// The grouped variant (Settings): a tab whose <see cref="Group"/> is set opens a group — the
/// group's title in capitals and a rule above it; the tabs after it, with no group, belong to it.
/// </summary>
public sealed class StepTab : TabItem
{
    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(StepTab), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(StepTabState), typeof(StepTab), new FrameworkPropertyMetadata(StepTabState.Empty));

    public static readonly DependencyProperty GroupProperty = DependencyProperty.Register(
        nameof(Group), typeof(string), typeof(StepTab), new FrameworkPropertyMetadata(""));

    static StepTab()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StepTab), new FrameworkPropertyMetadata(typeof(StepTab)));
    }

    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public StepTabState State { get => (StepTabState)GetValue(StateProperty); set => SetValue(StateProperty, value); }

    /// <summary>The title of the group this tab opens; empty for a tab inside a group, or with none.</summary>
    public string Group { get => (string)GetValue(GroupProperty); set => SetValue(GroupProperty, value); }
}

/// <summary>
/// The vertical classeur of a step with sections (R14): a 232 px column of <see cref="StepTab"/>
/// on the left, the selected section's pane on the right. The active tab runs into the pane;
/// the space under the tabs holds <see cref="SideContent"/> — the Novice help card, nothing in
/// Expert. The pane's own footer belongs to each section's content; the step's action bar
/// stays outside the classeur.
/// </summary>
public sealed class StepTabs : TabControl
{
    public static readonly DependencyProperty SideContentProperty = DependencyProperty.Register(
        nameof(SideContent), typeof(object), typeof(StepTabs), new FrameworkPropertyMetadata(null));

    static StepTabs()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StepTabs), new FrameworkPropertyMetadata(typeof(StepTabs)));
    }

    public object? SideContent { get => GetValue(SideContentProperty); set => SetValue(SideContentProperty, value); }

    protected override DependencyObject GetContainerForItemOverride() => new StepTab();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is StepTab;
}
