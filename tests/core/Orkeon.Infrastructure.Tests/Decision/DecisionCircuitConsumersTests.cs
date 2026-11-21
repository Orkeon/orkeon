using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Decision.SystemOne;
using Orkeon.Infrastructure.LLMs.Decision;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tools;
using Orkeon.Rag.Abstractions.Models;
using Orkeon.Rag.DependencyInjection;
using Orkeon.Rag.Factories;
using Orkeon.Rag.Reranking;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using DomainCrew = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Infrastructure.Tests.Decision;

/// <summary>
/// #433: a run whose decision circuit is off makes no <see cref="IDecisionModel"/> call, whatever the
/// cause. The circuit names its cause; the <c>system_one_decide</c> tool is withheld; the LLM judge
/// refuses to start when fail-closed and is left out, with one warning, when fail-open; and no consumer
/// - tool, judge, decision profile, <c>decision</c> reranker - reaches the model.
/// </summary>
public sealed class DecisionCircuitConsumersTests
{
    private static ServiceCollection Services(Dictionary<string, string?> settings, FakeDecisionModel? model, RecordingLoggerFactory? logs = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        if (logs is not null)
            services.AddSingleton<ILoggerFactory>(logs);
        services.AddSingleton<IConfiguration>(configuration);
        if (model is not null)
        {
            services.AddSingleton<IDecisionModel>(model);
            services.AddOrkeonDecisionProvider(configuration, DecisionProviderDescriptor.TypeSafe);
        }

        services.AddSingleton<IPromptSanitizer>(new MockPromptSanitizer());
        services.AddOrkeonGuardian();
        return services;
    }

    private static FakeDecisionModel Model() => new() { Failure = new InvalidOperationException("the model must not be asked") };

    private static Dictionary<string, string?> Judge(string failMode) => new()
    {
        ["Orkeon:Guardian:Llm:Enabled"] = "true",
        ["Orkeon:Guardian:Llm:FailMode"] = failMode,
    };

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldNameTheSettingsAsTheCause_WhenDecisionIsDisabledInTheSettings()
    {
        // Arrange
        using var container = Services(new() { ["Orkeon:Decision:Enabled"] = "false" }, Model()).BuildServiceProvider();

        // Act
        var circuit = DecisionCircuit.Of(container);

        // Assert
        Assert.Equal(DecisionCircuitCause.Settings, circuit.Cause);
        Assert.Equal("the decision circuit is off: Orkeon:Decision:Enabled=false", circuit.OffReason);
    }

