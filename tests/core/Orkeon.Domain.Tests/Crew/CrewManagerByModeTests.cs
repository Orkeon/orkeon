using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using CrewAggregate = Orkeon.Domain.Crew.Crew;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Domain.Tests.Crew;

/// <summary>
/// GAP-33, decision 3 — a manager is applied where the crew's mode uses it and refused elsewhere. A
/// manager agent means something in Hierarchical (it assigns each task and reviews its output) and in
/// Consensual (it arbitrates the ManagerDecision fallback); a manager LLM (C# <c>WithManagerLlm</c>)
/// in Hierarchical and in Autonomous (the manager hands the tasks out on it). Elsewhere it used to be
/// kept and ignored: the agent ran tasks like any other, the provider was never called.
/// </summary>
public sealed class CrewManagerByModeTests
{
    public static TheoryData<string> ModesWithoutAManagerAgent => new() { "Sequential", "Parallel", "Graph", "Autonomous" };

    public static TheoryData<string> ModesWithoutAManagerLlm => new() { "Sequential", "Parallel", "Graph", "Consensual" };

    private static DomainAgent Agent(string role) => new AgentBuilder().Role(role).Goal($"Goal of {role}").Build();

    // ── the builder, in its own words ────────────────────────────────────────────────────

    [Trait("BR", "CH-08")]
    [Theory]
    [MemberData(nameof(ModesWithoutAManagerAgent))]
    public void The_builder_refuses_a_manager_agent_in_a_mode_without_one(string process)
    {
        var chef = Agent("Chef");
        var builder = new CrewBuilder().Goal("Ship the article").Process(ProcessType.From(process)).WithAgent(chef).WithManager(chef);

        var error = Assert.Throws<BuilderValidationException>(() => builder.Build());

        Assert.Equal("Crew", error.BuilderName);
        Assert.Contains(".WithManager", error.Message, StringComparison.Ordinal);
        Assert.Contains(process, error.Message, StringComparison.Ordinal);
        Assert.Contains(".Hierarchical(", error.Message, StringComparison.Ordinal);
        Assert.Contains(".Consensual()", error.Message, StringComparison.Ordinal);
    }

    [Trait("BR", "CH-08")]
    [Fact]
    public void The_builder_refuses_a_manager_id_set_before_the_mode_changed()
    {
        var chef = Agent("Chef");
        var builder = new CrewBuilder().Goal("Ship the article").Hierarchical(chef).WithAgent(chef).Process(ProcessType.Graph);

        var error = Assert.Throws<BuilderValidationException>(() => builder.Build());

        Assert.Contains("Graph", error.Message, StringComparison.Ordinal);
    }

    [Trait("BR", "CH-08")]
    [Fact]
    public void The_builder_tells_an_autonomous_crew_its_manager_is_an_llm()
    {
        var chef = Agent("Chef");
        var builder = new CrewBuilder().Goal("Ship the article").Process(ProcessType.Autonomous).WithAgent(chef).WithManagerId(chef.Id);

        var error = Assert.Throws<BuilderValidationException>(() => builder.Build());

        Assert.Contains("WithManagerLlm", error.Message, StringComparison.Ordinal);
        Assert.Contains("default profile", error.Message, StringComparison.Ordinal);
    }

    [Trait("BR", "CH-08")]
    [Theory]
    [MemberData(nameof(ModesWithoutAManagerLlm))]
    public void The_builder_refuses_a_manager_llm_in_a_mode_that_never_calls_it(string process)
    {
        var builder = new CrewBuilder().Goal("Ship the article").Process(ProcessType.From(process))
            .WithAgent(Agent("Writer")).WithManagerLlm(new StubLlmProvider());

        var error = Assert.Throws<BuilderValidationException>(() => builder.Build());

        Assert.Contains(".WithManagerLlm", error.Message, StringComparison.Ordinal);
        Assert.Contains(process, error.Message, StringComparison.Ordinal);
    }

    [Trait("BR", "CH-08")]
    [Fact]
    public void The_builder_keeps_a_manager_agent_where_the_mode_uses_one()
    {
        var chair = Agent("Chair");

        var hierarchical = new CrewBuilder().Goal("Ship the article").Hierarchical(chair).WithAgent(chair).Build();
        var consensual = new CrewBuilder().Goal("Ship the article").Consensual().WithAgent(chair).WithManager(chair).Build();

        Assert.Equal(chair.Id, hierarchical.ManagerAgentId);
        Assert.Equal(chair.Id, consensual.ManagerAgentId);
    }

    [Trait("BR", "CH-08")]
    [Fact]
    public void The_builder_keeps_a_manager_llm_where_the_mode_reads_one()
    {
        var provider = new StubLlmProvider();

        var hierarchical = new CrewBuilder().Goal("Ship the article").Hierarchical().WithManagerLlm(provider).WithAgent(Agent("Writer")).Build();
        var autonomous = new CrewBuilder().Goal("Ship the article").Process(ProcessType.Autonomous).WithManagerLlm(provider).WithAgent(Agent("Writer")).Build();

        Assert.Same(provider, hierarchical.ManagerLlm);
        Assert.Same(provider, autonomous.ManagerLlm);
    }

    // ── Crew.Create(CrewCreateOptions) ───────────────────────────────────────────────────

    [Trait("BR", "CH-08")]
    [Theory]
    [MemberData(nameof(ModesWithoutAManagerAgent))]
    public void The_options_refuse_a_manager_agent_in_a_mode_without_one(string process)
    {
        var error = Assert.Throws<ArgumentException>(() => CrewAggregate.Create(new CrewCreateOptions
        {
            Goal = "Ship the article",
            ProcessType = ProcessType.From(process),
            ManagerAgentId = AgentId.Create(),
        }));

        Assert.Contains(process, error.Message, StringComparison.Ordinal);
        Assert.Contains("Hierarchical", error.Message, StringComparison.Ordinal);
        Assert.Contains("Consensual", error.Message, StringComparison.Ordinal);
    }

    [Trait("BR", "CH-08")]
    [Theory]
    [MemberData(nameof(ModesWithoutAManagerLlm))]
    public void The_options_refuse_a_manager_llm_in_a_mode_that_never_calls_it(string process)
    {
        var error = Assert.Throws<ArgumentException>(() => CrewAggregate.Create(new CrewCreateOptions
        {
            Goal = "Ship the article",
            ProcessType = ProcessType.From(process),
            ManagerLlm = new StubLlmProvider(),
        }));

        Assert.Contains(process, error.Message, StringComparison.Ordinal);
        Assert.Contains("Autonomous", error.Message, StringComparison.Ordinal);
    }

    // ── Validate ──

    [Trait("BR", "CH-08")]
    [Fact]
    public void A_consensual_manager_outside_the_crew_is_invalid()
    {
        var crew = CrewAggregate.Create(new CrewCreateOptions
        {
            Goal = "Vote",
            ProcessType = ProcessType.Consensual,
            ManagerAgentId = AgentId.Create(),
        });
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());

        var result = crew.Validate();

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("member", StringComparison.OrdinalIgnoreCase));
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
