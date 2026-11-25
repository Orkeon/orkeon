using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>The <c>files:</c> block of a crew: the folders it opens to the people who talk to it.</summary>
public sealed class CrewFilesYamlTests
{
    private const string Crew = """

        name: support
        goal: g
        agents:
          worker:
            goal: work
        tasks:
          job:
            description: d
            expectedOutput: o
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static YamlCrewDefinitionLoader Loader(FakeFileSystemService? fs = null) =>
        new(new YamlDotNetSerializer(), fs ?? new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    /// <summary>What a run or a validation refuses the crew for: the load, then the rules of a loaded crew.</summary>
    private static async Task<string> RefusalAsync(string yaml)
    {
        try
        {
            var validation = Loader().Validate(await Loader().LoadFromStringAsync(yaml, Ct));
            Assert.False(validation.IsValid);
            return string.Join(" ", validation.Errors);
        }
        catch (InvalidOperationException ex)
        {
            return ex.ToString();
        }
    }

    private static string[] Text(IReadOnlyList<CrewFileFolder>? folders) =>
        [.. folders!.Select(f => $"{f.Path} {f.Download} {f.Upload}")];

    [Fact]
    public async Task A_block_comes_back_as_it_was_written()
    {
        const string block = "files:\n  - path: /output/\n    download: true\n  - path: /output/depots\n    upload: true\n";

        var config = await Loader().LoadFromStringAsync(block + Crew, Ct);
        var exported = new YamlCrewExporter(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewExporter>.Instance).ExportToString(config);
        var again = await Loader().LoadFromStringAsync(exported, Ct);

        Assert.Equal(["/output True False", "/output/depots False True"], Text(config.Files));
        Assert.Equal(Text(config.Files), Text(again.Files));
        Assert.DoesNotContain("upload: false", exported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crew_directory_declares_them_in_its_settings()
    {
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/crm/config.yaml", "name: crm\ngoal: g\nfiles:\n  - path: /output\n    download: true\n    upload: true\n");
        fs.AddFile("/crews/crm/agents/a.yaml", "role: R\ngoal: G");
        fs.AddFile("/crews/crm/tasks/t.yaml", "description: D\nexpected_output: O\nagent: a");

        var config = await Loader(fs).LoadFromDirectoryAsync("/crews/crm", Ct);

        Assert.Equal(["/output True True"], Text(config.Files));
    }

    [Theory]
    [InlineData("files: /output", "Invalid")]
    [InlineData("files:\n  - /output", "must be a block of keys")]
    [InlineData("files:\n  - path: /output\n    downlod: true", "Did you mean 'download'?")]
    [InlineData("files:\n  - path: /output\n    download: perhaps", "perhaps")]
    [InlineData("files:\n  - path: /output", "opens nothing")]
    [InlineData("files:\n  - download: true", "missing")]
    [InlineData("files:\n  -", "missing")]
    [InlineData("files:\n  - path: output\n    download: true", "starts with '/'")]
    [InlineData("files:\n  - path: /\n    download: true", "no folder")]
    [InlineData("files:\n  - path: /output/../secret\n    download: true", "segment")]
    [InlineData("files:\n  - path: /output/./a\n    download: true", "segment")]
    [InlineData("files:\n  - path: /output//a\n    download: true", "segment")]
    [InlineData("files:\n  - path: /output/private.\n    download: true", "segment")]
    [InlineData("files:\n  - path: /output\\a\n    download: true", "'\\'")]
    [InlineData("files:\n  - path: /output\n    download: true\n  - path: /Output/\n    upload: true", "listed twice")]
    public async Task A_block_that_cannot_be_served_fails_the_load_and_the_probe(string block, string expected)
    {
        var refused = await RefusalAsync(block + "\n" + Crew);
        var probed = CrewFilesBlock.ReadCrew(block + "\n" + Crew, "/crews/support.yaml");
        var neighbour = CrewFilesBlock.ReadNeighbour(block + "\n", "/crews/support.files.yaml");

        if (expected != "perhaps")
            Assert.Contains(expected, refused, StringComparison.Ordinal);
        Assert.Null(probed.Folders);
        Assert.NotNull(probed.Fault);
        Assert.NotNull(neighbour.Fault);
        if (expected != "perhaps")
            Assert.Contains(expected, probed.Fault, StringComparison.Ordinal);
        Assert.Contains("/crews/support.yaml", probed.Fault, StringComparison.Ordinal);
    }

    [Fact]
    public void The_probe_of_a_crew_leaves_its_other_keys_to_the_loader()
    {
        var probed = CrewFilesBlock.ReadCrew("files:\n  - path: /output\n    download: true\nnonsense: 1\nagents: 3\n", "/c.yaml");

        Assert.Null(probed.Fault);
        Assert.Equal(["/output True False"], Text(probed.Folders));
    }

    [Fact]
    public void The_file_beside_a_script_holds_nothing_else()
    {
        var probed = CrewFilesBlock.ReadNeighbour("name: support\nfiles: []\n", "/c.files.yaml");

        Assert.Contains("'name' is not a key", probed.Fault, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("name: support\n")]
    [InlineData("files:\n")]
    public void No_block_is_no_fault(string yaml)
    {
        var probed = CrewFilesBlock.ReadCrew(yaml, "/c.yaml");

        Assert.Null(probed.Fault);
        Assert.Null(probed.Folders);
    }

    [Theory]
    [InlineData("/crews/a.ork.ts", "/crews/a.files.yaml")]
    [InlineData("/crews/a.ORK.JS", "/crews/a.files.yaml")]
    [InlineData("/crews/a.yaml", null)]
    public void The_file_beside_a_script_is_named_after_it(string path, string? expected) =>
        Assert.Equal(expected, CrewFilesBlock.NeighbourOf(path));
}
