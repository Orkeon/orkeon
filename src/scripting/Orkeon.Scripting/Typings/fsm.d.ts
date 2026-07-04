// Orkeon Scripting DSL — Finite state machine literal
// See chapter 07 (orchestration-fsm-graph.md).

declare global {
    interface CircuitBreakerOptions {
        maxFailures?: number;
        resetAfterMs?: number;
        maxConsecutive?: number;
        windowMs?: number;
    }

    interface FsmTransition<TState, TEvent extends string, TContext = unknown> {
        from: TState | readonly TState[];
        on: TEvent;
        to: TState;
        guard?: (ctx: TContext) => boolean;
        action?: (ctx: TContext) => Promise<void> | void;
    }

    interface FsmDefinition<TState, TEvent extends string, TContext = unknown> {
        name: string;
        initial: TState;
        states: Record<string, unknown>;
        transitions: readonly FsmTransition<TState, TEvent, TContext>[];
        circuitBreaker?: CircuitBreakerOptions;
        onEnter?: Partial<Record<string, (ctx: TContext) => Promise<void> | void>>;
        onExit?: Partial<Record<string, (ctx: TContext) => Promise<void> | void>>;
    }

    interface StateMachine<TState, TEvent extends string, TContext = unknown> {
        readonly name: string;
        readonly current: TState;
        send(event: TEvent, payload?: unknown): Promise<void>;
        run(ctx: TContext): Promise<TState>;
    }

    function stateMachine<TState, TEvent extends string, TContext = unknown>(
        def: FsmDefinition<TState, TEvent, TContext>,
    ): StateMachine<TState, TEvent, TContext>;
}

export { };
