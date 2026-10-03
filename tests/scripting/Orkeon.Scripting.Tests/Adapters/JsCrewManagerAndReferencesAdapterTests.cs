using Orkeon.Scripting.Adapters;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Adapters;

/// <summary>
/// GAP-33 — the <c>.ork.ts</c> twin of the YAML rules. <c>crewBuilder().manager(agent)</c> means
/// something in two modes: <c>"hierarchical"</c> (the manager assigns each task and reviews its
/// output) and <c>"consensual"</c> (it arbitrates the ManagerDecision fallback); in the four others it
/// was carried and ignored, the agent running tasks like any other. A task whose agent — or a task it
/// takes as context — is not in the crew was erased the same way. Each is refused when the crew is
/// adapted for the run, naming the call as the script writes it and what the crew has.
/// </summary>
public sealed class JsCrewManagerAndReferencesAdapterTests
{
    private static JsCrew Evaluate(string script) => (JsCrew)new JsEngineFactory().Create().Evaluate(script).ToObject()!;

    private static JsCrew ManagedCrew(string process) => Evaluate($$"""
        const chef = agentBuilder().name("chef").role("Chef").goal("Lead the team").build();
        const writer = agentBuilder().name("writer").role("Writer").goal("Write the article").build();
        const write = taskBuilder().name("write").agent(writer)
            .description("Write the article").expectedOutput("An article").build();

        crewBuilder()
            .name("managed").goal("Ship the article")
            .process("{{process}}")
            .manager(chef)
            .withAgents([chef, writer])
            .withTask(write)
            .build();
        """);

    [Theory]
    [InlineData("sequential")]
    [InlineData("parallel")]
    [InlineData("graph")]
    [InlineData("autonomous")]
    public void A_manager_in_a_mode_without_one_is_refused_naming_the_call_and_the_process(string process)
    {
        var error = Assert.Throws<InvalidOperationException>(() => JsCrewConfigurationAdapter.ToConfiguration(ManagedCrew(process)));

        Assert.Contains(".manager(chef)", error.Message, StringComparison.Ordinal);
        Assert.Contains($".process(\"{process}\")", error.Message, StringComparison.Ordinal);
        Assert.Contains(".process(\"hierarchical\")", error.Message, StringComparison.Ordinal);
        Assert.Contains(".process(\"consensual\")", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_autonomous_crew_is_told_its_manager_is_an_llm()
    {
        var error = Assert.Throws<InvalidOperationException>(() => JsCrewConfigurationAdapter.ToConfiguration(ManagedCrew("autonomous")));

        Assert.Contains("default profile", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hierarchical")]
    [InlineData("consensual")]
    public void Hierarchical_and_consensual_carry_the_manager(string process)
    {
        var config = JsCrewConfigurationAdapter.ToConfiguration(ManagedCrew(process));

        Assert.Equal(config.Agents.Single(a => a.Role == "Chef").Id, config.ManagerAgentId);
    }

    [Fact]
    public void A_manager_that_is_not_in_the_crew_is_refused()
    {
        // The manager joins its crew at build — unless it already belongs to another one.
        var crew = Evaluate("""
            const chef = agentBuilder().name("chef").role("Chef").goal("Lead the team").build();
            const writer = agentBuilder().name("writer").role("Writer").goal("Write the article").build();
            crewBuilder().name("other").goal("Elsewhere").withAgent(chef).build();
            const write = taskBuilder().name("write").agent(writer)
                .description("Write the article").expectedOutput("An article").build();
            crewBuilder()
                .name("managed").goal("Ship the article").process("hierarchical")
                .manager(chef).withAgent(writer).withTask(write)
                .build();
            """);

        var error = Assert.Throws<InvalidOperationException>(() => JsCrewConfigurationAdapter.ToConfiguration(crew));

        Assert.Contains(".manager(chef)", error.Message, StringComparison.Ordinal);
        Assert.Contains("writer", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_task_whose_agent_is_not_in_the_crew_is_refused_listing_its_agents()
    {
        var crew = Evaluate("""
            const writer = agentBuilder().name("writer").role("Writer").goal("Write the article").build();
            const editor = agentBuilder().name("editor").role("Editor").goal("Edit the article").build();
            const write = taskBuilder().name("write").agent(writer)
                .description("Write the article").expectedOutput("An article").build();
            const edit = taskBuilder().name("edit").agent(editor)
                .description("Edit the article").expectedOutput("An edited article").build();
            crewBuilder().name("desk").goal("Ship the article")
                .withAgent(writer).withTasks([write, edit])
                .build();
            """);

        var error = Assert.Throws<InvalidOperationException>(() => JsCrewConfigurationAdapter.ToConfiguration(crew));

        Assert.Contains("'edit'", error.Message, StringComparison.Ordinal);
        Assert.Contains(".agent(editor)", error.Message, StringComparison.Ordinal);
        Assert.Contains("Its agents: writer", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("'write'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_task_whose_context_task_is_not_in_the_crew_is_refused_listing_its_tasks()
    {
        var crew = Evaluate("""
            const writer = agentBuilder().name("writer").role("Writer").goal("Write the article").build();
            const outline = taskBuilder().name("outline").agent(writer)
                .description("Outline the article").expectedOutput("An outline").build();
            const write = taskBuilder().name("write").agent(writer).withContext(outline)
                .description("Write the article").expectedOutput("An article").build();
            crewBuilder().name("desk").goal("Ship the article")
                .withAgent(writer).withTask(write)
                .build();
            """);

        var error = Assert.Throws<InvalidOperationException>(() => JsCrewConfigurationAdapter.ToConfiguration(crew));

        Assert.Contains("'write'", error.Message, StringComparison.Ordinal);
        Assert.Contains(".withContext(outline)", error.Message, StringComparison.Ordinal);
        Assert.Contains("Its tasks: write", error.Message, StringComparison.Ordinal);
    }
}
