using Jint;
using Jint.Native;
using Jint.Runtime;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;
using Orkeon.Scripting.Orchestration;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Orchestration;

public sealed class StateMachineTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();
    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;
    private static JsValue Js(Engine engine, string js) => engine.Evaluate(js);

    /// <summary>
    /// Calls <c>fsm.send(event)</c> the way a script does — <c>send</c> is a JS async function —
    /// with the engine at rest, so the test thread is the only drainer.
    /// </summary>
    private static async Task<string> SendAsync(Engine engine, JsStateMachine fsm, string eventName)
    {
        var promise = engine.Invoke(fsm.send, eventName);
        var settled = await promise.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);
        return settled.AsString();
    }

    [Fact]
    public async Task FSM_minimal_transitions_through_send()
    {
        var engine = NewEngine();
        var fsm = Eval<JsStateMachine>(engine, """
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
        var next = await SendAsync(engine, fsm, "approve");
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
        var engine = NewEngine();
        var fsm = Eval<JsStateMachine>(engine, """
            stateMachine({
                name: "g",
                initial: "a",
                states: {
                    a: { transitions: { go: { target: "b", guard: () => false } } },
                    b: {},
                }
            });
            """);

        var next = await SendAsync(engine, fsm, "go");

        Assert.Equal("a", next);
    }

    [Fact]
    public async Task FSM_guard_returning_true_allows_transition()
    {
        var engine = NewEngine();
        var fsm = Eval<JsStateMachine>(engine, """
            stateMachine({
                name: "g",
                initial: "a",
                states: {
                    a: { transitions: { go: { target: "b", guard: () => true } } },
                    b: {},
                }
            });
            """);

        var next = await SendAsync(engine, fsm, "go");

        Assert.Equal("b", next);
    }

    [Fact]
    public async Task FSM_async_guard_is_awaited_and_a_veto_fires_no_hook()
    {
        var engine = NewEngine();
        engine.SetValue("__order", new List<object>());
        var fsm = Eval<JsStateMachine>(engine, """
            stateMachine({
                name: "g",
                initial: "a",
                states: {
                    a: {
                        onExit: () => { __order.push("exit:a"); },
                        transitions: {
                            veto: { target: "b", guard: async () => { await Promise.resolve(); return false; } },
                            pass: { target: "b", guard: async () => { await Promise.resolve(); return true; } },
                        }
                    },
                    b: { onEntry: () => { __order.push("enter:b"); } },
                }
            });
            """);

        var vetoed = await SendAsync(engine, fsm, "veto");
        var passed = await SendAsync(engine, fsm, "pass");

        var order = (List<object>)engine.GetValue("__order").ToObject()!;
        Assert.Equal("a", vetoed);
        Assert.Equal("b", passed);
        Assert.Equal(new object[] { "exit:a", "enter:b" }, order);
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

        await SendAsync(engine, fsm, "go");

        var order = (List<object>)engine.GetValue("__order").ToObject()!;
        Assert.Equal(new object[] { "exit:a", "enter:b" }, order);
    }

    [Fact]
    public async Task FSM_hooks_see_the_state_they_frame_and_the_payload()
    {
        var engine = NewEngine();
        engine.SetValue("__seen", new List<object>());
        var fsm = Eval<JsStateMachine>(engine, """
            stateMachine({
                name: "p",
                initial: "a",
                states: {
                    a: {
                        onExit: (c) => { __seen.push("exit:" + c.state + ":" + c.payload.id); },
                        transitions: { go: { target: "b", guard: (c) => { __seen.push("guard:" + c.state + ":" + c.payload.id); return true; } } }
                    },
                    b: {
                        onEntry: (c) => { __seen.push("enter:" + c.state + ":" + c.payload.id); },
                        transitions: { back: { target: "a" } }
                    },
                }
            });
            """);

        var promise = engine.Invoke(fsm.send, "go", Js(engine, "({ id: 7 })"));
        await promise.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);

        var seen = (List<object>)engine.GetValue("__seen").ToObject()!;
        Assert.Equal(new object[] { "guard:a:7", "exit:a:7", "enter:b:7" }, seen);
    }

    [Fact]
    public async Task FSM_hooks_read_payload_as_undefined_when_send_had_none()
    {
        var engine = NewEngine();
        var fsm = Eval<JsStateMachine>(engine, """
            globalThis.__payload = "unset";
            stateMachine({
                name: "p",
                initial: "a",
                states: {
                    a: { transitions: { go: { target: "b" } } },
                    b: { onEntry: (c) => { globalThis.__payload = typeof c.payload; } },
                }
            });
            """);

        await SendAsync(engine, fsm, "go");

        Assert.Equal("undefined", engine.GetValue("__payload").AsString());
    }

    [Fact]
    public async Task FSM_unknown_event_keeps_current_state()
    {
        var engine = NewEngine();
        var fsm = Eval<JsStateMachine>(engine, """
            stateMachine({
                name: "u",
                initial: "a",
                states: { a: {}, b: {} }
            });
            """);

        var next = await SendAsync(engine, fsm, "unknown");

        Assert.Equal("a", next);
    }

    [Fact]
    public async Task FSM_blank_event_rejects_with_a_typed_ArgumentException()
    {
        var engine = NewEngine();
        var fsm = Eval<JsStateMachine>(engine, """
            stateMachine({ name: "u", initial: "a", states: { a: {} } });
            """);

        var rejected = await Assert.ThrowsAsync<PromiseRejectedException>(() => SendAsync(engine, fsm, " "));

        Assert.IsType<ArgumentException>(JsHostError.Unwrap(rejected.RejectedValue), exactMatch: false);
        Assert.Equal("a", fsm.current);
    }

    [Fact]
    public async Task FSM_send_is_awaitable_from_a_script_and_a_script_catch_sees_a_bridged_failure()
    {
        var engine = NewEngine();
        var result = await engine.EvaluateAsync("""
            (async () => {
                const fsm = stateMachine({
                    name: "s", initial: "a",
                    states: {
                        a: { transitions: { go: { target: "b" } } },
                        b: { onEntry: async () => { await Promise.resolve(); } },
                    }
                });
                const moved = await fsm.send("go");
                let caught = "none";
                try { await fsm.send(""); } catch (e) { caught = e.clrType; }
                return moved + "|" + fsm.current + "|" + caught;
            })()
            """, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("b|b|ArgumentException", result.AsString());
    }

    [Fact]
    public void FSM_send_is_one_JS_function_per_machine()
    {
        var engine = NewEngine();
        var fsm = Eval<JsStateMachine>(engine, """
            stateMachine({ name: "u", initial: "a", states: { a: {} } });
            """);

        Assert.True(fsm.send.IsObject());
        Assert.Same(fsm.send, fsm.send);
        Assert.Equal("send", fsm.send.AsObject().Get("name").AsString());
        Assert.True(engine.Evaluate("typeof stateMachine({ name: 'v', initial: 'a', states: { a: {} } }).send === 'function'").AsBoolean());
    }
}
