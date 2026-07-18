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
/// Covers the CrewFactory leg of P2-O-02: a <see cref="CrewConfiguration.MemoryProvider"/> must
/// survive onto the built domain <c>Crew</c> so the orchestrator can record it at kickoff — it was
/// silently dropped during crew assembly before.
/// </summary>
public class CrewFactoryMemoryProviderTests
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

    private static CrewConfiguration BuildConfig(string? memoryProvider)
    {
        var agentId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "mem-crew",
            Goal = "test",
            Process = ProcessType.Sequential,
            Memory = true,
            MemoryProvider = memoryProvider,
            Agents = [new AgentConfiguration { Id = agentId, Role = "worker", Goal = "do work", Backstory = "b" }],
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
    public async Task CreateFromConfig_ShouldCarryMemoryProvider_OntoDomainCrew()
    {
        var factory = BuildFactory();

        var crew = await factory.CreateFromConfigAsync(BuildConfig("Redis"), TestContext.Current.CancellationToken);

        Assert.Equal("Redis", crew.MemoryProvider);
    }

    [Fact]
    public async Task CreateFromConfig_ShouldLeaveMemoryProviderNull_WhenNoneDeclared()
    {
        var factory = BuildFactory();

        var crew = await factory.CreateFromConfigAsync(BuildConfig(memoryProvider: null), TestContext.Current.CancellationToken);

        Assert.Null(crew.MemoryProvider);
    }
}
