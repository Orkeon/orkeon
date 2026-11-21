using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.FileSystem;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.LLMs.Decision;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using CrewInput = Orkeon.Application.Interfaces.Services.CrewInput;

namespace Orkeon.Infrastructure.Tests.LLMs.Profiles;

/// <summary>
/// #439 — an agent or a task on <c>thinking: { effort: auto }</c>: System One picks, at each task's start, a level of
/// <c>Orkeon:Decision:Effort:Levels</c>; with the circuit off the profile's effort stands. A crew on
/// <c>decision: { mode: none }</c> or on a profile whose model does not think fails its load.
/// </summary>
public sealed class LlmProfileAutoEffortTests
{
    private readonly MockLlmProvider _default = Provider("vendor-default");
    private readonly MockLlmProvider _small = Provider("vendor-small");
    private readonly MockLlmProvider _large = Provider("vendor-large");
    private readonly FakeDecisionModel _model = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MockLlmProvider Provider(string name)
    {
        var provider = new MockLlmProvider { Name = name };
        provider.SetChatResult(new LlmResponse { Content = "done", PromptTokens = 10, CompletionTokens = 2, TokensUsed = 12 });
        return provider;
    }

    private static LlmReasoningCapabilities Thinks(ThinkingSupport support) => new("m", true, support, LlmThinkingConfig.ThreeLevels);

