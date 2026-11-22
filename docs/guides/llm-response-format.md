> 🇫🇷 [Version française](../fr/guides/llm-response-format.md)

# LLM Response Format

> Force JSON output at the provider boundary instead of patching prompts. One value object,
> `LlmResponseFormat`, is translated by every provider according to what its API declares it
> can honour.

## Why

Most LLM APIs can constrain the shape of a response at the API layer. Ask for
`response_format: {"type": "json_object"}` on DeepSeek and the API **guarantees** valid JSON
— no broken `{`/`}` brackets, no leading prose; ask OpenAI, Gemini or Anthropic for a JSON
Schema and the vendor validates the answer against it server-side. This is a second barrier
complementing the GBNF grammar (llama.cpp-compatible servers, behind `Llm:Grammar`) and the
downstream `StructuredOutputResolver`.

For Orkeon, this matters most for:

- `structured_output` deliverables that must parse cleanly
- Hierarchical manager agents returning JSON decisions
- TypeScript-scripted workflows expecting typed objects

## The three formats

| `Type` | Built with | Meaning |
|---|---|---|
| `text` | `LlmResponseFormat.Text()` | The provider default. Maps to *nothing on the wire* — writing `text` and writing nothing are equivalent. |
| `json_object` | `LlmResponseFormat.JsonObject()` | Well-formed JSON, no schema. |
| `json_schema` | `LlmResponseFormat.JsonSchema(name, schema, strict = true)` | JSON validated against a JSON Schema (`LlmJsonSchema`: the schema travels as a JSON string; `strict` asks the vendor to reject any deviation, ignored where no strict mode exists). |

`Type` is an open string: any other value is forwarded as-is (the YAML mapper logs a warning,
event id `101`), so a new vendor value works without a framework release.

## Cascade — two layers, four surfaces

The base config and the task override are merged **once** per turn: by `ChatOptionsComposer` on the chat-client loop, by `LlmConfigResolver.Resolve(baseConfig, taskOverride)` on the fallback loops.
Priority highest-first: task override → base config. Each field is resolved
independently (`Profile`, `ResponseFormat`, `Temperature`, `MaxTokens`, `TopP`, `Thinking`, `Cache`), and a
`null` field on an override never erases an inherited value.

| Surface | Lands in | Where it is written |
|---|---|---|
| 1. Crew default | base config | crew YAML `llm:` — merged field by field into every agent's `llm:` |
| 2. Agent | base config | agent YAML `llm:`, `agentBuilder().withResponseFormat(...)` / `.withResponseSchema(...)`, the agent's `LlmConfig` in C# |
| 3. Task | task override (`LlmConfigOverride`) | task YAML `llm_override:`, `taskBuilder().withResponseFormat(...)` / `.withResponseSchema(...)`, `CrewTaskBuilder.WithResponseFormat(...)` / `.WithLlmOverride(...)` |
| 4. Script-time | base config | `llmConfig.with({ responseFormat })` on the config handed to `agentBuilder().llm(...)` |

A call, from C# or from a script (`ctx.llm.complete/chat/stream(..., { responseFormat })`), passes its own `LlmConfig`, outside the cascade.

## YAML surfaces

### Crew default

```yaml
llm:
  model: deepseek-flash
  response_format: json_object   # every agent of this crew replies in JSON
```

### Agent override (wins over crew)

```yaml
agents:
  extractor:
    role: "Invoice extractor"
    llm:
      model: deepseek-flash
      response_format: json_object   # only this agent forces JSON
```

### Task override (wins over agent — the surgical one)

```yaml
tasks:
  extract_invoice:
    description: "Extract fields. Reply as a json object."
    expected_output: "JSON object with all invoice fields"
    llm_override:
      response_format: json_object
      temperature: 0.0
```

The `llm_override:` block accepts `profile` (the task runs on that host LLM profile
instead of its agent's), `response_format`, `response_schema`, `temperature`,
`max_tokens`, `top_p`, `thinking` and `cache` — the agent-level `llm:` fields minus `model`.
A task's `cache:` block (same shape as an agent's) replaces the agent's whole; a block with no
breakpoint turns the agent's cache off for that task.

### JSON Schema

`response_schema:` accompanies `response_format: json_schema`, at agent, crew or task level.
`schema` is the JSON Schema **as a JSON string** (a block scalar keeps it readable); `name`
defaults to `response`, `strict` to `true`:

```yaml
llm:
  model: gpt-5.6-sol
  response_format: json_schema
  response_schema:
    name: invoice
    strict: true
    schema: |
      {"type": "object",
       "properties": {"number": {"type": "string"}, "total": {"type": "number"}},
       "required": ["number", "total"], "additionalProperties": false}
```

