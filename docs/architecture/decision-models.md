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
| `choice` | at least two named options, each with its description | the chosen option and the probability of each |
| `score` | at least two levels of an ordered scale | the level and the probability of each |
| `noul` | `criteria.true` and `criteria.false` | `noul`, the probability of true |
| `box` | images in `assets` | a box in one image |

A `choice`, `score` or `box` answer also carries a `confidence`. A `DecisionRequestValidator` refuses an empty question
set, a `choice` or `score` with fewer than two criteria, and a `box` without images.

## TypeSafe

```csharp
services.AddOrkeonTypeSafe(configuration);
```

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
  key variable, a 413 or 415 says what the request exceeded. A 429 or a 529 is retried with an
  exponential backoff, up to `MaxRetries` times; each attempt has its own `TimeoutSeconds`.
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
  reranker may need `LongContext`, which the stages and the reranker do not set.
