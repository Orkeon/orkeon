using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// The corpus (GAP-39): every crew of <c>examples/</c> and the anchors fixture make the round trip. A
/// <c>config.yaml</c> or <c>crew.yaml</c> next to an <c>agents/</c> or <c>tasks/</c> folder is a crew directory,
/// read as one through the disk; any other YAML file with a top-level <c>agents:</c> or <c>tasks:</c> is a crew
/// read from its text.
/// </summary>
public partial class YamlCrewExportRoundTripTests
{
    private const string AnchorsFixture = "tests/core/Orkeon.Infrastructure.Tests/TestData/crew-with-anchors.yaml";

    /// <summary>Folders a build or a package manager fills: never part of the corpus.</summary>
    private static readonly string[] OutputFolders = ["bin", "obj", "obj-linux", "node_modules"];

    /// <summary>Each crew of the corpus, by its path from the repository root — a directory ends in <c>/</c>.</summary>
    public static TheoryData<string> CorpusCrews() => [.. CorpusCrewPaths()];

    private static List<string> CorpusCrewPaths()
    {
        var root = RepositoryRoot();
        var examples = Path.Combine(root, "examples");

        var directories = Directory.EnumerateDirectories(examples, "*", SearchOption.AllDirectories)
            .Where(directory => !InOutputFolder(directory) && IsCrewDirectory(directory))
            .Order(StringComparer.Ordinal)
            .ToList();
        var files = Directory.EnumerateFiles(examples, "*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            .Where(file => !InOutputFolder(file))
            .Where(file => !directories.Any(directory => file.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .Where(file => CrewFileShape().IsMatch(File.ReadAllText(file)))
            .Order(StringComparer.Ordinal);

        return
        [
            .. directories.Select(directory => RelativePath(root, directory) + "/"),
            .. files.Select(file => RelativePath(root, file)),
            AnchorsFixture,
        ];
    }

    [Fact]
    public void The_corpus_holds_the_example_crews_of_every_layout()
    {
        var crews = CorpusCrewPaths();

        Assert.Contains("examples/crew-multifile/", crews);
        Assert.Contains("examples/forge/promote-demo/.orkeon/forge/supplier-watch/crew/", crews);
        Assert.Contains("examples/quickstart/crew.yaml", crews);
        Assert.Contains(AnchorsFixture, crews);
        Assert.True(crews.Count > 90, $"The corpus holds {crews.Count} crews: examples/ was not found whole.");
    }

    [Theory]
    [MemberData(nameof(CorpusCrews))]
    public async Task Every_crew_of_the_corpus_comes_back_the_same(string crew)
    {
        var loaded = await LoadCorpusCrewAsync(crew);

        var first = _exporter.ExportToString(loaded);
        var reloaded = await _loader.LoadFromStringAsync(first, TestContext.Current.CancellationToken);
        CrewConfigurationProjection.AssertEqual(loaded, reloaded);
        Assert.Equal(first, _exporter.ExportToString(reloaded));

        await _exporter.ExportToDirectoryAsync(loaded, "/out/crew", TestContext.Current.CancellationToken);
        CrewConfigurationProjection.AssertEqual(
            loaded, await _loader.LoadFromDirectoryAsync("/out/crew", TestContext.Current.CancellationToken));
    }

    private async Task<CrewConfiguration> LoadCorpusCrewAsync(string crew)
    {
        var physical = Path.Combine(RepositoryRoot(), crew.TrimEnd('/'));
        if (!crew.EndsWith('/'))
            return await _loader.LoadFromStringAsync(
                await File.ReadAllTextAsync(physical, TestContext.Current.CancellationToken),
                TestContext.Current.CancellationToken);

        var disk = new DiskBackedFileSystemService(physical, "/crew");
        var loader = new YamlCrewDefinitionLoader(_serializer, disk, NullLogger<YamlCrewDefinitionLoader>.Instance);
        return await loader.LoadFromDirectoryAsync("/crew", TestContext.Current.CancellationToken);
    }

    private static bool IsCrewDirectory(string directory) =>
        (File.Exists(Path.Combine(directory, "config.yaml")) || File.Exists(Path.Combine(directory, "crew.yaml")))
        && (Directory.Exists(Path.Combine(directory, "agents")) || Directory.Exists(Path.Combine(directory, "tasks")));

    private static bool InOutputFolder(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(segment => OutputFolders.Contains(segment, StringComparer.Ordinal));

    private static string RelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>The repository root: the first folder above the test binaries that holds <c>Orkeon.sln</c>.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Orkeon.sln")))
            directory = directory.Parent;

        Assert.True(directory is not null, $"Could not locate the repository root above {AppContext.BaseDirectory}.");
        return directory!.FullName;
    }

    [GeneratedRegex(@"^(agents|tasks)[ \t]*:", RegexOptions.Multiline)]
    private static partial Regex CrewFileShape();
}
