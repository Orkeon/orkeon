using System.Windows;
using System.Windows.Controls;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// The one empty state (R17): a Lucide <see cref="Icon"/> in a 40 px sky circle, a
/// <see cref="Title"/> in 16/600, one 14 px <see cref="Text"/> sentence and 0 to 2
/// <see cref="Actions"/> (the only place where «Add …» may be the primary). Compact
/// (<see cref="IsCompact"/>): a 24 px circle on the left of a 14/600 title and the sentence, no
/// action — for a list inside a card («Créations en cours»). Its look is the implicit style in
/// Themes/Components.xaml.
/// </summary>
public sealed class EmptyState : Control
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(EmptyState), new FrameworkPropertyMetadata("info"));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EmptyState), new FrameworkPropertyMetadata(""));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(EmptyState), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(EmptyState), new FrameworkPropertyMetadata(false));

    static EmptyState()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(typeof(EmptyState)));
        FocusableProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(false));
    }

    /// <summary>The Lucide kind drawn in the circle («brain-circuit», «history», «mail»…).</summary>
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
    public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
}

/// <summary>
/// The loading skeleton (R17): three grey lines (full, 85 %, 60 % of the width), never a
/// coloured block, shown while a list or a card is loading.
/// </summary>
public sealed class SkeletonLines : Control
{
    static SkeletonLines()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SkeletonLines), new FrameworkPropertyMetadata(typeof(SkeletonLines)));
        FocusableProperty.OverrideMetadata(typeof(SkeletonLines), new FrameworkPropertyMetadata(false));
    }
}
