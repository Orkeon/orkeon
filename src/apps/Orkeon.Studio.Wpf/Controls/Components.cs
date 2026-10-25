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
/// The one alert of Studio (R7): icon on the left, the message, the actions on the right; in a
/// card (<see cref="IsBanner"/> false) or as the page banner under the header. The engine's own
/// text is shown as is: as the <see cref="Message"/> when Studio has no phrase of its own, or
/// under a folded «Détails techniques» (<see cref="Details"/>) when it has. Content, when set,
/// sits under the message (a list, a second line). Its look is the implicit style in
/// Themes/Components.xaml.
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
}

/// <summary>
/// The one badge (R8): a 22 px pill in one of five tones, its text the Content. Folder rights
/// read neutral «Lecture» and info «Lecture et écriture»; run statuses success «Réussie»,
/// danger «Échouée», neutral «Annulée», info «En cours». Never a bare coloured dot.
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
/// One editable property in a card body (R6): «Libellé · valeur · bouton» on one line — the
/// label in the tertiary ink on a fixed 104 px, the value (the Content), and a 28 px secondary
/// button on the right (<see cref="Action"/>: Changer, Arrêter, Autoriser, Planifier). The same
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
