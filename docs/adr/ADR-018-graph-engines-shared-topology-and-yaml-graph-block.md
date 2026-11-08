> 🇫🇷 [Version française](../fr/adr/ADR-018-graph-engines-shared-topology-and-yaml-graph-block.md)

> **See also**: [Graph orchestration](../orchestration/graph.md) · [YAML schema](../architecture/yaml-schema.md) · [Scripting DSL](../reference/scripting-dsl.md) · [ADR-016](./ADR-016-decision-tasks-and-conditional-fan-out.md) · [Back to the index](../INDEX.md)

# ADR-018 — Two graph engines, one topology validation and one budget vocabulary, and a YAML `graph:` block

**Status**: Accepted · **Date**: 2026-11-07
· **Scope**: `Orkeon.Domain` (`Graph/StateGraph.cs`, `GraphRunner.cs`, `Configuration/GraphConfig.cs`), `Orkeon.Infrastructure` (`Crew/Strategies/GraphProcessStrategy.cs`, `Configuration/Yaml/`), `Orkeon.Scripting` (`Orchestration/JsStateGraph.cs`, `Bindings/StateGraphBinding.cs`, `Typings/graph.d.ts`). This ADR fixes semantics and ships no code; the work that implements it is FWK-27 (validation, implicit graph, `JsStateGraph`) and FWK-28 (the YAML block, export, Studio).

## Context

Two graph loops exist.

1. **The Domain engine** (`StateGraph<TState>` compiled into a `GraphRunner<TState>`) has the sentinels `__start__` and `__end__`, fixed and conditional edges, and cycles bounded by a `CircuitBreakerPolicy`: `MaxTransitions` (a node runs only while the count of nodes already run is under it), `MaxStateVisits` (checked per node, a trip when its visits exceed it) and `MaxTotalDuration`. `AddEdge` and `AddConditionalEdge` refuse a second outgoing edge from a node. `Compile()` checks three things only: START has an edge, every node has an outgoing edge, and the target of a fixed edge exists. It does not detect a node that nothing reaches or a graph with no path to END, and a conditional edge is checked at run time against its declared targets.
2. **`JsStateGraph`** runs its own loop in JavaScript (a trampoline) over an arbitrary `JsValue` state, with the sentinels `__START__` and `__END__`. Its edge map is keyed by source node, so one edge per node holds by construction. `stateGraph(...)` rejects a literal with no START edge, a string edge to an undeclared node, and a graph with no static path from START to END (a function edge is assumed to reach END). Its budgets are `maxTransitions`, `maxStateVisits` and `maxRetryCycles`: the last one counts, over the whole run, the entries into a node already entered once, not the visits of one node.
3. **The Graph process does not use the engine's shape.** `GraphProcessStrategy.BuildCrewGraph` builds `execute_task` → `route` → (`execute_task` or END): one node that loops over a queue of tasks. A crew's YAML has no `graph:` block, only `graphConfig:` (`maxRetryCycles`, `circuitBreakerPreset`, `maxTransitions`, `maxStateVisits`, `maxTotalDurationSeconds`), and its `maxRetryCycles` is a retry of a failed task, not a bound on cycles.
4. **A single engine is not possible.** SCR-25 T1 removed the CLR loop that awaited JavaScript functions: Jint has one drainer, and a CLR loop blocked after the first `await` until its 10 s timeout. The JavaScript loop must stay in JavaScript.

## Decision

