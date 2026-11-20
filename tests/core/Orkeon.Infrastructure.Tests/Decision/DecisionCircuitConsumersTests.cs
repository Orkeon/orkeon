using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Security;
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
}
