using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Per-entity directory layout exercised against a real on-disk tree through
/// <see cref="DiskBackedFileSystemService"/>, complementing the in-memory fixtures of
/// <see cref="PerEntityDirectoryLayoutTests"/>. The loader enumerates <c>agents/*.yaml</c> via
/// <c>IFileSystemService.EnumerateFilesAsync</c>, so the real enumeration semantics (search
/// pattern, non-recursive scope, virtual paths) need their own coverage: a fake that returns
/// everything would hide a mismatch here.
/// </summary>
public sealed class PerEntityDirectoryDiskLayoutTests : IDisposable
{
    private const string VirtualRoot = "/crew";

    private readonly string _physicalRoot = Path.Combine(
        Path.GetTempPath(), "ork-perentity-" + Guid.NewGuid().ToString("N"));

    public PerEntityDirectoryDiskLayoutTests() => Directory.CreateDirectory(_physicalRoot);

    public void Dispose()
    {
        try { Directory.Delete(_physicalRoot, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_physicalRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private YamlCrewDefinitionLoader NewLoader()
        => new(
            new YamlDotNetSerializer(),
            new DiskBackedFileSystemService(_physicalRoot, VirtualRoot),
            NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task ShouldMergeEntityFilesIntoOneConfiguration_FromRealDisk()
    {
        // Arrange
        Write("config.yaml", "name: disk-crew\ngoal: Merge entity files\nprocess: sequential");
        Write("agents/researcher.yaml", "role: Researcher\ngoal: Find data");
        Write("agents/writer.yaml", "role: Writer\ngoal: Write report");
        Write("tasks/collect.yaml", "description: Collect data\nexpected_output: Raw data\nagent: researcher");
        Write("tasks/report.yaml", "description: Write the report\nexpected_output: Final report\nagent: writer");

        // Act
        var config = await NewLoader().LoadFromDirectoryAsync(VirtualRoot, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("disk-crew", config.Name);
        Assert.Equal(2, config.Agents.Count);
        Assert.Equal(2, config.Tasks.Count);
        var writer = config.Agents.Single(a => a.Role == "Writer");
        var report = config.Tasks.Single(t => t.Description == "Write the report");
        Assert.Equal(writer.Id, report.AssignedAgentId);
    }

    [Fact]
    public async Task ShouldIgnoreNonYamlAndNestedFiles_WhenEnumeratingEntityFolders()
    {
        // Arrange — only *.yaml directly inside agents/ counts as an agent file.
        Write("config.yaml", "name: c\ngoal: G\nprocess: sequential");
        Write("agents/kept.yaml", "role: Kept\ngoal: G");
        Write("agents/README.md", "not an agent");
        Write("agents/draft.yaml.bak", "role: Ignored\ngoal: G");
        Write("agents/archive/old.yaml", "role: Nested\ngoal: G");
        Write("tasks/t.yaml", "description: D\nexpected_output: O\nagent: kept");

        // Act
        var config = await NewLoader().LoadFromDirectoryAsync(VirtualRoot, TestContext.Current.CancellationToken);

        // Assert
        var agent = Assert.Single(config.Agents);
        Assert.Equal("Kept", agent.Role);
    }

    [Fact]
    public async Task ShouldThrowFileNotFound_WhenNoSettingsFileAccompaniesTheEntityFolders()
    {
        // Arrange — agents/ selects the per-entity mode, but neither config.yaml nor crew.yaml exists.
        Write("agents/a.yaml", "role: R\ngoal: G");
        Write("tasks/t.yaml", "description: D\nexpected_output: O\nagent: a");

        // Act + Assert
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(
            () => NewLoader().LoadFromDirectoryAsync(VirtualRoot, TestContext.Current.CancellationToken));
        Assert.Contains("config.yaml", ex.Message, StringComparison.Ordinal);
    }
}
