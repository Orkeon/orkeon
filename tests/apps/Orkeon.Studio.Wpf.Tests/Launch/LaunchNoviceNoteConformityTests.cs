using System.Runtime.CompilerServices;

namespace Orkeon.Studio.Wpf.Tests.Launch;

/// <summary>
/// UX lot 02 (mod. 1): the Launch screen's team-type note names « orkeon run », the CLI and a version
/// for a multi-file folder; the Novice reads the detected shape instead. The view is read as text.
/// </summary>
public sealed class LaunchNoviceNoteConformityTests
{
    private static string View([CallerFilePath] string thisFile = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!,
            "..", "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf", "Views", "LaunchTabView.xaml")));

    [Fact]
    public void The_team_type_note_shows_in_Expert_only_and_the_Novice_reads_the_shape()
    {
        var view = View();

        Assert.DoesNotContain("Text=\"{Binding TargetTypeNote}\"", view, StringComparison.Ordinal);
        var expert = view.IndexOf("<DataTrigger Binding=\"{Binding IsExpertMode}\" Value=\"True\">", StringComparison.Ordinal);
        var note = view.IndexOf("Value=\"{Binding TargetTypeNote}\"", StringComparison.Ordinal);
        Assert.True(expert >= 0 && note > expert && note - expert < 200, "TargetTypeNote is set only by the Expert trigger.");
        Assert.Contains("<Setter Property=\"Text\" Value=\"{Binding Target.StatusDisplay}\"/>", view, StringComparison.Ordinal);
    }
}
