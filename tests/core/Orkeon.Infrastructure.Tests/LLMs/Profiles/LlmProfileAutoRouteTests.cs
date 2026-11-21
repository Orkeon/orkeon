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
/// #434 — an agent on <c>llm.profile: auto</c>: System One picks, at each task's start, a profile of
/// <c>Orkeon:Decision:Route:Ladder</c>; with the circuit off the default profile stands, without a call or an error.
/// A crew naming <c>auto</c> on a host without a usable ladder fails its load.
/// </summary>
public sealed class LlmProfileAutoRouteTests
{
    private readonly MockLlmProvider _default = Provider("vendor-default", "default answer");
    private readonly MockLlmProvider _small = Provider("vendor-small", "answer from small");
    private readonly MockLlmProvider _large = Provider("vendor-large", "answer from large");
    private readonly FakeDecisionModel _model = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MockLlmProvider Provider(string name, string answer)
    {
        var provider = new MockLlmProvider { Name = name };
        provider.SetChatResult(new LlmResponse { Content = answer, PromptTokens = 10, CompletionTokens = 2, TokensUsed = 12 });
        return provider;
    }

    private ServiceProvider Container(DecisionCircuit? circuit = null, params string[] ladder)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ladder.Select((rung, i) => new KeyValuePair<string, string?>($"{LlmProfiles.RouteLadderKey}:{i}", rung)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddOrkeonLlmProvider(_ => _default, LlmConfig.Create("host-model"));
        services.AddOrkeonLlmProfile("small", _ => _small, LlmConfig.Create("model-of-small"));
        services.AddOrkeonLlmProfile("large", _ => _large, LlmConfig.Create("model-of-large"));
        services.AddSingleton<IDecisionModel>(_model);
        if (circuit is not null)
            services.AddSingleton(circuit);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        services.AddOrkeonDecisionProfiles(configuration);
        return services.BuildServiceProvider();
    }

    private static string Crew(string mode = "") => $$"""
        name: routed
        goal: Route a task
        process: sequential
        {{mode}}
        agents:
          coder:
            role: Coder
            goal: Change code
            llm:
              profile: auto
        tasks:
          change:
            description: Rename the variable x to count
            expected_output: The change
            agent: coder
        """;

    private void Answer(string choice, double probability) =>
        _model.Respond = _ => new DecisionResponse
        {
            Model = "fake",
            Answers = ImmutableDictionary<string, DecisionAnswer>.Empty.Add("profile", new ChoiceAnswer
            {
                Choice = choice,
                Confidence = probability,
                Probabilities = ImmutableDictionary<string, double>.Empty.Add(choice, probability),
            }),
        };

    private static async Task<CrewOutput> RunAsync(ServiceProvider container, string yaml)
    {
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        var crew = await container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
        return await container.GetRequiredService<ICrewOrchestrationService>().KickoffAsync(
            crew.Id, new CrewInput("route", new Dictionary<string, object>()), Ct);
    }

