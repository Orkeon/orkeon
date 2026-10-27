using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// #218 - the <c>budget:</c> block of an autonomous YAML crew reaches the crew, and from there the
/// run's budget; a crew of another process that writes one is refused at load.
/// </summary>
public sealed class CrewBudgetYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Yaml(string process, string crewLines) => $$"""
        name: bounded
        goal: Answer within bounds
        process: {{process}}
        {{crewLines}}
        agents:
          researcher:
            role: Researcher
            goal: Find the facts
          writer:
            role: Writer
            goal: Write the answer
        tasks:
          find:
            description: Find the facts
            expected_output: Facts
            agent: researcher
        """;

    private static async Task<DomainCrew> LoadAsync(string yaml)
    {
        var loader = new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);
        var unitOfWork = new NullUnitOfWork();
        var factory = new CrewFactory(
            loader, new MockToolRegistry(), NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork), new InMemoryAgentRepository(unitOfWork), new InMemoryTaskRepository(unitOfWork));

        return await factory.CreateFromConfigAsync(await loader.LoadFromStringAsync(yaml, Ct), Ct);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public async Task ShouldCarryTheBudgetToTheCrew_WhenAnAutonomousCrewDeclaresOne()
    {
        var crew = await LoadAsync(Yaml("autonomous", """
            budget:
              tokens: 400000
              wallTimeSeconds: 1800
              toolCalls: 80
              delegationDepth: 2
              spawnedAgents: 3
            """));

        Assert.Equal(400_000, crew.Budget.Tokens);
        Assert.Equal(TimeSpan.FromMinutes(30), crew.Budget.WallTime);
        Assert.Equal(80, crew.Budget.ToolCalls);
        Assert.Equal(2, crew.Budget.DelegationDepth);
        Assert.Equal(3, crew.Budget.SpawnedAgents);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public async Task ShouldSetNoTokenCap_WhenAnAutonomousCrewDeclaresNoBudget()
    {
        var crew = await LoadAsync(Yaml("autonomous", ""));

        Assert.Equal(int.MaxValue, crew.Budget.ToExecutionBudget().MaxTokensConsumed);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public async Task ShouldRefuseTheBudget_WhenTheProcessIsNotAutonomous()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(() => LoadAsync(Yaml("sequential", """
            budget:
              tokens: 400000
            """)));

        Assert.Contains("budget", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "CH-218")]
    public async Task ShouldRefuseALimitOfZeroOrLess_NamingTheCrew()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => LoadAsync(Yaml("autonomous", """
            budget:
              tokens: 0
            """)));

        Assert.Contains("'bounded'", error.Message, StringComparison.Ordinal);
        Assert.Contains("budget.tokens", error.Message, StringComparison.Ordinal);
    }
}
