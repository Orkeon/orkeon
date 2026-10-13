using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Constants.Agent;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// CH-02 - <c>maxExecutionTime:</c> (or <c>max_execution_time:</c>) of a YAML agent: whole seconds,
/// reaching the agent as a TimeSpan; zero, negative, above the ceiling, decimal, or set on the
/// hierarchical manager is refused at load.
/// </summary>
public sealed class MaxExecutionTimeYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Yaml(string researcherLines, string process = "sequential", string crewLine = "") => $$"""
        name: timed
        goal: Answer within a limit
        process: {{process}}
        {{crewLine}}
        agents:
          researcher:
            role: Researcher
            goal: Find the facts
        {{researcherLines}}
          writer:
            role: Writer
            goal: Write the answer
        tasks:
          find:
            description: Find the facts
            expected_output: Facts
            agent: researcher
          write:
            description: Write the answer
            expected_output: An answer
            agent: writer
        """;

    public static TheoryData<string> OutOfRangeValues() =>
        new()
        {
            "0",
            "-5",
            (((int)AgentDefaults.MaxExecutionTimeCeiling.TotalSeconds) + 1).ToString(CultureInfo.InvariantCulture),
        };

    private static async Task<DomainAgent> LoadResearcherAsync(string yaml)
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var unitOfWork = new NullUnitOfWork();
        var agents = new InMemoryAgentRepository(unitOfWork);
        var factory = new CrewFactory(
            loader, new MockToolRegistry(), NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork), agents, new InMemoryTaskRepository(unitOfWork));

        var crew = await factory.CreateFromConfigAsync(await loader.LoadFromStringAsync(yaml, Ct), Ct);
        var loaded = new List<DomainAgent>();
        foreach (var id in crew.Agents)
            loaded.Add((await agents.GetByIdAsync(id, Ct))!);
        return loaded.Single(a => a.Role.Value == "Researcher");
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldMapSeconds_WhenYamlDeclaresMaxExecutionTime()
    {
        var researcher = await LoadResearcherAsync(Yaml("    maxExecutionTime: 90"));

        Assert.Equal(TimeSpan.FromSeconds(90), researcher.MaxExecutionTime);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldMapSeconds_WhenYamlDeclaresSnakeCaseMaxExecutionTime()
    {
        var researcher = await LoadResearcherAsync(Yaml("    max_execution_time: 90"));

        Assert.Equal(TimeSpan.FromSeconds(90), researcher.MaxExecutionTime);
    }

    [Theory]
    [MemberData(nameof(OutOfRangeValues))]
    [Trait("BR", "CH-02")]
    public async Task ShouldRefuseLoad_WhenYamlLimitIsZeroNegativeOrAboveCeiling(string seconds)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LoadResearcherAsync(Yaml($"    maxExecutionTime: {seconds}")));

        Assert.Contains("'researcher'", error.Message, StringComparison.Ordinal);
        Assert.Contains("maxExecutionTime", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldRefuseLoad_WhenYamlLimitIsDecimal()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => LoadResearcherAsync(Yaml("    maxExecutionTime: 1.5")));
    }

    public static TheoryData<double, int> ExportedLimits() =>
        new()
        {
            { 0.5, 1 },
            { 90.5, 91 },
            { 90, 90 },
            { AgentDefaults.MaxExecutionTimeCeiling.TotalSeconds, 4294967 },
        };

    [Theory]
    [MemberData(nameof(ExportedLimits))]
    [Trait("BR", "CH-02")]
    public async Task ShouldExportWholeSecondsRoundedUp_WhenLimitHasAFraction(double seconds, int expected)
    {
        var fs = new FakeFileSystemService();
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), fs, NullLogger<YamlCrewDefinitionLoader>.Instance);
        var exporter = new YamlCrewExporter(new YamlDotNetSerializer(), fs, NullLogger<YamlCrewExporter>.Instance);
        var config = new CrewConfiguration
        {
            Name = "timed",
            Goal = "g",
            Agents = [new AgentConfiguration { Role = "R", Goal = "G", MaxExecutionTime = TimeSpan.FromSeconds(seconds) }],
            Tasks = [new TaskConfiguration { Description = "D", ExpectedOutput = "O" }],
        };

        var yaml = exporter.ExportToString(config);
        var reloaded = await loader.LoadFromStringAsync(yaml, Ct);

        Assert.Matches($@"(?m)axExecutionTime: {expected}\s*$|ax_execution_time: {expected}\s*$", yaml);
        Assert.Equal(TimeSpan.FromSeconds(expected), reloaded.Agents[0].MaxExecutionTime);
    }

    [Fact]
    [Trait("BR", "CH-02")]
    public async Task ShouldRefuseLoad_WhenHierarchicalManagerDeclaresLimit()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LoadResearcherAsync(Yaml("    maxExecutionTime: 90", "hierarchical", "managerAgent: researcher")));

        Assert.Contains("maxExecutionTime", error.Message, StringComparison.Ordinal);
    }
}
