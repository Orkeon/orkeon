> 🇫🇷 [Version française](../fr/adr/ADR-016-decision-tasks-and-conditional-fan-out.md)

> **See also**: [Decision models](../architecture/decision-models.md) · [YAML schema](../architecture/yaml-schema.md) · [Run event bus](../architecture/run-event-bus.md) · [ADR-015](./ADR-015-model-provider-categories-and-decision-stages.md) · [Back to the index](../INDEX.md)

# ADR-016 — Decision tasks, and a `when` condition that skips instead of failing

**Status**: Accepted · **Date**: 2026-10-28
· **Scope**: `Orkeon.Domain` (`TaskDecision`, `DecisionOutcome`, `CrewTaskBase`), `Orkeon.Infrastructure` (`Crew/Strategies/`: Sequential, Parallel and Graph, `DecisionPreflight`), `Orkeon.Application` (`TaskExecutionSnapshot`), the `orkeon run` CLI (`--no-decision`, the `task.completed` event)

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
   of them is refused on load, and a crew built in C# that reaches them fails before its first task.
5. **The circuit can be off.** When no decision model is configured (the shipped binaries register
   the active decision provider, named or deduced), when `Orkeon:Decision:Enabled` is
   `false`, or with `orkeon run --no-decision`, a decision task makes no request and answers from its `fallback`; its
   `task.completed` event carries `defaulted: true` and the conditions evaluate on that answer. A
   failing request is never turned into the fallback.
6. **The run reports it.** `task.completed` carries `skipReason`, `defaulted`, `isDecision` and
   `decision` (the answers as compact JSON); `TaskExecutionSnapshot` carries `SkipReason`,
   `Defaulted`, `IsDecision` and `DecisionAnswers`; Orkeon Studio showed them at the time (it now
   lives in its own repository), and no Orkeon console shows them. `TaskCompletedEvent.AgentId` is
   nullable, a decision task having no agent.

   > Amendment 2026-10-31: the reason of a jump is read by the run's JSONL (and was by Studio at the time), not by "the consoles"; the question name `defaulted` is reserved.

   > Amendment 2026-10-31: the decision model behind a decision task may be TypeSafe, Liquid or Kev; the task, its circuit and its fallback do not change.

   > Amendment 2026-11-20 (#433), on point 5: the circuit covers every consumer of the decision model, not the decision tasks alone. Off, whatever the cause (the switch, the setting, no provider), the run makes no `IDecisionModel` call: `system_one_decide` is withheld, a decision profile takes no decision (its `route` falls back, its `verify` never escalates, its `guard` lets the call on unless the profile is fail-closed), the LLM judge refuses to start when fail-closed and is left out when fail-open, and the `decision` reranker keeps the retrieval order. Every consumer reads the circuit through `DecisionCircuit.Of` or the scope's override (`DecisionCircuitOverride`, entered for the run as `DecisionCircuitScope`), which names the cause (`DecisionCircuitCause`), and says it in one warning. See [Decision models](../architecture/decision-models.md#every-consumer-follows-the-circuit).

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

## Amendment — a crew's decision mode (#429)

With the circuit off a decision task answers from its fallback, which suits a crew that can run without System One
and misleads one that cannot. A crew now declares which it is, in a crew-level `decision: { mode }`: `none` (it
never asks System One), `optional` (each decision task names a `without:` relay, an agent of the crew that answers
with the circuit off, in place of a fallback) or `required` (a run with the circuit off is refused before its first
task, exit `69`, code `decision_unavailable`). A crew with no mode keeps the fallback semantics above. The rules live
in `DecisionTaskRules` beside the existing ones, so YAML, `.ork.ts` and C# crews meet the same checks (a C# crew's
relay names an agent by its role, and its decision profiles are checked only by the crew factory, which knows the
host's profiles); the refusal
sits in the orchestrator, before memory and planning, so every process and every host enforces it. The relay itself
is declared and checked at load here and run by a later change (#430). See
[Decision models](../architecture/decision-models.md#a-crews-decision-mode).

## Amendment — the `without:` relay (#430)

This amends §5: with the circuit off, a decision task of an `optional` crew is not answered by a fallback but by its
`without:` relay. The relay makes one LLM call on the relay agent's provider, with no tool and no history, and the
agent reports probabilities only, in a JSON object whose schema is generated from the questions. The engine checks
them as it checks the model's answer (every question, values in [0, 1], distributions summing to 1 within 0.05),
asks once more when they fail, and derives `chosen`, the score and the confidence by the same rules as the model
path; a second refusal fails the task, and no fallback is used. `task.completed` says which answered
(`decisionSource: model | relay | fallback`), the checkpoint records it so that every process replays a decision on
resume, and an `optional` crew resumed under the other circuit is refused (code `decision_circuit_mismatch`, exit `1`). See
[Decision models](../architecture/decision-models.md#the-without-relay).

## Alternatives considered

- **An agent that classifies and writes the route**: rejected. It spends an LLM call on a
  judgement and returns text the next task has to parse. The `without:` relay (#430) is not that agent: it is
  an LLM call constrained by a schema to probabilities, checked like the model's answer and read by the same
  conditions, and it is only a second mode an `optional` crew declares for a circuit that is off. Nothing parses
  a route out of its text.
- **A condition that fails the task when false**: rejected. A branch not taken is the expected
  case, and a failure would trigger the crew's retries and error handling.
