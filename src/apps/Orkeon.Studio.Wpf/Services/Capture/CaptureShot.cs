using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orkeon.Compliance.Vfs;

namespace Orkeon.Studio.Wpf.Services.Capture;

/// <summary>Taking the picture: the window, every open popup, and the bytes on disk.</summary>
[SuppressVfsCompliance(
    "UI-layer capture harness writing PNGs where the operator pointed it — Studio's own state, " +
    "not framework I/O; same exception class as ScreenCaptureRunner and UiPreferences.")]
internal static class CaptureShot
{
    /// <summary>WPF's own device-independent unit density; the scale multiplies it.</summary>
    private const double BaseDpi = 96;

    /// <summary>
    /// Renders the window and everything hovering over it into one bitmap.
    /// <para>
    /// A <see cref="Popup"/> is hosted in its OWN <c>HwndSource</c>: it is not a descendant of the
    /// window, and rendering the window alone simply cannot see it. The language menu would come
    /// out as a chevron rotated to 180° above nothing — a picture of a broken application. Hence
    /// the composite.
    /// </para>
    /// </summary>
    public static RenderTargetBitmap Render(Window window, double scale, IList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(notes);

        var width = window.ActualWidth;
        var height = window.ActualHeight;
        var windowLayer = RenderVisual(window, width, height, scale);

        var overlays = OpenPopupLayers(window, width, height, scale, notes).ToList();
        if (overlays.Count == 0)
            return windowLayer;

        var composed = new DrawingVisual();
        using (var context = composed.RenderOpen())
        {
            context.DrawImage(windowLayer, new Rect(0, 0, width, height));
            foreach (var (layer, placement) in overlays)
                context.DrawImage(layer, placement);
        }

        return RenderVisual(composed, width, height, scale);
    }

    /// <summary>The PNG bytes, and the digest that makes two runs comparable.</summary>
    public static (byte[] Png, string Sha256) Encode(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var buffer = new MemoryStream();
        encoder.Save(buffer);
        var png = buffer.ToArray();

        return (png, Convert.ToHexStringLower(SHA256.HashData(png)));
    }

    /// <summary>
    /// Writes through a temporary name and moves it into place, so a campaign killed mid-encode
    /// leaves no half file that still looks like a plausible screenshot.
    /// </summary>
    public static void WriteAtomic(string path, byte[] png)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(png);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var staging = path + ".tmp";
        File.WriteAllBytes(staging, png);
        File.Move(staging, path, overwrite: true);
    }

    private static RenderTargetBitmap RenderVisual(Visual visual, double width, double height, double scale)
    {
        var pixelWidth = (int)Math.Ceiling(width * scale);
        var pixelHeight = (int)Math.Ceiling(height * scale);

        var bitmap = new RenderTargetBitmap(
            pixelWidth, pixelHeight, BaseDpi * scale, BaseDpi * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        return bitmap;
    }

    /// <summary>
    /// Each open popup's own root, and where it sits relative to the window.
    /// <para>
    /// The placement is checked against the window's bounds rather than trusted: if the arithmetic
    /// lands the menu somewhere impossible, the layer is dropped with a note instead of being
    /// pasted into a corner, so the collection never carries a confidently-wrong composite.
    /// </para>
    /// </summary>
    private static IEnumerable<(RenderTargetBitmap Layer, Rect Placement)> OpenPopupLayers(
        Window window, double width, double height, double scale, IList<string> notes)
    {
        var popups = CaptureVisualTree.Descendants(window)
            .OfType<Popup>()
            .Where(popup => popup is { IsOpen: true, Child: not null })
            .ToList();

        if (popups.Count == 0)
            yield break;

        var dpi = VisualTreeHelper.GetDpi(window);
        var windowOrigin = window.PointToScreen(new Point(0, 0));
        var bounds = new Rect(0, 0, width, height);

        foreach (var popup in popups)
        {
            // The popup's ROOT, not its Child: the child carries a margin and a drop shadow, and
            // both draw outside the child's own bounds.
            if (CaptureVisualTree.TopmostParent(popup.Child!) is not FrameworkElement root)
                continue;

            if (PresentationSource.FromVisual(root) is null)
            {
                notes.Add($"popup '{PopupName(popup)}' is open but not yet hosted — not composited");
                continue;
            }

            var origin = root.PointToScreen(new Point(0, 0));
            var placement = new Rect(
                (origin.X - windowOrigin.X) / dpi.DpiScaleX,
                (origin.Y - windowOrigin.Y) / dpi.DpiScaleY,
                root.ActualWidth,
                root.ActualHeight);

            if (placement.Width <= 0 || placement.Height <= 0 || !bounds.Contains(placement))
            {
                notes.Add(
                    $"popup '{PopupName(popup)}' computed at {Format(placement)}, outside the "
                    + $"{width:0}x{height:0} window — not composited");
                continue;
            }

            yield return (RenderVisual(root, root.ActualWidth, root.ActualHeight, scale), placement);
        }
    }

    private static string PopupName(Popup popup) =>
        popup.Name.Length > 0 ? popup.Name : popup.GetType().Name;

    private static string Format(Rect rect) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"{rect.X:0}x{rect.Y:0} {rect.Width:0}x{rect.Height:0}");
}
