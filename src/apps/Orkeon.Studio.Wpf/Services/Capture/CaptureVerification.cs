using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Orkeon.Studio.Wpf.Services.Capture;

/// <summary>What a sample of the pixels found, kept for the manifest as well as for the verdict.</summary>
/// <param name="Sampled">How many pixels were looked at.</param>
/// <param name="DistinctColours">Distinct colours among them.</param>
/// <param name="FlatShare">Share held by the single most common colour, 0 to 1.</param>
internal sealed record PixelStats(int Sampled, int DistinctColours, double FlatShare);

/// <summary>
/// The checks that stop the collection from asserting something false.
/// <para>
/// A blank image, an image identical to the one before it, or an image drawn while a binding was
/// failing is worse than a missing image: it looks like evidence. The campaign that produced the
/// current 22 shots would write a duplicate of the previous screen and count it as a success.
/// </para>
/// </summary>
internal static class CaptureVerification
{
    /// <summary>Below this many distinct colours in the sample, nothing was drawn.</summary>
    private const int MinimumDistinctColours = 24;

    /// <summary>Above this share for one colour, the shot is a flat plate with a speck on it.</summary>
    private const double MaximumFlatShare = 0.985;

    /// <summary>One pixel in this many is examined — enough to tell blank from drawn, cheaply.</summary>
    private const int SampleStride = 16;

    /// <summary>Bytes per pixel in the Pbgra32 buffer the campaign renders into.</summary>
    private const int BytesPerPixel = 4;

    /// <summary>Walks a sample of the bitmap and counts what it finds.</summary>
    public static PixelStats Sample(BitmapSource bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var stride = bitmap.PixelWidth * BytesPerPixel;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var counts = new Dictionary<uint, int>();
        var sampled = 0;

        for (var offset = 0; offset + BytesPerPixel <= pixels.Length; offset += BytesPerPixel * SampleStride)
        {
            var colour = BitConverter.ToUInt32(pixels, offset);
            counts[colour] = counts.TryGetValue(colour, out var seen) ? seen + 1 : 1;
            sampled++;
        }

        if (sampled == 0)
            return new PixelStats(0, 0, 1);

        return new PixelStats(sampled, counts.Count, counts.Values.Max() / (double)sampled);
    }

    /// <summary>The rendered pixels are the size that was asked for, or the reason they are not.</summary>
    public static string? Geometry(BitmapSource bitmap, int expectedWidth, int expectedHeight)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        if (bitmap.PixelWidth == expectedWidth && bitmap.PixelHeight == expectedHeight)
            return null;

        return string.Create(CultureInfo.InvariantCulture,
            $"rendered {bitmap.PixelWidth}x{bitmap.PixelHeight}, expected {expectedWidth}x{expectedHeight}");
    }

    /// <summary>Something was actually drawn, or the reason to doubt it.</summary>
    public static string? Uniformity(PixelStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        if (stats.DistinctColours < MinimumDistinctColours)
            return string.Create(CultureInfo.InvariantCulture,
                $"only {stats.DistinctColours} distinct colour(s) — nothing was drawn");

        if (stats.FlatShare > MaximumFlatShare)
            return string.Create(CultureInfo.InvariantCulture,
                $"{stats.FlatShare:P1} of the image is one colour — the content area is empty");

        return null;
    }

    /// <summary>
    /// The shot differs from the one before it.
    /// <para>
    /// The highest-value check of the set: it catches the failure mode a growing campaign is most
    /// likely to have — a stop that changed nothing, because the navigation landed elsewhere, the
    /// expert-only screen stayed on the novice one, or the modal never opened.
    /// </para>
    /// </summary>
    public static string? DiffersFromPrevious(string sha, string? previousSha, bool allowed)
    {
        if (allowed || previousSha is null || !string.Equals(sha, previousSha, StringComparison.Ordinal))
            return null;

        return "identical to the previous shot — the stop changed nothing on screen";
    }

    /// <summary>The panel the stop claims to be standing on is up and has a size.</summary>
    public static string? PanelIsUp(FrameworkElement? panel, string panelName)
    {
        if (panel is null)
            return $"the '{panelName}' panel was not found in the window";

        if (!panel.IsVisible)
            return $"the '{panelName}' panel is not visible";

        if (panel.ActualWidth <= 0 || panel.ActualHeight <= 0)
            return $"the '{panelName}' panel has no size";

        return null;
    }
}
