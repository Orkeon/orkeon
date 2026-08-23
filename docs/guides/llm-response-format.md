> 🇫🇷 [Version française](../fr/guides/llm-response-format.md)

# LLM Response Format

> Force JSON output at the provider boundary instead of patching prompts. First wired provider: **DeepSeek**.

## Why

OpenAI-compatible APIs expose a `response_format` field that the model honours at the API layer. Set `response_format: {"type": "json_object"}` and DeepSeek **guarantees** the response is valid JSON — no broken `{`/`}` brackets, no leading prose. This is a second barrier complementing the existing GBNF grammar (llama.cpp / Ollama) and the downstream `StructuredOutputResolver`.

For Orkeon, this matters most for:

- `structured_output` deliverables that must parse cleanly
- Hierarchical manager agents returning JSON decisions
- TypeScript-scripted workflows expecting typed objects

## Cascade — 5 levels of override

```
1. LlmConfig.ResponseFormat       (global default / crew YAML)
2. agent.LlmConfig.ResponseFormat (agent YAML)
3. task.LlmOverride.ResponseFormat (task YAML — NEW)
4. script-time override            (TS via Jint — NEW)
5. call-time override              (method parameter — NEW)
```

The fusion is done **once** in `LlmConfigResolver.Resolve(baseConfig, taskOverride, callOverride)`. Priority highest-first: callOverride → taskOverride → baseConfig. A `null` field on an override never erases an inherited value.

## YAML surfaces

### Crew default

```yaml
llm:
  model: deepseek-v4-flash
  response_format: json_object   # every agent of this crew now replies in JSON
```

### Agent override (wins over crew)

```yaml
agents:
  extractor:
    role: "Invoice extractor"
    llm:
      model: deepseek-v4-flash
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

The `llm_override:` block accepts the same fields as agent-level `llm:` minus the model (which doesn't make sense to swap per task in the current orchestrator).

## TypeScript / `.ork.ts` surfaces

```typescript
// Builder — agent
const extractor = agentBuilder()
    .name("extractor")
    .role("Invoice extractor")
    .llm({ provider: "deepseek", model: "deepseek-v4-flash" })
    .withResponseFormat("json_object")
    .build();

// Builder — task (overrides agent on this task only)
const task = taskBuilder()
    .description("Return invoice fields as JSON")
    .expectedOutput("JSON")
    .agent(extractor)
    .withResponseFormat("json_object")
    .build();

// Call-time override (most surgical — wins over everything else)
const res = await llm.complete(
    "Return the answer as a json object",
    { responseFormat: "json_object" }
);
```

## C# fluent surface

```csharp
var task = new CrewTaskBuilder()
    .Description("Extract as JSON")
    .ExpectedOutput("JSON")
    .WithResponseFormat(LlmResponseFormat.JsonObject())
    .Build();

// Call-time override via extension method
await provider.GenerateAsync(
    prompt,
    LlmConfigOverride.ForResponseFormat(LlmResponseFormat.JsonObject()),
    agent.LlmConfig,
    ct);
```

## The "json" keyword guard

DeepSeek's `response_format: json_object` requires the prompt (system **or** user message) to contain the word `"json"` somewhere — otherwise the API may emit an **infinite whitespace stream** until `max_tokens` is exhausted. The shared base (`OpenAICompatibleProviderBase`, so every OpenAI-compatible provider that declares `RequiresJsonKeywordInPrompt`) logs a structured `Warning` (event id `100`, `LogMissingJsonKeyword`) if it detects the situation — in substance:

```
DeepSeek response_format=json_object is set but no system/user message
contains the word 'json'. The API may emit an infinite whitespace stream
until max_tokens. Add 'json' to the prompt to be safe.
```

We **do not** mutate the prompt for you — the caller stays in control. Add `"Reply as a json object."` to the system message and the warning disappears.

## Provider support matrix

Support is **capability-driven** (`LlmProviderCapabilities.ResponseFormat`, translated
once by `OpenAICompatibleProviderBase` — see the
[provider comparison](../reference/llm-providers-comparison.md)):

| Provider | Declared capability | Notes |
|---|---|---|
| OpenAI, Azure OpenAI, Groq, Mistral, TogetherAI | `JsonSchema` | Server-side schema validation. |
| **Anthropic** | `JsonSchema` | Own dialect (`output_config`) — schema-only, no bare `json_object`. |
| **Ollama** | `JsonSchema` | Own dialect (`format`). |
| **DeepSeek** (`deepseek-v4-flash`, `deepseek-v4-pro`), Kimi, Qwen, HuggingFace, Z.AI | `JsonObject` | Well-formed JSON guaranteed; a schema is downgraded with a warning. |
| `deepseek-reasoner` (R1) | ⚠️ | May refuse `response_format` with HTTP 400. Test before production. The error surfaces as a typed `APIError` through the existing pipeline — no crash. |
| **Gemini** | `None` | A declared response format produces the structured capability warning. |

## How it travels through the orchestrator

The cascade is fused exactly once per turn (`LlmConfigResolver.Resolve`), in 3 call sites — in the agent loops and the validation coordinator (`LegacyTextAgentLoop`, `NativeToolCallingAgentLoop`, `OutputValidationCoordinator`), which `ExecutionOrchestrator` drives:

1. Legacy text-based `[TOOL_CALL]` loop — `_llmProvider.ChatAsync(prompt, effectiveConfig, …)`
2. Native tool-calling loop — `_fullProvider.ChatAsync(messages, effectiveConfig, …)`. As a side effect of this work, `BuildNativeLlmConfig` now seeds from `agent.LlmConfig` instead of `LlmConfig.Default()` — the model name and Thinking config no longer get dropped on entry to the native path.
3. Validation correction retry — `_llmProvider.ChatAsync(correctionPrompt, effectiveConfig, …)`

The 6 process strategies (Sequential, Hierarchical, Autonomous, Graph, Parallel, Consensual) delegate to `ExecutionOrchestrator.ExecuteTask` — they get the cascade for free.

`LlmBasedManager` (manager LLM in Hierarchical mode) is **not** patched because it has no `task` in scope (per plan §2.9: sites without a task pass `taskOverride: null`).

## Reference

- Value object: `Orkeon.Domain.SharedKernel.ValueObjects.LlmResponseFormat`
- Patch record: `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride`
- Fusion: `Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigResolver.Resolve`
- Wire translation: `Orkeon.Infrastructure.LLMs.Base.OpenAICompatibleProviderBase.ApplyProviderSpecificOptions` (virtual — Qwen carries the only override; DeepSeek opts in declaratively via `ResponseFormat = JsonObject`)
- YAML mapping: `Orkeon.Infrastructure.Configuration.Yaml.YamlCrewMapper.MapResponseFormat`
- Call-time extensions: `Orkeon.Infrastructure.LLMs.Extensions.LlmProviderExtensions`
- Plan / spec: the maintainers' archive (LLM-RESPONSE-FORMAT plan)
- DeepSeek API doc: https://api-docs.deepseek.com/api/create-chat-completion
