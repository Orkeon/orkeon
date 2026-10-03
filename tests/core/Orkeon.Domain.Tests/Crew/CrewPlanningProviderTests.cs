using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using CrewAggregate = Orkeon.Domain.Crew.Crew;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// GAP-33, decision 2 — <c>Planning</c> switches the planner on, <c>WithPlanningLlm</c> chooses what
/// it runs on. A planning provider given to a crew that does not plan was kept, then lost without a
/// word: the crew refuses it now, like a memory provider without memory (GAP-30), and says how to
/// fix it.
/// </summary>
public sealed class CrewPlanningProviderTests
{
    [Fact]
    public void The_builder_refuses_a_planning_provider_without_planning()
    {
        var builder = new CrewBuilder().Goal("Write the report").WithPlanningLlm(new StubLlmProvider());

        var error = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.Contains("WithPlanningLlm", error.Message, StringComparison.Ordinal);
        Assert.Contains(".Planning()", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_refuses_it_when_planning_is_switched_off_after_it()
    {
        var builder = new CrewBuilder().Goal("Write the report").Planning().WithPlanningLlm(new StubLlmProvider()).Planning(false);

        Assert.Throws<ArgumentException>(() => builder.Build());
    }

    [Fact]
    public void The_options_refuse_a_planning_provider_without_planning()
    {
        var error = Assert.Throws<ArgumentException>(() => CrewAggregate.Create(new CrewCreateOptions
        {
            Goal = "Write the report",
            PlanningLlm = new StubLlmProvider(),
        }));

        Assert.Contains("WithPlanningLlm", error.Message, StringComparison.Ordinal);
        Assert.Contains(".Planning()", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_planning_provider_with_planning_is_the_one_the_crew_plans_on()
    {
        var provider = new StubLlmProvider();

        var crew = new CrewBuilder().Goal("Write the report").Planning().WithPlanningLlm(provider).Build();

        Assert.True(crew.Planning);
        Assert.Same(provider, crew.PlanningLlm);
    }

    [Fact]
    public void Planning_cannot_be_turned_off_under_a_planning_provider()
    {
        var crew = new CrewBuilder().Goal("Write the report").Planning().WithPlanningLlm(new StubLlmProvider()).Build();

        var error = Assert.Throws<InvalidOperationException>(() => crew.UpdateConfiguration(planning: false));

        Assert.Contains("WithPlanningLlm", error.Message, StringComparison.Ordinal);
        Assert.True(crew.Planning);
    }

    [Fact]
    public void Planning_turns_off_on_a_crew_that_plans_on_the_default_profile()
    {
        var crew = new CrewBuilder().Goal("Write the report").Planning().Build();

        crew.UpdateConfiguration(planning: false);

        Assert.False(crew.Planning);
    }

    private sealed class StubLlmProvider : ILlmProvider
    {
        public string Name => "StubLlm";

        public System.Threading.Tasks.Task<LlmResponse> GenerateAsync(string prompt, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });

        public System.Threading.Tasks.Task<LlmResponse> ChatAsync(LlmMessage[] messages, LlmConfig? config = null, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new LlmResponse { Content = "stub" });
    }
}
