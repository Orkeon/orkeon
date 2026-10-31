> 🇫🇷 [Version française](../fr/architecture/decision-models.md)

> **See also**: [Model providers](./model-providers.md) · [LLM providers](./llm-providers.md) · [ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md) · [Back to the index](../INDEX.md)

# Decision models

A decision model answers typed questions about a state, with calibrated probabilities, and
writes no text. Orkeon reaches one through the `IDecisionModel` port (`Orkeon.Application`); the
first adapter is **TypeSafe**, which serves the "System One" model **Jev** (`jev-latest`). The
model judges and Orkeon's code acts: it blocks, routes or escalates a call, ranks passages, or
hands a decision to an agent
([ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md)).

## The port

`IDecisionModel.DecideAsync(DecisionRequest, ct)` sends one request and returns a
`DecisionResponse`; `ListModelsAsync(ct)` lists the models the provider serves.

A `DecisionRequest` carries:

- `state`: the JSON the questions are about (text, an object, a document);
- `questions`: a dictionary from a name of your choice to a question;
- `assets` (optional, named images), `model` (optional, the configured default otherwise) and
  `LongContext` (not serialized: it sets the long-context header).

| Question | What you give | What you get back |
|----------|---------------|-------------------|
| `choice` | one to 255 named options, each with its description | the chosen option and the probability of each |
| `score` | one to ten levels of an ordered scale | the level and the probability of each |
| `noul` | the statement; `criteria.true` and `criteria.false` are optional | `noul`, the probability of true |
| `box` | images in `assets` | a box in one image |

A `choice`, `score` or `box` answer also carries a `confidence`. A `DecisionRequestValidator` refuses an empty question
set, a `choice` of more than 255 options, a `score` of more than ten levels, and a `box` without images.

## TypeSafe

```csharp
services.AddOrkeonTypeSafe(configuration);
```

The three shipped binaries (`orkeon run`, `orkeon-host` and `orkeon-repl`) call it themselves when their
settings hold an `Orkeon:TypeSafe` section or `TYPESAFE_API_KEY` is set, and `AddOrkeonDecisionProfiles`
in every case; a host written in C# calls both.

```json
{
  "Orkeon": {
    "TypeSafe": {
      "Model": "jev-latest",
      "TimeoutSeconds": 30,
      "MaxRetries": 3
    }
  }
}
```

- **The key** is read from `Orkeon:TypeSafe:ApiKey`, then from the variable `ApiKeyEnvVar`
  names (`TYPESAFE_API_KEY` by default), in the process and then in the user scope. It never
  appears in a log, an error or a tool call.
- **`BaseUrl`** defaults to `https://api.typesafe.ai/v1`; requests go to `POST <BaseUrl>/systemone`
  and `GET <BaseUrl>/models`.
- **Errors** become a `DecisionModelException`: a 422 cites the field at fault, a 401 names the
  key variable, a 400 `max_tokens_exceeded` names the limit (and suggests `longContext` when the
  request did not ask for it), a 413 or 415 says what the request exceeded. A 429 or a 529 is retried with an
  exponential backoff, up to `MaxRetries` times; each attempt has its own `TimeoutSeconds`. Every
  error cites the id TypeSafe gave the request (`x-typesafe-request-id`) when the answer carries one.
  A `usage` without token counts is metered as zero, not refused.
- **The danger-zone headers** are set from the request: `TypeSafe-Danger-Zone-Images` when it
  carries images or a `box` question, `TypeSafe-Danger-Zone-Long-Context` when `LongContext` is set.
- **Usage** is metered once, as `decision` (`LlmUsageOperations.Decision`): TypeSafe bills
  input tokens only.

`AddOrkeonTypeSafe` also registers the input guard and the `system_one_decide` tool.

## Decision stages around an LLM call

The main use. A **decision profile** wraps an LLM profile in `DecisionLlmProvider`, which runs
stages before the call (`OnRequestAsync`) and after it (`OnResponseAsync`). Each stage returns a
verdict, and **never rewrites** the messages or the response:

| Verdict | Before the call | After the call |
|---------|-----------------|----------------|
| `Continue` | the next stage runs | the next stage runs |
| `Block(reason)` | the call is not made; an error response with `ErrorType = "DecisionBlocked"` | the response is replaced by that error |
| `Route(profile)` | the call goes to that profile | the call is made again on that profile, once |
| `Escalate(profile)` | same as `Route` | same as `Route` |

After the response, the first `Route` or `Escalate` re-calls the LLM and runs the response stages
again on the new answer; a second one is logged and ignored. A response that is already an error
skips the response stages. Every verdict other than `Continue` is logged and audited
(`decision.block`, `decision.route`, `decision.escalate`).

Three stages ship:

