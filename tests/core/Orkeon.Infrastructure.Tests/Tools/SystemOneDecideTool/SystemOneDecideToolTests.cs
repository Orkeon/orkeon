using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Orkeon.Application.Decision;
using Orkeon.Application.Decision.DTOs;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Tools.SystemOneDecideTool;

public class SystemOneDecideToolTests
{
    private readonly FakeDecisionModel _model = new()
    {
        Respond = _ => new DecisionResponse
        {
            Model = "jev-1.14.0",
            Answers = ImmutableDictionary<string, DecisionAnswer>.Empty.Add("urgent", new NoulAnswer { Noul = 0.87 }),
        },
    };

    private static ToolCallRequest Call(object? questions, object? state = null) => new(
        ToolName: "system_one_decide",
        Parameters: new Dictionary<string, object?>
        {
            ["state"] = state ?? "The server is down for every customer.",
            ["questions"] = questions,
        });

    private static readonly JsonObject Urgent = JsonNode.Parse("""
        {"urgent":{"type":"noul","criteria":{"true":"The ticket needs an answer within the hour"}}}
        """)!.AsObject();

    [Fact]
    [Trait("BR", "CH-430")]
    public async Task ShouldReturnAnswersAndProbabilitiesInOrdinalOrder_WhenTheModelAnswersInHashOrder()
    {
        // Arrange: ten answers of ten options each, in maps whose order is their per-process hash order.
        string[] names = ["whiskey", "alpha", "tango", "bravo", "oscar", "charlie", "kilo", "delta", "zulu", "echo"];
        var probabilities = names.ToImmutableDictionary(n => n, _ => 0.1);
        _model.Respond = _ => new DecisionResponse
        {
            Model = "jev-1.14.0",
            Answers = names.ToImmutableDictionary(n => "q-" + n, _ => (DecisionAnswer)new ChoiceAnswer { Choice = "kilo", Confidence = 0.1, Probabilities = probabilities }),
        };
        using var tool = new Orkeon.Infrastructure.Tools.SystemOneDecideTool(_model);

        // Act
        var result = await tool.CallAsync(Call(Urgent), TestContext.Current.CancellationToken);

        // Assert: the agent reads the same answer in every process.
        Assert.True(result.Success, result.Error);
        var answers = JsonNode.Parse(JsonSerializer.Serialize(result.Result))!["answers"]!.AsObject();
        var ordinal = names.Order(StringComparer.Ordinal).ToList();
        Assert.Equal(ordinal.Select(n => "q-" + n), answers.Select(a => a.Key));
        foreach (var (_, answer) in answers)
            Assert.Equal(ordinal, answer!["probabilities"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public void ShouldCarryTheContractName_WhenBuilt()
    {
        using var tool = new Orkeon.Infrastructure.Tools.SystemOneDecideTool(_model);

        Assert.Equal("system_one_decide", tool.Name);
        Assert.Equal(Orkeon.Domain.Tools.ToolAccess.Read, tool.Access);
    }

    [Fact]
    public async Task ShouldReturnTheTypedAnswers_WhenTheModelDecides()
    {
        using var tool = new Orkeon.Infrastructure.Tools.SystemOneDecideTool(_model);

        var result = await tool.CallAsync(Call(Urgent), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        var sent = Assert.Single(_model.Requests);
        Assert.IsType<NoulQuestion>(sent.Questions["urgent"]);
        Assert.Equal("The server is down for every customer.", sent.State.GetValue<string>());
        var json = JsonSerializer.Serialize(result.Result);
        Assert.Contains("0.87", json, StringComparison.Ordinal);
        Assert.Contains("jev-1.14.0", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailWithTheModelError_WhenTheModelRefuses()
    {
        _model.Failure = new DecisionModelException("TypeSafe found the request invalid (422): questions.urgent");
        using var tool = new Orkeon.Infrastructure.Tools.SystemOneDecideTool(_model);

        var result = await tool.CallAsync(Call(Urgent), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("422", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldFailWithoutCalling_WhenAQuestionIsABox()
    {
        using var tool = new Orkeon.Infrastructure.Tools.SystemOneDecideTool(_model);
        var box = JsonNode.Parse("""{"where":{"type":"box"}}""");

        var result = await tool.CallAsync(Call(box), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Contains("box", result.Error, StringComparison.Ordinal);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task ShouldFailWithoutCalling_WhenThereIsNoQuestion()
    {
        using var tool = new Orkeon.Infrastructure.Tools.SystemOneDecideTool(_model);

        var result = await tool.CallAsync(Call(new JsonObject()), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    [Trait("BR", "CH-433")]
    public async Task ShouldFailWithTheCauseWithoutCalling_WhenTheRunIsOffAndTheHostCircuitIsOn()
    {
        // Arrange
        using var services = new ServiceCollection()
            .AddSingleton<Orkeon.Application.Interfaces.Ports.IDecisionModel>(_model)
            .AddSingleton(Orkeon.Application.Crew.Execution.DecisionCircuit.On)
            .BuildServiceProvider();
        using var tool = new Orkeon.Infrastructure.Tools.SystemOneDecideTool(_model, services);
        using var run = Orkeon.Application.Crew.Execution.DecisionCircuitScope.Begin(Orkeon.Application.Crew.Execution.DecisionCircuit.Off);

        // Act
        var result = await tool.CallAsync(Call(Urgent), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(
            !result.Success && _model.Requests.Count == 0
                && (result.Error ?? "").Contains("the decision circuit is off: --no-decision", StringComparison.Ordinal),
            $"success={result.Success} requests={_model.Requests.Count} error={result.Error}");
    }
}
