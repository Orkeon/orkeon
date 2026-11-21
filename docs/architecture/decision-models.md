> 🇫🇷 [Version française](../fr/architecture/decision-models.md)

> **See also**: [Model providers](./model-providers.md) · [LLM providers](./llm-providers.md) · [ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md) · [Back to the index](../INDEX.md)

# Decision models

A decision model answers typed questions about a state, with calibrated probabilities, and
writes no text. Orkeon reaches one through the `IDecisionModel` port (`Orkeon.Application`); the
adapter serves three decision providers on the "System One" wire: **TypeSafe** (Jev), **Liquid** (d1) and **Kev**. The
model judges and Orkeon's code acts: it blocks, routes or escalates a call, ranks passages, or
hands a decision to an agent
([ADR-015](../adr/ADR-015-model-provider-categories-and-decision-stages.md)).

## The port

`IDecisionModel.DecideAsync(DecisionRequest, ct)` sends one request and returns a
`DecisionResponse`.

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

## Decision providers

Three providers serve the same "System One" wire: **TypeSafe** (model Jev), **Liquid** (model d1)
and **Kev**. One client, `SystemOneDecisionModel`, serves them through three descriptors; the
request is `POST <BaseUrl>/systemone` with `model`, `state` and `questions`, the answer carries
`answers` and `usage`.

| Provider | Default address | Default model | Key variable | Section |
|----------|-----------------|---------------|--------------|---------|
| `typesafe` | `https://api.typesafe.ai/v1` | `jev-latest` | `TYPESAFE_API_KEY`, required | `Orkeon:TypeSafe` |
| `liquid` | `https://api.liquid.ai/decisions/v1` | `d1.1` (or `d1-ultra`) | `LIQUID_API_KEY`, required | `Orkeon:Liquid` |
| `kev` | `http://127.0.0.1:8009/v1` | `kev-latest` | `KEV_API_KEY`, optional: sent when given, otherwise the call is unauthenticated | `Orkeon:Kev` |

```csharp
services.AddOrkeonDecisionProvider(configuration);
```

The three shipped binaries (`orkeon run`, `orkeon-host` and `orkeon-repl`) call it themselves, and
`AddOrkeonDecisionProfiles` in every case; a host written in C# calls both.

```json
{
  "Orkeon": {
    "Decision": { "Provider": "typesafe" },
    "TypeSafe": {
      "Model": "jev-latest",
      "TimeoutSeconds": 30,
      "MaxRetries": 3
    }
  }
}
```

### The active provider

The host starts with one **active provider**, or none.

- **Named**: `Orkeon:Decision:Provider` is `typesafe`, `liquid` or `kev` (case-insensitive; a blank
  value counts as not named). Any other value refuses the start, with the valid names listed.
- **Deduced**, when it is not named: the one hosted provider (TypeSafe or Liquid) that has a
  settings section (a key present under `Orkeon`, even an empty one, counts). When neither has a
  section, the one whose key variable is set. A section outweighs a bare key: that provider is
  then ignored.
- **Refused**: both sections present, or no section and both keys set; the message names both
  providers and `Orkeon:Decision:Provider`.
- **None**: nothing present, no decision model is registered.
- **Kev** is never deduced, only named. An `Orkeon:Kev` section without `Orkeon:Decision:Provider`
  is warned in the start log (when no provider is named, the warning names `Orkeon:Decision:Provider`) (a log line, not a settings notice).

The start log names the active provider, the model, the address and `named` or `deduced`. Every
provider section present that is not the active one is named in a warning (a bare environment key
is not reported). `--no-decision` (a run without decisions) holds for every provider.

### Keys, addresses and errors

- **The key** is given three ways: the default variable, a variable named in `ApiKeyEnvVar`, or
  `ApiKey` in the provider's section. It never appears in a log, an error or a tool call. A hosted
  provider starts without its key; the first decision without it fails, is not retried, and names
  the variable or the setting.