    private static async Task<InvalidOperationException> LoadFailureAsync(ServiceProvider container, string yaml)
    {
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        return await Assert.ThrowsAsync<InvalidOperationException>(
            () => container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct));
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldRunTheTaskOnTheRungSystemOnePicks_WhenTheAgentIsOnAuto()
    {
        // Arrange
        Answer("large", 0.9);
        await using var container = Container(null, "small", "large");

        // Act
        var output = await RunAsync(container, Crew());

        // Assert
        Assert.Single(output.TaskOutputs);
        Assert.Equal(1, _large.ChatCallCount);
        Assert.Equal(0, _small.ChatCallCount);
        Assert.Equal(0, _default.ChatCallCount);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldRunOnTheDefaultProfileWithNoDecisionModelCall_WhenTheCircuitIsOff()
    {
        // Arrange
        Answer("large", 0.9);
        await using var container = Container(DecisionCircuit.Off, "small", "large");

        // Act
        var output = await RunAsync(container, Crew());

        // Assert
        Assert.Single(output.TaskOutputs);
        Assert.Empty(_model.Requests);
        Assert.Equal(1, _default.ChatCallCount);
        Assert.Equal(0, _large.ChatCallCount);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldFailTheLoadNamingTheLadderKey_WhenTheHostDeclaresNoLadder()
    {
        // Arrange
        await using var container = Container();

        // Act
        var error = await LoadFailureAsync(container, Crew());

        // Assert
        Assert.Contains("agent 'Coder' names the LLM profile 'auto'", error.Message, StringComparison.Ordinal);
        Assert.Contains(LlmProfiles.RouteLadderKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldFailTheLoadNamingTheRung_WhenTheLadderNamesAProfileTheHostDoesNotOffer()
    {
        // Arrange
        await using var container = Container(null, "small", "huge");

        // Act
        var error = await LoadFailureAsync(container, Crew());

        // Assert
        Assert.Contains("'huge'", error.Message, StringComparison.Ordinal);
        Assert.Contains(LlmProfiles.RouteLadderKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-429")]
    public async Task ShouldSpellTheRefusalAsTheLoadersDo_WhenTheFactoryRefusesTheDecisionMode()
    {
        // Arrange
        await using var container = Container(null, "small", "large");

        // Act
        var error = await LoadFailureAsync(container, Crew("decision: { mode: none }"));

        // Assert
        Assert.StartsWith("Crew 'routed': " + Orkeon.Domain.Task.DecisionTaskRules.ModeRefusal([]), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldRefuseTheLoad_WhenACrewWithDecisionModeNoneNamesAuto()
    {
        // Arrange
        await using var container = Container(null, "small", "large");

        // Act
        var error = await LoadFailureAsync(container, Crew("decision: { mode: none }"));

        // Assert
        Assert.Contains("names the profile 'auto', which System One picks", error.Message, StringComparison.Ordinal);
    }

    private const string ManagerOnAuto = """
        name: led
        goal: Lead a task
        process: hierarchical
        managerAgent: chef
        agents:
          chef:
            role: Chef
            goal: Lead the team
            llm:
              profile: auto
          writer:
            role: Writer
            goal: Write the article
        tasks:
          write:
            description: Write the article
            expected_output: An article
        """;

    private const string RelayOnAuto = """
        name: support
        goal: Handle tickets
        process: sequential
        decision:
          mode: optional
        agents:
          writer: { role: Writer, goal: Write the answer }
          analyst:
            role: Analyst
            goal: Read the ticket
            llm:
              profile: auto
        tasks:
          intake:
            description: Read the ticket
            expectedOutput: A summary
            agent: writer
          triage:
            description: Route the ticket
            expectedOutput: A route
            decision:
              questions:
                route:
                  choice:
                    bug: a defect
                    other: anything else
              state: [intake]
              without:
                agent: analyst
                instructions: Read the summary first.
        """;

    private static async Task CreateAsync(ServiceProvider container, string yaml)
    {
        var config = await container.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync(yaml, Ct);
        await container.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, Ct);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldLoadTheCrew_WhenItsManagerIsOnAutoOnAHostWithoutALadder()
    {
        // Arrange: the manager runs on the default profile, System One routes the tasks of an agent only.
        await using var container = Container();

        // Act
        var error = await Record.ExceptionAsync(() => CreateAsync(container, ManagerOnAuto));

        // Assert
        Assert.Null(error);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldLoadTheCrew_WhenItsManagerIsOnAutoInACrewWithDecisionModeNone()
    {
        // Arrange
        await using var container = Container(null, "small", "large");
        var yaml = ManagerOnAuto.Replace("process: hierarchical", "process: hierarchical\ndecision: { mode: none }", StringComparison.Ordinal);

        // Act
        var error = await Record.ExceptionAsync(() => CreateAsync(container, yaml));

        // Assert
        Assert.Null(error);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldLoadTheCrew_WhenItsDecisionRelayIsOnAutoOnAHostWithoutALadder()
    {
        // Arrange: LlmDecisionRelay runs the relay on the default profile.
        await using var container = Container();

        // Act
        var error = await Record.ExceptionAsync(() => CreateAsync(container, RelayOnAuto));

        // Assert
        Assert.Null(error);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldRunTheTaskOnTheRungSystemOnePicks_WhenTheTaskOverridesItsProfileWithAuto()
    {
        // Arrange
        Answer("large", 0.9);
        await using var container = Container(null, "small", "large");
        var yaml = Crew().Replace("""
                llm:
                  profile: auto
            """, string.Empty, StringComparison.Ordinal)
            .Replace("agent: coder", "agent: coder\n    llm_override:\n      profile: auto", StringComparison.Ordinal);

        // Act
        var output = await RunAsync(container, yaml);

        // Assert
        Assert.Single(output.TaskOutputs);
        Assert.Equal(1, _large.ChatCallCount);
        Assert.Equal(0, _default.ChatCallCount);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldRefuseTheLoad_WhenTheAgentNamesAModelBesideAuto()
    {
        // Arrange: the model would run on every rung System One picks.
        await using var container = Container(null, "small", "large");
        var yaml = Crew().Replace("profile: auto", "profile: auto\n      model: model-of-large", StringComparison.Ordinal);

        // Act
        var error = await LoadFailureAsync(container, yaml);

        // Assert
        Assert.StartsWith("Crew 'routed': agent 'coder' (its llm: block, or the crew's) names the model 'model-of-large' beside the LLM profile 'auto'",
            error.Message, StringComparison.Ordinal);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async Task ShouldRefuseTheLoad_WhenTheCrewsLlmBlockGivesAModelToAnAgentOnAuto()
    {
        // Arrange
        await using var container = Container(null, "small", "large");
        var yaml = Crew().Replace("process: sequential", "process: sequential\nllm:\n  model: model-of-small", StringComparison.Ordinal);

        // Act
        var error = await LoadFailureAsync(container, yaml);

        // Assert
        Assert.Contains("names the model 'model-of-small' beside the LLM profile 'auto'", error.Message, StringComparison.Ordinal);
    }
}
