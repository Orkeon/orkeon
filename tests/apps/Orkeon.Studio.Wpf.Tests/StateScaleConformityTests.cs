using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// UX lot 11 (mod. 14, 15): the state scale of the badges. The converter lives in the WPF project,
/// which this suite does not reference (it would pin the suite to Windows): its table is read from
/// the source, and the views are scanned for the string triggers it replaced.
/// </summary>
public sealed partial class StateScaleConformityTests
{
    private static string WpfSourceRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));

    [Theory]
    [InlineData("ok", "Success")]
    [InlineData("warn", "Warning")]
    [InlineData("running", "Info")]
    [InlineData("fail", "Danger")]
    public void StateToneConverter_maps_each_state_to_its_tone(string state, string tone)
    {
        var source = File.ReadAllText(Path.Combine(WpfSourceRoot(), "Views", "Converters.cs"));

        Assert.Contains($"\"{state}\" => Controls.Tone.{tone},", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StateToneConverter_falls_back_to_neutral_for_muted_idle_and_the_unknown()
    {
        var source = File.ReadAllText(Path.Combine(WpfSourceRoot(), "Views", "Converters.cs"));

        Assert.Contains("_ => Controls.Tone.Neutral,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"muted\" =>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"idle\" =>", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("TeamsView.xaml")]
    [InlineData("LaunchTabView.xaml")]
    public void The_badges_take_their_tone_from_the_converter_not_from_string_triggers(string view)
    {
        var text = File.ReadAllText(Path.Combine(WpfSourceRoot(), "Views", view));

        Assert.Contains("StateToneConverter", text, StringComparison.Ordinal);
        Assert.DoesNotMatch(StateTriggerPattern(), text);
    }

    [GeneratedRegex("Value=\"(ok|warn|muted|fail)\"")]
    private static partial Regex StateTriggerPattern();

    [Fact]
    public void The_run_badge_and_the_readiness_dot_read_no_state_string_in_the_launch_view()
    {
        // Lot 11 criterion 8: the pulse rides a flag, the readiness dot the converter's dot scale.
        var text = File.ReadAllText(Path.Combine(WpfSourceRoot(), "Views", "LaunchTabView.xaml"));

        Assert.DoesNotContain("Binding RunBadgeTone}\" Value=", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding LaunchReadinessTone}\" Value=", text, StringComparison.Ordinal);
        Assert.Contains("LaunchReadinessTone, Converter={StaticResource StateToneConverter}", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Success", "Ready")]
    [InlineData("Warning", "Attention")]
    [InlineData("Danger", "Error")]
    [InlineData("_", "Neutral")]
    public void StateToneConverter_gives_a_status_dot_its_side_of_the_scale(string tone, string dotTone)
    {
        var source = File.ReadAllText(Path.Combine(WpfSourceRoot(), "Views", "Converters.cs"));
        var arm = tone == "_" ? "_" : $"Controls.Tone.{tone}";

        Assert.Contains($"{arm} => Controls.DotTone.{dotTone},", source, StringComparison.Ordinal);
    }

    [Fact]
    public void No_view_or_theme_keeps_the_retired_badge_styles()
    {
        var offenders = Directory.EnumerateFiles(WpfSourceRoot(), "*.xaml", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => RetiredBadgeStylePattern().IsMatch(File.ReadAllText(file)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    [GeneratedRegex("Badge(Base|Neutral|Sky|Ok|Warn|Fail|Run)\\b")]
    private static partial Regex RetiredBadgeStylePattern();
}
