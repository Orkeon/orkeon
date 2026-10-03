using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// GAP-38 — <c>maxRpm:</c> at both levels of a YAML crew, as CrewAI reads <c>max_rpm</c>: the
/// agent's reaches the agent, the crew's — new — reaches the crew, and a key left out is no limit
/// of its own (the 10 and 100 that replaced it are gone). Zero or less is refused at load, naming
/// the agent or the crew, and so is <c>maxIter</c> — both used to be replaced without a word. An
/// agent without <c>maxIter:</c> has 20 turns, as in C# and <c>.ork.ts</c>.
/// </summary>
public sealed class MaxRpmYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Yaml(string crewLine = "", string researcherLines = "") => $$"""
        name: paced
        goal: Answer at a pace
        process: sequential
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

    private sealed record Loaded(DomainCrew Crew, DomainAgent Researcher, DomainAgent Writer);

    private static async Task<Loaded> LoadAsync(string yaml)
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
        return new Loaded(crew, loaded.Single(a => a.Role.Value == "Researcher"), loaded.Single(a => a.Role.Value == "Writer"));
    }

    [Fact]
    public async Task An_agents_maxRpm_reaches_the_agent_and_one_left_out_is_no_limit()
    {
        var loaded = await LoadAsync(Yaml(researcherLines: "    maxRpm: 3"));

        Assert.Equal(3, loaded.Researcher.MaxRpm);
        Assert.Null(loaded.Writer.MaxRpm);
        Assert.Null(loaded.Crew.MaxRpm);
    }

    [Theory]
    [InlineData("maxRpm: 30")]
    [InlineData("max_rpm: 30")]
    public async Task A_crews_maxRpm_reaches_the_crew(string crewLine)
    {
        var loaded = await LoadAsync(Yaml(crewLine));

        Assert.Equal(30, loaded.Crew.MaxRpm);
    }

    [Fact]
    public async Task An_agent_without_maxIter_has_twenty_turns()
    {
        var loaded = await LoadAsync(Yaml(researcherLines: "    maxIter: 7"));

        Assert.Equal(7, loaded.Researcher.MaxIterations);
        Assert.Equal(20, loaded.Writer.MaxIterations);
    }

    [Theory]
    [InlineData("", "    maxRpm: 0", "researcher", "maxRpm")]
    [InlineData("", "    maxRpm: -1", "researcher", "maxRpm")]
    [InlineData("maxRpm: 0", "", "paced", "maxRpm")]
    [InlineData("max_rpm: -1", "", "paced", "maxRpm")]
    [InlineData("", "    maxIter: 0", "researcher", "maxIter")]
    [InlineData("", "    maxIter: -3", "researcher", "maxIter")]
    public async Task Zero_or_less_is_refused_at_load_naming_the_agent_or_the_crew(
        string crewLine, string researcherLines, string named, string key)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LoadAsync(Yaml(crewLine, researcherLines)));

        Assert.Contains($"'{named}'", error.Message, StringComparison.Ordinal);
        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }
}
