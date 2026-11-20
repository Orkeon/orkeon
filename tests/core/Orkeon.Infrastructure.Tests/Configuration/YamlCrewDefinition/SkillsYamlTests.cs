using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// #434 in YAML: an agent's <c>skills:</c>, a list of virtual paths or <c>{ auto: true, fallback: [...] }</c>,
/// reaches the configuration, survives an export, and a value of another shape is refused naming the agent.
/// </summary>
public sealed class SkillsYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Yaml(string writerLines) => $$"""
        name: skilled
        goal: Review a change
        process: sequential
        agents:
          writer:
            role: Writer
            goal: Write the review
        {{writerLines}}
        tasks:
          write:
            description: Write the review
            expected_output: A review
            agent: writer
        """;

    private static YamlCrewDefinitionLoader Loader(FakeFileSystemService? fs = null)
        => new(new YamlDotNetSerializer(), fs ?? new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    private static async Task<AgentConfiguration> WriterAsync(string writerLines)
        => (await Loader().LoadFromStringAsync(Yaml(writerLines), Ct)).Agents.Single();

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldMapTheFixedList_WhenSkillsIsAList()
    {
        // Arrange
        const string lines = "    skills: [/workspace/skills/review.md, /workspace/skills/tests.md]";

        // Act
        var skills = (await WriterAsync(lines)).Skills;

        // Assert
        Assert.NotNull(skills);
        Assert.False(skills.Auto);
        Assert.Equal(["/workspace/skills/review.md", "/workspace/skills/tests.md"], skills.Paths);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldMapThePickAndItsFallback_WhenSkillsIsAnAutoMap()
    {
        // Arrange
        const string lines = "    skills:\n      auto: true\n      fallback:\n        - /workspace/skills/review.md";

        // Act
        var skills = (await WriterAsync(lines)).Skills;

        // Assert
        Assert.NotNull(skills);
        Assert.True(skills.Auto);
        Assert.Empty(skills.Paths);
        Assert.Equal(["/workspace/skills/review.md"], skills.Fallback);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldReadTheFallbackAsAFixedList_WhenAutoIsFalse()
    {
        // Arrange
        const string lines = "    skills: { auto: false, fallback: [/workspace/skills/review.md] }";

        // Act
        var skills = (await WriterAsync(lines)).Skills;

        // Assert
        Assert.NotNull(skills);
        Assert.False(skills.Auto);
        Assert.Equal(["/workspace/skills/review.md"], skills.Paths);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldDeclareNoSkill_WhenTheKeyIsAbsent()
    {
        // Arrange
        const string lines = "    verbose: false";

        // Act
        var skills = (await WriterAsync(lines)).Skills;

        // Assert
        Assert.Null(skills);
    }

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData("    skills: /workspace/skills/review.md", "expected a list of virtual paths or a map")]
    [InlineData("    skills: { auto: true, library: /x }", "unknown key 'library'")]
    [InlineData("    skills: { auto: maybe }", "'maybe' is not true or false")]
    [InlineData("    skills: { fallback: [/workspace/a.md] }", "needs 'auto: true'")]
    [InlineData("    skills: { auto: true, fallback: /workspace/a.md }", "skills.fallback: expected a list")]
    [InlineData("    skills: ['']", "each item is the virtual path")]
    public async Task ShouldRefuseTheLoadNamingTheAgent_WhenSkillsHasAnotherShape(string lines, string expected)
    {
        // Arrange
        var yaml = Yaml(lines);

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Loader().LoadFromStringAsync(yaml, Ct));

        // Assert
        Assert.Contains("Agent 'writer' skills", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData("    skills: [/workspace/skills/review.md]")]
    [InlineData("    skills: { auto: true, fallback: [/workspace/skills/review.md] }")]
    public async Task ShouldRoundTrip_WhenExported(string lines)
    {
        // Arrange
        var fs = new FakeFileSystemService();
        var loaded = await Loader(fs).LoadFromStringAsync(Yaml(lines), Ct);
        var exporter = new YamlCrewExporter(new YamlDotNetSerializer(), fs, NullLogger<YamlCrewExporter>.Instance);

        // Act
        var reloaded = await Loader(fs).LoadFromStringAsync(exporter.ExportToString(loaded), Ct);

        // Assert
        var before = loaded.Agents.Single().Skills!;
        var after = reloaded.Agents.Single().Skills!;
        Assert.Equal(before.Auto, after.Auto);
        Assert.Equal(before.Paths, after.Paths);
        Assert.Equal(before.Fallback, after.Fallback);
    }
}
