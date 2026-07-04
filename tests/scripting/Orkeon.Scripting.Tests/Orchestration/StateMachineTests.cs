using Jint;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Orchestration;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Orchestration;

public sealed class StateMachineTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();
    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public async Task FSM_minimal_transitions_through_send()
    {
        var fsm = Eval<JsStateMachine>(NewEngine(), """
            stateMachine({
                name: "order",
                initial: "pending",
                states: {
                    pending: { transitions: { approve: { target: "approved" } } },
                    approved: {},
                }
            });
            """);

        Assert.Equal("pending", fsm.current);
        var next = await fsm.send("approve", null);
        Assert.Equal("approved", next);
        Assert.Equal("approved", fsm.current);
    }

    [Fact]
    public void FSM_invalid_initial_throws_InvalidScriptException()
    {
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                stateMachine({ name: "x", initial: "missing", states: { a: {} } });
                """),
            "initial");

        Assert.NotNull(ex);
    }

    [Fact]
    public void FSM_transition_to_undeclared_target_throws_InvalidScriptException()
    {
        var ex = ThrowsContaining<InvalidScriptException>(
            () => NewEngine().Evaluate("""
                stateMachine({
                    name: "x",
                    initial: "a",
                    states: {
                        a: { transitions: { go: { target: "ghost" } } },
                    }
                });
                """),
            "ghost");

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task FSM_guard_returning_false_blocks_transition()
    {
        var fsm = Eval<JsStateMachine>(NewEngine(), """
            stateMachine({
                name: "g",
                initial: "a",
                states: {
                    a: { transitions: { go: { target: "b", guard: () => false } } },
                    b: {},
                }
            });
            """);

        var next = await fsm.send("go", null);

        Assert.Equal("a", next);
    }

    [Fact]
    public async Task FSM_guard_returning_true_allows_transition()
    {
        var fsm = Eval<JsStateMachine>(NewEngine(), """
            stateMachine({
                name: "g",
                initial: "a",
                states: {
                    a: { transitions: { go: { target: "b", guard: () => true } } },
                    b: {},
                }
            });
            """);

        var next = await fsm.send("go", null);

        Assert.Equal("b", next);
    }

    [Fact]
    public async Task FSM_invokes_onExit_then_onEntry_during_transition()
    {
        var engine = NewEngine();
        engine.SetValue("__order", new List<object>());
        var fsm = Eval<JsStateMachine>(engine, """
            stateMachine({
                name: "h",
                initial: "a",
                states: {
                    a: {
                        onEntry: () => { __order.push("enter:a"); },
                        onExit: () => { __order.push("exit:a"); },
                        transitions: { go: { target: "b" } }
                    },
                    b: {
                        onEntry: () => { __order.push("enter:b"); },
                        onExit: () => { __order.push("exit:b"); },
                    },
                }
            });
            """);

        await fsm.send("go", null);

        var order = (List<object>)engine.GetValue("__order").ToObject()!;
        Assert.Equal(new object[] { "exit:a", "enter:b" }, order);
    }

    [Fact]
    public async Task FSM_unknown_event_keeps_current_state()
    {
        var fsm = Eval<JsStateMachine>(NewEngine(), """
            stateMachine({
                name: "u",
                initial: "a",
                states: { a: {}, b: {} }
            });
            """);

        var next = await fsm.send("unknown", null);

        Assert.Equal("a", next);
    }
}
