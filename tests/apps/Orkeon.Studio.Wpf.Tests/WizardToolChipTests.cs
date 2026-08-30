using Orkeon.Studio.Core.Localization;
using Orkeon.Studio.Wpf.ViewModels.Teams;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// The tool chips of the wizard (30/08 mock, T-18): what an agent may do, said in words the
/// person reading the card can act on rather than in the engine's identifiers.
/// </summary>
public sealed class WizardToolChipTests
{
    private static WizardToolChip Chip(string id, string? scope = null) =>
        WizardToolChips.For(id, EnglishStudioStrings.Instance, scope);

    [Theory]
    [InlineData("fs.read", "folder-open")]
    [InlineData("fs.list", "eye")]
    [InlineData("fs.write", "pencil")]
    [InlineData("llm.complete", "sparkles")]
    [InlineData("web.fetch", "route")]
    [InlineData("rag.search", "scan-search")]
    public void Every_known_tool_carries_its_own_glyph(string id, string icon)
    {
        Assert.Equal(icon, Chip(id).Icon);
    }

    [Fact]
    public void A_filesystem_tool_says_which_folder_it_touches()
    {
        // The scope IS the answer to «what will this agent actually reach» — a chip that
        // said only «read» would leave the one question the card exists to answer open.
        Assert.Equal("read /docs", Chip("fs.read").Label);
        Assert.Equal("write /output", Chip("fs.write").Label);
        Assert.Equal("list /archive", Chip("fs.list", "/archive").Label);
    }

    [Fact]
    public void A_tool_with_no_folder_of_its_own_says_what_it_does()
    {
        Assert.Equal("write with the AI", Chip("llm.complete").Label);
        Assert.Equal("browse a web page", Chip("web.fetch").Label);
        Assert.DoesNotContain("/", Chip("rag.search").Label, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_studio_has_never_heard_of_keeps_its_own_name()
    {
        // The engine can grow a tool before Studio learns its label. Showing the raw id is
        // ugly; hiding the capability entirely is worse.
        var chip = Chip("db.query");

        Assert.Equal("db.query", chip.Label);
        Assert.Equal("braces", chip.Icon);
    }

    [Fact]
    public void The_label_comes_from_the_catalogue_so_it_follows_a_language_switch()
    {
        var french = new FrenchToolStrings();

        Assert.Equal("lire /docs", WizardToolChips.For("fs.read", french).Label);
        Assert.Equal("rédiger avec l'IA", WizardToolChips.For("llm.complete", french).Label);
    }

    private sealed class FrenchToolStrings : IStudioStrings
    {
        private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
        {
            [StudioStringKeys.ToolReadScoped] = "lire {0}",
            [StudioStringKeys.ToolLlm] = "rédiger avec l'IA",
        };

        public string this[string key] => Table.GetValueOrDefault(key, key);

        public event EventHandler? CultureChanged { add { } remove { } }
    }
}

/// <summary>
/// The console's four brushes, and the run-cost line under a finished run (T-23, DS-8).
/// </summary>
public sealed class RunLogAndMetricsTests
{
    [Fact]
    public void A_log_line_is_classified_by_whoever_produced_it()
    {
        var log = new Orkeon.Studio.Wpf.ViewModels.Launch.RunLogViewModel();

        log.AppendCommand("orkeon run ./crew");
        log.AppendOutcome("Finished without errors.");
        log.AppendNotice("step 1/3");
        log.AppendError("boom");

        // Never sniffed from the text: the outcome sentence is localized, so a regex over
        // it would colour the console in English and leave it grey everywhere else.
        Assert.True(log.Lines[0].IsCommand);
        Assert.False(log.Lines[0].IsOutcome);
        Assert.True(log.Lines[1].IsOutcome);
        Assert.False(log.Lines[2].IsCommand);
        Assert.False(log.Lines[2].IsOutcome);
        Assert.True(log.Lines[3].IsError);
    }

    [Fact]
    public void A_metric_the_stream_did_not_measure_produces_no_chip_at_all()
    {
        // A zero would read as «it cost nothing», which is a different statement from
        // «nobody counted».
        var none = Orkeon.Studio.Core.Launch.UsageMetricsFormatter.Chips(null, null, null, null);
        Assert.Empty(none);

        var some = Orkeon.Studio.Core.Launch.UsageMetricsFormatter.Chips(12_840, 7_980, 4_860, 59_000);
        Assert.Equal(3, some.Count);
        Assert.Contains(some, c => c.Contains("12", StringComparison.Ordinal));
        Assert.Contains(some, c => c.Contains("62", StringComparison.Ordinal));   // cache hit, in %
        Assert.Contains(some, c => c.Contains("980", StringComparison.Ordinal));  // …and in tokens
    }
}
