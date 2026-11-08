# 102. Graph-Based Orchestration (LangGraph-Style)

## What this demonstrates

This example showcases the **LangGraph-style state graph orchestrator** introduced in Orkeon, which provides:

- **Typed state graph**: A `StateGraph<CrewGraphState>` flows typed state through nodes instead of relying on implicit sequential ordering.
- **One node per task**: each task is a node of the graph, named after the task and chained in execution order.
- **Task-level retry**: a failed task runs again from its own `retry:` block (here `collect_data`, 3 attempts with an exponential backoff), the same in every orchestration mode.
- **Circuit breaker protection**: Prevents infinite loops via `CircuitBreakerPolicy` (max transitions, max node visits, max duration).
- **Full observability**: `OnNodeEntered`, `OnNodeCompleted` and `OnCircuitBroken` events fire at each step for logging/telemetry, and `orkeon run --events jsonl` streams a `graph.node.entered` line per node.

## Graph topology

```
START --> task 1 --> task 2 --> ... --> last task --> END
```

## YAML configuration

The key difference from a standard sequential crew is:

```yaml
process: "graph"          # <-- activates GraphProcessStrategy

graphConfig:
  circuitBreakerPreset: "strict"
  # Optional overrides:
  # maxTransitions: 30
  # maxStateVisits: 3
  # maxTotalDurationSeconds: 300
```

A flaky task asks for its own retries (`retry: { attempts: 3, backoffMs: 500, backoff: exponential }` on `collect_data`): the graph itself no longer retries.
All other YAML fields (agents, tasks, dependencies, tools) work identically to `process: "sequential"`.

## When to use Graph vs Sequential

| Criteria | Sequential | Graph |
|----------|-----------|-------|
| Simple linear pipeline | Best choice | Overkill |
| Need automatic retries | Manual handling | Built-in cycles |
| Risk of infinite loops | No protection | Circuit breaker |
| Observability at each step | Basic logging | Node events |
| Conditional branching | Not supported | Conditional edges |

## Running

```bash
dotnet run --project src/apps/Orkeon.ConsoleApp -- --config examples/09-experimental/102-graph-orchestration/config.yaml
```

**LLM profile** — this runner resolves the LLM profile from the nearest `appsettings.json`. Copy one of the ready-made profiles from [`examples/appsettings/`](../../appsettings/README.md) (drop the `.example` suffix, add your key), or pass one explicitly with `--settings`. First run? See [Run your first example](../../../docs/getting-started/run-your-first-example.md).

**Data** — this example does not ship sample data yet; see [docs/reference/example-data-policy.md](../../../docs/reference/example-data-policy.md).
