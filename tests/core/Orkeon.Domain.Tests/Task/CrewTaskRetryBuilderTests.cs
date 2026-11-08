using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.Task;

/// <summary>
/// FWK-15: a task carries a retry policy through the builder; a decision task runs no agent, so it has
/// nothing to retry and the build refuses the pair, as the YAML and script loaders do.
/// </summary>
public sealed class CrewTaskRetryBuilderTests
{
    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldCarryTheRetryPolicyToTheTask()
    {
        var task = new CrewTaskBuilder().Description("work").ExpectedOutput("done")
            .Retry(new TaskRetryPolicy(3, 100, TaskRetryBackoff.Exponential)).Build();

        Assert.Equal(new TaskRetryPolicy(3, 100, TaskRetryBackoff.Exponential), task.Retry);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldRefuseARetryOnADecisionTask()
    {
        var decision = TaskDecision.Create(
            [ChoiceQuestionSpec.Create("intent", "Pick", new Dictionary<string, string> { ["a"] = "first", ["b"] = "second" })],
            null, null, false, null);
        var builder = new CrewTaskBuilder().Description("decide").ExpectedOutput("an outcome")
            .WithDecision(decision).Retry(new TaskRetryPolicy(2));

        var ex = Assert.Throws<BuilderValidationException>(() => builder.Build());

        Assert.Contains("retry", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
