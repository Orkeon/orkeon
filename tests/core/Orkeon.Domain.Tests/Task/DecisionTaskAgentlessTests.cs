using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;
using TaskStatus = Orkeon.Domain.Task.ValueObjects.TaskStatus;

namespace Orkeon.Domain.Tests.Task;

public class DecisionTaskAgentlessTests
{
    private static TaskDecision Decision() =>
        TaskDecision.Create([ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "A", ["b"] = "B" })]);

    private static CrewTaskBuilder Minimal() =>
        new CrewTaskBuilder().Description("Decide").ExpectedOutput("An outcome");

    private static TaskOutput DecisionOutput() =>
        TaskOutput.Text("{}") with
        {
            Decision = new DecisionOutcome
            {
                Answers = new Dictionary<string, DecisionAnswerValue>
                {
                    ["intent"] = new() { Chosen = "a", Confidence = 0.9 },
                },
            },
        };

    [Fact]
    [Trait("BR", "BR-01")]
    public void ShouldCompleteDecisionTaskWithoutAgent_WhenOutputCarriesDecision()
    {
        var task = Minimal().WithDecision(Decision()).Build();
        var output = DecisionOutput();

        task.Start();
        task.CompleteWithoutAgent(output);

        Assert.Equal(TaskStatus.Completed, task.Status);
        Assert.Same(output, task.Output);
    }

    [Fact]
    [Trait("BR", "BR-01")]
    public void ShouldRefuseAgentlessCompletion_WhenTaskIsNotDecision()
    {
        var task = Minimal().Build();
        task.Start();

        Assert.Throws<InvalidOperationException>(() => task.CompleteWithoutAgent(DecisionOutput()));
        Assert.Equal(TaskStatus.InProgress, task.Status);
        Assert.Null(task.Output);
    }

    [Fact]
    [Trait("BR", "BR-02")]
    public void ShouldAddDecisionTaskToDependencies_WhenConditionSetThroughBuilder()
    {
        var decisionTask = Minimal().WithDecision(Decision()).Build();
        var condition = TaskCondition.Otherwise("decide");

        var task = Minimal().When(decisionTask.Id, condition).Build();
        var declared = Minimal().DependsOn(decisionTask.Id).When(decisionTask.Id, condition).Build();

        Assert.Equal([decisionTask.Id], task.Dependencies);
        Assert.Equal([decisionTask.Id], declared.Dependencies);
    }
}
