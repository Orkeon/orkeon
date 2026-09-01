using System.Globalization;
using Orkeon.Studio.Wpf.ViewModels.Capture.Catalog;

namespace Orkeon.Studio.Wpf.ViewModels.Capture;

/// <summary>One shot to take: a stop, in a pass, at a stable place in the collection.</summary>
/// <param name="Stop">What to arrange and photograph.</param>
/// <param name="Appearance">The pass it belongs to.</param>
/// <param name="Ordinal">Its index in the CATALOGUE, not in the write order.</param>
/// <param name="RelativePath">Where the PNG goes, relative to the campaign's directory.</param>
internal sealed record CapturePlanItem(
    CaptureStop Stop,
    CaptureAppearance Appearance,
    int Ordinal,
    string RelativePath);

/// <summary>
/// Turns the catalogue and the matrix into an ordered list of shots.
/// <para>
/// The ordinal is the stop's index in the catalogue, deliberately — so the same stop carries the
/// SAME relative path in every pass, and comparing light against dark, or French against German, is
/// a directory diff. That single choice is what makes a collection of several hundred images
/// reviewable at all.
/// </para>
/// </summary>
internal static class CapturePlanner
{
    /// <summary>The whole campaign, in the order it runs.</summary>
    public static IReadOnlyList<CapturePlanItem> Plan(
        IReadOnlyList<CaptureStop> catalog, CaptureMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(matrix);

        var ordinals = catalog
            .Select((stop, index) => (stop.Name, Ordinal: index + 1))
            .ToDictionary(entry => entry.Name, entry => entry.Ordinal, StringComparer.Ordinal);

        var items = new List<CapturePlanItem>();
        foreach (var appearance in matrix.Passes)
        {
            var sweep = matrix.IsSweep(appearance);
            foreach (var stop in catalog)
            {
                if (!CaptureCatalog.Applies(stop, appearance.ModeFlag) || (sweep && !stop.SweepsLanguages))
                    continue;

                var ordinal = ordinals[stop.Name];
                items.Add(new CapturePlanItem(stop, appearance, ordinal, PathOf(stop, appearance, ordinal)));
            }
        }

        return items;
    }

    /// <summary>The file name of one shot: <c>&lt;lang&gt;/&lt;theme&gt;/&lt;mode&gt;/NNN-category-name.png</c>.</summary>
    public static string PathOf(CaptureStop stop, CaptureAppearance appearance, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(stop);
        ArgumentNullException.ThrowIfNull(appearance);

        var category = CaptureCategories.Slug(stop.Category);
        var index = ordinal.ToString("D3", CultureInfo.InvariantCulture);

        return $"{appearance.Folder}/{index}-{category}-{stop.Name}.png";
    }
}
