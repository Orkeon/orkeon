using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>The tone of an <see cref="Alert"/> or a <see cref="Badge"/> (refonte R7, R8): one meaning per tone.</summary>
public enum Tone
{
    Neutral,
    Info,
    Success,
    Warning,
    Danger,
}

/// <summary>
/// The one banner of Studio (R07): four tones — info (sky / tide), success, warning (sand icon,
/// never red), error (the circle slashed) — plus a neutral one; a 1 px border of the tone; a
/// 20 px icon on the first line of text (C22). An optional one-line <see cref="Title"/> in 14/600
/// over the <see cref="Message"/> in 14/400; without a title, the message is the 14/600 line and
/// wraps. Content, when set, sits under the message (a list, a second line). The engine's own
/// text is never shown outside the folded «Technical details» (<see cref="Details"/>, a
/// <see cref="Disclosure"/> over a mono <see cref="TechBlock"/>, R16/R18). The
/// <see cref="Actions"/> (32 px buttons) sit on the right when the banner is wider than
/// <see cref="WideThreshold"/>, under the text otherwise (<see cref="IsWide"/>, measured here from
/// the width the container offers — no view model involved). It stretches to its container.
/// Its look is the implicit style in Themes/Components.xaml.
/// </summary>
public sealed class Alert : ContentControl
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(Tone), typeof(Alert), new FrameworkPropertyMetadata(Tone.Info));

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(Alert), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details), typeof(string), typeof(Alert), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(Alert), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IsBannerProperty = DependencyProperty.Register(
        nameof(IsBanner), typeof(bool), typeof(Alert), new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Alert), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty IsDetailsExpandedProperty = DependencyProperty.Register(
        nameof(IsDetailsExpanded), typeof(bool), typeof(Alert),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private static readonly DependencyPropertyKey IsWidePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsWide), typeof(bool), typeof(Alert), new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty IsWideProperty = IsWidePropertyKey.DependencyProperty;

    /// <summary>The width above which the actions sit on the right of the text (R07).</summary>
    public const double WideThreshold = 720;

    static Alert()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Alert), new FrameworkPropertyMetadata(typeof(Alert)));
        FocusableProperty.OverrideMetadata(typeof(Alert), new FrameworkPropertyMetadata(false));
    }

    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string? Details { get => (string?)GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public bool IsBanner { get => (bool)GetValue(IsBannerProperty); set => SetValue(IsBannerProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool IsDetailsExpanded { get => (bool)GetValue(IsDetailsExpandedProperty); set => SetValue(IsDetailsExpandedProperty, value); }

    /// <summary>True when the width offered is above <see cref="WideThreshold"/>: the actions go right.</summary>
    public bool IsWide => (bool)GetValue(IsWideProperty);

    protected override Size MeasureOverride(Size constraint)
    {
        // Read from the width offered, not from the width obtained: moving the actions changes
        // the latter, and a banner sized by its content would flip at every pass.
        var wide = double.IsInfinity(constraint.Width) || constraint.Width > WideThreshold;
        if (wide != IsWide)
        {
            SetValue(IsWidePropertyKey, wide);
        }

        return base.MeasureOverride(constraint);
    }
}

/// <summary>
/// The one badge (R12): a 22 px pill, the tone's tinted background and dark ink, the background
/// kept in the dark theme; its text the Content. Folder rights read neutral «Lecture seule» and
/// info (sky) «Lecture et écriture», everywhere; run statuses success «Réussie», danger
/// «Échouée», info «En cours», neutral «En attente». A state line with a dot is a
/// <c>StatusDot</c>, not a badge.
/// </summary>
public sealed class Badge : ContentControl
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(Tone), typeof(Badge), new FrameworkPropertyMetadata(Tone.Neutral));

    static Badge()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(typeof(Badge)));
        FocusableProperty.OverrideMetadata(typeof(Badge), new FrameworkPropertyMetadata(false));
    }

    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}

/// <summary>
/// One editable property in a card body (R6): «label · value · button» on one line — the
/// label in the tertiary ink on a fixed 104 px, the value (the Content), and a 28 px secondary
/// button on the right (<see cref="Action"/>: Change, Stop, Allow, Schedule). The same
/// shape whatever the action.
/// </summary>
public sealed class PropertyRow : ContentControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(PropertyRow), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty ActionProperty = DependencyProperty.Register(
        nameof(Action), typeof(object), typeof(PropertyRow), new FrameworkPropertyMetadata(null));

    static PropertyRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PropertyRow), new FrameworkPropertyMetadata(typeof(PropertyRow)));
        FocusableProperty.OverrideMetadata(typeof(PropertyRow), new FrameworkPropertyMetadata(false));
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public object? Action { get => GetValue(ActionProperty); set => SetValue(ActionProperty, value); }
}

/// <summary>
/// The single page header (R3): the title and a one-line subtitle on the left, the page
/// actions on the right (the Content) — secondaries first, the primary last.
/// </summary>
public sealed class PageHeader : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PageHeader), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(PageHeader), new FrameworkPropertyMetadata(""));

    static PageHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PageHeader), new FrameworkPropertyMetadata(typeof(PageHeader)));
        FocusableProperty.OverrideMetadata(typeof(PageHeader), new FrameworkPropertyMetadata(false));
    }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
}