A `response_schema:` on its own implies `json_schema`. `json_schema` **without** a schema falls
back to `json_object` with a warning (event id `102`).

## TypeScript / `.ork.ts` surfaces

```typescript
// Builder — agent (the host's configured provider, here DeepSeek)
const extractor = agentBuilder()
    .name("extractor")
    .role("Invoice extractor")
    .llm(llm.default_.with({ model: "deepseek-flash" }))
    .withResponseFormat("json_object")
    .build();

// Builder — task (overrides agent on this task only)
const task = taskBuilder()
    .description("Return invoice fields as JSON")
    .expectedOutput("JSON")
    .agent(extractor)
    .withResponseSchema("invoice", {
        type: "object",
        properties: { number: { type: "string" }, total: { type: "number" } },
        required: ["number", "total"],
    })
    .build();

// A call passes its own LlmConfig, outside the cascade
const res = await ctx.llm.complete(
    "Return the answer as a json object",
    { responseFormat: "json_object" }
);
```

`.llm(...)` takes an `LlmConfig` — `llm.default_`, `llm.model("…")`, `llm.profile("…")` or
`.with({...})` on any of them — and refuses a string or a plain object literal. The provider is
the host's, or the one of the host profile `llm.profile(...)` names; `.llm(...)` sets the model.
A response format is checked against the provider the agent actually runs on. `withResponseSchema(name, schema, strict?)` takes an object
literal or a JSON string and implies `json_schema`. `ctx.llm.extract(prompt, schema)` sends the
schema as a `json_schema` constraint (name `extract`, `strict: false`: the OpenAI dialect's strict
mode needs the `additionalProperties: false` and full `required` a hand-written schema rarely
carries; Anthropic has no `strict` on a response format and adapts the schema itself, see
[Anthropic's schema dialect](#anthropics-schema-dialect)) when the provider validates one (`ResponseFormatSupport.JsonSchema`), and asks for `json_object` otherwise; an explicit
`responseFormat` on the call wins, `"text"` included.

## C# fluent surface

```csharp
var task = new CrewTaskBuilder()
    .Description("Extract as JSON")
    .ExpectedOutput("JSON")
    .WithResponseFormat(LlmResponseFormat.JsonSchema("invoice", invoiceSchemaJson))
    .Build();

// A call passes its own LlmConfig, outside the cascade. Start from the provider's own
// configuration: built empty, it would reset the provider's penalties.
var config = (provider.BaseConfig ?? LlmConfig.OnProfile()) with
{
    ResponseFormat = LlmResponseFormat.JsonObject(),
};
await provider.GenerateAsync(prompt, config, ct);
```

`WithResponseFormat` also accepts a string (`"json_object"`); `WithLlmOverride(LlmConfigOverride)`
sets the whole task patch.

## On the wire

- **OpenAI-compatible family** (every provider but Anthropic and Ollama) —
  `OpenAICompatibleProviderBase` writes `response_format` once, for every provider that
  declares the capability: `{"type": "json_object"}`, or
  `{"type": "json_schema", "json_schema": {"name", "strict", "schema"}}`.
- **Anthropic** — `output_config.format`, schema only: Anthropic has no `json_object`
  equivalent, so a schema-less JSON request is reported (structured warning) rather than sent.
- **Ollama** — `format`: the schema object itself, or `"json"` for `json_object`.

What a provider cannot honour is never dropped in silence:

- a provider declaring `None` sends nothing and logs `Option 'response_format' was declared but
  … does not support it` (event id `110`);
- a schema sent to a `JsonObject` provider is **downgraded** to `json_object` with the same
  warning (`response_format.schema`) — describe the shape in the prompt.

## The "json" keyword guard

DeepSeek's JSON mode requires the prompt (system **or** user message) to contain the word
`"json"` somewhere — otherwise the API may emit an **unbounded whitespace stream** until
`max_tokens` is exhausted. Every provider that declares `RequiresJsonKeywordInPrompt` (DeepSeek
alone) logs a structured `Warning` (event id `100`, `LogMissingJsonKeyword`) when it detects the
situation:

```
DeepSeek was asked for a JSON response format but no system/user message contains the
word 'json'. This API may then emit an unbounded whitespace stream until max_tokens.
Add 'json' to the prompt to be safe.
```

We **do not** mutate the prompt for you — the caller stays in control. Add `"Reply as a json object."` to the system message and the warning disappears.

## Provider support matrix

Support is **capability-driven** (`LlmProviderCapabilities.ResponseFormat` — see the
[provider comparison](../reference/llm-providers-comparison.md)). The declaration is per
provider while reality is per model: a model that refuses the field answers with the vendor's
own error, which surfaces as `LlmResponse.Error` and fails the task with that reason.

| Provider | Declared capability | Notes |
|---|---|---|
| OpenAI, Azure OpenAI, Grok, Gemini, Mistral, Together AI | `JsonSchema` | Server-side schema validation. |
| **Anthropic** | `JsonSchema` | Own dialect (`output_config`) — schema-only, no bare `json_object`; the provider sends a copy of the schema adapted to Anthropic's subset of JSON Schema, or refuses it before the call ([below](#anthropics-schema-dialect)). |
| **Ollama** | `JsonSchema` | Own dialect (`format`). |
| **DeepSeek**, Kimi, Qwen, HuggingFace, Z.AI | `JsonObject` | Well-formed JSON guaranteed; a schema is downgraded with a warning. DeepSeek also requires the `json` keyword (above). |
| **MiniMax** | `None` | Accepted but non-binding (a schema is ignored, `json_object` comes back fenced in markdown); a declared format produces the structured capability warning. |
| **OpenRouter** | `JsonSchema` | Documented per endpoint. The provider does not send `provider.require_parameters`, so an endpoint that lacks schema support may still ignore a schema. |
| **Mammouth AI** | `None` | Accepted but non-binding (`json_object` returns bare JSON, a strict schema is not enforced); a declared format produces the structured capability warning. |

## Anthropic's schema dialect

Anthropic's structured outputs take a subset of JSON Schema and answer a 400 to the rest
([JSON Schema limitations](https://platform.claude.com/docs/en/build-with-claude/structured-outputs#json-schema-limitations)).
A script, a crew or a YAML file never writes a schema for one provider: whatever the way in
(`ctx.llm.extract`, `withResponseSchema`, `response_format`, a `structured_output` deliverable, the
decision circuit), `AnthropicLlmProvider` works on a copy of the schema, as Anthropic's SDK helpers do.
What follows was checked against the Messages API itself.

- Every object node that does not set `additionalProperties` gets `"additionalProperties": false`,
  wherever it sits (`properties`, `items`, `anyOf`/`allOf` branches, `$defs`/`definitions`); an
  explicit `false` is left as written. A property left out of `required` stays optional.
- The constraints the API refuses are removed and written into the node's `description`
  (`{minimum: 18, maximum: 120}`), so the model still reads them: `minimum`, `maximum`,
  `exclusiveMinimum`, `exclusiveMaximum`, `multipleOf`, `maxItems`, `uniqueItems: true`, a
  `minItems` above 1, a `format` other than `date-time`, `time`, `date`, `duration`, `email`,
  `hostname`, `uri`, `ipv4`, `ipv6` and `uuid`. A `uniqueItems: false`, which the API refuses as
  well and which constrains nothing, is dropped.
- What the API takes is sent as written: `minLength`, `maxLength` and `pattern` (the answer keeps to
  `maxLength` and `pattern`; a `minLength` is taken without being held to), `enum`, `const`,
  `anyOf`, `allOf`, a local `$ref` to `$defs` or `definitions`, a `type` list (`["string",
  "null"]`), `title`, `default` and `examples`. Off an object node, `if`, `then` and `else` are
  taken too, on a string, number or `allOf` node, as are `unevaluatedItems` and `additionalItems`
  on an array: the API holds the answer to none of them, and walks no sub-schema under them.

The root is walked the same way whatever its `type`: a root `array` keeps no `additionalProperties`
of its own, the objects below it are closed; a root `{"type": "string"}` is sent as is.

What the API refuses and Orkeon cannot rewrite without changing the schema's meaning is **refused
before any HTTP call**, the sentence naming the keyword and its path:

- `oneOf` (write `anyOf`) and `not`;
- an `additionalProperties` set to `true` or to a schema;
- an object that declares no `properties`, or an empty `properties: {}`, without an explicit
  `additionalProperties: false` — a free-form `{"type": "object"}` (a `metadata` bag), the root
  included: closing it would leave the model nothing to answer but `{}`. List the keys the object
  may hold, write `additionalProperties: false` when an empty object is what you mean, or use an
  array of key and value items for arbitrary keys;
- on an object node, `if`, `then`, `else`, `patternProperties`, `dependentSchemas`,
  `dependencies`, `contains`, `propertyNames`, `unevaluatedProperties`, `unevaluatedItems` and
  `additionalItems`; on an array node, `contains` and `prefixItems`, and an `items` that is not one
  schema object (a tuple, `true`); on an `anyOf` node, `if`, `then` and `else`;
- a `$ref` beside a constraint (`type`, for one) — the annotations `description`, `title`,
  `default` and `examples` may sit beside it, and a constraint the provider moves no longer counts;
- an external `$ref`, and a recursive schema (a `$ref` back to a node it sits in);
- a schema that accepts any value — `{}`, a boolean schema (`true`), the root included — or a node
  that names no `type` (nor `anyOf`, `allOf`, `$ref`, `const` or `enum`);
- a `description` that is not a string;
- messages that end on an `assistant` turn (a prefill, which Anthropic refuses with a JSON output).

A script reads, for example:

```
ctx.llm.extract: the provider failed: Anthropic cannot take the response schema 'extract': `oneOf` at `#/properties/pick` is not supported; use `anyOf`.
```

The refusal is an error answer of type `UnsupportedResponseSchema`, classed as refused: a fallback
chain does not move it to its next profile. A task with a retry policy still runs again after its
delay, and is refused again: fix the schema rather than raise the attempts.

The caller's schema is never changed. A task's `output_json` is validated against the author's own
schema (`OutputValidationCoordinator`), every constraint included, so a constraint moved into a
description is still checked after the call and a miss runs the correction round. Elsewhere
(`ctx.llm.extract`, a deliverable) the answer is parsed as JSON, and a moved constraint reaches the
model as text only.

## `structured_output` deliverables

A task whose deliverable is `source: structured_output` carries its schema (`schema_inline` or
`schema_path`) in two forms, and the chat-client adapter sends the one the provider honours —
never both, which `llama-server` refuses:

- a **GBNF grammar**, on an endpoint the settings declare able to take one
  (`"Llm": { "Grammar": true }` — Docker Model Runner, `llama-server`);
- otherwise a **`json_schema` response format** (name `structured_output`, `strict: false`, since
  OpenAI's strict mode refuses a schema that leaves an object open or a property optional), on a
  provider that declares `JsonSchema` in the matrix above (Anthropic sends it in
  [its own dialect](#anthropics-schema-dialect));
- otherwise nothing binds: the grammar reaches the provider, which drops it with a structured
  warning naming `Llm:Grammar`.

A response format the crew sets itself (`llm:` / `llm_override:`) always wins over the
deliverable's schema. In every case `StructuredOutputResolver` still parses the answer as JSON
before writing the file.

## How it travels through the orchestrator

The cascade is fused exactly once per turn by `LlmConfigResolver.Resolve`. On the chat-client path, `ChatOptionsComposer` calls it on the agent's configuration and the task's override, then translates the merged configuration onto the `ChatOptions` field by field: it merges nothing itself, and each field reaches the provider as written. The other call sites have no chat client and call it as they are: `LegacyTextAgentLoop` and `OutputValidationCoordinator` (base: the agent's configuration), and `NativeToolCallingAgentLoop` (base: `BuildNativeLlmConfig`, the agent's configuration plus the system prompt and the tools). `ExecutionOrchestrator` drives them:

1. Legacy text-based `[TOOL_CALL]` loop — `_llmProvider.ChatAsync(prompt, effectiveConfig, …)`
2. Native tool-calling loop — `_fullProvider.ChatAsync(messages, effectiveConfig, …)`. `BuildNativeLlmConfig` seeds from `agent.LlmConfig`, so the model name and Thinking config survive the entry to the native path; an agent without one gets a configuration that names no model (`LlmConfig.OnProfile()`), and the provider runs the call on its own.
3. Validation correction retry — `_llmProvider.ChatAsync(correctionPrompt, effectiveConfig, …)`

The 6 process strategies (Sequential, Hierarchical, Autonomous, Graph, Parallel, Consensual) run tasks through `IAgentExecutionService.ExecuteTaskAsync`, which delegates to `ExecutionOrchestrator.ExecuteTaskCoreAsync` — they get the cascade for free.

`LlmBasedManager` (manager LLM in Hierarchical mode) does not apply a task override: its calls run on the LLM the crew gives its manager — the provider `WithManagerLlm` sets, else the manager agent's profile and model, else the default profile — with that provider's own configuration for everything else (no response format, no task override).

## Reference

- Value objects: `Orkeon.Domain.SharedKernel.ValueObjects.LlmResponseFormat`, `LlmJsonSchema`
- Patch record: `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride`
- Fusion: `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigResolver.Resolve`
- Wire translation: `Orkeon.Infrastructure.LLMs.Base.OpenAICompatibleProviderBase.ApplyProviderSpecificOptions` (virtual — Kimi, OpenRouter, Qwen, Together AI and Z.AI override it and call the base for `response_format`); `AnthropicLlmProvider` and `OllamaLlmProvider` for their own dialects
- YAML mapping: `Orkeon.Infrastructure.Configuration.Yaml.YamlCrewMapper` (its private `MapResponseFormat`); the export is `Orkeon.Infrastructure.Configuration.YamlCrewExporter.MapResponseFormat`
- DeepSeek API doc: https://api-docs.deepseek.com/api/create-chat-completion
