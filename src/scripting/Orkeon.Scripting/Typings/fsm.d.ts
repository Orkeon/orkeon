// Orkeon Scripting DSL - Finite state machine literal
// Declares the stateMachine() literal: states, their per-state transitions, guards and
// entry/exit hooks.
//
// This file was rewritten on 2026-09-07 against StateMachineBinding.cs and JsStateMachine.cs
// because the declaration it replaced described an API the runtime never had: a top-level
// `transitions` ARRAY of `{ from, on, to, action }`, definition-level `onEnter`/`onExit`, a
// `circuitBreaker` option, and a `run(ctx)` method. The binding reads none of those. It reads
// a per-state `transitions` OBJECT keyed by event name, whose values carry `target` (not
// `to`), and hooks named `onEntry`/`onExit` on each state; there is no `run`. A script
// written to the old declaration therefore built a machine with ZERO transitions and failed
// silently -- `send()` returns the current state unchanged when it finds no transition.

declare global {
    /**
     * The single argument handed to a guard and to the onEntry/onExit hooks.
     * Note it carries the state, not the whole machine.
     */
    interface FsmHookContext<TState extends string = string, TPayload = unknown> {
        /**
         * For a guard and for onExit, the state being left; for onEntry, the state entered.
         */
        readonly state: TState;
        /** Whatever `send()` was given as its second argument; `undefined` if it had none. */
        readonly payload: TPayload | undefined;
    }

    interface FsmTransition<TState extends string = string, TPayload = unknown> {
        /**
         * Where this event leads. Validated when the literal is built: a target that is not
         * a key of `states` throws `InvalidScriptException` before the machine ever runs.
         */
        target: TState;
        /**
         * Vetoes the transition when it returns false: `send()` then returns the current
         * state unchanged and neither hook fires. A rejected transition is not an error.
         */
        guard?: (ctx: FsmHookContext<TState, TPayload>) => boolean | Promise<boolean>;
    }

    interface FsmState<TState extends string = string, TPayload = unknown> {
        /**
         * Keyed by EVENT NAME -- the string later passed to `send()`. A state with no
         * outgoing transitions declares `{}` (or omits the key), which makes it terminal.
         */
        transitions?: Record<string, FsmTransition<TState, TPayload>>;
        /** Runs after the machine has entered this state. Awaited if it returns a promise. */
        onEntry?: (ctx: FsmHookContext<TState, TPayload>) => void | Promise<void>;
        /** Runs before the machine leaves this state, after the guard has passed. */
        onExit?: (ctx: FsmHookContext<TState, TPayload>) => void | Promise<void>;
    }

    interface FsmDefinition<TState extends string = string, TPayload = unknown> {
        name: string;
        /** Must be a key of `states`; the literal is rejected at build time otherwise. */
        initial: TState;
        states: Record<TState, FsmState<TState, TPayload>>;
    }

    interface StateMachine<TState extends string = string, TPayload = unknown> {
        readonly name: string;
        /** The state the machine is in now. Updated by `send()`. */
        readonly current: TState;
        /**
         * Fires an event and resolves to the state the machine ended up in -- which is the
         * CURRENT state, unchanged, when the event is unknown to it or a guard refused.
         * Compare the result with `current` if you need to know whether it moved.
         */
        send(event: string, payload?: TPayload): Promise<TState>;
    }

    /**
     * Builds a state machine from a literal. Rejects at build time: a missing or non-string
     * `name`/`initial`, a `states` that is not an object, an `initial` that is not declared,
     * a transition without a string `target`, and a `target` naming an undeclared state.
     */
    function stateMachine<TState extends string, TPayload = unknown>(
        def: FsmDefinition<TState, TPayload>,
    ): StateMachine<TState, TPayload>;
}

export { };
