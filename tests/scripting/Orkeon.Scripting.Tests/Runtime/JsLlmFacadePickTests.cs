using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Crew.Routing;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Crew;
using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// <c>ctx.llm.pick(request, { profile, effort })</c> (#460): System One's profile and thinking level for a script that
/// runs no task, through the host's <see cref="ISystemOnePicker"/> as <c>orkeon run</c> and the other hosts wire it.
/// </summary>
public sealed class JsLlmFacadePickTests
{
    private const string Request = "Rename the variable x to count in parser.cs.";

    private readonly FakeDecisionModel _model = new();
    private readonly FakeAgentRouteObserver _observer = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider Host(bool circuitOff = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton<IDecisionModel>(_model);
        services.AddSingleton<ILlmProfileRegistry>(new StubLlmProfileRegistry("small", "medium", "large", "other"));
        services.AddSingleton<IAgentRouteObserver>(_observer);
        services.AddSingleton(Options.Create(new AgentRouteOptions { Ladder = ["small", "large"] }));
        services.AddSingleton(Options.Create(new AgentEffortOptions { Levels = ["low", "medium", "high"] }));
        if (circuitOff)
            services.AddSingleton(DecisionCircuit.Off);
        services.AddOrkeonApplication();
        return services.BuildServiceProvider();
    }

    private static ChoiceAnswer Choice(string choice, double probability) => new()
    {
        Choice = choice,
        Confidence = probability,
        Probabilities = ImmutableDictionary<string, double>.Empty.Add(choice, probability),
    };

    private void Answer(params (string Question, ChoiceAnswer Answer)[] answers) =>
        _model.Respond = _ => new DecisionResponse
        {
            Model = "fake",
            Answers = answers.ToImmutableDictionary(a => a.Question, a => (DecisionAnswer)a.Answer),
        };

    /// <summary>Runs a one-agent crew whose body calls <c>ctx.llm.pick(Request, options)</c> and returns its answer as JSON, or <c>ERR|type|message</c>.</summary>
    private async Task<string> PickAsync(string options, bool circuitOff = false, CancellationToken? ct = null)
    {
        await using var host = Host(circuitOff);
        await using var scope = host.CreateAsyncScope();
        var engine = new JsEngineFactory(hostPorts: new ScriptingHostPorts
        {
            SystemOnePicker = scope.ServiceProvider.GetRequiredService<ISystemOnePicker>(),
        }).Create();
        var crew = (JsCrew)(await engine.EvaluateAsync($$"""
            const planner = agentBuilder().name("planner").role("Planner").goal("Plan")
                .body(async (input, ctx) => {
                    try { return JSON.stringify(await ctx.llm.pick({{JsonSerializer.Serialize(Request)}}, {{options}})); }
                    catch (e) { return ["ERR", e instanceof InvalidScriptError, e.clrType, e.message].join("|"); }
                }).build();
            crewBuilder().name("pick").withAgent(planner).build();
            """, cancellationToken: Ct)).ToObject()!;
        var result = await crew.RunAsync(null, ct ?? Ct);
        return result.tasks[0].output!.ToString()!;
    }

    private static JsonElement Part(string json, string part) => JsonDocument.Parse(json).RootElement.GetProperty(part);