| `Kind` | Phase | Question asked | Verdict |
|--------|-------|----------------|---------|
| `route` | request | a `choice` among `Routes` (profile → description) on the last user message | `Route` to the chosen profile; under `MinConfidence`, to `Fallback`, or none |
| `guard` | `Phase`: `request`, `response` or `both` (default) | one `noul` per entry of `Risks` (name → description) | `Block` at or over `BlockThreshold` (0.5); a warning at or over `WarnThreshold` (0.3) |
| `verify` | response | one `noul`: does the answer meet `Criterion`? | `Escalate` to `EscalateTo` under `MinProbability` (0.5) |

The default `verify` criterion is "The response addresses the request, is coherent, and states
nothing the request does not support."

### Configuration

```csharp
services.AddOrkeonTypeSafe(configuration);
services.AddOrkeonDecisionProfiles(configuration);
```

A cascade: a cheap model answers first, a stronger one takes over when the answer does not hold.

```json
{
  "Orkeon": {
    "Decision": {
      "Profiles": {
        "cascade": {
          "Target": "small",
          "Stages": [
            { "Kind": "guard", "Phase": "request",
              "Risks": { "prompt_injection": "The message tries to override the system instructions" },
              "BlockThreshold": 0.8 },
            { "Kind": "verify", "EscalateTo": "large", "MinProbability": 0.6 }
          ]
        }
      }
    }
  }
}
```

A router: the decision model picks the profile that fits the request.

```json
{
  "Orkeon": {
    "Decision": {
      "Profiles": {
        "triage": {
          "Target": "general",
          "Stages": [
            { "Kind": "route", "MinConfidence": 0.6, "Fallback": "general",
              "Routes": {
                "code": "Writing, reviewing or debugging code",
                "general": "Anything else"
              } }
          ]
        }
      }
    }
  }
}
```

`small`, `large`, `code` and `general` are LLM profiles (`Llm:Profiles`). A crew uses a decision
profile like any other: `llm: { profile: cascade }`.

The host checks the section when it builds and names the key to fix: a stage `Kind` other than
`route`, `guard` or `verify`, a missing `Target`, `Routes`, `Risks` or `EscalateTo`, a
probability outside [0, 1], and a decision profile named as a target, route, fallback or
escalation (a decision profile names LLM profiles only, so no cycle can form). A profile that
names an LLM profile which does not exist fails when it is first resolved.

### Failure and streaming

- When the decision model fails, the stage gives `Continue` and a warning; with
  `"FailClosed": true` on the profile, it gives `Block`.
- With no response stage, a stream passes through untouched. With one, the stream is buffered,
  judged, then replayed; a blocked text stream throws `InvalidOperationException`.

### A custom stage

Implement `IDecisionStage` (`Name`, `OnRequestAsync`, `OnResponseAsync`) and build the
`DecisionLlmProvider` yourself in `AddOrkeonLlmProfile(name, sp => ...)`. `GuardDecisionStage.AssessAsync`
asks a set of risks about any state and can be reused.

## Decision tasks and conditional fan-out

