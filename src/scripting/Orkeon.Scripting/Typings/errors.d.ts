// Orkeon Scripting DSL — Errors module
// Loaded first because other modules reference these types.

declare global {
    /** Thrown when the runtime cannot satisfy the version declared by a script. */
    class ScriptVersionMismatchError extends Error {
        readonly declaredVersion: string;
        readonly supportedVersion: string;
    }

    /** Thrown when an agent is added to a crew it already belongs to. */
    class AgentAlreadyInCrewError extends Error {
        readonly agentName: string;
        readonly crewName: string;
    }

    /** Thrown when a context lookup expects an agent currently bound to a crew. */
    class AgentNotInCrewError extends Error {
        readonly agentName: string;
    }

    /** Thrown when an agent is removed from the wrong crew. */
    class AgentNotInThisCrewError extends Error {
        readonly agentName: string;
        readonly crewName: string;
    }

    /** Thrown by `crew.add` when two agents share the same name. */
    class DuplicateAgentNameError extends Error {
        readonly agentName: string;
    }

    /** Thrown when a recursive invocation of the same agent is detected. */
    class RecursiveAgentInvocationError extends Error {
        readonly agentName: string;
    }

    /** Thrown when an awaiter on a queue is interrupted via `queue.kick()`. */
    class WaiterKickedError extends Error { }

    /** Thrown when `queue.pop({ timeout })` expires before a value is available. */
    class ReceiveTimeoutError extends Error {
        readonly timeoutMs: number;
    }

    /** Thrown when state is mutated outside a `state.with(...)` block. */
    class StateMutationOutsideWithError extends Error { }

    /** Thrown when an agent's execution budget is exhausted. */
    class BudgetExhaustedException extends Error {
        readonly dimension: string;
    }

    /** Normalized error code categories surfaced by `onError`. */
    type ErrorCode =
        | "rate_limit"
        | "timeout"
        | "network"
        | "auth"
        | "budget"
        | "validation"
        | "tool"
        | "unknown";
}

export { };
