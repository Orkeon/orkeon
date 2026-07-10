# examples/appsettings — canonical settings profiles

Runner LLM/rate-limit settings for the examples live here. When a runner starts,
`RunnerSettings.ResolveSettingsPath` walks up from the example's config directory
and picks up `appsettings/appsettings.json` unless you pass an explicit `--settings`
or drop an `appsettings.json` next to the crew's `config.yaml`.

## Files

| File | Purpose |
|------|---------|
| `appsettings.json` | Committed default. Docker Model Runner on `localhost:12434` (no API key). Used automatically when nothing else is specified. |
| `appsettings.docker-model-runner.local.json.example` | Local Docker Model Runner profile (no key). |
| `appsettings.openai.local.json.example` | OpenAI (`gpt-4o`). |
| `appsettings.deepseek.local.json.example` | DeepSeek V4 Flash. |
| `appsettings.glm.local.json.example` | Z.AI GLM-5.2. |
| `appsettings.glm-medium.local.json.example` | Z.AI GLM-5.2 with `Thinking.Effort = medium`. |
| `appsettings.local.json.example` | Neutral template (defaults to DeepSeek); edit `BaseUrl`/`Model`/`ApiKey` for any provider. |

The provider is **auto-detected from the `Llm.BaseUrl` host** — there is no `Provider`
key. `api.deepseek.com` → DeepSeek, `api.z.ai` → Z.AI GLM, `api.openai.com` → OpenAI,
`.../engines/...` (Docker Model Runner) → OpenAI-compatible, etc.

## Using a profile

1. Copy the template, dropping the `.example` suffix:

   ```bash
   cp appsettings.deepseek.local.json.example appsettings.deepseek.local.json
   ```

2. Put your key in the copied file (replace the `${DEEPSEEK_API_KEY}` placeholder —
   it is **not** expanded automatically), or leave the placeholder and export the key
   at runtime instead:

   ```bash
   export ORKEON_Llm__ApiKey=sk-...
   ```

3. Point a runner at it explicitly:

   ```bash
   ./run-example.sh <example> --settings examples/appsettings/appsettings.deepseek.local.json
   ```

## Secrets stay local

`.local.json` copies are **git-ignored** by the root `.gitignore` pattern
`**/appsettings*local*.json`, so your real keys never get committed. Only the
`.example` templates (placeholders only) and the committed `appsettings.json`
(no key) are tracked. Never put a real key in a committable file.