- **`BaseUrl`** overrides the default address. A Kev address needs an `http` or `https` scheme,
  refused at start otherwise (the message names `Orkeon:Kev:BaseUrl`). A configured Kev key towards a
  non-local `http` address is refused at start (local is `localhost`, `127.0.0.0/8` and `::1`;
  `https` is fine).
- **Errors** become a `DecisionModelException`: a 401 or 403 gives the key message; another 4xx
  except 429 is non-transient and quotes the provider's message (a 422 cites the field at fault, a
  400 `max_tokens_exceeded` names the limit and suggests `longContext` when the request did not
  ask for it, a 413 or 415 says what the request exceeded). A 429, a 5xx (529 included), a 408 or
  an unreachable address is retried with an exponential backoff, up to `MaxRetries` times, each
  attempt with its own `TimeoutSeconds`; then it fails naming the provider, and the address only when it was unreachable or did not
  answer in time (a persistent 429, 5xx or 408 gives `<Name> answered <status>: ...`, with no address).
  A `usage` without token counts is metered as zero, not refused.
- **The danger-zone headers** are set from the request: `TypeSafe-Danger-Zone-Images` when it
  carries images or a `box` question, `TypeSafe-Danger-Zone-Long-Context` when `LongContext` is set.
- **Questions**: Orkeon sends every question and the per-call model as written. Kev, and d1 served
  by llama.cpp, accept only yes/no, `choice` and `score` questions.
- **Usage** is metered once, as `decision` (`LlmUsageOperations.Decision`) against the active
  provider; Orkeon computes no cost.

### Live campaign

`orkeon llm probe -p typesafe` (or `liquid`, `kev`) runs seven modes against the real API through
`SystemOneDecisionModel`: a `noul`, a `choice` and a `score` with obvious answers, a `box` on a PNG
asset, the refusal of a state over the 64k context, a typed error for an unknown model, and
cancellation. An eighth, D8, sends the same state with long context set and checks that a sentence
buried in its middle is found; it costs about 100k input tokens, so it runs only when named
(`--modes D8`). The LLM provider campaign kit runs it like the chat providers
(`llmproviders-test/run-campaign.sh --provider typesafe`) and archives the report under
`llmproviders-test/typesafe/`; its README lists the modes.

### The open d1 weights

Orkeon does not serve the d1 weights itself. Served behind Kev or another System One-compatible
local server, they are used as Kev, with that server's address in `Orkeon:Kev:BaseUrl`. A d1 served
by llama.cpp takes only yes/no, `choice` and `score` questions.

`AddOrkeonDecisionProvider` also registers the input guard and the `system_one_decide` tool.

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

A `verify` judges answers, not the steps towards them: in an agent loop, a response that calls
tools (an OpenAI `tool_calls`, an Anthropic `tool_use` block) passes unjudged. `Steps: "all"`
judges those steps too; `final` is the default.

Every question carries `instructions` with its criterion: a `guard` asks "Does the text match the
following description?" with the risk's description, a `verify` "Does the response meet the
following criterion?" with `Criterion`. A System One server built on Liquid's reference code
refuses a question without them.

A blocked response carries, besides `error` and `error_type`, the metadata `decision_profile`,
`decision_stage`, `decision_risk` (a `guard` only) and `decision_reason`;
`DecisionBlockedException.FromResponse` reads them back. A text stream (`GenerateStreamingAsync`)
throws that exception instead. A script receives a block as a `DecisionBlockedError`
([scripting DSL](../reference/scripting-dsl.md)).

### Configuration

