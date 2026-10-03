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
| `appsettings.gemini.local.json.example` | Google Gemini (OpenAI-compatible endpoint, `gemini-3.7-flash`). |
| `appsettings.grok.local.json.example` | Grok / x.AI (OpenAI-compatible endpoint, `grok-4.6`). |
| `appsettings.minimax.local.json.example` | MiniMax (OpenAI-compatible endpoint, `MiniMax-M2`; intl + mainland hosts). |
| `appsettings.openrouter.local.json.example` | OpenRouter (model marketplace, `vendor/model` ids, `google/gemini-3.7-flash`). |
| `appsettings.mammouth.local.json.example` | Mammouth AI (French subscription, LiteLLM proxy, bare vendor ids — `BaseUrl` first, `gemini-3.7-flash`). |
| `appsettings.glm-medium.local.json.example` | Z.AI GLM-5.2 with `Thinking.Effort = medium`. |
| `appsettings.local.json.example` | Neutral template (defaults to DeepSeek); edit `BaseUrl`/`Model`/`ApiKeyEnvVar` for any provider. |

The provider is **auto-detected from the `Llm.BaseUrl` host** — there is no `Provider`
key. `api.deepseek.com` → DeepSeek, `api.z.ai` → Z.AI GLM, `api.openai.com` → OpenAI,
`openrouter.ai` → OpenRouter, `api.mammouth.ai` → Mammouth, `.../engines/...` (Docker
Model Runner) → OpenAI-compatible, etc. The two aggregators are the case where the host
matters most: Mammouth serves the vendors' own model names, so a `Model` copied from
another profile without its `BaseUrl` talks to that vendor, not to Mammouth.

The Docker Model Runner profiles (`appsettings.json`, `appsettings.docker-model-runner.local.json.example`)
set `"Grammar": true`: its llama.cpp engine honours the GBNF `grammar` a `structured_output`
deliverable produces. Leave the key out for any other endpoint — no vendor API takes the field,
and a grammar sent there is dropped with a warning.

## Inside the `orkeon-runners` container image

The image bakes a **container-appropriate default** over its copy of
`appsettings.json`: the BaseUrl targets `host.docker.internal:12434` (Docker
Model Runner on the host) instead of `localhost`, which is unreachable from a
container. This file on disk is unchanged — it stays correct for source runs.
Ready-made in-container profiles also ship at `/etc/orkeon/profiles`
(`host-dmr`, `host-ollama`, `openai`, plus `local` in the embedded-model
variant); select one with `-e ORKEON_LLM_PROFILE=<name>` and see
[Three ways to run Orkeon §3](../../docs/getting-started/three-ways-to-run-orkeon.md#3-container)
and the [Local models guide](../../docs/guides/local-models.md).

## Using a profile

1. Copy the template, dropping the `.example` suffix:

   ```bash
   cp appsettings.deepseek.local.json.example appsettings.deepseek.local.json
   ```

2. Put your key in the environment variable the copy names — `ApiKeyEnvVar`, here
   `DEEPSEEK_API_KEY`. Every run reads it, and the key never goes in the file:

   ```bash
   export DEEPSEEK_API_KEY=sk-...
   ```

   `ORKEON_Llm__ApiKey`, when set, wins over the variable the file names. An `ApiKey`
   written `${…}` — the shape these templates used to carry — refuses the start: nothing
   ever expanded it, and the text went out as the key.

3. Point a runner at it explicitly:

   ```bash
   ./run-example.sh <example> --settings examples/appsettings/appsettings.deepseek.local.json
   ```

## Memory providers (`memoryProvider:` in a crew)

A crew's `memoryProvider:` names a **type** — `"Redis"`, `"SQLite"`, `"ChromaDb"`, … — and
nothing else: where a crew with `memory: true` keeps what it remembers (without `memory: true`,
a `memoryProvider:` is refused at load). Its connection comes from the settings, one section per
provider; add the one your example needs to the profile you pass with `--settings`:

```json
{
  "Orkeon": {
    "Redis": { "ConnectionString": "localhost:6379" },
    "Sqlite": { "ConnectionString": "Data Source=/output/crew-memory.db" }
  }
}
```

- **Redis** (examples 86, 89, 92, 94, 95, 96, 101): without the section the provider connects
  to `localhost:6379`; it connects at the crew's kickoff, which checks the memory before the first
  LLM call and refuses the run when the server cannot be reached, so start a server first
  (`docker run -d -p 6379:6379 redis:7`). Add `,password=…` to the connection string when yours
  needs one, or export `ORKEON_Orkeon__Redis__ConnectionString` instead of writing it down.
- **SQLite** (examples 87, 88, 90, 91, 93, 97, 98, 99, 100): without the section the database is
  in memory and lost when the run ends. A file `Data Source` is a **virtual** path and must lie on
  a writable mount — e.g. `/output`, mounted with `--mount ./out:/output:rw`.

The other sections (`Orkeon:ChromaDb`, `Orkeon:Pinecone`, `Orkeon:LanceDb`) are described in
the [configuration reference](../../docs/reference/configuration.md).

A crew with `memory: true` embeds what it stores and what it recalls with the host's embedding
provider — the local model, which the runners register unless `RaggableTree:Enabled` is `false`
(then set `Orkeon:Embeddings`). A vector store's dimension must be the embedder's: the local model
gives 384, and LanceDB's table defaults to 1,536. How much a crew recalls before each task is the
`Orkeon:CrewMemory` section (`RecallLimit`, `MinScore`, `MaxChars`) — see the
[memory system](../../docs/architecture/memory-system.md#what-a-crew-recalls).

## Secrets stay local

`.local.json` copies are **git-ignored** by the root `.gitignore` pattern
`**/appsettings*local*.json`, so your real keys never get committed. Only the
`.example` templates (placeholders only) and the committed `appsettings.json`
(no key) are tracked. Never put a real key in a committable file.
