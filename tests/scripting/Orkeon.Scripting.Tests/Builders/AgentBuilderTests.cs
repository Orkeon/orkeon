using Jint;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Exceptions;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Builders;

public sealed class AgentBuilderTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static T Eval<T>(string js)
    {
        var engine = NewEngine();
        var v = engine.Evaluate(js);
        return (T)v.ToObject()!;
    }

    [Fact]
    public void AgentBuilder_minimum_required_fields_builds()
    {
        var agent = Eval<JsAgent>(
            """agentBuilder().name("Researcher").role("Senior research analyst").goal("Find authoritative sources").build();""");

        Assert.Equal("Researcher", agent.name);
        Assert.False(string.IsNullOrEmpty(agent.id));
    }

    [Fact]
    public void AgentBuilder_missing_role_throws_InvalidScriptException()
    {
        var engine = NewEngine();

        var ex = ThrowsContaining<InvalidScriptException>(
            () => engine.Evaluate("""agentBuilder().name("X").goal("G").build();"""),
            "role");

        Assert.NotNull(ex);
    }

    [Fact]
    public void AgentBuilder_id_is_ULID_auto_generated_26_chars()
    {
        var agent = Eval<JsAgent>(
            """agentBuilder().name("A").role("R").goal("G").build();""");

        Assert.Equal(26, agent.id.Length);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{26}$", agent.id);
    }

    [Fact]
    public void AgentBuilder_with_autonomous_tools_attaches_them()
    {
        var agent = Eval<JsAgent>(
            """
            const tool = n => toolBuilder().name(n).description("d").execute(() => n).build();
            const a = agentBuilder()
              .name("A").role("R").goal("G")
              .withAutonomousTool(tool("tool1"))
              .withAutonomousTool(tool("tool2"))
              .build();
            a;
            """);

        Assert.Equal(["tool1", "tool2"], agent.Builder.AutonomousTools.Select(t => t.Name));
    }

    [Theory]
    [InlineData("""agentBuilder().withAutonomousTool({ name: "tool1" })""")]
    [InlineData("""agentBuilder().withAutonomousTool("file_read")""")]
    [InlineData("""agentBuilder().withAutonomousTools([{ name: "t1" }])""")]
    [InlineData("""agentBuilder().withAutonomousTools({ name: "t1" })""")]
    public void AgentBuilder_withAutonomousTool_refuses_what_is_not_a_built_tool(string js)
    {
        // A plain object used to be stored and read by nothing: the declarative adapter took
        // names and tool instances only, so it vanished without a word (GAP-12).
        var ex = Assert.ThrowsAny<Exception>(() => new JsEngineFactory().Create().Evaluate(js));

        Assert.Contains("toolBuilder()", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AgentBuilder_withAutonomousTools_iterates_array()
    {
        var agent = Eval<JsAgent>(
            """
            const a = agentBuilder()
              .name("A").role("R").goal("G")
              .withAutonomousTools(["t1", "t2", "t3"].map(n => toolBuilder().name(n).description("d").execute(() => n).build()))
              .build();
            a;
            """);

        Assert.Equal(3, agent.Builder.AutonomousTools.Count);
    }

    [Fact]
    public void AgentBuilder_captures_state_factory_and_body_for_later_invocation()
    {
        var agent = Eval<JsAgent>(
            """
            const a = agentBuilder()
              .name("A").role("R").goal("G")
              .withState(() => ({ counter: 0 }))
              .body(async (input, ctx) => `done:${input}`)
              .build();
            a;
            """);

        Assert.NotNull(agent.Builder.StateFactory);
        Assert.NotNull(agent.Builder.BodyFunction);
    }

    [Fact]
    public void AgentBuilder_respects_maxIterations_concurrency_verbose_and_allowDelegation()
    {
        var agent = Eval<JsAgent>(
            """
            agentBuilder().name("A").role("R").goal("G")
              .maxIterations(50)
              .concurrency(1)
              .verbose(true)
              .allowDelegation(true)
              .build();
            """);

        Assert.Equal(50, agent.Builder.MaxIterationsValue);
        Assert.Equal(1, agent.Builder.ConcurrencyValue);
        Assert.True(agent.Builder.VerboseFlag);
        Assert.True(agent.Builder.AllowDelegationFlag);
        Assert.True(agent.Domain.AllowDelegation);
        Assert.Equal(50, agent.Domain.MaxIterations);
    }
}