```csharp
services.AddOrkeonDecisionProvider(configuration);
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
profile like any other: `llm: { profile: cascade }`; a script names it for one `ctx.llm` call
with `{ llm: { profile: "cascade" } }`, and its stages judge that call.

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
  `confidence`, plus `"defaulted": true` when the fallback answered (`defaulted` is a reserved key: no question may take that name). A score question has one form: its
  answers are keyed by level position and it carries no `chosen`, the fallback included. A dependency
  that was retried is read at its last successful output, else at its last attempt. The task's
  `TokensUsed` is the usage the model reports.
- A decision task takes no agent, but its crew still declares one: the YAML loader refuses a crew
  with no agent, and every mode refuses to start one (`Crew.StartExecution`), a script crew
  included. A skipped task
  takes no agent turn in round-robin.
- The task sends its state and **all its questions in one request**. The state is the output of
  each task it depends on that ran, labelled with the task's key, or only the tasks `state` lists.
  The only repeats are the adapter's retries of that same request on a 429, a 5xx, a 408 or a network error.
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
- A skipped task is reported as skipped by the run's JSONL, with its reason (see
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
configured (in the shipped binaries, when an active provider is named or deduced) and
`Orkeon:Decision:Enabled` is `true` (its default), and off when:

- no decision model is configured;
- `Orkeon:Decision:Enabled` is `false`;
- the run is started with `orkeon run --no-decision`, which wins over `Orkeon:Decision:Enabled=true`.
  The option exists on `orkeon run` only; the other hosts read the setting.

A failed request never turns the circuit off: that is a failed task.

With the circuit off, every decision task answers by its **fallback**, without a request, and is
marked **defaulted** (`defaulted` in the task's event; the question name `defaulted` is reserved for that reason). Its status stays completed.

- With explicit `fallback` answers, the chosen answer carries a confidence of 1 and a probability of
  1: the conditions on it hold as if the model had answered with full confidence. A question left out
  of `fallback` selects no option.
- With no fallback, the decision task selects no option and carries a confidence of 0: every option
  condition is false, the `otherwise` task runs and the others are skipped.
- Before the first task, a run with the circuit off is **refused**, naming the task, when a decision
  task has neither `fallback` answers nor an `otherwise` task on it
  (`Decision task 'triage' has neither a fallback nor an otherwise task.`).
- A crew without a decision task runs the same whatever the circuit.

#### Every consumer follows the circuit

With the circuit off, the run makes **no** `IDecisionModel` call, whatever the cause. The cause is
named in every message, as `the decision circuit is off: <cause>`, the cause being `--no-decision`,
`Orkeon:Decision:Enabled=false` or `no decision provider resolved`, and, in the relay pass of a forge
trial, `the forge's trial pass without System One` (cause `trial`); when the switch and the setting
both hold, the switch is named. A forge trial emits no `run.decision`: its pass reports `circuit` and `cause` on the
forge stream's `run.started`, `task.completed` and `llm.call.*` lines. Each consumer that degrades says so in one warning:

