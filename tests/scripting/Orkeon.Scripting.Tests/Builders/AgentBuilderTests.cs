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
            const a = agentBuilder()
              .name("A").role("R").goal("G")
              .withAutonomousTool({ name: "tool1" })
              .withAutonomousTool({ name: "tool2" })
              .build();
            a;
            """);

        Assert.Equal(2, agent.Builder.AutonomousTools.Count);
    }

    [Fact]
    public void AgentBuilder_withAutonomousTools_iterates_array()
    {
        var agent = Eval<JsAgent>(
            """
            const a = agentBuilder()
              .name("A").role("R").goal("G")
              .withAutonomousTools([{ name: "t1" }, { name: "t2" }, { name: "t3" }])
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