1. **Two loops stay.** The Domain engine and the JavaScript loop are not merged. They share one **topology validation** (`GraphTopologyValidator`, in `Orkeon.Domain`, FWK-27), one **vocabulary** (point 2) and one **budget meaning** (point 4).
2. **Vocabulary.** A graph has nodes (an id and a body), a start node, edges (`from`, `to`) and the exit `END`. The Domain's and the script's sentinel strings differ (`__start__`/`__end__`, `__START__`/`__END__`) and stay as they are; they are not a surface of the YAML, where the start node is named by `start:` and the exit is the reserved word `END`. `START` and `END` are refused as node ids.
3. **The validator** takes a neutral description (node ids, start, edges as `from` to targets, each flagged fixed or conditional) and returns every fault, not the first. It checks:
   1. a node id is unique, not empty and not `START`/`END`;
   2. `start` names a node;
   3. an edge starts at a declared node and ends at a declared node or `END` (an unknown id is named, with the ids that exist);
   4. a node has an outgoing edge: nothing may be left hanging at run time;
   5. a node that START cannot reach by following every edge is an orphan;
   6. END is reachable from START, and every node reachable from START can reach END (a conditional edge counts for all its declared targets; a script's function edge, whose targets are unknown, is assumed to reach END, as `stateGraph` does today; a Domain conditional edge declared without `possibleTargets` is treated the same way). The texts `has no path from START to END` and `ghost` node faults of the present engines are kept, so their tests hold;
   7. a node has at most one fixed outgoing edge, and a fixed edge excludes any other from the same node. Edges on one node that carry conditions are one conditional edge (point 6).

   The Domain engine calls it from `Compile()` (its three present checks are part of this list), the YAML mapper calls it at load, `JsStateGraph` calls it at construction (it replaces `ValidateEdgeTargets` and `ValidateReachability`), and the export calls it before writing. A fault surfaces as the exception of the caller's layer (`InvalidOperationException` for the Domain, the load error of `CrewDefinitionValidator` for the YAML, `InvalidScriptException` for a script) with the same text.
4. **Budgets: those of the Domain.** `maxTransitions` and `maxVisits` (the Domain's `MaxStateVisits`) are the two bounds; `maxTotalDurationSeconds` and `circuitBreakerPreset` stay. The two engines count alike: a run executes at most `maxTransitions` nodes, and a node entered more than `maxVisits` times trips the breaker. In a script, `maxStateVisits` is already that budget. `maxRetryCycles` is documented as the script's run-wide count of revisits: it equals `maxVisits - 1` for a graph that loops on one node and is a different, global bound for several. It stays in `graph.d.ts`, unchanged, and a parity test pins the one-node case on both engines. The defaults differ today and stay distinct: `StateGraph` alone takes the Strict policy (50 transitions, 5 visits), a bare `CircuitBreakerPolicy` is 100 and 10, a script is 200 transitions with no visit bound unless a preset is named. For a crew, the defaults of `graph.maxVisits` and `graph.maxTransitions` keep the rule of `GraphProcessStrategy.ResolvePolicy`, sized from the crew: visits = the sum over the nodes of 1 + the retries (`attempts - 1`) of the node's task, that is nodes × (1 + retry attempts) when the tasks retry alike, transitions = 2 × visits + 1; an explicit value wins. A crew of more than 49 tasks is therefore not capped by the Strict figures. FWK-27 documents each script default as it is.
5. **The YAML `graph:` block.** With `process: graph`:

   ```yaml
   process: graph
   graph:
     start: triage              # optional: default the first node
     nodes:                     # optional: absent = the implicit graph (point 7)
       - { id: triage,   task: triage }
       - { id: fix,      task: fix }
       - { id: escalate, task: escalate }
     edges:
       - { from: triage, to: fix,      when: { category: [bug] } }
       - { from: triage, to: escalate }          # no `when`: the "otherwise"
       - { from: fix,      to: END }
       - { from: escalate, to: END }
     maxTransitions: 50
     maxVisits: 5
     circuitBreakerPreset: strict
     maxTotalDurationSeconds: 600
   ```

   `graph:` replaces the YAML `graphConfig:` (removed, `[breaking]`: its budget keys move into `graph:`, `maxStateVisits` becoming `maxVisits`, `maxRetryCycles` disappearing). The `graphConfig` key of the script's `stateGraph({...})` literal stays. A `graph:` outside `process: graph` is refused at load, not read and ignored. A node is `{id, task}`: `task` names a task of the crew. A task is named by at most one node, and when `nodes` is given a task named by no node is refused at load. A node is revisited by an edge leading back to it; on a revisit the latest output and the latest decision outcome win, the run's state and snapshots being keyed by task id. A failed node stops the run: there is no routing on failure (a retry is the task's `retry:`). No `maxCycles` key exists: a bound on cycles is `maxVisits`, and a retry of a task is the task's `retry:` (FWK-15).
6. **Edges.** Several `edges` from one `from` become one `AddConditionalEdge`, evaluated in the order declared; the first edge whose `when` holds wins. A `from` that carries `when` edges is a decision task (ADR-016) and has exactly one edge without `when`, the "otherwise", taken when no `when` holds, which is also the case when the decision task was defaulted. An ordinary task has exactly one edge. The `when` of an edge uses the grammar of the task-level `when`, keeping its `<task>.` prefix: `<task>.<question>: [option, ...]`, `<task>.<question>: ">= 0.7"`, `<task>.<question>.<option>: ">= 0.25"`. The task must be the `from` task, and the questions are checked at load against the ones it declares; `otherwise` is not written, an edge without `when` is it. These are refused at load, each with a message naming the node: a `when` on an edge whose `from` is not a decision task, a decision `from` with no edge without `when`, two edges without `when` from one node, an ordinary task with several edges, and a task-level `when:` or `dependencies:` in a crew that has nodes (the edges order the tasks, a second router would contradict them).
7. **The implicit graph.** Without a `graph:` block, or with a `graph:` that holds only budgets, `BuildCrewGraph` builds one node per task, its id the task name, chained in the order the crew executes its tasks today, the last one leading to END. The queue-and-route loop of `execute_task` goes; a retry of a failed task is the task's `retry:`. The task-level `when` of ADR-016 keeps its skip semantics in the implicit graph only. `edges` without `nodes` means that same implicit node per task, with the edges between them.
8. **Which engine runs what.** The Domain engine runs every crew in the Graph process, with a `graph:` block or without. `JsStateGraph` runs `stateGraph(...)` in a `.ork.ts` script, over the script's state. A script cannot hand a graph to the Domain engine and a crew cannot run on `JsStateGraph`: the two meet at the validator, the vocabulary and the budgets.
9. **Run state and display.** The current node and the visit counts of the Graph process are part of the checkpoint (FWK-07), so a resumed run re-enters the node it stopped at; the serialization is fixed by FWK-07 and used by FWK-28. A node entered emits a `graph.node.entered` event from both engines (field `node`, plus `task` and `visit` for the Domain engine); Studio draws the graph read-only from these events and has no editor. The YAML export writes the `graph:` block back, so that import, export and import give the same graph.
10. **Out of this ADR: handoff.** An agent handing a task to another agent by name is not a graph edge; it is neither specified nor refused here.

## Consequences

- A crew can describe a loop (a reviewer sending work back to a writer) and a branch on a decision, with the engine's budgets as the only bound, and an invalid topology fails at load, not in the middle of a run.
- Two engines keep being maintained; what they have in common is tested, not assumed: the validator is the single code path for topology faults and a parity test pins the budgets.
- `graphConfig:` and the crew-level `maxRetryCycles` disappear: a crew that wrote them is refused at load and moves its keys (`[breaking]`, FWK-15 and FWK-28). FWK-28 migrates the example `09-experimental/102-graph-orchestration` (config and README) and the tests `CrewFactoryGraphConfigTests`, `RemovedCircuitBreakerYamlTests` and `UnknownCrewYamlKeysTests`.
- A crew in the Graph process with no `graph:` block runs one node per task, so its trace and events name the tasks, not `execute_task`.

## Alternatives considered

- **One engine, the Domain's, running scripts**: rejected. It recreates the CLR loop that awaited JavaScript functions, which SCR-25 T1 removed because Jint has a single drainer.
- **One engine, the script's, running crews**: rejected. A crew's state is typed and its tasks are awaited CLR calls; every task would sit behind a trampoline.
- **A `maxCycles` key**: rejected. It would be read as a retry bound next to the task's `retry:`; `maxVisits` bounds cycles and `retry:` bounds attempts.
- **A `maxRetryCycles` kept in `graph:`**: rejected for the same confusion, and because it is a run-wide count, not a per-node bound.
- **Edges evaluated as a script or an expression**: rejected. A `when` reads the answers of a decision task, as in ADR-016, and nothing else.
