using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Common;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Persistence.Agent;
using Orkeon.Infrastructure.Persistence.Crew;
using Orkeon.Infrastructure.Persistence.Task;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// GAP-04 — a consensual crew's <c>manager_agent</c> is the arbiter of the
/// <c>ManagerDecision</c> fallback: the factory keeps it on the domain crew (it kept it for
/// hierarchical crews only), and the validator refuses one that is not among the agents.
/// </summary>
public class ConsensualManagerConfigurationTests
{
    private static readonly AgentId Chair = AgentId.Create();

    private static CrewConfiguration Config(AgentId? manager) => new()
    {
        Name = "vote",
        Goal = "decide",
        Process = ProcessType.Consensual,
        ManagerAgentId = manager,
        Agents =
        [
            new AgentConfiguration { Id = AgentId.Create(), Role = "analyst", Goal = "answer", Backstory = "b" },
            new AgentConfiguration { Id = Chair, Role = "chair", Goal = "arbitrate", Backstory = "b" },
        ],
        Tasks = [new TaskConfiguration { Id = TaskId.Create(), Description = "d", ExpectedOutput = "o" }],
    };

    [Fact]
    public async Task The_factory_keeps_a_consensual_crews_manager()
    {
        var loader = new MockCrewDefinitionLoader();
        loader.SetValidateResult(new CrewDefinitionValidationResult(true, [], []));
        var unitOfWork = new NullUnitOfWork();
        var factory = new CrewFactory(
            loader, new MockToolRegistry(), NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork), new InMemoryAgentRepository(unitOfWork),
            new InMemoryTaskRepository(unitOfWork));

        var crew = await factory.CreateFromConfigAsync(Config(Chair), TestContext.Current.CancellationToken);

        Assert.NotNull(crew.ManagerAgentId);
        Assert.Contains(crew.ManagerAgentId, crew.Agents);
    }

    [Fact]
    public void The_validator_refuses_a_consensual_manager_that_is_not_an_agent()
    {
        var result = CrewDefinitionValidator.Validate(Config(AgentId.Create()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Manager agent", StringComparison.Ordinal));
    }

    [Fact]
    public void The_validator_accepts_a_consensual_crew_with_or_without_a_manager()
    {
        Assert.True(CrewDefinitionValidator.Validate(Config(Chair)).IsValid);
        Assert.True(CrewDefinitionValidator.Validate(Config(null)).IsValid);
    }
}
