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
   in this repository) → the global
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
   `01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data:/output:rw` — and several entries may
   declare one root when each carries one: `--mount-id <ulid>`, else the crew's `mounts:`
   block, selects the entry the run keeps, and every other entry of that root is
   **withdrawn** — its key is written to `null` at its own index, its base path is not
   whitelisted and its folder is not probed. `orkeon-repl` places its `--mount` arguments the
   same way, over its own layers — its command line (`--Orkeon:FileSystem:Mounts:<n>=…`) among
   them, judged as the file is.

The checks a command runs **before its host** — a reserved root, a root declared twice, a
selection with no answer, a mount folder that does not exist — read these same layers
(`RunnerSettings.ReadDeclaredMounts`, over `RunnerSettings.ReadConfiguration`): a mount declared
by a variable, `ORKEON_`-prefixed or not, is judged as the same line of the file.

**Nothing else is read**: neither the `appsettings.json`, `appsettings.{Environment}.json` and
`<binary>.settings.json` of the current directory nor, in `Development`, the user secrets — a file
of another project would otherwise add its profiles, MCP servers or mounts to the run. `orkeon-host` reads `./appsettings.json` as **its** settings
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
would a run launched from this shell use, absent any per-launch overlay?* A client's named
model profiles can ride layer 2: the profile elected as default may be written into the
file's `Llm` section whole — endpoint, model, timeout, thinking switch and the variable that holds its key (`ApiKeyEnvVar`), never
the key — so a manual terminal `orkeon run` or a scheduled team follows the same election,
its 600 s and its key included (that is what `llm-config` reflects), while a team that
elected a different profile can receive it as `ORKEON_Llm__*` variables on its own launch
only — every field the profile models, blank where it sets none, so nothing of the default,
its key least of all, reaches the team's endpoint; `llm-config` cannot see those, because
they exist nowhere until that launch starts. Every profile can also be written into the file's
`Llm:Profiles`, naming the variable that holds its key and never the key, as a host profile
a crew can name ([below](#named-profiles-llmprofiles)). No file is ever generated: the
composition is in-memory.

**A file to start from.** Every installation carries `appsettings.sample.json` at the root of
what its channel installs, beside `VERSION`: every key a shipped binary reads, by category, at
its default — produced from the settings catalogue, the one `orkeon settings` lists. No binary
loads it. Copied as it is in place of the settings file, it changes nothing: each key it writes
holds the value that key has when it is left out, and what cannot be written that way — a key
without a default, a secret, a list, an entry under a name you choose, a key a RAG profile sets,
the whole `Llm` section — is shown as a comment to uncomment. It is JSON with `//` comments and a
comma after every member, which the settings readers take (the runners); a
strict JSON parser does not.

## When a setting is refused

Every shipped host — `orkeon run` in every form (`--validate`, `--list-tools`, `mcp serve`, the
forge, `rag`, `email`), `orkeon-host` and `orkeon-repl` — judges every setting it reads **at its
start**, whether the run uses it or not: before the crew loads, before any model is called,
before a warning is printed or the telemetry starts. A refusal is one line naming the key — exit code
`1` for `orkeon` and `orkeon-repl`, `78` for `orkeon-host` — and `orkeon doctor` reports the same
refusals on the same file, one `runner-settings` row each. What is refused:

- **A value** the configuration binder cannot convert (`"Orkeon:Guardian:Enabled": "oui"`), or that
  a rule of its section refuses (`Orkeon:Rag:Retrieval:TopK` at `0`, an `Orkeon:Sqlite:TableName`
  SQLite cannot be given). The `Llm` section is read as strictly as its profiles: a
  `TimeoutSeconds` of `"600s"` is refused, `Thinking:Enabled` and
  `Grammar` are `true` or `false`, and a `Temperature` is a finite number — `NaN`, `Infinity` or
  `1e400` (which reads as infinity) parsed, then failed every request, since JSON writes no such
  number. So is every number a section reads as a float or a double (`Orkeon:Rag:Generation:Temperature`,
  `Orkeon:CrewMemory:MinScore`, …), which the binder takes as `NaN` or infinity without a word. A
  `Logging` level is one of `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`,
  any case.
- **A key** no section carries, against the shape its readers declare — the readers of one section
  together, a sub-section another reader declares included: `Orkeon:Guardian:Enabeld`,
  `Llm:Modle`. The refusal of an unknown key ends on the command that lists the
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
`Orkeon:MultiModal`, `Evaluation` and `Orkeon:VectorSearch`
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
| [Models](#models) | [`Evaluation`](#evaluation), [`Llm`](#llm), [`LlmLogging`](#llmlogging), [`Orkeon:CostTracking`](#orkeoncosttracking), [`Orkeon:Decision`](#orkeondecision), [`Orkeon:Kev`](#orkeonkev), [`Orkeon:Liquid`](#orkeonliquid), [`Orkeon:TokenCounter`](#orkeontokencounter), [`Orkeon:TypeSafe`](#orkeontypesafe) | 101 |
| [Rate and budgets](#rate-and-budgets) | [`RateLimiting`](#ratelimiting), [`TokenBudget`](#tokenbudget), [`ToolRateLimiting`](#toolratelimiting) | 11 |
| [Memory and vectors](#memory-and-vectors) | [`Memory`](#memory), [`Orkeon:ChromaDb`](#orkeonchromadb), [`Orkeon:CognitiveMemory`](#orkeoncognitivememory), [`Orkeon:CrewMemory`](#orkeoncrewmemory), [`Orkeon:EmbeddingCache`](#orkeonembeddingcache), [`Orkeon:Embeddings`](#orkeonembeddings), [`Orkeon:Encryption`](#orkeonencryption), [`Orkeon:LanceDb`](#orkeonlancedb), [`Orkeon:Pinecone`](#orkeonpinecone), [`Orkeon:Redis`](#orkeonredis), [`Orkeon:Sqlite`](#orkeonsqlite), [`Orkeon:VectorSearch`](#orkeonvectorsearch) | 69 |
| [RAG](#rag) | [`Orkeon:Rag`](#orkeonrag), [`Orkeon:Rag:Ingestion`](#orkeonragingestion), [`Orkeon:Rag:QueryRouting`](#orkeonragqueryrouting), [`Orkeon:Rag:Retrieval:Hybrid`](#orkeonragretrievalhybrid), [`Orkeon:Rag:WebFallback`](#orkeonragwebfallback) | 34 |
| [Files and sandbox](#files-and-sandbox) | [`Orkeon:CodeSandbox`](#orkeoncodesandbox), [`Orkeon:CodeSandbox:Docker`](#orkeoncodesandboxdocker), [`Orkeon:FileSystem`](#orkeonfilesystem), [`Orkeon:Sandbox`](#orkeonsandbox), [`PathSecurity`](#pathsecurity) | 33 |
| [Security](#security) | [`Orkeon:Dlp`](#orkeondlp), [`Orkeon:Guardian`](#orkeonguardian), [`Orkeon:Security:PermissionGate`](#orkeonsecuritypermissiongate), [`Secrets`](#secrets), [`Security:Audit`](#securityaudit), [`Security:Prompt`](#securityprompt), [`Security:ToolResults`](#securitytoolresults), [`Security:Url`](#securityurl), [`Security:Vault`](#securityvault) | 40 |
| [Tools](#tools) | [`BRAVE_API_KEY`](#brave_api_key), [`MCP`](#mcp), [`MCP:Server`](#mcpserver), [`Orkeon:MultiModal`](#orkeonmultimodal), [`Orkeon:Tools:Drive`](#orkeontoolsdrive), [`Orkeon:Tools:Email`](#orkeontoolsemail), [`Orkeon:Tools:Firefox`](#orkeontoolsfirefox), [`Orkeon:Tools:GitHub`](#orkeontoolsgithub), [`Orkeon:Tools:ResultLimits`](#orkeontoolsresultlimits), [`Orkeon:Tools:Shell`](#orkeontoolsshell), [`Plugins`](#plugins), [`RaggableTree`](#raggabletree) | 110 |
| [Orchestration and persistence](#orchestration-and-persistence) | [`Orkeon:Checkpointing`](#orkeoncheckpointing), [`Orkeon:Consensus`](#orkeonconsensus), [`Orkeon:CrewFactory`](#orkeoncrewfactory), [`Orkeon:ExecutionState:Persistence`](#orkeonexecutionstatepersistence), [`Orkeon:HumanInput`](#orkeonhumaninput), [`Orkeon:RunHistory`](#orkeonrunhistory) | 28 |
| [Scripts and console](#scripts-and-console) | [`Orkeon:Cli:ConsoleStreaming`](#orkeoncliconsolestreaming), [`Orkeon:Cli:Memory`](#orkeonclimemory), [`Orkeon:Cli:ScriptCommands`](#orkeoncliscriptcommands), [`Orkeon:Cli:ScriptHost`](#orkeoncliscripthost), [`Orkeon:Cli:Session`](#orkeonclisession), [`Orkeon:Cli:Tui`](#orkeonclitui), [`Orkeon:Scripting:AgentInstances`](#orkeonscriptingagentinstances), [`Orkeon:Scripting:AgentMemory`](#orkeonscriptingagentmemory), [`Orkeon:Scripting:Limits`](#orkeonscriptinglimits), [`Orkeon:Scripting:Toolchain`](#orkeonscriptingtoolchain) | 35 |
| [Service host and A2A](#service-host-and-a2a) | [`A2A`](#a2a), [`A2A:Security`](#a2asecurity), [`A2A:Security:AzureAD`](#a2asecurityazuread), [`A2A:Security:Oidc`](#a2asecurityoidc), [`Orkeon:Host`](#orkeonhost), [`Orkeon:Host:Discord`](#orkeonhostdiscord) | 59 |
| [Observability](#observability) | [`Logging`](#logging), [`Orkeon:Monitoring`](#orkeonmonitoring), [`Telemetry`](#telemetry) | 8 |

80 sections, 528 keys. Read by no shipped binary, only by a host written in C# (6): [`TokenBudget`](#tokenbudget), [`ToolRateLimiting`](#toolratelimiting), [`Orkeon:VectorSearch`](#orkeonvectorsearch), [`Orkeon:Dlp`](#orkeondlp), [`Orkeon:MultiModal`](#orkeonmultimodal), [`Orkeon:Monitoring`](#orkeonmonitoring).
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
order: the base URL alone when there is one — the same reading for the run and the CLI
(a vendor host such as `deepseek.com` → DeepSeek or `api.x.ai` → Grok; `/engines/` or port
`12434` → Docker Model Runner; port `11434` → Ollama at its root; any other address → an
OpenAI-compatible server, reported as silent when it has no path; an address no run serves —
not an absolute `http(s)` URL, or a vendor host under `/anthropic` — refuses the start) — then,
without a base URL, model-name patterns, then API-key shape; default `openai`. A section without `Model` runs on that provider's own default
model (the *Default (code)* column of the
[provider comparison](llm-providers-comparison.md#defaults-and-newer-models--catalogue-reviews-of-2026-09-19-2026-10-07-and-2026-11-11)),
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
| `Cache:SystemPrompt` | boolean | — | Marks the system prompt cacheable for every agent that sets no cache of its own, on a provider whose prompt cache is explicit (Anthropic). Left out, it is not. |
| `Cache:Tools` | boolean | — | Marks the tool definitions cacheable for every agent that sets no cache of its own, on a provider whose prompt cache is explicit (Anthropic). Left out, they are not. |
| `Cache:Ttl` | string | — | The cache lifetime, `5m` or `1h`; it needs `SystemPrompt` or `Tools`, and any other value refuses the load. Left out, the vendor default (5 minutes on Anthropic). |
| `CallMode` | string | `"Auto"` | How a call reaches the model: `Auto`, `Buffer` or `Stream`, case ignored. Left out, `Auto`: the call streams, and falls back on a buffered call when the provider refuses streaming. |
| `Grammar` | boolean | `false` | Whether the endpoint honours a GBNF grammar — a llama.cpp-compatible server, such as Docker Model Runner. Elsewhere a grammar is dropped with a warning. |
| `MaxRateLimitWait` | duration | `"00:05:00"` | The longest rate-limit window a call waits out before it fails: a provider that announces a longer one makes the call fail at once with a rate-limit error. `00:00:00` waits for nothing. |
| `MaxRetries` | integer | `10` | How many times a call that fails on a passing error is tried again; `0` never retries. |
| `MaxTokens` | integer | — | The most tokens an answer may hold: a pin. Left out, a request carries the documented maximum of its model, and 4096 for a model the catalogue does not know. |
| `Model` | string | — | The model's name. Left out, the provider runs its own default model. |
| `ModelCapabilities:<name>:AudioInput` | boolean | — | Whether the model takes audio parts as input. Left out, the built-in value of the model (false when the table does not know it). |
| `ModelCapabilities:<name>:AudioOutput` | boolean | — | Whether the model answers in audio. Recorded only: nothing sends or reads audio output. Left out, the built-in value of the model (false when the table does not know it). |
| `ModelCapabilities:<name>:ReasoningEfforts` | list of string | `[]` | The reasoning efforts the model takes: a list, or one comma-separated value; written empty, none. Left out, the built-in list of the model (no restriction for a model the table does not know) and the provider's own levels rule. |
| `ModelCapabilities:<name>:Vision` | boolean | — | Whether the model takes image parts as input. Left out, the built-in value of the model (false when the table does not know it). |
| `Profiles:<name>:ApiKey` | string, secret | — | The API key, in clear text. Prefer `ApiKeyEnvVar`: a key written here is in the settings file. |
| `Profiles:<name>:ApiKeyEnvVar` | string | — | The name of the environment variable that holds the API key, read when `ApiKey` resolves none: the process environment, then the Windows user scope. |
| `Profiles:<name>:BaseUrl` | string | — | The address of the provider's endpoint, `http://` or `https://`. Left out, the address of the provider the model's name or the key points to. |
| `Profiles:<name>:Cache:SystemPrompt` | boolean | — | Marks the system prompt cacheable for every agent that sets no cache of its own, on a provider whose prompt cache is explicit (Anthropic). Left out, it is not. |
| `Profiles:<name>:Cache:Tools` | boolean | — | Marks the tool definitions cacheable for every agent that sets no cache of its own, on a provider whose prompt cache is explicit (Anthropic). Left out, they are not. |
| `Profiles:<name>:Cache:Ttl` | string | — | The cache lifetime, `5m` or `1h`; it needs `SystemPrompt` or `Tools`, and any other value refuses the load. Left out, the vendor default (5 minutes on Anthropic). |
| `Profiles:<name>:CallMode` | string | `"Auto"` | How a call reaches the model: `Auto`, `Buffer` or `Stream`, case ignored. Left out, `Auto`: the call streams, and falls back on a buffered call when the provider refuses streaming. |
| `Profiles:<name>:Fallback` | list of string | `[]` | The profiles a call moves to, in order, when this one fails in a way a retry cannot cure: a transport error, a 5xx, a 429 once `MaxRetries` is spent, a timeout. A list of profile names, or one comma-separated value; `default` names the host's own. Left out, a failure is the call's. |
| `Profiles:<name>:FallbackBudgetSeconds` | integer | — | The longest the chain may spend before it stops trying the next profile, in seconds, counted from the first call. It is checked between two profiles and never cuts a call in flight. Left out, the chain is bounded only by each profile's own `TimeoutSeconds` and `MaxRetries`. |
| `Profiles:<name>:Grammar` | boolean | `false` | Whether the endpoint honours a GBNF grammar — a llama.cpp-compatible server, such as Docker Model Runner. Elsewhere a grammar is dropped with a warning. |
| `Profiles:<name>:MaxRateLimitWait` | duration | `"00:05:00"` | The longest rate-limit window a call waits out before it fails: a provider that announces a longer one makes the call fail at once with a rate-limit error. `00:00:00` waits for nothing. |
| `Profiles:<name>:MaxRetries` | integer | `10` | How many times a call that fails on a passing error is tried again; `0` never retries. |
| `Profiles:<name>:MaxTokens` | integer | — | The most tokens an answer may hold: a pin. Left out, a request carries the documented maximum of its model, and 4096 for a model the catalogue does not know. |
| `Profiles:<name>:Model` | string | — | The model's name. Left out, the provider runs its own default model. |
| `Profiles:<name>:Provider` | string | — | `mock`, the only value: the offline provider that answers from the JSONL file `Responses` names. Left out, the provider follows from `BaseUrl`, the model's name and the key. |
| `Profiles:<name>:Responses` | string | — | With `Provider: mock`, the virtual path of the JSONL file — one `{match, regex?, content, toolCalls?}` object per line — the mock provider answers from. |
| `Profiles:<name>:StreamIdleSeconds` | integer | — | The longest silence a streamed answer may hold between two of its chunks, in seconds. Left out, nothing bounds it: `TimeoutSeconds` alone bounds the whole call, streamed or not. A model that thinks before it writes may stay silent for a while: set it only above that silence, or turn its thinking off. |
| `Profiles:<name>:Temperature` | number | — | The sampling temperature. Left out, none is sent and the model applies its own. |
| `Profiles:<name>:Thinking:Effort` | string | — | How hard it thinks, in the provider's own words (`low`, `medium`, `high`…). Left out, the provider's own. |
| `Profiles:<name>:Thinking:Enabled` | boolean | — | Whether the model thinks before it answers. Left out, the provider's own behaviour. |
| `Profiles:<name>:TimeoutSeconds` | integer | `30` | How long one call may take, in seconds. Too short at its default for a model that thinks before it answers: write 600 for one, or turn its thinking off. |
| `Provider` | string | — | `mock`, the only value: the offline provider that answers from the JSONL file `Responses` names. Left out, the provider follows from `BaseUrl`, the model's name and the key. |
| `Replay:Cassette` | string | — | The virtual path of the cassette, a JSONL file; `record` and `replay` need it. |
| `Replay:Mode` | string | — | `record` writes every call of the run and its answer to the cassette; `replay` answers every call from it, and a call it does not hold fails; `off` does neither. Left out, `off`. |
| `Responses` | string | — | With `Provider: mock`, the virtual path of the JSONL file — one `{match, regex?, content, toolCalls?}` object per line — the mock provider answers from. |
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
  for a moderate one.
- **`Thinking:Enabled`, `Thinking:Effort`** are for thinking-capable providers.
- **`Profiles:<name>`** holds the named profiles a crew picks per agent or per task, each with
  the same keys as the section itself ([below](#named-profiles-llmprofiles)); a client can write
  one per model setting, naming the variable holding its key, never the key.
- **`AvailableModels`** is the model list a scripted `/model` REPL command can offer (string
  array, or one comma-separated string), read by `AddOrkeonSessionTools(configuration)`.

#### The timeout, and the models that think

`TimeoutSeconds` defaults to 30 s,
too short for a model that thinks before it answers (Kimi K2.6, DeepSeek V4 and GLM do so by
default): set 600 s, or turn thinking off with `Thinking:Enabled = false`. A call that hits the
timeout is retried once, then fails its task with a message naming the setting — it is never
reported as an empty answer.

The timeout bounds the whole call, streamed or not, the wait for the first token included.
`StreamIdleSeconds` adds a second bound, unset by
default: the longest silence between two streamed chunks. A model that thinks before it writes
may stay silent for a while, so set it above that silence, or turn thinking off. Either bound
elapsing is a failed call whose message names the setting — never an empty answer.

#### `MaxTokens` is a pin

`MaxTokens` is a **pin**: left
out, the request carries the model's **documented maximum output** from the
`LlmModelOutputLimits` catalogue (128K on `gpt-5.6-sol` and the Claude 5 generation, 384K on
`deepseek-flash`, 131 072 on the GLM-5 and Qwen 3.7/3.8 families, 65 536 on Gemini 3.x Flash —
see the [output caps table](llm-providers-comparison.md#output-caps--the-documented-maximum-per-model)),
no cap at all where the vendor documents none (Mistral, a local Ollama), and **4096 only for a
model the catalogue does not know** — a cap a reasoning model may spend thinking before it writes a word and answers empty (an empty final
answer fails the task rather than passing for a completed one). Pin it when the model is not in
the catalogue or when you want a tighter cap; a client's model profile can pin it as
`ORKEON_Llm__MaxTokens`. A catalogue cap the endpoint refuses is retried once without the
field, with a warning naming the model.

#### `Provider: mock` answers from a file

A profile — or the `Llm` section itself — written `"Provider": "mock", "Responses": "/llm-mock/answers.jsonl"` builds no vendor provider: it answers from a JSONL file read through the virtual file system, one `{"match", "regex"?, "content", "toolCalls"?}` object per line (the first line whose `match` fits the prompt wins; see [`orkeon run --llm-mock`](./cli.md#orkeon-run)). It makes no network call, so a YAML crew or an `.ork.ts` script runs offline with the same output every time. `mock` is the only value `Provider` takes; a provider otherwise follows from `BaseUrl`, the model's name and the key. `Responses` is required with it. `orkeon run --llm-mock <file>` writes both keys for every profile of the run. The mock is not one of the sixteen providers: it is a test double.

#### `Replay` records or replays the calls

`Llm:Replay:Mode` set to `record` writes every call a provider makes, with its answer, to the JSONL file `Llm:Replay:Cassette` names; `replay` answers every call from that file and calls no model; `off` (the default) does neither. The decorator sits in the provider factory, under the call-mode layer, so the cassette holds what really went over the wire. `orkeon run --llm-record` and `--llm-replay` set both keys and mount the folder; see [Record and replay LLM calls](../guides/testing.md). A mode without a cassette, or a mode that is none of the three, refuses the start naming the key.

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
   (`ORKEON_Llm__Profiles__<id>__ApiKey` for a profile), or what a client launch lays over
   its child. An existing installation keeps the key it had, and the environment still wins over
   the file.
2. **Otherwise the variable `ApiKeyEnvVar` names**, read in the process environment, then — on
   Windows — in the user's persistent scope (`HKCU\Environment`), where a client may remember a
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
field. An `ApiKey` written as a `${NAME}` placeholder — never expanded, so the text would go out as
the key — refuses the start
too, with the fix: `"ApiKeyEnvVar": "NAME"` ([a reference is never expanded](#a-variable-reference-is-never-expanded)). At startup the runner host says where each key came
from, never the key nor the variable's name — `LLM resolved: … apiKey=from the variable named by
Llm:ApiKeyEnvVar (user environment)`, then one `LLM profile <id>: apiKey=…` line per profile
offered to crews (`from configuration (…:ApiKey)`, `from the variable named by … (process
environment)`, `none`) — and warns once per section whose reference names a variable set nowhere,
by its path, on its logger and on stderr; `orkeon doctor`, `orkeon init`'s probe and the REPL say
the same. A profile `orkeon-host`'s allow-list hides is named on a line of its own and never warned
about: no crew can name it ([service host](../architecture/service-host.md)). A value left blank reads as
absent, for every key of the section (`Thinking:Effort` included): a client launch may blank the
default fields its team's setting does not set, and a section whose every value is blank
configures no default — the echo provider, with its warning. `orkeon init --api-key-env <name>`
writes `Llm:ApiKeyEnvVar`. The reference may name any variable: a settings file is trusted
configuration ([Security](../architecture/security.md#llm-keys-the-settings-file-names-the-variable)).

### A variable reference is never expanded

Orkeon **never expands** a variable in a setting: the text goes out as written. Two spellings are
recognised as a reference and refused — `${NAME}` (also `${env:NAME}`, `${NAME:-default}` and any
`${…}`, alone or inside a longer value such as `sk-${X}`) and `%NAME%` (the whole value). `$NAME`,
without braces, is no reference: it passes as written. Refused at start, before any MCP server
connects, naming the setting's path and never the value, with its correction: the `run` command, the
REPL and `doctor` stop (`run` exits 1), `orkeon-host` exits 78. A value a higher layer
covers is not the one read, so it is not judged. The settings judged, from
`Orkeon.Constants.Configuration` (`SettingPlaceholders`), keys compared without case:

| Settings | Correction stated |
|---|---|
| `Llm:ApiKey`, `Llm:Profiles:<name>:ApiKey`, `Orkeon:Embeddings:ApiKey` | write `"ApiKeyEnvVar": "<NAME>"` instead |
| `Llm:ApiKeyEnvVar`, `Llm:Profiles:<name>:ApiKeyEnvVar`, `Orkeon:Rag:WebFallback:ApiKeyEnvVar`, `Orkeon:Embeddings:ApiKeyEnvVar`, `Orkeon:Host:Discord:TokenEnvironmentVariable`, `A2A:Security:ApiKeySecretNames:<i>`, `Orkeon:Encryption:SecretName`, `Orkeon:Tools:GitHub:TokenVariable` | write the name alone, without `${` and `}` |
| `MCP:Servers:<id>:Command`, `:Args:<i>`, `:Url`; `Orkeon:Redis:ConnectionString`, `Orkeon:Sqlite:ConnectionString`; `RaggableTree:Embedding:ApiKey`, `Orkeon:Pinecone:ApiKey`, `Orkeon:LanceDb:ApiKey`; `Llm:BaseUrl`, `Llm:Profiles:<name>:BaseUrl`, `RaggableTree:Embedding:BaseUrl`, `Orkeon:ChromaDb:BaseUrl`, `Orkeon:Pinecone:Host`, `Orkeon:LanceDb:Endpoint`, `Orkeon:Embeddings:Endpoint`, `Orkeon:Rag:WebFallback:Endpoint`, `Telemetry:OtlpEndpoint`; `Security:Vault:AzureKeyVaultUri`, `A2A:Security:AzureAD:Authority`, `A2A:Security:Oidc:Authority`; `Orkeon:Tools:GitHub:Token` | write the value, or keep it out of the file through the environment layer `ORKEON_<path with __>` |
| `MCP:Servers:<id>:Env:<name>` | remove the entry: the server inherits the environment Orkeon runs in, set the variable there |
| `Secrets:<name>` | remove the entry and set `ORKEON_<name>`, which the secret chain reads first |
| `BRAVE_API_KEY` | remove the entry, Orkeon reads the variable |

An e-mail account holding a reference in `Incoming:Host`, `Outgoing:Host`, `Auth:PasswordEnvVar` or
`Auth:ClientSecretEnvVar`, or a drive account holding one in `Auth:ClientSecretEnvVar`, is not a
start refusal: the account is set aside, with its problem ([e-mail guide](../guides/email.md),
[drive guide](../guides/drive.md)).

### Named profiles (`Llm:Profiles`)

A host can offer more than one provider. Each child of `Llm:Profiles` is a **profile**: a
name, and a provider described with exactly the keys of the `Llm` section (`BaseUrl`,
`ApiKeyEnvVar`, `ApiKey`, `Model`, `Temperature`, `MaxTokens`, `TimeoutSeconds`, `MaxRetries`,
`Thinking`, `Cache`, `Grammar`, `CallMode`, `MaxRateLimitWait`, `StreamIdleSeconds`).
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
readings name each call's provider, and a client can break the tokens down per
provider. The profiles are validated when the host starts: `default` is a reserved name (it
designates the `Llm` section, and a crew may name it to bring an agent back to the default), and
an invalid `BaseUrl` (the same reading refuses it) or a value that is not a number fails the start with the key to fix. A
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
a scheduled team's launchers pass.

### `LlmLogging`

Tunes the capture of LLM exchanges. Read only when the run passes `--llm-log` or
`--llm-log-path`, which say where the log goes (`RunnerHost` → `AddLlmExchangeLogging(logDirectory, options)`):
no key of this section turns the log on or chooses its folder. A switch that is not
`true` or `false`, a length that is no whole number, or another key refuses the start of a run
passing `--llm-log`. Recipe: [follow the run and keep the LLM exchanges](../getting-started/run-your-first-example.md#follow-the-run-and-keep-the-llm-exchanges).

<!-- settings:LlmLogging -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `FullEmbeddingLog` | boolean | `true` | Whether an embedding vector is logged at full length. `false` replaces each one by its first and last two values: the exchange keeps its shape and loses its megabytes. |
| `LogStreamingExchanges` | boolean | `true` | Whether a streamed exchange is logged: its request, and the answer once the stream has ended. |
| `MaxBodyLengthChars` | integer | `0` | The most characters logged of one request or response body; `0` logs it whole. |
<!-- /settings -->

### `Orkeon:CostTracking`

LLM cost tracking: a default budget per crew (`DefaultCrewBudget`) and the price of a model, by
name or pattern (`CustomPricings`). Registered by `AddOrkeonInfrastructure()`; the section gates
behavior. The budget covers every LLM call made inside a crew run (YAML, `.ork.ts`, the service
host) and is summed per crew id: a YAML or C# crew takes a new id at each load, so in practice the
budget is per run; the calls a script makes itself through `ctx.llm` are summed under the crew's
name for the life of the process, apart from its orchestrated tasks. A call outside a crew is under
no budget. The call that crosses a cap completes; the next one is refused before anything is sent
and the task ends `BudgetExhausted`, never retried. The caps
on the number of calls are [`RateLimiting`](#ratelimiting).

<!-- settings:Orkeon:CostTracking -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `CustomPricings:<i>:CacheReadPricePerMillion` | number | — | Cost per million cache-read tokens; null when the model declares none. |
| `CustomPricings:<i>:CacheWritePricePerMillion` | number | — | Cost per million cache-write tokens; null when the model declares none. |
| `CustomPricings:<i>:CompletionPricePerMillion` | number | `0` | Cost per million completion (output) tokens in the specified currency. |
| `CustomPricings:<i>:Currency` | string | `"USD"` | Currency for pricing (default: USD). |
| `CustomPricings:<i>:EffectiveDate` | date-time | the moment the value is read | Date when this pricing became effective. |
| `CustomPricings:<i>:EmbeddingPricePerMillion` | number | — | Cost per million embedding tokens (null if not an embedding model). |
| `CustomPricings:<i>:ModelPattern` | string | `""` | Model name or pattern (e.g., "gpt-4o", "claude-3-opus"). |
| `CustomPricings:<i>:PromptPricePerMillion` | number | `0` | Cost per million prompt (input) tokens in the specified currency. |
| `DefaultCrewBudget:AlertThresholdPercent` | number | `80` | Percentage of budget at which a warning alert is fired (default: 80%). |
| `DefaultCrewBudget:MaxCostUsd` | number | — | Maximum total cost in USD (null = unlimited): once the calls recorded reach it, the next LLM call is refused (`BudgetExhaustedException`, dimension `Cost`). |
| `DefaultCrewBudget:MaxTokens` | integer | — | Maximum total tokens (null = unlimited): once the calls recorded reach it, the next LLM call is refused (`BudgetExhaustedException`, dimension `Tokens`). |
| `Enabled` | boolean | `true` | Whether the budgets are enforced (default: true); false records the calls but refuses none. |
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
second call registers no evaluator twice). The runner hosts (`orkeon`, `orkeon-host`) bind it with
the second; `orkeon eval run --llm-judge` sets it for one run, `--offline` forces it off.

<!-- settings:Evaluation -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `EnableLlmJudge` | boolean | `false` | When true, the default evaluation suite also runs the LLM-as-Judge evaluators (coherence, fluency, groundedness). Requires an IChatClient in DI. Default: false. Bound from the `Evaluation` section. |
<!-- /settings -->

### `Orkeon:TypeSafe`

The decision provider TypeSafe (model Jev, key required), bound by `AddOrkeonDecisionProvider(configuration, provider)`, which the three
shipped binaries call when it is the active provider (one provider is active at a time, see [`Orkeon:Decision`](#orkeondecision)): this section is present — even empty, `TypeSafe: {}` counts — or `TYPESAFE_API_KEY` is set, or `Orkeon:Decision:Provider` is `typesafe`: the key, the
endpoint, the default model, the timeout and retries of each call (the LLM judge of the guardian is
[`Orkeon:Guardian:Llm`](#orkeonguardian)). See [Decision models](../architecture/decision-models.md).

<!-- settings:Orkeon:TypeSafe -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ApiKey` | string, secret | — | The API key. |
| `ApiKeyEnvVar` | string | `"TYPESAFE_API_KEY"` | The environment variable read when `ApiKey` is empty. |
| `BaseUrl` | uri | `"https://api.typesafe.ai/v1"` | The API root. |
| `MaxRetries` | integer | `3` | The retries. |
| `Model` | string | `"jev-latest"` | The default model. |
| `TimeoutSeconds` | integer | `30` | The HTTP timeout of one attempt, in seconds. |
<!-- /settings -->

### `Orkeon:Liquid`

The decision model Liquid, bound by `AddOrkeonDecisionProvider(configuration, provider)` when it is the active decision
provider: this section is present (even empty), or `LIQUID_API_KEY` is set, or `Orkeon:Decision:Provider` is `liquid`. One
provider is active at a time (see [`Orkeon:Decision`](#orkeondecision)). See [Decision models](../architecture/decision-models.md).

<!-- settings:Orkeon:Liquid -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ApiKey` | string, secret | — | The API key. |
| `ApiKeyEnvVar` | string | `"LIQUID_API_KEY"` | The environment variable read when `ApiKey` is empty. |
| `BaseUrl` | uri | `"https://api.liquid.ai/decisions/v1"` | The API root. |
| `MaxRetries` | integer | `3` | The retries. |
| `Model` | string | `"d1.1"` | The default model. |
| `TimeoutSeconds` | integer | `30` | The HTTP timeout of one attempt, in seconds. |
<!-- /settings -->

### `Orkeon:Kev`

The decision model Kev, a System One server run locally, bound by `AddOrkeonDecisionProvider(configuration, provider)` only
when `Orkeon:Decision:Provider` is `kev`: a Kev section alone never selects it and is reported as ignored at start. The address needs an
`http` or `https` scheme (refused at start otherwise, naming `Orkeon:Kev:BaseUrl`). The key is optional (`KEV_API_KEY`: sent when
given, otherwise the call is unauthenticated), and a configured key is refused at start towards a non-local address over plain
`http` (local: `localhost`, `127.0.0.0/8`, `::1`); use `https` for a remote Kev. See [Decision models](../architecture/decision-models.md).

<!-- settings:Orkeon:Kev -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ApiKey` | string, secret | — | The API key. |
| `ApiKeyEnvVar` | string | `"KEV_API_KEY"` | The environment variable read when `ApiKey` is empty. |
| `BaseUrl` | uri | `"http://127.0.0.1:8009/v1"` | The API root. |
| `MaxRetries` | integer | `3` | The retries. |
| `Model` | string | `"kev-latest"` | The default model. |
| `TimeoutSeconds` | integer | `30` | The HTTP timeout of one attempt, in seconds. |
<!-- /settings -->


### `Orkeon:Decision`

`Enabled`, the decision circuit of the decision tasks, is bound by `AddOrkeonInfrastructure()`; the
decision profiles (`Profiles`) by `AddOrkeonDecisionProfiles(configuration)`, which the three shipped
binaries call: each profile wraps an LLM profile (`Target`) in stages that block, route or escalate a
call. A crew uses one like any profile, `llm: { profile: <name> }`.

`Provider` names the active decision provider (`typesafe`, `liquid` or `kev`, case-insensitive; blank means not named; an
unknown name is refused at start, the message listing the valid ones). Not named, the host deduces it: the `Orkeon:TypeSafe`
or `Orkeon:Liquid` section (even empty), else the one whose key variable is set; a section outweighs a bare key (one section with both keys set picks the provider holding the section), and both
sections present, or no section and both keys set, is refused, naming both and this setting. Kev is never deduced: a Kev section without `Provider: "kev"` is
warned as ignored in the start log (with a hint to name it when no provider is named). See [Decision models](../architecture/decision-models.md).

<!-- settings:Orkeon:Decision -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | boolean | `true` | Whether decision tasks ask the decision model. |
| `Profiles:<name>:FailClosed` | boolean | `false` | Whether a failing stage blocks the call; false lets it go on with a warning. |
| `Profiles:<name>:Stages:<i>:BlockThreshold` | number | `0.5` | The probability at or over which a `guard` blocks. |
| `Profiles:<name>:Stages:<i>:Criterion` | string | — | What a good answer is, for a `verify`; empty takes the default criterion. |
| `Profiles:<name>:Stages:<i>:EscalateTo` | string | — | The profile a `verify` escalates to. |
| `Profiles:<name>:Stages:<i>:Fallback` | string | — | The profile a `route` takes under `MinConfidence`; empty keeps the target. |
| `Profiles:<name>:Stages:<i>:Kind` | string | `""` | The stage: `route`, `guard` or `verify`. |
| `Profiles:<name>:Stages:<i>:MinConfidence` | number | `0` | The confidence under which a `route` takes `Fallback`. |
| `Profiles:<name>:Stages:<i>:MinProbability` | number | `0.5` | The probability under which a `verify` escalates. |
| `Profiles:<name>:Stages:<i>:Phase` | string | — | When a `guard` judges: `request`, `response` or `both` (the default). |
| `Profiles:<name>:Stages:<i>:Risks:<name>` | string | — | The risks a `guard` detects, each with what it is. |
| `Profiles:<name>:Stages:<i>:Routes:<name>` | string | — | The profiles a `route` may choose, each with what it is for. |
| `Profiles:<name>:Stages:<i>:Steps` | string | — | Which responses a `verify` judges: `final` (the default) lets a response that calls tools pass as a step of an agent loop, `all` judges it too. |
| `Profiles:<name>:Stages:<i>:WarnThreshold` | number | `0.3` | The probability at or over which a `guard` logs a warning. |
| `Profiles:<name>:Target` | string | `""` | The profile a call goes to when no stage routes it; `default` is the `Llm` section. |
| `Provider` | string | — | The decision provider: `typesafe`, `liquid` or `kev`; empty deduces it. |
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
(or `Host`) refuses the start rather than running on the volatile provider
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
by `MemoryCoordinator`; bound by `AddOrkeonInfrastructure()`. With the cognitive memory enabled the
recall is floored by `MinCompositeScore` and `MinSemanticScore` instead of `MinScore`, and `ConsolidateOnRunEnd`
consolidates the memory when a run ends.

<!-- settings:Orkeon:CrewMemory -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ConsolidateOnRunEnd` | boolean | `false` | Whether the crew's memory is consolidated (redundant memories merged, low-value ones pruned) when a run ends. False by default: a consolidation costs LLM calls. Read only when the cognitive memory is wired. |
| `MaxChars` | integer | `4000` | How many characters of memory content a task's prompt receives at most, all recalled memories together. 4,000 by default; the last memory that does not fit is cut. |
| `MinCompositeScore` | number | `0.4` | The least composite score (similarity, recency and importance together) a memory needs to be recalled when the cognitive memory is wired (`Orkeon:CognitiveMemory:Enabled`). 0.4 by default. Distinct from `MinScore`, which bounds the cosine similarity of the vector path: the two scales are not the same, never reuse one for the other. |
| `MinScore` | number | `0.6` | The least cosine similarity between a task and a memory for the memory to be recalled. 0.6 by default, measured on the local embedding model (BGE-micro-v2), on which the same task of an earlier run scores 0.72 and above and an unrelated English task 0.53 and below. Its scale is the embedder's: another model needs its own measure. |
| `MinSemanticScore` | number | `0.5` | The least semantic similarity (cosine) a memory needs to be recalled by the cognitive memory, whatever its recency and importance: a memory stored a moment ago at default importance reaches `MinCompositeScore` on those two alone, so relevance is required apart. 0.5 by default; its scale is the embedder's, like `MinScore`. |
| `RecallLimit` | integer | `5` | How many memories a task recalls at most. 5 by default; 0 recalls nothing (the crew still stores what its tasks produce). |
<!-- /settings -->

### `Orkeon:CognitiveMemory`

Cognitive memory layering. Opt-in: the runners wire it when `Enabled` is `true` (`AddOrkeonCognitiveMemory(configuration)` for a host written in C#). No YAML key: the analysis cost is the operator's.

<!-- settings:Orkeon:CognitiveMemory -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `AnalysisModel` | string | — |  | Optional model override for analysis calls. Null (or blank) runs them on the provider's own model. |
| `AnalysisTemperature` | number | `0.1` |  | Temperature for LLM analysis calls. Default: 0.1. |
| `ContradictionCandidateCount` | integer | `10` |  | Number of existing memories to check for contradictions. Default: 10. |
| `DefaultRecallOptions:ImportanceWeight` | number | `0.2` |  | Weight for importance in composite scoring. Default: 0.2. |
| `DefaultRecallOptions:MinScore` | number | `0.1` |  | Minimum composite score threshold. Default: 0.1. |
| `DefaultRecallOptions:MinSemanticScore` | number | `0` |  | Minimum semantic similarity a memory needs whatever its recency and importance: without it a fresh, default-importance memory unrelated to the query reaches the composite floor on those two alone. Default: 0 (no floor). |
| `DefaultRecallOptions:RecencyWeight` | number | `0.3` |  | Weight for temporal recency in composite scoring. Default: 0.3. |
| `DefaultRecallOptions:SemanticWeight` | number | `0.5` |  | Weight for semantic similarity in composite scoring. Default: 0.5. |
| `DefaultRecallOptions:TagFilter` | list of string | `[]` |  | Optional filter by tags (any match). |
| `DefaultRecallOptions:TopK` | integer | `10` |  | Maximum number of results to return. Default: 10. |
| `DefaultRecallOptions:TypeFilter` | enum | — | `ShortTerm`, `LongTerm`, `Episodic`, `Entity`, `Procedural` | Optional filter by memory type. |
| `EnableContradictionDetection` | boolean | `true` |  | Whether to enable contradiction detection on remember. Default: true. |
| `Enabled` | boolean | `false` |  | Whether the runners wire the cognitive memory into a run: recall by composite score, analysed storage, the `memory_recall` tool. Default: false (the analysis costs LLM calls). |
| `EnableLlmAnalysis` | boolean | `true` |  | Whether to enable LLM-based content analysis on remember. Default: true. |
| `Profile` | string | — |  | The LLM profile (`Llm:Profiles:<name>`) the analysis, contradiction detection and consolidation run on, so they are metered like any call. Null or blank: the default profile. |
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
registers, `ollama`, `mistral`, `azure-openai` or `gemini`; another name refuses the start. The last
three speak the OpenAI embeddings wire format and read `ApiKey` (or `ApiKeyEnvVar`), `Endpoint` and
`Model`; `azure-openai` also needs `Endpoint` (the resource URL) and takes the deployment name as
`Model`. At start the host compares the provider, model and dimensions with the ones recorded at
`FingerprintPath` and refuses to start when they changed. Read by
`DefaultEmbeddingProviderResolver`, when no local or code-analysis provider is registered. Bound
by `AddOrkeonInfrastructure()`, and by `AddOrkeonVectorSearch(configuration)` (called by
`AddOrkeonInfrastructure(configuration)`).

<!-- settings:Orkeon:Embeddings -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ApiKey` | string, secret | — | The API key, in clear text, for `mistral`, `azure-openai` and `gemini`. Prefer `ApiKeyEnvVar`: a key written here is in the settings file. |
| `ApiKeyEnvVar` | string | `""` | Name of the environment variable holding the API key, read when `ApiKey` is empty. |
| `ApiVersion` | string | `""` | The `api-version` query of `azure-openai`. Empty: the provider's default. |
| `BatchSize` | integer | `100` | Maximum batch size for bulk embedding requests. |
| `Dimension` | integer | `1536` | The dimension of the embedding vectors. |
| `EnableCache` | boolean | `true` | Whether to enable embedding caching. |
| `Endpoint` | string | `""` | Base URL of the embeddings endpoint. Empty: the provider's public endpoint. Required by `azure-openai` (the resource URL, `https://<resource>.openai.azure.com`). |
| `FingerprintPath` | string | `"/workspace/.orkeon/embedding-fingerprint.json"` | Virtual path of the file recording the provider, model and dimensions the stored vectors came from; the host refuses to start when they change. Empty: the guard is off. |
| `Model` | string | `"text-embedding-3-small"` | The model name to use for embeddings. |
| `Provider` | string | `"openai"` | The embedding provider to use: "openai", "ollama", "mistral", "azure-openai" or "gemini". |
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
  unknown name refuses the host start, listing the known ones; `orkeon eval rag --offline` ignores
  it.
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
may carry an **id** — `<ulid>|<physical>:<virtual>:<rights>`: what a crew's `mounts:`
block, a team sidecar and `--mount-id` name it by. A CLI `--mount` on the same virtual root
**replaces every entry of that root** for that run; on a new root it is appended (never
merged, never dropped). One root declared twice is legitimate only when every entry of it
carries an id — `--mount-id`, or the crew's `mounts:`, then selects one and the
others are withdrawn for the run; with nothing selecting one the run is refused before any host
builds (`'/output' is declared twice in <settings> (<idA>: <folderA>, <idB>: <folderB>) and nothing selects one. Pass --mount-id <id>, list '<id>|/output' under mounts: in the crew, or pass --mount <folder>:/output:rw to replace them all.`),
except under `orkeon forge` and a command that offers no way to select an entry (`orkeon mcp serve`),
which withdraw every entry of that root with a warning and carry on. A root declared twice with an
entry that has no id (`… and '<entry>' has no id. Give every entry an id …`) or an id carried by
two entries is refused by every command. Every
declared entry's base path is **whitelisted for `PathValidator`** without
`--allow-external-mounts` — a declared folder is the machine owner's intent, so it is reachable
even when it lies outside the process working directory; a withdrawn entry is not.

**`InternalMounts`** — same grammar as `Mounts`, registered `MountVisibility.Internal`: resolvable
by the VFS, **absent from `list_mounts`, from the agent prompt's mount table and from
access-denied messages**. Where a host puts what the VFS must reach and no agent has any business
addressing — the `--llm-log` directory lives here, and so does `/credentials`, the OAuth tokens of
the e-mail accounts, when one is declared
([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)).

**`WorkingDirectory`** — the virtual directory a relative tool path resolves against and a
`shell_command` runs in when it names none: an absolute path without `.` or `..` segments inside an
agent-facing mount, else the host refuses to start, naming the key. Set, it wins over `/workspace`. Unset, the runners take `/workspace` when it is mounted, else
`/output`, else none
([Working directory and relative paths](../architecture/vfs-compliance.md#working-directory-and-relative-paths)).

**`WriteDirectory`** — the writable virtual directory a writing `shell_command` runs in when it
names no directory, runs under no agent root, and the working directory does not grant it every
right (a read-only `/workspace`, as in `orkeon forge`): an absolute path without `.` or `..`
segments in an agent-facing mount whose effective rights on it, subtree overrides included, grant
Write, else the host refuses to start, naming the key. Unset, `/output` when an
agent-facing mount grants Write there, else none, and such a command is refused. It never comes
from the order of the mounts, and the file tools' relative paths still resolve against the working
directory only.

The four are read by `AddOrkeonFileSystem(...)`.

<!-- settings:Orkeon:FileSystem -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `InternalMounts` | list of string | `[]` | Infrastructure mounts, same grammar as `Mounts`: resolvable by the host, absent from `list_mounts`, the agent prompt's mount table and access-denied messages, and refused to every tool. This is where a runner puts what it needs the VFS to reach but no agent has any business addressing — the LLM exchange log directory, for one. |
| `Mounts` | list of string | `[]` | Mount definitions in the format "physical:virtual:rights[;subpath:rights;...]". |
| `WorkingDirectory` | string | — | The virtual directory relative paths resolve against and a shell command runs in when it names none (`Orkeon:FileSystem:WorkingDirectory`). Unset, the runner takes `/workspace` when it is mounted, else `/output`, else none. |
| `WriteDirectory` | string | — | The writable virtual directory a writing shell command runs in when it names none, runs under no agent root, and the working directory does not grant it every right (`Orkeon:FileSystem:WriteDirectory`). Unset, `/output` when an agent-facing mount grants Write there, else none. |
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
| `BlockReadsOfBlockedExtensions` | boolean | `false` | Whether a plain read of a built-in script extension (`.sh`, `.bash`, `.ps1`, `.bat`, `.cmd`, `.vbs`, `.wsf`) is refused too. Default: `false`: reading a script is not executing it, so a read passes while a write, a creation, a deletion or a command target on it is refused. Built-in binary extensions and `AdditionalBlockedExtensions` refuse reads whatever this says. |
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
The `Llm` sub-section adds an LLM judge, off by default: the decision model judges the input and the
output of a task (`Phases`), a risk at or over `BlockThreshold` blocks, and when the model cannot answer
`FailMode` decides: `Closed` (default) refuses, `Open` lets the text in; both audit
`guardian.judge.unavailable`. While the output phase is guarded a task's streaming waits for the judgement. It needs a decision model (`AddOrkeonDecisionProvider`); enabled without
one, the host refuses to start.

<!-- settings:Orkeon:Guardian -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `DefaultPolicy:DelegationGuardEnabled` | boolean | `true` |  | Whether the delegation phase (depth, self-delegation, cycles) runs. |
| `DefaultPolicy:InputGuardEnabled` | boolean | `true` |  | Whether the input phase (prompt-injection screening of the composed user prompt) runs. |
| `DefaultPolicy:MaxDelegationDepth` | integer | `5` |  | How many synchronous delegations may nest before the next one is blocked. |
| `DefaultPolicy:OutputGuardEnabled` | boolean | `true` |  | Whether the output phase runs. The only guard of that phase is the LLM judge, which is itself off unless `Orkeon:Guardian:Llm:Enabled`. |
| `DefaultPolicy:ToolGuardEnabled` | boolean | `true` |  | Whether the tool phase (path traversal, SSRF and SQL injection in tool arguments) runs. |
| `Enabled` | boolean | `true` |  | Whether the guardian runs. When false no guardian is registered in the pipeline: agent turns are neither screened on input nor on tool calls. |
| `Llm:BlockThreshold` | number | `0.8` |  | The probability at or over which a risk blocks the text. |
| `Llm:Enabled` | boolean | `false` |  | Whether the judge runs; off by default (it costs a model call per phase). |
| `Llm:FailMode` | enum | `"Closed"` | `Closed`, `Open` | What happens when the decision model cannot judge: `Closed` (default) refuses, `Open` lets the text in; both audit `guardian.judge.unavailable`. |
| `Llm:MaxContentChars` | integer | `8000` |  | The characters of a text the judge reads in one call. Under `Closed` a longer text is judged whole, chunk by chunk (any chunk that blocks blocks), up to 32 chunks, and a text beyond that is refused; under `Open` it is judged on its first and last half of that budget, what lies between is not judged, and the cut is audited. |
| `Llm:Phases` | list of enum | `[]` | `Input`, `Output` | The phases judged, `Input` and/or `Output`; empty judges both. Any other value (the tool and delegation phases are checked by rules, not by a model) is refused at start. |
| `Llm:Risks:<name>` | string | — |  | The risks detected, each with what it is; empty takes the default risks. |
| `Llm:WarnThreshold` | number | `0.5` |  | The probability at or over which a risk is a warning. |
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
defaults. The length bound is not configured here but under `Orkeon:Tools:ResultLimits`
(below). Bound by `AddOrkeonInfrastructure()` and applied to every tool result by
`IToolInvocationPipeline`.

<!-- settings:Security:ToolResults -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `Policy` | enum | `"Warn"` | `None`, `Warn`, `Block` | The policy applied to a successful tool result. Default `Warn`: the result reaches the model tagged as data and any injection pattern is logged and audited. `Block` withholds a result carrying a High or Critical pattern (the model is told so); `None` passes results untouched and untagged. |
| `TrustedTools` | list of string | `["email_accounts", "email_create_folder", "email_delete", "email_draft", "email_folders", "email_mark", "email_move", "email_parser", "email_read", "email_rename_folder", "email_save_attachment", "email_search", "email_send", "drive_read"]` |  | Tools whose results bypass sanitization. Defaults to the `email_*` tools, which screen what they read with `PromptInjectionDocumentValidator` and mark it untrusted themselves (ADR-012), and to `drive_read`, which does the same with a file's text (ADR-013) — tagging them again would stack two envelopes. The other `drive_*` tools return names and metadata they do not screen, and stay sanitized. Entries from configuration are added to these. |
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
| `DefaultMode` | string | `"default"` | The permission mode of an agent of a crew that declares none (`permissionMode:`): `default`, `acceptEdits`, `bypassPermissions`, `plan` or `dontAsk`. A script poses its own mode on each `act`. |
| `Enabled` | boolean | `false` | Whether the gate is registered. Left `false`, no tool call is gated. |
| `Interactive` | boolean | `false` | Whether the host resolves an approval itself, without an approval channel. A host that registers a channel (orkeon-repl does) asks the operator whatever this says. Left `false` without a channel, a call that needs an approval is refused, and the model reads the reason as the tool's result. |
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

### `Orkeon:Tools:ResultLimits`

The cut of a tool result and the fan-out of a tool-call batch. `Limits` maps a tool name
(case-insensitive) to the most characters of its result; it starts as the `AgentDefaults` map
(`shell_command`: 10_000), each value is at least 1, and a tool absent from the map keeps its
`AgentDefaults` bound. `MaxParallelToolCalls` (default `4`, at least 1) is the most read-class tool
calls of one model turn that run at once ([parallel calls](../architecture/coding-agent-ts.md#an-agent-loop-in-a-crew--ctxllmact)).

A result past its bound is cut and ends with a notice, `N chars omitted. …`, naming how much was
dropped. `shell_command`, its stream cap and `job_tail` read the same bound, and so does the cut
`ctx.llm.act` falls back on when no pipeline applies one. A tool that has already cut its own output
(`shell_command` with its notice) flags the result as already bounded, and the pipeline does not cut
it a second time. The `ResultBudget` of the Drive and
Email tools is a separate bound, out of this setting.

<!-- settings:Orkeon:Tools:ResultLimits -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Limits:<name>` | integer | — | The cut in characters, by tool name (case-insensitive). |
| `MaxParallelToolCalls` | integer | `4` | The most tool calls of one batch that run at once. |
<!-- /settings -->

### `Orkeon:Tools:Shell`

What `ShellCommandTool` may run, configuration only (`AddOrkeonCodeTools()`):
`AllowInterpreters` allows the interpreters and mutating git (**RCE-equivalent**, a warning is
emitted); `ExtraAllowedCommands` adds to the allowlist and `AllowedCommands` replaces it whole —
the replacement cancels `AllowInterpreters`. A command outside the read-only default list (an
interpreter, an added command, git once its restriction is lifted) can write: a virtual path on
its command line and its working directory need every right, so a read-only mount is refused to
it and stays readable through the file tools. `Confinement` says how the operating system
confines the agent's shell session: `Mode = Auto` (the default) picks bubblewrap on Linux,
Seatbelt on macOS, an AppContainer inside a Job Object on Windows, and no confinement when the
host runs in a container; `None` turns it off. A backend that cannot run fails the call, with the
reason, rather than run it unconfined. `AllowNetwork` opens the network (off by default) and
`ReadOnlyPaths` lends a toolchain outside the system directories
([Shell confinement](../architecture/security.md#shell-confinement), [limits](./limitations.md)).
`Environment:Propagate` names extra host variables handed to the shell session and to the code
sandbox on top of the built-in allow-list, such as `HTTPS_PROXY` when the proxy needs no
credential; the code sandbox reads it from configuration only, so a list set in code with
`Configure<ShellToolOptions>` reaches the shell alone. The secret filter wins: a name starting with
`ORKEON_`, containing `KEY`, `TOKEN`, `SECRET`, `PASSWORD`, `PASSWD`, `PASS`, `CREDENTIAL` or
`AUTH`, or the word `PAT` on its own (without case), blank, or holding `=` or a NUL, or whose value
carries URL credentials (`http://user:password@proxy:3128`, `user:password@proxy:3128`), is dropped
with a warning in the log that never quotes the value, and the host still starts; a built-in name
(`SSH_AUTH_SOCK`, `PATH`) is ignored. Names compare with case, except on Windows. Under bubblewrap
an added variable passes whatever `AllowNetwork` says, and no path it names is bound into the view.
`Environment:SessionBus` (off by default) hands a confined session the operator's session bus,
through which a command can leave the sandbox and read the keyring: the host logs a warning when it
is on ([the session bus is opt-in](../architecture/security.md#shell-confinement)).
Paths in the output come back virtual: a path that follows an opening quote runs to its closing
quote, spaces included, and a Windows base (`C:\proj`) matches without case and with `/` for `\`
(`c:\Proj`, git's `C:/proj`). `WindowsPaths` serves a Windows program run under WSL with
`Confinement:Mode = None` (`cmd.exe`, a Windows `dotnet.exe`): keyed by virtual path, it gives the
Windows spelling of a mount's base (`WindowsPaths:/workspace` = `C:\proj`, what `wslpath -w`
prints); a mount it does not name, on a Windows drive (`/mnt/c/proj`), gets that drive's spelling.
Such a program receives its path arguments in that spelling, after the same rights check, and
what it prints in it comes back virtual. Outside WSL the key is not read.

<!-- settings:Orkeon:Tools:Shell -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `AllowedCommands` | list of string | `[]` |  | A full replacement of the default allowlist, verbatim; it cancels `AllowInterpreters` and re-enables the git read-only restriction. Empty keeps the default allowlist. |
| `AllowInterpreters` | boolean | `false` |  | Enables the interpreters (`node`, `dotnet`, `npm`, `find`) and the mutating git subcommands — RCE-equivalent on the host, for trusted coding-agent hosts only. Default false. |
| `Confinement:AllowNetwork` | boolean | `false` |  | Lets the confined process reach the network. Default false: it gets its own network namespace, loopback only (under AppContainer: no network capability, hence no network at all). |
| `Confinement:Mode` | enum | `"Auto"` | `None`, `Bubblewrap`, `Auto`, `Seatbelt`, `AppContainer` | How the process is confined. `Auto` (default) picks the backend of the platform: `Bubblewrap` on Linux, `Seatbelt` on macOS, `AppContainer` on Windows, and no confinement when the host runs in a container (the container is the boundary). `None` runs the process on the host, as the user of the host. `Bubblewrap` runs it under `bwrap` (Linux): each mount bound at its virtual path, read-only when it lacks Write, the system directories read-only, nothing else of the host, the network off. When the backend is unavailable (another OS, no `bwrap`, namespaces refused) the call fails with the reason and the command does not run. |
| `Confinement:ReadOnlyPaths` | list of string | `[]` |  | Absolute host directories bound read-only at their own path, on top of the system directories (`/usr`, `/bin`, `/sbin`, `/lib`, `/lib32`, `/lib64`, `/etc`), for a program installed elsewhere, such as `/opt/node` or a `.dotnet` folder. Under AppContainer (Windows) they are fully qualified host directories the container may read and execute, such as a tool installed under the user profile. |
| `Environment:Propagate` | list of string | `[]` |  | Extra variable names propagated from the runner's environment, such as `HTTPS_PROXY` when the proxy needs no credential. A name that is blank, holds `=` or a NUL, starts with `ORKEON_` or contains `KEY`, `TOKEN`, `SECRET`, `PASSWORD`, `PASSWD`, `PASS`, `CREDENTIAL` or `AUTH` or the word `PAT` on its own (without case), or whose value carries URL credentials (`http://user:password@proxy:3128`, `user:password@proxy:3128`), is dropped with a warning that never quotes the value; a built-in name is ignored. Names compare with case, except on Windows. Under bubblewrap the value passes whatever the network, but no path it names is bound into the view. The code sandbox reads this list from configuration only: one set in code (`Configure<ShellToolOptions>`) reaches the shell alone. |
| `Environment:SessionBus` | boolean | `false` |  | Whether a confined shell session (bubblewrap, Seatbelt, AppContainer) receives the operator's session bus: `DBUS_SESSION_BUS_ADDRESS` and, under bubblewrap with the network, its socket. False by default. The bus lets a command leave the sandbox (`systemd-run --user`) and read every keyring secret (`secret-tool`), so turning it on is logged as a warning. An unconfined session and the code sandbox, which no jail holds, always receive it. |
| `ExtraAllowedCommands` | list of string | `[]` |  | Commands added on top of the default (or replacement) allowlist, such as `make`. |
| `WindowsPaths:<name>` | string | — |  | Under WSL, the Windows spelling of a mount's base by virtual path (`WindowsPaths:/workspace` = `C:\proj`), what a launcher reads from `wslpath -w`. A Windows program run under WSL receives its path arguments in that spelling, and what it prints in it comes back virtual. A mount not named here whose base lies on a Windows drive (`/mnt/c/…`) gets the spelling of that drive. |
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
the user's persistent scope (`HKCU\Environment`), where a client may remember a password; read,
never copied into the process; the process environment alone on Linux and macOS. Provider walkthroughs and the key-by-key table:
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

### `Orkeon:Tools:Drive`

Bound by `AddOrkeonDriveTools(configuration)` — the shared runner host and `orkeon-repl` call it —
and validated lazily, as the e-mail section is: an account is validated the first time a tool or an
`orkeon drive` command uses it, every problem reported at once, and a value the binder cannot read
sets that one account aside instead of failing the host. Every account signs in with OAuth2; the
client secret a Google account needs is never a value here, only the **name** of the variable that
holds it (`Auth:ClientSecretEnvVar`), read like the e-mail ones. Walkthroughs and the tools:
[Drive tools](../guides/drive.md).

- **`DefaultAccount`** — the account a call that names none uses (optional with a single account).
- **`CredentialsDirectory`** — the physical directory whose `drive` subdirectory holds the OAuth
  tokens; default: `credentials` next to the per-user settings file. The runner keeps one
  credentials directory: when `Orkeon:Tools:Email:CredentialsDirectory` names another one, the
  e-mail one wins, and the runner says so at start.
- **`Screening:WithholdRejected`** — withhold the text of a file the prompt-injection screen
  rejects (default `false`: flag only).
- **`Accounts:<name>`** — one account. `Provider` (`GoogleDrive` or `OneDrive`) and `Rights`
  (`Read, Write, Organize, Share, Delete, Purge`) are **mandatory**; `Root` confines the agent to
  a folder of the drive; an empty `Share:AllowedRecipients` allows nobody, and public links stay
  off until `Share:AllowPublicLinks` is `true`.

<!-- settings:Orkeon:Tools:Drive -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `Accounts:<name>:Auth:ClientId` | string | — |  | OAuth client id (Google Cloud or Microsoft Entra application). |
| `Accounts:<name>:Auth:ClientSecretEnvVar` | string | — |  | Name of the environment variable holding the OAuth client secret (Google desktop clients). |
| `Accounts:<name>:Auth:Tenant` | string | — |  | Microsoft tenant: `consumers` (default), `organizations`, `common` or a tenant id. |
| `Accounts:<name>:MaxUploadMegabytes` | integer | `100` |  | The largest file an agent may upload, in megabytes. Unset means 100. |
| `Accounts:<name>:Provider` | enum | — | `GoogleDrive`, `OneDrive` | `GoogleDrive` or `OneDrive`. Mandatory. |
| `Accounts:<name>:Rights` | enum | `"None"` | `None`, `Read`, `Write`, `Organize`, `Share`, `Delete`, `Purge` | What an agent may do, e.g. `"Read, Write, Organize"`. Mandatory. |
| `Accounts:<name>:Root` | string | — |  | The folder the agent is confined to, as a path from the root of the drive (`Orkeon/agents`). Unset opens the whole drive. |
| `Accounts:<name>:Share:AllowedRecipients` | list of string | `[]` |  | Who an agent may share with: an address, `*@domain`, or `*` for anyone with an address. Empty means nobody: sharing is closed until an operator opens it. |
| `Accounts:<name>:Share:AllowPublicLinks` | boolean | — |  | Whether an agent may create a link anyone holding it can open. Off by default. |
| `Accounts:<name>:TimeoutSeconds` | integer | — |  | HTTP timeout in seconds. Unset keeps the client default. |
| `CredentialsDirectory` | string | — |  | Physical directory holding the OAuth tokens, for a host whose per-user settings directory is not the right one (a service account). Read by the host, not by the tools. |
| `DefaultAccount` | string | — |  | The account a call uses when it names none. Optional with a single account. |
| `Screening:WithholdRejected` | boolean | `false` |  | When true, a file the prompt-injection detector rejects has its text withheld. Off by default, as for e-mail: the detector flags; rights and the root folder bound an agent. |
<!-- /settings -->

### `Orkeon:Tools:GitHub`

Read once by `AddOrkeonWebTools` when the `github` tool is registered. The token is the first
non-empty of `Token`, the environment variable `TokenVariable` names (default `GITHUB_TOKEN`), then
`GH_TOKEN`. Without one every call is anonymous: the read actions work within GitHub's anonymous rate
limit, `create_issue` and `create_pr` are refused, and the tool's description says where to set the
token. The token is masked (`***`) in the tool's errors and logs.

<!-- settings:Orkeon:Tools:GitHub -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Token` | string, secret | — | GitHub personal access token the tool sends as a bearer. Keep it out of a committed file (`appsettings.local.json`, or the environment variable instead). Not set: the variable named by `TokenVariable`, then `GH_TOKEN`. |
| `TokenVariable` | string | `"GITHUB_TOKEN"` | Name of the environment variable holding the token when `Token` is empty; `GH_TOKEN` is read after it. |
<!-- /settings -->

### `Orkeon:Tools:Firefox`

Read once by `AddOrkeonWebTools` when the `firefox` tool is registered. The tool drives a Firefox of
its own over WebDriver BiDi: the browser starts at the first action of an agent run, with a profile
folder no other browser uses, keeps its page from one action to the next, and closes with the run —
the profile is removed then. Firefox itself is not shipped: it is the one installed on the machine.
The window is visible unless `Headless` is `true` or the machine has no display. What the tool does
not do is listed in [Known limitations](limitations.md).

<!-- settings:Orkeon:Tools:Firefox -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `AllowedProfiles` | list of string | `[]` |  | The profiles an agent may name with the `profile` argument, beside `disposable` and `Profile`. Empty: an agent names no other profile. A name in `DeniedProfiles` stays refused. |
| `CommandTimeoutSeconds` | integer | `30` |  | Seconds one browser command (a page load, a script, a click) is given to answer. |
| `DeniedProfiles` | list of string | `[]` |  | The profiles no browser ever starts with, whether an agent names one or `Profile` does: the operator's own profiles, for instance. |
| `ExecutablePath` | string | — |  | Path of the Firefox program, or its name on the PATH. Not set: `firefox` on the PATH, then the usual install folder of the platform. |
| `Headless` | boolean | `false` |  | `true` starts Firefox without a window. The default shows the window; under Linux with neither `DISPLAY` nor `WAYLAND_DISPLAY` the browser starts headless anyway, and a warning says so. |
| `Lifetime` | enum | `"Run"` | `Run`, `Host` | How long a browser lives: `Run` (the default) closes it with the agent run that started it; `Host` keeps it open, with its tabs and its profile, from one run to the next, one browser per profile, until `close`, the user closes the window, or the host stops. Case ignored. |
| `MaxNodes` | integer | `40` |  | Most nodes one `snapshot` or `find` answer lists; the rest is read with `offset`. |
| `MaxTextLength` | integer | `3000` |  | Most characters of page text one `read` answer carries. |
| `Profile` | string | — |  | The profile a browser starts with when the agent names none: `disposable` (the default, a fresh folder removed when the browser closes), the name of a profile of the Firefox installed on the host (`profiles.ini`), or the path of a profile folder. A named profile is used in place, with its cookies and sessions, and never removed; a profile another Firefox holds open is refused. |
| `ProfileRoot` | string | — |  | Folder under which each browser gets its own profile folder, removed when the browser closes. Not set: `~/snap/firefox/common/orkeon/profiles` when Firefox is a snap (a snap cannot read the system's temp folder), else `orkeon-firefox` under the system's temp folder. |
| `SharedInstance` | boolean | `false` |  | `true` shares one browser per profile between the agent runs of the host: each run drives tabs of its own in it, and the browser closes when the last run leaves it, unless `Lifetime` is `Host`. The default gives each run a browser of its own. |
| `StartupTimeoutSeconds` | integer | `30` |  | Seconds Firefox is given to start and open its remote protocol. |
<!-- /settings -->

### `MCP`

MCP client connections (`MCP:Servers:<name>`) and the switch `MCP:Enabled`. No setting starts an
MCP server: a key the section does not carry is refused at startup like any unknown key.
Read by `RunnerHost` (`orkeon run`, `orkeon-host`, `orkeon mcp serve`) when `MCP:Servers` declares
at least one server and `MCP:Enabled` is not `false` — the servers are connected before the crew
loads, by `orkeon-host` once at startup, before its first message, and by
`orkeon mcp serve` before it serves; library hosts call `AddOrkeonMcp(configuration)` or the
`AddOrkeonInfrastructure(configuration)` overload — see
[MCP integration](../architecture/mcp.md).

<!-- settings:MCP -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `Enabled` | boolean | `true` |  | Whether MCP is enabled at all. |
| `Servers:<name>:Args` | list of string | `[]` |  | Command-line arguments for the server process. |
| `Servers:<name>:Auth:Audience` | string | — |  | The OAuth audience the token is issued for, when the provider asks for one. |
| `Servers:<name>:Auth:ClientId` | string | — |  | The OAuth client id. |
| `Servers:<name>:Auth:ClientSecretEnv` | string | — |  | The environment variable holding the OAuth client secret (required for client credentials, optional for the device flow). |
| `Servers:<name>:Auth:DeviceAuthorizationUrl` | uri | — |  | The OAuth device authorization endpoint (`oauth2-device`). |
| `Servers:<name>:Auth:PasswordEnv` | string | — |  | The environment variable holding the basic-auth password. |
| `Servers:<name>:Auth:Scopes` | list of string | `[]` |  | The OAuth scopes requested; none by default. |
| `Servers:<name>:Auth:TokenEnv` | string | — |  | The environment variable holding the bearer token. |
| `Servers:<name>:Auth:TokenUrl` | uri | — |  | The OAuth token endpoint (`oauth2-client-credentials`, `oauth2-device`): HTTPS, or HTTP on loopback. |
| `Servers:<name>:Auth:Type` | enum | `"None"` | `None`, `Bearer`, `Basic`, `OAuth2ClientCredentials`, `OAuth2Device` | The authentication scheme, each with the keys it needs: `None`; `Bearer` (`TokenEnv`); `Basic` (`UserEnv`, `PasswordEnv`); `oauth2-client-credentials` (`TokenUrl`, `ClientId`, `ClientSecretEnv`; optional `Scopes`, `Audience`); `oauth2-device` (`TokenUrl`, `DeviceAuthorizationUrl`, `ClientId`; optional `ClientSecretEnv`, `Scopes`, `Audience`; refused by `orkeon-host`, which has no terminal). Accepted spellings, exactly: `None`, `none`, `Bearer`, `bearer`, `Basic`, `basic`, `OAuth2ClientCredentials`, `oauth2-client-credentials`, `OAuth2Device`, `oauth2-device`. |
| `Servers:<name>:Auth:UserEnv` | string | — |  | The environment variable holding the basic-auth user name. |
| `Servers:<name>:CallTimeout` | duration | `"00:01:00"` |  | How long one request to this server may take: a tool call ends as a tool error, and `prompts/list`, `prompts/get`, `resources/list`, `resources/read` and a subscription raise a timeout. 60 seconds by default; a value that is not positive is refused. A device sign-in owed first is outside the bound. |
| `Servers:<name>:Command` | string | `""` |  | Command to launch the MCP server process (for stdio transport). |
| `Servers:<name>:ConnectTimeout` | duration | `"00:00:30"` |  | How long the handshake and the tool listing of this server may take before the connection fails. 30 seconds by default; a value that is not positive is refused. |
| `Servers:<name>:Env:<name>` | string | — |  | Environment variables for the server process. |
| `Servers:<name>:Headers:<name>` | string | — |  | Extra HTTP headers (SSE transport) sent on every request: a literal value, or `env:VAR` to read it from an environment variable when the server is connected. An `Authorization` header is refused here; credentials go through `Auth`. The headers the transport sets itself (`Accept`, `Content-Type`, `Mcp-Session-Id`, `MCP-Protocol-Version`, `Mcp-Method`, `Mcp-Name`, `Last-Event-ID`) are refused too. |
| `Servers:<name>:Tools:Allow` | list of string | `[]` |  | Tool names to register; empty means all. |
| `Servers:<name>:Tools:Deny` | list of string | `[]` |  | Tool names never registered, applied after `Allow`. |
| `Servers:<name>:Transport` | enum | `"Stdio"` | `Stdio`, `Sse` | Transport type to use. |
| `Servers:<name>:TrustAnnotations` | boolean | `false` |  | Whether the permission gate takes this server's tool annotations at their word: a tool marked readOnlyHint is then a read, which plan mode lets run. Off by default: an untrusted server could label a destructive tool read-only, so its tools stay classified fail-closed. |
| `Servers:<name>:Url` | uri | — |  | URL endpoint for SSE transport. With credentials (an `Auth` type, or an `env:` header) it must be an absolute https address, http only on loopback, or the server is refused at configuration. |
<!-- /settings -->

### `MCP:Server`

How the server of `orkeon mcp serve` introduces itself (it exposes tools only). Read by
`AddOrkeonMcpServer`, which `orkeon mcp serve` calls.

<!-- settings:MCP:Server -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `AllowedTools` | list of string | `[]` | The registry tools the server exposes and runs, by name (case-insensitive); empty serves them all. A tool outside the list is neither listed nor callable, whoever registers it and whenever: a tool an MCP server adds later stays out. The tools an extension serves (`--crew`) are not subject to it. `orkeon mcp serve --tools` sets it. |
| `ExposeOutput` | boolean | `false` | Whether the server serves `/output` as read-only MCP resources (`orkeon:///output/<path>`). Off by default: nothing but the tools is exposed. `/workspace` and every other mount are never exposed. `orkeon mcp serve --expose-output` sets it. |
| `MaxConcurrency` | integer | `4` | How many requests the stdio loop runs at once; the others wait their turn. At least 1. `orkeon mcp serve --max-concurrency` overrides it. |
| `MaxResourceBytes` | integer | `1048576` | The largest file `resources/read` returns, in bytes; a bigger one is refused. |
| `Name` | string | `"Orkeon"` | Server name advertised during MCP initialization. |
| `ResourcesPageSize` | integer | `100` | How many resources one `resources/list` page carries. At least 1. |
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

Plugin directory discovery. `orkeon-repl` loads the plugins when the section is present; another
host calls `AddOrkeonPlugins(fileSystem, configuration)` — see [Plugins](../architecture/plugins.md).

<!-- settings:Plugins -->
**Read by**: `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ContinueOnError` | boolean | `false` | When `true`, a plugin that fails to load, is refused by its manifest (absent, malformed, out of the Orkeon range, a tool not declared) or fails to configure its services is recorded in `IPluginRegistry.Failures` and the remaining candidates keep loading. When `false` (default), the first failure throws a `PluginLoadException` (fail fast). |
| `Directory` | string | `"/plugins"` | Virtual path (VFS) of the directory whose sub-folders are plugins: each holds a `plugin.json` manifest and the entry assembly it names; a `.dll` dropped flat is ignored. Must start with `/` and resolve inside a configured mount. Default: `/plugins`. |
| `SearchPattern` | string | `"*.dll"` | Simple glob pattern applied to the file name of each manifest's entry assembly (e.g. `*.dll`, `MyCompany.*.dll`). Entry assemblies must additionally carry the `.dll` extension regardless of the pattern. Default: `*.dll`. |
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
`AddOrkeonInfrastructure(configuration)` when the section exists, and by `orkeon run` and `orkeon-host`;
requires an `IStateStore` (`Orkeon:Checkpointing`).

<!-- settings:Orkeon:ExecutionState:Persistence -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `DeleteFromStoreOnArchive` | boolean | `false` | When `true`, archiving an execution (completion or expiry cleanup) also deletes its persisted entry from the state store. Default: `false` — the final snapshot is kept in the store so that status queries keep working after the in-memory entry is evicted. |
| `Enabled` | boolean | `false` | Enables durable persistence of execution states. Default: `false` (in-memory only, identical to the historical behavior). |
<!-- /settings -->

### `Orkeon:Checkpointing`

Which store holds the checkpoint sessions of the runs: `Enabled` with `Store` memory, json, sqlite or
postgres (`CheckpointingExtensions`). `orkeon run` and `orkeon-host` read it: `json` and `sqlite` write
under `./.orkeon/state`, mounted at `/state` as an internal mount (`Path` is a virtual path under it);
`sqlite` with a `ConnectionString` and `postgres` need no mount. A host written in C# registers it with
`AddOrkeonCheckpointing(configuration)`, `AddOrkeonSqliteCheckpointing(...)` or
`AddOrkeonPostgresCheckpointing(configuration)`.

<!-- settings:Orkeon:Checkpointing -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `AutoMigrate` | boolean | `true` |  | Whether the PostgreSQL schema and tables are created on first use. |
| `ConnectionString` | string, secret | — |  | The connection string of the Sqlite (instead of `Path`) or Postgres store. |
| `Enabled` | boolean | `false` |  | Whether the store named by `Store` is used (default: false, the in-memory store). |
| `MaxHistoryPerSession` | integer | `1000` |  | The maximum number of version entries per session in the PostgreSQL store. |
| `MaxVersions` | integer | `20` |  | The number of versions a session keeps in the Json store (default: 20); each new version deletes the oldest beyond it. Zero or negative keeps them all. |
| `Path` | string | — |  | The virtual path of the folder (Json) or the file (Sqlite) the store writes, under the `/state` mount. Null: `/state/checkpoints` or `/state/checkpoints.db`. |
| `SchemaName` | string | `"orkeon"` |  | The PostgreSQL schema name (default: "orkeon"). |
| `Store` | enum | `"Memory"` | `Memory`, `Json`, `Sqlite`, `Postgres` | The store (default: `Memory`). |
<!-- /settings -->

### `Orkeon:HumanInput`

How a run pauses on a question to a human. Active only with `Orkeon:Checkpointing:Enabled`, which gives the question a session to be persisted with. `Pause` is `Hold` (the run waits for the answer, up to `HoldTimeout`, then suspends: the question stays persisted, nothing is refused) or `Suspend` (the run stops at once: `run.finished` `status: waiting_for_input`, exit 75). Unset, a run with a person to ask holds, a plain `orkeon run` with nobody suspends, and `orkeon-host` and `--events` hold (30 minutes and no limit). A run is resumed with `orkeon run <crew> --resume <id> --input "..."`.

<!-- settings:Orkeon:HumanInput -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `HoldTimeout` | duration | — |  | How long a held run waits before it suspends (the question stays persisted, nothing is refused). Unset, the host decides: 30 minutes in the daemon, no limit under `--events`. |
| `Pause` | enum | — | `Hold`, `Suspend` | The policy. Unset, the host decides: `Hold` in the daemon and under `--events`, `Suspend` in a plain `orkeon run`. |
<!-- /settings -->

### `Orkeon:RunHistory`

Whether the events of a run are kept after it ended: `Enabled` turns the history on, `Store` is `jsonl`
(one `<run id>.jsonl` file per run and an `index.jsonl`) or `sqlite` (`runs.db`, WAL). `orkeon-host` and the
`orkeon init` template turn it on; the library and `orkeon run` leave it off. A run is recorded with or without `--events`. Secrets are masked before they are written, and the folder holds what runs said and did: keep it out of a repository (`orkeon init` adds `.orkeon/` to the `.gitignore`). The folder is `./.orkeon/runs`, mounted
at `/runs` as an internal mount next to the checkpoint state (`Path` is a virtual path under it); `orkeon runs list`,
`show` and `tail` read it back. A purge applies `RetentionDays` and `MaxRuns` when a process first writes and when a run
finishes, never when the history is only read; a run still in flight is never purged, a run whose process vanished (`orphaned`) is. `llm.delta` events stay on the stdout stream and are kept in the
history only with `IncludeDeltas`.

<!-- settings:Orkeon:RunHistory -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `Enabled` | boolean | `false` |  | Whether the history is recorded. Off in the library, on in `orkeon-host` and the `orkeon init` template. |
| `IncludeDeltas` | boolean | `false` |  | Whether `llm.delta` events (token by token, verbose) are kept in the history. They always stay on the stdout stream. |
| `MaxRuns` | integer | `1000` |  | At most this many runs are kept, the oldest purged first; 0 keeps them all. |
| `Path` | string | `"/runs"` |  | A virtual path (it starts with '/'), under the `/runs` mount; the Sqlite file is `runs.db` inside it. |
| `RetentionDays` | integer | `30` |  | Runs older than this many days are purged, at start and when a run finishes; 0 keeps them for ever. |
| `Store` | enum | `"Jsonl"` | `Jsonl`, `Sqlite` | The store: `jsonl` (default) or `sqlite`. |
<!-- /settings -->

## Scripts and console

The sandbox of `.ork.ts` scripts, the esbuild toolchain, and the interactive console.

### `Orkeon:Scripting:AgentInstances`

The transcript caps of the agent instances of a crew.

<!-- settings:Orkeon:Scripting:AgentInstances -->
**Read by**: `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `MaxTranscriptCharacters` | integer | `32000` | The most characters (sum of message contents) one instance keeps in its transcript. |
| `MaxTranscriptMessages` | integer | `40` | The most messages one instance keeps in its transcript. |
<!-- /settings -->

### `Orkeon:Scripting:AgentMemory`

The durable memory behind `ctx.memory.agent`.

<!-- settings:Orkeon:Scripting:AgentMemory -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `RootDirectory` | string | `"/workspace/.orkeon/agent-memory"` | Root directory of the memory files. |
<!-- /settings -->

### `Orkeon:Scripting:Limits`

The Jint sandbox of every script engine (`Orkeon.Scripting`): about 100 MB of memory, a recursion
depth of 64 and 30 s by default.

<!-- settings:Orkeon:Scripting:Limits -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ExecutionTimeout` | duration | `"00:00:30"` | Maximum wall-clock time the engine is allowed to run, in total — the time spent waiting for a tool or a model counts; the time the permission gate takes to decide, an operator answering a tool-call approval, does not. Default: 30 seconds, a ceiling for scripts of unknown provenance; a trusted long run raises it. |
| `MemoryLimitBytes` | integer | `104857600` | Maximum memory the script is allowed to allocate on the engine's thread during one run, in bytes — one window per run (reset when the run starts), not counted since the engine was created; what a host tool call allocates is left out. Default: 100 MB, a ceiling for scripts of unknown provenance; a trusted run that needs more raises it. |
| `PromiseTimeout` | duration | — | How long a host waiting for a script's promise gives it before the wait is rejected, the script never seeing the rejection. Default: none set, the engine's ten seconds; a negative one (-00:00:00.001) leaves cancellation as the only bound, which a scripted command needs since it waits for a model or an operator. |
| `RecursionLimit` | integer | `64` | Maximum recursion depth before the engine throws. Default: 64: it catches runaway recursion well before the .NET stack overflows and leaves room for legitimately nested script logic. A trusted script may raise it. |
<!-- /settings -->

### `Orkeon:Scripting:Toolchain`

esbuild toolchain resolution: `EsbuildPath` comes first in the lookup order. Read by
`EsbuildTranspiler.Create` — `orkeon run`, the shared runner, `orkeon doctor`, `orkeon forge`,
`*.cmd.ts` commands.

<!-- settings:Orkeon:Scripting:Toolchain -->
**Read by**: `orkeon`, `orkeon-host`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `EsbuildPath` | string | — |  | Absolute path to the esbuild binary. When null, the transpiler falls back to the `ORKEON_ESBUILD_PATH` environment variable, then to a binary bundled next to the application (`esbuild-bin/esbuild`), then to the copy `npm install` places in a checkout of the repository (`tools/scripting-esbuild/node_modules/`, searched from the working directory and the application directory upwards), then to a PATH lookup. |
| `EsbuildTimeout` | duration | `"00:00:30"` |  | Maximum time esbuild has to transpile a single source. Default: 30 s. |
| `TscPath` | string | — |  | Absolute path to the `tsc` binary. When set it is the only candidate: a relative path, or one that does not exist, is reported as a missing compiler, never resolved against the working directory nor silently replaced. When null, the type checker looks in `tools/scripting-typecheck/node_modules/.bin/` above the application directory, then in `PATH`; never in the working directory. Like `EsbuildPath`, it may come from the `appsettings.json` next to the crew: that file is trusted as the crew itself is. |
| `TypeCheck` | enum | `"Validate"` | `Off`, `Validate`, `Always` | When the TypeScript compiler (`tsc`) checks the types of a `.ork.ts` script against the typings this build ships: `Off` never, `Validate` (default) in `orkeon run --validate`, `Always` in `--validate` and before every run. Under `Validate` a missing compiler is a warning; under `Always` it is a failure. |
| `TypeCheckTimeout` | duration | `"00:01:00"` |  | Maximum time `tsc` has to check one script. Default: 60 s. |
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
| `MaxChainDepth` | integer | `8` | Maximum depth of a chain of async commands launched from completed handlers. |
| `MaxScripts` | integer | `200` | Hard cap on the number of `*.cmd.ts` files the loader keeps; past it, the remaining files, in path order, are dropped with a warning. Default 200: above a real command set. |
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

Streamed `ctx.llm.act` deltas on the REPL console (`AddLlmConsoleStreaming(configuration, enabledByDefault)`).

<!-- settings:Orkeon:Cli:ConsoleStreaming -->
**Read by**: `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Enabled` | boolean | `false` | Whether each piece of an answer is written to the console as the model sends it, instead of the whole answer at its end. The REPL turns it on when the key is absent; a crew run keeps it off. |
<!-- /settings -->

### `Orkeon:Cli:Memory`

The typed memory behind `memory_store` (`FileCategoryMemoryStore`): one `memory.json` per project, under the same project slug as the session file. The REPL turns `Persist` on when the key is absent.

<!-- settings:Orkeon:Cli:Memory -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Persist` | boolean | `false` | Whether the typed memory (`memory_store`: user, project, feedback and reference entries) is kept per project under `RootDirectory` and read again at the next start; off unless a host turns it on, and the REPL turns it on when the key is absent. Off, the entries live in memory and die with the process. |
| `RootDirectory` | string | `"/workspace/.orkeon/memory"` | Virtual directory holding one `memory.json` per project, in the project's folder of the sessions. |
<!-- /settings -->

### `Orkeon:Cli:Session`

The context window used by `token_budget` (`TokenBudgetTool`) and the TUI.

<!-- settings:Orkeon:Cli:Session -->
**Read by**: `orkeon`, `orkeon-host`, `orkeon-repl`.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `ContextWindowTokens` | integer | — | The model's context window, in tokens: a number above zero. Unset is `200000`. |
| `Persist` | boolean | `false` | Whether the session (messages, state, id and title) is persisted per project under `SessionsDirectory`; off unless a host turns it on, and the REPL turns it on when the key is absent. While it is on, a `*.cmd.ts` saves it now (`ctx.services.get("session").save()`, or `save(path)` to keep a copy elsewhere) or loads another saved session in its place (`load(path)`). |
| `SessionsDirectory` | string | `"/workspace/.orkeon/sessions"` | Virtual directory holding one folder of persisted session per project. |
| `StartScript` | string | — | The script run once at the REPL's start (`Orkeon:Cli:Session:StartScript`); unset runs none. |
<!-- /settings -->

### `Orkeon:Cli:Tui`

The TUI's spinner verbs.

<!-- settings:Orkeon:Cli:Tui -->
**Read by**: `orkeon-repl`.

| Key | Type | Default | Values | Meaning |
|---|---|---|---|---|
| `SpinnerVerbs` | list of string | `[]` |  | The verbs the status line cycles through while the model works ("Thinking", "Reading"…). Left out or empty, the console's own list. |
| `Theme` | enum | — | `Dark`, `Light`, `None` | The palette the console starts in. Left out: none when NO_COLOR is set, else the dark one. |
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
- **`A2A`** — the A2A server, off by default: `Host` is `http://localhost`, and
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
| `Api:Enabled` | boolean | `false` | Serves the API. Off, the host listens on no port for it. |
| `Api:IdempotencyMaxKeys` | integer | `10000` | The most idempotency keys held in memory; the oldest finished ones go first. |
| `Api:IdempotencyTtl` | duration | `"1.00:00:00"` | How long an idempotency key is remembered. After it, the same key starts a new run. |
| `Api:MaxEventStreams` | integer | `64` | The most event streams (`GET /runs/{id}/events`) served at once. Beyond it a stream is refused with a 503 `too_many_streams` and a `Retry-After`: each stream holds a connection and polls the history. |
| `Api:Prefix` | string | `"http://localhost:5190/"` | The `HttpListener` prefix: scheme, host, port and a closing slash. A host the loopback does not contain (`http://+:5190/`) requires an authentication scheme or mutual TLS under `Security`. |
| `Api:ReadinessCacheTtl` | duration | `"00:00:03"` | How long a `/readyz` answer is reused before the checks run again. Readiness reaches the state store and the history: a probe loop or a hostile client must not turn each request into that work. Zero runs the checks every time. |
| `Api:ReadinessTags` | list of string | `[]` | Health-check tags added to readiness besides `ready`. `llm` adds the LLM provider check, which reaches an external service: left out by default, so a provider outage does not take the daemon out of rotation. |
| `Api:Security:AllowedAuthSchemes` | list of string | `[]` | Authentication schemes accepted: `Bearer` and/or `ApiKey`. Empty means no authentication is required. Each declared scheme is validated, not merely matched (see `A2ACredentialValidator`). |
| `Api:Security:ApiKeySecretNames` | list of string | `[]` | Names of the secrets holding the API keys the `ApiKey` scheme accepts, read through the `ISecretProvider` on every request. |
| `Api:Security:RequireMutualTls` | boolean | `false` | Whether a client certificate chaining to a trust anchor is required (fail-closed without one). |
| `Api:Security:TrustedCertificateAuthorities` | list of string | `[]` | Paths of the CA certificates a client certificate must chain to when mutual TLS is required. |
| `Api:Security:TrustedClientCertificateThumbprints` | list of string | `[]` | Thumbprints of client certificates pinned exactly (still refused outside their validity window). |
| `Api:SseKeepAliveInterval` | duration | `"00:00:15"` | How long a quiet event stream waits before it writes a keep-alive frame (an SSE comment, a blank NDJSON line). The write is what detects a client that went away: an abandoned stream of a long run would otherwise live as long as the run. |
| `Crews:<i>:Description` | string | — | What the crew does, in a sentence: the description of its skill on the A2A agent card when `Orkeon:Host:A2A` exposes it. Read from the configuration, never from the crew definition — the card is built without loading a crew. Optional. |
| `Crews:<i>:Mounts` | list of string | `[]` | The folders this host grants THIS crew, as mount strings (`<physical>:<virtual>:<rights>`): a mount namespace of its own for each run, so two hosted crews may both address `/output` over two different folders. Empty (the default) keeps the host's mounts for that crew. A granted folder is an input of the daemon: readable wherever it lies, without `--allow-external-mounts`. |
| `Crews:<i>:Name` | string | `""` | The name the channels and the operator use to refer to it. |
| `Crews:<i>:Path` | string | `""` | Path to the crew definition — a YAML file, a multi-file crew directory, or an `.ork.ts` script. The same targets `orkeon run` accepts. |
| `Crews:<i>:Profile:MaxConcurrentRuns` | integer | `4` | How many runs of this crew may be in flight at once. Bounded on purpose: a daemon that accepts unlimited concurrent runs is a daemon that dies under its first burst. |
| `LlmProfiles` | list of string | `[]` | The LLM profiles (`Llm:Profiles:<name>`) the hosted crews may name. The service runs crews it does not control: unset, every profile the configuration defines is offered; set, only those listed — a crew naming another one fails to load, the run with it. The default profile (the `Llm` section) is always offered, so `["default"]` offers it alone. Every entry must name a defined profile. |
| `RunTimeout` | duration | `"00:30:00"` | How long a run may take before the host cancels it. A daemon has no user watching to press Ctrl-C, so an unbounded run is a stuck daemon. The clock stops while the run holds on a question to a person: the hold timeout, not this one, ends that wait. |
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
otherwise; the standard `OTEL_EXPORTER_OTLP_ENDPOINT` also works in the runners. Read by `AddOrkeonTelemetry(configuration)` — called by `AddOrkeonInfrastructure(configuration)`
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
| `ORKEON_<Section>__<Key>` | Any setting, by its path: `ORKEON_Llm__Model` is `Llm:Model`, `ORKEON_RateLimiting__MaxConcurrentRequests` is `RateLimiting:MaxConcurrentRequests`, `ORKEON_Orkeon__Rag__Profile` is `Orkeon:Rag:Profile`. `ORKEON_Llm__ApiKey` is the variable `orkeon init` proposes for the key of the default model. | Over the settings file: the variable wins. |
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
| `ORKEON_ALLOW_EXTERNAL_MOUNTS` | `orkeon`, `orkeon-repl` | `1`, `true` or `yes` | Stands for `--allow-external-mounts` on every `orkeon run`, `orkeon rag` and `orkeon-repl`: a `--mount` may point outside the working directory. A sandboxed deployment sets it once, its own boundary being the isolation. |
| `ORKEON_DEBUG` | `orkeon` | `1`, `true` or `yes` | An unexpected error prints its exception — the chain of types and the stack — instead of one line. |
| `ORKEON_MCP_SERVE` | `orkeon` | set by `orkeon mcp serve`, never by hand | Marks every process `orkeon mcp serve` starts. Under it `orkeon mcp serve` refuses to start, so settings that list it among their own MCP servers cannot start it in a loop. |
| `ORKEON_ESBUILD_PATH` | `orkeon`, `orkeon-host`, `orkeon-repl` | the path of an esbuild executable | Where a `.ork.ts` script finds esbuild, after `Orkeon:Scripting:Toolchain:EsbuildPath` and before the copy shipped beside the binary. The launchers of an installation set it to the esbuild they ship when it is unset. |
| `ORKEON_LLM_API_KEY` | `orkeon` | an API key | The variable `orkeon llm probe` and `orkeon llm models` read the key from when `--api-key-env` names no other. |
| `BRAVE_API_KEY` | `orkeon`, `orkeon-host` | a Brave Search API key | Registers the `brave_search` tool. It is read as a configuration key first, so the settings file and `ORKEON_BRAVE_API_KEY` give it too. |
| `ORKEON_DISCORD_TOKEN` | `orkeon-host` | a Discord bot token | The token of the Discord channel: the default of `Orkeon:Host:Discord:TokenEnvironmentVariable`, which may name another variable. |
| `OLLAMA_BASE_URL` | `orkeon`, `orkeon-host`, `orkeon-repl` | the address of an Ollama server | The address the Ollama provider uses when it is given none: `Llm:BaseUrl` wins over it, and `http://localhost:11434` is what remains without either. |
| `TUI_DRIVER` | `orkeon-repl` | `windows`, `dotnet` or `ansi` | The Terminal.Gui driver of the text interfaces, for diagnosis. Unset: `windows` under Windows, `dotnet` elsewhere — `ansi` draws nothing under WSL and in many container terminals. |
| `TUI_DIAG` | `orkeon-repl` | `1` | The split-pane console writes `[tui-diag]` lines on stderr while it starts: the terminal, the driver chosen. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `orkeon`, `orkeon-host` | the address of an OTLP collector | Turns the OpenTelemetry export on when `Telemetry:OtlpEndpoint` is empty; the exporter reads the address, and the other `OTEL_EXPORTER_OTLP_*` variables, by itself. |
| `XDG_CONFIG_HOME` | `orkeon`, `orkeon-host`, `orkeon-repl` | an absolute directory | Under Linux and macOS, the root of the per-user settings — `$XDG_CONFIG_HOME/Orkeon/appsettings.json`, `~/.config` when it is unset — and of the systemd user units `orkeon forge schedule` installs. |
| `CI` | `orkeon-repl` | `true` | No text interface opens: `orkeon-repl` falls back to its plain console. |
| `TERM` | `orkeon-repl` | a terminal type | Under Linux, unset or empty has the effect of `CI=true`: there is no terminal to draw in. |

**Passed on, not read.** The `shell_command` tool hands the command it starts a fixed list of the
host's variables — `PATH`, `HOME`, the locale, the temporary folders, `DOTNET_ROOT`,
`NUGET_PACKAGES` and what Windows needs to start a program —, plus the three that name a socket
of the operator's session: `DBUS_SESSION_BUS_ADDRESS`, `XDG_RUNTIME_DIR` (the keyring a credential
helper such as `gh auth git-credential` reads) and `SSH_AUTH_SOCK` (the ssh agent). They hold an
address, not a secret, but the bus lets a command leave a sandbox and read the keyring: a confined
session gets `DBUS_SESSION_BUS_ADDRESS` only when `Orkeon:Tools:Shell:Environment:SessionBus` is
true, and under bubblewrap the sockets follow only when `Confinement:AllowNetwork` is true
([security](../architecture/security.md)). The code sandbox builds a smaller environment still,
the three sockets included. Both add the names of `Orkeon:Tools:Shell:Environment:Propagate` that
pass its secret filter. Neither reads them as settings,
and no key travels that way — `OPENAI_API_KEY`, `ORKEON_*` and every other variable stay behind. `PATH`
is also where a `.ork.ts` script looks for esbuild last; `HOME` is
read when the system names no profile folder, to place the per-user settings. The variables
written under `MCP:Servers:<name>:Env` are the operator's: Orkeon hands them to the server it
starts and reads none. The container image has variables of its own, read by its entry script
and not by the binaries — `ORKEON_ENTRYPOINT` and its neighbours
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
| `Orkeon:Tools:Drive:Accounts:<name>:Auth:ClientSecretEnvVar` | The OAuth client secret of the Google Drive account. | The process environment, then the user's own variables under Windows. |
| `Orkeon:TypeSafe:ApiKeyEnvVar` | The key of the TypeSafe decision model; the key names `TYPESAFE_API_KEY` when nothing sets it. | The process environment, then the user's own variables under Windows. |
| `Orkeon:Liquid:ApiKeyEnvVar` | The key of the Liquid decision model; the key names `LIQUID_API_KEY` when nothing sets it. | The process environment, then the user's own variables under Windows. |
| `Orkeon:Kev:ApiKeyEnvVar` | The optional key of the Kev decision model; the key names `KEV_API_KEY` when nothing sets it. | The process environment, then the user's own variables under Windows. |
| `Orkeon:Embeddings:ApiKeyEnvVar` | The API key of the `mistral`, `azure-openai` or `gemini` embedding provider, read when no `Orkeon:Embeddings:ApiKey` is set. | The process environment. |
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
