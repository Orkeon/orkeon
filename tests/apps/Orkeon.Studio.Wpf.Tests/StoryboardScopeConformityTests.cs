using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// One rule, and it is worth its own file: a storyboard begun from a trigger that lives in a
/// <c>&lt;Style&gt;</c> may not use <c>Storyboard.TargetName</c>.
/// <para>
/// A <see cref="System.Windows.Style"/> has no namescope. WPF resolves TargetName when the
/// storyboard STARTS, not when the markup is parsed or compiled, so the build is clean, the
/// window opens, and the throw waits for whichever gesture first satisfies the trigger:
/// «'Slot' name cannot be found in the name scope of 'System.Windows.Style'».
/// </para>
/// <para>
/// That is exactly how it was found. The chat panel's slide-in was begun by a DataTrigger on
/// IsOpen inside a Border.Style; the throw came out of the IsOpen setter, up through
/// StartInterview, and into the Task <c>AsyncRelayCommand.Execute</c> discards — so «Compose a
/// team» was a live, ungreyed button that did nothing whatsoever. A storyboard with no
/// TargetName addresses the styled element itself, which is what these animations want anyway.
/// </para>
/// <para>
/// Read as text from the source tree, like <see cref="DesignTokenConformityTests"/>: no WPF, so
/// the rule holds on the Linux runner.
/// </para>
/// </summary>
public sealed partial class StoryboardScopeConformityTests
{
    [GeneratedRegex("""<Storyboard\b[^>]*x:Key="([^"]+)"(.*?)</Storyboard>""", RegexOptions.Singleline)]
    private static partial Regex KeyedStoryboardPattern();

    [GeneratedRegex("""<Style\b.*?</Style>""", RegexOptions.Singleline)]
    private static partial Regex StyleBlockPattern();

    // An escaped string, not a raw one: the pattern ends with a quote, which would run into
    // the closing delimiter.
    [GeneratedRegex("<BeginStoryboard[^>]*Storyboard=\"\\{StaticResource\\s+([^}]+)\\}\"")]
    private static partial Regex BeginStoryboardPattern();

    private static string WpfSourceRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf"));
    }

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(WpfSourceRoot(), "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void No_storyboard_begun_from_a_style_resolves_a_target_by_name()
    {
        var offenders = new List<string>();

        foreach (var file in XamlFiles())
        {
            var markup = File.ReadAllText(file);

            // Which storyboards in this file would need a namescope to start?
            var needsNamescope = KeyedStoryboardPattern().Matches(markup)
                .Where(m => m.Groups[2].Value.Contains("Storyboard.TargetName", StringComparison.Ordinal))
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);

            if (needsNamescope.Count == 0)
                continue;

            // ... and is any of them begun from inside a Style, where there is no namescope?
            foreach (var style in StyleBlockPattern().Matches(markup).Cast<Match>())
            {
                foreach (var begin in BeginStoryboardPattern().Matches(style.Value).Cast<Match>())
                {
                    var key = begin.Groups[1].Value.Trim();
                    if (!needsNamescope.Contains(key))
                        continue;

                    var line = markup.Take(style.Index + begin.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(file)}:{line} begins {key} from a Style");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A Style has no namescope, so Storyboard.TargetName cannot be resolved when the "
            + "storyboard starts — it throws at the first gesture that fires the trigger, long "
            + "after a clean build. Drop the TargetName and let the animation address the styled "
            + "element, or move the trigger to a ControlTemplate, which does have one. Offenders: "
            + string.Join("; ", offenders));
    }
}
