> 🇫🇷 [Version française](../fr/adr/ADR-016-decision-tasks-and-conditional-fan-out.md)

> **See also**: [Decision models](../architecture/decision-models.md) · [YAML schema](../architecture/yaml-schema.md) · [Run event bus](../architecture/run-event-bus.md) · [ADR-015](./ADR-015-model-provider-categories-and-decision-stages.md) · [Back to the index](../INDEX.md)

# ADR-016 — Decision tasks, and a `when` condition that skips instead of failing

**Status**: Accepted · **Date**: 2026-10-28
· **Scope**: `Orkeon.Domain` (`TaskDecision`, `DecisionOutcome`, `CrewTaskBase`), `Orkeon.Infrastructure` (`Crew/Strategies/`: Sequential, Parallel and Graph, `DecisionPreflight`), `Orkeon.Application` (`TaskExecutionSnapshot`), the `orkeon run` CLI (`--no-decision`, the `task.completed` event) and Studio (`RunProgressModel`)

## Context

[ADR-015](./ADR-015-model-provider-categories-and-decision-stages.md) put the decision model
behind its own port and used it around one LLM call. A crew is a flow of tasks, and the same
model can route that flow: read a ticket, decide its category, and run only the task that fits.
Until then a crew could not branch on a model's judgement without an agent writing a text that
the next task had to parse.

## Decision

1. **A decision task** is a task that carries a `decision` block instead of an agent. It sends its
   typed questions to the decision model in one request and its output is the answers, never
   text. A task has an agent or a decision, never both.
2. **A `when` condition** on a later task names a decision task and the answers it must have
   given: an option (read under a confidence floor, 0.5 by default) or a threshold on a
   probability or a score. `otherwise` holds when no sibling condition on the same decision held.
3. **A false condition skips, it never fails.** The task is skipped with the reason recorded
   (`skipReason`), a task whose dependencies were all skipped is skipped too, and a skipped task
   leaves nothing in the context of a later one. It counts as neither success nor failure. A
   decision the model cannot make is a failed task and follows the crew's existing rule.
4. **Sequential, Parallel and Graph** run decision tasks. Hierarchical, Consensual and Autonomous
   do not run a task on the task it names: a crew that uses a decision task or a `when` with one
   of them is refused on load.
5. **The circuit can be off.** When no decision model is configured, or with `orkeon run
   --no-decision`, a decision task makes no request and answers from its `fallback`; its
   `task.completed` event carries `defaulted: true` and the conditions evaluate on that answer. A
   failing request is never turned into the fallback.
6. **The run reports it.** `task.completed` carries `skipReason`, `defaulted`, `isDecision` and
   `decision` (the answers as compact JSON); `TaskExecutionSnapshot` carries `SkipReason`,
   `Defaulted`, `IsDecision` and `DecisionAnswers`; the consoles and Studio's `RunProgressModel`
   show them. `TaskCompletedEvent.AgentId` is nullable, a decision task having no agent.

## Consequences

- A decision task costs one request per evaluation; the only repeats are the adapter's retries on
  throttling, and a process that retries a failed task (Graph) evaluates the decision again.
- A crew without a decision task runs as before, whatever the circuit.
- A client of the event bus must read `skipped` and `skipReason` together: a skipped task is
  neither a failure nor an output.

## Later steps

The richer model of the interview is deferred for simplicity (spec § 10): decision nodes before or
after a task (guards on its input or final answer), go-to outcomes, loops, a decision journal and
chains with "last answer" defaults; fixes to guarded profiles; reranker batching and doctor
surfaces; box questions and images. A retry or fallback when a request fails is not planned: the
fallback exists only for a circuit that is off.

## Alternatives considered

- **An agent that classifies and writes the route**: rejected. It spends an LLM call on a
  judgement and returns text the next task has to parse.
- **A condition that fails the task when false**: rejected. A branch not taken is the expected
  case, and a failure would trigger the crew's retries and error handling.
