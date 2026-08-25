using System.Globalization;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.Launch;

/// <summary>
/// The one recipe for the usage chips a finished run or trial shows (v3 W-08):
/// «12 840 tokens» · «cache 62 % · 7 980 tokens» · «59 s». A metric that was not
/// measured produces NO chip — never a zero. The cache pair is a partition of the
/// prompt tokens; the ratio shown is hits over the measured pair.
/// </summary>
public static class UsageMetricsFormatter
{
    /// <summary>Builds the chips in display order (tokens, cache, duration), English.</summary>
    public static IReadOnlyList<string> Chips(long? tokens, long? cacheHitTokens, long? cacheMissTokens, long? durationMs)
        => Chips(tokens, cacheHitTokens, cacheMissTokens, durationMs, EnglishStudioStrings.Instance, CultureInfo.CurrentCulture);

    /// <summary>Builds the chips in the given culture port and number culture (STUDIO-11).</summary>
    public static IReadOnlyList<string> Chips(
        long? tokens,
        long? cacheHitTokens,
        long? cacheMissTokens,
        long? durationMs,
        IStudioStrings strings,
        CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(strings);
        ArgumentNullException.ThrowIfNull(culture);

        var chips = new List<string>(3);

        if (tokens is { } total)
            chips.Add(string.Format(culture, strings[StudioStringKeys.UsageTokens], total.ToString("N0", culture)));

        if (cacheHitTokens is { } hit && cacheMissTokens is { } miss && hit + miss > 0)
        {
            var percent = (long)Math.Round(100.0 * hit / (hit + miss));
            chips.Add(string.Format(
                culture, strings[StudioStringKeys.UsageCache], percent, hit.ToString("N0", culture)));
        }

        if (durationMs is { } elapsed && elapsed >= 0)
            chips.Add(Duration(elapsed, strings, culture));

        return chips;
    }

    /// <summary>«59 s» under a minute, «1 min 40 s» above — seconds rounded, never faked.</summary>
    public static string Duration(long durationMs, IStudioStrings strings, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(strings);

        var totalSeconds = (long)Math.Round(durationMs / 1000.0);
        return totalSeconds < 60
            ? string.Format(culture, strings[StudioStringKeys.UsageSeconds], totalSeconds)
            : string.Format(
                culture, strings[StudioStringKeys.UsageMinutesSeconds], totalSeconds / 60, totalSeconds % 60);
    }
}
