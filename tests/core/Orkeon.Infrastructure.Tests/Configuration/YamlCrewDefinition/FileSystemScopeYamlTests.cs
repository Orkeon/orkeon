using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// #349 and #348 in YAML: an agent's <c>filesystem:</c> list (a path, or <c>{path, access}</c>) and
/// <c>announce_mounts:</c> at agent or crew level reach the configuration, survive an export, and a
/// malformed entry is refused at load naming the agent.
/// </summary>
public sealed class FileSystemScopeYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Yaml(string crewLine = "", string writerLines = "") => $$"""
        name: scoped
        goal: Write within bounds
        process: sequential
        {{crewLine}}
        agents:
          writer:
            role: Writer
            goal: Write the report
        {{writerLines}}
          reader:
            role: Reader
            goal: Read the report
        tasks:
          write:
            description: Write the report
            expected_output: A report
            agent: writer
        """;

    private static YamlCrewDefinitionLoader Loader(FakeFileSystemService? fs = null)
        => new(new YamlDotNetSerializer(), fs ?? new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    private static async Task<(AgentConfiguration Writer, AgentConfiguration Reader)> LoadAsync(string yaml)
    {
        var config = await Loader().LoadFromStringAsync(yaml, Ct);
        return (config.Agents.Single(a => a.Role == "Writer"), config.Agents.Single(a => a.Role == "Reader"));
    }

    private const string Scope = """
            filesystem:
              - /workspace/docs
              - path: /output/report
                access: rwnd
              - { path: /workspace/src, access: ro }
        """;

    [Fact]
    [Trait("BR", "#349")]
    public async Task ShouldMapEntries_WhenAgentDeclaresFilesystem()
    {
        var (writer, reader) = await LoadAsync(Yaml(writerLines: Scope));

        Assert.Equal<FileSystemScopeEntry>(
            [
                new FileSystemScopeEntry("/workspace/docs"),
                new FileSystemScopeEntry("/output/report", FileAccessRights.ReadWriteNoDelete),
                new FileSystemScopeEntry("/workspace/src", FileAccessRights.ReadOnly),
            ],
            writer.FileSystem);
        Assert.Empty(reader.FileSystem);
    }

    [Theory]
    [Trait("BR", "#349")]
    [InlineData("    filesystem: []", "empty")]
    [InlineData("    filesystem:\n      - { path: /workspace, access: all }", "ro, rw, rwnd")]
    [InlineData("    filesystem:\n      - { path: /workspace, mode: ro }", "unknown key 'mode'")]
    [InlineData("    filesystem:\n      - { access: ro }", "no path")]
    public async Task ShouldRefuseAtLoad_WhenEntryIsMalformed(string writerLines, string expected)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LoadAsync(Yaml(writerLines: writerLines)));

        Assert.Contains("'writer'", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "#348")]
    public async Task ShouldAnnounceMounts_WhenNothingOptsOut()
    {
        var (writer, reader) = await LoadAsync(Yaml());

        Assert.True(writer.AnnounceMounts);
        Assert.True(reader.AnnounceMounts);
    }

    [Theory]
    [Trait("BR", "#348")]
    [InlineData("announce_mounts: false")]
    [InlineData("announceMounts: false")]
    public async Task ShouldOptEveryAgentOut_WhenCrewSaysFalse(string crewLine)
    {
        var (writer, reader) = await LoadAsync(Yaml(crewLine));

        Assert.False(writer.AnnounceMounts);
        Assert.False(reader.AnnounceMounts);
    }

    [Fact]
    [Trait("BR", "#348")]
    public async Task ShouldLetAgentOverrideCrew_WhenBothDeclare()
    {
        var (writer, reader) = await LoadAsync(Yaml("announce_mounts: false", "    announce_mounts: true"));

        Assert.True(writer.AnnounceMounts);
        Assert.False(reader.AnnounceMounts);
    }

    [Fact]
    [Trait("BR", "#349")]
    [Trait("BR", "#348")]
    public async Task ShouldRoundTrip_WhenExported()
    {
        var fs = new FakeFileSystemService();
        var loaded = await Loader(fs).LoadFromStringAsync(Yaml(writerLines: Scope + "\n    announce_mounts: false"), Ct);
        var exporter = new YamlCrewExporter(new YamlDotNetSerializer(), fs, NullLogger<YamlCrewExporter>.Instance);

        var reloaded = await Loader(fs).LoadFromStringAsync(exporter.ExportToString(loaded), Ct);

        var writer = reloaded.Agents.Single(a => a.Role == "Writer");
        Assert.Equal(loaded.Agents.Single(a => a.Role == "Writer").FileSystem, writer.FileSystem);
        Assert.False(writer.AnnounceMounts);
        Assert.True(reloaded.Agents.Single(a => a.Role == "Reader").AnnounceMounts);
    }
}
