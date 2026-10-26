using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>The meaning of a <see cref="StatusDot"/> (R08): one colour per state of progress.</summary>
public enum DotTone
{
    /// <summary>Nothing to do yet, or not started (NeutralDotBrush).</summary>
    Neutral,

    /// <summary>Something is expected from the user (WarningIconBrush, the sand).</summary>
    Attention,

    /// <summary>Something failed or blocks (DangerBrush).</summary>
    Error,

    /// <summary>Done, ready to go on (SuccessBrush).</summary>
    Ready,
}

/// <summary>
/// The progress line (R08): an 8 px dot then one line of text (the Content) — a card footer
/// says the state of its section, a page footer what is missing to go on; the two never say the
/// same sentence. Distinct from a <see cref="Badge"/>: no pill, no tinted background. Its look is
/// the implicit style in Themes/Components.xaml.
/// </summary>
public sealed class StatusDot : ContentControl
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(DotTone), typeof(StatusDot), new FrameworkPropertyMetadata(DotTone.Neutral));

    static StatusDot()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusDot), new FrameworkPropertyMetadata(typeof(StatusDot)));
        FocusableProperty.OverrideMetadata(typeof(StatusDot), new FrameworkPropertyMetadata(false));
    }

    public DotTone Tone { get => (DotTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}
