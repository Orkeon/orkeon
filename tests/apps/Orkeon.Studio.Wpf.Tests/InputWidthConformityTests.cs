using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Orkeon.Studio.Wpf.Tests;

/// <summary>
/// A WPF <c>TextBox</c> has no width of its own: left-aligned under a <c>MaxWidth</c> alone, it
/// measures to its text, and an empty field shrinks to its padding — a square the size of a
/// character, which is what the E-mail tab showed on a machine with no account (the name and
/// the address of the account to add, then every empty field of an account's form). The house
/// rule: a left-aligned editable <c>TextBox</c> carries a <c>Width</c>, or a <c>MinWidth</c> when
/// it may grow with a paragraph. A read-only box that wraps a sentence sizes to it and may keep
/// a <c>MaxWidth</c> alone. The XAML is read as text from the
/// source tree, no WPF involved, so the rule holds on the Linux runner.
/// </summary>
public sealed partial class InputWidthConformityTests
{
    [GeneratedRegex("<TextBox\\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex TextBoxTag();

    private static string ViewsRoot([CallerFilePath] string thisFile = "")
    {
        var testsDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testsDir, "..", "..", "..", "src", "apps", "Orkeon.Studio.Wpf", "Views"));
    }

    [Fact]
    public void A_left_aligned_editable_text_box_carries_a_width()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ViewsRoot(), "*.xaml", SearchOption.AllDirectories))
        {
            var markup = File.ReadAllText(file);
            foreach (Match match in TextBoxTag().Matches(markup))
            {
                var tag = match.Value;
                if (!tag.Contains("HorizontalAlignment=\"Left\"", StringComparison.Ordinal)
                    || tag.Contains(" Width=\"", StringComparison.Ordinal)
                    || tag.Contains("MinWidth=\"", StringComparison.Ordinal)
                    || tag.Contains("IsReadOnly=\"True\"", StringComparison.Ordinal))
                {
                    continue;
                }

                var line = markup[..match.Index].Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetFileName(file)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "An empty left-aligned TextBox without a Width or a MinWidth shrinks to a square: " + string.Join(", ", offenders));
    }
}