    private ServiceProvider Container(DecisionCircuit? circuit = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([
                new($"{LlmProfiles.RouteLadderKey}:0", "small"),
                new($"{LlmProfiles.RouteLadderKey}:1", "large"),
            ])
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => _default, LlmConfig.Create("host-model"));
        services.AddOrkeonLlmProfile("small", _ => _small, LlmConfig.Create("model-of-small") with { Thinking = new LlmThinkingConfig { Effort = "low" } });
        services.AddOrkeonLlmProfile("large", _ => _large, LlmConfig.Create("model-of-large"));
        services.AddSingleton<IDecisionModel>(_model);
        if (circuit is not null)
            services.AddSingleton(circuit);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        services.AddOrkeonDecisionProfiles(configuration);
        return services.BuildServiceProvider();
    }

    private static string Crew(string profile = "small", string mode = "") => $$"""
        name: thoughtful
        goal: Pick a level
        process: sequential
        {{mode}}
        agents:
          coder:
            role: Coder
            goal: Change code
            llm:
              profile: {{profile}}
              thinking:
                effort: auto
        tasks:
          change:
            description: Rename the variable x to count
            expected_output: The change
            agent: coder
        """;

    private static string BoundedCrew(string bounds) => Crew().Replace("effort: auto", $"effort: {{ auto: true, {bounds} }}", StringComparison.Ordinal);

    private void Answer(string level, double probability) =>
        _model.Respond = _ => new DecisionResponse
        {
            Model = "fake",
            Answers = ImmutableDictionary<string, DecisionAnswer>.Empty.Add("effort", new ChoiceAnswer
            {
                Choice = level,
                Confidence = probability,
                Probabilities = ImmutableDictionary<string, double>.Empty.Add(level, probability),
            }),
        };

    private static async Task<CrewOutput> RunAsync(ServiceProvider container, string yaml)
    {
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        var crew = await container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        return await container.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("effort", new Dictionary<string, object>()), Ct);
    }

    private static async Task<InvalidOperationException> LoadFailureAsync(ServiceProvider container, string yaml)
    {
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        return await Assert.ThrowsAsync<InvalidOperationException>(
            () => container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct));
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldSendTheLevelSystemOnePicks_WhenTheAgentIsOnEffortAuto()
    {
        // Arrange
        Answer("high", 0.9);
        await using var container = Container();

        // Act
        await RunAsync(container, Crew());

        // Assert
        Assert.Equal("high", _small.LastChatConfig?.Thinking?.Effort);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldSendTheProfilesLevelWithNoDecisionCall_WhenTheCircuitIsOff()
    {
        // Arrange
        Answer("high", 0.9);
        await using var container = Container(DecisionCircuit.Off);

        // Act
        await RunAsync(container, Crew());

        // Assert
        Assert.Empty(_model.Requests);
        Assert.Equal("low", _small.LastChatConfig?.Thinking?.Effort);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheLoad_WhenACrewWithDecisionModeNoneLeavesTheEffortToSystemOne()
    {
        // Arrange
        await using var container = Container();

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => container.GetRequiredService<ICrewDefinitionLoader>()
            .LoadFromStringAsync(Crew(mode: "decision: { mode: none }"), Ct));

        // Assert
        Assert.Contains("Agent 'coder' declares thinking: { effort: auto }, which System One picks", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheLoadNamingTheProfile_WhenItsModelDoesNotThink()
    {
        // Arrange
        _small.Reasoning = Thinks(ThinkingSupport.None);
        await using var container = Container();

        // Act
        var error = await LoadFailureAsync(container, Crew());

        // Assert
        Assert.Contains("its profile 'small' does not think", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldAcceptTheLoad_WhenOneProfileSystemOneMayPickThinks()
    {
        // Arrange
        _small.Reasoning = Thinks(ThinkingSupport.None);
        _default.Reasoning = Thinks(ThinkingSupport.None);
        _large.Reasoning = Thinks(ThinkingSupport.EffortOnly);
        Answer("medium", 0.9);
        await using var container = Container();

        // Act
        var output = await RunAsync(container, Crew(profile: "auto"));

        // Assert
        Assert.Single(output.TaskOutputs);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheLoad_WhenNoProfileSystemOneMayPickThinks()
    {
        // Arrange
        _small.Reasoning = Thinks(ThinkingSupport.None);
        _large.Reasoning = Thinks(ThinkingSupport.None);
        _default.Reasoning = Thinks(ThinkingSupport.None);
        await using var container = Container();

        // Act
        var error = await LoadFailureAsync(container, Crew(profile: "auto"));

        // Assert
        Assert.Contains("every profile System One may pick (small, large, default)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheLoadNamingTheBoundsAndTheKey_WhenTheyKeepNoneOfTheHostLevels()
    {
        // Arrange
        await using var container = Container();

        // Act
        var error = await LoadFailureAsync(container, BoundedCrew("min: xhigh"));

        // Assert
        Assert.Contains("agent 'coder' declares thinking: { effort: { auto: true, min: xhigh } }", error.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Decision:Effort:Levels", error.Message, StringComparison.Ordinal);
    }

    /// <summary>A crew whose agent writes a level, its task leaving the effort to System One through its llm_override.</summary>
    private static string TaskCrew(string overrideLines, string mode = "") => $$"""
        name: thoughtful
        goal: Pick a level
        process: sequential
        {{mode}}
        agents:
          coder:
            role: Coder
            goal: Change code
            llm:
              profile: small
        tasks:
          change:
            description: Rename the variable x to count
            expected_output: The change
            agent: coder
            llm_override:
        {{overrideLines}}
        """;

    private const string TaskEffortAuto = """
              thinking:
                effort: auto
        """;

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheLoadNamingTheTask_WhenTheTaskLeavesTheEffortToSystemOneOnItsAgentsProfileThatDoesNotThink()
    {
        // Arrange
        _small.Reasoning = Thinks(ThinkingSupport.None);
        await using var container = Container();

        // Act
        var error = await LoadFailureAsync(container, TaskCrew(TaskEffortAuto));

        // Assert
        Assert.Contains("task 'Rename the variable x to count' declares thinking: { effort: auto } in its llm_override", error.Message, StringComparison.Ordinal);
        Assert.Contains("its profile 'small' does not think", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheLoadNamingTheTasksOwnProfile_WhenItsModelDoesNotThink()
    {
        // Arrange
        _large.Reasoning = Thinks(ThinkingSupport.None);
        await using var container = Container();

        // Act
        var error = await LoadFailureAsync(container, TaskCrew("      profile: large\n" + TaskEffortAuto));

        // Assert
        Assert.Contains("in its llm_override", error.Message, StringComparison.Ordinal);
        Assert.Contains("its profile 'large' does not think", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheLoad_WhenTheTaskIsOnProfileAutoAndNoProfileSystemOneMayPickThinks()
    {
        // Arrange
        _small.Reasoning = Thinks(ThinkingSupport.None);
        _large.Reasoning = Thinks(ThinkingSupport.None);
        _default.Reasoning = Thinks(ThinkingSupport.None);
        await using var container = Container();

        // Act
        var error = await LoadFailureAsync(container, TaskCrew("      profile: auto\n" + TaskEffortAuto));

        // Assert
        Assert.Contains("in its llm_override", error.Message, StringComparison.Ordinal);
        Assert.Contains("every profile System One may pick (small, large, default)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheLoadNamingTheTask_WhenItsBoundsKeepNoneOfTheHostLevels()
    {
        // Arrange
        await using var container = Container();

        // Act
        var error = await LoadFailureAsync(container, TaskCrew("""
                  thinking:
                    effort: { auto: true, min: xhigh }
            """));

        // Assert
        Assert.Contains("task 'Rename the variable x to count' declares thinking: { effort: { auto: true, min: xhigh } } in its llm_override", error.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Decision:Effort:Levels", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldRefuseTheLoadNamingEnabledAuto_WhenTheAgentsModelDoesNotThink()
    {
        // Arrange
        _small.Reasoning = Thinks(ThinkingSupport.None);
        await using var container = Container();
        var yaml = Crew().Replace("effort: auto", "enabled: auto", StringComparison.Ordinal);

        // Act
        var error = await LoadFailureAsync(container, yaml);

        // Assert
        Assert.Contains("agent 'coder' declares thinking: { enabled: auto }", error.Message, StringComparison.Ordinal);
        Assert.Contains("its profile 'small' does not think", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData("effort: auto", "declares thinking: { effort: auto }, which System One picks")]
    [InlineData("enabled: auto", "declares thinking: { enabled: auto }, which System One picks")]
    public async Task ShouldRefuseTheLoadNamingTheTask_WhenACrewWithDecisionModeNoneHasATaskThatLeavesThinkingToSystemOne(string written, string expected)
    {
        // Arrange
        await using var container = Container();
        var yaml = TaskCrew(TaskEffortAuto.Replace("effort: auto", written, StringComparison.Ordinal), mode: "decision: { mode: none }");

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => container.GetRequiredService<ICrewDefinitionLoader>()
            .LoadFromStringAsync(yaml, Ct));

        // Assert
        Assert.Contains("Task '", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async Task ShouldSendTheOnlyLevelWithNoDecisionCall_WhenTheBoundsKeepOneHostLevel()
    {
        // Arrange
        Answer("low", 0.9);
        await using var container = Container();

        // Act
        await RunAsync(container, BoundedCrew("min: high"));

        // Assert
        Assert.Equal("high", _small.LastChatConfig?.Thinking?.Effort);
    }
}
