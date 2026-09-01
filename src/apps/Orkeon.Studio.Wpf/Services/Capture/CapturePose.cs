using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Orkeon.Studio.Wpf.Controls;

namespace Orkeon.Studio.Wpf.Services.Capture;

/// <summary>
/// Puts every running animation at a chosen, repeatable value, so two runs of the campaign differ
/// only where the UI differs.
/// <para>
/// Deliberately a POSE and not a freeze-wherever-it-lands. A spinner sampled at a random phase
/// makes six images differ on every run and drowns the signal a fidelity reference exists to
/// carry — the reviewer can no longer tell a regression from a lucky frame. A spinner posed at a
/// fixed 135° still reads as work in progress, because it is off both axes, and it reads the same
/// way twice.
/// </para>
/// <para>
/// Clearing is never enough on its own: <c>BeginAnimation(dp, null)</c> snaps the property back to
/// its base value, which is often invisible — the chat halo carries a local <c>Opacity="0"</c>, so
/// clearing alone erases it. Every clear here is followed by a value.
/// </para>
/// </summary>
internal static class CapturePose
{
    /// <summary>The glyph the three loader spinners share; the language chevron is a different one.</summary>
    private const string SpinnerKind = "loader-circle";

    /// <summary>Off both axes: a spinner at 0° or 90° reads as a paused icon, not as motion.</summary>
    private const double SpinnerAngle = 135;

    /// <summary>Applies the pose to everything currently in the window's tree.</summary>
    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var visuals = CaptureVisualTree.Descendants(window).ToList();

        PoseSpinners(visuals);
        PoseSweep(visuals);
        PoseHalo(visuals);
        PoseDots(visuals);
        PoseOpenPopups(visuals);
    }

    /// <summary>
    /// The loader spinners. Matched on the glyph rather than on "any RotateTransform": the language
    /// menu's chevron also rotates, but its angle is STATE — 0 closed, 180 open — and posing it at
    /// 135° would draw a chevron nobody has ever seen.
    /// </summary>
    private static void PoseSpinners(IEnumerable<DependencyObject> visuals)
    {
        foreach (var icon in visuals.OfType<LucideIcon>())
        {
            if (!string.Equals(icon.Kind, SpinnerKind, StringComparison.Ordinal))
                continue;

            if (icon.RenderTransform is not RotateTransform { IsFrozen: false } rotate)
                continue;

            rotate.BeginAnimation(RotateTransform.AngleProperty, null);
            rotate.Angle = SpinnerAngle;
        }
    }

    /// <summary>
    /// The chat's busy hairline — the <c>SweepBrush</c> transform. Reached through the brush rather
    /// than by name: the transform lives
    /// inside a <see cref="Brush.RelativeTransform"/>, and a brush is not in the
    /// visual tree at all. Posed left of centre so the sweep reads as travelling; at its base value
    /// of 0 the gradient sits dead centre and reads as a static bar.
    /// </summary>
    private static void PoseSweep(IEnumerable<DependencyObject> visuals)
    {
        foreach (var shape in visuals.OfType<Shape>())
        {
            if (shape.Fill is not LinearGradientBrush { IsFrozen: false } gradient)
                continue;

            if (gradient.RelativeTransform is not TranslateTransform { IsFrozen: false } sweep)
                continue;

            sweep.BeginAnimation(TranslateTransform.XProperty, null);
            sweep.X = -0.32;
        }
    }

    /// <summary>The pulsing halo behind the assistant's avatar, caught mid-expansion.</summary>
    private static void PoseHalo(IEnumerable<DependencyObject> visuals)
    {
        foreach (var halo in visuals.OfType<Ellipse>().Where(e => e.Name == "Halo"))
        {
            halo.BeginAnimation(UIElement.OpacityProperty, null);
            halo.Opacity = 0.18;

            if (halo.RenderTransform is not ScaleTransform { IsFrozen: false } scale)
                continue;

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = 1.28;
            scale.ScaleY = 1.28;
        }
    }

    /// <summary>The three thinking dots, posed mid-wave so they read as a sequence.</summary>
    private static void PoseDots(IEnumerable<DependencyObject> visuals)
    {
        var opacities = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["Dot1"] = 1.00,
            ["Dot2"] = 0.62,
            ["Dot3"] = 0.26,
        };

        foreach (var dot in visuals.OfType<Ellipse>())
        {
            if (!opacities.TryGetValue(dot.Name, out var opacity))
                continue;

            dot.BeginAnimation(UIElement.OpacityProperty, null);
            dot.Opacity = opacity;
        }
    }

    /// <summary>
    /// An open popup's own root, which <c>PopupAnimation="Fade"</c> animates. The campaign
    /// composites that root into the shot; catching it mid-fade would put a translucent menu into
    /// the collection.
    /// </summary>
    private static void PoseOpenPopups(IEnumerable<DependencyObject> visuals)
    {
        foreach (var popup in visuals.OfType<Popup>())
        {
            if (!popup.IsOpen || popup.Child is null)
                continue;

            if (CaptureVisualTree.TopmostParent(popup.Child) is not UIElement root)
                continue;

            root.BeginAnimation(UIElement.OpacityProperty, null);
            root.Opacity = 1;
        }
    }
}
