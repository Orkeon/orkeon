using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Orkeon.Studio.Core.Text;

namespace Orkeon.Studio.Wpf.Controls;

/// <summary>
/// One line of text cut in the middle when it does not fit — a file path keeps its drive and
/// its folder name (<see cref="MiddleEllipsis"/>). The full text is the tooltip once cut, and
/// always the automation name. Font, size and colour are inherited like a TextBlock's.
/// Usage: &lt;controls:MiddleEllipsisText Text="{Binding Directory}" Style="{StaticResource PathText}"/&gt;
/// </summary>
public sealed class MiddleEllipsisText : Control
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MiddleEllipsisText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => AutomationProperties.SetName(d, (string?)e.NewValue ?? "")));

    private string _shown = "";

    static MiddleEllipsisText()
    {
        FocusableProperty.OverrideMetadata(typeof(MiddleEllipsisText), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(MiddleEllipsisText), new FrameworkPropertyMetadata(false));
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    protected override Size MeasureOverride(Size constraint)
    {
        var full = Format(Text ?? "");
        return new Size(double.IsInfinity(constraint.Width) ? full.WidthIncludingTrailingWhitespace
                                                            : Math.Min(full.WidthIncludingTrailingWhitespace, constraint.Width),
                        full.Height);
    }

    protected override Size ArrangeOverride(Size arrangeBounds)
    {
        var text = Text ?? "";
        _shown = MiddleEllipsis.Fit(text, s => Format(s).WidthIncludingTrailingWhitespace, arrangeBounds.Width);
        ToolTip = _shown == text ? null : text;
        return arrangeBounds;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        drawingContext.DrawText(Format(_shown), new Point(0, 0));
    }

    private FormattedText Format(string text) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
