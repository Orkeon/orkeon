// Orkeon Scripting DSL — Errors module
// Loaded first because other modules reference these types.
//
// Every class below is a real global (GAP-12): `err instanceof ReceiveTimeoutError` holds for
// the error the runtime throws. They used to be declared and never planted, so that check
// compiled and threw a ReferenceError. A host failure with no class here arrives as a plain
// `Error`; every host error, classed or not, carries `clrType` (the .NET exception's type name)
// and `clr` (the exception object itself).

declare global {
    /** What every error the host throws carries, whichever its class. */
    interface HostError extends Error {
        /** The .NET exception type name, e.g. `"ReceiveTimeoutException"`. */
        readonly clrType?: string;
        /** The .NET exception object. */
        readonly clr?: unknown;
    }

    /** Thrown when the runtime cannot satisfy the version declared by a script. */
    class ScriptVersionMismatchError extends Error implements HostError {
        readonly declaredVersion: string;
        readonly supportedVersion: string;
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /** Thrown when an agent is added to a crew while it belongs to another. */
    class AgentAlreadyInCrewError extends Error implements HostError {
        readonly agentName: string;
        readonly crewName: string;
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /** Thrown by a context call (`ctx.send`, `ctx.receive`, `ctx.delegate`…) from an agent no crew holds. */
    class AgentNotInCrewError extends Error implements HostError {
        readonly agentName: string;
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /** Thrown when an agent — or an agent name — is not a member of the crew addressed. */
    class AgentNotInThisCrewError extends Error implements HostError {
        readonly agentName: string;
        readonly crewName: string;
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /** Thrown by `crew.add` when two agents share the same name. */
    class DuplicateAgentNameError extends Error implements HostError {
        readonly agentName: string;
        readonly crewName: string;
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /** Thrown when an agent runs itself (`crew.runAgent`, `ctx.delegate`) or spawns its own name. */
    class RecursiveAgentInvocationError extends Error implements HostError {
        readonly agentName: string;
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /** Thrown when an awaiter on a queue is interrupted via `queue.kick()`. */
    class WaiterKickedError extends Error implements HostError {
        readonly queueName: string;
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /** Thrown when `queue.pop({ timeout })` or `ctx.receive({ timeout })` expires before a value arrives. */
    class ReceiveTimeoutError extends Error implements HostError {
        readonly timeoutMs: number;
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /** Thrown when state is mutated outside a `state.with(...)` block. */
    class StateMutationOutsideWithError extends Error implements HostError {
        readonly propertyName: string;
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /** Thrown when an agent's execution budget is exhausted. */
    class BudgetExhaustedError extends Error implements HostError {
        readonly dimension: "toolCalls" | "delegationDepth" | "wallTime" | "tokens" | "spawnedAgents";
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /**
     * Thrown by `ctx.llm.act` when the agent's `.tools([...])` names a tool the host does not
     * offer — a typo is never run as a loop without that tool. Names match case-insensitively.
     */
    class UnknownToolError extends Error implements HostError {
        /** The agent whose `.tools([...])` names them. */
        readonly agentName: string;
        /** The names no host tool answers to, as written. */
        readonly toolNames: readonly string[];
        /** The tools the host offers, by name. */
        readonly availableTools: readonly string[];
        readonly clrType?: string;
        readonly clr?: unknown;
    }

    /**
     * The code an `onError` handler reads on `err.code` — exactly the codes the runtime's
     * mapper produces (`ErrorCodeMapper`).
     */
    type ErrorCode =
        | "rate_limit"
        | "network"
        | "timeout"
        | "receive_timeout"
        | "state_mutation"
        | "agent_not_in_crew"
        | "validation"
        | "unknown";
}

export { };
