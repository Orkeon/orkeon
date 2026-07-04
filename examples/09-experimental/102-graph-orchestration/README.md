# 102. Graph-Based Orchestration (LangGraph-Style)

## What this demonstrates

This example showcases the **LangGraph-style state graph orchestrator** introduced in Orkeon, which provides:

- **Typed state graph**: A `StateGraph<CrewGraphState>` flows typed state through nodes instead of relying on implicit sequential ordering.
- **Conditional routing**: The `route` node inspects accumulated state to decide whether to continue, retry, or finish.
- **Controlled retry cycles**: Failed tasks are automatically re-enqueued up to `maxRetryCycles` times.
- **Circuit breaker protection**: Prevents infinite loops via `CircuitBreakerPolicy` (max transitions, max node visits, max duration).
- **Full observability**: `OnNodeCompleted` and `OnCircuitBroken` events fire at each step for logging/telemetry.

## Graph topology

```
START --> execute_task --> route --+--> execute_task  (if pending/retry)
                                  |
                                  +--> END           (if all done)
```

## YAML configuration

The key difference from a standard sequential crew is:

```yaml
process: "graph"          # <-- activates GraphProcessStrategy

graphConfig:
  maxRetryCycles: 2       # retry failed tasks up to 2x
  circuitBreakerPreset: "strict"
  # Optional overrides:
  # maxTransitions: 30
  # maxStateVisits: 3
  # maxTotalDurationSeconds: 300
```

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
