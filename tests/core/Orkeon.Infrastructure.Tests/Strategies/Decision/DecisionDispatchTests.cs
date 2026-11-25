using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Crew.Execution;
using Orkeon.Constants.Protocol;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Crew.Strategies;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.Strategies;

/// <summary>#430: a decision task the runner breaks on still says which source it was asking.</summary>
public sealed class DecisionDispatchTests
{
    private readonly FakeDecisionModel _model = new();

    private static CrewTask DecisionTask() =>
        new CrewTaskBuilder().WithKey("triage").Description("Decide").ExpectedOutput("An outcome")
            .WithDecision(TaskDecision.Create(
                [ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "first", ["b"] = "second" })],
                null, null, false, null))
            .Build();

    [Fact]
    [Trait("BR", "CH-430")]
    public async System.Threading.Tasks.Task ShouldFailWithTheModelSource_WhenTheModelThrowsAnExceptionTheRunnerDoesNotCatch()
    {
        // Arrange
        _model.Failure = new InvalidOperationException("the model broke");
        var dispatch = new DecisionDispatch(new DecisionTaskRunner(_model, DecisionCircuit.On, NullLogger<DecisionTaskRunner>.Instance));
        var task = DecisionTask();

        // Act
        var result = await dispatch.RunAsync(task, [task], [], [], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((false, "the model broke", DecisionSources.Model), (result.Success, result.Error, result.DecisionSource));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldSayThatADecisionIsShownNoImage()
    {
        var dispatch = new DecisionDispatch(new DecisionTaskRunner(_model, DecisionCircuit.On, NullLogger<DecisionTaskRunner>.Instance));
        var task = DecisionTask();
        using var run = RunImages.Begin([new Orkeon.Application.Interfaces.Services.CrewInputImage { Path = "/inbox/photo.png" }]);

        await dispatch.RunAsync(task, [task], [], [], TestContext.Current.CancellationToken);

        var unseen = Assert.Single(RunImages.Unseen);
        Assert.Equal(("/inbox/photo.png", "the decision 'triage'", "a decision reads text alone"), (unseen.Path, unseen.Reader, unseen.Reason));
    }
}
