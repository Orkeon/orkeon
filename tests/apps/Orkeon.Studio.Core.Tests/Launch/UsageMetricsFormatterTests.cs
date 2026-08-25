using System.Globalization;
using Orkeon.Studio.Core.Launch;
using Orkeon.Studio.Core.Localization;

namespace Orkeon.Studio.Core.Tests.Launch;

/// <summary>
/// The one chip recipe of W-08: tokens, then the cache partition, then the duration —
/// and never a chip for something that was not measured.
/// </summary>
public sealed class UsageMetricsFormatterTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>fr-FR groups digits with (narrow) no-break spaces; assertions read through them.</summary>
    private static string Plain(string chip) => chip.Replace('\u202f', ' ').Replace('\u00a0', ' ');

    [Fact]
    public void The_three_chips_come_out_in_display_order()
    {
        var chips = UsageMetricsFormatter.Chips(
            tokens: 12_840, cacheHitTokens: 7_980, cacheMissTokens: 4_020, durationMs: 59_000,
            EnglishStudioStrings.Instance, French);

        Assert.Equal(3, chips.Count);
        Assert.Equal("12 840 tokens", Plain(chips[0]));
        Assert.Equal("cache 66 % · 7 980 tokens", Plain(chips[1]));
        Assert.Equal("59 s", chips[2]);
    }

    [Fact]
    public void A_long_run_reads_in_minutes_and_seconds()
    {
        var chips = UsageMetricsFormatter.Chips(
            tokens: null, cacheHitTokens: null, cacheMissTokens: null, durationMs: 100_000,
            EnglishStudioStrings.Instance, CultureInfo.InvariantCulture);

        Assert.Equal(["1 min 40 s"], chips);
    }

    [Fact]
    public void Nothing_measured_means_no_chip_at_all()
    {
        Assert.Empty(UsageMetricsFormatter.Chips(
            tokens: null, cacheHitTokens: null, cacheMissTokens: null, durationMs: null,
            EnglishStudioStrings.Instance, CultureInfo.InvariantCulture));

        // A measured-but-empty cache pair has nothing to ratio: no cache chip.
        var chips = UsageMetricsFormatter.Chips(
            tokens: 100, cacheHitTokens: 0, cacheMissTokens: 0, durationMs: null,
            EnglishStudioStrings.Instance, CultureInfo.InvariantCulture);
        Assert.Equal(["100 tokens"], chips);
    }
}
