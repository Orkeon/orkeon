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
/// Covers the CrewFactory leg of P2-O-01: a <see cref="CrewConfiguration"/>'s
/// <see cref="CrewConfiguration.GraphConfig"/> / <see cref="CrewConfiguration.CircuitBreaker"/>
/// must survive onto the built domain <c>Crew</c> so the GraphProcessStrategy can read them at
/// execution — previously they were silently dropped during crew assembly.
/// </summary>
public class CrewFactoryGraphConfigTests
{
    private static CrewFactory BuildFactory()
    {
        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        var unitOfWork = new NullUnitOfWork();
        return new CrewFactory(
            loaderMock,
            new MockToolRegistry(),
            NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            new InMemoryAgentRepository(unitOfWork),
            new InMemoryTaskRepository(unitOfWork));
    }

    private static CrewConfiguration BuildGraphConfig(GraphConfig? graph, CircuitBreakerConfig? circuitBreaker)
    {
        var agentId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "graph-crew",
            Goal = "test",
            Process = ProcessType.Graph,
            GraphConfig = graph,
            CircuitBreaker = circuitBreaker,
            Agents =
            [
                new AgentConfiguration { Id = agentId, Role = "worker", Goal = "do work", Backstory = "b" }
            ],
            Tasks =
            [
                new TaskConfiguration
                {
                    Id = TaskId.Create(),
                    Description = "do something",
                    ExpectedOutput = "output",
                    AssignedAgentId = agentId
                }
            ]
        };
    }

    [Fact]
    public async Task CreateFromConfig_ShouldCarryGraphConfig_OntoDomainCrew()
    {
        var factory = BuildFactory();
        var config = BuildGraphConfig(
            new GraphConfig { MaxRetryCycles = 5, CircuitBreakerPreset = "permissive", MaxTransitions = 12 },
            circuitBreaker: null);

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        Assert.NotNull(crew.GraphConfig);
        Assert.Equal(5, crew.GraphConfig!.MaxRetryCycles);
        Assert.Equal("permissive", crew.GraphConfig.CircuitBreakerPreset);
        Assert.Equal(12, crew.GraphConfig.MaxTransitions);
    }

    [Fact]
    public async Task CreateFromConfig_ShouldCarryCircuitBreaker_OntoDomainCrew()
    {
        var factory = BuildFactory();
        var config = BuildGraphConfig(
            graph: null,
            new CircuitBreakerConfig { Preset = "strict", MaxTransitions = 7 });

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        Assert.NotNull(crew.CircuitBreaker);
        Assert.Equal("strict", crew.CircuitBreaker!.Preset);
        Assert.Equal(7, crew.CircuitBreaker.MaxTransitions);
    }

    [Fact]
    public async Task CreateFromConfig_ShouldLeaveConfigNull_WhenNoneProvided()
    {
        var factory = BuildFactory();
        var config = BuildGraphConfig(graph: null, circuitBreaker: null);

        var crew = await factory.CreateFromConfigAsync(config, TestContext.Current.CancellationToken);

        Assert.Null(crew.GraphConfig);
        Assert.Null(crew.CircuitBreaker);
    }
}
