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
  "model": "deepseek-v4-flash",
  "messages": [ … ],
  "response_format": { "type": "json_object" }
}
```

## Run

This demo is design-only — no `Program.cs`. Wire either file into your
standard runner (`OrkeonScriptingRunner` for the `.ork.ts`, or the YAML
crew loader for `crew.yaml`) the same way you'd wire any other example
crew. The point is the YAML/TS syntax and the effective payload that hits
the wire, not a runnable trading bot.

## Caveat — the "json" keyword

DeepSeek requires the prompt to mention `"json"` somewhere (system or user
message). Both files put it in the task description; if you strip it, the
DeepSeek provider logs a `Warning` (event id `100`) telling you so but
does **not** modify your prompt.

## Reference

- Full guide: `docs/guides/llm-response-format.md`
- Plan / spec: `project/tasks/LLM-RESPONSE-FORMAT-PLAN.md`