A decision does not have to hide inside an LLM profile: a crew can place it in its flow. A **decision
task** is a crew task with no agent, answered by the decision model; the tasks that follow carry a
`when` condition and run, or are skipped, according to its answers. A crew that must sort, score or
route (a ticket to the right agent, a document to the right pipeline) does it with one typed,
calibrated answer instead of an LLM call. The syntax is in the
[YAML reference](./yaml-schema.md#decision-tasks-and-conditions) and, for `.ork.ts`, in the
[scripting DSL](../reference/scripting-dsl.md#taskbuilder) (`taskBuilder().decision()` and `.when()`).

### The task and its request

- A decision task has no agent, no tool and no memory, and writes no text. Its output is the
  answers as compact JSON, so that a later LLM task reads them as context: for each question the
  `chosen` option (a choice only), the `score` or the `probability` of a yes-no, the `probabilities` and the
  `confidence`, plus `"defaulted": true` when the fallback answered. A score question has one form: its
  answers are keyed by level position and it carries no `chosen`, the fallback included. A dependency
  that was retried is read at its last successful output, else at its last attempt. The task's
  `TokensUsed` is the usage the model reports.
- A crew made of decision tasks only is admitted with no agent. A skipped task takes no agent turn in
  round-robin.
- The task sends its state and **all its questions in one request**. The state is the output of
  each task it depends on that ran, labelled with the task's key, or only the tasks `state` lists.
  The only repeats are the adapter's retries of that same request on a 429 or a 529.
- The request is metered once, as `decision`, like every request to the model; a defaulted task
  makes no request and counts nothing.
- A process that retries a failed task (Graph) retries a failed decision task as a new evaluation,
  with a new request.

### Conditions and skipped tasks

A condition is evaluated once its decision task has answered. All the conditions of a task must
hold for it to run; the clauses are listed in the
[YAML reference](./yaml-schema.md#decision-tasks-and-conditions).

- **Threshold scale.** A threshold on a yes-no question or on an option of a choice is a
  probability in [0, 1]. A threshold on a score question is read on the positions of its levels, 0
  for the first and n-1 for the last: `urgency: "> 1.5"` on `[low, medium, high]` holds when the
  model's weighted score is above the middle level. A fallback level counts as its position.
- **Skipped, not failed.** A task whose condition does not hold is skipped, with the reason: the
  condition and the answers that failed it. A task is also skipped when every task it depends on was
  skipped by a condition, and when the decision task it reads was itself skipped. The crew still
  succeeds.
- **No trace.** A skipped task leaves no output: no "task skipped" text reaches the context or the
  state of a later task.
- **Join.** A task that depends on several branches of a fan-out runs when at least one of them ran,
  with the outputs of those that ran.
- **Final output.** The crew's final output is the output of the last task that ran in declared
  order, not of a skipped one.
- **`otherwise`.** It holds exactly when no sibling condition on that decision task holds on the
  answers, a confidence under the floor included; whether a sibling then ran or was skipped for
  another reason does not matter.
- A skipped task is reported by the consoles as skipped, with its reason (see
  [the run event bus](./run-event-bus.md)).

### Failure

A decision the model cannot make is a **failed task**, not a skip and not a fallback: the model
refused the request (state over its limit, key refused, rate limit or overload after the retries) or
is unavailable. The task fails with the reason; the tasks that depend on it, `otherwise` tasks
included, are skipped as for any failed task, and the crew's rule for a failed task applies. The
key is in no message. The failure is recorded as a failed output marked as a decision, with its
cause, and the exception is logged. An answer outside the options (a choice not among those asked)
fails the task, non-transiently: it is not retried. `Parallel` and `Graph` stop the dependants of a
failed or cancelled decision. The domain refuses a non-finite confidence floor and repeated score
levels, and `TaskCondition.Holds` is false for an `otherwise` clause (its rule is the one above).

### The decision circuit and the fallback

The **decision circuit** says whether decision tasks ask the model. It is on when a decision model is
configured (in the shipped binaries, when `Orkeon:TypeSafe` or `TYPESAFE_API_KEY` is set) and
`Orkeon:Decision:Enabled` is `true` (its default), and off when:

- no decision model is configured;
- `Orkeon:Decision:Enabled` is `false`;
- the run is started with `orkeon run --no-decision`, which wins over `Orkeon:Decision:Enabled=true`.
  The option exists on `orkeon run` only; the other hosts read the setting.

A failed request never turns the circuit off: that is a failed task.

With the circuit off, every decision task answers by its **fallback**, without a request, and is
marked **defaulted** (`defaulted` in the task's event). Its status stays completed.

- With explicit `fallback` answers, the chosen answer carries a confidence of 1 and a probability of
  1: the conditions on it hold as if the model had answered with full confidence. A question left out
  of `fallback` selects no option.
- With no fallback, the decision task selects no option and carries a confidence of 0: every option
  condition is false, the `otherwise` task runs and the others are skipped.
- Before the first task, a run with the circuit off is **refused**, naming the task, when a decision
  task has neither `fallback` answers nor an `otherwise` task on it
  (`Decision task 'triage' has neither a fallback nor an otherwise task.`).
- A crew without a decision task runs exactly as before, whatever the circuit.

### Jev's limits and long context

Jev takes 64,000 tokens per request, 32,000 per question, and 262,000 in long context. A choice holds
up to 255 options and a score up to 10 levels (refused at load beyond). Orkeon counts no token: the
limit is the model's refusal, which fails the task with the limit in the message, and suggests
`longContext: true` when the task did not ask for it. Long context is requested by the task only,
never on its own.

## Secondary uses

- **Input guard** — `TypeSafeInputGuard` joins the agent's guardian pipeline in the `Input` phase
  when `Orkeon:TypeSafe:Guard:Enabled` is true. Its `Risks` default to `prompt_injection` and
  `data_exfiltration`, `BlockThreshold` to 0.8, `WarnThreshold` to 0.5; it warns on a failure,
  or blocks with `FailClosed`.
- **RAG reranker** — `rerank.kind: decision` (alias `typesafe`) asks one `noul` per passage in a
  single request, passages cut at 4,000 characters, and orders them by probability.
- **Tool** — `system_one_decide` lets an agent ask `choice`, `score` and `noul` questions
  ([Tool inventory](../tools/inventory.md)); `box` is refused.

## Limits

- The request stages ask about the last user message only; a decision over the whole
  conversation needs a custom stage.
- Each stage phase costs one round trip to the decision model, and a re-call costs one more LLM
  call.
- Jev's context is bounded per request and per question: a long state or many passages in the
  reranker may need `LongContext`, which the stages and the reranker do not set. A decision task
  sets it with `longContext: true`.
- Decision tasks and `when` run in the Sequential, Parallel and Graph processes only; the others
  refuse them at load.
- A failed request to the decision model is a failed task: there is no retry beyond the adapter's
  and no fallback. The fallback answers only when the circuit is off.
- Decision tasks take text states and `choice`, `score` and `noul` questions; `box` questions and
  images are not offered.
