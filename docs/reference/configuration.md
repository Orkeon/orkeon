> 🇫🇷 [Version française](../fr/reference/configuration.md)

# Configuration reference (`appsettings.json`)

This page is the single map of the settings Orkeon reads: where they come from, when one is
refused, then **every key, by category** — its type, its default, what it means and which host
reads it ([Find a setting](#find-a-setting)) —, and last the
[environment variables](#environment-variables) Orkeon reads. A section that only takes effect after a dedicated
registration says so — see [Opt-in subsystems](./opt-in-subsystems.md) for each one's details.

## Where settings are read from

Every Orkeon host — the runner hosts (`RunnerHost.Build`, used by `orkeon run`, the YAML runners
and `orkeon-host`) and the REPL — composes the same layers, and those alone
(`RunnerSettings.ComposeSources`). Under everything lie the **environment variables without a
prefix**: the standard `OTEL_EXPORTER_OTLP_ENDPOINT` a collector or a .NET Aspire AppHost sets
reaches the OpenTelemetry exporter through them ([telemetry](./hosting.md#telemetry)), and a bare
`Llm__Model` is read too, below every layer that follows:

1. **One resolved `appsettings.json`** — resolution chain (`RunnerSettings.ResolveSettingsPath`):
   explicit `--settings <path>` → `appsettings.json` next to the crew config →
   `appsettings/appsettings.json` walking up the directory tree (`examples/appsettings/appsettings.json`
   in this repository; `_shared/appsettings.json` is a deprecated fallback) → the global
   per-user config written by `orkeon init`. No file found ⇒ environment variables only.
2. **Environment variables with the `ORKEON_` prefix** (`AddEnvironmentVariables("ORKEON_")`,
   for every host and for `orkeon doctor`). Standard .NET mapping: `__` separates levels —
   `ORKEON_Llm__ApiKey` overrides `Llm:ApiKey`, `ORKEON_Orkeon__Rag__Profile` overrides
   `Orkeon:Rag:Profile`. The prefix and the keys are compared without case, and `:` separates
   levels as well as `__`: `ORKEON_LLM__APIKEY`, `orkeon_llm__apikey` and `ORKEON_Llm:ApiKey` are
   all `Llm:ApiKey`. Under Linux and macOS two spellings of a setting are two variables, and a run
   reads either one — never set the same setting twice under two spellings. Env vars are added
   **after** the file, so they win. A key need not
   come from either layer: the file can name the variable that holds it (`ApiKeyEnvVar`,
   [below](#the-api-key-apikey-apikeyenvvar)), read when neither resolves an `ApiKey`.
3. **CLI mount overrides** — each `--mount` argument becomes an in-memory
   `Orkeon:FileSystem:Mounts:<i>` entry (highest precedence), placed **by virtual root**:
   a `--mount` on a root the declared array (the layers above) already holds is written at the
   first entry's index and **replaces every declared entry of that root** for the run; a
   `--mount` on a new root is appended after the highest declared index. The runner's own
   `/crew` (or `/script`) mount and the `InternalMounts` are always appended. A declared
   entry may carry an **id** — the 26-character ULID before a `|`,
   `01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data:/output:rw` (VFS-90) — and several entries may
   declare one root when each carries one: `--mount-id <ulid>`, else the crew's `mounts:`
   block, selects the entry the run keeps, and every other entry of that root is
   **withdrawn** — its key is written to `null` at its own index, its base path is not
   whitelisted and its folder is not probed.

**Nothing else is read.** The default .NET host also laid, under the resolved file, the
`appsettings.json` and `appsettings.{Environment}.json` of the **current directory** — since .NET 10,
`<binary>.settings.json` and its environment twin too (`orkeon.settings.json`) — and, in
`Development`, its user secrets: a file of another project — the folder a terminal happened to be
in — added the profiles, MCP servers or mounts it declared to the run, and no diagnostic saw them.
They are not read any more (GAP-36). `orkeon-host` reads `./appsettings.json` as **its** settings
file, when `--settings` names none, never under the one it names; the REPL reads its `--settings`
files, else the global file, then its command line, last ([CLI](./cli.md#orkeon-repl--the-separate-interactive-console)).

The same `ORKEON_` prefix also feeds `EnvironmentSecretProvider` (secret lookup, e.g.
`OPENAI_API_KEY` → `ORKEON_OPENAI_API_KEY`; the `web_search` tool's Tavily key is
`ORKEON_TAVILY_API_KEY`). The second stop of that chain is the `Secrets` section of the
file (`Secrets:TAVILY_API_KEY`), the environment variable winning when both exist. The
variables a binary reads by their own name — they carry no setting — are listed with the
others in [Environment variables](#environment-variables).

**What this means in practice.** The file is the durable, shared base; everything laid
over it is an ephemeral layer that lives and dies with one process. `orkeon doctor`'s
`llm-config` check composes exactly like a runner (same resolution chain, same layers: the
variables without a prefix, the file, the `ORKEON_` overlay), so its verdict answers: *what
would a run launched from this shell use, absent any per-launch overlay?* Orkeon Studio's named model profiles ride layer 2:
the profile elected as default is written into the file's `Llm` section whole — endpoint,
model, timeout, thinking switch and the variable that holds its key (`ApiKeyEnvVar`), never
the key — so a manual terminal `orkeon run` or a scheduled team follows the same election,
its 600 s and its key included (that is what `llm-config` reflects), while a team that
elected a different profile receives it as `ORKEON_Llm__*` variables on its own launch
only — every field the profile models, blank where it sets none, so nothing of the default,
its key least of all, reaches the team's endpoint; `llm-config` cannot see those, because
they exist nowhere until that launch starts. Every profile is also written into the file's
`Llm:Profiles`, naming the variable that holds its key and never the key, as a host profile
a crew can name ([below](#studio-writes-this-section)), and every launch from Studio carries
them all, keys included, as `ORKEON_Llm__Profiles__<id>__*`. No file is ever generated: the
composition is in-memory.

**A file to start from.** Every installation carries `appsettings.sample.json` at the root of
what its channel installs, beside `VERSION`: every key a shipped binary reads, by category, at
its default — produced from the settings catalogue, the one `orkeon settings` lists. No binary
loads it. Copied as it is in place of the settings file, it changes nothing: each key it writes
holds the value that key has when it is left out, and what cannot be written that way — a key
without a default, a secret, a list, an entry under a name you choose, a key a RAG profile sets,
the whole `Llm` section — is shown as a comment to uncomment. It is JSON with `//` comments and a
comma after every member, which the settings readers take (the runners and Orkeon Studio); a
strict JSON parser does not.

## When a setting is refused

Every shipped host — `orkeon run` in every form (`--validate`, `--list-tools`, `mcp serve`, the
forge, `rag`, `email`), `orkeon-host` and `orkeon-repl` — judges every setting it reads **at its
start**, whether the run uses it or not (GAP-40): before the crew loads, before any model is called,
before a warning is printed or the telemetry starts. A refusal is one line naming the key — exit code
`1` for `orkeon` and `orkeon-repl`, `78` for `orkeon-host` — and `orkeon doctor` reports the same
refusals on the same file, one `runner-settings` row each. What is refused:

- **A value** the configuration binder cannot convert (`"Orkeon:Guardian:Enabled": "oui"`), or that
  a rule of its section refuses (`Orkeon:Rag:Retrieval:TopK` at `0`, an `Orkeon:Sqlite:TableName`
  SQLite cannot be given). The `Llm` section is read as strictly as its profiles: a
  `TimeoutSeconds` of `"600s"` is refused, where it used to run on 30 s, `Thinking:Enabled` and
  `Grammar` are `true` or `false`, and a `Temperature` is a finite number — `NaN`, `Infinity` or
  `1e400` (which reads as infinity) parsed, then failed every request, since JSON writes no such
  number. So is every number a section reads as a float or a double (`Orkeon:Rag:Generation:Temperature`,
  `Orkeon:CrewMemory:MinScore`, …), which the binder takes as `NaN` or infinity without a word. A
  `Logging` level is one of `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`,
  any case.
- **A key** no section carries, against the shape its readers declare — the readers of one section
  together, a sub-section another reader declares included: `Orkeon:Guardian:Enabeld`,
  `Llm:Provider` (nothing reads it: the provider follows from `BaseUrl` and the model). A key GAP-08
  removed — `Memory:ConnectionString`, `Orkeon:Rag:ConnectionString`, `Orkeon:Rag:ProviderOptions`,
  `Orkeon:Pinecone:Environment` — is refused with its migration. The refusal of an unknown key ends on the command that lists the
  keys of its section — `` `orkeon settings Orkeon:Guardian` lists its keys ``. A section name under `Orkeon:` and
  under the groups `Orkeon:Cli`, `Orkeon:Tools`, `Orkeon:Scripting`, `Orkeon:Security` and `Security`
  must be one Orkeon reads (`SettingsSections`); the refusal proposes the closest
  (`Orkeon:Guardain` → `Orkeon:Guardian`). Keys and section names are compared without case, as the
  configuration reads them: `ORKEON_LLM__APIKEY` is `Llm:ApiKey`, and `"llm": { "apikey": … }` too.
- **A name** a component is chosen by, against the names this host knows, listed in the refusal:
  `Memory:Provider` and `Orkeon:Rag:Provider`, with what the named provider needs
  (`Orkeon:LanceDb:Endpoint` for `lancedb`; `Orkeon:Pinecone:ApiKey` and `IndexName`, or `Host`, for
  `pinecone`); `Orkeon:Rag:Profile`; the `Rerank:Kind`, `QueryTransform:Mode`, `Context:Ordering`
  and `Ingestion:DefaultChunkingStrategy` of the effective RAG options — profile plus overrides, so
  a host without the ONNX reranker refuses `balanced` and `quality`, naming `onnx`;
  `Orkeon:Rag:QueryRouting:Classifier`; `Orkeon:Embeddings:Provider`;
  `RaggableTree:Embedding:Provider`. A YAML crew's `memoryProvider:` is checked when the crew loads.

**What stays open**: the configuration root — it also holds the environment variables without a
prefix, and nothing tells a misspelt section there from a variable of the machine —, `Logging`
beyond its levels, `Secrets`, the keys of a dictionary (`Llm:Profiles:<name>`, `MCP:Servers:<id>`,
`…:Env:<VAR>`, `Orkeon:Consensus:RoleWeights:<role>`, `Orkeon:Tools:Email:Accounts:<name>`) and the
indices of a list, and the keys of a section this host does not read — `orkeon run` leaves
`Orkeon:Host` to the daemon, which judges it; a section **no** shipped binary reads is
reported, not judged ([below](#when-a-setting-is-only-reported)). **The e-mail accounts are the exception**: an account
holding a value or a key that cannot be read is set aside and reported when a call or
`orkeon email` names it, and the others keep working ([e-mail](../guides/email.md)).

A C# host that starts (`StartAsync`, a .NET Aspire AppHost) refuses the values and the names its
Orkeon registrations bind — each is registered with `ValidateOnStart` —; the keys of its sections
stay its own. A container built by hand and never started judges nothing until an option is read.

### When a setting is only reported

A section Orkeon knows and **no shipped binary reads** is not refused: a settings file shared
between `orkeon run` and a host written in C# is a legitimate one. It is not read in silence
either — the run would start without the limit, the budget or the screening the file describes.
Each shipped host says it at its start, once per section and per process, on stderr (the standard
output stays the run's) and on its logger, then starts; the exit code is the run's:

```text
WARNING: ToolRateLimiting is read by no component of this host: a C# host reads it through AddOrkeonToolRateLimiting(). The calls to the model are limited by RateLimiting, which this host reads.
```

`orkeon doctor` reports the same sentence as a `warn` row of `runner-settings`, one per section,
and still exits `0`. The sections are those the settings catalogue marks as read by no shipped
binary: `ToolRateLimiting`, `TokenBudget`, `Orkeon:Dlp`, `Orkeon:Monitoring`,
`Orkeon:CognitiveMemory`, `Orkeon:MultiModal`, `Plugins`, `Evaluation`, `Orkeon:VectorSearch`,
`Orkeon:Checkpointing` and `Orkeon:ExecutionState:Persistence`
([opt-in subsystems](./opt-in-subsystems.md)). The list is computed, not written: a section a
shipped binary starts to read leaves it. Not reported: a section another shipped binary reads
(`Orkeon:Host` in a file `orkeon run` reads), a root section Orkeon does not know, and a section
the host itself reads because its C# composition registered what reads it. The `ORKEON_`
environment writes a section as the file does:
`ORKEON_ToolRateLimiting__GlobalToolRequestsPerMinute` gets the same line.

## Find a setting

Every setting Orkeon reads is listed below by what it is for: eleven categories, one part of
this page each. In a category, each section has its own sub-part — what the section does, who
reads it, then **one row per key**: its type, its default, the values it accepts when they are a
closed list, and what it means.

<!-- settings-index -->
| Category | Sections | Keys |
|---|---|---|
| [Models](#models) | [`Evaluation`](#evaluation), [`Llm`](#llm), [`LlmLogging`](#llmlogging), [`Orkeon:CostTracking`](#orkeoncosttracking), [`Orkeon:TokenCounter`](#orkeontokencounter) | 44 |
| [Rate and budgets](#rate-and-budgets) | [`RateLimiting`](#ratelimiting), [`TokenBudget`](#tokenbudget), [`ToolRateLimiting`](#toolratelimiting) | 11 |
| [Memory and vectors](#memory-and-vectors) | [`Memory`](#memory), [`Orkeon:ChromaDb`](#orkeonchromadb), [`Orkeon:CognitiveMemory`](#orkeoncognitivememory), [`Orkeon:CrewMemory`](#orkeoncrewmemory), [`Orkeon:EmbeddingCache`](#orkeonembeddingcache), [`Orkeon:Embeddings`](#orkeonembeddings), [`Orkeon:Encryption`](#orkeonencryption), [`Orkeon:LanceDb`](#orkeonlancedb), [`Orkeon:Pinecone`](#orkeonpinecone), [`Orkeon:Redis`](#orkeonredis), [`Orkeon:Sqlite`](#orkeonsqlite), [`Orkeon:VectorSearch`](#orkeonvectorsearch) | 58 |
| [RAG](#rag) | [`Orkeon:Rag`](#orkeonrag), [`Orkeon:Rag:Ingestion`](#orkeonragingestion), [`Orkeon:Rag:QueryRouting`](#orkeonragqueryrouting), [`Orkeon:Rag:Retrieval:Hybrid`](#orkeonragretrievalhybrid), [`Orkeon:Rag:WebFallback`](#orkeonragwebfallback) | 34 |
| [Files and sandbox](#files-and-sandbox) | [`Orkeon:CodeSandbox`](#orkeoncodesandbox), [`Orkeon:CodeSandbox:Docker`](#orkeoncodesandboxdocker), [`Orkeon:FileSystem`](#orkeonfilesystem), [`Orkeon:Sandbox`](#orkeonsandbox), [`PathSecurity`](#pathsecurity) | 30 |
| [Security](#security) | [`Orkeon:Dlp`](#orkeondlp), [`Orkeon:Guardian`](#orkeonguardian), [`Orkeon:Security:PermissionGate`](#orkeonsecuritypermissiongate), [`Secrets`](#secrets), [`Security:Audit`](#securityaudit), [`Security:Prompt`](#securityprompt), [`Security:ToolResults`](#securitytoolresults), [`Security:Url`](#securityurl), [`Security:Vault`](#securityvault) | 31 |
| [Tools](#tools) | [`BRAVE_API_KEY`](#brave_api_key), [`MCP`](#mcp), [`MCP:Server`](#mcpserver), [`Orkeon:MultiModal`](#orkeonmultimodal), [`Orkeon:Tools:Email`](#orkeontoolsemail), [`Orkeon:Tools:Shell`](#orkeontoolsshell), [`Plugins`](#plugins), [`RaggableTree`](#raggabletree) | 54 |
| [Orchestration and persistence](#orchestration-and-persistence) | [`Orkeon:Checkpointing`](#orkeoncheckpointing), [`Orkeon:Consensus`](#orkeonconsensus), [`Orkeon:CrewFactory`](#orkeoncrewfactory), [`Orkeon:ExecutionState:Persistence`](#orkeonexecutionstatepersistence) | 16 |
| [Scripts and console](#scripts-and-console) | [`Orkeon:Cli:ConsoleStreaming`](#orkeoncliconsolestreaming), [`Orkeon:Cli:ScriptCommands`](#orkeoncliscriptcommands), [`Orkeon:Cli:ScriptHost`](#orkeoncliscripthost), [`Orkeon:Cli:Session`](#orkeonclisession), [`Orkeon:Cli:Tui`](#orkeonclitui), [`Orkeon:Scripting:Limits`](#orkeonscriptinglimits), [`Orkeon:Scripting:Toolchain`](#orkeonscriptingtoolchain) | 21 |
| [Service host and A2A](#service-host-and-a2a) | [`A2A`](#a2a), [`A2A:Security`](#a2asecurity), [`A2A:Security:AzureAD`](#a2asecurityazuread), [`A2A:Security:Oidc`](#a2asecurityoidc), [`Orkeon:Host`](#orkeonhost), [`Orkeon:Host:Discord`](#orkeonhostdiscord) | 46 |
| [Observability](#observability) | [`Logging`](#logging), [`Orkeon:Monitoring`](#orkeonmonitoring), [`Telemetry`](#telemetry) | 8 |

67 sections, 353 keys. Read by no shipped binary, only by a host written in C# (11): [`Evaluation`](#evaluation), [`TokenBudget`](#tokenbudget), [`ToolRateLimiting`](#toolratelimiting), [`Orkeon:CognitiveMemory`](#orkeoncognitivememory), [`Orkeon:VectorSearch`](#orkeonvectorsearch), [`Orkeon:Dlp`](#orkeondlp), [`Orkeon:MultiModal`](#orkeonmultimodal), [`Plugins`](#plugins), [`Orkeon:Checkpointing`](#orkeoncheckpointing), [`Orkeon:ExecutionState:Persistence`](#orkeonexecutionstatepersistence), [`Orkeon:Monitoring`](#orkeonmonitoring).
<!-- /settings-index -->

How to read a table:

- **Read by** names the shipped binaries that read the section: `orkeon` is the CLI — `orkeon run`
  in every form and every verb that builds the runner host (`--validate`, `--list-tools`,
  `mcp serve`, the forge, `rag`, `email`) —, `orkeon-host` the service host, `orkeon-repl` the
  interactive console. A section **no shipped binary reads** says so, and names the registration a
  host written in C# reads it through: written in the settings file of `orkeon run`, it configures
  nothing there ([Opt-in subsystems](./opt-in-subsystems.md) has each one's details).
- **Key** is written from its section: `QueueLimit` under `RateLimiting` is the setting
  `RateLimiting:QueueLimit` — `"RateLimiting": { "QueueLimit": 5 }` in the file,
  `ORKEON_RateLimiting__QueueLimit` in the environment. `<name>` stands for a name you choose (a
  profile, an MCP server, an e-mail account, a role) and `<i>` for an index in a list.
- **Type** is `string`, `integer`, `number`, `boolean`, `duration` (`hh:mm:ss`, `d.hh:mm:ss` from
  a day up), `uri`, `date-time`, `enum` (its names are under **Values**), `list of …`, or `any` for
  a value its reader takes as it is written. `secret` marks a key whose value is a secret: no
  table shows one, and the keys that name a variable instead (`ApiKeyEnvVar`, `PasswordEnvVar`, …)
  keep it out of the file.
- **Default** is the value the key has when nothing sets it; `—` means it has none, and the
  meaning then says what an unset key does. A default that is no constant is said in words.
- **Meaning** is the sentence the code carries on the property the key is read into.

The tables are produced from the code — the catalogue of settings a host refuses an unknown key
with ([above](#when-a-setting-is-refused)) — and a test holds them to it. **The same inventory is
in the tool, offline**: `orkeon settings` lists the categories, `orkeon settings RateLimiting` the
keys of a section, `orkeon settings rate` whatever a word matches, and `orkeon settings --json`
hands the whole catalogue to a program ([CLI](./cli.md#orkeon-settings)).

The options of a crew file — `maxRpm`, `llm: { profile: … }`, `memoryProvider:`, `rag:`,
`mounts:` — are not host settings: they are described in the
[YAML schema](../architecture/yaml-schema.md). This page names one where it bounds or selects a
host setting.

The environment variables have their own part, after the categories: the ones that carry a
setting, the ones a binary reads by their name — `ORKEON_DEBUG`, `TUI_DRIVER` — and the ones a
setting names ([Environment variables](#environment-variables)).

## Models

The model a host calls, how its exchanges are logged, and what its tokens cost. The caps on the
calls themselves are under [Rate and budgets](#rate-and-budgets).

<a id="llm-provider-llm-section"></a>

### `Llm`

The `Llm` section is read by `RunnerHost.RegisterLlmProvider` and turned into an
`ILlmProvider` via `ILlmProviderFactory`; the REPL reads it with the same reader
(`LlmSettings.ReadDefault`). **The provider is inferred automatically**, in
order: base-URL host patterns (e.g. `deepseek.com` → DeepSeek, `api.x.ai` → Grok,
`/engines/` → Docker Model Runner/OpenAI-compatible), then model-name patterns, then
API-key shape; default `openai`. A section without `Model` runs on that provider's own default
model (the *Default (code)* column of the
[provider comparison](llm-providers-comparison.md#defaults-and-newer-models--catalogue-review-of-2026-09-19)),
never on OpenAI's pinned onto another vendor. Without an `Llm` section the
runtime degrades to the echo provider and warns once. See
[LLM providers](../architecture/llm-providers.md); templates live in
`examples/appsettings/*.json.example`.

**This section is the base of every call** to its provider:
a component that passes a configuration of its own — the planner, the cognitive memory, the
context-window and RaggableTree summarizers, the agent loops outside the chat client, a chat
client registered without one — completes the section's rather than replacing it. What it leaves
unset is the section's (the key, `BaseUrl`, `TimeoutSeconds`, `Thinking`, `MaxTokens`…); what it
sets wins (the planner's temperature 0.3, an analysis's output cap). A profile's provider works
the same way with its own keys.

<!-- settings:Llm -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ApiKey` | string, secret | — | The API key, in clear text. Prefer `ApiKeyEnvVar`: a key written here is in the settings file. |
| `ApiKeyEnvVar` | string | — | The name of the environment variable that holds the API key, read when `ApiKey` resolves none: the process environment, then the Windows user scope. |
| `AvailableModels` | list of string | `[]` | The models a scripted `/model` may offer: a list, or one comma-separated value. |
| `BaseUrl` | string | — | The address of the provider's endpoint, `http://` or `https://`. Left out, the address of the provider the model's name or the key points to. |
| `Grammar` | boolean | `false` | Whether the endpoint honours a GBNF grammar — a llama.cpp-compatible server, such as Docker Model Runner. Elsewhere a grammar is dropped with a warning. |
| `MaxRetries` | integer | `10` | How many times a call that fails on a passing error is tried again; `0` never retries. |
| `MaxTokens` | integer | — | The most tokens an answer may hold: a pin. Left out, a request carries the documented maximum of its model, and 4096 for a model the catalogue does not know. |
| `Model` | string | — | The model's name. Left out, the provider runs its own default model. |
| `Profiles:<name>:ApiKey` | string, secret | — | The API key, in clear text. Prefer `ApiKeyEnvVar`: a key written here is in the settings file. |
| `Profiles:<name>:ApiKeyEnvVar` | string | — | The name of the environment variable that holds the API key, read when `ApiKey` resolves none: the process environment, then the Windows user scope. |
| `Profiles:<name>:BaseUrl` | string | — | The address of the provider's endpoint, `http://` or `https://`. Left out, the address of the provider the model's name or the key points to. |
| `Profiles:<name>:Grammar` | boolean | `false` | Whether the endpoint honours a GBNF grammar — a llama.cpp-compatible server, such as Docker Model Runner. Elsewhere a grammar is dropped with a warning. |
| `Profiles:<name>:MaxRetries` | integer | `10` | How many times a call that fails on a passing error is tried again; `0` never retries. |
| `Profiles:<name>:MaxTokens` | integer | — | The most tokens an answer may hold: a pin. Left out, a request carries the documented maximum of its model, and 4096 for a model the catalogue does not know. |
| `Profiles:<name>:Model` | string | — | The model's name. Left out, the provider runs its own default model. |
| `Profiles:<name>:StreamIdleSeconds` | integer | — | The longest silence a streamed answer may hold between two of its chunks, in seconds. Left out, nothing bounds it: `TimeoutSeconds` alone bounds the whole call, streamed or not. A model that thinks before it writes may stay silent for a while: set it only above that silence, or turn its thinking off. |
| `Profiles:<name>:Temperature` | number | — | The sampling temperature. Left out, none is sent and the model applies its own. |
| `Profiles:<name>:Thinking:Effort` | string | — | How hard it thinks, in the provider's own words (`low`, `medium`, `high`…). Left out, the provider's own. |
| `Profiles:<name>:Thinking:Enabled` | boolean | — | Whether the model thinks before it answers. Left out, the provider's own behaviour. |
| `Profiles:<name>:TimeoutSeconds` | integer | `30` | How long one call may take, in seconds. Too short at its default for a model that thinks before it answers: write 600 for one, or turn its thinking off. |
| `StreamIdleSeconds` | integer | — | The longest silence a streamed answer may hold between two of its chunks, in seconds. Left out, nothing bounds it: `TimeoutSeconds` alone bounds the whole call, streamed or not. A model that thinks before it writes may stay silent for a while: set it only above that silence, or turn its thinking off. |
| `Temperature` | number | — | The sampling temperature. Left out, none is sent and the model applies its own. |
| `Thinking:Effort` | string | — | How hard it thinks, in the provider's own words (`low`, `medium`, `high`…). Left out, the provider's own. |
| `Thinking:Enabled` | boolean | — | Whether the model thinks before it answers. Left out, the provider's own behaviour. |
| `TimeoutSeconds` | integer | `30` | How long one call may take, in seconds. Too short at its default for a model that thinks before it answers: write 600 for one, or turn its thinking off. |
<!-- /settings -->

- **The key.** `ApiKeyEnvVar` names the environment variable that holds the key —
  [below](#the-api-key-apikey-apikeyenvvar); the variable each provider's key conventionally lives
  in, and the names confused with it, are in the
  [provider comparison](llm-providers-comparison.md#api-keys-the-variable-per-provider). `ApiKey`
  is the key in clear text — discouraged.
- **`Temperature`.** Left out, none is sent and the model applies its own — often 1; write `0.7`
  to keep the engine's former default (GAP-36).
- **`Thinking:Enabled`, `Thinking:Effort`** are for thinking-capable providers.
- **`Profiles:<name>`** holds the named profiles a crew picks per agent or per task, each with
  the same keys as the section itself ([below](#named-profiles-llmprofiles)); Orkeon Studio writes
  one per model setting, naming the variable holding its key, never the key.
- **`AvailableModels`** is the model list a scripted `/model` REPL command can offer (string
  array, or one comma-separated string), read by `AddOrkeonSessionTools(configuration)`.

#### The timeout, and the models that think

`TimeoutSeconds` defaults to 30 s,
too short for a model that thinks before it answers (Kimi K2.6, DeepSeek V4 and GLM do so by
default): set 600 s, or turn thinking off with `Thinking:Enabled = false`. A call that hits the
timeout is retried once, then fails its task with a message naming the setting — it is never
reported as an empty answer (LLM-11).

The timeout bounds the whole call, streamed or not: a streamed call (every Studio or `--events`
run) used to be bounded only until its headers arrived, and a model that thought for minutes
before its first token hung the run (LLM-12). `StreamIdleSeconds` adds a second bound, unset by
default: the longest silence between two streamed chunks. A model that thinks before it writes
may stay silent for a while, so set it above that silence, or turn thinking off. Either bound
elapsing is a failed call whose message names the setting — never an empty answer.

#### `MaxTokens` is a pin

`MaxTokens` is a **pin**: left
out, the request carries the model's **documented maximum output** from the
`LlmModelOutputLimits` catalogue (128K on `gpt-5.6-sol` and the Claude 5 generation, 384K on
`deepseek-flash`, 131 072 on the GLM-5 and Qwen 3.7/3.8 families, 65 536 on Gemini 3.x Flash —
see the [output caps table](llm-providers-comparison.md#output-caps--the-documented-maximum-per-model-llm-10)),
no cap at all where the vendor documents none (Mistral, a local Ollama), and **4096 only for a
model the catalogue does not know** — the value the engine used to send for every model, which
a reasoning model spends thinking before it writes a word and answers empty (an empty final
answer fails the task rather than passing for a completed one). Pin it when the model is not in
the catalogue or when you want a tighter cap; a Studio model profile pins it as
`ORKEON_Llm__MaxTokens`, and the profile editor says what an empty field means for the chosen
model. A catalogue cap the endpoint refuses is retried once without the field, with a warning
naming the model.

#### `Grammar` is for a llama.cpp-compatible server

`Grammar` (default `false`):
set it to `true` only when `BaseUrl` points at a llama.cpp-compatible server (Docker Model
Runner, `llama-server`) — the one kind of endpoint that honours the GBNF `grammar` field a
`structured_output` deliverable produces; elsewhere the grammar is dropped with a warning naming
the key ([provider comparison](llm-providers-comparison.md)).

### The API key (`ApiKey`, `ApiKeyEnvVar`)

A section never has to hold its key. `ApiKeyEnvVar` names the environment variable that does —
the name, never the key — in the `Llm` section and in every profile:
`"ApiKeyEnvVar": "DEEPSEEK_API_KEY"`. A run takes, in this order:

1. **A key the configuration resolves**: `ApiKey` in the file, `ORKEON_Llm__ApiKey`
   (`ORKEON_Llm__Profiles__<id>__ApiKey` for a profile), or what an Orkeon Studio launch lays over
   its child. An existing installation keeps the key it had, and the environment still wins over
   the file.
2. **Otherwise the variable `ApiKeyEnvVar` names**, read in the process environment, then — on
   Windows — in the user's persistent scope (`HKCU\Environment`), where Orkeon Studio remembers a
   key: a terminal opened before the key was remembered, or a scheduled team, finds it all the
   same. The value is read, never copied into the process, so what the run starts — a shell tool,
   a stdio MCP server, the code sandbox — inherits no key it did not have. A user scope that cannot
   be read (the registry refused, a virtual service account) counts as an absent variable. Linux
   and macOS have no user scope: the process environment alone.
3. **Otherwise no key**, and every call answers that an API key is required, as without the
   reference.

The name is read as written: Windows compares variable names without case, Linux with. A
reference that cannot be a variable's name — an `=`, a space, a line break — refuses the host
start, naming the configuration path and never the value, which may be a key pasted in the wrong
field. An `ApiKey` written as a `${NAME}` placeholder — the shape the old `examples/appsettings`
templates carried, which nothing ever expanded, so the text went out as the key — refuses the start
too, with the fix: `"ApiKeyEnvVar": "NAME"`. At startup the runner host says where each key came
from, never the key nor the variable's name — `LLM resolved: … apiKey=from the variable named by
Llm:ApiKeyEnvVar (user environment)`, then one `LLM profile <id>: apiKey=…` line per profile
offered to crews (`from configuration (…:ApiKey)`, `from the variable named by … (process
environment)`, `none`) — and warns once per section whose reference names a variable set nowhere,
by its path, on its logger and on stderr; `orkeon doctor`, `orkeon init`'s probe and the REPL say
the same. A profile `orkeon-host`'s allow-list hides is named on a line of its own and never warned
about: no crew can name it ([service host](../architecture/service-host.md)). A value left blank reads as
absent, for every key of the section (`Thinking:Effort` included): a Studio launch blanks the
default fields its team's setting does not set, and a section whose every value is blank
configures no default — the echo provider, with its warning. `orkeon init --api-key-env <name>`
and `orkeon-studio-config` write `Llm:ApiKeyEnvVar`; Orkeon Studio writes it for the elected
setting and for every profile it owns ([below](#studio-writes-this-section)). The reference may
name any variable: a settings file is trusted configuration ([Security](../architecture/security.md#llm-keys-the-settings-file-names-the-variable)).

### Named profiles (`Llm:Profiles`)

A host can offer more than one provider. Each child of `Llm:Profiles` is a **profile**: a
name, and a provider described with exactly the keys of the `Llm` section (`BaseUrl`,
`ApiKeyEnvVar`, `ApiKey`, `Model`, `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries`,
`Thinking`, `Grammar`).
The `Llm` section itself stays the **default** profile — the one every agent runs on unless it
names another. A crew picks a profile **by name**, never by key or endpoint: `llm: { profile:
claude }` on the crew, an agent or a task's `llm_override` in YAML, `llm.profile("claude")` on an
agent and `taskBuilder().withProfile("claude")` on a task in `.ork.ts` ([YAML and builders](../getting-started/yaml-and-builders.md#one-provider-per-agent-profiles)).

```json
{
  "Llm": {
    "BaseUrl": "https://api.deepseek.com/v1", "Model": "deepseek-v4-flash", "ApiKeyEnvVar": "DEEPSEEK_API_KEY",
    "Profiles": {
      "claude": { "BaseUrl": "https://api.anthropic.com/v1", "Model": "claude-sonnet-5", "ApiKeyEnvVar": "ANTHROPIC_API_KEY" },
      "local":  { "BaseUrl": "http://localhost:11434", "Model": "qwen3" }
    }
  }
}
```

Keys stay out of files the same way: the profile names its variable (`ApiKeyEnvVar`), and
`ORKEON_Llm__Profiles__claude__ApiKey` wins over it when set. Each profile's
provider is built once, on first use, metered like the default one — the run's `cost.updated`
readings name each call's provider, and Studio's status bar breaks the tokens down per
provider. The profiles are validated when the host starts: `default` is a reserved name (it
designates the `Llm` section, and a crew may name it to bring an agent back to the default), and
an invalid `BaseUrl` or a value that is not a number fails the start with the key to fix. A
crew naming a profile the host does not define **fails to load**, and the message lists the
profiles the host offers. **A model left unset is the profile's own, on every path**: a YAML or
`.ork.ts` `llm:` block without `model`, a C# agent's `.Thinking()` or `.MaxOutputTokens(n)`,
`llm.default_` on a host that configures no model, the planner, the agent loops outside the chat
client and the cognitive memory's analysis calls all leave the model to the provider they reach,
which sends the model its profile configures, else its own default — never an empty one, never
OpenAI's on another vendor.

Who runs on which profile:

- **An agent's turns** — on its own `llm:` profile, or its task's `llm_override` for that task
  (`taskBuilder().withProfile(name)` in `.ork.ts`). An agent built in C# may carry its own provider
  instead (`AgentBuilder.WithLlm(provider)`, or a Microsoft Agent Framework agent through
  `WithAgentFrameworkAgent` — `Agent.Llm`): its turns, its correction round and its ballot run there,
  on a client the host builds once per provider and meters as the agent's work; a task's
  `llm_override` profile still moves that task, and the agent cannot also name a profile (the build
  refuses both).
- **The manager** of a hierarchical crew (and the one that hands an autonomous crew's tasks out) —
  on the LLM the crew gives it: in C# the provider `CrewBuilder.WithManagerLlm` sets, metered like
  the host's own; else the provider its manager agent carries itself (`WithLlm`,
  `WithAgentFrameworkAgent`), metered the same way; else its manager agent's `llm:` block, profile and
  model; else the default profile.
- **The RAG subsystem** — grounded generation, query transformers, the listwise reranker, the
  corrective graph's evaluator and groundedness checker, the `llm` classifier and the evaluation
  judge — on the profile `Orkeon:Rag:LlmProfile` names (unset: the default). A name the host does
  not offer refuses the start of the host, listing the ones it does; the profile's provider is
  built at the first RAG call that needs it, never at start-up.
- **The planner, the Guardian, the `Evaluation` judges and the cognitive memory's analyses** stay
  on the default profile: they are host services, not crew roles (`CrewBuilder.WithPlanningLlm`
  keeps the hand in C#, with `.Planning()`, its calls metered like the host's own).

A section holding `Profiles` alone configures no default provider — the default is then the echo
provider, with the usual warning. `orkeon-host` can restrict which profiles its crews may name
(`Orkeon:Host:LlmProfiles`, below); the restriction applies to the RAG profile and to a manager
agent's too.

**One run on another profile.** `orkeon run --llm-profile <id>` elects one profile as the run's
default ([CLI](./cli.md#orkeon-run)): for that run the `Llm` section is that profile, whole — what it
leaves unset is unset, its key is its own —, and every role above that runs on the default runs on
it. An id the configuration does not define refuses the run, listing the ones it does. It is what
Orkeon Studio writes into a scheduled team's launchers.

#### Studio writes this section

No `Orkeon:Studio` section exists, and Studio writes none of its own preferences here: this is the file `orkeon run` reads, and the host refuses at start any `Orkeon:*` section it does not know — a `Orkeon:Studio:TeamsRoot` key would fail every run of the machine. The teams folder Studio lists is chosen by the `ORKEON_STUDIO_TEAMS_ROOT` variable, the `--teams-root` option or Settings › Studio, whose preference lives in Studio's own `ui-preferences.json` (STUDIO-61, [Studio](../architecture/studio.md)).

Orkeon Studio's model settings are the host's profiles (STUDIO-48). Each setting of the AI-model
tab that names a provider is the profile named after it by the folder-name rule of the teams —
« Claude » is `claude`, « Z.AI » is `z-ai` — and its card and its editor show what a crew writes,
`profile: claude`. Creating, editing, renaming or deleting the setting writes, moves or removes
its entry: every field the setting pins (`BaseUrl`, `Model`, `Temperature`, `TimeoutSeconds`,
`MaxTokens`, `Thinking`) and the variable Studio remembers its key in (`ApiKeyEnvVar`, none for a
setting that needs no key), never the key, and a key Studio does not model (`MaxRetries`,
`Grammar`) stays where it is. A Docker Model Runner setting writes `"ApiKey": "not-needed"` where
its entry holds no key, as `orkeon init` does (STUDIO-54): the run reads that server as OpenAI,
whose dialect refuses to call without a key, and the server checks none — a key already there
stays. A setting without a model (the echo card), or whose name keeps no
ASCII letter or digit, is offered to no crew; `default`, a name another setting already answers
to, and a name that would take over an entry written by hand are refused.

- **Keys.** The file names the variable Studio remembers each key in — `ApiKeyEnvVar`, the
  provider's conventional name (`DEEPSEEK_API_KEY`, `ZAI_API_KEY`) — and a run outside Studio reads
  it ([above](#the-api-key-apikey-apikeyenvvar)): `orkeon run` in a terminal, a scheduled team,
  `orkeon-host` started by the user, `orkeon-repl`. The editor names that variable in expert mode. A
  launch from Studio — a run, a trial, the creation assistant — still lays every setting over its
  child as `ORKEON_Llm__Profiles__<id>__*`, key included, so it never depends on the file having been
  saved nor on which settings file it reads. A team of an Orkeon Workshop folder — its folder right
  under the teams root, with `settings/<slug>/appsettings.json` beside that root — is launched on
  that file, passed as `--settings` unless Expert mode pins another (STUDIO-62): its `Llm` section
  and its `RateLimiting` apply as they are when the team's card names no setting; a card naming a
  setting of this machine lays that setting's `ORKEON_Llm__*` over it, key by key.
- **The default.** The elected setting is written into `Llm` whole — every field it pins and its
  `ApiKeyEnvVar`, a field it leaves unset removing its key; `ApiKey`, `MaxRetries`, `Grammar`,
  `AvailableModels` and `Profiles` stay. Docker Model Runner's placeholder alone follows the card
  (STUDIO-54): an elected Docker Model Runner setting writes `"ApiKey": "not-needed"` where `Llm`
  holds no key, and electing any other card takes exactly that value out — left, it would pass
  before the elected setting's `ApiKeyEnvVar`; the file `orkeon init --preset docker-model-runner`
  wrote, then DeepSeek elected in Studio, included. Any other `ApiKey` stays. A team launched on
  another setting lays all those fields over its child as `ORKEON_Llm__*`, value or blank,
  `ORKEON_Llm__ApiKeyEnvVar` included: a team on Z.AI whose key is not remembered fails without a
  key rather than send the default's DeepSeek key to Z.AI; a team on a Docker Model Runner setting
  lays `ORKEON_Llm__ApiKey=not-needed`. A team on the elected setting itself lays what it sets. `orkeon-studio-config` edits
  `Llm` field by field; an election made in Studio afterwards rewrites the fields it owns.
- **An older file heals at the next gesture.** An entry Studio owns without its reference, and an
  `Llm` without the elected setting's, are written again — the whole setting — at the next change
  on the model settings (edit then save a setting), never at startup; so are an entry and an `Llm`
  whose key their card disagrees with (STUDIO-54): a Docker Model Runner setting's without the
  placeholder receives it, another card's with it loses it — once, since writing makes them agree.
  A setting written before in another language, or whose provider the default blanked, finds its
  card again as Studio reads `studio-model-profiles.json`, with that card's key variable, and the
  card's name reaches the file at that same gesture.
- **A scheduled team.** The launchers of a team Studio adopted carry the team's setting as
  `--llm-profile <id>` — its entry here —, so the run the operating system schedules takes it, as a
  launch from Studio does ([Studio](../architecture/studio.md#a-scheduled-team-runs-as-studio-launches-it-studio-50)).
  That run reads this file as saved, keys where it names them: the team's card says when this file
  does not define its entry yet, holds an older version of it, or when the setting is one no run
  outside Studio can take — the run then takes the default —, and « Install the schedule » saves the
  settings first when they lack it. A setting renamed in Studio carries its teams
  ([Studio](../architecture/studio.md#a-team-keeps-its-setting-and-its-folders-studio-52)).
- **« Other OpenAI-compatible ».** A setting created from that card keeps its key in
  `ORKEON_CUSTOM_LLM_API_KEY`, shared by the settings of the card. One created before keeps
  `ORKEON_Llm__ApiKey` — the runtime's own key of the default: remembered in the user scope, it is
  the default key of every run of the user, whatever its settings file or its endpoint. Its card says
  so in expert mode when it is not the default, with the way back: create it again from the card,
  delete the old one, remove `ORKEON_Llm__ApiKey` from the user environment. Studio never migrates
  it on its own: it cannot tell its own write from a variable set by hand.
- **Entries written by hand** — no setting owns them — are listed read-only under the settings,
  and Studio never rewrites nor removes them. Ownership is the setting's name: an id is Studio's
  when a setting of `studio-model-profiles.json` answers to it.
- **The RAG's profile** (`Orkeon:Rag:LlmProfile`) is chosen on the same tab, in expert mode, among
  the profiles; it follows its setting through a rename and falls back to the default when the
  setting is deleted. Studio warns, before saving, about a file whose RAG profile names a profile
  the file does not define.

`orkeon-studio-config` shows the section — each profile with the variable that holds its key,
`key: ZAI_API_KEY` — and the RAG's profile read-only.

### `LlmLogging`

Tunes the capture of LLM exchanges. Read only when the run passes `--llm-log`, which says where
the log goes (`RunnerHost` → `AddLlmExchangeLogging(logDirectory, options)`). A switch that is not
`true` or `false`, a length that is no whole number, or another key refuses the start of a run
passing `--llm-log`.

<!-- settings:LlmLogging -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `FullEmbeddingLog` | boolean | `true` | Whether an embedding vector is logged at full length. `false` replaces each one by its first and last two values: the exchange keeps its shape and loses its megabytes. |
| `LogStreamingExchanges` | boolean | `true` | Whether a streamed exchange is logged: its request, and the answer once the stream has ended. |
| `MaxBodyLengthChars` | integer | `0` | The most characters logged of one request or response body; `0` logs it whole. |
<!-- /settings -->

### `Orkeon:CostTracking`

LLM cost tracking: a default budget per crew (`DefaultCrewBudget`) and the price of a model, by
name or pattern (`CustomPricings`). Registered by `AddOrkeonInfrastructure()`; the section gates
behavior. The caps on the number of calls are [`RateLimiting`](#ratelimiting).

<!-- settings:Orkeon:CostTracking -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `CustomPricings:<i>:CompletionPricePerMillion` | number | `0` | Cost per million completion (output) tokens in the specified currency. |
| `CustomPricings:<i>:Currency` | string | `"USD"` | Currency for pricing (default: USD). |
| `CustomPricings:<i>:EffectiveDate` | date-time | the moment the value is read | Date when this pricing became effective. |
| `CustomPricings:<i>:EmbeddingPricePerMillion` | number | — | Cost per million embedding tokens (null if not an embedding model). |
| `CustomPricings:<i>:ModelPattern` | string | `""` | Model name or pattern (e.g., "gpt-4o", "claude-3-opus"). |
| `CustomPricings:<i>:PromptPricePerMillion` | number | `0` | Cost per million prompt (input) tokens in the specified currency. |
| `DefaultCrewBudget:AlertThresholdPercent` | number | `80` | Percentage of budget at which a warning alert is fired (default: 80%). |
| `DefaultCrewBudget:MaxCallsPerMinute` | integer | — | Maximum LLM calls per minute (null = unlimited). |
| `DefaultCrewBudget:MaxCostUsd` | number | — | Maximum total cost in USD (null = unlimited). |
| `DefaultCrewBudget:MaxTokens` | integer | — | Maximum total tokens (null = unlimited). |
| `Enabled` | boolean | `true` | Whether cost tracking is enabled (default: true). |
<!-- /settings -->

### `Orkeon:TokenCounter`

Token estimation. Registered by `AddOrkeonInfrastructure()`; the section gates behavior.

<!-- settings:Orkeon:TokenCounter -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `CharsPerToken` | number | `3.5` | Average number of characters per token (default: 3.5). |
| `SpecialTokenOverhead` | integer | `2` | Number of special tokens overhead (e.g., BOS/EOS) per request (default: 2). |
| `TokensPerMessage` | integer | `4` | Number of overhead tokens per message in chat format (default: 4). |
| `TokensPerReply` | integer | `3` | Number of overhead tokens for the reply priming (default: 3). |
<!-- /settings -->

### `Evaluation`

With `EnableLlmJudge`, the default `IEvaluationSuite` also runs the coherence, fluency and
groundedness LLM judges over the registered `IChatClient`. Bound by
`AddOrkeonInfrastructure(configuration)`, or `AddOrkeonEvaluation(configuration)` (idempotent: a
second call registers no evaluator twice).

<!-- settings:Evaluation -->
**Read by**: a host written in C# only, through `AddOrkeonEvaluation()` — no shipped binary reads this section.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `EnableLlmJudge` | boolean | `false` | When true, the default evaluation suite also runs the LLM-as-Judge evaluators (coherence, fluency, groundedness). Requires an IChatClient in DI. Default: false. Bound from the `Evaluation` section. |
<!-- /settings -->

## Rate and budgets

Two different things are called rate limiting. **`RateLimiting`** caps the calls to the model,
and every shipped host applies it. **`ToolRateLimiting`** caps the calls to tools and
**`TokenBudget`** the tokens and the cost of an agent or a crew: both take effect in a host
written in C# that calls `AddOrkeonToolRateLimiting()` — no shipped binary reads them. A crew
bounds one agent with `maxRpm` ([YAML schema](../architecture/yaml-schema.md)), the stricter of it
and `RateLimiting:AgentRequestsPerMinute` winning; what the calls cost is tracked by
[`Orkeon:CostTracking`](#orkeoncosttracking).

### `RateLimiting`

The host's caps on model calls: every model call of the host counts once, at the entrance of its
provider — agent turns, the manager, the planner, RAG, the judges, the cognitive memory, scripts'
`ctx.llm` (embeddings do not) — against `GlobalRequestsPerMinute`, `ProviderRequestsPerMinute`,
`MaxConcurrentRequests` and `QueueLimit`; `AgentRequestsPerMinute` caps each agent instance in its
own window, with its `maxRpm`, the stricter winning, and a request over it waits its turn
([security](../architecture/security.md)). Bound by `AddOrkeonInfrastructure()`
(`ILlmRateLimiter`; `RateLimitingOptions` in `Orkeon.Application.Configuration`).

<!-- settings:RateLimiting -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AgentRequestsPerMinute` | integer | `20` | Maximum model requests per minute of each agent — each agent instance, not each role: it bounds the agent's own window with its `maxRpm`, the stricter winning, and a request over it waits its turn instead of failing. Read by the execution orchestrator and the manager. |
| `GlobalRequestsPerMinute` | integer | `60` | Maximum model requests per minute across all providers and agents. A request over it is held in a queue of `QueueLimit`, then retried, then failed. |
| `MaxConcurrentRequests` | integer | `0` | Maximum number of concurrent (in-flight) model requests. 0 means no concurrency limit (rate limiting only). |
| `ProviderRequestsPerMinute` | integer | `30` | Maximum model requests per minute per provider, held and refused like the global cap. |
| `QueueLimit` | integer | `5` | Maximum number of requests the global, per-provider and concurrency caps hold in a queue when they are reached; a request beyond it is refused, then retried. |
<!-- /settings -->

### `ToolRateLimiting`

Per-tool rate limits. Opt-in: `AddOrkeonToolRateLimiting()`, which binds from the registered
`IConfiguration` (see [opt-in subsystems](./opt-in-subsystems.md)).

<!-- settings:ToolRateLimiting -->
**Read by**: a host written in C# only, through `AddOrkeonToolRateLimiting()` — no shipped binary reads this section.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `DefaultToolRequestsPerMinute` | integer | `30` | Default maximum requests per minute for tools without a specific limit. |
| `GlobalToolRequestsPerMinute` | integer | `120` | Maximum tool requests per minute across all tools. |
| `ToolSpecificLimits:<name>` | integer | FileWriteTool = 15, HttpApiTool = 20, SecureCodeInterpreterTool = 5, WebScrapeTool = 10 | Per-tool rate limits, keyed by tool name. |
<!-- /settings -->

### `TokenBudget`

Token budgets. Same opt-in as [`ToolRateLimiting`](#toolratelimiting).

<!-- settings:TokenBudget -->
**Read by**: a host written in C# only, through `AddOrkeonToolRateLimiting()` — no shipped binary reads this section.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `MaxCostPerCrew` | number | `10.0` | Maximum estimated cost per crew in USD. 0 means unlimited. |
| `MaxTokensPerAgent` | integer | `100000` | Maximum tokens per agent. 0 means unlimited. |
| `MaxTokensPerCrew` | integer | `500000` | Maximum tokens per crew. 0 means unlimited. |
<!-- /settings -->

## Memory and vectors

Where memories live, and how texts are embedded and searched. `Memory:Provider` names the
provider; its connection is that provider's own section.

### `Memory`

`Memory:Provider` is the TYPE of the application-wide memory provider (`inmemory`, `redis`,
`sqlite`, `chromadb`, `pinecone`, `lancedb`, or an alias: `in-memory`, `chroma`, `lance`; unset →
in-memory). Its connection is that provider's own section (`Orkeon:Redis`, `Orkeon:Sqlite`, …
below). It is also where the memory of a named crew with `memory: true` and no `memoryProvider:`
lives — see [Memory system](../architecture/memory-system.md#selection-by-configuration). Another
name, `lancedb` without `Orkeon:LanceDb:Endpoint` or `pinecone` without `Orkeon:Pinecone:ApiKey`
(or `Host`) refuses the start: it no longer runs on the volatile provider
([when a setting is refused](#when-a-setting-is-refused)). Bound by `AddOrkeonInfrastructure()`.

<!-- settings:Memory -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Provider` | string | — | The provider type — `inmemory`, `redis`, `sqlite`, `chromadb`, `pinecone`, `lancedb`, or an alias. Unset is the in-memory provider. |
<!-- /settings -->

### `Orkeon:Redis`

Redis memory provider. `ConnectionString` defaults to `localhost:6379`; the connection opens on
first use. Read by `MemoryProviderFactory` on every path selecting `redis`: `Memory:Provider`, a
crew's `memoryProvider:`, `Orkeon:Rag:Provider`, `AddOrkeonRedisMemory`. Bound by
`AddOrkeonInfrastructure()`.

<!-- settings:Orkeon:Redis -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ConnectionString` | string, secret | — | The StackExchange.Redis connection string (e.g. `localhost:6379`, or `redis.internal:6379,password=…,ssl=true`). |
| `KeyPrefix` | string | `"orkeon:memory:"` | The prefix namespacing every key the provider writes. |
<!-- /settings -->

### `Orkeon:Sqlite`

SQLite memory provider. `ConnectionString` defaults to `Data Source=:memory:`; a file
`Data Source` is a virtual path on a writable mount. Read by `MemoryProviderFactory` on every path
selecting `sqlite`. Bound by `AddOrkeonInfrastructure()`.

<!-- settings:Orkeon:Sqlite -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ConnectionString` | string, secret | — | The SQLite connection string (e.g. `"Data Source=/data/orkeon-memory.db"` or `"Data Source=:memory:"`). Defaults to an in-memory database (`Data Source=:memory:`); a file `Data Source` is a virtual path, which must point at a writable mount. |
| `DefaultTopK` | integer | `10` | The default number of results returned by vector queries. |
| `MinSimilarityScore` | number | `0` | The minimum similarity score threshold for vector search results. |
| `TableName` | string | `"memory_items"` | The table name used for storing memory items. Must match `^[A-Za-z_][A-Za-z0-9_]*$`: the identifier is interpolated into SQL statements. Checked when a host starts and again at construction. |
<!-- /settings -->

### `Orkeon:ChromaDb`

ChromaDB server. Read by `MemoryProviderFactory` on every path selecting `chromadb`. Bound by
`AddOrkeonInfrastructure()`; `AddOrkeonChromaDb(configuration)` (called by
`AddOrkeonInfrastructure(configuration)` when the section exists) also exposes the shared
`ChromaDbMemoryProvider` by its class.

<!-- settings:Orkeon:ChromaDb -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `BaseUrl` | uri | `"http://localhost:8000/"` | The base URL of the ChromaDB server. |
| `CollectionName` | string | `"orkeon_memories"` | The collection name to use for storing memories. |
| `Database` | string | `"default_database"` | The database addressed by the v2 API (`/api/v2/tenants/{tenant}/databases/{database}/...`). Defaults to `default_database`. Non-default databases must already exist on the server. |
| `DefaultTopK` | integer | `10` | The default number of results to return from queries. |
| `Tenant` | string | `"default_tenant"` | The tenant addressed by the v2 API (`/api/v2/tenants/{tenant}/...`). Defaults to `default_tenant`. Non-default tenants must already exist on the server. |
<!-- /settings -->

### `Orkeon:Pinecone`

Pinecone index. `Host` is optional: without it, one `describe_index` call resolves the index host
on first use. Read by `MemoryProviderFactory` on every path selecting `pinecone`. Bound by
`AddOrkeonInfrastructure()`, and by `AddOrkeonPinecone(configuration)`.

<!-- settings:Orkeon:Pinecone -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ApiKey` | string, secret | — | The Pinecone API key. |
| `Host` | string | — | The index host, as Pinecone reports it (e.g. `orkeon-memories-abc1234.svc.aped-4627-b74a.pinecone.io`; a scheme is optional). When empty, the provider asks the control plane once, on first use (`GET https://api.pinecone.io/indexes/{IndexName}`), and uses the `host` it returns. |
| `IndexName` | string | `"orkeon-memories"` | The name of the Pinecone index. |
| `Namespace` | string | `"default"` | The namespace within the index. |
<!-- /settings -->

### `Orkeon:LanceDb`

Remote LanceDB server. Read by `MemoryProviderFactory` on every path selecting `lancedb`. Bound by
`AddOrkeonInfrastructure()`; `AddOrkeonLanceDb(configuration)` adds the concrete class and
`LanceDbMigrationService`.

<!-- settings:Orkeon:LanceDb -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ApiKey` | string, secret | — | The API key sent in the `x-api-key` header. |
| `CreateFullTextIndexOnInit` | boolean | `true` | Whether the provider creates a full-text (FTS) index on the `content` column when it creates the table. Server-side full-text search requires this index; creation failures are logged as warnings and surface later as server errors on `SearchAsync`. |
| `Database` | string | — | The optional database name sent in the `x-lancedb-database` header (Enterprise deployments behind a shared/private endpoint). |
| `DefaultTopK` | integer | `10` | The default number of results to return from queries. |
| `DistanceType` | string | `"cosine"` | The distance metric used by server-side vector search (`cosine`, `l2` or `dot`). The provider converts the returned `_distance` to a similarity score as `1 - distance`, which is only meaningful for the default `cosine` metric. |
| `EmbeddingDimension` | integer | `1536` | The embedding vector dimension of the table's fixed-size `vector` column. Must match the embeddings stored through the provider. |
| `Endpoint` | string | `""` | The base URL of the LanceDB Cloud/Enterprise REST endpoint (e.g. `https://my-deployment.us-east-1.api.lancedb.com`). |
| `FullTextWeight` | number | `0.3` | The weight applied to the server-side full-text (BM25) ranking when fusing hybrid search results. |
| `MinSimilarityScore` | number | `0` | The minimum similarity score threshold for vector search results. |
| `TableName` | string | `"orkeon_memories"` | The table name used for storing memories. |
| `VectorWeight` | number | `0.7` | The weight applied to the server-side vector similarity ranking when fusing hybrid search results. |
<!-- /settings -->

### `Orkeon:CrewMemory`

What a crew with `memory: true` recalls before each task (`CrewMemoryOptions`): at most
`RecallLimit` memories (0 recalls nothing), those whose cosine similarity reaches `MinScore` — on
the embedder's scale, measured on the local model —, cut to `MaxChars` characters of memory
content in all — see [Memory system](../architecture/memory-system.md#what-a-crew-recalls). Read
by `MemoryCoordinator`; bound by `AddOrkeonInfrastructure()`.

<!-- settings:Orkeon:CrewMemory -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `MaxChars` | integer | `4000` | How many characters of memory content a task's prompt receives at most, all recalled memories together. 4,000 by default; the last memory that does not fit is cut. |
| `MinScore` | number | `0.6` | The least cosine similarity between a task and a memory for the memory to be recalled. 0.6 by default, measured on the local embedding model (BGE-micro-v2), on which the same task of an earlier run scores 0.72 and above and an unrelated English task 0.53 and below. Its scale is the embedder's: another model needs its own measure. |
| `RecallLimit` | integer | `5` | How many memories a task recalls at most. 5 by default; 0 recalls nothing (the crew still stores what its tasks produce). |
<!-- /settings -->

### `Orkeon:CognitiveMemory`

Cognitive memory layering. Opt-in: `AddOrkeonCognitiveMemory(configuration)`.

<!-- settings:Orkeon:CognitiveMemory -->
**Read by**: a host written in C# only, through `AddOrkeonCognitiveMemory()` — no shipped binary reads this section.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `AnalysisModel` | string | — |  | Optional model override for analysis calls. Null (or blank) runs them on the provider's own model. |
| `AnalysisTemperature` | number | `0.1` |  | Temperature for LLM analysis calls. Default: 0.1. |
| `ContradictionCandidateCount` | integer | `10` |  | Number of existing memories to check for contradictions. Default: 10. |
| `DefaultRecallOptions:ImportanceWeight` | number | `0.2` |  | Weight for importance in composite scoring. Default: 0.2. |
| `DefaultRecallOptions:MinScore` | number | `0.1` |  | Minimum composite score threshold. Default: 0.1. |
| `DefaultRecallOptions:RecencyWeight` | number | `0.3` |  | Weight for temporal recency in composite scoring. Default: 0.3. |
| `DefaultRecallOptions:SemanticWeight` | number | `0.5` |  | Weight for semantic similarity in composite scoring. Default: 0.5. |
| `DefaultRecallOptions:TagFilter` | list of string | `[]` |  | Optional filter by tags (any match). |
| `DefaultRecallOptions:TopK` | integer | `10` |  | Maximum number of results to return. Default: 10. |
| `DefaultRecallOptions:TypeFilter` | enum | — | `ShortTerm`, `LongTerm`, `Episodic`, `Entity`, `Procedural` | Optional filter by memory type. |
| `EnableContradictionDetection` | boolean | `true` |  | Whether to enable contradiction detection on remember. Default: true. |
| `EnableLlmAnalysis` | boolean | `true` |  | Whether to enable LLM-based content analysis on remember. Default: true. |
| `PruningMinAgeDays` | integer | `30` |  | Minimum age in days before a memory can be pruned. Default: 30. |
| `PruningThreshold` | number | `0.1` |  | Importance threshold below which memories are candidates for pruning. Default: 0.1. |
| `RecencyHalfLifeHours` | number | `69` |  | Half-life for recency decay in hours (exp(-ln2/halfLife * ageHours)). Default: 69.0. |
<!-- /settings -->

### `Orkeon:Encryption`

At-rest memory encryption. Registered by `AddOrkeonInfrastructure()`; the section gates behavior,
and the decorator is applied by the host. Key rotation stays opt-in: `AddOrkeonKeyRotation()`.

<!-- settings:Orkeon:Encryption -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | boolean | `false` | Whether encryption is enabled. |
| `KeySizeInBits` | integer | `256` | Key size in bits (128, 192, or 256). |
| `SecretName` | string | `"orkeon-encryption-key"` | The secret name used to retrieve the encryption key from `ISecretProvider`. |
<!-- /settings -->

### `Orkeon:Embeddings`

Embedding provider selection: `Provider` is `openai`, any M.E.AI embedding generator the host
registers, or `ollama`; another name refuses the start. Read by
`DefaultEmbeddingProviderResolver`, when no local or code-analysis provider is registered. Bound
by `AddOrkeonInfrastructure()`, and by `AddOrkeonVectorSearch(configuration)` (called by
`AddOrkeonInfrastructure(configuration)`).

<!-- settings:Orkeon:Embeddings -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `BatchSize` | integer | `100` | Maximum batch size for bulk embedding requests. |
| `Dimension` | integer | `1536` | The dimension of the embedding vectors. |
| `EnableCache` | boolean | `true` | Whether to enable embedding caching. |
| `Model` | string | `"text-embedding-3-small"` | The model name to use for embeddings. |
| `Provider` | string | `"openai"` | The embedding provider to use (e.g., "openai", "ollama"). |
<!-- /settings -->

### `Orkeon:EmbeddingCache`

Embedding cache. Bound like [`Orkeon:Embeddings`](#orkeonembeddings).

<!-- settings:Orkeon:EmbeddingCache -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `MaxCacheSizeBytes` | integer | `104857600` | Maximum total size of the cache in bytes. |
| `SlidingExpirationMinutes` | integer | `60` | Sliding expiration time in minutes for cached embeddings. |
<!-- /settings -->

### `Orkeon:VectorSearch`

Vector search options. Bound by `AddOrkeonVectorSearch(configuration)`, which
`AddOrkeonInfrastructure(configuration)` calls.

<!-- settings:Orkeon:VectorSearch -->
**Read by**: a host written in C# only, through `AddOrkeonVectorSearch()` — no shipped binary reads this section.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `DefaultMinScore` | number | `0.7` | The default minimum similarity score threshold. |
| `DefaultTopK` | integer | `10` | The default number of top results to return. |
| `PreferVectorSearch` | boolean | `true` | Whether to prefer vector search over text-based search when available. |
<!-- /settings -->

## RAG

Details and semantics: [RAG pipeline](../architecture/rag-pipeline.md). Everything below
requires `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection`), which every shipped
host calls.

### `Orkeon:Rag`

The query pipeline, stage by stage:

- **`Profile`** — the preset: `fast` (default), `balanced`, `quality`, `adaptive` or
  `corrective`; any `Orkeon:Rag` key overrides the preset key-by-key. **The defaults of the table
  are those of the default profile, `fast`**: a key the profiles set differently says so, and
  the five presets are compared in [RAG pipeline](../architecture/rag-pipeline.md).
- **`LlmProfile`** — the host LLM profile (`Llm:Profiles:<name>`) the RAG subsystem calls —
  generation, query transformers, listwise reranker, corrective evaluator and groundedness
  checker, `llm` classifier, evaluation judge; unset or `default` is the default profile. An
  unknown name refuses the host start, listing the known ones; `orkeon rag eval --offline` ignores
  it. Chosen in Studio › Settings › AI model (expert).
- **`Provider`** — TYPE of the RAG document-store provider (`RagStoreOptions` — a
  `MemoryProviderFactory` type alias), connected from that provider's own section (`Orkeon:Redis`,
  `Orkeon:Sqlite`, …); default is the ambient `IMemoryProvider`.
- **`Collection`** — the collection `rag_search` queries when the agent names none (unset:
  `default`); `rag_eval` uses it for a dataset that names no collection and brings no corpus.
- **`Retrieval`** — the bounds of the retrieval stage; **`Retrieval:Mmr`** — opt-in MMR.
- **`Rerank`** — reranker selection and depth.
- **`Context`** — context assembly (anti-Lost-in-the-Middle `edges` ordering).
- **`Groundedness`** — the groundedness hook.
- **`Generation`** — the cited generation stage.
- **`QueryTransform`** — query transformers (`multi-query`/`rag-fusion`/`hyde`).
- **`Corrective`** — the bounds of the CRAG corrective graph; **`Corrective:WebFallback`** — the
  policy half of the web fallback, whose transport is
  [`Orkeon:Rag:WebFallback`](#orkeonragwebfallback).

<!-- settings:Orkeon:Rag -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Collection` | string | — | Default collection queried when the call site names none. |
| `Context:MaxTokens` | integer | `2000` | Context budget in tokens (1 token ≈ 4 characters heuristic; excess chunks are dropped, never silently generated over). |
| `Context:Ordering` | string | `"edges"` | Chunk layout inside the context block: `edges` (default — anti-Lost-in-the-Middle: odd ranks 1, 3, 5… open the block, even ranks …6, 4, 2 close it, so the two best chunks sit at the extremities) or `linear` (plain rank order). Citation markers stay rank-based (`[1]` = best chunk) whatever the layout. |
| `Corrective:MaxIterations` | integer | `3` | Maximum number of corrective iterations (query rewrites — whether triggered by an `Incorrect` retrieval verdict or by an ungrounded answer) before the pipeline generates with the best chunks available. Defaults to `3`. |
| `Corrective:WebFallback:Enabled` | boolean | `false` | Whether the web fallback may fire. Off by default (opt-in): retrieval stays strictly local unless the host explicitly enables it AND registers a web document retriever. |
| `Corrective:WebFallback:MaxResults` | integer | `3` | Maximum number of web documents requested from the retriever. Defaults to `3`. |
| `Generation:MaxOutputTokens` | integer | — | Maximum output tokens passed to the chat client. |
| `Generation:SystemPrompt` | string | — | Grounded system prompt; `null` selects the pipeline default (anti-hallucination, `[n]` markers). |
| `Generation:Temperature` | number | — | Sampling temperature passed to the chat client. |
| `Groundedness:Enabled` | boolean | `false` — under the default profile, `fast`: each profile sets its own | Whether the answer is verified against the retrieved context after generation. Requires an `IGroundednessChecker` registration; enabled without one, the stage is traced as skipped. |
| `LlmProfile` | string | — | The host LLM profile (`Llm:Profiles:<name>`) every model call of the subsystem goes to — grounded generation, the query transformers, the listwise reranker, the corrective graph's evaluator and groundedness checker, the `llm` classifier and the evaluation judge. Null, blank or `default` is the host's default profile. A host key: one profile for the whole subsystem, which no preset sets. |
| `Profile` | string | `"fast"` | Profile name (`fast`, `balanced`, `quality`, `adaptive`, `corrective`). Defaults to `fast` — `balanced` needs the opt-in ONNX reranker package. Unknown names fail loudly. |
| `Provider` | string | — | The memory-provider type backing the RAG document store: `inmemory`, `in-memory`, `redis`, `sqlite`, `chromadb`, `chroma`, `pinecone`, `lancedb`, `lance`. `null` or empty selects the ambient container provider. |
| `QueryTransform:Mode` | string | `"none"` | Transformer name resolved through the query-transformer factory: `none` (default), `multi-query`, `rag-fusion` or `hyde`. `none` skips the stage; unknown names fail loudly. |
| `QueryTransform:VariantCount` | integer | `3` | Number of variants requested from non-`none` transformers. |
| `Rerank:Enabled` | boolean | `false` — under the default profile, `fast`: each profile sets its own | Whether the rerank stage runs. Disabled, candidates are truncated to TopN in retrieval order. |
| `Rerank:Kind` | string | `"none"` — under the default profile, `fast`: each profile sets its own | Reranker name resolved through the reranker factory (`none`/`noop`, `llm`/`listwise`, `onnx`/`cross-encoder` — the latter via the opt-in `Orkeon.Rag.Onnx` package). Unknown names fail loudly. |
| `Rerank:TopN` | integer | `5` | Default number of chunks kept after reranking (a call-site `RagQuery.TopN` wins). |
| `Retrieval:CandidateK` | integer | `5` — under the default profile, `fast`: each profile sets its own | Number of candidates retrieved before fusion/rerank (wide stage of the 50 → 5 cascade). The pipeline always retrieves at least the final TopN. |
| `Retrieval:MinScore` | number | — | Optional score floor applied to raw retrieval scores before fusion. `null` (default) applies none — the toxic global 0.7 floor of the legacy subsystem is deliberately gone. |
| `Retrieval:Mmr:Enabled` | boolean | `false` | Whether MMR diversification runs at the fusion/dedup stage. Off by default. |
| `Retrieval:Mmr:Lambda` | number | `0.7` | Relevance/diversity trade-off in `[0, 1]`: `1` keeps the pure relevance order, `0` maximises diversity. Defaults to `0.7`. |
| `Retrieval:TopK` | integer | `5` | Default number of chunks kept for context assembly (a call-site `RagQuery.TopN` wins). |
<!-- /settings -->

### `Orkeon:Rag:Retrieval:Hybrid`

Hybrid BM25+RRF retrieval.

<!-- settings:Orkeon:Rag:Retrieval:Hybrid -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | boolean | `false` — under the default profile, `fast`: each profile sets its own | Whether retrieval fuses lexical (BM25 / native full-text) and vector rankings. Honoured per query through the hybrid-capable document store. |
| `RrfK` | integer | `60` | Reciprocal Rank Fusion constant (must be positive; 60 is the standard). |
<!-- /settings -->

### `Orkeon:Rag:Ingestion`

The ingestion pipeline (`RagIngestionOptions`).

<!-- settings:Orkeon:Rag:Ingestion -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `DefaultChunkingStrategy` | string | `"recursive"` | Chunking strategy resolved by the chunking factory when an `IngestionRequest` names none. Defaults to `recursive`. |
| `ManifestDirectory` | string | `"/output/rag/manifests"` | Virtual directory (VFS path) holding the per-collection ingestion manifests (`{collection}.json`). Defaults to `/output/rag/manifests` — under the conventional writable `/output` mount, next to other run artifacts. |
<!-- /settings -->

### `Orkeon:Rag:QueryRouting`

Adaptive-RAG routing: the classifier is `heuristic` or `llm`.

<!-- settings:Orkeon:Rag:QueryRouting -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Classifier` | string | `"heuristic"` | Classifier name: `heuristic` (default — safe, deterministic, works without any LLM) or `llm`. Unknown names fail loudly at resolution. When `llm` is selected but no `IChatClient` is registered, the heuristic classifier is used as the documented fallback (with a warning) rather than failing. |
<!-- /settings -->

### `Orkeon:Rag:WebFallback`

The web fallback is a double opt-in, both `Enabled` off by default: the policy
(`Orkeon:Rag:Corrective:WebFallback`, in the table of [`Orkeon:Rag`](#orkeonrag)) and this
section, its SearxNG transport.

<!-- settings:Orkeon:Rag:WebFallback -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `ApiKeyEnvVar` | string | `""` |  | Name of the environment variable holding the API key, sent as an `Authorization: Bearer` header when set. Empty = no auth header. The key itself never lives in configuration files. |
| `Enabled` | boolean | `false` |  | Master switch of the transport. Default `false` — strict opt-in. |
| `Endpoint` | string | `""` |  | Search endpoint URL of a SearxNG-compatible JSON API (queried as `{Endpoint}?q={query}&format=json`, expecting a `results[].url` JSON array). Empty = disabled (logged). |
| `MaxResults` | integer | `3` |  | Maximum number of search results fetched per query. Default 3. |
| `SuspiciousAction` | enum | `"Flag"` | `Flag`, `Discard` | What to do with documents the injection validator flags as `Suspicious`: keep them flagged in metadata (default) or discard them. Rejected documents are always discarded. |
| `Timeout` | duration | `"00:00:10"` |  | Per-request timeout (search call and each page download). Default 10 s. |
<!-- /settings -->

## Files and sandbox

The folders a run may reach, and where the code an agent writes may run.

### `Orkeon:FileSystem`

**`Mounts`** — the VFS mounts (see [VFS compliance](../architecture/vfs-compliance.md)). An entry
may carry an **id** — `<ulid>|<physical>:<virtual>:<rights>` (VFS-90): what a crew's `mounts:`
block, Studio's team sidecar and `--mount-id` name it by; Studio writes one on every save. A CLI
`--mount` on the same virtual root **replaces every entry of that root** for that run; on a new
root it is appended (never merged, never dropped). One root declared twice is legitimate only when
every entry of it carries an id — `--mount-id`, or the crew's `mounts:`, then selects one and the
others are withdrawn for the run; with nothing selecting one the run is refused before any host
builds (`'/output' is declared twice in <settings> (<idA>: <folderA>, <idB>: <folderB>) and nothing selects one. Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, or pass --mount <folder>:/output:rw to replace them all.`),
and so is a root declared twice with an entry that has no id
(`… and '<entry>' has no id. Give every entry an id …`) or an id carried by two entries. Every
declared entry's base path is **whitelisted for `PathValidator`** without
`--allow-external-mounts` — a declared folder is the machine owner's intent, so it is reachable
even when it lies outside the process working directory; a withdrawn entry is not.

**`InternalMounts`** — same grammar as `Mounts`, registered `MountVisibility.Internal`: resolvable
by the VFS, **absent from `list_mounts`, from the agent prompt's mount table and from
access-denied messages**. Where a host puts what the VFS must reach and no agent has any business
addressing — the `--llm-log` directory lives here, and so does `/credentials`, the OAuth tokens of
the e-mail accounts, when one is declared
([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)).

Both are read by `AddOrkeonFileSystem(...)`.

<!-- settings:Orkeon:FileSystem -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `InternalMounts` | list of string | `[]` | Infrastructure mounts, same grammar as `Mounts`: resolvable by the host, absent from `list_mounts`, the agent prompt's mount table and access-denied messages, and refused to every tool. This is where a runner puts what it needs the VFS to reach but no agent has any business addressing — the LLM exchange log directory, for one. |
| `Mounts` | list of string | `[]` | Mount definitions in the format "physical:virtual:rights[;subpath:rights;...]". |
<!-- /settings -->

### `Orkeon:Sandbox`

The sandbox file-system mount (`/sandbox`, Internal); orphaned session directories are cleaned up
after `CleanupOrphansOlderThan` (24 h). Read by `AddOrkeonFileSystem(...)`.

<!-- settings:Orkeon:Sandbox -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `CleanupOrphansOlderThan` | duration | `"1.00:00:00"` | Age threshold for orphaned session directories. Directories older than this value are deleted by the janitor on startup. Defaults to 24 hours. |
| `EphemeralRoot` | string | — | Root directory under which per-session sandbox directories are created. When `null`, defaults to `Path.Combine(Path.GetTempPath(), "orkeon-sandbox")`. |
| `VirtualPath` | string | `"/sandbox"` | Virtual path at which the sandbox is mounted. Defaults to `/sandbox`. |
<!-- /settings -->

### `PathSecurity`

Physical path validation. Bound by `AddOrkeonInfrastructure()` (`IPathValidator`).

<!-- settings:PathSecurity -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AdditionalAllowedDirectories` | list of string | `[]` | Additional directories that are allowed beyond the workspace root. |
| `AdditionalBlockedExtensions` | list of string | `[]` | Additional file extensions to block beyond the built-in list. |
| `DefaultWorkspaceRoot` | string | — | The default workspace root directory. If null, defaults to the current working directory. |
| `MaxFileSizeBytes` | integer | `52428800` | Maximum allowed file size in bytes. Default: 50 MB. |
| `ResolveSymlinks` | boolean | `true` | Whether to resolve symlinks and verify the target is within the workspace. Default: true. |
<!-- /settings -->

### `Orkeon:CodeSandbox`

The sandbox of the secure code interpreter. Registered by `AddOrkeonInfrastructure()`; the section
gates behavior.

<!-- settings:Orkeon:CodeSandbox -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AllowHostExecution` | boolean | `false` | Explicit, noisy opt-in to allow executing code on a sandbox that provides NO OS-level isolation (the host process runner) when an isolating sandbox (Docker) is unavailable. Left `false`, code execution is refused rather than run on the host; `true` is RCE-equivalent on the host, for trusted code in a trusted environment only. |
| `DefaultPermissions:AllowedPaths` | list of string | `[]` | List of allowed file paths. |
| `DefaultPermissions:AllowFileRead` | boolean | `false` | Allow reading files. |
| `DefaultPermissions:AllowFileWrite` | boolean | `false` | Allow writing files. |
| `DefaultPermissions:AllowNetworkAccess` | boolean | `false` | Allow network access. |
| `DefaultPermissions:AllowProcessExec` | boolean | `false` | Allow spawning child processes. |
| `DefaultPermissions:AllowReflection` | boolean | `false` | Allow reflection APIs. |
| `DefaultPermissions:WorkingDirectory` | string | — | Working directory for the sandbox. |
| `MaxMemoryBytes` | integer | `268435456` | Maximum memory in bytes (default: 256 MB). |
| `MaxOutputBytes` | integer | `50000` | Maximum output bytes (default: 50 KB). |
| `SecurityOptions:AllowedNamespaces` | list of string | `["System", "System.Collections.Generic", "System.Linq", "System.Text", "System.Text.Json", "System.Text.RegularExpressions", "System.Math"]` | Namespaces that are allowed in using directives. |
| `SecurityOptions:AllowFileIO` | boolean | `false` | Allow file I/O operations. |
| `SecurityOptions:AllowNetworking` | boolean | `false` | Allow networking operations. |
| `SecurityOptions:AllowProcessExec` | boolean | `false` | Allow spawning processes. |
| `SecurityOptions:AllowReflection` | boolean | `false` | Allow reflection APIs. |
| `SecurityOptions:AllowUnsafeCode` | boolean | `false` | Allow unsafe code blocks. |
| `SecurityOptions:BlockedTypes` | list of string | `["System.Diagnostics.Process", "System.IO.File", "System.IO.Directory", "System.Reflection.Assembly", "System.Runtime.InteropServices.Marshal", "System.Net.Sockets.Socket", "System.AppDomain"]` | Types that are always blocked. |
| `TimeoutSeconds` | integer | `30` | Default execution timeout in seconds. |
<!-- /settings -->

### `Orkeon:CodeSandbox:Docker`

The Docker sandbox of the secure code interpreter.

<!-- settings:Orkeon:CodeSandbox:Docker -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ImageName` | string | `"mcr.microsoft.com/dotnet/sdk:10.0-alpine"` | Docker image to use for code execution. |
| `PullImageOnStartup` | boolean | `false` | Whether to pull the image on startup. |
<!-- /settings -->

## Security

What screens a prompt, a tool call and a tool result, what is audited, and where secrets come
from. The story is in [Security](../architecture/security.md).

### `Orkeon:Guardian`

The guard pipeline: `GuardianPipeline` runs the input phase of every agent turn, and the tool and
delegation phases of every tool call. On by default, registered by `AddOrkeonInfrastructure()`.

<!-- settings:Orkeon:Guardian -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `DefaultPolicy:DelegationGuardEnabled` | boolean | `true` | Whether the delegation phase (depth, self-delegation, cycles) runs. |
| `DefaultPolicy:InputGuardEnabled` | boolean | `true` | Whether the input phase (prompt-injection screening of the composed user prompt) runs. |
| `DefaultPolicy:MaxDelegationDepth` | integer | `5` | How many synchronous delegations may nest before the next one is blocked. |
| `DefaultPolicy:ToolGuardEnabled` | boolean | `true` | Whether the tool phase (path traversal, SSRF and SQL injection in tool arguments) runs. |
| `Enabled` | boolean | `true` | Whether the guardian runs. When false no guardian is registered in the pipeline: agent turns are neither screened on input nor on tool calls. |
<!-- /settings -->

### `Security:Prompt`

The Guardian's input phase: `Policy` is `Block` by default — High/Critical fails the task, lower
warns —, `Warn` or `None`. Bound by `AddOrkeonInfrastructure()`; it screens every agent turn's
prompt — see [Security](../architecture/security.md).

<!-- settings:Security:Prompt -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `CustomPatterns` | list of string | `[]` |  | Custom regex patterns to detect in addition to built-in patterns. |
| `EnableExfiltrationDetection` | boolean | `true` |  | Whether to enable detection of data exfiltration attempts. Default is true. |
| `Policy` | enum | `"Block"` | `None`, `Warn`, `Block` | The policy the Guardian's input phase applies to the composed user prompt. Default `Block`: a High or Critical pattern fails the task before any provider call, a lower one is logged and audited; the prompt is never rewritten. |
<!-- /settings -->

### `Security:ToolResults`

Tool-result screening: `Policy` is `Warn` by default — tagged as data and reported —, `Block`
withholds High/Critical, `None` passes everything; `TrustedTools` are added to the `email_*`
defaults. The length bound is not configured here: one rule,
`AgentDefaults.ResolveMaxToolResultLength`. Bound by `AddOrkeonInfrastructure()` and applied to
every tool result by `IToolInvocationPipeline`.

<!-- settings:Security:ToolResults -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `Policy` | enum | `"Warn"` | `None`, `Warn`, `Block` | The policy applied to a successful tool result. Default `Warn`: the result reaches the model tagged as data and any injection pattern is logged and audited. `Block` withholds a result carrying a High or Critical pattern (the model is told so); `None` passes results untouched and untagged. |
| `TrustedTools` | list of string | `["email_accounts", "email_create_folder", "email_delete", "email_draft", "email_folders", "email_mark", "email_move", "email_parser", "email_read", "email_rename_folder", "email_save_attachment", "email_search", "email_send"]` |  | Tools whose results bypass sanitization. Defaults to the `email_*` tools, which screen what they read with `PromptInjectionDocumentValidator` and mark it untrusted themselves (ADR-012) — tagging them again would stack two envelopes. Entries from configuration are added to these. |
<!-- /settings -->

### `Security:Url`

SSRF validation. Bound by `AddOrkeonInfrastructure()` (`IUrlValidator`).

<!-- settings:Security:Url -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AllowedDomains` | list of string | `[]` | If non-empty, only these domains (and their subdomains) are allowed. |
| `AllowedSchemes` | list of string | `["http", "https"]` | Allowed URL schemes. Defaults to http and https. |
| `BlockedDomains` | list of string | `[]` | Domains (and their subdomains) that are explicitly blocked. |
| `BlockedPorts` | list of integer | `[22, 23, 25, 110, 143, 445, 3306, 5432, 6379, 27017]` | Ports that are blocked from access (common service ports). |
| `BlockPrivateIPs` | boolean | `true` | Whether to block requests to private/internal IP ranges. Defaults to true. |
| `ResolveDNS` | boolean | `true` | Whether to resolve DNS and check the resolved IP against private ranges. Defaults to true. |
<!-- /settings -->

### `Security:Audit`

Audit sinks. Bound by `AddOrkeonInfrastructure()`.

<!-- settings:Security:Audit -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `AuditDirectory` | string | `"./audit-logs"` |  | Directory where audit log files are stored. |
| `Enabled` | boolean | `true` |  | Whether audit logging is enabled. |
| `EnabledCategories` | list of enum | `[]` | `LlmCall`, `ToolExecution`, `FileAccess`, `HttpRequest`, `SecurityEvent`, `CrewLifecycle`, `AgentDecision`, `MemoryOperation`, `ConfigChange` | Categories to log. Empty means all categories are enabled. |
| `MinSeverity` | enum | `"Info"` | `Debug`, `Info`, `Warning`, `Error`, `Critical` | Minimum severity level for events to be logged. |
| `RetentionDays` | integer | `90` |  | Number of days to retain audit logs. |
<!-- /settings -->

### `Security:Vault`

The secret vault chain. Bound by `AddOrkeonInfrastructure()`.

<!-- settings:Security:Vault -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AzureKeyVaultUri` | uri | — | URI of Azure Key Vault (e.g. https://my-vault.vault.azure.net/) |
| `CacheTtl` | duration | `"00:05:00"` | Cache TTL for secrets in memory (default: 5 min) |
| `DpapiSecretsDirectory` | string | — | Directory for DPAPI secret store (Windows only) |
| `UseAwsSecretsManager` | boolean | `false` | Whether to use AWS Secrets Manager |
<!-- /settings -->

### `Secrets`

`Secrets:<NAME>` is the second stop of the secret chain after `ORKEON_<NAME>`
(`ConfigurationSecretProvider`), e.g. `Secrets:TAVILY_API_KEY` for `web_search`. Bound by
`AddOrkeonInfrastructure()`.

<!-- settings:Secrets -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `<name>` | string, secret | — | A secret by its name — `Secrets:TAVILY_API_KEY` —, where the secret chain looks after the `ORKEON_<NAME>` environment variable and before a vault. A value written here is in clear in the settings file. |
<!-- /settings -->

### `Orkeon:Security:PermissionGate`

The per-tool-call gate (`ModePermissionGate`). `AddOrkeonPermissionGate(configuration)` — called
by `RunnerHost` — is a no-op unless `Enabled = true`.

<!-- settings:Orkeon:Security:PermissionGate -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | boolean | `false` | Whether the gate is registered. Left `false`, no tool call is gated. |
| `Interactive` | boolean | `false` | Whether an approval channel exists to ask an operator. No shipped host has one: left `false`, a call that needs an approval is refused, and the model reads the reason as the tool's result. |
<!-- /settings -->

### `Orkeon:Dlp`

DLP policies and PII detection. Opt-in: `AddOrkeonDlp()`.

<!-- settings:Orkeon:Dlp -->
**Read by**: a host written in C# only, through `AddOrkeonDlp()` — no shipped binary reads this section.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `ChannelPolicies:<name>` | any | — |  | Per-channel policy overrides. |
| `DefaultAction` | enum | `"Audit"` | `Allow`, `Mask`, `Block`, `Audit` | The default action for all channels. |
| `Enabled` | boolean | `true` |  | Whether DLP is globally enabled. |
<!-- /settings -->

## Tools

What the tools of an agent may do, and which servers and indexes they reach.

### `Orkeon:Tools:Shell`

What `ShellCommandTool` may run, configuration only (`AddOrkeonCodeTools()`):
`AllowInterpreters` allows the interpreters and mutating git (**RCE-equivalent**, a warning is
emitted); `ExtraAllowedCommands` adds to the allowlist and `AllowedCommands` replaces it whole —
the replacement cancels `AllowInterpreters`.

<!-- settings:Orkeon:Tools:Shell -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AllowedCommands` | list of string | `[]` | A full replacement of the default allowlist, verbatim; it cancels `AllowInterpreters` and re-enables the git read-only restriction. Empty keeps the default allowlist. |
| `AllowInterpreters` | boolean | `false` | Enables the interpreters (`node`, `dotnet`, `npm`, `find`) and the mutating git subcommands — RCE-equivalent on the host, for trusted coding-agent hosts only. Default false. |
| `ExtraAllowedCommands` | list of string | `[]` | Commands added on top of the default (or replacement) allowlist, such as `make`. |
<!-- /settings -->

### `Orkeon:Tools:Email`

Bound by `AddOrkeonEmailTools(configuration)` — the shared runner host and `orkeon-repl` call
it — and validated lazily: an account is validated the first time a tool or an `orkeon email`
command uses it, every problem reported at once, and a value the binder cannot even read (a
misspelt right, a port in words) sets that one account aside instead of failing the host — a
broken section never breaks a crew that sends no mail. Secrets are never values here, only the
**names** of the environment variables that hold them (`Auth:PasswordEnvVar`,
`Auth:ClientSecretEnvVar`), read as the variable `ApiKeyEnvVar` names is
([above](#the-api-key-apikey-apikeyenvvar)): in the process environment, then — on Windows — in
the user's persistent scope (`HKCU\Environment`), where Orkeon Studio remembers a password typed
in **Settings › E-mail**; read, never copied into the process; the process environment alone on
Linux and macOS. Provider walkthroughs and the key-by-key table:
[E-mail tools](../guides/email.md#where-a-secret-variable-is-read).

- **`DefaultAccount`** — the account a call that names none uses (optional with a single account).
- **`CredentialsDirectory`** — the physical directory whose `email` subdirectory holds the OAuth
  tokens; a relative path is read from the settings file's directory. The runner mounts it at the
  internal root `/credentials` when an OAuth account is declared; default: `credentials` next to
  the per-user settings file.
- **`Screening:WithholdRejected`** — withhold the body of a message the prompt-injection screen
  rejects (default `false`: flag only).
- **`Accounts:<name>`** — one account. `Provider` is `Gmail`, `Outlook` or `Custom` (the default);
  `Rights` is **mandatory** (`Read, Organize, Draft, Send, Delete, Purge`); `Incoming:Security`
  and `Outgoing:Security` are `SslOnConnect`, `StartTls` or `None` — the last towards a loopback
  host only; an empty `Send:AllowedRecipients` allows nobody. The name holds letters, digits, `.`,
  `_` and `-`, starts with a letter or a digit (64 at most).

<!-- settings:Orkeon:Tools:Email -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `Accounts:<name>:Address` | string | — |  | The account's address; also the `From` of every message it sends. |
| `Accounts:<name>:Auth:ClientId` | string | — |  | OAuth client id (Google Cloud or Microsoft Entra application). |
| `Accounts:<name>:Auth:ClientSecretEnvVar` | string | — |  | Name of the environment variable holding the OAuth client secret (Google desktop clients). |
| `Accounts:<name>:Auth:Method` | enum | — | `Password`, `OAuth2` | `Password` or `OAuth2`. |
| `Accounts:<name>:Auth:PasswordEnvVar` | string | — |  | Name of the environment variable holding the password or app password. |
| `Accounts:<name>:Auth:Tenant` | string | — |  | Microsoft tenant: `consumers` (default), `organizations`, `common` or a tenant id. |
| `Accounts:<name>:Auth:Username` | string | — |  | Login name; defaults to the address. |
| `Accounts:<name>:DisplayName` | string | — |  | Display name paired with `Address` in `From`. |
| `Accounts:<name>:Incoming:Host` | string | — |  | Server host name. |
| `Accounts:<name>:Incoming:Port` | integer | — |  | Server port. |
| `Accounts:<name>:Incoming:Protocol` | enum | — | `Imap`, `Pop3`, `Graph` | `Imap`, `Pop3` or `Graph`; the preset decides when unset. |
| `Accounts:<name>:Incoming:Security` | enum | — | `SslOnConnect`, `StartTls`, `None` | Transport security. |
| `Accounts:<name>:Outgoing:Host` | string | — |  | Server host name. |
| `Accounts:<name>:Outgoing:Port` | integer | — |  | Server port. |
| `Accounts:<name>:Outgoing:Protocol` | enum | — | `Smtp`, `Graph` | `Smtp` or `Graph`; the preset decides when unset. |
| `Accounts:<name>:Outgoing:Security` | enum | — | `SslOnConnect`, `StartTls`, `None` | Transport security. |
| `Accounts:<name>:Provider` | enum | `"Custom"` | `Custom`, `Gmail`, `Outlook` | The preset: `Gmail`, `Outlook` or `Custom` (the default). |
| `Accounts:<name>:Rights` | enum | `"None"` | `None`, `Read`, `Organize`, `Draft`, `Send`, `Delete`, `Purge` | What an agent may do, e.g. `"Read, Organize, Draft"`. Mandatory. |
| `Accounts:<name>:SaveSentCopy` | boolean | — |  | Whether a sent message is appended to the Sent folder. Unset follows the preset: Gmail, Outlook and Graph file sent mail themselves, a custom server usually does not. |
| `Accounts:<name>:Send:AllowedRecipients` | list of string | `[]` |  | Who may receive mail from this account: an address, `*@domain`, or `*` for anyone. Empty means nobody: sending is closed until an operator opens it. |
| `Accounts:<name>:Send:MaxPerHour` | integer | — |  | Most messages this process sends per hour from the account. Unset means no cap. |
| `Accounts:<name>:Send:MaxRecipients` | integer | — |  | Most recipients one message may have. Unset leaves the limit to the server. |
| `Accounts:<name>:TimeoutSeconds` | integer | — |  | Protocol timeout in seconds. Unset keeps the library default. |
| `CredentialsDirectory` | string | — |  | Physical directory holding the OAuth tokens, for a host whose per-user settings directory is not the right one (a service account). Read by the host, not by the tools. |
| `DefaultAccount` | string | — |  | The account a call uses when it names none. Optional with a single account. |
| `Screening:WithholdRejected` | boolean | `false` |  | When true, a message the prompt-injection detector rejects has its body withheld. Off by default: the detector was tuned on web pages, and newsletters trip it. |
<!-- /settings -->

### `MCP`

MCP client connections (`MCP:Servers:<name>`) and the switch `MCP:Enabled`. `MCP:EnableServer` is
removed: a section that still carries it is refused at startup, naming `orkeon mcp serve` (GAP-24).
Read by `RunnerHost` (`orkeon run`, `orkeon-host`, `orkeon mcp serve`) when `MCP:Servers` declares
at least one server and `MCP:Enabled` is not `false` — the servers are connected before the crew
loads (STUDIO-21), by `orkeon-host` once at startup, before its first message (GAP-11), and by
`orkeon mcp serve` before it serves; library hosts call `AddOrkeonMcp(configuration)` or the
`AddOrkeonInfrastructure(configuration)` overload — see
[MCP integration](../architecture/mcp.md).

<!-- settings:MCP -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `Enabled` | boolean | `true` |  | Whether MCP is enabled at all. |
| `Servers:<name>:Args` | list of string | `[]` |  | Command-line arguments for the server process. |
| `Servers:<name>:Command` | string | `""` |  | Command to launch the MCP server process (for stdio transport). |
| `Servers:<name>:Env:<name>` | string | — |  | Environment variables for the server process. |
| `Servers:<name>:Transport` | enum | `"Stdio"` | `Stdio`, `Sse` | Transport type to use. |
| `Servers:<name>:Url` | uri | — |  | URL endpoint for SSE transport. |
<!-- /settings -->

### `MCP:Server`

How the server of `orkeon mcp serve` introduces itself (it exposes tools only). Read by
`AddOrkeonMcpServer`, which `orkeon mcp serve` calls.

<!-- settings:MCP:Server -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Name` | string | `"Orkeon"` | Server name advertised during MCP initialization. |
| `Version` | string | `"1.0.0"` | Server version advertised during MCP initialization. |
<!-- /settings -->

### `RaggableTree`

Codebase indexing: `Enabled`, and `Embedding` — nothing else: any other key, another provider name
or a number that is no whole number fails the host at startup; what an index covers is set per
`index_codebase` call. **Opt-out in runner hosts**: `RunnerHost` registers it by default,
`RaggableTree:Enabled = false` disables; library consumers call `AddRaggableTree(options)`
explicitly.

<!-- settings:RaggableTree -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `Embedding:ApiKey` | string, secret | — |  | The key of a remote provider. The local provider needs none. |
| `Embedding:BaseUrl` | string | — |  | The `http://` or `https://` address of the provider's endpoint, when it is not the provider's own. |
| `Embedding:Dimensions` | integer | — |  | The size of the vectors, for a model that lets it be chosen. Left out, the model's own. |
| `Embedding:MaxTextChars` | integer | — |  | The most characters of one text sent to the model; what is longer is cut. Left out, the provider's own limit. |
| `Embedding:Model` | string | — |  | The embedding model. Left out, the provider uses its own — `bge-micro-v2` for the local one. |
| `Embedding:Provider` | enum | `"LocalSmartComponents"` | `None`, `OpenAI`, `Ollama`, `Onnx`, `LocalSmartComponents` | Who computes the embeddings, any case. The default runs on the machine: no key, no network. |
| `Enabled` | boolean | `true` |  | Whether the index and its tools are registered. `false` leaves them out of the host. |
<!-- /settings -->

### `BRAVE_API_KEY`

Also read as a **configuration key** (not only an environment variable) to gate the Brave tool, by
`RunnerHost`.

<!-- settings:BRAVE_API_KEY -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `BRAVE_API_KEY` | string, secret | — | The key of the Brave Search API. Set — here or as the environment variable of the same name —, it registers the `brave_search` tool; left out, the tool is not offered. |
<!-- /settings -->

### `Orkeon:MultiModal`

Vision and content validation — no image is resized. Opt-in:
`AddOrkeonMultiModal(configuration)`.

<!-- settings:Orkeon:MultiModal -->
**Read by**: a host written in C# only, through `AddOrkeonMultiModal()` — no shipped binary reads this section.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | boolean | `true` | Whether multi-modal content is enabled. |
| `MaxAudioDurationSeconds` | integer | `300` | Maximum allowed audio duration in seconds (default: 300 seconds / 5 minutes). |
| `MaxImageSizeBytes` | integer | `20971520` | Maximum allowed image size in bytes (default: 20 MB). |
| `SupportedAudioFormats` | list of string | `["audio/wav", "audio/mp3", "audio/ogg"]` | List of supported audio MIME types. |
| `SupportedImageFormats` | list of string | `["image/png", "image/jpeg", "image/gif", "image/webp"]` | List of supported image MIME types. |
<!-- /settings -->

### `Plugins`

Plugin directory discovery. Opt-in: `AddOrkeonPlugins(fileSystem, configuration)` — see
[Plugins](../architecture/plugins.md).

<!-- settings:Plugins -->
**Read by**: a host written in C# only, through `AddOrkeonPlugins()` — no shipped binary reads this section.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ContinueOnError` | boolean | `false` | When `true`, a plugin assembly that fails to load or to configure its services is recorded in `IPluginRegistry.Failures` and the remaining candidates keep loading. When `false` (default), the first failure throws a `PluginLoadException` (fail fast). |
| `Directory` | string | `"/plugins"` | Virtual path (VFS) of the directory scanned for plugin assemblies. Must start with `/` and resolve inside a configured mount. Default: `/plugins`. |
| `SearchPattern` | string | `"*.dll"` | Simple glob pattern applied to candidate assembly file names (e.g. `*.dll`, `MyCompany.*.dll`). Candidates must additionally carry the `.dll` extension regardless of the pattern. Default: `*.dll`. |
| `SharedAssemblyPrefixes` | list of string | `["Orkeon.", "Orkeon.Rag.Abstractions", "Microsoft.Extensions."]` | Assembly simple-name prefixes that are never loaded into the plugin's isolated `AssemblyLoadContext`; resolution defers to the host (default) context instead, so contract types (e.g. `IOrkeonPlugin`, `IBaseTool`, `IServiceCollection`) keep a single identity shared between the host and the plugin. Defaults: `Orkeon.`, `Orkeon.Rag.Abstractions` and `Microsoft.Extensions.`. |
<!-- /settings -->

## Orchestration and persistence

How a crew is built and how it decides, and what of its execution is kept.

### `Orkeon:CrewFactory`

`StrictTools` fails the load of a crew on an unknown tool name (the runners default to `true`).
Read by `RunnerHost` → `CrewFactoryOptions`.

<!-- settings:Orkeon:CrewFactory -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `StrictTools` | boolean | `true` | Whether a tool a crew names and the registry does not hold fails the crew's load, listing the unknown names and the available ones. `false` drops the tool with a warning instead — the library's lenient behaviour, which the shipped hosts turn off. |
<!-- /settings -->

### `Orkeon:Consensus`

Consensual-mode voting — the modes and what a ballot is are in
[Process types](../orchestration/process-types.md). Registered by `AddOrkeonInfrastructure()`; the
section gates behavior.

<!-- settings:Orkeon:Consensus -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `EnableDiscussion` | boolean | `true` |  | Whether discussion rounds are enabled when consensus is not reached. During discussion, agents receive the context of other agents' results. |
| `FallbackStrategy` | enum | `"AcceptBestScore"` | `AcceptBestScore`, `Fail`, `ManagerDecision` | The fallback strategy when consensus cannot be reached. |
| `MaxVotingRounds` | integer | `3` |  | The maximum number of voting rounds before applying fallback. A round runs every agent on the task, then collects one ballot per agent. |
| `RoleWeights:<name>` | number | — |  | Role-based weights for weighted consensus voting. Key is the agent role, value is the weight multiplier. |
| `VotingOptions:AllowAbstention` | boolean | `true` |  | Whether abstention is allowed. When true (the default), an abstention only counts toward the quorum. When false, an abstention counts as a vote against every choice: it stays in the denominator of each share, and it breaks unanimity. A Borda count, which has no share threshold, then reaches no consensus while anyone abstains. |
| `VotingOptions:ConsensusThreshold` | number | `66.7` |  | The consensus threshold percentage. Used by SuperMajority and WeightedConsensus types. |
| `VotingOptions:ConsensusType` | enum | `"Majority"` | `Majority`, `SuperMajority`, `Unanimity`, `WeightedConsensus`, `BordaCount` | The consensus type to use. |
| `VotingOptions:QuorumPercent` | number | `50` |  | The quorum, in percent: the minimum share of expressed ballots among all ballots. Below it, no consensus is reached, whatever the expressed ballots say. |
| `VotingOptions:UseWeightedVotes` | boolean | `false` |  | Whether to use weighted votes. |
<!-- /settings -->

### `Orkeon:ExecutionState:Persistence`

Durable crew execution states (`ScopedCrewExecutionStateManager`). Opt-in:
`AddCrewExecutionStatePersistence(configuration)` — auto-called by
`AddOrkeonInfrastructure(configuration)` when the section exists; requires an `IStateStore`.

<!-- settings:Orkeon:ExecutionState:Persistence -->
**Read by**: a host written in C# only, through `AddCrewExecutionStatePersistence()` — no shipped binary reads this section.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `DeleteFromStoreOnArchive` | boolean | `false` | When `true`, archiving an execution (completion or expiry cleanup) also deletes its persisted entry from the state store. Default: `false` — the final snapshot is kept in the store so that status queries keep working after the in-memory entry is evicted. |
| `Enabled` | boolean | `false` | Enables durable persistence of execution states. Default: `false` (in-memory only, identical to the historical behavior). |
<!-- /settings -->

### `Orkeon:Checkpointing`

The Postgres state store (`CheckpointingExtensions`). Opt-in:
`AddOrkeonPostgresCheckpointing(configuration)`.

<!-- settings:Orkeon:Checkpointing -->
**Read by**: a host written in C# only, through `AddOrkeonPostgresCheckpointing()` — no shipped binary reads this section.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AutoMigrate` | boolean | `true` | Whether to auto-create the schema and tables on first use. |
| `ConnectionString` | string, secret | — | The PostgreSQL connection string. |
| `MaxHistoryPerSession` | integer | `1000` | The maximum number of version entries per session (default: 1000). |
| `SchemaName` | string | `"orkeon"` | The schema name (default: "orkeon"). |
<!-- /settings -->

## Scripts and console

The sandbox of `.ork.ts` scripts, the esbuild toolchain, and the interactive console.

### `Orkeon:Scripting:Limits`

The Jint sandbox of every script engine (`Orkeon.Scripting`): about 100 MB of memory, a recursion
depth of 64 and 30 s by default.

<!-- settings:Orkeon:Scripting:Limits -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ExecutionTimeout` | duration | `"00:00:30"` | Maximum wall-clock time the engine is allowed to run, in total — the time spent waiting for a tool or a model counts. Default: 30 seconds, a ceiling for scripts of unknown provenance; a trusted long run raises it. |
| `MemoryLimitBytes` | integer | `104857600` | Maximum cumulative memory the engine is allowed to allocate, in bytes — counted since the engine was created, not at its peak. Default: 100 MB, a ceiling for scripts of unknown provenance; a trusted run that needs more raises it. |
| `RecursionLimit` | integer | `64` | Maximum recursion depth before the engine throws. Default: 64: it catches runaway recursion well before the .NET stack overflows and leaves room for legitimately nested script logic. A trusted script may raise it. |
<!-- /settings -->

### `Orkeon:Scripting:Toolchain`

esbuild toolchain resolution: `EsbuildPath` comes first in the lookup order. Read by
`EsbuildTranspiler.Create` — `orkeon run`, the shared runner, `orkeon doctor`, `orkeon forge`,
`*.cmd.ts` commands.

<!-- settings:Orkeon:Scripting:Toolchain -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `EsbuildPath` | string | — | Absolute path to the esbuild binary. When null, the transpiler falls back to the `ORKEON_ESBUILD_PATH` environment variable, then to a binary bundled next to the application (`esbuild-bin/esbuild`), then to a PATH lookup. |
| `EsbuildTimeout` | duration | `"00:00:30"` | Maximum time esbuild has to transpile a single source. Default: 30 s. |
<!-- /settings -->

### `Orkeon:Cli:ScriptCommands`

TypeScript CLI command discovery (`Orkeon.Cli.Commands.Scripting`), and under `Limits` a sandbox
profile tighter than the scripts' own.

<!-- settings:Orkeon:Cli:ScriptCommands -->
**Read by**: `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ContinueOnConflict` | boolean | `true` | When false, two scripts declaring the same command name is a fatal startup error. |
| `Directories` | list of string | `[]` | Virtual paths scanned for `*.cmd.ts`. First directory wins on conflict. |
| `Enabled` | boolean | `true` | Master switch. False ⇒ discovery is skipped, registry is empty. |
| `EsbuildTranspile` | boolean | `true` | When false, `*.cmd.js` only (no TS transpile). Test/debug; production keeps this true. |
| `FailFastOnInvalidScript` | boolean | `false` | When true, the first invalid script aborts the loader (use in CI). |
| `FallbackCommandName` | string | `"assistant"` | Name of the scripted command that handles free text (a REPL line not prefixed with `/`). In the coding-agent surface this is the interactive assistant wired to the `main-loop` crew. Empty/unknown ⇒ free text yields the unknown-command message instead of being routed. |
| `Limits:ExecutionTimeout` | duration | `"00:05:00"` | Hard timeout for a single command invocation. Default: 5 minutes. |
| `Limits:MemoryLimitBytes` | integer | `67108864` | Memory cap in bytes. Default: 64 MB (spec §8.1 CLI profile). |
| `Limits:RecursionLimit` | integer | `100` | Recursion depth cap. Default: 100 (inherited from spec §8.1 — same as global). |
| `MaxScripts` | integer | `50` | Hard cap on the number of engines kept in cache. |
<!-- /settings -->

### `Orkeon:Cli:ScriptHost`

Crew resolution for `<name>/crew.ork.ts` (`ScriptHostFacade`).

<!-- settings:Orkeon:Cli:ScriptHost -->
**Read by**: `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `CrewDirectories` | list of string | `[]` | Virtual directories under which crews live as `<dir>/<name>/crew.ork.ts`. Resolution tries each directory in order; first match wins. |
| `CrewFileName` | string | `"crew.ork.ts"` | The crew entry-file name resolved under each `<dir>/<name>/`. |
| `RunCrewTimeout` | duration | `"00:10:00"` | Upper bound on a single synchronous `script-host.runCrew` call. When it expires, the calling script receives a clear `TimeoutException`, the abandoned run is cancelled cooperatively, and the engine/REPL thread is always released. Default: 10 minutes — generous on purpose, since `runCrew` is meant for short crews (long workflows should go through `runCrewAsync`'s ticket cycle). Zero or a negative value disables the bound. |
<!-- /settings -->

### `Orkeon:Cli:ConsoleStreaming`

Streamed `ctx.llm.act` deltas on the REPL console (`AddLlmConsoleStreaming(configuration)`).

<!-- settings:Orkeon:Cli:ConsoleStreaming -->
**Read by**: `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | boolean | `false` | Whether each piece of an answer is written to the console as the model sends it, instead of the whole answer at its end. |
<!-- /settings -->

### `Orkeon:Cli:Session`

The context window used by `token_budget` (`TokenBudgetTool`) and the TUI.

<!-- settings:Orkeon:Cli:Session -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ContextWindowTokens` | integer | — | The model's context window, in tokens: a number above zero. Unset is `200000`. |
<!-- /settings -->

### `Orkeon:Cli:Tui`

The TUI's spinner verbs.

<!-- settings:Orkeon:Cli:Tui -->
**Read by**: `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `SpinnerVerbs` | list of string | `[]` | The verbs the status line cycles through while the model works ("Thinking", "Reading"…). Left out or empty, the console's own list. |
<!-- /settings -->

## Service host and A2A

The daemon `orkeon-host`, its chat channel, and the A2A protocol — see
[Service host](../architecture/service-host.md).

### `Orkeon:Host`

The daemon (`Orkeon.Host`):

- **`Crews:<i>`** — each hosted crew: `Name` (unique, case-insensitively), `Path`,
  `Profile:MaxConcurrentRuns` (chat and A2A runs counted together), `Mounts` (the per-crew mount
  namespace) and `Description` (what the crew's A2A skill says it does).
- **`RunTimeout`**, **`ShutdownGracePeriod`**.
- **`LlmProfiles`** — the allow-list of `Llm:Profiles` the hosted crews may name — their agents,
  tasks and managers — and `Orkeon:Rag:LlmProfile` too; unset offers them all, `["default"]` the
  default alone; an entry naming an undefined profile, or a RAG profile the list leaves out,
  refuses the start.
- **`A2A`** — the A2A server, off by default (GAP-23): `Host` is `http://localhost`, and
  `http://+` listens on every interface; `Crews` lists the crews other agents may run, by name —
  one skill each, a task being a run of that crew; none by default. An enabled section that exposes
  no crew or a crew `Crews` does not declare, a malformed `Host` or `Port`, or a listener beyond
  the loopback while `A2A:Security` declares no authentication scheme nor mutual TLS refuses the
  start — see [Service host](../architecture/service-host.md#5-other-agents-a2a).

<!-- settings:Orkeon:Host -->
**Read by**: `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `A2A:Crews` | list of string | `[]` | The hosted crews other agents may run, by name — one skill each. None by default. |
| `A2A:Enabled` | boolean | `false` | Serves the exposed crews over A2A. Off, the host listens on no port. |
| `A2A:Host` | string | `"http://localhost"` | The listener's scheme and host name: `http://localhost` by default, `http://+` for every interface. No port (that is `Port`) and no path. |
| `A2A:Port` | integer | `5002` | The listener's port. |
| `Crews:<i>:Description` | string | — | What the crew does, in a sentence: the description of its skill on the A2A agent card when `Orkeon:Host:A2A` exposes it. Read from the configuration, never from the crew definition — the card is built without loading a crew. Optional. |
| `Crews:<i>:Mounts` | list of string | `[]` | The folders this host grants THIS crew, as mount strings (`<physical>:<virtual>:<rights>`): a mount namespace of its own for each run, so two hosted crews may both address `/output` over two different folders. Empty (the default) keeps the host's mounts for that crew. A granted folder must lie under the workspace root, or under `PathSecurity:AdditionalAllowedDirectories`. |
| `Crews:<i>:Name` | string | `""` | The name the channels and the operator use to refer to it. |
| `Crews:<i>:Path` | string | `""` | Path to the crew definition — a YAML file, a multi-file crew directory, or an `.ork.ts` script. The same targets `orkeon run` accepts. |
| `Crews:<i>:Profile:MaxConcurrentRuns` | integer | `4` | How many runs of this crew may be in flight at once. Bounded on purpose: a daemon that accepts unlimited concurrent runs is a daemon that dies under its first burst. |
| `LlmProfiles` | list of string | `[]` | The LLM profiles (`Llm:Profiles:<name>`) the hosted crews may name. The service runs crews it does not control: unset, every profile the configuration defines is offered; set, only those listed — a crew naming another one fails to load, the run with it. The default profile (the `Llm` section) is always offered, so `["default"]` offers it alone. Every entry must name a defined profile. |
| `RunTimeout` | duration | `"00:30:00"` | How long a run may take before the host cancels it. A daemon has no user watching to press Ctrl-C, so an unbounded run is a stuck daemon. |
| `ShutdownGracePeriod` | duration | `"00:00:20"` | How long the host waits for in-flight runs on shutdown before giving up. Systemd sends SIGKILL after its own timeout, so this must stay under it. |
<!-- /settings -->

### `Orkeon:Host:Discord`

The Discord channel. `Routes` maps a Discord channel id to a crew name: a thread opened in that
channel starts that crew; `DefaultCrew` is the crew an unrouted channel reaches, the first declared
crew when unset. A route to an undeclared crew, a route key that is not a channel id, or an unknown
`DefaultCrew` refuses the start.

<!-- settings:Orkeon:Host:Discord -->
**Read by**: `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AllowedUserIds` | list of string | `[]` | Discord user ids allowed to talk to the bot. **Empty denies everyone** — a bot on a public server with an open door spends someone's API budget on strangers. |
| `DefaultCrew` | string | — | The crew a room without a route reaches. Unset, it is the first crew declared under `Orkeon:Host:Crews`; set, it must name one of them. |
| `Enabled` | boolean | `false` | Whether the channel is switched on. Off unless a deployment says otherwise. |
| `GuildIds` | list of string | `[]` | Guild (server) ids to register the slash commands in. **Empty registers them globally**, which needs no configuration but is cached by Discord for up to an hour; naming guilds makes `/status` and `/stop` available immediately — the dev loop. |
| `ProgressInterval` | duration | `"00:00:02"` | How long progress updates are spaced out. Discord's rate limit is per channel and unforgiving; a message per agent thought would exhaust it inside one crew. |
| `Routes:<name>` | string | — | Which crew each room reaches: a Discord channel id (the channel threads are opened in) mapped to a hosted crew's name. A thread opened in a routed channel starts that crew; a thread anywhere else starts `DefaultCrew`. A route to a crew the host does not declare, or a key that is not a channel id, refuses the start. |
| `TokenEnvironmentVariable` | string | `"ORKEON_DISCORD_TOKEN"` | Name of the environment variable holding the bot token. |
<!-- /settings -->

### `A2A`

A2A server and client; `EnableServer` also registers the hosted service that starts the server
with the generic host. Opt-in: `AddOrkeonA2A(configuration)`. `orkeon-host` reads the card's
identity and `A2A:Security` here, and refuses `EnableServer`, `Host` and `Port` at start — its
listener is `Orkeon:Host:A2A`, [above](#orkeonhost); see
[A2A conformance](./a2a-conformance.md#activation).

<!-- settings:A2A -->
**Read by**: `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AgentDescription` | string | `"Orkeon A2A Agent"` | The agent description advertised in the agent card. |
| `AgentName` | string | `"Orkeon"` | The agent name advertised in the agent card. |
| `AgentVersion` | string | `"1.0.0"` | The agent version advertised in the agent card. |
| `ContactUrl` | uri | — | Contact URL for the provider field in the agent card. |
| `EnableServer` | boolean | `false` | Whether to run an A2A server exposing local agents for remote task submission: registers `IA2AServer` and a hosted service that starts it with the host. |
| `Host` | string | `"http://localhost"` | The host/prefix for the A2A HTTP server (e.g., "http://localhost"). |
| `Organization` | string | — | Organization name for the provider field in the agent card. |
| `Port` | integer | `5002` | The port for the A2A HTTP server to listen on. |
| `TimeoutSeconds` | integer | `30` | Timeout in seconds for outbound HTTP requests to remote agents. |
<!-- /settings -->

### `A2A:Security`

Mutual TLS and authentication of the A2A endpoints: each scheme of `AllowedAuthSchemes` (`Bearer`,
`ApiKey`) needs its validator or the server refuses to start; `ApiKeySecretNames` are the names of
the secrets holding the accepted keys, read through `ISecretProvider`; client side,
`ClientCredentialSecretName` is the secret `A2AClient` sends as `Authorization`. Same opt-in as
[`A2A`](#a2a) — see [Security](../architecture/security.md#a2a-mutual-tls).

<!-- settings:A2A:Security -->
**Read by**: `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AllowedAuthSchemes` | list of string | `[]` | Authentication schemes the server accepts on its task endpoints: `Bearer` and/or `ApiKey`. Empty means no authentication is required. Each declared scheme is validated, not merely matched (`A2ACredentialValidator`): a bearer token by a registered `IAuthenticationProvider` (`A2A:Security:AzureAD` / `A2A:Security:Oidc`), an API key against `ApiKeySecretNames`. The server refuses to start when a declared scheme has no validator. |
| `ApiKeySecretNames` | list of string | `[]` | Names of the secrets holding the API keys the `ApiKey` scheme accepts (`Authorization: ApiKey <key>`). Each name is read through the `ISecretProvider` on every request — `ORKEON_<NAME>` with the default chain — so keys never sit in the configuration file and rotate without a restart. |
| `ClientAuthScheme` | string | — | Client side: the scheme of the credential the client sends on every task call (`Authorization: <scheme> <credential>`) — `Bearer` or `ApiKey`, matching what the peer declares in its `AllowedAuthSchemes`. Empty means the client sends no `Authorization` header. Agent-card discovery never carries it. |
| `ClientCertificatePassword` | string, secret | — | Password for the client certificate private key. |
| `ClientCertificatePath` | string | — | Path to the client certificate file for mTLS authentication. |
| `ClientCredentialSecretName` | string | — | Client side: name of the secret holding the credential sent with `ClientAuthScheme`, read through the `ISecretProvider` on every call (`ORKEON_<NAME>` with the default chain) so it never sits in the configuration file and rotates without a restart. A call fails before any request when it cannot be read. |
| `RequireMutualTls` | boolean | `false` | Whether mTLS is required for server-to-server communication. When enabled, the server requires a client certificate that chains to one of the `TrustedCertificateAuthorities` or matches one of the `TrustedClientCertificateThumbprints`; starting the server without any configured trust anchor throws (fail-closed) — mere presence and date validity of a certificate is not authentication. |
| `TrustedCertificateAuthorities` | list of string | `[]` | List of trusted certificate authority certificate paths. Client side: when set, only servers presenting certificates signed by these CAs are trusted (private-CA pinning). Server side: when `RequireMutualTls` is enabled, incoming client certificates must chain to one of these CAs (unless pinned via `TrustedClientCertificateThumbprints`). |
| `TrustedClientCertificateThumbprints` | list of string | `[]` | Thumbprints (hex, case-insensitive) of client certificates accepted by the server when `RequireMutualTls` is enabled — exact pinning, checked before the `TrustedCertificateAuthorities` chain validation. Pinned certificates are still rejected outside their validity window. |
<!-- /settings -->

### `A2A:Security:AzureAD`

The Azure AD validator of bearer tokens.

<!-- settings:A2A:Security:AzureAD -->
**Read by**: `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Authority` | string | `""` | Authority URL (e.g. https://login.microsoftonline.com/{tenantId}/v2.0). |
| `ClientId` | string | `""` | Application (client) ID registered in Azure AD. |
| `TenantId` | string | `""` | Azure AD tenant ID. |
| `ValidAudiences` | list of string | `[]` | Valid audiences for token validation. |
| `ValidIssuers` | list of string | `[]` | Valid token issuers. Defaults to standard Azure AD issuers for the tenant. |
<!-- /settings -->

### `A2A:Security:Oidc`

The generic OIDC validator of bearer tokens.

<!-- settings:A2A:Security:Oidc -->
**Read by**: `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Authority` | string | `""` | OIDC authority URL (e.g. https://login.example.com/realms/myapp). |
| `ClientId` | string | `""` | Client ID registered with the OIDC provider. |
| `RequireHttpsMetadata` | boolean | `true` | Whether to require HTTPS for the metadata endpoint. Defaults to true. |
| `ValidAudiences` | list of string | `[]` | Valid audiences for token validation. |
<!-- /settings -->

## Observability

Logs, traces and metrics.

### `Logging`

.NET's logging configuration. A level is one of `Trace`, `Debug`, `Information`, `Warning`,
`Error`, `Critical`, `None`, any case; the section stays open beyond its levels
([above](#when-a-setting-is-refused)).

<!-- settings:Logging -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `Console` | any | — |  | The console logger's own section: its `LogLevel` by category, `FormatterName`, `FormatterOptions` and the other keys of .NET's console logger. |
| `LogLevel:<name>` | enum | — | `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None` | The lowest level logged, by category: `Default` for every category without a line of its own, a namespace such as `Orkeon.Infrastructure` for what it logs. A level that is none refuses the start, naming the key. |
<!-- /settings -->

### `Telemetry`

OpenTelemetry export. `OtlpEndpoint` is an `http://` or `https://` address, refused by its key
otherwise; the standard `OTEL_EXPORTER_OTLP_ENDPOINT` also works in the runners. `ExportToConsole`
and `PrometheusEndpoint` are removed and refused, naming the OTLP collector that replaces them:
the console exporter wrote on the stdout a program reads, and nothing served Prometheus (GAP-35).
Read by `AddOrkeonTelemetry(configuration)` — called by `AddOrkeonInfrastructure(configuration)`
and `RunnerHost`.

<!-- settings:Telemetry -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | boolean | `true` | Whether telemetry is enabled. Default is true. |
| `MaxMemoryMB` | integer | `2048` | The maximum memory threshold in MB for health checks. Default is 2048 MB. |
| `OtlpEndpoint` | string | — | The OTLP (OpenTelemetry Protocol) exporter endpoint. When set, traces and metrics are exported to this endpoint. Example: "http://localhost:4317" for gRPC or "http://localhost:4318" for HTTP. |
<!-- /settings -->

### `Orkeon:Monitoring`

The monitoring backend. Opt-in: `AddOrkeonMonitoring(configuration)`.

<!-- settings:Orkeon:Monitoring -->
**Read by**: a host written in C# only, through `AddOrkeonMonitoring()` — no shipped binary reads this section.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `MaxTraceHistory` | integer | `1000` | Maximum number of completed traces kept in the circular buffer. Older traces are evicted when this limit is reached. |
| `MetricsRetentionMinutes` | integer | `60` | Number of minutes to retain detailed metrics data. |
| `TraceSourcePrefix` | string | `"Orkeon"` | Prefix of the activity-source names whose traces are captured. Defaults to every Orkeon source; narrow it to watch one subsystem — or, in a test, to a name nothing else emits, since the listener is process-wide and would otherwise pick up unrelated activity. |
<!-- /settings -->

## Environment variables

Three kinds of environment variables reach Orkeon: those that **carry a setting**, those a binary
**reads by their name**, and those **a setting names**. `orkeon settings env` prints the same
three lists in the terminal, offline ([CLI](./cli.md#orkeon-settings)).

### Variables that carry a setting

Any setting of this page can come from the environment instead of the file: the variable is the
path of the setting, `__` between its levels. The order of the layers, the rule on case and the
two spellings a setting has under Linux and macOS are in
[Where settings are read from](#where-settings-are-read-from).

| Variable | What it is | Where it sits |
|---|---|---|
| `ORKEON_<Section>__<Key>` | Any setting, by its path: `ORKEON_Llm__Model` is `Llm:Model`, `ORKEON_RateLimiting__MaxConcurrentRequests` is `RateLimiting:MaxConcurrentRequests`, `ORKEON_Orkeon__Rag__Profile` is `Orkeon:Rag:Profile`. `ORKEON_Llm__ApiKey` is the variable `orkeon init` and Orkeon Studio propose for the key of the default model. | Over the settings file: the variable wins. |
| `<Section>__<Key>` | The same setting without the prefix: `Llm__Model`. | Under the settings file: the file wins. |
| `ORKEON_<NAME>` | A secret, by its name: `ORKEON_TAVILY_API_KEY` is the key of `web_search`, `ORKEON_OPENAI_API_KEY` the key of `image_generation`. | The first stop of the secret chain, before `Secrets:<NAME>` in the file. |

A variable of the next table that starts with `ORKEON_` also passes through that layer, as a
key no setting is — `ORKEON_DEBUG` is the root key `DEBUG`. The configuration root stays open
([above](#when-a-setting-is-refused)): it refuses nothing.

### Variables a binary reads by their name

These are not settings: no key of this page stands for them, and the binary named reads the
variable itself. The list is the one the code carries — a test holds this table to it.

| Variable | Read by | Value | Effect |
|---|---|---|---|
| `ORKEON_ALLOW_EXTERNAL_MOUNTS` | `orkeon` | `1`, `true` or `yes` | Stands for `--allow-external-mounts` on every `orkeon run` and `orkeon rag`: a `--mount` may point outside the working directory. A sandboxed deployment sets it once, its own boundary being the isolation. |
| `ORKEON_DEBUG` | `orkeon` | `1`, `true` or `yes` | An unexpected error prints its exception — the chain of types and the stack — instead of one line. |
| `ORKEON_MCP_SERVE` | `orkeon` | set by `orkeon mcp serve`, never by hand | Marks every process `orkeon mcp serve` starts. Under it `orkeon mcp serve` refuses to start, so settings that list it among their own MCP servers cannot start it in a loop. |
| `ORKEON_ESBUILD_PATH` | `orkeon`, `orkeon-host`, `orkeon-repl` | the path of an esbuild executable | Where a `.ork.ts` script finds esbuild, after `Orkeon:Scripting:Toolchain:EsbuildPath` and before the copy shipped beside the binary. The launchers of an installation set it to the esbuild they ship when it is unset. |
| `ORKEON_LLM_API_KEY` | `orkeon` | an API key | The variable `orkeon llm probe` and `orkeon llm models` read the key from when `--api-key-env` names no other. |
| `BRAVE_API_KEY` | `orkeon`, `orkeon-host` | a Brave Search API key | Registers the `brave_search` tool. It is read as a configuration key first, so the settings file and `ORKEON_BRAVE_API_KEY` give it too. |
| `ORKEON_DISCORD_TOKEN` | `orkeon-host` | a Discord bot token | The token of the Discord channel: the default of `Orkeon:Host:Discord:TokenEnvironmentVariable`, which may name another variable. |
| `OLLAMA_BASE_URL` | `orkeon`, `orkeon-host`, `orkeon-repl` | the address of an Ollama server | The address the Ollama provider uses when it is given none: `Llm:BaseUrl` wins over it, and `http://localhost:11434` is what remains without either. |
| `ORKEON_CLI_DIR` | `orkeon-studio`, `orkeon-studio-config`, `orkeon-studio-run` | a directory | Where Orkeon Studio looks for the `orkeon` executable, after `--cli-dir` and its own install directory, before `PATH`. |
| `ORKEON_STUDIO_TEAMS_ROOT` | `orkeon-studio`, `orkeon-studio-run` | a fully qualified directory | The folder Orkeon Studio keeps its teams in. It wins over `--teams-root` and over the folder chosen in Settings › Studio. |
| `ORKEON_CUSTOM_LLM_API_KEY` | `orkeon-studio`, `orkeon-studio-config` | an API key | The variable Orkeon Studio proposes for the key of an OpenAI-compatible model setting, and writes in that setting's `ApiKeyEnvVar`: a run reads it through that key, not by this name. |
| `TUI_DRIVER` | `orkeon-repl`, `orkeon-studio-config`, `orkeon-studio-run` | `windows`, `dotnet` or `ansi` | The Terminal.Gui driver of the text interfaces, for diagnosis. Unset: `windows` under Windows, `dotnet` elsewhere — `ansi` draws nothing under WSL and in many container terminals. |
| `TUI_DIAG` | `orkeon-repl` | `1` | The split-pane console writes `[tui-diag]` lines on stderr while it starts: the terminal, the driver chosen. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `orkeon`, `orkeon-host` | the address of an OTLP collector | Turns the OpenTelemetry export on when `Telemetry:OtlpEndpoint` is empty; the exporter reads the address, and the other `OTEL_EXPORTER_OTLP_*` variables, by itself. |
| `XDG_CONFIG_HOME` | `orkeon`, `orkeon-host`, `orkeon-repl`, `orkeon-studio-config`, `orkeon-studio-run` | an absolute directory | Under Linux and macOS, the root of the per-user settings — `$XDG_CONFIG_HOME/Orkeon/appsettings.json`, `~/.config` when it is unset — and of the systemd user units `orkeon forge schedule` installs. |
| `CI` | `orkeon-repl`, `orkeon-studio-config`, `orkeon-studio-run` | `true` | No text interface opens: `orkeon-repl` falls back to its plain console, the two Studio terminal applications say they need a terminal and exit. |
| `TERM` | `orkeon-repl`, `orkeon-studio-config`, `orkeon-studio-run` | a terminal type | Under Linux, unset or empty has the effect of `CI=true`: there is no terminal to draw in. |

**Passed on, not read.** The `shell_command` tool hands the command it starts a fixed list of the
host's variables — `PATH`, `HOME`, the locale, the temporary folders, `DOTNET_ROOT`,
`NUGET_PACKAGES` and what Windows needs to start a program —, and the code sandbox builds a
smaller environment still: neither reads them as settings, and no key travels that way. `PATH`
is also where a `.ork.ts` script looks for esbuild last, and Orkeon Studio for `orkeon`; `HOME` is
read when the system names no profile folder, to place the per-user settings. The variables
written under `MCP:Servers:<name>:Env` are the operator's: Orkeon hands them to the server it
starts and reads none. The container image has variables of its own, read by its entry script
and not by the binaries — `ORKEON_RUNNER` and its neighbours
([three ways to run Orkeon](../getting-started/three-ways-to-run-orkeon.md#3-container)).

### Variables a setting names

A key that ends in `EnvVar` or `EnvironmentVariable` holds the **name** of a variable, never a
value: the secret stays out of the file. `orkeon settings <key>` describes each.

| Key | What the variable it names holds | Read from |
|---|---|---|
| `Llm:ApiKeyEnvVar` | The API key of the default model, read when no `Llm:ApiKey` is resolved ([the API key](#the-api-key-apikey-apikeyenvvar)). | The process environment, then the user's own variables under Windows. |
| `Llm:Profiles:<name>:ApiKeyEnvVar` | The API key of that profile, read the same way. | The process environment, then the user's own variables under Windows. |
| `Orkeon:Tools:Email:Accounts:<name>:Auth:PasswordEnvVar` | The password, or app password, of the e-mail account. | The process environment, then the user's own variables under Windows. |
| `Orkeon:Tools:Email:Accounts:<name>:Auth:ClientSecretEnvVar` | The OAuth client secret of the e-mail account. | The process environment, then the user's own variables under Windows. |
| `Orkeon:Rag:WebFallback:ApiKeyEnvVar` | The key of the web search the corrective RAG falls back to; empty, no key is sent. | The process environment. |
| `Orkeon:Host:Discord:TokenEnvironmentVariable` | The bot token of the Discord channel; the key names `ORKEON_DISCORD_TOKEN` when nothing sets it. | The process environment. |

The conventional variable of each provider — `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, … — is a
name to write in `ApiKeyEnvVar`:
[API keys: the variable per provider](./llm-providers-comparison.md#api-keys-the-variable-per-provider).

---

> **See also**: [Opt-in subsystems](./opt-in-subsystems.md) ·
> [Hosting](./hosting.md) ·
> [Memory system](../architecture/memory-system.md) ·
> [RAG pipeline](../architecture/rag-pipeline.md) ·
> [Back to index](../INDEX.md)