| Consumer | Circuit off |
| --- | --- |
| Decision task | Answers by its fallback, as above. |
| `system_one_decide` tool | Withheld: a crew that lists it fails to load under `StrictTools` (the runners' default), naming the tool and the cause (`tool 'system_one_decide' withheld: the decision circuit is off: --no-decision`); a `required` crew meets the mode refusal instead; otherwise it is skipped with a warning. Under `--validate` it is a `WARNING:` line, the tool is not counted in `tools resolved`, and the exit stays 0. |
| Decision profile | Built without the model: its calls go to the `route`'s `fallback`, else to the `target`; a `verify` never escalates; a `guard` lets the call go on, except in a `failClosed` profile, where every call is blocked. One warning when the profile is first resolved. |
| LLM judge (`Orkeon:Guardian:Llm`) | `FailMode: Closed` refuses to start, naming the cause (the runner host checks it with its settings); `Open` leaves the judge out of the pipeline, with one warning, and the other guards stay. |
| `decision` reranker | Passes the passages through in their retrieval order, as `none` does, with one warning. |
| Agent skills (`skills: { auto: true }`) | The agent carries its `fallback` list, else no skill, and the task runs; `agent.skills` reports `mode: fallback` and `reason: circuit-off`, with the cause as `detail`. One warning per run. |
| Agent routing (`llm.profile: auto`) | The task runs on the host's `default` profile, never the agent's own, with no decision call; `agent.route` reports `reason: circuit-off`. One warning per run. |
| Tool-result pruning (`Orkeon:Compaction:Prune`) | Nothing is pruned and no decision call is made; the loop compacts as it would without it, and no `agent.pruned` is written. One warning per run. |

The circuit is read at each call, not once per host. A run whose scope switches it off (its
`DecisionCircuitOverride`, as the forge sets for a trial, entered for the run as `DecisionCircuitScope`) is off
for every consumer above, those built once per host included: there a fail-closed judge blocks each text it
would judge and a fail-open one lets it through, with one warning, each verdict audited as
`guardian.judge.unavailable` with the fail mode in its description, and a decision profile routes as if built
without the model; a decision task is answered by its relay or its fallback, and `system_one_decide`, offered
because the host's circuit is on, answers a tool error naming the cause without asking the model. A scope never
switches on a circuit the host has off.

#### A crew's decision mode

A crew says what it needs from System One in a crew-level `decision:` block: in YAML `decision: { mode: required }`,
in a script `crewBuilder().decision({ mode: "required" })`, in C# `CrewBuilder.DecisionMode(...)`. Without the block
the crew behaves as above, and the engine reports its mode as `unspecified`. The same rules apply at load to YAML,
declarative `.ork.ts` and C# crews:

| | `none` | `optional` | `required` |
| --- | --- | --- | --- |
| Decision task | Refused. | At least one, so refused under a process that runs no decision task. | Allowed. |
| `without:` relay | Refused. | Required on every decision task; it stands in for the fallback-or-otherwise rule. | A warning: never used. |
| `fallback` | n/a | Refused: never used. | A warning: never used; the fallback-or-otherwise rule does not apply. |
| `system_one_decide` in `tools:` | Refused. | Refused. | Allowed. |
| Decision profile (`llm.profile`, `llm_override.profile`) | Refused. | Allowed; with the circuit off its stages degrade as in the table above. | Allowed. |
| Agent routing (`llm.profile: auto`) | Refused. | Allowed; with the circuit off the task runs on the default profile. | Allowed. |
| Agent skills (`skills: { auto: true }`) | Refused: list the skills instead. | Allowed; with the circuit off the agent carries its `fallback` list. | Allowed. |
| Tool-result pruning (`compaction: { prune: true }`) | Refused; the host's `Orkeon:Compaction:Prune` is not applied to its agents. | Allowed; with the circuit off nothing is pruned. | Allowed. |

A crew with no mode may not declare a `without:` relay: the load asks for `decision.mode: optional`. A relay names an
agent of the crew (`without: { agent, instructions }`, `instructions` optional), and an agent the crew does not
declare is refused at load. A `required` crew that asks System One nothing (no decision task, no `system_one_decide`,
no decision profile, no `llm.profile: auto`, no `skills: { auto: true }`, no `compaction: { prune: true }`) loads with a
warning. A crew built in C# meets these rules at `CrewBuilder.Build()`, a manager given as an object
(`.Hierarchical(manager)`, `.WithManager(manager)`) included, and `llm.profile: auto` is checked there too (an agent on
`LlmConfig.OnProfile("auto")`, a task's `LlmConfigOverride.Profile`; a manager or a relay with no task of its own
stays accepted, as in YAML), with three differences: its relay names an agent by its role (a crew factory resolves a
YAML or `.ork.ts` key to the agent); its decision profiles are not checked there, since only the crew factory knows
the host's profiles (`Orkeon:Decision:Profiles`); and the rules' warnings are dropped, only their refusals fail the
build.

With `mode: required` and the circuit off, whatever the cause, a crew run by the orchestrator (YAML, C#, a declarative
`.ork.ts` crew) is **refused before its first task**, in every process and every host: `crew 'x' requires System One
(decision.mode: required) and the decision circuit is off: --no-decision`. The error carries the code
`decision_unavailable`; `orkeon run` exits `69` (`EX_UNAVAILABLE`), and `orkeon-host` and `orkeon mcp serve --crew`
record exit `69` on the run's `run.finished`. A procedural `.ork.ts` crew (`await crew.run()`) neither applies the
mode nor warns about it. A
`required` crew that lists `system_one_decide` still loads, the tool withheld with a warning, so that it meets this
refusal rather than an unknown tool. With `mode: optional` and the circuit off, each decision task is answered by its
`without:` relay ([below](#the-without-relay)). `--validate` reports the refusal a run would meet as a `WARNING:` line and exits `0`
([the CLI](../reference/cli.md#orkeon-run)), and a run emits one `run.decision` event with the mode, the circuit the run
follows and its cause: `off` with the cause `no-provider` on a host whose circuit is on but that has no decision model,
as the refusal says ([the run event bus](./run-event-bus.md)).

#### Circuit off, by mode

| Mode | Circuit off |
| --- | --- |
| no mode (`unspecified`) | Each decision task answers by its fallback, marked `defaulted`, as above; a resumed run replays the decisions it recorded, a `defaulted` fallback included, without deciding again ([below](#decisions-on-resume)). |
| `none` | Nothing changes: the crew has no decision task. |
| `optional` | Each decision task is answered by its `without:` relay; no fallback is used, and `defaulted` stays `false`. |
| `required` | The run is refused before its first task, with `decision_unavailable`. |

#### The `without:` relay

The relay is one LLM call made with the relay agent's provider: its own `llm` block, else its `llm.profile`, else the
default profile. The block's sampling, penalties, stop sequences, thinking and cache go with the call, as on the
agent's own calls; its `response_format` does not, the relay asking for its own JSON object. A stop sequence that can end
the JSON object (`}`, a blank line) cuts the answer, which is then refused. The call sends the agent's prompt (role, goal, backstory), the questions (a choice's options in their declared order), the state the task reads,
as JSON, and `without.instructions`; it has no tool, no memory and no history. The agent reports **probabilities
only**, as one JSON object with one key per question:

- a `choice`: an object giving every option a probability;
- a `score`: an object giving every level, keyed by its position from `"0"`, a probability;
- a `noul`: a number, the probability that the statement is true.

The schema of that object is generated from the questions. A provider that declares `json_schema` receives it as
its response format; any other receives it in the prompt, and its answer (a fenced block included) is read and
checked the same way.

- **Checks.** The answer is JSON that parses, every question is present and no other key, a distribution gives a
  probability to every option or level and to none that was not offered, every value is in [0, 1], and each
  distribution sums to 1 within a tolerance of **0.05**; it is then normalised.
- **Derivation.** The rest is derived exactly as from the model's answer: a `choice` takes the most probable option
  as `chosen` (on a tie, the option declared first; options given in C# as an `ImmutableDictionary`, whose order
  changes from one process to the next, are kept in the ordinal order of their names), with that probability as its
  confidence; a `noul` is `yes` when p >= 0.5, with a confidence of
  max(p, 1 - p); a `score` is the weighted position, the sum of i * p(i) with the first level counted 0, the scale
  thresholds read, with the largest p(i) as its confidence and no `chosen`.
- **One more try.** An answer that fails a check is asked once more, with the reason it was refused. This is the
  relay's own retry, not the task's `retry:` (which decision tasks refuse). A second refusal fails the task with
  the reason; so does an error of the call. No fallback is ever used.
- **Reported.** The task's `task.completed` carries `decisionSource: relay`, `defaulted: false`, its answers as
  `decision`; it names no model. The relay's prompt and completion tokens count on the task and in the run's totals,
  and its `cost.updated` names the operation `relay` with the relay agent's role as `agentId`.
- **Rate limits.** A relay call is a model call of the relay agent: it waits its turn as an agent's call does, under
  the agent's `maxRpm`, the crew's `maxRpm` and `RateLimiting:AgentRequestsPerMinute`, and one Information line
  says how long it waited and under which limit.
- **`longContext`** is a request option of System One and the relay ignores it: a state larger than the relay
  model's window fails the task as any LLM call over its window does.

**Calibration.** The probabilities an LLM states for itself are not calibrated, and they cluster high.
`minConfidence` gates only option clauses: a threshold clause such as `triage.urgent: ">= 0.7"` reads the raw
probability and ignores it, so behind a relay it mostly passes. An `optional` crew therefore prefers option clauses,
`triage.route: [bug]` on a choice and `triage.urgent: [yes]` on a yes-no question, which `minConfidence` gates. A
`score` has no chosen level: it takes thresholds only, and `minConfidence` does not apply to it.

#### Decisions on resume

A decision task's answer is recorded in the run's checkpoint with its source (`model`, `relay` or `fallback`), in
every process: a resumed Sequential, Parallel or Graph run replays it without a new request and reports the stored
`decisionSource`. The checkpoint thus shows the circuit the run was started with: on when a decision came from the
model, off when one came from the relay or the fallback. An `optional` crew resumed under the other circuit is
refused, with `decision_circuit_mismatch` and exit `1`, naming both circuits: its decisions left would otherwise come from the other
source. Only the decisions of the tasks the crew still declares as decision tasks count, by their key (one whose
definition changed under the same key still counts, though it would be decided again: the check is made at kickoff,
before any task's fingerprint is compared), and they are compared with the circuit of the run that resumes: off when its runner has no decision model. A run that recorded no decision yet resumes under either
circuit, and a crew with no mode is never refused for its circuit: its recorded decisions are replayed, a
`defaulted` fallback included, even when the run resumes with System One back. A decision is replayed only while its task is
unchanged: its questions, fallback and relay count in the task's checkpoint fingerprint, beside its description and
dependencies, so a decision task edited since the run is decided again, and the tasks after it run again. The
fingerprint reads a choice's options and the fallback by their content, not their order: reordering them alone does
not decide the task again, though it changes which option wins a tie the next time it is decided.

### Jev's limits and long context

Jev takes 64,000 tokens per request, 32,000 per question, and 262,000 in long context. A choice holds
up to 255 options and a score up to 10 levels (refused at load beyond). Orkeon counts no token: the
limit is the model's refusal, which fails the task with the limit in the message, and suggests
`longContext: true` when the task did not ask for it. Long context is requested by the task only,
never on its own.

## Secondary uses

- **LLM judge** — `LlmJudgeGuard` joins the agent's guardian pipeline in the `Input` and `Output`
  phases when `Orkeon:Guardian:Llm:Enabled` is true. It judges through the one `IDecisionModel` (the
  active provider; the guard has no profile of its own) and signs its verdicts with that provider's
  name. Its section is `Orkeon:Guardian:Llm` (`Enabled`, `Phases`, thresholds, `Risks`, `FailMode`,
  `MaxContentChars`); `Risks` default to `prompt_injection` and `data_exfiltration`, `BlockThreshold` to
  0.8, `WarnThreshold` to 0.5. The `Output` phase judges a task's validated output before the
  deliverable, the memory and `task.completed`: a block fails the task and nothing is kept; a call the
  provider never answered has no output and is not judged. When the model fails, `FailMode: Closed`
  (default) blocks and `Open` warns; both audit `guardian.judge.unavailable`. A text longer than
  `MaxContentChars` is, under `FailMode: Closed`, judged whole in chunks of that size (any chunk that
  blocks blocks), up to 32 chunks; a text beyond 32 chunks is refused and the refusal audited as
  `guardian.judge.truncated`. Under `Open` it is judged on its first and its last half of that budget
  with a marker between them, and **what lies between is not judged**; the cut is audited as
  `guardian.judge.truncated`.
  While the `Output` phase is guarded, a task's streamed deltas (`llm.delta`, the console) are held
  until the judgement: streaming is delayed until the output is allowed, and a blocked output is
  never streamed. The `Output` phase covers crew tasks only; a scripted `ctx.llm.act` crosses the
  `Input` phase and its answer is not judged.
  With the decision circuit off, a `Closed` judge refuses to start and an `Open` one is left out
  ([Every consumer follows the circuit](#every-consumer-follows-the-circuit)).
- **Agent skills** — an agent with `skills: { auto: true }` asks one `choice` per task, `skill`, whose options
  are one line per markdown file of `Orkeon:Skills:Library` (at most 24, named `s1`…`sN`) plus `none`; the state is
  the task's text cut at 2,000 characters, and no skill body is sent. Every option at a probability of 0.3 or more
  is kept, the three most likely at most; `none` keeps no skill. With the circuit off, or when the call fails, the
  agent carries its `fallback` list ([`Orkeon:Skills`](../reference/configuration.md#orkeonskills)); in a crew on
  `decision.mode: none` it carries it with no call and no warning, `agent.skills` giving `mode: fallback` and
  `reason: decision-none`.
- **Agent routing** — an agent with `llm.profile: auto` asks one `choice` at the start of each task, `profile`,
  whose options are the rungs of `Orkeon:Decision:Route:Ladder`, cheapest first, each with what it is for (the
  cheapest, a middle one, the most capable); the state is the agent's role and the task's text cut at 4,000
  characters in the middle. The rung picked at a probability of 0.6 or more runs every call of the task, on that
  rung's own model: a `model:` beside `profile: auto`, in the agent's `llm:` block or the crew's, is refused at load,
  as `AgentBuilder.Build()` refuses `LlmConfig { Profile = "auto", Model = ... }`. Under 0.6, without an answer, or
  with the circuit off, the host's default profile runs, and `agent.route` says why
  ([`Orkeon:Decision:Route`](../reference/configuration.md#orkeondecisionroute)); in a crew on `decision.mode: none`
  it runs with no call and no warning, `agent.route` giving `reason: decision-none`. This is one pick per task, not the
  `route` stage of a decision profile, which picks a profile for each call.
- **Tool-result pruning** — once a loop's estimate reaches the smaller of `Orkeon:Compaction:Prune:FillShare` of the
  usable window and the compaction trigger, before compaction, it asks two `noul` per older read-tool result,
  `needed_n` and `whole_n`, eight results to a request and 24 at most at one pressure, oldest first, the rest at the
  next model call; the state is the task's text cut at 2,000 characters and one line per call (tool, input cut at 200
  characters, result size, age in model turns), never the result itself. Not needed drops the result, needed but not
  whole keeps its first `HeadChars` characters, needed whole keeps it, at a probability of 0.5. A failed request
  leaves its results as they are; with the circuit off nothing is asked
  ([`Orkeon:Compaction:Prune`](../reference/configuration.md#orkeoncompactionprune)).
- **RAG reranker** — `rerank.kind: decision` asks one `noul` per passage in a
  single request, passages cut at 4,000 characters, and orders them by probability. With the circuit
  off, it keeps the retrieval order.
- **Tool** — `system_one_decide` lets an agent ask `choice`, `score` and `noul` questions
  ([Tool inventory](../tools/inventory.md)); `box` is refused. With the circuit off, it is withheld.
- **The forge** — `orkeon forge` asks System One for its own advice while it designs a crew: a `score` of the
  brief's coverage of the request, a `choice` of process (passed to the assistant as `processHint` at a probability
  of 0.6 or more, among the processes the crew's decision mode allows), a `noul` estimate before the trial and one
  `noul` per acceptance criterion and per successful trial pass after the judge. Every answer is advice and never moves the brief, the blueprint,
  the verdict or the arbitration; each call is one `systemone.answered` event, its tokens are charged to the
  session's budget (`systemOneTokens` on `cost.updated`), and a failed call is one `FORGE-SYSTEMONE-FAILED` warning.
  These calls are the forge's own and are split from the trial's: `--no-decision` (announced on `session.started`
  as `forgeCircuit: off`, `forgeCircuitCause: switch`, like `settings` and `no-provider`) turns them off without
  touching the engine host's circuit, which the trial of an `optional` or `required` crew follows
  ([the CLI](../reference/cli.md#orkeon-forge)). The assistant and the judge name no decision profile.

## Limits

- The request stages ask about the last user message only; a decision over the whole
  conversation needs a custom stage. A request sent inside a long user message (context,
  history, then the request) is judged with all of it, and the context dilutes its risk: a
  script passes that context to `ctx.llm.act` as `ActOptions.context`, a user message of its own
  before the prompt, and the stages judge and route on the prompt alone (#276).
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
