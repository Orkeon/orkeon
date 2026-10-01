using System.Text.RegularExpressions;

namespace Orkeon.Compliance.Vfs.Tests;

/// <summary>
/// Guards the rule of docs/architecture/vfs-compliance.md ("Suppression categories"): every
/// <c>[SuppressVfsCompliance]</c> reason in <c>src/</c> starts with a ratified category. Nothing
/// checked it before GAP-16, and four Studio reasons drifted without one.
/// </summary>
public sealed partial class SuppressionReasonCategoryTests
{
    private static readonly string[] s_ratifiedCategories =
    [
        "EXCEPTION-BOOTSTRAP",
        "EXCEPTION-WATCHER-BRIDGE",
        "OUT-OF-SCOPE",
    ];

    [GeneratedRegex("""SuppressVfsCompliance\(\s*(?:reason:\s*)?[@$]*"(?<reason>[^"]*)""")]
    private static partial Regex ReasonPattern();

    [Fact]
    public void Every_Suppression_Reason_In_Src_Starts_With_A_Ratified_Category()
    {
        var src = Path.Combine(FindRepositoryRoot(), "src");
        var offenders = new List<string>();
        var count = 0;

        foreach (var file in EnumerateSources(src))
        {
            var text = File.ReadAllText(file);
            var index = text.IndexOf("SuppressVfsCompliance(", StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                var match = ReasonPattern().Match(text, index);
                var relative = Path.GetRelativePath(src, file);
                if (!match.Success || match.Index != index)
                    offenders.Add($"{relative}: reason is not a string literal");
                else if (!s_ratifiedCategories.Any(c => HasCategory(match.Groups["reason"].Value, c)))
                    offenders.Add($"{relative}: \"{match.Groups["reason"].Value}\"");

                index = text.IndexOf("SuppressVfsCompliance(", index + 1, StringComparison.Ordinal);
            }
        }

        Assert.True(count > 0, "No [SuppressVfsCompliance] found under src/ — the scan is looking in the wrong place.");
        Assert.True(offenders.Count == 0,
            "Suppression reasons without a ratified category (" + string.Join(", ", s_ratifiedCategories) + "):"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static bool HasCategory(string reason, string category) =>
        reason.StartsWith(category, StringComparison.Ordinal)
        && (reason.Length == category.Length || (!char.IsLetterOrDigit(reason[category.Length]) && reason[category.Length] != '-'));

    private static IEnumerable<string> EnumerateSources(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
            yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (name is "bin" or "obj")
                continue;
            foreach (var file in EnumerateSources(child))
                yield return file;
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Orkeon.sln not found above " + AppContext.BaseDirectory);
    }
}
