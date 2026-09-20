using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// VFS-90: the <c>mounts:</c> block is how a crew names the settings entries it expects — a
/// root, or a root pinned to one entry by its id. Parsing and validation only; the selection
/// itself is the host's job (<c>MountSelection</c>).
/// </summary>
public class CrewMountsYamlParsingTests
{
    private const string Id = "01J9Z3K4M5N6P7Q8R9S0T1V2W3";

    private static YamlCrewDefinitionLoader BuildLoader(FakeFileSystemService? fs = null)
        => new(new YamlDotNetSerializer(), fs ?? new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task LoadFromString_MapsRootsAndPinnedRoots()
    {
        var yaml = $"""
name: crm
goal: g
mounts:
  - /workspace
  - {Id}|/output
agents:
  worker:
    role: Worker
    goal: work
""";

        var config = await BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.NotNull(config.Mounts);
        Assert.Equal(2, config.Mounts!.Count);
        Assert.Null(config.Mounts[0].Id);
        Assert.Equal("/workspace", config.Mounts[0].VirtualRoot);
        Assert.Equal(Id, config.Mounts[1].Id!.ToString());
        Assert.Equal("/output", config.Mounts[1].VirtualRoot);
    }

    [Fact]
    public async Task LoadFromDirectory_ReadsTheBlockOfConfigYaml()
    {
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/crm/config.yaml", $"name: crm\ngoal: g\nmounts:\n  - /output\n  - {Id}|/workspace\n");
        fs.AddFile("/crews/crm/agents/a.yaml", "role: R\ngoal: G");
        fs.AddFile("/crews/crm/tasks/t.yaml", "description: D\nexpected_output: O\nagent: a");

        var config = await BuildLoader(fs).LoadFromDirectoryAsync("/crews/crm", TestContext.Current.CancellationToken);

        Assert.Equal(["/output", $"{Id}|/workspace"], config.Mounts!.Select(m => m.ToString()));
    }

    [Fact]
    public async Task LoadFromString_WithoutTheBlock_LeavesMountsNull()
    {
        var config = await BuildLoader().LoadFromStringAsync(
            "name: crm\ngoal: g\nagents:\n  worker:\n    role: Worker\n    goal: work\n",
            TestContext.Current.CancellationToken);

        Assert.Null(config.Mounts);
    }

    [Fact]
    public async Task LoadFromString_WithAnEmptyBlock_LeavesAnEmptyList()
    {
        var config = await BuildLoader().LoadFromStringAsync(
            "name: crm\ngoal: g\nmounts: []\nagents:\n  worker:\n    role: Worker\n    goal: work\n",
            TestContext.Current.CancellationToken);

        Assert.NotNull(config.Mounts);
        Assert.Empty(config.Mounts!);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("01J9Z3K4M5N6P7Q8R9S0T1V2W3")]
    [InlineData("nope|/output")]
    public async Task LoadFromString_RefusesAnItemThatIsNeitherForm(string item)
    {
        // Strict, unlike links: a dropped selection would resurface at startup as a refusal
        // about a root the author never meant.
        var yaml = $"name: crm\ngoal: g\nmounts:\n  - '{item}'\nagents:\n  worker:\n    role: Worker\n    goal: work\n";

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken));

        Assert.Contains($"mounts: entry '{item}'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'<ulid>|/root'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_RefusesTwoIdsOnOneRoot_AndAnItemListedTwice()
    {
        var other = "01J9Z3K4M5N6P7Q8R9S0T1V2W4";
        var config = new CrewConfiguration
        {
            Name = "crm",
            Goal = "g",
            Agents = [new AgentConfiguration { Role = "R", Goal = "G" }],
            Tasks = [new TaskConfiguration { Description = "D", ExpectedOutput = "O" }],
            Mounts =
            [
                MountReference.Parse($"{Id}|/output"),
                MountReference.Parse($"{other}|/output"),
                MountReference.Parse("/workspace"),
                MountReference.Parse("/workspace/"),
            ],
        };

        var result = CrewDefinitionValidator.Validate(config);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("two ids select '/output'", StringComparison.Ordinal)
                                            && e.Contains(Id, StringComparison.Ordinal)
                                            && e.Contains(other, StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("'/workspace' is listed twice", StringComparison.Ordinal));
    }

    [Fact]
    public void Validation_AcceptsARootAndItsPinnedTwin()
    {
        var config = new CrewConfiguration
        {
            Name = "crm",
            Goal = "g",
            Agents = [new AgentConfiguration { Role = "R", Goal = "G" }],
            Tasks = [new TaskConfiguration { Description = "D", ExpectedOutput = "O" }],
            Mounts = [MountReference.Parse("/output"), MountReference.Parse($"{Id}|/output")],
        };

        var result = CrewDefinitionValidator.Validate(config);

        Assert.DoesNotContain(result.Errors, e => e.Contains("mounts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Export_WritesTheBlockBackTheWayTheLoaderReadsIt()
    {
        var fs = new FakeFileSystemService();
        var loader = BuildLoader(fs);
        var exporter = new YamlCrewExporter(new YamlDotNetSerializer(), fs, NullLogger<YamlCrewExporter>.Instance);
        var config = await loader.LoadFromStringAsync(
            $"name: crm\ngoal: g\nmounts:\n  - /workspace\n  - {Id}|/output\nagents:\n  worker:\n    role: Worker\n    goal: work\ntasks:\n  t:\n    description: D\n    expected_output: O\n    agent: worker\n",
            TestContext.Current.CancellationToken);

        var yaml = exporter.ExportToString(config);
        var reloaded = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Contains("mounts:", yaml, StringComparison.Ordinal);
        Assert.Equal(["/workspace", $"{Id}|/output"], reloaded.Mounts!.Select(m => m.ToString()));
    }
}
