using System.Xml.Linq;
using Orkeon.Analysis.TreeSitter;

namespace Orkeon.Analysis.Tests;

/// <summary>
/// WIN-04 guard-rail. <c>src/Directory.Build.targets</c> drops every tree-sitter native
/// library outside a hard-coded whitelist from the publish output. That whitelist is
/// duplicated knowledge: the truth lives in <see cref="LanguageRegistry"/>. These tests
/// parse the whitelist straight out of the MSBuild file and pin it against the registry,
/// so adding a language without shipping its grammar fails here instead of at run time on
/// a user's machine.
/// </summary>
public sealed class TreeSitterGrammarPruningTests
{
    /// <summary>The shared tree-sitter runtime — not a grammar, and never prunable.</summary>
    private const string RuntimeLibrary = "tree-sitter";

    [Fact]
    public void Whitelist_CoversEveryLibraryTheRegistryCanLoad()
    {
        var whitelist = ReadWhitelist();

        var missing = LanguageRegistry.Libraries
            .Where(lib => !whitelist.Contains(lib))
            .OrderBy(lib => lib, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"LanguageRegistry loads native libraries absent from OrkeonTreeSitterKeptGrammars in "
            + $"{TargetsFileRelativePath}: {string.Join(", ", missing)}. Publishing would prune them "
            + "and the parser would fail at run time. Add them to the whitelist.");
    }

    [Fact]
    public void Whitelist_KeepsTheSharedRuntime()
    {
        // Pruning tree-sitter itself would break every grammar, whitelisted or not.
        Assert.Contains(RuntimeLibrary, ReadWhitelist());
    }

    [Fact]
    public void Whitelist_HasNoEntryBeyondTheRegistryRuntimeAndTsx()
    {
        // The reverse direction: an entry nobody loads is dead weight in every archive.
        // tree-sitter-tsx is the one deliberate extra — the package ships it as its own
        // library while the registry maps `tsx` onto tree-sitter-typescript.
        var allowed = LanguageRegistry.Libraries
            .Append(RuntimeLibrary)
            .Append("tree-sitter-tsx")
            .ToHashSet(StringComparer.Ordinal);

        var stale = ReadWhitelist()
            .Where(name => !allowed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            stale.Count == 0,
            $"OrkeonTreeSitterKeptGrammars ships grammars no language maps to: {string.Join(", ", stale)}.");
    }

    [Fact]
    public void Whitelist_EntriesAreBareLibraryNames()
    {
        // The MSBuild filter matches on %(Filename), so an extension or a `lib` prefix
        // in the whitelist would silently never match and the grammar would be pruned.
        foreach (var name in ReadWhitelist())
        {
            Assert.StartsWith("tree-sitter", name, StringComparison.Ordinal);
            Assert.DoesNotContain('.', name);
        }
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
