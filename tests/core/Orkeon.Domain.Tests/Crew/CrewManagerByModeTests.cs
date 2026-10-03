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

    [Fact]
    public void The_builder_refuses_a_manager_id_set_before_the_mode_changed()
    {
        var chef = Agent("Chef");
        var builder = new CrewBuilder().Goal("Ship the article").Hierarchical(chef).WithAgent(chef).Process(ProcessType.Graph);

        var error = Assert.Throws<BuilderValidationException>(() => builder.Build());

        Assert.Contains("Graph", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_builder_tells_an_autonomous_crew_its_manager_is_an_llm()
    {
        var chef = Agent("Chef");
        var builder = new CrewBuilder().Goal("Ship the article").Process(ProcessType.Autonomous).WithAgent(chef).WithManagerId(chef.Id);

        var error = Assert.Throws<BuilderValidationException>(() => builder.Build());

        Assert.Contains("WithManagerLlm", error.Message, StringComparison.Ordinal);
        Assert.Contains("default profile", error.Message, StringComparison.Ordinal);
    }

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

    [Fact]
    public void The_builder_keeps_a_manager_agent_where_the_mode_uses_one()
    {
        var chair = Agent("Chair");

        var hierarchical = new CrewBuilder().Goal("Ship the article").Hierarchical(chair).WithAgent(chair).Build();
        var consensual = new CrewBuilder().Goal("Ship the article").Consensual().WithAgent(chair).WithManager(chair).Build();

        Assert.Equal(chair.Id, hierarchical.ManagerAgentId);
        Assert.Equal(chair.Id, consensual.ManagerAgentId);
    }

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

    // ── SetManagerAgent, ChangeProcessType, Validate, RemoveAgent ───────────────────────

    [Fact]
    public void A_consensual_crew_takes_a_manager_agent_among_its_agents()
    {
        var crew = CrewAggregate.Create("Vote", ProcessType.Consensual);
        var chair = AgentId.Create();
        crew.AddAgent(AgentId.Create());
        crew.AddAgent(chair);

        crew.SetManagerAgent(chair);

        Assert.Equal(chair, crew.ManagerAgentId);
    }

    [Fact]
    public void A_sequential_crew_takes_no_manager_agent()
    {
        var crew = CrewAggregate.Create("Write");
        var agent = AgentId.Create();
        crew.AddAgent(agent);

        var error = Assert.Throws<InvalidOperationException>(() => crew.SetManagerAgent(agent));

        Assert.Contains("Sequential", error.Message, StringComparison.Ordinal);
        Assert.Contains("Hierarchical", error.Message, StringComparison.Ordinal);
        Assert.Contains("Consensual", error.Message, StringComparison.Ordinal);
        Assert.Null(crew.ManagerAgentId);
    }

    [Fact]
    public void Turning_sequential_drops_the_manager_agent_which_stays_a_member()
    {
        var chef = AgentId.Create();
        var crew = CrewAggregate.Create("Ship", ProcessType.Hierarchical, managerAgentId: chef);
        crew.AddAgent(chef);
        crew.AddAgent(AgentId.Create());
        crew.AddTask(TaskId.Create());

        crew.ChangeProcessType(ProcessType.Sequential);

        Assert.Null(crew.ManagerAgentId);
        Assert.Contains(chef, crew.Agents);
        Assert.True(crew.Validate().IsValid);
    }

    [Fact]
    public void Turning_sequential_refuses_a_manager_agent_passed_with_it()
    {
        var crew = CrewAggregate.Create("Ship");
        var agent = AgentId.Create();
        crew.AddAgent(agent);

        Assert.Throws<InvalidOperationException>(() => crew.ChangeProcessType(ProcessType.Sequential, agent));
        Assert.Null(crew.ManagerAgentId);
    }

    [Fact]
    public void Turning_consensual_sets_the_agent_passed_as_the_arbiter()
    {
        var crew = CrewAggregate.Create("Vote");
        var chair = AgentId.Create();
        crew.AddAgent(AgentId.Create());
        crew.AddAgent(chair);

        crew.ChangeProcessType(ProcessType.Consensual, chair);

        Assert.Equal(ProcessType.Consensual, crew.ProcessType);
        Assert.Equal(chair, crew.ManagerAgentId);
    }

    [Fact]
    public void Turning_to_a_mode_with_a_manager_refuses_an_agent_outside_the_crew()
    {
        var crew = CrewAggregate.Create("Vote");
        crew.AddAgent(AgentId.Create());

        Assert.Throws<InvalidOperationException>(() => crew.ChangeProcessType(ProcessType.Consensual, AgentId.Create()));
        Assert.Equal(ProcessType.Sequential, crew.ProcessType);
    }

    [Theory]
    [MemberData(nameof(ModesWithoutAManagerLlm))]
    public void A_crew_with_a_manager_llm_does_not_turn_to_a_mode_that_never_calls_it(string process)
    {
        var crew = CrewAggregate.Create("Ship", ProcessType.Autonomous, managerLlm: new StubLlmProvider());
        crew.AddAgent(AgentId.Create());

        var error = Assert.Throws<InvalidOperationException>(() => crew.ChangeProcessType(ProcessType.From(process)));

        Assert.Contains("WithManagerLlm", error.Message, StringComparison.Ordinal);
        Assert.Equal(ProcessType.Autonomous, crew.ProcessType);
    }

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

    [Fact]
    public void A_consensual_crew_whose_manager_leaves_has_no_arbiter_left()
    {
        var chair = AgentId.Create();
        var crew = CrewAggregate.Create("Vote", ProcessType.Consensual, managerAgentId: chair);
        crew.AddAgent(AgentId.Create());
        crew.AddAgent(chair);

        crew.RemoveAgent(chair, "left the board");

        Assert.Null(crew.ManagerAgentId);
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