    [Fact]
    [Trait("BR", "CH-431")]
    public void ShouldNameTheForgesTrialPassInTheReason_WhenTheTrialTurnsTheCircuitOff()
    {
        // Arrange
        var circuit = DecisionCircuit.OffBy(DecisionCircuitCause.Trial);

        // Act
        var reason = circuit.OffReason;

        // Assert
        Assert.Equal("the decision circuit is off: the forge's trial pass without System One", reason);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldNameNoProviderAsTheCause_WhenNoDecisionModelIsRegistered()
    {
        // Arrange
        using var container = Services([], model: null).BuildServiceProvider();

        // Act
        var circuit = DecisionCircuit.Of(container);

        // Assert
        Assert.Equal(DecisionCircuitCause.NoProvider, circuit.Cause);
        Assert.Equal("the decision circuit is off: no decision provider resolved", circuit.OffReason);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldNameTheSwitchAsTheCause_WhenTheSwitchAndTheSettingsBothTurnItOff()
    {
        // Arrange
        var services = Services(new() { ["Orkeon:Decision:Enabled"] = "false" }, Model());
        services.AddSingleton(DecisionCircuit.Off);
        using var container = services.BuildServiceProvider();

        // Act
        var circuit = DecisionCircuit.Of(container);

        // Assert
        Assert.Equal(DecisionCircuitCause.Switch, circuit.Cause);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("BR", "CH-433")]
    public void ShouldOfferTheSystemOneDecideToolOnlyWhenTheCircuitIsOn(bool enabled)
    {
        // Arrange
        using var container = Services(new() { ["Orkeon:Decision:Enabled"] = enabled ? "true" : "false" }, Model())
            .BuildServiceProvider();

        // Act
        var names = container.GetServices<IBaseTool>().OfType<IBaseTool>().Select(t => t.Name).ToList();

        // Assert
        Assert.Equal(enabled, names.Contains(SystemOneDecideTool.ToolName));
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public void ShouldRefuseToStartNamingTheCause_WhenAFailClosedJudgeMeetsAnOffCircuit()
    {
        // Arrange
        var services = Services(Judge("Closed"), Model());
        services.AddSingleton(DecisionCircuit.Off);
        using var container = services.BuildServiceProvider();

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => container.GetRequiredService<IGuardianPipeline>());

        // Assert
        Assert.Contains("Orkeon:Guardian:Llm", ex.Message, StringComparison.Ordinal);
        Assert.Contains("the decision circuit is off: --no-decision", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldLeaveTheJudgeOutWithOneWarning_WhenAFailOpenJudgeMeetsAnOffCircuit()
    {
        // Arrange
        using var logs = new RecordingLoggerFactory();
        var model = Model();
        var services = Services(Judge("Open"), model, logs);
        services.AddSingleton(DecisionCircuit.Off);
        using var container = services.BuildServiceProvider();

        // Act
        await container.GetRequiredService<IGuardianPipeline>().ExecuteAsync(
            new GuardContext { Phase = GuardPhase.Output, AgentRole = "writer", Content = "the report" },
            TestContext.Current.CancellationToken);

        // Assert
        var warning = Assert.Single(logs.Logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("left out", warning.Message, StringComparison.Ordinal);
        Assert.Empty(model.Requests);
    }

    public static TheoryData<string> OffCauses => [nameof(DecisionCircuitCause.Switch), nameof(DecisionCircuitCause.Settings)];

    [Theory]
    [MemberData(nameof(OffCauses))]
    [Trait("BR", "CH-433")]
    public async Task ShouldMakeNoDecisionModelCall_WhenEveryConsumerRunsWithTheCircuitOff(string cause)
    {
        // Arrange
        var model = Model();
        var settings = Judge("Open");
        settings["Orkeon:Decision:Profiles:p:Target"] = "cheap";
        settings["Orkeon:Decision:Profiles:p:Stages:0:Kind"] = "route";
        settings["Orkeon:Decision:Profiles:p:Stages:0:Routes:cheap"] = "Small talk";
        settings["Orkeon:Decision:Profiles:p:Stages:0:Routes:strong"] = "Mathematics";
        settings["Orkeon:Decision:Profiles:p:Stages:1:Kind"] = "guard";
        settings["Orkeon:Decision:Profiles:p:Stages:1:Risks:secret"] = "The text leaks a secret";
        settings["Orkeon:Decision:Profiles:p:Stages:2:Kind"] = "verify";
        settings["Orkeon:Decision:Profiles:p:Stages:2:EscalateTo"] = "strong";
        if (cause == nameof(DecisionCircuitCause.Settings))
            settings["Orkeon:Decision:Enabled"] = "false";
        var services = Services(settings, model);
        services
            .AddOrkeonLlmProfile("cheap", _ => new StubLlmProvider { Name = "cheap" }.RespondToChatWith(new LlmResponse { Content = "ok" }))
            .AddOrkeonLlmProfile("strong", _ => new StubLlmProvider { Name = "strong" }.RespondToChatWith(new LlmResponse { Content = "ok" }))
            .AddOrkeonDecisionProfiles(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .AddOrkeonRagReranking();
        if (cause == nameof(DecisionCircuitCause.Switch))
            services.AddSingleton(DecisionCircuit.Off);
        using var container = services.BuildServiceProvider();

        // Act
        await RunEveryConsumerAsync(container);

        // Assert
        Assert.Empty(model.Requests);
    }

    private static async Task RunEveryConsumerAsync(ServiceProvider container)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.DoesNotContain(container.GetServices<IBaseTool>().OfType<IBaseTool>(), t => t.Name == SystemOneDecideTool.ToolName);
        await container.GetRequiredService<IGuardianPipeline>().ExecuteAsync(
            new GuardContext { Phase = GuardPhase.Output, AgentRole = "writer", Content = "the report" }, ct);
        await container.GetRequiredService<ILlmProfileRegistry>().Resolve("p").Provider
            .ChatAsync([LlmMessage.User("Integrate x^2")], cancellationToken: ct);
        await container.GetRequiredService<RerankerFactory>().Create(DecisionModelReranker.RerankerName).RerankAsync(
            "what is X?",
            [new ScoredChunk { Chunk = new Chunk { Id = "c", DocumentId = "d", SourceId = "s", Content = "x", Index = 0 }, Score = 1, ScoreOrigin = "vector" }],
            topN: 1,
            ct);
    }

    // #431: a run whose scope is off (DecisionCircuitScope, entered from the scope's DecisionCircuitOverride) asks
    // no model through what is built once per host with the circuit on - the judge, a decision profile, the
    // decision reranker -, and the same consumers do ask it with the circuit on.

    public static TheoryData<string> HostConsumers => ["judge", "profile", "reranker"];

    private static FakeDecisionModel Answering() => new() { Respond = Answer };

    private static DecisionResponse Answer(DecisionRequest request) => new()
    {
        Model = "fake",
        Answers = request.Questions.ToImmutableDictionary(
            q => q.Key,
            q => q.Value is ChoiceQuestion
                ? (DecisionAnswer)new ChoiceAnswer
                {
                    Choice = "cheap",
                    Confidence = 0.9,
                    Probabilities = ImmutableDictionary<string, double>.Empty.Add("cheap", 0.9),
                }
                : new NoulAnswer { Noul = 0.1 }),
    };

    private static ServiceProvider HostWithEveryConsumer(FakeDecisionModel model, string failMode = "Open")
    {
        var settings = Judge(failMode);
        settings["Orkeon:Decision:Profiles:p:Target"] = "cheap";
        settings["Orkeon:Decision:Profiles:p:Stages:0:Kind"] = "route";
        settings["Orkeon:Decision:Profiles:p:Stages:0:Routes:cheap"] = "Small talk";
        settings["Orkeon:Decision:Profiles:p:Stages:0:Routes:strong"] = "Mathematics";
        var services = Services(settings, model);
        services
            .AddOrkeonLlmProfile("cheap", _ => new StubLlmProvider { Name = "cheap" }.RespondToChatWith(new LlmResponse { Content = "ok" }))
            .AddOrkeonLlmProfile("strong", _ => new StubLlmProvider { Name = "strong" }.RespondToChatWith(new LlmResponse { Content = "ok" }))
            .AddOrkeonDecisionProfiles(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .AddOrkeonRagReranking();
        return services.BuildServiceProvider();
    }

    private static async Task RunConsumerAsync(ServiceProvider container, string consumer)
    {
        var ct = TestContext.Current.CancellationToken;
        switch (consumer)
        {
            case "judge":
                await container.GetRequiredService<IGuardianPipeline>().ExecuteAsync(
                    new GuardContext { Phase = GuardPhase.Output, AgentRole = "writer", Content = "the report" }, ct);
                break;
            case "profile":
                await container.GetRequiredService<ILlmProfileRegistry>().Resolve("p").Provider
                    .ChatAsync([LlmMessage.User("Integrate x^2")], cancellationToken: ct);
                break;
            default:
                await container.GetRequiredService<RerankerFactory>().Create(DecisionModelReranker.RerankerName).RerankAsync(
                    "what is X?",
                    [new ScoredChunk { Chunk = new Chunk { Id = "c", DocumentId = "d", SourceId = "s", Content = "x", Index = 0 }, Score = 1, ScoreOrigin = "vector" }],
                    topN: 1,
                    ct);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(HostConsumers))]
    [Trait("BR", "CH-433")]
    public async Task ShouldMakeNoDecisionModelCall_WhenAConsumerBuiltOnceRunsInARunWhoseScopeIsOff(string consumer)
    {
        // Arrange
        var model = Answering();
        using var container = HostWithEveryConsumer(model);
        using var run = DecisionCircuitScope.Begin(DecisionCircuit.OffBy(DecisionCircuitCause.Switch));

        // Act
        await RunConsumerAsync(container, consumer);

        // Assert
        Assert.Empty(model.Requests);
    }

    [Theory]
    [MemberData(nameof(HostConsumers))]
    [Trait("BR", "CH-433")]
    public async Task ShouldAskTheDecisionModel_WhenAConsumerBuiltOnceRunsWithTheCircuitOn(string consumer)
    {
        // Arrange
        var model = Answering();
        using var container = HostWithEveryConsumer(model);

        // Act
        await RunConsumerAsync(container, consumer);

        // Assert
        Assert.NotEmpty(model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldMakeNoDecisionModelCall_WhenTheProfileWasFirstUsedWithTheCircuitOnThenRunsInAnOffRun()
    {
        // Arrange
        var model = Answering();
        using var container = HostWithEveryConsumer(model);
        await RunConsumerAsync(container, "profile");
        var asked = model.Requests.Count;
        using var run = DecisionCircuitScope.Begin(DecisionCircuit.OffBy(DecisionCircuitCause.Switch));

        // Act
        await RunConsumerAsync(container, "profile");

        // Assert
        Assert.Equal(asked, model.Requests.Count);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldNameTheCauseOfEachOffRun_WhenAFailClosedProfileBlocksTwoRunsOffForDifferentCauses()
    {
        // Arrange
        var settings = Judge("Open");
        settings["Orkeon:Decision:Profiles:p:Target"] = "cheap";
        settings["Orkeon:Decision:Profiles:p:FailClosed"] = "true";
        settings["Orkeon:Decision:Profiles:p:Stages:0:Kind"] = "guard";
        settings["Orkeon:Decision:Profiles:p:Stages:0:Risks:secret"] = "The text leaks a secret";
        var services = Services(settings, Answering());
        services
            .AddOrkeonLlmProfile("cheap", _ => new StubLlmProvider { Name = "cheap" }.RespondToChatWith(new LlmResponse { Content = "ok" }))
            .AddOrkeonDecisionProfiles(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<ILlmProfileRegistry>().Resolve("p").Provider;
        var ct = TestContext.Current.CancellationToken;
        using (DecisionCircuitScope.Begin(DecisionCircuit.OffBy(DecisionCircuitCause.Switch)))
            await provider.ChatAsync([LlmMessage.User("hello")], cancellationToken: ct);
        using var second = DecisionCircuitScope.Begin(DecisionCircuit.OffBy(DecisionCircuitCause.Settings));

        // Act
        var response = await provider.ChatAsync([LlmMessage.User("hello")], cancellationToken: ct);

        // Assert
        Assert.Contains(DecisionCircuit.OffBy(DecisionCircuitCause.Settings).OffReason!, response.Error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldBlockWithoutAskingTheModel_WhenAFailClosedJudgeRunsInARunWhoseScopeIsOff()
    {
        // Arrange
        var model = Answering();
        using var container = HostWithEveryConsumer(model, failMode: "Closed");
        var pipeline = container.GetRequiredService<IGuardianPipeline>();
        using var run = DecisionCircuitScope.Begin(DecisionCircuit.OffBy(DecisionCircuitCause.Switch));

        // Act
        var result = await pipeline.ExecuteAsync(
            new GuardContext { Phase = GuardPhase.Output, AgentRole = "writer", Content = "the report" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(model.Requests);
        Assert.False(result.IsAllowed);
    }

    // A crew run in a scope whose DecisionCircuitOverride is off: the orchestrator enters it for the run.

    private const string CrewYaml = """
        name: judged-crew
        goal: Publish the notes
        process: sequential
        agents:
          writer:
            role: Writer
            goal: Write the notes
        tasks:
          draft:
            description: Draft the notes
            expected_output: The draft
            agent: writer
        """;

    private static ServiceProvider CrewHost(FakeDecisionModel model)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(Judge("Open")).Build());
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        var vendor = new MockLlmProvider { Name = "vendor" };
        vendor.SetChatFunc((_, _) => new LlmResponse { Content = "The notes." });
        services.AddOrkeonLlmProvider(_ => vendor, LlmConfig.Create("host-model"));
        services.AddSingleton<IDecisionModel>(model);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<CrewOutput> RunCrewAsync(ServiceProvider host, DecisionCircuit? scopeCircuit)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = host.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<DecisionCircuitOverride>().Circuit = scopeCircuit;
        var config = await host.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(CrewYaml, ct);
        DomainCrew crew = await scope.ServiceProvider.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, ct);
        return await scope.ServiceProvider.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("notes", new Dictionary<string, object>()), ct);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldMakeNoDecisionModelCall_WhenACrewRunsInAScopeWhoseOverrideIsOff()
    {
        // Arrange
        var model = Answering();
        await using var host = CrewHost(model);

        // Act
        var output = await RunCrewAsync(host, DecisionCircuit.OffBy(DecisionCircuitCause.Switch));

        // Assert
        Assert.True(output.Succeeded, output.Error);
        Assert.Empty(model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldAskTheDecisionModel_WhenTheSameCrewRunsInAScopeWithoutAnOverride()
    {
        // Arrange
        var model = Answering();
        await using var host = CrewHost(model);

        // Act
        var output = await RunCrewAsync(host, scopeCircuit: null);

        // Assert
        Assert.True(output.Succeeded, output.Error);
        Assert.NotEmpty(model.Requests);
    }
}
