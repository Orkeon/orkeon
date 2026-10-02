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

## Cascade — three layers, five surfaces

The fusion is done **once** in `LlmConfigResolver.Resolve(baseConfig, taskOverride, callOverride)`.
Priority highest-first: call override → task override → base config. Each field is resolved
independently (`ResponseFormat`, `Temperature`, `MaxTokens`, `TopP`, `Thinking`, `Cache`), and a
`null` field on an override never erases an inherited value.

| Surface | Lands in | Where it is written |
|---|---|---|
| 1. Crew default | base config | crew YAML `llm:` — merged field by field into every agent's `llm:` |
| 2. Agent | base config | agent YAML `llm:`, `agentBuilder().withResponseFormat(...)` / `.withResponseSchema(...)`, the agent's `LlmConfig` in C# |
| 3. Task | task override (`LlmConfigOverride`) | task YAML `llm_override:`, `taskBuilder().withResponseFormat(...)` / `.withResponseSchema(...)`, `CrewTaskBuilder.WithResponseFormat(...)` / `.WithLlmOverride(...)` |
| 4. Script-time | base config | `llmConfig.with({ responseFormat })` on the config handed to `agentBuilder().llm(...)` |
| 5. Call-time | call override | `ctx.llm.complete/chat/stream(..., { responseFormat })` in a script, `LlmProviderExtensions.GenerateAsync/ChatAsync(..., LlmConfigOverride, baseConfig)` in C# |

## YAML surfaces

### Crew default

```yaml
llm:
  model: deepseek-flash
  response_format: json_object   # every agent of this crew now replies in JSON
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

The `llm_override:` block accepts `response_format`, `response_schema`, `temperature`,
`max_tokens`, `top_p` and `thinking` — the agent-level `llm:` fields minus `model` and
`cache:`.

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

// Call-time override (most surgical — wins over everything else)
const res = await ctx.llm.complete(
    "Return the answer as a json object",
    { responseFormat: "json_object" }
);
```

`.llm(...)` takes an `LlmConfig` — `llm.default_`, `llm.model("…")`, `llm.profile("…")` or
`.with({...})` on any of them — and refuses a string or a plain object literal. The provider is
the host's, or the one of the host profile `llm.profile(...)` names; `.llm(...)` sets the model.
A response format is checked against the provider the agent actually runs on. `withResponseSchema(name, schema, strict?)` takes an object
literal or a JSON string and implies `json_schema`. `ctx.llm.extract(prompt, schema)` asks
for `json_object` by default unless the call passes `{ responseFormat: "text" }`.

## C# fluent surface

```csharp
var task = new CrewTaskBuilder()
    .Description("Extract as JSON")
    .ExpectedOutput("JSON")
    .WithResponseFormat(LlmResponseFormat.JsonSchema("invoice", invoiceSchemaJson))
    .Build();

// Call-time override via extension method
await provider.GenerateAsync(
    prompt,
    LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject()),
    agent.LlmConfig,
    ct);
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
today) logs a structured `Warning` (event id `100`, `LogMissingJsonKeyword`) when it detects the
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
| **Anthropic** | `JsonSchema` | Own dialect (`output_config`) — schema-only, no bare `json_object`. |
| **Ollama** | `JsonSchema` | Own dialect (`format`). |
| **DeepSeek**, Kimi, Qwen, HuggingFace, Z.AI | `JsonObject` | Well-formed JSON guaranteed; a schema is downgraded with a warning. DeepSeek also requires the `json` keyword (above). |
| **MiniMax** | `None` | Accepted but non-binding — measured 2026-08-30 (schema ignored, `json_object` fenced in markdown); a declared format produces the structured capability warning. |
| **OpenRouter** † | `JsonSchema` | Documented per endpoint (2026-09-18, not campaigned); the provider does not send `provider.require_parameters`, so a schema may be ignored by an endpoint that lacks it — the first campaign's question. |
| **Mammouth AI** † | `None` | Undocumented on the proxy (2026-09-18, not campaigned); a declared format produces the structured capability warning until a campaign measures it. |

† not campaigned yet.

## `structured_output` deliverables

A task whose deliverable is `source: structured_output` carries its schema (`schema_inline` or
`schema_path`) in two forms, and the chat-client adapter sends the one the provider honours —
never both, which `llama-server` refuses:

- a **GBNF grammar**, on an endpoint the settings declare able to take one
  (`"Llm": { "Grammar": true }` — Docker Model Runner, `llama-server`);
- otherwise a **`json_schema` response format** (name `structured_output`, `strict: false`, since
  OpenAI's strict mode refuses a schema that leaves an object open or a property optional), on a
  provider that declares `JsonSchema` in the matrix above;
- otherwise nothing binds: the grammar reaches the provider, which drops it with a structured
  warning naming `Llm:Grammar`.

A response format the crew sets itself (`llm:` / `llm_override:`) always wins over the
deliverable's schema. In every case `StructuredOutputResolver` still parses the answer as JSON
before writing the file.

## How it travels through the orchestrator

The cascade is fused exactly once per turn (`LlmConfigResolver.Resolve`), in 3 call sites — in the agent loops and the validation coordinator (`LegacyTextAgentLoop`, `NativeToolCallingAgentLoop`, `OutputValidationCoordinator`), which `ExecutionOrchestrator` drives:

1. Legacy text-based `[TOOL_CALL]` loop — `_llmProvider.ChatAsync(prompt, effectiveConfig, …)`
2. Native tool-calling loop — `_fullProvider.ChatAsync(messages, effectiveConfig, …)`. `BuildNativeLlmConfig` seeds from `agent.LlmConfig`, so the model name and Thinking config survive the entry to the native path; an agent without one gets a configuration that names no model (`LlmConfig.OnProfile()`), and the provider runs the call on its own.
3. Validation correction retry — `_llmProvider.ChatAsync(correctionPrompt, effectiveConfig, …)`

The 6 process strategies (Sequential, Hierarchical, Autonomous, Graph, Parallel, Consensual) run tasks through `IAgentExecutionService.ExecuteTaskAsync`, which delegates to `ExecutionOrchestrator.ExecuteTaskCoreAsync` — they get the cascade for free.

`LlmBasedManager` (manager LLM in Hierarchical mode) does not apply a task override: it has no `task` in scope, so its calls run on the provider's own configuration.

## Reference

- Value objects: `Orkeon.Domain.SharedKernel.ValueObjects.LlmResponseFormat`, `LlmJsonSchema`
- Patch record: `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride`
- Fusion: `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigResolver.Resolve`
- Wire translation: `Orkeon.Infrastructure.LLMs.Base.OpenAICompatibleProviderBase.ApplyProviderSpecificOptions` (virtual — Qwen, OpenRouter and Together AI override it and call the base for `response_format`); `AnthropicLlmProvider` and `OllamaLlmProvider` for their own dialects
- YAML mapping: `Orkeon.Infrastructure.Configuration.Yaml.YamlCrewMapper.MapResponseFormat`
- Call-time extensions: `Orkeon.Infrastructure.LLMs.Extensions.LlmProviderExtensions`
- Plan / spec: the maintainers' archive (LLM-RESPONSE-FORMAT plan)
- DeepSeek API doc: https://api-docs.deepseek.com/api/create-chat-completion