    private static string? Str(JsonElement answer, string key) =>
        answer.GetProperty(key).ValueKind == JsonValueKind.Null ? null : answer.GetProperty(key).GetString();

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldAnswerTheModelsRung_WhenItsProbabilityReachesTheBar()
    {
        // Arrange
        Answer(("profile", Choice("large", 0.8)));

        // Act
        var json = await PickAsync("""{ profile: { ladder: ["small", "large"], fallback: "small" } }""");

        // Assert
        var profile = Part(json, "profile");
        Assert.Equal("large", Str(profile, "value"));
        Assert.Equal("model", Str(profile, "reason"));
        Assert.Equal("large", Str(profile, "picked"));
        Assert.Equal(0.8, profile.GetProperty("probability").GetDouble());
        Assert.False(JsonDocument.Parse(json).RootElement.TryGetProperty("effort", out _));
        Assert.Single(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldAnswerTheFallback_WhenTheProbabilityIsUnderTheBar()
    {
        Answer(("profile", Choice("large", 0.4)));

        var profile = Part(await PickAsync("""{ profile: { ladder: ["small", "large"], fallback: "other" } }"""), "profile");

        Assert.Equal("other", Str(profile, "value"));
        Assert.Equal("low-confidence", Str(profile, "reason"));
        Assert.Equal("large", Str(profile, "picked"));
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldAnswerTheFallback_WhenTheModelGivesNoAnswer()
    {
        _model.Respond = null;

        var profile = Part(await PickAsync("""{ profile: { ladder: ["small", "large"], fallback: "small" } }"""), "profile");

        Assert.Equal("small", Str(profile, "value"));
        Assert.Equal("no-answer", Str(profile, "reason"));
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldAnswerTheFallbackWithNoCall_WhenTheCircuitIsOff()
    {
        Answer(("profile", Choice("large", 0.9)));

        var profile = Part(await PickAsync("""{ profile: { ladder: ["small", "large"], fallback: "small" } }""", circuitOff: true), "profile");

        Assert.Equal("small", Str(profile, "value"));
        Assert.Equal("circuit-off", Str(profile, "reason"));
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldAnswerCircuitOffWithNoCall_WhenTheCrewRunsOnDecisionModeNone()
    {
        Answer(("profile", Choice("large", 0.9)), ("effort", Choice("high", 0.9)));

        string json;
        using (SystemOneRunScope.Begin(CrewDecisionMode.None))
            json = await PickAsync("""{ profile: { ladder: ["small", "large"] }, effort: { fallback: "low" } }""");

        Assert.Equal("circuit-off", Str(Part(json, "profile"), "reason"));
        Assert.Equal("default", Str(Part(json, "profile"), "value"));
        Assert.Equal("circuit-off", Str(Part(json, "effort"), "reason"));
        Assert.Equal("low", Str(Part(json, "effort"), "value"));
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldAnswerTheHostDefault_WhenNoFallbackIsGiven()
    {
        Answer(("profile", Choice("large", 0.3)));

        var profile = Part(await PickAsync("""{ profile: { ladder: ["small", "large"] } }"""), "profile");

        Assert.Equal("default", Str(profile, "value"));
        Assert.Equal("low-confidence", Str(profile, "reason"));
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldTakeTheHostLadder_WhenTheScriptGivesNone()
    {
        Answer(("profile", Choice("large", 0.9)));

        var profile = Part(await PickAsync("""{ profile: {} }"""), "profile");

        Assert.Equal("large", Str(profile, "value"));
        var question = Assert.IsType<ChoiceQuestion>(Assert.Single(_model.Requests).Questions["profile"]);
        Assert.Equal(["large", "small"], question.Criteria.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldClampToTheFloorAndSaySo_WhenThePickIsUnderIt()
    {
        Answer(("profile", Choice("small", 0.9)));

        var profile = Part(await PickAsync("""{ profile: { ladder: ["small", "medium", "large"], floor: "medium" } }"""), "profile");

        Assert.Equal("medium", Str(profile, "value"));
        Assert.Equal("floor", Str(profile, "reason"));
        Assert.Equal("small", Str(profile, "picked"));
        Assert.Single(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldClampToTheCeilingAndSaySo_WhenThePickIsAboveIt()
    {
        Answer(("profile", Choice("large", 0.9)));

        var profile = Part(await PickAsync("""{ profile: { ladder: ["small", "medium", "large"], ceiling: "medium" } }"""), "profile");

        Assert.Equal("medium", Str(profile, "value"));
        Assert.Equal("ceiling", Str(profile, "reason"));
        Assert.Equal("large", Str(profile, "picked"));
    }

    [Theory]
    [Trait("BR", "CH-460")]
    [InlineData("""{ ladder: ["small", "large"], floor: "large" }""", "large", "floor")]
    [InlineData("""{ ladder: ["small", "large"], ceiling: "small" }""", "small", "ceiling")]
    [InlineData("""{ ladder: ["small", "medium", "large"], floor: "medium", ceiling: "medium" }""", "medium", "floor")]
    public async Task ShouldAnswerTheOneRungLeftWithNoCall_WhenTheBoundsLeaveOne(string profileOptions, string rung, string reason)
    {
        Answer(("profile", Choice("small", 0.9)));

        var profile = Part(await PickAsync($$"""{ profile: {{profileOptions}} }"""), "profile");

        Assert.Equal(rung, Str(profile, "value"));
        Assert.Equal(reason, Str(profile, "reason"));
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldAskBothInOneCall_WhenProfileAndEffortAreAsked()
    {
        Answer(("profile", Choice("large", 0.9)), ("effort", Choice("high", 0.7)));

        var json = await PickAsync("""{ profile: { ladder: ["small", "large"] }, effort: {} }""");

        Assert.Equal("large", Str(Part(json, "profile"), "value"));
        Assert.Equal("high", Str(Part(json, "effort"), "value"));
        Assert.Equal("model", Str(Part(json, "effort"), "reason"));
        var request = Assert.Single(_model.Requests);
        Assert.Equal(["effort", "profile"], request.Questions.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldOfferTheHostLevelsBetweenMinAndMax_WhenEffortAloneIsAsked()
    {
        Answer(("effort", Choice("medium", 0.9)));

        var json = await PickAsync("""{ effort: { min: "medium", max: "high" } }""");

        Assert.Equal("medium", Str(Part(json, "effort"), "value"));
        Assert.False(JsonDocument.Parse(json).RootElement.TryGetProperty("profile", out _));
        var question = Assert.IsType<ChoiceQuestion>(Assert.Single(Assert.Single(_model.Requests).Questions).Value);
        Assert.Equal(["high", "medium"], question.Criteria.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldAnswerSingleLevelWithNoCall_WhenOneLevelIsLeft()
    {
        Answer(("effort", Choice("medium", 0.9)));

        var effort = Part(await PickAsync("""{ effort: { min: "high" } }"""), "effort");

        Assert.Equal("high", Str(effort, "value"));
        Assert.Equal("single-level", Str(effort, "reason"));
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldAnswerNullEffort_WhenNoFallbackIsGivenAndTheModelGivesNone()
    {
        _model.Respond = null;

        var effort = Part(await PickAsync("""{ effort: {} }"""), "effort");

        Assert.Null(Str(effort, "value"));
        Assert.Equal("no-answer", Str(effort, "reason"));
    }

    [Theory]
    [Trait("BR", "CH-460")]
    [InlineData("""{}""", "options")]
    [InlineData("""{ colour: "red" }""", "options")]
    [InlineData("""{ profile: { ladder: ["small"] } }""", "profile.ladder")]
    [InlineData("""{ profile: { ladder: ["small", "small"] } }""", "profile.ladder")]
    [InlineData("""{ profile: { ladder: ["small", " "] } }""", "profile.ladder")]
    [InlineData("""{ profile: { ladder: ["small", "auto"] } }""", "profile.ladder")]
    [InlineData("""{ profile: { ladder: ["small", "unknown"] } }""", "profile.ladder")]
    [InlineData("""{ profile: { ladder: ["small", "large"], floor: "unknown" } }""", "profile.floor")]
    [InlineData("""{ profile: { ladder: ["small", "large"], ceiling: "other" } }""", "profile.ceiling")]
    [InlineData("""{ profile: { ladder: ["small", "large"], fallback: "unknown" } }""", "profile.fallback")]
    [InlineData("""{ profile: { ladder: ["small", "large"], floor: "large", ceiling: "small" } }""", "profile.floor")]
    [InlineData("""{ effort: { min: "huge" } }""", "effort.min")]
    [InlineData("""{ effort: { fallback: "huge" } }""", "effort.fallback")]
    [InlineData("""{ effort: { min: "high", max: "low" } }""", "effort.min")]
    [InlineData("""{ effort: { min: "max" } }""", "effort")]
    public async Task ShouldRefuseNamingTheOptionBeforeAnyCall_WhenTheOptionsAreWrong(string options, string option)
    {
        Answer(("profile", Choice("large", 0.9)), ("effort", Choice("high", 0.9)));

        var caught = await PickAsync(options);

        var parts = caught.Split('|', 4);
        Assert.Equal("ERR", parts[0]);
        Assert.Equal("true", parts[1]);
        Assert.Equal("InvalidScriptException", parts[2]);
        Assert.StartsWith($"ctx.llm.pick {{ {option} }}", parts[3], StringComparison.Ordinal);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldTellTheObserverWithTheAgentsRoleAndNoTaskId_WhenItPicks()
    {
        Answer(("profile", Choice("large", 0.9)), ("effort", Choice("high", 0.9)));

        await PickAsync("""{ profile: { ladder: ["small", "large"] }, effort: {} }""");

        var notice = Assert.Single(_observer.Notices);
        Assert.Equal("Planner", notice.AgentRole);
        Assert.Equal(string.Empty, notice.TaskId);
        Assert.Equal("large", notice.Profile);
        var effort = Assert.Single(_observer.EffortNotices);
        Assert.Equal("Planner", effort.AgentRole);
        Assert.Equal(string.Empty, effort.TaskId);
        Assert.Equal("high", effort.Level);
        Assert.Contains("Planner", Assert.Single(_model.Requests).State.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-460")]
    public async Task ShouldCancelTheCall_WhenTheRunIsCancelled()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        Answer(("profile", Choice("large", 0.9)));
        _model.OnCall = cts.Cancel;

        var outcome = await Record.ExceptionAsync(() => PickAsync("""{ profile: { ladder: ["small", "large"] } }""", ct: cts.Token));

        Assert.Single(_model.Requests);
        Assert.IsType<OperationCanceledException>(outcome, exactMatch: false);
    }
}
