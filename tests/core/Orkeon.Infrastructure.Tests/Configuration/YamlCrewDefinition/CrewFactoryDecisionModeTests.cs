using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.Crew;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration.YamlCrewDefinition;

/// <summary>
/// #429 — a <see cref="CrewConfiguration"/> handed straight to <see cref="ICrewFactory"/>, with no loader before it:
/// its <c>decision.mode</c> refusal is spelled as every loader spells it.
/// </summary>
public sealed class CrewFactoryDecisionModeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider Container()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => new MockLlmProvider { Name = "vendor-default" }, LlmConfig.Create("host-model"));
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider();
    }

    private static CrewConfiguration NoneCrewWhoseAgentDecides()
    {
        var agentId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "direct",
            Goal = "Write the notes",
            Process = ProcessType.Sequential,
            DecisionMode = CrewDecisionMode.None,
            Agents = [new AgentConfiguration { Id = agentId, Key = "writer", Role = "Writer", Goal = "Write", Tools = ["system_one_decide"] }],
            Tasks = [new TaskConfiguration { Key = "notes", Description = "Write the notes", ExpectedOutput = "Notes", AssignedAgentId = agentId }],
        };
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldSpellTheRefusalAsTheLoadersDo_WhenAConfigurationReachesTheFactoryWithNoLoader()
    {
        // Arrange
        await using var container = Container();

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(NoneCrewWhoseAgentDecides(), Ct));

        // Assert
        Assert.Equal(
            "Crew 'direct': " + Orkeon.Domain.Task.DecisionTaskRules.ModeRefusal(
                ["Agent 'writer' lists system_one_decide, which a crew with decision.mode: none refuses."]),
            error.Message);
    }
}
