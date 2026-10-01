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
        bool prepareRagCollections = true,
        Microsoft.Extensions.Logging.ILogger<CrewFactory>? logger = null,
        InMemoryAgentRepository? agentRepository = null)
    {
        var loaderMock = new MockCrewDefinitionLoader();
        loaderMock.SetValidateResult(new CrewDefinitionValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        var unitOfWork = new NullUnitOfWork();
        return new CrewFactory(
            loaderMock,
            new MockToolRegistry(),
            logger ?? NullLogger<CrewFactory>.Instance,
            new InMemoryCrewRepository(unitOfWork),
            agentRepository ?? new InMemoryAgentRepository(unitOfWork),
            new InMemoryTaskRepository(unitOfWork),
            Options.Create(new CrewFactoryOptions { PrepareRagCollections = prepareRagCollections }),
            bootstrapper);
    }

    private static CrewConfiguration BuildConfiguration(
        RagCrewConfig? rag,
        IReadOnlyList<Orkeon.Domain.Knowledge.KnowledgeAttachment>? knowledge = null)
    {
        var agentId = AgentId.Create();
        return new CrewConfiguration
        {
            Name = "rag-crew",
            Goal = "test",
            Rag = rag,
            Agents =
            [
                new AgentConfiguration
                {
                    Id = agentId, Role = "worker", Goal = "do work", Backstory = "b",
                    KnowledgeAttachments = knowledge ?? [],
                }
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

    [Fact]
    public async Task CreateFromConfig_WithKnowledgeButNoSubsystem_WarnsThatNothingWillBeInjected()
    {
        // GAP-02: an agent's knowledge: attachments without the RAG subsystem used to be
        // dropped in silence at execution. The crew still loads, and says so.
        var logger = new Orkeon.Tests.Shared.Doubles.MockLogger<CrewFactory>();
        var factory = BuildFactory(bootstrapper: null, logger: logger);

        var crew = await factory.CreateFromConfigAsync(
            BuildConfiguration(rag: null, knowledge: [Orkeon.Domain.Knowledge.KnowledgeAttachment.Create("produits")]),
            TestContext.Current.CancellationToken);

        Assert.NotNull(crew);
        var warning = Assert.Single(logger.LogEntries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        Assert.Contains("worker", warning.Message, StringComparison.Ordinal);
        Assert.Contains("knowledge", warning.Message, StringComparison.Ordinal);
        Assert.Contains("produits", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateFromConfig_WithKnowledgeAndTheSubsystem_DoesNotWarn()
    {
        var logger = new Orkeon.Tests.Shared.Doubles.MockLogger<CrewFactory>();
        var factory = BuildFactory(new SpyRagCollectionsBootstrapper(), logger: logger);

        await factory.CreateFromConfigAsync(
            BuildConfiguration(rag: null, knowledge: [Orkeon.Domain.Knowledge.KnowledgeAttachment.Create("produits")]),
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logger.LogEntries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public async Task CreateFromConfig_AttachmentsWithoutProfile_TakeTheCrewDefaultProfile()
    {
        // GAP-02: rag.defaults.profile was parsed and then read by nobody. It now fills
        // every attachment that names no profile of its own; an explicit one wins.
        var agents = new InMemoryAgentRepository(new NullUnitOfWork());
        var factory = BuildFactory(new SpyRagCollectionsBootstrapper(), agentRepository: agents);

        var crew = await factory.CreateFromConfigAsync(
            BuildConfiguration(
                new RagCrewConfig { DefaultProfile = "quality" },
                knowledge:
                [
                    Orkeon.Domain.Knowledge.KnowledgeAttachment.Create("produits"),
                    Orkeon.Domain.Knowledge.KnowledgeAttachment.Create("procedures", profile: "balanced"),
                ]),
            TestContext.Current.CancellationToken);

        var agent = await agents.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.NotNull(agent);
        Assert.Equal(["quality", "balanced"], agent!.KnowledgeAttachments.Select(a => a.Profile));
    }

    [Fact]
    public async Task CreateFromConfig_WithoutCrewDefaultProfile_LeavesTheProfileToTheHost()
    {
        var agents = new InMemoryAgentRepository(new NullUnitOfWork());
        var factory = BuildFactory(new SpyRagCollectionsBootstrapper(), agentRepository: agents);

        var crew = await factory.CreateFromConfigAsync(
            BuildConfiguration(rag: null, knowledge: [Orkeon.Domain.Knowledge.KnowledgeAttachment.Create("produits")]),
            TestContext.Current.CancellationToken);

        var agent = await agents.GetByIdAsync(Assert.Single(crew.Agents), TestContext.Current.CancellationToken);
        Assert.Null(Assert.Single(agent!.KnowledgeAttachments).Profile);
    }
}
