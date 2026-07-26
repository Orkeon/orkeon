using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
using Orkeon.Rag.Abstractions.Interfaces;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// RAG-03/C3: a configuration carrying a <c>rag:</c> block has its collections ingested at
/// crew creation through the optional <see cref="IRagCollectionsBootstrapper"/> — unless
/// <see cref="CrewFactoryOptions.PrepareRagCollections"/> is off (validation hosts), and
/// never as a hard failure when the RAG subsystem is absent.
/// </summary>
public class CrewFactoryRagBootstrapTests
{
    private sealed class SpyRagCollectionsBootstrapper : IRagCollectionsBootstrapper
    {
        public RagCrewConfig? LastConfig { get; private set; }
        public int CallCount { get; private set; }

        public System.Threading.Tasks.Task PrepareAsync(
            RagCrewConfig config, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastConfig = config;
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    private static CrewFactory BuildFactory(
        IRagCollectionsBootstrapper? bootstrapper,
        bool prepareRagCollections = true)
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
            new InMemoryTaskRepository(unitOfWork),
            Options.Create(new CrewFactoryOptions { PrepareRagCollections = prepareRagCollections }),
            bootstrapper);
    }

    private static CrewConfiguration BuildConfiguration(RagCrewConfig? rag)
    {
        var agentId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "rag-crew",
            Goal = "test",
            Rag = rag,
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

    private static RagCrewConfig DeclaredCollections() => new()
    {
        Collections = new Dictionary<string, RagCollectionConfig>
        {
            ["docs"] = new() { Sources = ["/kb/**/*.md"] },
        },
    };

    [Fact]
    public async Task CreateFromConfig_WithRagBlock_IngestsDeclaredCollections()
    {
        var spy = new SpyRagCollectionsBootstrapper();
        var factory = BuildFactory(spy);

        await factory.CreateFromConfigAsync(BuildConfiguration(DeclaredCollections()),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, spy.CallCount);
        Assert.NotNull(spy.LastConfig);
        Assert.True(spy.LastConfig!.Collections.ContainsKey("docs"));
    }

    [Fact]
    public async Task CreateFromConfig_WithoutRagBlock_DoesNotCallBootstrapper()
    {
        var spy = new SpyRagCollectionsBootstrapper();
        var factory = BuildFactory(spy);

        await factory.CreateFromConfigAsync(BuildConfiguration(rag: null),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, spy.CallCount);
    }

    [Fact]
    public async Task CreateFromConfig_WhenPreparationDisabled_DoesNotCallBootstrapper()
    {
        var spy = new SpyRagCollectionsBootstrapper();
        var factory = BuildFactory(spy, prepareRagCollections: false);

        await factory.CreateFromConfigAsync(BuildConfiguration(DeclaredCollections()),
            TestContext.Current.CancellationToken);

        Assert.Equal(0, spy.CallCount);
    }

    [Fact]
    public async Task CreateFromConfig_WithRagBlockButNoSubsystem_LoadsTheCrewAnyway()
    {
        var factory = BuildFactory(bootstrapper: null);

        var crew = await factory.CreateFromConfigAsync(BuildConfiguration(DeclaredCollections()),
            TestContext.Current.CancellationToken);

        Assert.NotNull(crew); // warning logged, never a failure
    }
}
