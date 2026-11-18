# LLM Response Format — Demo

Smallest possible demo of the `response_format` cascade shipped in feature
`LLM-RESPONSE-FORMAT-PLAN`. Shows the same crew expressed two ways:

| Format | File | What it demonstrates |
|---|---|---|
| YAML | `crew.yaml` | Crew default `response_format: json_object` + per-task `llm_override` to drop temperature to 0.0 on extraction |
| TypeScript (`.ork.ts`) | `crew.ork.ts` | Same crew expressed via `agentBuilder()` / `taskBuilder()` with `.withResponseFormat("json_object")` |

Both expect the same effective DeepSeek payload:

```json
{
  "model": "deepseek-flash",
  "messages": [ … ],
  "response_format": { "type": "json_object" }
}
```

## Run

This demo has no `Program.cs`. Run `crew.yaml` with
`orkeon run examples/09-experimental/llm-response-format/crew.yaml`. `crew.ork.ts`
builds its crew without handing it to the host: to run it with `orkeon run`, end
the script with `globalThis.crew = crewBuilder()…build();`. The point is the YAML/TS syntax and the effective payload that hits
the wire, not a runnable trading bot.

## Caveat — the "json" keyword

DeepSeek requires the prompt to mention `"json"` somewhere (system or user
message). Both files put it in the task description; if you strip it, the
DeepSeek provider logs a `Warning` (event id `100`) telling you so but
does **not** modify your prompt.

## Reference

- Full guide: `docs/guides/llm-response-format.md`
- Reference: [LLM response format](../../../docs/architecture/llm-providers.md)
