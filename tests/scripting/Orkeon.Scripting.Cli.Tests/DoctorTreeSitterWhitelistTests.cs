using System.Xml.Linq;
using Orkeon.Scripting.Cli.Commands;

namespace Orkeon.Scripting.Cli.Tests;

/// <summary>
/// WIN-03 guard-rail. <see cref="DoctorCommand.TreeSitterLibraries"/> duplicates the
/// publish-pruning whitelist of <c>src/Directory.Build.targets</c>
/// (<c>OrkeonTreeSitterKeptGrammars</c>): a grammar kept by publishing but absent from
/// doctor would never be flagged, and one doctor checks but publishing prunes would warn
/// on every healthy install. Same parse-the-MSBuild-file technique as
/// <c>TreeSitterGrammarPruningTests</c> (Analysis.Tests), so any drift breaks here.
/// </summary>
public sealed class DoctorTreeSitterWhitelistTests
{
    [Fact]
    public void Doctor_ChecksExactlyThePruningWhitelist()
    {
        var whitelist = ReadWhitelist()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var doctor = DoctorCommand.TreeSitterLibraries
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(whitelist, doctor);
    }

    private const string TargetsFileRelativePath = "src/Directory.Build.targets";

    private static IReadOnlyCollection<string> ReadWhitelist()
    {
        var path = Path.Combine(RepoRoot(), "src", "Directory.Build.targets");
        Assert.True(File.Exists(path), $"Missing {TargetsFileRelativePath} (expected at {path}).");

        var document = XDocument.Load(path);
        var property = document
            .Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "OrkeonTreeSitterKeptGrammars");

        Assert.NotNull(property);

        var names = property!.Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(names);
        return names;
    }

    private static string RepoRoot()
    {
        // Climb from bin/<config>/<tfm>/ until the solution file shows up rather than
        // counting "..": the test output layout has moved before.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Orkeon.sln")))
            dir = dir.Parent;

        Assert.True(dir is not null, $"Could not locate the repo root above {AppContext.BaseDirectory}.");
        return dir!.FullName;
    }
}
