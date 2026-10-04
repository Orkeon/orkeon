# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed — the Studio TUI presents the key under every spelling a run reads, the open model-setting editor follows the language, and a refused write of the model settings is said

Three defects of Studio's model settings (STUDIO-56):

- **The `orkeon-studio-config` « Test connection » now finds the key under every spelling a run
  reads.** The .NET configuration compares the `ORKEON_` prefix and the keys without case and reads
  `:` like `__`, so a run reads `ORKEON_LLM__APIKEY`, `orkeon_llm__apikey` or `ORKEON_Llm:ApiKey`
  as `Llm:ApiKey`. Under Linux and macOS the TUI looked for the exact name: it said « API key
  missing », or tested another key than the one the run sends. It now reads the process block as the
  configuration does (`IEnvironmentVariables.ReadAll`, `LlmApiKeyResolver`). When two spellings in
  the layer that decides hold different values, a run reads either one: the TUI sends no request and
  names the variables, never their values (« API key ambiguous — … Keep one. »). The variable
  `Llm:ApiKeyEnvVar` names is still read by its exact name, as the run reads it.
  `LlmApiKeyResolver.Resolve` returns an `LlmApiKeyResolution` (the key, none, or the conflict); its
  overloads over a function and over the machine without a seam, and `LlmForm.ToProbeRequest(Func)`,
  are removed — pass an `IEnvironmentVariables` (`SystemEnvironmentVariables.Instance`).
- **The open model-setting editor follows a language switch.** Its cards, its three thinking
  choices and every line it computes — the key block, the hints, the host id, the test's verdict, the
  balance — stayed in the previous language until it was reopened. The tab relays the switch; the
  editor rebuilds its catalogue and its choices, finds its card again by name, keeps what was typed,
  and keeps each verdict as a state, formed as it is read. The key rows of the Models and Tools tabs
  and the tab's error lines follow it too.
- **A refused write of `studio-model-profiles.json` is said, and no longer kills the writes that
  follow.** Every change wrote the file after the previous write; a write that threw (an infinite
  temperature) made every later one fail with it until the end of the session, silently, and the
  store swallowed a read-only or locked file. Each write now answers for itself, the Models tab says
  the failure on a line of its own (« The model profiles file could not be written — … »), the next
  change writes the whole set again and clears it, and the file is written beside then moved over the
  original: replaced whole or not at all.

### Fixed — Studio refuses before saving what the run will refuse, says each refusal in the language of the screen, and neither keeps nor erases in silence a value it cannot read **[breaking]**

The model-setting editor took « NaN », « Infinity » or `1e400` as a temperature: « Save » threw, the
settings document kept the value, and every later save of the session failed with it. The check before
saving took `600.0` or `0.5` where the run reads an integer, looked at no entry of `Llm:Profiles`, and
let through an `ApiKey` written `${NAME}` and an entry named `default` — all of which the run refuses
at start. `orkeon-studio-config` loaded an unreadable value empty and erased it at the next save, a
tri-state switch included; a `studio-model-profiles.json` holding `"Temperature": 1e400` loaded as an
infinity, after which no change of the model settings reached that file and every launch passed
`Infinity` to the run; and eleven refusals stayed in English in every language (STUDIO-55).

- **A temperature is a finite number.** The editor (`ParsedTemperature`) and the TUI
  (`FieldText.TryReadDouble`) refuse NaN, ±∞ and `1e400` like any unreadable text: « Save » stays
  inactive, the TUI says the field. The store refuses a file whose setting holds a non-finite
  temperature like one it cannot convert — the empty set and a reason naming the setting and the field,
  shown on the settings screen. The editor also refuses an address that is not an absolute http(s) URL.
- **The check judges as the run reads.** `AppSettingsValidator` judges `Llm` and each
  `Llm:Profiles:<id>` entry by the run's rules: strings, a finite temperature, `MaxTokens`,
  `TimeoutSeconds` and `MaxRetries` whole numbers that fit an `int` — like every other integer field
  (`RateLimiting`, `LlmLogging:MaxBodyLengthChars`, `Orkeon:Rag:Corrective:MaxIterations`), « must be
  a whole number » —, `Thinking:Enabled` and `Grammar` booleans, `BaseUrl` an absolute http(s) URL,
  `ApiKeyEnvVar` a variable's name; `Llm:Profiles` and each entry objects. An `ApiKey` written `${NAME}`
  is the error `STUDIO-LLM-APIKEY-PLACEHOLDER`, giving the run's remedy (`"ApiKeyEnvVar": "NAME"`), and
  an entry named `default` the error `STUDIO-LLM-PROFILE-NAME`. The rules are Studio.Core functions
  (`LlmSection.IsKeyPlaceholder`, `IsVariableName`, `IsAbsoluteHttpUrl`), held to the real runner host
  by a test.
- **The TUI shows what it cannot read.** Its fields load as the file writes them
  (`AppSettingsDocument.GetWritten`): an unreadable number or switch is shown and refused when applied,
  never erased. A tri-state switch whose key is no boolean (`"yes"`, `1`) keeps that text, says it in
  its caption, and is refused until it changes state.
- **Every refusal in the language of the screen.** The thirteen codes without a plain-language
  explanation — the two above, `STUDIO-LAUNCH-MOUNT-DASH`, `STUDIO-LAUNCH-MOUNT-ID`,
  `STUDIO-LAUNCH-DIRECTORY`, the six `STUDIO-TARGET-*`, `STUDIO-MOUNT-ID`, `STUDIO-MOUNT-SHARED` — have
  theirs, in the five languages, and a test requires one of every `STUDIO-*` or `WIN-*` code of
  Studio.Core. The target's status line says the explanation of its code; the line under the shape
  chooser shows only when it names the scripts to move.

**Migration**: a settings file Studio saved before may now be refused at its next save, at the path the
line names — the run refuses it at start already (GAP-40). Write a whole number where an integer is
read, a finite temperature, an absolute http(s) address; move an `ApiKey` written `${NAME}` to
`"ApiKeyEnvVar": "NAME"`; rename an entry `Llm:Profiles:default`. A `studio-model-profiles.json` holding
a non-finite temperature is reported unreadable: correct the temperature it names.

### Fixed — a team keeps its model setting and its folders: a renamed setting carries its teams, Studio says when a scheduled run cannot follow, and a team that changes machine carries nothing of the one it leaves **[breaking]**

Since STUDIO-50 a team's `run.cmd`/`run.sh` carry its model setting (`--llm-profile`) and its folders, so
the run the operating system schedules is the one Studio launches. Five situations undid that without a
word (STUDIO-52): a setting renamed in Studio detached its teams — launched on the default, their
launchers without `--llm-profile`, their card still naming the former name; a team on « no model » or on
a setting named without an ASCII letter ran on its setting from Studio and on the default once
scheduled, and a setting absent from the machine was shown as in force; an imported team kept the other
machine's settings file, setting and folders in its launchers, an exported one carried this machine's —
user name included — and `FORGE.md` and `forge.json` the absolute path of the team; a setting created
without saving the settings was named by the launchers before the file had it, and the next scheduled
run was refused; a declaration the settings editor gave an id when it read the file was linked to a team
under an id the file never held, refused at launch and lost at the next start.

- **A renamed setting carries its teams.** The editor rewrites the `profile` of every companion file
  naming the former name — archived teams included — and the wizard's choice, before the change is
  mirrored and the launchers written (`TeamCatalog.RenameSetting`, `ModelProfilesViewModel`'s
  `followRename`, `CreateTeamViewModel.FollowRenamedSetting`).
- **What a run outside Studio cannot take is said, never refused.** `TeamSettingStanding` (`None`,
  `Offered`, `NotOffered`, `Missing`) serves the card, the schedule offer, the Run and Test screens and
  `TeamLaunchers.Describe`: a scheduled team on a setting offered to no crew reads « Scheduled, this team
  runs on the default setting, not on “X” » and why; a setting absent from the machine reads « X —
  absent from this machine: the default setting runs in its place » with the same words on the card and
  the Run screens.
- **The settings file as saved is checked, and saved when a gesture depends on it.** `ScheduledRunCheck`
  compares what a scheduled team's launchers name with the file as saved — a profile it does not define
  or a folder id it does not declare (« will be refused »), the default, an entry older than the setting
  (its key judged by `KeyAgrees`) — and the card shows the first. « Install the schedule » (card and
  offer), saving « Change the folders », an adoption and a launch from the Run or Test screen save the
  settings when the file lacks what they name and the screen's document has it; a launch whose save is
  refused does not start and says why (`Studio.Run.SettingsNotSaved`). A launch pinning another settings
  file saves nothing. `ConfigTabViewModel.Saved` is raised after each save and load.
- **A team that changes machine.** An import writes the team's launchers for this machine at once; «
  Install the schedule » writes them just before `forge schedule`; nothing is written at startup. «
  Export » writes the copy's launchers without the settings file, the model setting or a folder of the
  disk (`TeamLaunchers.WritePortable`), and an export or an import leaves `schedule/` behind.
- **The engine writes nothing of the machine into `FORGE.md` or `forge.json`.** The card's schedule
  section keeps `orkeon forge schedule .` and loses « By hand, on this machine » and its command, which
  rides the `promoted` event and a refusal's `error` only; `forge rename` retitles the card's title line
  alone. What `forge schedule` installed is recorded in `schedule/installed.json`
  (`ConventionalNames.ScheduleDirectory`, `ConventionalNames.ScheduleInstallationFile`), beside the
  artifacts, no longer in `forge.json`'s `schedule.installed`; same fields, same rule for a copy made by
  hand; a re-adoption and a regeneration of the artifacts keep it.
- Seven strings in the five languages. The screenshot world declares its folders under stable ids, and
  the in-flight run shot waits for the scripted run to be parked.

Migration: a team detached by a rename made before this version stays detached — its card says «
absent », and « Modify » chooses it a setting. A team linked to an id the file never held (« declaration
… is missing ») is repaired by « Change the folders ». A schedule installed before this version is
recorded in `forge.json`, which nothing reads any more: the card's « Install the schedule » installs it
again under the same names and records it in `schedule/installed.json`. A `FORGE.md` written before keeps
its command.

### Fixed — every setting a host cannot honour is refused at its start, naming its key, and `orkeon doctor` judges the file as a run does **[breaking]**

GAP-35 gave a refused setting one line and one exit code, but only some settings were judged at start
(GAP-40). About thirty sections were read by the first service that needed them: with
`"Orkeon": { "Guardian": { "Enabled": "oui" } }`, `--validate` answered `VALIDATION OK`, `orkeon run` loaded
the crew then exited `2` on the binder's sentence, and `orkeon-host` declared itself ready and failed every
run; a mistake in `Orkeon:Rag` or `Orkeon:CrewMemory` waited for the first run that used it, sometimes after
model calls. Other values were swallowed, keys and section names written wrong were read as absent, names
were checked at first use or never, and `orkeon doctor` judged the `Llm` section alone.

- **Every setting a shipped host reads is judged at its start**, whether the run uses it or not. Each
  section is registered once — bound, validated with `ValidateOnStart`, its shape declared: the new
  `AddOrkeonSettings<T>(path)` (`Orkeon.Infrastructure.DependencyInjection`), or `DeclareSettings(path)` on an
  `OptionsBuilder<T>`, `ValidateSettings(...)` for a rule worded by its key, and `DeclareSettingsShape(path,
  type)` for a section read raw (`SettingsDeclaration`, `Orkeon.Application.Configuration`). `RunnerHost.Build`
  runs one start validation before anything else — no warning printed, no telemetry started —, which the REPL
  and `orkeon doctor` share: every declared section's options are created (the binder, the rules, the names
  they hold), then the section names, then the keys, then `Orkeon:Rag:LlmProfile`; the first refusal is the
  `RunnerSettingsException`. Nothing is built but options and the named factories: no store, provider, model
  or connection. `PathSecurity`, `Orkeon:Sandbox`, `RateLimiting`, `Orkeon:Guardian`, `Security:*`,
  `Orkeon:Tools:Shell` (`ShellToolOptions` — `--list-tools` crashed on its `AllowInterpreters`), `Orkeon:Rag`
  and its `Ingestion`, `QueryRouting`, `Retrieval:Hybrid` and `WebFallback`, `Orkeon:CrewMemory`, `Memory`,
  the five memory providers' sections, `Orkeon:Consensus`, `Orkeon:CodeSandbox`, `Orkeon:CostTracking`,
  `Orkeon:TokenCounter`, `Orkeon:Encryption`, `Orkeon:Embeddings`, `Orkeon:EmbeddingCache`,
  `Orkeon:Scripting:Limits` and `:Toolchain`, `Orkeon:Cli:Session`: all refuse the start now. The effective
  `RagOptions` are validated options too (the `Orkeon:Rag:Profile` preset with the section bound over it).
- **No value is swallowed.** The default `Llm` section is read as strictly as its profiles — a
  `TimeoutSeconds` of `"600s"` ran on 30 s —, `Thinking:Enabled` and `Grammar` are `true` or `false`
  everywhere, and a `Temperature` is a finite number, in the default section and every profile: `"NaN"`,
  `"Infinity"` or `1e400` (read as infinity) passed the strict read, then every request failed to
  serialise. So is every number a section reads as a float or a double — the binder took `NaN` and the
  infinities without a word: an `Orkeon:Rag:Generation:Temperature` failed every RAG answer, an
  `Orkeon:CrewMemory:MinScore` recalled nothing. `RaggableTree:Embedding:Provider` is one of its five
  names (a number is refused), `Dimensions` and `MaxTextChars` are whole numbers; `LlmLogging` is read
  strictly under `--llm-log`; a flat `Orkeon:Rag:Retrieval:Hybrid` is a boolean;
  `Orkeon:Cli:Session:ContextWindowTokens` is a number above zero, which `token_budget` reads from the session
  options (`CliSessionOptions`); `Orkeon:Checkpointing` is bound by the binder.
- **No key is ignored.** A key a section the host reads does not carry is refused against the shape its
  readers declare — the readers of one section together, a sub-section another reader declares included:
  `Orkeon:Guardian:Enabeld`, `Llm:Provider` (the provider follows from `BaseUrl` and the model). The keys
  GAP-08 removed — `Memory:ConnectionString`, `Orkeon:Rag:ConnectionString`, `Orkeon:Rag:ProviderOptions`,
  `Orkeon:Pinecone:Environment` — are refused with their migration. A section name under `Orkeon:` and
  under `Orkeon:Cli`, `Orkeon:Tools`, `Orkeon:Scripting`, `Orkeon:Security` and `Security` is one of
  `SettingsSections.Known` (`Orkeon.Constants.Configuration`), the closest proposed
  (`Orkeon:Guardain` → `Orkeon:Guardian`). The root, `Logging` beyond its levels, `Secrets`, the keys of a
  dictionary and the keys of a section another host reads stay open. An e-mail account carrying a key no
  account carries is set aside and reported when named, like an account whose value cannot be read: the
  e-mail section stays the one exception.
- **Names are checked against the host's lists.** `Memory:Provider` and `Orkeon:Rag:Provider`, with what the
  named provider needs (`Orkeon:LanceDb:Endpoint`; `Orkeon:Pinecone:ApiKey` and `IndexName`, or `Host`);
  the `Rerank:Kind`, `QueryTransform:Mode`, `Context:Ordering` and `Ingestion:DefaultChunkingStrategy` of the
  effective RAG options, against the factories this host builds — a C# host without the ONNX reranker
  refuses `balanced` and `quality` at its start, naming `onnx`; `Orkeon:Rag:QueryRouting:Classifier`;
  `Orkeon:Embeddings:Provider`, `openai` or `ollama` — another name took the OpenAI branch; the rule of
  `Orkeon:Sqlite:TableName`. `MemoryProviderFactory` no longer falls back on the volatile provider: an
  unknown type, or `lancedb` without its endpoint, is refused where it is reached, and a YAML crew's
  `memoryProvider:` that names no provider fails its load, like an unknown tool (`MemoryProviderTypes`,
  `Orkeon.Application.Memory`, is the one list).
- **`Logging` is judged before the logger is built.** A level is one of the seven, any case; the console
  options are bound under the barrier. A level written wrong made the build throw a sentence without the
  key, past every barrier — `orkeon-host`, restarted by systemd every ten seconds.
- **`orkeon-repl` starts behind the same barrier** (`ReplStartup`): its registrations, `Logging`, the start
  validation — one line, `orkeon-repl: <reason>`, exit `1`, no console; a `--settings` file it cannot read is
  named with the line and the position. It opened on a file `orkeon run` refused.
- **`orkeon doctor` judges the file as `orkeon run` does.** A `runner-settings` check, after `llm-profiles`,
  applies the run's guards to the mounts the file declares, then builds the host `orkeon run` builds — the
  ONNX reranker the CLI adds included, no crew, no MCP connection — and runs its start validation: one `fail`
  row per refusal, each naming its key, or one `ok` row; skipped when the `Llm` section is already refused.
  The `--json` schema is unchanged; one check name is added.
- **The settings files the repository hands out pass that validation**, and a test holds every one of them
  (`ExampleSettingsTests`: the examples' `appsettings*.json`, the `*.json.example` templates, the smoke
  fixtures). The `orkeon-host` example named its DeepSeek key under `Llm:ApiKeyEnvironmentVariable`, which
  nothing reads — the daemon started without its key and every run failed on "API key is required" —: it
  writes `ApiKeyEnvVar`, and it and the streaming demo lose `Llm:Provider`. The release smokes' RAG fixture
  wrote its database under `Orkeon:Rag:ConnectionString`, so it stayed in memory and the search process found
  nothing the ingestion process wrote: it writes `Orkeon:Sqlite:ConnectionString`. The porting guide's
  `Memory:ConnectionString` is `Orkeon:Redis:ConnectionString`.

Documented in [Configuration](docs/reference/configuration.md#when-a-setting-is-refused), [CLI](docs/reference/cli.md),
[Service host](docs/architecture/service-host.md), [E-mail](docs/guides/email.md) and the
[porting example](docs/guides/porting-example.md).

Breaking: a value that cannot be read, a key no section carries or a name the host does not know, in a
setting a host reads, refuses its start — exit `1` for `orkeon` and `orkeon-repl`, `78` for `orkeon-host`, an
exception out of `StartAsync` for a C# host (values and names) — whether the run uses the setting or not; the
default `Llm` section is read as strictly as the profiles; an unknown `Memory:Provider`, or `lancedb` without
its endpoint, no longer runs on the volatile in-memory provider, and `IMemoryProviderFactory.GetProvider`
throws on such a type; `Orkeon:Embeddings:Provider` accepts `openai` and `ollama` only; `orkeon doctor` judges
the file as `orkeon run` does, and may exit `1` on a file it called green; `TokenBudgetTool` takes the session
options in place of the configuration key it read.

Migration: fix the value, the key or the name the line names. Move a key GAP-08 removed to the section its
refusal names. A C# host whose profile reranks with `onnx` registers `AddOrkeonOnnxReranker()`, or names
another profile.

### Fixed — a crew exported to YAML reads back the same: everything the loader reads, under its author's keys **[breaking]**

`YamlCrewExporter` — the export `AddOrkeonYaml()` registers for a C# host — wrote another crew than the
one it was given, without a word (GAP-39). Reloaded, the crew answered without its knowledge and its
guardrails, the framework no longer wrote its deliverables, its EventHub door followed the host's policy
instead of what it declared, and every agent and task had been renamed.

- **Everything the loader reads is written.** The crew's `memoryProvider`, `graphConfig`, `rag`
  (collections, their sources as written, their chunking, `defaults.profile`) and `links` — an empty
  `links: []` stays a closed door, an absent block stays absent —, an agent's `guardrails` and
  `knowledge` (the short form for an attachment that only names its collection), a task's `deliverable`
  and `guardrails`: each was lost. What the loader transformed is written as the configuration carries
  it: a guardrails `preset:` as the header, rules and tool rules it brought, the crew's `llm:` under each
  agent, `anchors:` as their text. `ExportToDirectoryAsync` writes the crew keys from the same mapping as
  `ExportToString`, where it kept a copy of its own.
- **Under the author's keys.** `AgentConfiguration.Key` and `TaskConfiguration.Key` keep the key an agent
  or a task has in its crew file — the mapping key, or the file name in the per-entity layout; the YAML
  loader sets them, a configuration built in code or by a `.ork.ts` script has none. The export writes
  each entry, and each `agent:`, `dependencies:` and `managerAgent:`, under that key, where it wrote a
  fresh identifier (`01J9…`) nobody could read; a configuration without keys keeps its identifiers. Two
  entries under one name, or a reference that names no entry, fail the export with an
  `InvalidOperationException` naming them: the loader would refuse the file.
- **No key nobody set.** `YamlDotNetSerializer.Serialize` no longer writes a null property as an empty
  `key:` line — an exported crew of three agents carried dozens —, the form the forge already used through
  `SerializeWithoutNulls`, which is gone. A value set to its default, `false` or `0`, is still written.
- **The validator names an agent or a task by its key.** `Invalid crew configuration: Agent 'researcher'
  must have a goal.`, where `orkeon run` named `Agent '01J9…'`, an identifier the author never wrote; a
  configuration built in code is named by its identifier, as before.

The round trip is proven on a canonical projection of the configuration, by two witness crews that set
every key the YAML models read — a key added to a model fails the test until a witness sets it, and the
round trip then makes the export write it —, and by every crew of `examples/`. The second export of a
reloaded crew is the first, byte for byte.

Documented in [YAML schema](docs/architecture/yaml-schema.md#export).

Breaking: `YamlDotNetSerializer.SerializeWithoutNulls` is removed and `Serialize` omits null properties;
the export writes agents and tasks under their keys; the validator's messages name a loaded crew's
entries by their keys.

Migration: call `Serialize` where you called `SerializeWithoutNulls`.

### Removed — public surfaces nothing called, continued: a task state machine and queue, aggregate restores and mutators, an agent stop switch, two ports nothing resolved; `AddOrkeonApplication(o => …)` no longer resets a setting **[breaking]**

Public types and members that no run, host or REPL called are deleted rather than kept (GAP-37, after
GAP-26). Only a C# host could see them: the CLI, `orkeon-host`, the REPL and Studio behave as before.

- **Application options.** `AddOrkeonApplication(o => …)` registers the lambda as is
  (`services.Configure(configure)`). It applied it to a fresh instance and copied ten fields back:
  `EnableDebugLogging` and `DefaultTimeout` were lost, and a strategy a
  `services.Configure<OrkeonApplicationOptions>(…)` had set before it was reset to `FirstFit`. The two
  routes the documentation gives now compose, in either order. `OrkeonApplicationOptions` keeps
  `AgentSelectionStrategy`, the only setting anything read; `CrewRepositoryType` (with the
  `RepositoryType` enum), `CrewsPath`, `DefaultMaxIterations`, `EmbeddingDimension`,
  `EmbeddingProvider`, `OpenAIApiKey`, `OpenAIEmbeddingModel`, `AzureOpenAIEndpoint`,
  `AzureOpenAIDeploymentName`, `EnableDebugLogging` and `DefaultTimeout` are removed — no section bound
  them, nothing read them —, with the constants only they used (`PathDefaults`,
  `EmbeddingDefaults.FallbackProvider` and `DefaultOpenAIModel`).
- **A task state machine and a task queue no mode runs.** `TaskStateManager`
  (`Orkeon.Application.Services.StateManagement`, with `StateTransitionResult<TState>`,
  `TaskStateEvent`, `TaskReadinessFlags`, `TaskStatusFlags`, `TaskAction`, `PriorityAdjustment` and a
  second `TaskExecutionContext` — `Orkeon.Domain.Agent.TaskExecutionContext` stays) and what it alone
  used (`RetryPolicy`, `ExecutionTimePolicy`, three delays of `TaskDefaults`): a run moves its tasks
  through their lifecycle itself (GAP-21). `AsyncTaskPipeline<T>`, `CrewTaskPipeline`, `ITaskQueue<T>`
  and `WorkItem<T>`: no mode queued a task through them.
- **Aggregate restores.** `CrewTask.Restore` (both overloads), `CrewTaskSnapshot`, the restore
  constructors of `CrewTask` and `CrewTaskBase<TContext>`, and `Crew.Restore` had no caller — no
  repository persists the aggregates —, and the snapshot lost a task's deliverable, LLM override and
  guardrails, as `AgentSnapshot` lost an agent's `LlmConfig` (GAP-26).
- **The crew export.** `CrewConfigurationMapper.ToConfiguration`, the class's last method, ran only in
  its tests. `CrewConfiguration.ExecutionConfig` and `CrewConfiguration.Metadata`, set by no loader
  (YAML, `.ork.ts`) and read by no run, go with it, and so do the `ExecutionConfig` record and
  `TaskDefaults.DefaultOperationTimeout`. `ManagerLlm.Describe`, which the export called, stays: the
  manager's resolver names the manager's LLM with it.
- **Settings DTOs and an execution history.** `Orkeon.Application.Common.DTOs.CrewSettingsDto`, with
  `CallbackConfigDto`, `MemoryConfigDto`, `MemoryLimitsDto`, `MemoryCleanupDto`, `VectorConfigDto`,
  `RetryConfigDto` and `TimeoutConfigDto`, had no user; `CrewDto.ExecutionHistory`
  (`ExecutionHistoryDto`) was filled by nothing.
- **Aggregate mutators.** `Agent.UpdateConfiguration`, `Crew.UpdateConfiguration` (both overloads, with
  `CrewConfigurationUpdate`) and `CrewTaskBase<TContext>.UpdateConfiguration` had no caller, and every
  new rule had to be copied into them (GAP-30, GAP-31, GAP-33, GAP-34) or they bypassed it. The rules
  stay where a crew, an agent and a task are built: a memory provider needs memory, a planning provider
  needs planning, an agent whose provider runs its own tools is refused delegation.
- **The agent stop switch.** `IAgentLifecycleManager` with `AgentLifecycleState` and
  `AgentLifecycleManager` — registered by `AddOrkeonInfrastructure()`, resolved by nothing since
  GAP-26 —, `Agent.RegisterCancellation`, `Agent.StopAsync`, `AgentKilledEvent` (30 domain events
  remain), `Crew.SetKillAllStrategy` and `Crew.StopAllAgentsAsync`. A run stops through its cancellation
  token (`orkeon run`, `orkeon-host`'s `/stop`).
- **Two ports nothing resolved.** `IYamlDiffService` and `ITemplateInstantiator`, their stubs
  (`NullYamlDiffService`, `NullTemplateInstantiator`) and their registrations: the documentation
  invited a host to replace them, but nothing called them, so a host's own implementation never ran.
  The types only they used go too: `ConfigurationDiff`, `ConfigurationChange`, `ChangeType`,
  `ConfigurationVersion`, `RollbackOptions`, `RollbackResult` and `ConfigurationVersionId`;
  `InstantiationValidation`, `TemplateType`, `InstantiationPreview`, `AgentTemplateDefinition`,
  `TaskTemplateDefinition`, `CrewTemplate`, `AgentTemplate`, `TaskTemplate` and their ids
  (`AgentTemplateId`, `TaskTemplateId`, `CrewTemplateId`), `InvalidTemplateParameters`,
  `ResolvedTemplateParameters` and `TemplateConfiguration`. `TemplateInstantiationParameters` and
  `TemplateParameterValue`, which `ITemplateEngine` uses, stay.
- **A package reference nothing used.** `Orkeon.Tools.Abstractions` no longer references
  `Microsoft.Extensions.AI`: the nine tool families, `Orkeon.Interop.AgentFramework`, four test projects
  and the `local-embeddings` example no longer restore it through it. `Orkeon.Infrastructure` keeps it,
  so the binaries and the `Orkeon` package — the only one `Orkeon.Tools.Abstractions` ships in — still
  carry it.

Documented in [Default behaviors](docs/getting-started/default-behaviors.md),
[Events, CQRS and Observability](docs/architecture/domain-events.md),
[EventHub & Crew Lifecycle](docs/architecture/event-hub-and-crew-lifecycle.md) and
[ADR-010](docs/adr/ADR-010-agent-framework-interop.md).

Breaking: every type and member listed above is removed. `AddOrkeonApplication(Action<OrkeonApplicationOptions>)`
keeps its signature; a strategy configured before it is no longer reset.

Migration: remove the assignments of the removed settings (they had no effect); build a crew through
the loaders (YAML, `.ork.ts`) or the builders (`CrewBuilder`, `AgentBuilder`, `CrewTaskBuilder`), its
settings given at construction, without `Restore` or `UpdateConfiguration`; stop a run through its
cancellation token; remove any registration of `IYamlDiffService` or `ITemplateInstantiator` (nothing
called them).

### Fixed — every published artefact carries the notices of what it redistributes, and the Studio TUIs ship the pinned package versions

`THIRD-PARTY-NOTICES.md` covered every package the shipped applications redistribute, and
travelled with the tool packages, the archives, the `.deb` and both MSIs — but not with the
container images, not for the .NET runtime the self-contained installers bundle, and not for the
`esbuild` binary (GAP-45). The two Studio TUIs, meanwhile, shipped older package versions than the
CLI, the REPL and the host.

- **The images carry the license and the notices.** `Dockerfile.runners` (the published
  `ghcr.io/orkeon/orkeon-runners` and its `local-llm` variant), `deploy/Dockerfile.host` and the
  root `Dockerfile` copy `LICENSE.md` and `THIRD-PARTY-NOTICES.md` to `/usr/share/doc/orkeon/`, the
  Debian package's path, and `.dockerignore` no longer keeps them out of the build context. An
  image's .NET runtime is its Microsoft base image's, which carries its own notices.
- **The bundled .NET runtime ships with its own notices.** After each self-contained publish —
  `orkeon`, `orkeon-host`, the Orkeon Studio applications —, `scripts/package-installers.sh` and
  its `.ps1` mirror run `python3 scripts/third-party-notices.py --runtime-notices <publish>
  --project <csproj> --to <payload>/licenses`, which copies byte for byte the license and the
  third-party notices of the runtime packs the publish's `deps.json` names, found where the
  inventory looks: `licenses/Microsoft.NETCore.App.Runtime.<rid>/`, plus
  `licenses/Microsoft.WindowsDesktop.App.Runtime.win-x64/` beside Studio on Windows. A pack
  missing, or without its notices, stops the packaging; a framework-dependent publish has none to
  copy. The `.deb` installs them under `/usr/share/doc/orkeon/licenses/`, the service MSI under
  `licenses\` (its build script requires the folder), the CLI MSI with the rest of the tree, and
  the archives' `install.sh` and `install.ps1` copy them with `THIRD-PARTY-NOTICES.md` into the
  installed tree — they copied `LICENSE.md` alone. The packaging scripts need Python 3.
- **`esbuild` has its section.** Section 12 gives the MIT license of the TypeScript transpiler the
  archives, the `.deb`, the CLI MSI and the `orkeon-runners` image ship, at the version
  `tools/scripting-esbuild/package-lock.json` locks.
- **The Studio TUIs ship the pinned versions.** `orkeon-studio-config` and `orkeon-studio-run` got
  `Markdig` and ten `Microsoft.Extensions.*` packages from `Terminal.Gui` alone, at its minimums
  (1.3.2 and 10.0.11), while `Directory.Packages.props` pins 1.4.0 and 10.0.12: central management
  pins direct references only. `Orkeon.Cli.TerminalGui` now references the five `Terminal.Gui` asks
  for — `Markdig`, `Microsoft.Extensions.Configuration`, `.Configuration.Binder` (10.0.12, a new
  central pin), `.Configuration.Json` and `.Options` —, their dependencies follow, and the
  inventory loses its eleven older rows: 151 versions, one per package.
- **`--check` keeps it so.** It also fails now when a Dockerfile that publishes an application does
  not copy both files into the image it produces (a copy in a stage the image does not inherit
  does not count), when the build's ignore file keeps either out of the context, and when a
  shipped package resolves below the version `Directory.Packages.props` pins, naming the
  applications and both versions. `scripts/test-third-party-notices.py` proves the new rules and
  the new mode on throw-away trees.

Documented in [Three ways to run Orkeon](docs/getting-started/three-ways-to-run-orkeon.md), [the
service host](docs/architecture/service-host.md), the archives' `README.md` and `CONTRIBUTING.md`.

### Fixed — a YAML agent's `guardrails:` reach its prompt, before its task's, and an unknown preset fails the load **[breaking]**

The guardrails a YAML agent declares — a `preset`, `rules`, `toolRules` — were read onto the crew's
configuration and never handed to the agent `CrewFactory` built (GAP-42): the system prompt carried
its tasks' guardrails alone, for every crew loaded from YAML — `orkeon run` on a file or a directory,
`orkeon-host`, the REPL — in every mode. Example `102-ts-codebase-documentation` ran without its
rules against invented files.

- **An agent's guardrails reach its prompt.** `CrewFactory` passes them to the agent, and the one
  prompt composer renders them before the task's, as the documentation said: the preset's header and
  rules, then the agent's own, then its tool rules for the tools it holds. A crew that declares agent
  guardrails sees its prompts change — that is the fix.
- **An unknown preset fails the load.** A `preset:` the domain does not know — `analysys` — gave no
  guardrails at all, on an agent as on a task, or kept the block's own rules alone, without a word.
  The load fails now, in one line naming the agent or the task by its key, the preset as written and
  the three known ones (`analysis, strict, creative`). A blank preset is no preset.
- **A tool written twice in `toolRules` fails the load.** `file_write` and `File_Write` name one
  tool — a tool name ignores case — and the load threw a raw "An item with the same key has already
  been added". It names the agent or the task and the tool's spellings now. A tool written without
  rules adds none, where the load failed with a raw "Value cannot be null".
- **The templates of a configuration built in code reach the agent.** `CrewFactory` passes an
  `AgentConfiguration`'s `SystemTemplate`, `PromptTemplate` and `ResponseTemplate` to the agent it
  builds: a host that built or adjusted a configuration in C# (`config with { … }`) lost them without
  a word. No YAML key is added, and nothing reads `PromptTemplate` yet.

Documented in [YAML schema](docs/architecture/yaml-schema.md#guardrails-configuration) and
[YAML and builders](docs/getting-started/yaml-and-builders.md).

Breaking: a guardrails `preset:` other than `analysis`, `strict` or `creative`, and a tool written
under two keys of `toolRules`, no longer load; the system prompt of an agent that declares guardrails
now carries them, before its task's.

Migration: write `analysis`, `strict` or `creative` — or remove the preset; keep one key per tool in
`toolRules`.

### Fixed — a team's launchers hand the run its path and its inputs as they are: one composer for `forge promote` and Studio

`cmd` reads a team's `run.cmd` first and `orkeon` splits what it hands over second; both writers of the
launchers — `forge promote` and Studio (STUDIO-50) — wrote for one of those readings, and neither told
`cmd` the file is UTF-8 (STUDIO-51). A Windows profile with an accent (`C:\Users\Zoé`) turned
`--settings` into `C:\Users\Zo├®\…` and the run went on without its settings file; French sample inputs
arrived as `├ëconomie`; written without Studio, `seuil=10%` lost its `%`; a team folder in a OneDrive
« R&D » folder cut the command; and a sample input holding a quote then `&` — text the forge's model
writes — ran what followed as a command at each scheduled run.

- **One composer writes both launchers** (`TeamLauncherScript`, `Orkeon.Domain.FileSystem`): the writers
  say what to launch, it writes the text, and `forge rename` still finds the header
  (`TeamLauncherScript.Header` replaces `ForgePromoter.LauncherHeader` and `TeamLaunchers.Header`). Each
  value is written for the runner first, then for `cmd` — every `%` doubled; a value without a quote
  between `cmd`'s quotes end to end; a value with one outside them, each quote and operator escaped; the
  stretch carrying `%~dp0` always between quotes —: every value reaches the run as written, whatever
  the team's path holds. A line break is a space in `run.cmd` — a comment line says so — and is kept in
  `run.sh`.
- **`run.cmd`'s frame**: UTF-8 without a BOM, named so by both writers; ASCII up to `chcp 65001`, the
  caller's code page captured and given back before `orkeon` starts; `setlocal EnableExtensions
  DisableDelayedExpansion` whatever the registry says; the command in a block ended by `exit /b`, which
  keeps `orkeon`'s exit code and never lets `cmd` read the file again — a launcher written again during
  a run is not read from the middle.
- **Beyond 8 191 characters** once expanded, `run.cmd` launches nothing: it prints the length, the bound
  and the longest option on stderr, and exits 1; `run.sh` stays complete. `forge promote` says so with a
  `warning` (`FORGE-LAUNCHER-TOO-LONG`) and succeeds. Studio's adoption line says it —
  `TeamLaunchers.Regenerate` returns a `TeamLaunchersResult` (written, unchanged, no team, disk refused,
  and the Windows launcher's refusal) instead of a `bool` — and says the engine's last warning in the
  sentence of its code: `FORGE-SCHEDULE-STILL-INSTALLED` no longer reads as a session folder not renamed.
- **`orkeon run --option=value` takes any value.** An initial context starting with `-` — a bullet list
  — had the run refused as an unknown option, and written `--initial-context=…` it was refused as soon
  as it held a line break or started with a space: CommandLineParser 2.9.1 reads an attached value only
  when it matches `^([^=]+)=([^ ].*)$`. `orkeon run` now reads an attached value as written, cut at the
  first `=` — several lines, a leading dash or space included (`RunnerArguments`, `Orkeon.Hosting`) —,
  and what parsed before parses the same. The launchers and Studio's `RunArgumentsBuilder` write every
  single-value option that way; Studio refuses a variable name or a mount starting with `-` before the
  launch (`STUDIO-LAUNCH-VAR`, `STUDIO-LAUNCH-MOUNT-DASH`).
- **The scheduled task runs `cmd.exe /d /v:off /s /c ""<team>\run.cmd""`**, a quoting that does not
  depend on what the path holds; systemd's `ExecStart` escapes the path (`\\`, `\"`, `%%`, `$$`) and the
  cron line puts it between single quotes (`\%` for a `%`). `forge schedule --check` reads a task of the
  former form `stale` (reason `outdated`), and an install writes former artifacts again first.
- **The command line Studio shows and copies** — the preview, « Copy the command », the history, a
  failure report — is a line for `cmd` under Windows, each argument written by the composer: pasted, it
  launches what Studio launches. PowerShell is not covered.

Migration: a team scheduled on Windows before this version reads « Schedule to reinstall » once — the
card's « Install the schedule » (`forge schedule`) sets it right. The launchers of a team adopted before
take the new form at the next change of its setting or its folders, or at the next `forge promote`.
`TeamLaunchers.Regenerate` returns `TeamLaunchersResult` — read its `Outcome`; `ForgePromoter.LauncherHeader`
is `TeamLauncherScript.Header`.

### Fixed — Studio keeps a setting's key and presents it as the run reads it: its card recognised in every language, the run's order in the TUI, a missing key said missing, Docker Model Runner's placeholder key

A model setting created on « Other OpenAI-compatible » or « None / offline » opened without a card
once the interface switched language: the editor recognised a card by its title, translated. Saved
so, the setting lost the variable holding its key — in `studio-model-profiles.json` and in its
`Llm:Profiles:<id>` entry —, and its runs, Studio's included, ran without a key. The
`orkeon-studio-config` « Test » presented another key than a run, the status bar said « key refused »
for a key never remembered, and a Docker Model Runner setting passed its « Test » and failed every
run (STUDIO-54).

- **A setting keeps its card's name.** `ModelProfile.Provider` holds the card's stable name
  (`deepseek`, `custom`, `none`), never its title; the list, the creation assistant's step 4 and the
  status bar show the title in the language of the moment, said again when it switches. A value that
  is no card name — a title an older Studio wrote, in any language, a provider the default blanked, a
  hand edit — is recognised as the store reads it (`LlmPresets.CardOf`: the name, then the English
  title, then the endpoint; an endpoint no card carries is « Other OpenAI-compatible », neither
  endpoint nor model « None / offline »): a setting always opens on a card, with that card's key
  variable. The name reaches the file with the next gesture on the settings, never at startup.
- **The TUI presents the run's key.** Its « Test » read the file's key before `ORKEON_Llm__ApiKey`;
  a run does the reverse. It now follows the run's order — `ORKEON_Llm__ApiKey`, the file's
  `ApiKey`, `Llm__ApiKey`, then the variable `Llm:ApiKeyEnvVar` names, process then user scope —, a
  parity test holding it against the runtime's own reader. Without any key it refuses, at once and
  without a request, every endpoint but Ollama's, as a run refuses it: « API key missing — name the
  variable that holds it (API key variable), or set ORKEON_Llm__ApiKey ».
- **A missing key is said missing.** The balance probe answers a new `ProviderBalanceStatus.KeyMissing`
  for an account whose variable holds no key, without a request; « key refused » is a provider's 401
  or 403 alone. The bar says « DeepSeek · key missing », quietly like a refusal, in the five
  languages.
- **Docker Model Runner gets its placeholder key.** The run reads that server as OpenAI, whose
  dialect refuses to call without a key: `orkeon init` and the TUI write `"ApiKey": "not-needed"`,
  Studio's card wrote nothing. A setting of that card now writes it in its entry, and in `Llm` when
  elected — the one `ApiKey` Studio writes, where none is set —, lays it on its launches and presents
  it to « Test »; electing another card takes exactly that value out of `Llm`, where it would pass
  before the elected setting's `ApiKeyEnvVar`. One rule judges a section's key
  (`LlmProfilesSection.KeyAgrees`), and an entry or an `Llm` that disagrees is written again at the
  next gesture, once. The validator reports `not-needed` beside an `Llm:ApiKeyEnvVar` (a hand edit).
  `DockerModelRunnerDefaults.DefaultModel` and `.ApiKeyPlaceholder` are aliases of the
  `Orkeon.Constants.Llm` values Studio reads, as `BaseUrl` already was.

Migration: a setting the default damaged finds its card again when Studio opens; if its key was in
`ORKEON_Llm__ApiKey` (an « Other OpenAI-compatible » setting from before STUDIO-49), remember it again
in the editor — the card keeps its key in `ORKEON_CUSTOM_LLM_API_KEY`. A Docker Model Runner setting
runs at once from Studio, and outside Studio after the first gesture on the model settings.

### Fixed — a stopped A2A server no longer binds its port again, the test suites hold under load, the examples restore offline, and CI's NuGet cache keeps the packages

Under Linux and macOS, `A2AServer.StopAsync` called `HttpListener.Close()` on the listener `Stop()`
had just stopped. `Close()` goes back through the listener's prefixes and, finding nobody listening on
the port any more, binds it for an instant to remove them: a port another process had taken in between
made the stop throw « Address already in use », and a free one was held again, for that instant, under
whatever started next on it (GAP-41).

- **The stop releases the listener with `Abort()`**, which marks a stopped listener closed without going
  back through its prefixes; under Windows it frees the HTTP.sys configuration, as `Close()` does.
- **A test server takes its port through `LoopbackPorts`** (`Orkeon.Tests.Shared.Network`). Thirteen
  copies of one probe — a port asked of the system, given back, bound later by the server — left it to
  whoever took it in between, and the A2A suites failed on « Address already in use » when passes ran at
  once. `StartAsync` and `StartListener` start the server and, when the port turns out to be taken,
  dispose it and start again on another one — ten attempts, then the last conflict; any other refusal
  still fails at once. Servers listen, and clients call, on `127.0.0.1` (`localhost` under Windows):
  `HttpListener` binds the first address its host name resolves to — `::1` for `localhost` on a machine
  that ranks it first, a port the probe never looked at. `Refusing()` holds a port nobody listens on, for
  the tests that need a refused connection (an LLM endpoint probe, an IMAP mailbox) and used to count on
  a port given back staying free. The three HTTP doubles stop their listener with `Close()` alone.
- **A span test reads an `ActivityRecorder`** (`Orkeon.Tests.Shared.Telemetry`), which adds each
  activity under a lock and hands out copies; the test keeps to the trace of a root it starts. Three
  tests walked a list a process-wide listener kept filling from their neighbours' threads — one of the
  two `Orkeon.Application.Tests` failures that came one pass in three.
- **A delay a test must not reach is a hang guard, and no unit test bounds a duration.** Eight
  receptions of messages already sent (budgets of 100 ms to 1 s), five more of 1 s, and a 200 ms token
  racing the EventHub's own 100 ms timeout now allow 10 s and end on what they wait for. Three assertions
  that measured the machine — a token count under 100 ms, 10,000 tasks created under 1 s, a kickoff under
  5 s — are gone, and what those tests check otherwise stays; a timestamp is checked between two readings
  of the clock, never within a window of it, and a duration a test builds comes from one reading — two
  put the time between them into a duration expected to the millisecond.
- **A test that orders two concurrent ends forces that order.** The sequential `asyncExecution` test
  expecting the hook to hear the synchronous task before the launched one only made the launched task
  wait for the other to start: under load, its own end reached the hook first. It now waits until the
  hook has heard the other's end. The hook is unchanged — it hears each task as it ends.
- **The examples restore offline.** `Microsoft.Agents.AI` 1.20.0 brings an edge to
  `Microsoft.Extensions.AI.Abstractions (>= 10.8.2)`, a version nuget.org never published: every restore
  of `examples/interop/agent-framework` whose graph changed asked nuget.org for the version list, and
  failed without a network. The example references the package directly, at its central version
  (10.10.0, the one already resolved): a direct reference eclipses the transitive edge, which NuGet no
  longer resolves. The resolved graph is unchanged.
- **CI's NuGet cache keeps the packages.** Under Linux a restore fills `<repository>/packages`
  (`RestorePackagesPath`, `Directory.Build.props`), but the eight `actions/cache` steps kept
  `~/.nuget/packages`: every run downloaded the whole closure again — TreeSitter and the two ONNX
  Runtimes alone weigh 1.3 GB. They cache both folders now, keys unchanged.

Nothing breaks.

### Fixed — model requests are bounded as declared: an agent's and a crew's `maxRpm`, the host's `RateLimiting` on every call **[breaking]**

An author writes `maxRpm: 5` on a YAML agent, `.MaxRpm(5)` in C# or `CrewBuilder.MaxRpm(30)` to stay under a
provider's quota, and the docs promised a limit: nothing applied it, and the host's own limit did not do what it
said (GAP-38).

- **`maxRpm` is applied, as CrewAI's `max_rpm`.** An agent's bounds the model requests it makes per minute —
  each turn of its loop, the tool-free retry, the correction round, a ballot, a delegated colleague's turn
  (counted for that colleague); a crew's bounds those of all its agents and its manager together, parallel waves,
  vote candidates and ballots included. Each object that declares one has a sliding window of 60 s: the request
  of too many waits its turn — it never fails the task —, follows the run's token (Ctrl+C, `RunTimeout`,
  `/stop`) and counts nothing when cancelled, and one Information line says who waited, how long and under which
  limit. A crew run from a task counts in its own window. No default: `Agent.MaxRpm` and `Crew.MaxRpm` are
  `int?`, the 10 and 100 that applied nowhere are gone, and a run that declares nothing waits for nothing.
- **Every surface declares both levels.** YAML gains the crew's `maxRpm:` (`max_rpm:` accepted — a crew ported
  from CrewAI lost it without a word), `.ork.ts` gains `agentBuilder().maxRpm(n)` and `crewBuilder().maxRpm(n)`,
  in YAML parity; the procedural shape, whose `ctx.llm` calls are no agent turns, applies neither and warns once.
  A `maxRpm` of 0 or less fails the load naming the agent or the crew — it became 10 —, and so does a `maxIter`
  of 0 or less — it became 15.
- **`maxIter` is 20 everywhere.** `AgentDefaults.MaxIterations` goes from 15 to 20 — CrewAI's `max_iter` — and is
  the one default: YAML, `AgentConfiguration` and the DSL read it, and a C# agent without `MaxIterations` takes 20
  turns too. `ExecutionOrchestrator.MaxIterations` and the loops' default iteration count never applied —
  `Agent.Create` refuses zero — and are removed: the number of turns is set on the agent.
- **The host's per-agent cap is each agent's.** `RateLimiting:AgentRequestsPerMinute` was a bucket per role, in a
  singleton, created once: two teams of `orkeon-host` with a "Researcher" each shared it, from one run to the
  next. It now bounds each agent instance's own window, with its `maxRpm`, the stricter winning, and a request
  over it waits instead of failing after the queue.
- **The global and per-provider caps hold on every call, once.** `GlobalRequestsPerMinute` counted agent turns
  only: the manager, the planner, the RAG pipeline, the judges and the cognitive memory called the model past it.
  The limiter now sits at the entrance of each provider, around the token meter — the factory,
  `AddOrkeonLlmProvider` and `AddOrkeonLlmProfile`, the profile registry's `ForProvider`, the manager's resolver,
  a C# crew's planner —: every model call of the host takes one lease, the scripts' `ctx.llm` included, and
  `orkeon run` no longer wraps their provider in a limiter of its own. A provider passed through two entrances
  takes one lease, a call made inside another limited call takes none (`MaxConcurrentRequests: 1` no longer
  waits on itself), and a provider that runs its own tools (the Microsoft Agent Framework bridge) is not limited
  itself: what it calls of Orkeon's model is. A refusal is retried five times on its `RetryAfter`, then the turn
  fails as a failed call, with the limiter's reason.
- **The manager's review stops with its run.** It went out under `CancellationToken.None`: Ctrl+C, `RunTimeout` and
  `/stop` waited for it. `IManagerAgent.ReviewOutputAsync` takes the run's token, and a cancelled review is no
  longer an approval by default.

Documented in [YAML schema](docs/architecture/yaml-schema.md), [YAML and builders](docs/getting-started/yaml-and-builders.md),
[Security](docs/architecture/security.md), [Configuration](docs/reference/configuration.md),
[LLM providers](docs/architecture/llm-providers.md), [Opt-in subsystems](docs/reference/opt-in-subsystems.md),
[Scripting DSL](docs/reference/scripting-dsl.md), [Process types](docs/orchestration/process-types.md) and
[Agent Framework interop](docs/reference/agent-framework-interop.md).

Breaking: a declared `maxRpm` makes requests wait; `Agent.MaxRpm`, `Crew.MaxRpm`, `AgentCreateOptions.MaxRpm`,
`CrewCreateOptions.MaxRpm` and `AgentSpawnRequest.MaxRpm` are `int?`, null by default, `AgentConfiguration.MaxRPM`
is `int? MaxRpm` and `CrewConfiguration` gains `MaxRpm`; `AgentDefaults.MaxRequestsPerMinute` and
`CrewDefaults.DefaultMaxRpm` are removed; a `maxRpm` or `maxIter` of 0 or less fails the load; `maxIter` is 20 in
C# too; the host's per-agent cap holds per agent and waits; every model call of the host counts against
`RateLimiting`, the manager, the planner and RAG included; `ILlmRateLimiter.AcquireAsync` loses its `agentRole`
parameter; the `ExecutionOrchestrator` constructors lose their rate limiter and `MaxIterations` is gone;
`RateLimitingOptions` moves to `Orkeon.Application.Configuration`; `RateLimitedLlmProvider` is built by
`RateLimitedLlmProvider.Wrap` and loses `AgentBucketCount` and `DefaultMaxAcquireRetries`;
`IManagerAgent.ReviewOutputAsync` takes a `CancellationToken`; `CrewConfigurationMapper` exports a crew's rate as
`CrewConfiguration.MaxRpm`, no longer `ExecutionConfig.MaxRPM`.

Migration: remove `maxRpm`, or raise it, where a crew must not wait; read `Agent.MaxRpm` and `Crew.MaxRpm` as
`int?` (null: no limit of its own) and omit `maxRpm` instead of writing 0. Set `MaxIterations(15)` where a C#
agent relied on 15, on the agent: `ExecutionOrchestrator.MaxIterations` never applied and is gone. Raise
`RateLimiting:GlobalRequestsPerMinute` if the manager, the planner or RAG now make a busy host wait. Call
`AcquireAsync(provider, ct)`; build `ExecutionOrchestrator` without a rate limiter; import `RateLimitingOptions`
from `Orkeon.Application.Configuration`; a custom `IManagerAgent` takes the token its review is given.

### Fixed — the third-party notices list every package the shipped binaries redistribute, generated from the restore and checked by CI

`THIRD-PARTY-NOTICES.md` promised an entry for whatever the packages and the installers
redistribute, and had one for 11 of the 162 package versions the shipped applications carry:
`CommandLineParser`, which both tools and every installer ship, had none (GAP-36, decision 6).
It also said OpenTelemetry and Gremlin.Net "intentionally get no entry", being "only
referenced" — the `orkeon` tool ships both.

- **A generated inventory closes the file.** `scripts/third-party-notices.py` reads the
  restored `project.assets.json` of the six applications the tool packages, the installers
  and the container images publish — `orkeon`, `orkeon-repl`, `orkeon-host` and the three
  Orkeon Studio applications the installers add — and the `.nuspec` files of the local NuGet
  cache: the assets file's `packageFolders`, a Windows path there also tried at its WSL
  mount, then `NUGET_PACKAGES`, then `~/.nuget/packages`, never the network. It lists every
  package whose restored target puts a file in the build output (a managed, satellite, native
  or RID-specific asset; compilers, analyzers and meta-packages put none and are left out):
  162 versions of 151 packages, each with its license — the SPDX expression, or the file the
  package names —, its copyright, its project URL, the applications that ship it and the
  texts it carries. Sorted, and dated nowhere: the same restore writes the same file.
- **The texts travel with the binaries.** Every license file and every notice the packages
  ship is copied verbatim (line endings normalised, the NUL that ends an RTF file dropped),
  each distinct text once: 10 license texts and 10 notices — Gremlin.Net's Apache-2.0
  `NOTICE`, propagated as §4(d) requires, and the third-party notices of ONNX Runtime,
  OpenTelemetry, the .NET libraries, Roslyn, ML.Tokenizers and Onigwrap. The file grows from
  39 KB to 749 KB; the tool packages, the archives, the `.deb` and both MSIs already ship it.
- **The criterion says what ships.** Criterion (a) states that the tools and the installers
  redistribute the whole runtime closure of their binaries, and the OpenTelemetry/Gremlin.Net
  sentence is corrected: both are in the inventory, with their texts. The hand-written
  sections stay — provenance, decisions, Apache.Arrow's `NOTICE` —, and a package one of them
  covers points to it.
- **CI keeps it true.** `ci.yml` runs `python3 scripts/third-party-notices.py --check` after
  its restore. It fails, package by package, when the closure changed without regeneration;
  when a version a hand-written section states is no longer the shipped one — it found two,
  Jint (`4.16.1` stated, `4.16.3` shipped) and Microsoft.Extensions.AI.Abstractions (`10.9.0`,
  `10.10.0`), both corrected —; and when an installer table, a `PackAsTool` project or a
  Dockerfile publishes a project the script does not read. A package bump, Dependabot's
  included, therefore needs the script run and the file committed (CONTRIBUTING says so).
  `scripts/test-third-party-notices.py` proves the rules on a throw-away tree, before any
  restore.

### Fixed — Studio's « Test connection » and balance read present the setting's own key, never the default's

A model setting with no key remembered was tested with the key of Studio's own process in
`ORKEON_Llm__ApiKey` — the default's key —, sent to the setting's address: a « Z.AI » setting without
its key sent the default's DeepSeek key to Z.AI, and a Docker Model Runner setting carried it too. The
balance read did the same for an account whose variable held no key — and for a setting that names no
variable at all (GAP-36).

- **The setting's key, and it alone.** « Test connection » and the balance — the editor's « Read the
  balance », the profile rows and the status bar — present the key typed and not yet remembered, else
  the one remembered under the setting's variable. `ORKEON_Llm__ApiKey` serves only a setting whose
  variable it is (an « Other OpenAI-compatible » setting created before STUDIO-49). A setting that needs
  a key and has none is refused without a request, with the existing « API key missing — remember it
  first »; a setting that needs none — Ollama, Docker Model Runner — presents none, not even a key typed
  for another card; an account whose setting names no variable presents none.
- The run of the elected default still reads `ORKEON_Llm__ApiKey` first when it is set (STUDIO-49): the
  test checks the setting's key, and `orkeon doctor`'s `llm-config` row and the run's startup line say
  where the default's key really comes from.
- `ProviderBalanceAccount.RequestWith` loses its fallback, and `ProviderBalanceAccount.For` gives a
  setting that names no variable an account with none (`KeyVariable` empty) where it named
  `ORKEON_Llm__ApiKey`. The documentation of `LlmProbeRequest.ApiKey` says whose key it is.
- Nothing changes on screen otherwise. `LlmSectionViewModel` keeps what the settings screen calls — the
  election, the mirror of the settings into `Llm:Profiles`, the healing of a default, the RAG's profile,
  the entries written by hand, the check of a name — and loses a form of the `Llm` section and a
  connection test that no view displayed, with its probe and dispatcher, and three strings no screen
  showed (`Studio.Settings.NoBaseUrl`, `Studio.Settings.CustomProvider`,
  `Studio.Settings.ApiKeyRecommendation`, in the five languages).

Documented in [Orkeon Studio](docs/architecture/studio.md).

### Fixed — `orkeon init`'s probe presents the key a run presents, a run reads no settings file but the one it resolved, and `orkeon-host` names only the profiles it offers **[breaking]**

Three tools said something other than what a run does (GAP-36, decisions 3, 5 and 7).

- **`orkeon init`'s probe presents the run's key.** It read the variable `-k` names, in its own process
  alone — before `ORKEON_Llm__ApiKey`, the key a run takes first, and never in the Windows user scope,
  where Orkeon Studio remembers a key: a key Studio remembered failed the probe that the run then
  passed, and with both set the probe tested a key the run would not send. It now reads the file it
  has just written as `orkeon doctor` does (`RunnerSettings.ReadConfiguration`, then
  `LlmSettings.ReadDefault`): the key the configuration resolves, else the variable
  `Llm:ApiKeyEnvVar` names, in the process then in the user scope, presented to the endpoint the run
  calls. It says where the key comes from (`Probing <endpoint> — API key from the variable named by
  Llm:ApiKeyEnvVar (user environment)`), never the key; a reference that resolves nothing is warned
  about first, in the runners' words (`OperatorMessages.LlmApiKeyReferenceUnresolved`); and a section
  a run would refuse — a reference that is no variable's name, an `ApiKey` written `${NAME}`, a
  `BaseUrl` that is no address — fails the probe with the reader's message and exit 1, the file written.
- **A run reads the settings file it resolved, and no other.** The runner hosts were the default .NET
  host plus the resolved file and the `ORKEON_` layer: under the file lay the `appsettings.json` and
  `appsettings.{Environment}.json` of the current directory — since .NET 10, `<binary>.settings.json`
  and its environment twin too — and, in `Development`, the user secrets. The folder a terminal
  happened to be in could add profiles, MCP servers or mounts to a run, and `orkeon doctor`, which read
  the file and `ORKEON_` alone, never saw them; the REPL did the same, its `--settings` files under the
  bare variables. Every host now composes the same layers, in one place
  (`RunnerSettings.ComposeSources`): the environment variables without a prefix — the lowest layer, kept
  because the OpenTelemetry exporter reads the `OTEL_EXPORTER_OTLP_ENDPOINT` a .NET Aspire AppHost sets
  through it, which `OpenTelemetryIntegrationTests` now proves —, the resolved file (for the REPL, its
  `--settings` files, else the global file), then `ORKEON_`; the REPL's command line stays last.
  `RunnerSettings.ReadConfiguration` — `orkeon doctor`, `orkeon forge`, the `--llm-profile` guard,
  `init`'s probe — composes the same layers: a bare `Llm__ApiKey` that changed the run changes the
  verdict too. `orkeon-host` reads `./appsettings.json` as its settings file when `--settings` names
  none, as its help says — by resolution now, which its reserved-root guard reads too —, never under
  the file `--settings` names. No source watches its file: the default host's watcher over the
  current directory's tree went with its files. The REPL project's own `appsettings.json`, never
  shipped, is deleted.
  **Migration:** a setting that came from an `appsettings.json` of the current directory moves into the
  settings file the run resolves (the `appsettings` row of `orkeon doctor` names it) or the one
  `--settings` names; `orkeon-host --settings <file>` no longer reads `./appsettings.json` beside it; a
  bare variable that overrode a REPL settings file (`Llm__Model`) takes the `ORKEON_` prefix
  (`ORKEON_Llm__Model`), which wins over the file in every host.
- **`orkeon-host` names the profiles it offers.** Under its allow-list (`Orkeon:Host:LlmProfiles`), the
  startup line `LLM profiles offered to crews besides the default` listed every profile of the file,
  and a hidden profile whose key reference resolved nothing was warned about, on the log and on stderr,
  although no crew can name it. The lines now follow the registry (`ILlmProfileRegistry.Names`, the
  list applied): the profiles offered, each with where its key comes from, a warning for those alone,
  and one Information line naming the hidden ones (`LLM profiles hidden from crews by the host's
  allow-list: …`). Without a list — `orkeon run`, the REPL — nothing changes.

### Fixed — Studio saves the settings it has just written: an integer a screen writes is a number to the validator

Electing a model setting that pins a timeout — the 600 s Studio prefills for DeepSeek, Kimi, Z.AI and
MiniMax — made the settings screen impossible to save: the validator reported « 'Llm:TimeoutSeconds'
must be a number » and refused every save, the novice's automatic one included, until Studio read the
file again. `orkeon-studio-config` refused at every save any file holding an integer — a token budget,
a timeout, a rate limit, the shipped sample's among them. The file was right (`600`); the settings
document was not: a node a screen writes holds the CLR value it was given, which System.Text.Json
converts to no other type, so the document's number reader found no `double` in the `int` an election
had just written (STUDIO-53).

- **A number reads as its JSON text, whatever created its node** — as the file will, once read again.
  `AppSettingsDocument.GetDouble` reads any JSON number, the integers the screens write included
  (`Llm:MaxTokens` and `TimeoutSeconds`, the `RateLimiting` fields, `LlmLogging:MaxBodyLengthChars`,
  `Orkeon:Rag:Corrective:MaxIterations`); `GetInt32` reads one whose text is an integer within `int` —
  a `double` 600 included, never `0.5`, `600.0` or a value beyond `int`, which the configuration binder
  refuses for an integer too. A number written as a string still reads, as the binder reads it. What
  the screens write and what the validator checks are unchanged.
- **NaN and the infinities read as no number**, where their text — JSON has none — would have thrown:
  a document holding one cannot be written, and the validator refuses it by the field's name.
- **`ui-preferences.json` follows the same rule**: Settings › Studio's minutes and days, written as
  integers, read back as « not set » from the document that had just written them. Studio reads the
  file again before each use, so nothing showed.

### Fixed — a temperature or a `top_p` a crew declares reaches the model whatever its value, one nothing sets is not sent, and what a dialect cannot send is said **[breaking]**

The engine took a temperature of 0.7 and a `top_p` of 1.0 for "not set" (GAP-36, decisions 1 and 8). An agent
writing `temperature: 0.7` on a profile configured at 0.2 ran at 0.2, in YAML, `.ork.ts` and C# alike; a
`top_p: 1.0` never left; and a host that set no temperature sent 0.7 on every call — a value the default models
of OpenAI (`gpt-5.6-sol`) and Anthropic (`claude-sonnet-5`) refuse, which `orkeon init --provider openai` and
Studio's OpenAI and Anthropic cards choose without writing a temperature.

- **What nothing sets is not sent.** `LlmConfig.Temperature` and `LlmConfig.TopP` are `double?`: null when
  nothing sets them — not the call, not the agent, not the task, not the profile —, and then no payload carries
  them: the model applies its own default (often 1; the Modelfile's on Ollama). No producer writes the engine's
  0.7 and 1.0 any more — the YAML loader, `LlmSettings` (an `Llm` section or a profile without `Temperature`),
  `LlmConfig.OnProfile()` and `Create`, `CreateValidated` (`double? temperature = null`, `double? topP = null`,
  ranges checked when set) —, and every payload writer omits what is unset: the OpenAI dialect's two builders,
  Anthropic, Ollama's three, Azure's stream. Mistral still writes `top_p: 1` when nothing sets one
  (`AlwaysEmitTopP`, measured); Kimi re-sends its mandated temperature only for a value that was set and
  refused. The runners' startup line says `temperature=(not set: the model's own)`.
- **What is set is sent, whatever its value.** The composer forwards an agent's temperature and `top_p` when they
  are set — it compared them with 0.7 and 1.0 —, and a call's configuration that sets neither runs on its
  provider's (`LlmConfig.InheritFrom`), like its output cap and its timeout. The penalties keep no unset value:
  0, every vendor's default.
- **The export writes what is set.** `YamlCrewExporter` writes an agent's temperature at 0.7 like any other, its
  `topP`, `thinking`, `responseFormat` and `responseSchema`, `cache` and profile, and a task's `llmOverride` —
  what the loader reads back.
- **What a dialect cannot send is said** (decision 8). Ollama writes `top_p`, `seed` and `stop` in its `options`
  — it dropped a `top_p` a YAML crew declared —, through `OllamaRequestOptions.Builder`, whose `AddTemperature`,
  `AddTopP` and `AddSeed` take nullable values, with `AddStop`. A `frequency_penalty` or a `presence_penalty`, on
  every dialect, and a `seed`, everywhere but Ollama, is answered with the structured warning (event 110) naming
  it — they were dropped in silence. The penalties, the seed and the stop sequences of an agent's `LlmConfig`
  reach the call (the tool-free retry keeps them, and the agent's model, which it lost); both MEAI adapters carry
  the stop sequences, and an `IChatClient` wrapped as a provider no longer receives a temperature, a `top_p` or a
  zero penalty nobody set. The Microsoft Agent Framework bridge announces a temperature or a `top_p` whatever its
  value.
- `LlmParameters`, which nothing used and whose 0.7 and 1.0 contradicted the rule, is deleted, with
  `LlmDefaults.DefaultTemperature`; `LlmDto.Temperature` is nullable. Studio's hint under the temperature field
  says what an empty one now means, in the five languages.

Documented in [LLM providers](docs/architecture/llm-providers.md),
[the provider comparison](docs/reference/llm-providers-comparison.md),
[Configuration](docs/reference/configuration.md#llm-provider-llm-section) and
[the YAML schema](docs/architecture/yaml-schema.md).

Breaking: a host or a crew that sets no temperature runs on the model's default (often 1) instead of 0.7, and
one that sets no `top_p` sends none; `LlmConfig.Temperature` and `TopP` are `double?`;
`LlmConfig.CreateValidated` takes `double? temperature = null` and `double? topP = null`; `LlmParameters` and
`LlmDefaults.DefaultTemperature` are removed; `LlmDto.Temperature` is `double?`;
`OllamaRequestOptions.Builder.AddTemperature`, `AddTopP` and `AddSeed` take nullable values.

Migration: write `Llm:Temperature: 0.7` — and `Temperature` in each `Llm:Profiles:<name>` — to keep the former
sampling; read `LlmConfig.Temperature` and `TopP` as nullable; build an `LlmConfig` where an `LlmParameters` was
built.

### Fixed — a scheduled team runs as Studio launches it: on its model setting, with its folders

A team Studio scheduled (STUDIO-27) is run by the operating system through its `run.cmd` or `run.sh`,
and `forge promote` writes those launchers with the folders inside the team and nothing else. A team
adopted on « Z.AI », with a folder of the user's bound in Studio, ran fine from Studio and, scheduled,
on the default profile without that folder — and Studio never saw it: a run the system starts is in
no history. `orkeon run` had no option naming a profile (STUDIO-50).

- **`orkeon run --llm-profile <id>`** elects one of the host's profiles — `Llm:Profiles:<id>`, which
  Studio writes for each model setting (STUDIO-48) — as the run's default: the `Llm` section becomes
  that profile, whole, for the run. Every field it pins is taken, one it leaves unset stays unset, and
  its key is its own (`ApiKey`, or the variable its `ApiKeyEnvVar` names), never the default's. Every
  call that names no profile of its own runs on it — agents, tasks, the hierarchical manager, the
  planner, the RAG without `Orkeon:Rag:LlmProfile`, a script's `llm.default_` —, the endpoint probe
  before a kickoff and the startup line included (`LLM profile <id> elected as the run's default
  (--llm-profile)`, its key's source told by the profile's own path). The profiles stay offered by
  name, `default` elects nothing, and the id is matched without regard to case. An id the settings
  and the `ORKEON_` environment do not define is refused in one line with exit 1 before any host,
  listing the ones they do — as a crew naming an unknown profile fails its load — on the YAML and
  crew-directory paths, a declarative or procedural `.ork.ts`, `--validate` and `--list-tools`
  (`RunnerExecution.EnsureLlmProfileIsKnown`); a host built without that guard refuses it too
  (`RunnerHost.Build(…, llmProfile)`). The name is `RunOptionNames.LlmProfile`
  (`Orkeon.Constants.Cli`), which the runners declare and Studio writes.
- **Studio writes a team's launchers again from its companion file** (`TeamLaunchers`): the team's
  setting as `--llm-profile` — its host profile; a team on no setting, on one renamed or removed since,
  or on one offered to no crew runs on the default, as Studio launches it —, its folders as a Studio
  launch passes them — a settings declaration by `--mount-id`, the team's own folders by `--mount`
  anchored to the launcher's folder —, the settings file the settings screen writes as `--settings`,
  the brief's sample inputs and the engine's header (`forge rename` reads it). At adoption, at each save
  of « Change the folders », and when a setting a team names is created, renamed, removed or offered
  to crews no more. Never a key. An edit made by hand in a launcher is written over at the next of
  those changes, and its second line says so. The launcher names are `ConventionalNames.WindowsTeamLauncher`
  and `PosixTeamLauncher` (`Orkeon.Constants.FileSystem`), shared by both writers.
- **Installed schedules keep working.** The registration the operating system holds runs the launcher
  by its path and passes it nothing (the task's `Command`, the unit's `ExecStart`, the cron line): a
  launcher written again is what the next scheduled run executes, and nothing is reinstalled. A team
  adopted before this version keeps the launchers the engine wrote until one of those changes, or its
  next re-adoption. The scheduled run reads the settings file as saved: save the settings screen after
  creating a setting a team runs on.

### Fixed — hosts: a stopping `orkeon-host` starts no run, a refused setting is one line and an exit code everywhere, `sendSubscribe` streams a run's progress, and stdout carries the protocol alone **[breaking]**

`orkeon-host` and `orkeon` went wrong at three moments their user does not choose (GAP-35): while the host
stops, when it refuses a setting at start, and when a program reads their output.

- **A stopping host starts nothing.** The drain admitted what arrived during its grace period: a chat
  message or an A2A task loaded a crew and called the model, the drain waited for it, then stopped it — "The
  run was stopped." about work that should never have begun, and a stop that lasted the whole grace.
  `CrewHostService` now closes admission first: from then on a run asked for is refused with "The host is
  stopping: this run was not started. Send it again once the host is back." — the chat answers it with no
  acknowledgement and no Stop button, an A2A peer reads it as `Failed`. The runs admitted before keep their
  grace, `/status` and `/stop` still answer, and the channel and the A2A server still stop after the drain.
- **A refused setting is one line and an exit code.** `RunnerHost.Build` raises one type,
  `RunnerSettingsException` (`Orkeon.Hosting`, an `InvalidOperationException`), for every setting it refuses:
  a retired key, an invalid `Llm:Profiles`, an unknown `Orkeon:Rag:LlmProfile`, `MCP:EnableServer`, mounts it
  cannot honour, a value the configuration binder cannot convert (the `MCP:Servers` entries are now bound at
  build too), a settings file it cannot read — named, with the line and the position —, and a
  `RaggableTree:Embedding:BaseUrl` or `Telemetry:OtlpEndpoint` that is no `http(s)` address, refused by its
  key instead of a bare `UriFormatException`. A host already built is disposed first. `orkeon run` — YAML,
  crew directory, declarative `.ork.ts` —, `--validate`, `--list-tools` and `orkeon mcp serve` answer it with
  `ERROR: <reason>` as the last stderr line and exit `1` (an `--events jsonl` run closes its stream on
  `run.finished`, exit code 1); a procedural `.ork.ts` with `orkeon run: <reason>` and `orkeon forge` with
  `orkeon forge: <reason>`, exit `1` both — they were exit `2`. Most of these ended on an unhandled
  exception, a stack and a core dump (exit 134). `ORKEON_DEBUG=1` prints the exception before the line.
- **`orkeon-host` refuses before it starts.** Its whole pre-start sequence runs under the exit-78 barrier: the
  configuration it boots from, its sections — `Orkeon:Host` with its `A2A`, `Orkeon:Host:Discord`, and the
  `A2A` section —, now read once, at start (a `RunTimeout` or a `Discord:ProgressInterval` of `"abc"` refuses
  the start by its key; bound lazily, they crashed it once it built its services), and the runner host. A
  refusal is one line on stderr and in the Windows event log, and systemd does not restart on it.
- **A2A under the Windows service.** HTTP.sys lets `NT SERVICE\Orkeon` listen only on a URL reserved for it,
  and its refusal crashed the service, restarted twice in silence. It is now a refused configuration —
  exit 78 — that names the prefix and, for an access denied, the command to run once as an administrator
  (`netsh http add urlacl url=<prefix> user="<account>"`). `install-service.ps1 -A2AUrlPrefix <prefix>` makes
  that reservation when it registers the service, records it under the service key, and `-Uninstall`
  removes it; the service MSI reserves nothing — it installs before any configuration — and its closing
  screen gives the step.
- **`sendSubscribe` streams a run's progress.** A peer following a task read `Working`, then nothing until the
  final state, while a chat thread watching the same run read a line per finished task.
  `IA2ATaskRouter.RouteTaskAsync` takes an `IProgress<string>`: the server passes one for `sendSubscribe`
  only, and each line becomes a `Working` update carrying it in `A2ATaskUpdate.Message` (`message`, omitted
  when null; `partialOutput` stays the output). One writer drains one queue, so no line follows the final
  state. `orkeon-host` reports the lines a thread reads; the agent router reports none.
- **Stdout carries the protocol alone.** `Telemetry:ExportToConsole` attached OpenTelemetry's console
  exporter, which writes on stdout — into `--events jsonl`, the `--list-tools` manifest, `orkeon mcp serve`,
  `orkeon forge --events jsonl` and `orkeon email accounts --json`. It is removed, with
  `Telemetry:PrometheusEndpoint`, which nothing read, and the `OpenTelemetry.Exporter.Console` package; a
  section that still writes either key, whatever its value, is refused, naming the OTLP collector that
  replaces it.
- **`orkeon mcp serve` never starts under itself.** Settings declaring `orkeon mcp serve` under their own
  `MCP:Servers` made each server start another before answering, until the first gave up after 30 s. A
  serve now sets `ORKEON_MCP_SERVE` in its environment before it connects its servers, and one that finds
  it set refuses to start: exit `1`, one line. The stderr of a stdio MCP server is read as it is written —
  to the log at Debug, under the server's id — so a talkative server no longer blocks, and the last line of
  a server that stops is joined to the connection error, with its exit code.
- `docs/tools/inventory.md` said the `rag_*` tools were neither registered nor attachable for a YAML crew:
  every runner host and the REPL register them, and an agent that lists one receives it.

Documented in [Service host](docs/architecture/service-host.md), [A2A conformance](docs/reference/a2a-conformance.md),
[CLI](docs/reference/cli.md), [MCP integration](docs/architecture/mcp.md), [Hosting](docs/reference/hosting.md),
[Configuration](docs/reference/configuration.md), [Opt-in subsystems](docs/reference/opt-in-subsystems.md) and
[Tool inventory](docs/tools/inventory.md); the [service-host example](https://github.com/Orkeon/orkeon/blob/main/examples/service-host/README.md) follows a
task with `sendSubscribe`.

Breaking: `IA2ATaskRouter.RouteTaskAsync` takes an `IProgress<string>? progress` before its token, with no
overload left; `RunnerHost.Build` raises `RunnerSettingsException` — still an `InvalidOperationException` — for
a refused setting, and now for an unreadable settings file and an address that is none; `TelemetryOptions`
loses `ExportToConsole` and `PrometheusEndpoint`, and both keys are refused; `Orkeon.Infrastructure` and the
`Orkeon` package no longer depend on `OpenTelemetry.Exporter.Console`; a procedural `.ork.ts` and `orkeon forge`
exit `1`, no longer `2`, on a refused setting; `orkeon mcp serve` refuses to start under another one; the
configuration constructor of `StdioMcpTransport` takes an optional `serverId`.

Migration: a router of your own takes the `progress` parameter and reports its steps to it, or ignores it; a
caller passes `progress: null` before its token. Remove `Telemetry:ExportToConsole` and
`Telemetry:PrometheusEndpoint` from the settings and point `Telemetry:OtlpEndpoint` (or
`OTEL_EXPORTER_OTLP_ENDPOINT`) at a collector; a C# host that wants the console exporter references
`OpenTelemetry.Exporter.Console` itself and adds it to its own `AddOpenTelemetry()`. A script that read exit `2`
as a refused setting reads `1`. Under the Windows service with A2A, register with `-A2AUrlPrefix`, or run the
`netsh http add urlacl` the refusal names. Remove an `orkeon mcp serve` entry from the `MCP:Servers` of the
settings that same serve reads.

### Removed — public surfaces nothing called: a tool adapter that skipped the guard, per-crew Guardian policies, the request DTOs and per-agent planning; a crew's request rate is exported under its own key **[breaking]**

Public types and members that no shipped code called — several of them wrong — are deleted rather
than kept (GAP-26):

- **A tool adapter that skipped the guard.** `BaseToolToAIFunctionAdapter` (`Orkeon.Tools.Abstractions`)
  turned an `IBaseTool` into an `AIFunction` that called the tool directly: past the Guardian, the
  truncation, the result sanitizer and the audit every tool call crosses since GAP-09. Nothing called it.
- **One Guardian policy per host.** `GuardianPolicyEngine.SetCrewPolicy` and `SetAgentPolicy` had no
  caller, so the per-crew and per-agent policies were always empty. They are gone, and
  `GetPolicy(crewId, agentId)` is the `Policy` property: the host's `Orkeon:Guardian:DefaultPolicy`,
  as the documentation already said.
- **The request DTOs.** No API receives them, and the CQRS commands carry their own records:
  `CreateCrewRequest`, `UpdateCrewRequest`, `CrewAgentRequest`, `CrewTaskRequest` and the settings
  records beside them (`Orkeon.Application.Crew.DTOs.CrewSettingsDto`, `CrewRetryConfigDto`,
  `CrewMemoryConfigDto`, `CrewCallbackConfigDto`), `CreateAgentRequest`, `UpdateAgentRequest`,
  `AgentSettingsDto`, `AgentMemorySettingsDto`, `CreateTaskRequest`, `UpdateTaskRequest`,
  `TaskSettingsDto`, `TaskRetryConfigDto`, `LlmConfigDto`; their mapping methods
  (`CrewMapper.FromCreateRequest`, `CrewMapper.UpdateFromRequest` — which ignored `Process` —,
  `AgentMapper.CreateFromRequest`, `TaskMapper.CreateFromRequest`); and the DTO enums `ProcessType`,
  `CrewStatus`, `TaskStatus` and `TaskPriority` (the read DTOs carry those as strings). One
  `ProcessType` remains: the value object.
- **Per-agent planning.** `IAgentExecutionService.PlanTaskExecutionAsync` and
  `IExecutionOrchestrator.PlanExecutionAsync` (with `TaskExecutionPlan` and `PlannedStep`) asked a stub
  planner for the same fixed plan, and nothing asked them: the planner (`IAgentPlanner`,
  `AgentPlannerService`, `TaskPlan`, `PlanStep`, `PlanFeedback`, `PlanValidationResult`, and the
  Application `PlanningContext`, `PlanningStep`, `PlanningResult`) is gone, and so are the second
  `ExecutionPlan` (`Orkeon.Domain.SharedKernel.ValueObjects`, with `ExecutionStep` and
  `ExecutionStrategy`) and `ExecutionPlanDto` (`TaskDto.ExecutionPlan`, never filled). A crew's
  `planning: true` is the orchestrator's (GAP-31). `ExecutionOrchestrator`'s constructors lose their
  `planner` parameter.
- **Other dead surfaces.** `OrkeonApplicationOptions.MaxShortTermMemoryItems`, `EnablePersistence`,
  `EnableRAG`, `MemoryDatabasePath` and `DefaultMemoryProvider` (copied, never read — the REPL's
  `appsettings.json` loses its `Orkeon:DefaultMemoryProvider` key); `ShortTermMemory`, `LongTermMemory`,
  `EntityMemory` and `ContextualMemory` (`Orkeon.Infrastructure.Memory`: the memory service has its
  own); `StateTransitionManager`, which described crew transitions the aggregate does not follow;
  `Orkeon.Application.Validation.CrewValidator` (a crew is validated at load by
  `CrewDefinitionValidator`); `CrewConfigurationMapper.ToDomainCrew`, which re-created the agents under
  new ids and never set the manager (the loaders build crews through `CrewFactory`); `Agent.Restore`
  and `AgentSnapshot`, which lost the agent's `LlmConfig`; `Agent.ValidateForExecution` ("Agent has no
  tools assigned" — an agent without tools is valid) with `ExecutionValidationResult`,
  `IExecutionOrchestrator.ValidateExecutionAsync` and `IAgentExecutionService.CanExecuteTaskAsync`;
  `IExecutionOrchestrator.MapExecutionContext` with `SimpleTaskExecutionContext` and `AgentMemory`;
  `ExecutionStatus.Cancelled` (no run reached it: a cancelled run ends `Failed`, GAP-32; the other
  values keep their numbers); `ExecutionConfig.EnableAsyncExecution`; and the constants and ids only
  these used (`MemoryDefaults.DefaultMaxShortTermItems`, `PathDefaults.DefaultMemoryDatabasePath`,
  `PlanningDefaults` of `Orkeon.Application.Constants.Execution`, three `StatusDefaults` plan statuses,
  `MemoryId`, `TaskPlanId`, `PlanStepId`).
- **A crew's request rate is exported under its own key.** `CrewConfigurationMapper.ToConfiguration`
  wrote `Crew.MaxRpm` into `ExecutionConfig.MaxConcurrentTasks`, a task concurrency nothing reads (and
  `ToDomainCrew` read it back as the request rate). The export writes `ExecutionConfig.MaxRPM`, and
  `MaxConcurrentTasks` is removed: no mode bounds how many tasks run at once.

Documented in [Default behaviors](docs/getting-started/default-behaviors.md),
[Bootstrap](docs/getting-started/bootstrap.md), [State machine](docs/orchestration/fsm.md) and
[Limits](docs/reference/limitations.md).

Breaking: every type and member listed above is removed; `GuardianPolicyEngine.GetPolicy(crewId,
agentId)` is the `Policy` property; `ExecutionOrchestrator`'s constructors no longer take a planner;
`ExecutionConfig.Create` no longer takes `maxConcurrentTasks` or `enableAsyncExecution`;
`IAgentExecutionService` keeps its two `ExecuteTaskAsync` overloads and `IExecutionOrchestrator` its
`ExecuteTaskCoreAsync` alone.

Migration: run a tool through `IToolInvocationPipeline.InvokeAsync` (registered by
`AddOrkeonApplication()`), never `tool.CallAsync` behind an `AIFunction`; read the Guardian policy from
`GuardianPolicyEngine.Policy` and set it per host (`Orkeon:Guardian:DefaultPolicy`); build crews from
YAML or `.ork.ts` through the loaders, or in C# with `CrewBuilder`/`AgentBuilder`/`CrewTaskBuilder`,
instead of the request DTOs and `ToDomainCrew`; drop the planner argument of `new ExecutionOrchestrator(…)`
and any `IAgentPlanner` registration; read a crew's exported request rate from `ExecutionConfig.MaxRPM`.

### Added — `orkeon-host` exposes its crews to other agents over A2A: one skill per crew, and a task is a run of that crew **[breaking]**

An A2A peer could not reach a crew `orkeon-host` hosts: no shipped binary called `AddOrkeonA2A`, and a C#
host that did served a router running agents found by id in a directory the daemon never fills — it loads
a fresh crew for every message — so its card had no skill (GAP-23).

- **One skill per exposed crew.** `Orkeon:Host:A2A` — `Enabled` (off by default), `Host`
  (`http://localhost`), `Port` (5002) and `Crews`, the crews other agents may run (none by default:
  exposing is a choice per crew, like a chat route) — turns the A2A server on. The card lists one skill
  per exposed crew — its id and name the crew's `Name`, its description the crew's new `Description`
  key —, built from the configuration without loading a crew; a crew left out is absent from it.
- **A task is a run of the crew**, exactly what a chat message is: `POST /a2a/tasks/send` (or
  `sendSubscribe`) on a published id — compared exactly — runs that crew through `CrewRunner` on the
  task's `input`, its `metadata` as the run's variables: under the crew's mounts, inside its
  `MaxConcurrentRuns` — chat conversations and A2A tasks counted together, a task over the bound
  answering `Failed`, never queued —, under `RunTimeout`, logged with its origin `a2a:<task id>`.
  `Completed` carries the crew's answer; a failed run answers `Failed` with the sentence a chat thread
  gets (the run id; the detail stays in the host log); `DELETE /a2a/tasks/{id}` stops the run
  (`Cancelled`); a skill id the card does not publish answers `Failed`, naming the published ones.
- **Refused at start (exit 78)**: an enabled section that exposes no crew, or a crew
  `Orkeon:Host:Crews` does not declare; a malformed `Host` or `Port`; a listener beyond the loopback
  while `A2A:Security` declares neither an authentication scheme nor mutual TLS; and the C# hosts'
  `A2A:EnableServer`, `A2A:Host` and `A2A:Port`, which the daemon does not read. A server refusing its
  `A2A:Security` (a scheme without its validator) is a refused configuration too, not a crash to
  restart on. The rest of the `A2A` section — the card's identity, `A2A:Security` and its bearer
  validators — applies as written. The server starts after the MCP servers are connected and before
  the chat channel, so it stops after the drain: a run in flight still answers the peer that asked.
- **The card publishes what the router answers.** `IA2ATaskRouter` gains `GetSkillsAsync`, and
  `A2AServer` builds the card from it rather than from the agent directory: a host that routes
  differently — the daemon routes crews — publishes exactly the ids its router compares.
  `A2ATaskRouter` lists one skill per available agent, as the card did. `AddOrkeonA2A` registers its
  agent router — and the process-wide agent directory that router reads — only when no router is
  registered yet: a host with its own router keeps its own per-scope `IAgentRepository` (the daemon's
  runs would otherwise have shared their agents with each other, one entry per run, forever). A
  wildcard listener (`http://+`) no longer makes the card endpoint throw: the card advertises the
  address the peer reached it at.

Documented in [Service host](docs/architecture/service-host.md#5-other-agents-a2a),
[A2A conformance](docs/reference/a2a-conformance.md#activation), [Configuration](docs/reference/configuration.md),
[Opt-in subsystems](docs/reference/opt-in-subsystems.md) and [Limitations](docs/reference/limitations.md); the
[service-host example](https://github.com/Orkeon/orkeon/blob/main/examples/service-host/README.md) exposes its crew on the loopback.

Breaking: `IA2ATaskRouter` declares `GetSkillsAsync`; the two `A2AServer` constructors no longer take an
`IServiceScopeFactory`; after a router of the host's, `AddOrkeonA2A` no longer registers the agent
directory (`IAgentRegistrationStore`) nor swaps the host's `IAgentRepository` for
`SharedStoreAgentRepository`; `orkeon-host` refuses `A2A:EnableServer`, `A2A:Host` and `A2A:Port`,
which it ignored.

Migration: implement `GetSkillsAsync` on a router of your own — the skills its `RouteTaskAsync` answers,
keyed by the id it compares; drop the scope factory from `new A2AServer(...)`; a host router that reads
the agents other scopes register adds `IAgentRegistrationStore` and `SharedStoreAgentRepository` itself;
in `orkeon-host`'s settings, write the listener under `Orkeon:Host:A2A`.

### Added — `orkeon mcp serve` serves the host's tools to an MCP client, each call through the guard, and `MCP:EnableServer` is refused **[breaking]**

Orkeon's MCP server was reachable from C# alone (GAP-24): `MCP:EnableServer` registered `McpServer`,
no binary ever resolved it, and a user who wrote the key got nothing; and a server a C# host did start
called each tool itself, past the guard every agent turn crosses.

- **A verb serves.** `orkeon mcp serve [--settings <file>] [--tools a,b,…]` serves over stdio, the
  transport by which MCP clients — Claude Desktop, editors — launch a local server: one JSON-RPC
  message per line in on stdin, the answers out on stdout, until the client closes stdin. It builds
  the host `orkeon run --list-tools` reports — the settings resolved from the current directory, their
  mounts, the built-in tools, the MCP servers they declare, connected first — and serves its registry,
  every tool but `human_input`, which answers for the operator of a run (an MCP client has a human of
  its own). `--tools` serves the named tools only, and a name the host does not have refuses the start.
  Stdout carries the protocol and nothing else: logs, warnings, `--help` and usage errors go to stderr.
  `--list-tools` and the verb build their host with one method, which answers a setting the host
  refuses, or a settings mount whose folder does not exist, with one `ERROR:` line and exit `1` —
  `--list-tools` ended there on an unhandled exception and a core dump.
- **Each call is guarded.** `McpServer` takes the `IToolInvocationPipeline` and calls every tool
  through it, under the caller `mcp`: the guardian's tool phase, the call, the one truncation rule, the
  result sanitizer and a `ToolExecution` audit event. The client reads what a crew agent's model reads:
  a blocked call answers `isError: true` with `Error: Blocked by Guardian (ToolExecution): …` and never
  reaches the tool, a result arrives truncated and tagged as data, a failure reads `Error: …`.
  `RunStdioAsync` no longer disposes the process's console streams when the client leaves.
- **The verb is the switch.** `MCP:EnableServer` and `McpOptions.EnableServer` are removed: `AddOrkeonMcp`
  registers the client only, and `AddOrkeonMcpServer(configuration)` registers the server, its
  `McpServerOptions` bound from `MCP:Server` (`Name`, `Version`). A section that still carries the key,
  `true` or `false`, is refused at startup — by both extensions and by every runner built on
  `RunnerHost`, servers declared or not — with a message naming `orkeon mcp serve`.

Documented in [MCP integration](docs/architecture/mcp.md) — which no longer says that a crew agent
cannot use an MCP tool —, [CLI](docs/reference/cli.md), [Configuration](docs/reference/configuration.md),
[Security](docs/architecture/security.md), [Hosting](docs/reference/hosting.md),
[Experimental APIs](docs/reference/experimental-apis.md) and [Limitations](docs/reference/limitations.md).

Breaking: a settings file or configuration that carries `MCP:EnableServer` no longer starts;
`McpOptions.EnableServer` is removed; `AddOrkeonMcp` no longer registers `McpServer`; both `McpServer`
constructors take an `IToolInvocationPipeline` after the registry; a failed `tools/call` reads
`Error: <reason>`.

Migration: remove `MCP:EnableServer` from the settings and serve the tools with `orkeon mcp serve`; in a
C# host, call `AddOrkeonMcpServer(configuration)` next to `AddOrkeonInfrastructure()` and
`AddOrkeonApplication()`, then resolve `McpServer` and run `RunStdioAsync()`; a `McpServer` built by
hand takes the container's `IToolInvocationPipeline`, or `ToolInvocationPipeline.Unguarded` for the
bare call and truncation.

### Fixed — an agent backed by a Microsoft Agent Framework agent answers through it, counted once, and is refused the Orkeon tools it could never call **[breaking]**

`WithAgentFrameworkAgent(agent)` set a field nothing read (GAP-34):

- **The MAF agent answers.** `AgentBuilder.WithLlm` — which `WithAgentFrameworkAgent` calls — set
  `Agent.FunctionCallingLlm`, and no runtime path read it: the agent's tasks ran on the host's default
  model, or its profile, with its Orkeon tools, and the MAF agent heard nothing, although ADR-010, the
  README and the reference promised that every prompt of the agent is a run of the MAF agent. The
  field is `Agent.Llm` (`AgentCreateOptions.Llm`, `AgentSnapshot.Llm`, the `llm` parameter of
  `Agent.Create`) — the provider the agent's turns run on, CrewAI's `llm` given as an object — and the
  run honours it: the orchestrator builds a client over it once per provider instance
  (`ILlmProfileRegistry.ForProvider`, named `provider:<name>`), metered as the agent's work, and runs
  on it the agent's turns, their correction round and its ballot. The order is GAP-17's, the provider
  in the agent's place: the profile a task's `llm_override` names (`default` included), else the
  agent's own provider, else its profile, else the default. An orchestrator built by hand, without the
  host's profile registry, fails such a task naming the agent. A hierarchical manager agent carrying
  its own provider assigns and reviews on it (`ManagerLlmResolver`, after `Crew.ManagerLlm`;
  `provider:<name>` in the run's log and the export).
- **Its own provider or a host profile, not both.** `Agent.Create` and `AgentBuilder.Build()` refuse an
  own provider with an `LlmConfig` that names a profile, naming both.
- **No Orkeon tool on an agent that runs its own.** A MAF agent calls the tools it carries: Orkeon's
  were listed in its prompt, and a `[TOOL_CALL]` it wrote would have become its answer.
  `LlmProviderCapabilities.RunsOwnTools`, declared by `AIAgentLlmProvider` and relayed by the meter,
  makes `Agent.Create`, `AgentBuilder.Build()`, `AddTool` and `UpdateConfiguration(allowDelegation:
  true)` refuse tools and delegation on such an agent; a task that falls on such a provider — the
  agent's own, a profile's (`AddOrkeonLlmProfile`) or the host's default — with tools to hold (its
  `tools:`, `human_input`, the delegation tools of an agent that allows delegation — a YAML agent does
  unless it writes `allowDelegation: false`) fails before any call. The message names the tools and the
  two remedies: the tool on the MAF agent itself, or the MAF agent as a tool of an Orkeon agent
  (`WithAgentFrameworkTool`) — and, for the delegation tools, delegation switched off.
- **One MAF session, each message once, one call at a time.** The agent loop sends the whole
  conversation every turn, and the MAF session already held it: a resumed turn showed the model the
  conversation twice, and two tasks of the same agent wrote to the session at once. `AIAgentLlmProvider`
  now sends a call that extends the conversation the session holds only what is new, any other call — a
  new task, the session keeping the previous one — whole, and one call at a time: the tasks of one MAF
  agent run one after another, even in a parallel wave.
- **Counted once.** A provider answering through another metered provider — a MAF agent built over
  Orkeon's own model, as the example builds its reviewer — would have been counted twice.
  `MeteredLlmProvider` counts a call once, by the meter nearest the model: an outer call during which an
  inner meter counted reports nothing, so the event names the real provider and model; a MAF agent on a
  client Orkeon does not meter is counted under `agent-framework:<name>`. The metering guard declares
  `LlmProfileRegistry` an entrance of the metered path, and the bridge's exemption says where it is metered.
- **What the bridge does not send, it says.** Each option a call's `LlmConfig` declares (model,
  temperature, max tokens, response format, thinking…) is a structured warning of `AIAgentLlmProvider` —
  event 110, the HTTP providers' wording —, once per crew run and option, through the logger
  `WithAgentFrameworkAgent(agent, logger)` takes, else the logger factory the MAF agent exposes.
- `examples/interop/agent-framework/` has a third section: the MAF reviewer answering for an Orkeon
  auditor, metered once under the configured model.

Streaming is unchanged: a streamed turn receives the MAF answer as one fragment.

Documented in [Microsoft Agent Framework interop](docs/reference/agent-framework-interop.md),
[ADR-010](docs/adr/ADR-010-agent-framework-interop.md) (amendment of 2026-10-03),
[Configuration](docs/reference/configuration.md#named-profiles-llmprofiles),
[Process types](docs/orchestration/process-types.md) and
[YAML and builders](docs/getting-started/yaml-and-builders.md).

Breaking: `Agent.FunctionCallingLlm`, `AgentCreateOptions.FunctionCallingLlm` and
`AgentSnapshot.FunctionCallingLlm` are renamed `Llm`, and the `functionCallingLlm` parameter of
`Agent.Create` is `llm`; `ILlmProfileRegistry` gains `ForProvider`; `AIAgentLlmProvider` and
`WithAgentFrameworkAgent` take an optional logger; an agent with its own provider and a host profile,
and an agent whose own provider runs its own tools with tools or delegation, no longer build; a task
holding tools fails on a provider that runs its own.

Migration: rename `FunctionCallingLlm` to `Llm`; an `ILlmProfileRegistry` of your own implements
`ForProvider` (build and meter the provider's client once per instance, as `LlmProfileRegistry` does);
keep the agent's provider or its profile, not both; give a MAF agent's tool to the MAF agent itself, or
hand the MAF agent to an Orkeon agent as a tool (`WithAgentFrameworkTool`).

### Fixed — outside Studio, a run finds the key of its setting: the settings file names the variable, never the key **[breaking]**

Studio remembered a setting's key under the provider's variable (`ZAI_API_KEY`, `DEEPSEEK_API_KEY`),
in the user scope (STUDIO-44), and the runtime never read those names: it read `Llm:ApiKey` —
`ORKEON_Llm__ApiKey` — and, for a profile, `ORKEON_Llm__Profiles__<id>__ApiKey`, which only a Studio
launch set. A terminal `orkeon run`, an `orkeon-host` started by the user, `orkeon-repl` and above all
a team Studio scheduled (STUDIO-27) ran without a key: every call answered "API key is required", and
the run exited 2 (STUDIO-49).

- **The file names the variable.** Every section of the `Llm` shape — `Llm` and each
  `Llm:Profiles:<id>` — gains `ApiKeyEnvVar`: the name of the environment variable that holds its
  key, never the key. `LlmSettings` reads it when the configuration resolves no `ApiKey` (the file's,
  `ORKEON_Llm__ApiKey`, `ORKEON_Llm__Profiles__<id>__ApiKey`, a Studio launch's — they still win, so
  no existing installation changes): in the process environment, then, on Windows, in the user scope
  where Studio remembers keys — read, never copied into the process, so what a run starts inherits no
  key it did not have; a user scope that cannot be read counts as an absent variable. A reference
  that cannot be a variable's name (an `=`, a space, a line break) refuses the host start, naming its
  path and never its value. `ConfigurationKeys.LlmApiKeyEnvVar` (`Orkeon.Constants.Configuration`,
  ADR-009) spells it for the runtime, `doctor`, `init` and Studio.
- **Where each key came from, never the key.** The runner host's `LLM resolved:` line ends with
  `apiKey=<source>` — `from configuration (Llm:ApiKey)`, `from the variable named by Llm:ApiKeyEnvVar
  (process environment)` or `(user environment)`, `none` —, one `LLM profile <id>: apiKey=…` line
  follows per profile, and a reference to a variable set nowhere is warned about once per host build,
  by its configuration path, on the logger and on stderr (`OperatorMessages.LlmApiKeyReferenceUnresolved`).
  No message repeats the value of `ApiKeyEnvVar`: it may be a key pasted in the wrong field.
  `LlmSettings.DescribeApiKey` and `LlmSettings.UnresolvedApiKeyReferences` are the public surface.
- **Blank reads as absent.** A value left blank in a section of the `Llm` shape — `Thinking:Effort`
  included — reads as absent, and a section whose every value is blank configures no default: the
  echo provider, with the WIN-01 warning.
- **Studio writes the reference, and the default whole.** Each `Llm:Profiles:<id>` entry Studio owns
  names the setting's variable (none for a setting without a key). The election writes the elected
  setting into `Llm` whole — `BaseUrl`, `Model`, `ApiKeyEnvVar`, `Temperature`, `TimeoutSeconds`,
  `MaxTokens`, `Thinking`, a field it leaves unset removing its key — where it wrote the model and the
  endpoint alone: a team scheduled on DeepSeek ran on the engine's 30 s, not on the 600 s Studio
  pre-fills. `ApiKey`, `MaxRetries`, `Grammar`, `AvailableModels` and `Profiles` stay. A team launched
  on another setting than the default lays every modeled `ORKEON_Llm__*` field over its child, its
  value or blank, `ORKEON_Llm__ApiKeyEnvVar` included: a team on Z.AI whose key is not remembered fails
  without a key instead of sending the default's DeepSeek key to Z.AI. The editor's expert line names
  the setting's own variable (`ModelProfile.HostKeyVariable` is gone).
- **« Other OpenAI-compatible » gets a variable of its own.** A setting created from that card keeps
  its key in `ORKEON_CUSTOM_LLM_API_KEY` (`LlmPresets.CustomApiKeyEnv`). It used `ORKEON_Llm__ApiKey`,
  the runtime's own key of the default, which — remembered in the user scope — became the default key
  of every run of the user and went to another vendor's address. A setting created before keeps its
  variable, and its card says, in expert mode, what that variable is and how to take it back; nothing
  migrates on its own.
- **`orkeon init` and `orkeon-studio-config` write it too.** `orkeon init --api-key-env <name>` writes
  `Llm:ApiKeyEnvVar: <name>` — nothing for `ORKEON_Llm__ApiKey`, which the runtime reads natively —
  where it printed that the name was "only used by the probes". The TUI's presets write it, a preset
  without a key removes it, its LLM form edits it, and its read-only list of profiles shows each one's
  variable (`key: ZAI_API_KEY`).
- **The REPL reads what the runners read.** `orkeon-repl` composed the default .NET host alone:
  neither `ORKEON_Llm__ApiKey` — it read `Llm__ApiKey` — nor the global file `orkeon init` writes
  reached it. It now reads the `--settings` files — else the global file, which a named file replaces
  as for `orkeon run` —, then the `ORKEON_` variables, prefix removed, before the command line, and
  logs once where each key comes from, with the runner's warning. `Orkeon.ConsoleApp` now references
  `Orkeon.Hosting` for that path (`RunnerSettings.GetGlobalSettingsPath`, code that stays with the
  runners rather than a copy): the `orkeon-repl` tool package carries four more assemblies —
  `Orkeon.Hosting`, `Orkeon.Tools.EventHub`, `Orkeon.Constants.Cli` and `CommandLineParser` (MIT),
  all of which the `orkeon` tool already ships.
- **`orkeon doctor` reads the key a run would.** `llm-config` says where the default's key comes from,
  warns when its reference resolves nothing and fails when the section cannot be read; a new
  `llm-profiles` row covers the profiles, one `llm-profile-key` row warns per profile whose reference
  resolves nothing, and `llm-reachability` queries the catalogue with the resolved key.
- The ten `examples/appsettings/*.local.json.example` templates name their variable
  (`"ApiKeyEnvVar": "DEEPSEEK_API_KEY"`) instead of carrying `"ApiKey": "${DEEPSEEK_API_KEY}"`, which
  nothing ever expanded.

Documented in [Configuration](docs/reference/configuration.md#the-api-key-apikey-apikeyenvvar),
[API keys: the variable per provider](docs/reference/llm-providers-comparison.md#api-keys-the-variable-per-provider),
[CLI](docs/reference/cli.md), [Orkeon Studio](docs/architecture/studio.md),
[The service host](docs/architecture/service-host.md),
[Security](docs/architecture/security.md#llm-keys-the-settings-file-names-the-variable) and
[Limitations](docs/reference/limitations.md).

Breaking: an `ApiKey` written as a `${NAME}` placeholder — the old templates' shape, which went out as
the key and came back a 401 — refuses the start, naming the fix, and so does an `ApiKeyEnvVar` that
cannot be a variable's name; a blank value in a section of the `Llm` shape reads as absent, and a
section whose every value is blank configures no default; a Studio launch on another setting than the
default blanks every default field its setting does not set; `orkeon doctor` reports ten checks, plus
one row per unresolved profile reference; new « Other OpenAI-compatible » settings keep their key in
`ORKEON_CUSTOM_LLM_API_KEY`.

Migration: replace `"ApiKey": "${NAME}"` with `"ApiKeyEnvVar": "NAME"`. A file Studio wrote before
heals at one gesture: edit then save a model setting — its entries, and the `Llm` section of the
elected one, are written again with their references. A « Compatible OpenAI » setting that keeps
`ORKEON_Llm__ApiKey` is created again from its card — its key then goes to
`ORKEON_CUSTOM_LLM_API_KEY` — and `ORKEON_Llm__ApiKey` removed from the user environment. On Linux and
macOS, a scheduled team reads the variable from `~/.config/environment.d/*.conf` (the systemd user
manager) or from a line at the top of the crontab — never from the shell profile.

### Fixed — a crew's manager applies where its mode uses one and is refused elsewhere, the planner's provider is metered, and a reference that names nothing fails the load **[breaking]**

What a crew wrote about its manager, its planner and its tasks' references was read, then dropped
without a word (GAP-33):

- **A manager where the mode uses one.** A manager agent means something in two modes: Hierarchical —
  it assigns each task and reviews its output — and Consensual — it arbitrates the vote under the
  `ManagerDecision` fallback. On a `sequential`, `parallel`, `graph` or `autonomous` crew,
  `managerAgent:` (`.manager(agent)` in `.ork.ts`; `.WithManager(agent)`, `.WithManagerId(id)` or
  `.Hierarchical(agent)` in C#) was kept, then ignored: the agent ran tasks like any other. In C#,
  `.WithManagerLlm(provider)` on a `sequential`, `parallel`, `graph` or `consensual` crew was never
  called. Both are refused now — by the YAML loader, the `.ork.ts` adapter, `CrewDefinitionValidator`,
  `CrewBuilder.Build()` and `Crew.Create(CrewCreateOptions)` — with a message that names the key and
  the mode and says what to do: remove the manager, or use `hierarchical` or `consensual` (an
  autonomous crew's manager is an LLM: the default profile, or `WithManagerLlm` in C#). Two predicates
  of the `ProcessType` value object carry the rule, `AcceptsManagerAgent` and `AcceptsManagerLlm`, and
  the domain keeps it: `Crew.SetManagerAgent` accepts a consensual crew (it refused every mode but
  Hierarchical); `ChangeProcessType` sets the agent passed in a mode with a manager agent, drops the
  crew's manager — still a member, hence a worker — when it turns to a mode without one, refuses an
  agent passed with such a mode, and refuses to turn a crew with a manager LLM to a mode that never
  calls it; `Validate` checks that a consensual crew's manager is a member, and a consensual crew
  whose manager leaves has no arbiter left. Examples 86 and 100 declared a manager their mode
  ignored: it is gone, and the agent keeps its tasks.
- **A manager or a task reference that names nothing fails the load.** A `managerAgent:` that named no
  agent — a typo — was erased: a hierarchical crew was told it "requires a manager agent", a
  consensual crew lost its arbiter. A task's `agent:` or dependency that named nothing was erased the
  same way: the task ran on another agent, or without waiting for what it cited. Each fails the YAML
  load now, naming what was written and listing the crew's agents or tasks, every faulty reference
  at once. In `.ork.ts`, a task whose `.agent(...)` or `.withContext(...)` is not in the crew, and a
  `.manager(...)` another crew holds, fail the adaptation the same way. The forge checks its plan's
  manager — its key and the mode — and each task's agent and dependencies before it compiles
  (`ForgeBlueprint.Validate`): the loader's refusals come back to the assistant as repairable errors
  (`blueprint_submit`, the validate stage, an edited plan) instead of ending the session, and its
  `manager` field says "hierarchical or consensual".
- **A consensual manager says when it decides nothing.** The manager of a consensual crew neither
  answers nor votes: it arbitrates only when the host's fallback is `ManagerDecision`, and the
  default is `AcceptBestScore`. Under any other fallback the run now warns at its start, naming the
  agent and the host's fallback, with the two remedies — `Orkeon:Consensus:FallbackStrategy:
  ManagerDecision`, or no `managerAgent:` so that the agent answers and votes. Example 17's README
  says when its moderator decides.
- **The planner's provider is metered.** The provider C# gives the planner
  (`CrewBuilder.WithPlanningLlm`) was called as it was: its tokens reached neither `cost.updated` nor
  `run.finished`, although the documentation said "metered as `operation: planning`".
  `SequentialCrewOrchestrator` now meters it where the run resolves it, under the planning
  attribution — a provider already metered is read once per call —, through a new optional last
  constructor parameter, `ILlmUsageSink? usageSink`, which the container fills; the metering guard
  declares it the fourth entrance of the metered path. A planning provider without `.Planning()` was
  lost without a word: `Crew.Create` refuses it — from `CrewBuilder.Build()` and `CrewCreateOptions`
  alike —, and `UpdateConfiguration(planning: false)` refuses to switch off a crew that carries one.

Documented in [Process types](docs/orchestration/process-types.md), [Autonomous](docs/orchestration/autonomous.md),
[YAML schema](docs/architecture/yaml-schema.md), [YAML and builders](docs/getting-started/yaml-and-builders.md),
[Scripting DSL](docs/reference/scripting-dsl.md), [Configuration](docs/reference/configuration.md) and
[Limitations](docs/reference/limitations.md).

Breaking: a manager agent on a sequential, parallel, graph or autonomous crew, a manager LLM on a
sequential, parallel, graph or consensual crew, a `managerAgent:`, task `agent:` or dependency that
names nothing, a `.ork.ts` task whose agent or context task is not in its crew, and
`WithPlanningLlm` without `Planning` no longer load or build; `Crew.SetManagerAgent`,
`Crew.ChangeProcessType` and `Crew.UpdateConfiguration` follow the same rules.

Migration: remove a manager the mode does not use — the agent keeps its tasks — or turn the crew
`hierarchical` or `consensual`; fix the key a reference names — the message lists the crew's agents
or tasks; add the agent or task a `.ork.ts` reference cites to the crew (`withAgent`, `withTask`);
call `.Planning()` on a crew given `WithPlanningLlm`, or remove the provider.

### Added — Studio's model settings are the host's LLM profiles: a crew names one with `profile:`

Since named profiles (GAP-17), a crew can write `llm: { profile: claude }` — provided the host's
settings define `Llm:Profiles:claude`, which nothing in Studio wrote: a Studio user who created a
« Claude » setting and named it in a crew saw the load fail, the error listing `default` alone
(STUDIO-48).

- **Each setting is a host profile.** Every model setting that names a provider is written into the
  settings file as `Llm:Profiles:<id>` — its id the name through the teams' folder-name rule
  (« Claude » → `claude`, « Z.AI » → `z-ai`), its fields (`BaseUrl`, `Model`, `Temperature`,
  `TimeoutSeconds`, `MaxTokens`, `Thinking`), never its key — and the entry follows the setting
  through an edit, a rename (the keys Studio does not model travel with it) and a deletion. The card
  and the editor show what a crew writes (`profile: claude`); the editor refuses `default`, a name
  another setting already answers to and one an entry written by hand holds, warns that a renamed
  setting's old name stops loading, and names, in expert mode, the variable a terminal run reads the
  key from. A setting without a model, or whose name keeps no ASCII letter or digit, is offered to no
  crew.
- **Keys through the environment.** Every launch from Studio — a run, a trial, the creation
  assistant — carries every setting as `ORKEON_Llm__Profiles__<id>__*`, the key resolved through the
  key store (STUDIO-44), so it never depends on the file having been saved; `orkeon run` in a
  terminal reads the same file and needs only `ORKEON_Llm__Profiles__<id>__ApiKey`.
- **Entries written by hand stay as written.** An entry of `Llm:Profiles` no setting owns is listed
  read-only under the settings, never rewritten nor removed. Choosing « no model » for the default —
  in Studio or in `orkeon-studio-config` — removed the whole `Llm` section, profiles included; it now
  clears the default provider and keeps them, and a section holding profiles alone raises the WIN-01
  warning, as the runtime reads it.
- **The RAG's profile on the same screen.** An expert card of the AI-model tab chooses
  `Orkeon:Rag:LlmProfile` (GAP-19) among the profiles; it follows its setting through a rename and
  falls back to the default when the setting is deleted. The validation warns about a RAG profile the
  file does not define (`STUDIO-RAG-LLM-PROFILE`), in the five languages.
- `orkeon-studio-config` shows `Llm:Profiles` and the RAG's profile, read-only.
- `Orkeon.Constants.Llm` gains `LlmProfileNames.Default` (ADR-009): the reserved name the runtime
  refuses for a profile, which Studio refuses too; `LlmProfiles.Default` reads it.

Documented in [Configuration](docs/reference/configuration.md#studio-writes-this-section),
[Orkeon Studio](docs/architecture/studio.md) and
[YAML and builders](docs/getting-started/yaml-and-builders.md#one-provider-per-agent-profiles).

### Fixed — a crew's manager runs on the LLM the crew gives it, the RAG subsystem on the profile the host names, a `.ork.ts` task changes profile, and an address is no glob **[breaking]**

Since named profiles, each agent ran on its own; the rest did not follow (GAP-19):

- **The manager runs on the LLM the crew gives it.** A hierarchical crew whose `managerAgent` carried
  `llm: { profile: claude }` had its tasks assigned and reviewed on the host's default profile: the
  manager agent's `llm:` block was read, set on the agent and ignored. In C#,
  `CrewBuilder.WithManagerLlm(provider)` was validated — it spares a crew its manager agent — then never
  read; worse, `Crew.AddAgent` made the first agent of such a crew its manager, taking it out of the
  workers. The manager now runs, resolved once per run by `ManagerLlmResolver`, on `Crew.ManagerLlm`
  when C# set one (CrewAI's `manager_llm`, metered like a provider the host registers), else on its
  manager agent's `llm:` block — profile and model, a model left unset being the profile's own —, else
  on the default profile, and the run logs which (`provider:<name>`, `profile:<name>`). A crew given a
  manager LLM needs no manager agent: every agent works, and `Crew.Validate`, `StartExecution` and
  `ChangeProcessType` accept it. An autonomous crew hands its tasks out on `Crew.ManagerLlm` too. The
  export's `ExecutorSettings["ManagerLlm"]` names that LLM (it carried a CLR type name), the unused
  `ExecutionConfig.ManagerLlm` string is gone, and a YAML or `.ork.ts` hierarchical crew without
  `managerAgent` fails its load naming the key — it was announced to "use the first agent as manager",
  then refused by the builder, asking for a manager LLM a configuration cannot carry.
- **The RAG subsystem runs on `Orkeon:Rag:LlmProfile`.** Grounded generation, the query transformers
  (`multi-query`, `rag-fusion`, `hyde`), the listwise reranker, the corrective graph's evaluator and
  groundedness checker, the `llm` classifier and the evaluation judge all took the container's chat
  client — the default profile. They now call the host profile `Orkeon:Rag:LlmProfile` names
  (`RagOptions.LlmProfile`; unset, the default), resolved at the first RAG call that needs a model. A
  name the host does not offer — `orkeon-host`'s `Orkeon:Host:LlmProfiles` allow-list included —
  refuses the start of the runner hosts and the REPL, listing the known ones
  (`RagLlm.EnsureProfileIsKnown`); `orkeon rag eval --offline` ignores the key, its stub answering in
  place of every model.
- **A `.ork.ts` task changes profile like a YAML task.** `taskBuilder().withProfile(name)` sets the
  task's `llm_override` profile (alongside `withResponseFormat` when both are given); a name the host
  does not offer fails the load, listing the known ones. `task.d.ts` declares it.
- **An address is not a glob.** `SourceGlobExpander` took the `?` of a query string, or a `*`, in an
  `http(s)://` source for a wildcard: `rag_ingest` and `rag.ingest` walked the virtual file system for
  it and failed ("No mount for virtual path '/https:/…'"), and `orkeon rag ingest --source` turned it
  into `/workspace/https://…`. An absolute http(s) address now reaches the web loader as written, on
  every surface — the crew `rag:` block included, which shares the rule (`SourceGlobExpander.IsWebAddress`).

The planner and the Guardian stay on the default profile: they are host services, not crew roles
(`CrewBuilder.WithPlanningLlm` keeps the hand in C#).

Documented in [Configuration](docs/reference/configuration.md#named-profiles-llmprofiles),
[Process types](docs/orchestration/process-types.md), [Autonomous](docs/orchestration/autonomous.md),
[RAG pipeline](docs/architecture/rag-pipeline.md), [Scripting DSL](docs/reference/scripting-dsl.md),
[YAML and builders](docs/getting-started/yaml-and-builders.md), [YAML schema](docs/architecture/yaml-schema.md),
[Service host](docs/architecture/service-host.md), [CLI](docs/reference/cli.md) and
[LLM response format](docs/guides/llm-response-format.md).

Breaking: `IManagerAgent.AssignTaskAsync` and `ReviewOutputAsync` take the crew's `ManagerLlm`, and
`LlmBasedManager` has no model of its own (constructor `(ILogger)`); `HierarchicalProcessStrategy` and
`AutonomousProcessStrategy` take a `ManagerLlmResolver`; `IProcessStrategy.ExecuteHierarchicalAsync`
takes a nullable manager agent id; `ExecutionConfig.ManagerLlm` and the `managerLlm` parameter of
`ExecutionConfig.Create` are removed; a hierarchical configuration without `managerAgent` fails its
validation.

Migration: an `IManagerAgent` of your own sends its prompts to `llm.ChatClient`, on `llm.Model` when it
is set; a strategy built by hand takes `new ManagerLlmResolver(defaultChatClient, profiles, usageSink)` —
`AddOrkeonInfrastructure()` registers one; a crew that relied on its first agent managing names it with
`managerAgent:`; instead of `ExecutionConfig.ManagerLlm`, give the manager agent an `llm:` block, or a C#
crew `WithManagerLlm`.

### Fixed — what a crew writes is what runs: an unknown tool fails `act`, `CrewResult` declares what it serves, `rag:` sources expand, and a forge trial reads the folders and profiles it is given **[breaking]**

Four things a user wrote were ignored without a word, plus a fifth found since (GAP-27):

- **`ctx.llm.act` refuses an unknown tool.** A procedural `.ork.ts` agent whose `.tools([...])` names a
  tool the host does not offer — `web_serch` for `web_search` — ran `act` without that tool, silently,
  where the declarative shape fails its load on the same name (`StrictTools`). `act` now rejects, before
  any model call, with the declared `UnknownToolError` (new in `errors.d.ts`, planted as a global like
  the others: `agentName`, `toolNames`, `availableTools`); an `onError` handler reads it as
  `validation`, and a C# host calling `JsCrew.RunAsync` gets the typed `UnknownToolException`
  (`Orkeon.Scripting.Exceptions`). Names still match case-insensitively, and a body that never calls
  `act` is not concerned.
- **`CrewResult` declares what `crew.run()` serves.** The typings promised `output: TOut` and
  `artifacts: ReadonlyMap<string, unknown>`; the runtime served a string and a CLR dictionary that was
  always empty, so `result.artifacts.get("x")` compiled and threw. `CrewResult` is now
  `{ output: string; tasks: readonly TaskResult[] }`, `TaskResult.output` is `unknown`, the type
  parameters of `CrewResult`, `TaskResult` and `Crew.run` are gone, and so is `JsCrewResult.artifacts`.
  `TypingsRuntimeParityTests` pairs both shapes with their CLR types and compares each property's type,
  not only its name.
- **A crew's `rag:` sources mean what they say.** The schema documented "file globs or directories" and
  showed `sources: ["./data/catalogue/**/*.pdf"]` and `["./docs/procedures/"]`: both failed at
  ingestion ("No document loader can handle source"), the sources reaching the pipeline verbatim.
  `RagCollectionsBootstrapper` now expands a glob through `SourceGlobExpander`, as `rag_ingest`,
  `orkeon rag ingest`, `rag.ingest` and the eval harness do; a directory stands for every file below
  it; a path without a leading `/` resolves against the crew's folder (`RagCrewConfig.CrewDirectory`,
  recorded by the YAML loader — `/crew` under `orkeon run crew.yaml`); an `http(s)` address and a
  plain file reach the loaders as written, and a file two entries name is ingested once. A pattern or a
  directory that yields no file, a glob outside every mount and a relative source of a crew read from a
  string are load warnings naming the collection. Both examples of the schema page now ingest what they
  show, checked by a test that reads them from the page.
- **A forge folder outside the working directory is refused where the trial could not read it.** A
  JSONL client that confirmed, in a run trying the team in the same process, an input bound to a
  directory outside the working directory had it mounted for the trial and refused on every read: the
  path validator keeps the directories its host started with. In such a run `folders.confirmed` now
  refuses it — a recoverable `FORGE-FOLDERS-INVALID` saying to confirm the folders in a run that stops
  before its trial (`orkeon forge resume <slug> --dry`) and to try the team with
  `orkeon forge resume <slug>`, whose host mounts and allows them from its start. The validator is
  unchanged, and Studio, which confirms the folders in a `--dry` run, is not concerned.
- **A forge trial knows the host's LLM profiles.** It loaded a forged `.ork.ts` crew without them: a crew
  naming a host profile (`llm.profile("fast")`) failed its trial with "Known profiles: default." and ran
  once promoted. The trial now loads it with `LlmProfiles`, like `orkeon run` and the forge's assistant;
  a name the host does not define still fails, listing the ones it does.

Documented in [Scripting DSL](docs/reference/scripting-dsl.md),
[Write a crew in TypeScript](docs/guides/write-a-crew-in-typescript.md),
[YAML schema](docs/architecture/yaml-schema.md), [RAG pipeline](docs/architecture/rag-pipeline.md),
[CLI](docs/reference/cli.md#orkeon-forge) and
[Forge a team from a need](docs/getting-started/forge-a-team-from-a-need.md); the namespace entry of
[Known limitations](docs/reference/limitations.md) says how the forge keeps its trial clear of the
validator's refusal.

Breaking: `ctx.llm.act` rejects an agent whose `.tools([...])` names a tool the host does not offer;
`CrewResult` has no `artifacts` and no type parameter, `TaskResult` and `crew.run()` take none, and
`JsCrewResult.artifacts` is removed; `RagCollectionsBootstrapper` takes the `IFileSystemService` its
sources are expanded against.

Migration: correct or drop the names an `UnknownToolError` lists (`toolNames`; `availableTools` says
what the host offers); drop the type argument of `crew.run<T>()` and narrow `result.tasks[i].output`
yourself; remove any read of `result.artifacts` — it was always empty; a C# host that builds
`RagCollectionsBootstrapper` itself passes its `IFileSystemService`.

### Fixed — the streaming kickoff runs the crew and says what happens as it goes, a failed run fails the crew, and `KickoffForEachAsync` runs each input in turn **[breaking]**

`ICrewOrchestrationService.KickoffStreamingAsync` — the documented C# equivalent of CrewAI's
`stream=True` — did not run the crew: for each task id it ran a fake task whose description was the
GUID, on the crew's agents taken in turn, ignoring the declared agent, the dependencies, the mode, the
memory, the knowledge and the plan; the crew was never started nor ended; the stream said neither the
output nor whether the run succeeded; and its per-token service could call no tool through the shipped
chat client. A run whose task failed raised `CrewExecutionCompletedEvent` (`FailedTasks = 1`, the
execution `PartialSuccess`, the crew back to `Idle`) while `orkeon run` exited 2; a run that failed
before its strategy — a memory that cannot work, a plan whose provider failed — never reached the
execution hook, so `orkeon run --events` ended without its `error`; `--stream` never streamed a crew's
turns; and `KickoffForEachAsync` started every input at once on the same crew, all but the first
failing with "Crew is already executing" (GAP-32):

- **The streaming kickoff is the crew's run.** `KickoffStreamingAsync` runs exactly what `KickoffAsync`
  runs — one kickoff core: load, memory, validation, start, the strategy of the crew's mode (all six),
  the lifecycle of its tasks and agents, its memory, knowledge and plan, the terminal transition, the
  checkpoints and the domain events — and yields what happens while it runs.
- **`CrewExecutionEvent` speaks the run's wire vocabulary.** Its `Kind` is a `RunEventKinds` constant:
  `task.started` and `task.completed` for each task (task id, agent role; success, skip, duration,
  tokens, tool calls), `tool.called` and `tool.returned` for each tool call (the tool's name, success and
  duration — never an argument's value nor the result), `llm.delta` for the model's text as it arrives
  (with the task and agent of the call), and `run.finished`, always last, whose `Output` is the run's
  `CrewOutput` — after an `error` (`Code` `crew_failed` or `crew_cancelled`, new `RunEventErrorCodes`
  in `Orkeon.Constants.Protocol`, and the run's reason) when the run failed. A task's events come in
  order; the events of tasks that run at once interleave. A crew run from inside a task — a tool that
  kicks another crew off — never writes into its caller's stream (`CrewStreamScope`, one per run).
  **Leaving the stream cancels the run**, and the iterator waits for its end: the crew fails by
  cancellation, its running task is cancelled, its events are dispatched.
- **The model's text streams through the real agent loop.** The chat-client loop streams a turn when
  someone reads it — a streamed run, or a host's `ILlmDeltaSink` — and folds the updates into the turn
  it reads as before; otherwise the call stays buffered, byte for byte. The chat client adapter's
  streaming path is built like its buffered one — same messages, roles, tools and options — and reads
  the provider's chat stream: the text as it arrives, then one last update with the tool calls (native
  or of the text protocol), the provider's usage, the finish reason, the model, the vendor's cost and the
  reasoning to replay. A streamed turn calls its tools and is metered once, with the provider's own
  figures — it was flattened into one prompt, sent no tool, and counted as an estimate. The providers'
  streams now say what their buffered answers say: Anthropic assembles the `tool_use` blocks it
  streams; MiniMax splits its leading `<think>` block out of the content deltas
  (`LeadingReasoningTag`); and on the native protocol the OpenAI dialect and Anthropic give a streamed
  answer the body their buffered answer carries, so the text-protocol fallback reads a call the model
  wrote as text. The native and text loops (no `IChatClient`) stream every event but the text.
- **`orkeon run --stream` streams a crew's turns.** The run observer's delta sink now reaches the
  agent loop (`ExecutionOrchestrator.DeltaSink`, set by `AddOrkeonApplication` from the registered
  `ILlmDeltaSink`): a YAML crew, a crew directory and a declarative `.ork.ts` crew write `llm.delta`
  events, where only `ctx.llm.*` calls did. The observer registers the sink only under `--stream`, so a
  run with `--events` alone keeps every call buffered. The REPL's console sink
  (`Orkeon:Cli:ConsoleStreaming:Enabled`) renders the crew's turns too.
- **A failed run fails the crew.** A strategy that returns a failed output — the rule of every mode
  since GAP-03 — makes the orchestrator call `Crew.FailExecution` with the output's error (a readable
  sentence when it has none): `CrewExecutionFailedEvent` with that `Reason`, naming each failed or
  skipped task, and no `Exception`; the crew and its execution stay `Failed`. Only a run whose every task
  succeeded completes it — one terminal transition per run. A cancelled run is a failed run whose
  `Exception` is the `OperationCanceledException`, and a task the cancellation caught running is
  `Cancelled` (it was failed with "Operation was cancelled").
- **A task counts once, by its final outcome.** `CrewExecutionCompletedEvent.CompletedTasks` and
  `CrewExecution.CompletedTasks` count each task the run completed once: a graph task that failed and
  succeeded on its retry made a successful run report a failed task and a `PartialSuccess` execution.
  `CrewExecutionCompletedHandler` logs "N tasks".
- **A run that fails before its strategy reaches the execution hook**, once, from the orchestrator
  (which takes an optional `ICrewExecutionHook`): a memory that cannot work, a plan whose provider
  failed, a crew not found or refused by `ValidateCanKickoff`. `AUTO_SUMMARY.md` is written, `orkeon-host`
  hears it, and `orkeon run --events jsonl` says `error` (`crew_failed`, the cause) before `run.finished`
  — as [the run event bus](docs/architecture/run-event-bus.md) promised. A failure a strategy reported
  is never reported twice.
- **`KickoffForEachAsync` runs each input in sequence**, as its contract says and like CrewAI's
  `kickoff_for_each`: each input is a full run, the next one starts once the previous one has
  returned, and the results keep the inputs' order.
- **`StepNotifyingToolInvocationPipeline.Wrap` always wraps** — without callbacks it notifies the stream
  of a streamed run alone —, so an `ExecutionOrchestrator` built by hand streams its tool calls too.
- **The `Orkeon` package embeds `Orkeon.Constants.Protocol`**, which `Orkeon.Application` now
  references: the core closure is twelve assemblies.

Breaking: `CrewExecutionEvent` is a record with `Kind`, `Timestamp`, `TaskId`, `AgentRole`, `Text`,
`ToolName`, `Success`, `Duration`, `Skipped`, `SkipReason`, `Tokens`, `ToolCalls`, `Code`, `Message` and
`Output` (its `TaskDescription` and `Thought` are gone); `AgentThought` (with `ThoughtType`),
`IStreamingAgentExecutionService` and `StreamingAgentExecutionService` are removed, and
`AddOrkeonInfrastructure()` registers no streaming service; `SequentialCrewOrchestrator` loses its
`streamingService` parameter and gains an optional `ICrewExecutionHook executionHook`;
`Crew.CompleteExecution(int completedTasks)` and the internal `CrewExecution.Complete(int)` lose their
`failedTasks` parameter; `CrewExecutionCompletedEvent.FailedTasks`, `ExecutionStatus.PartialSuccess`
(`Failed` is now `2`, `Cancelled` `3`), `CrewExecution.FailedTasks`, `CrewExecution.SuccessRate` and the
internal `CrewExecution.UpdateProgress` are removed; a run with a failed task raises
`CrewExecutionFailedEvent` instead of `CrewExecutionCompletedEvent` and leaves the crew `Failed`;
`KickoffForEachAsync` no longer runs its inputs at once; `orkeon run --events` without `--stream`
registers no `ILlmDeltaSink`, so `ctx.llm.act` stays buffered there.

Migration: read a streamed run's events by `Kind` (`RunEventKinds.TaskStarted`, `LlmDelta`,
`RunFinished`, …) and its result from the `run.finished` event's `Output`; drop
`IStreamingAgentExecutionService` and call `KickoffStreamingAsync` on a crew; drop the
`streamingService` argument of a hand-built `SequentialCrewOrchestrator` (pass `executionHook:` to have
it report failures before the strategy); call `CompleteExecution(completed)`; move a handler of
`CrewExecutionCompletedEvent` that inspected `FailedTasks` to `CrewExecutionFailedEvent`, whose
`Reason` names each task that did not succeed; replace `ExecutionStatus.PartialSuccess` with `Failed`.
A batch that relied on its inputs running at once builds one crew per input.

### Fixed — `planning: true` earns its call: the planner reads the crew and each task reads its own plan, in every mode **[breaking]**

`planning: true` made one call before the first task (GAP-29) and little else came of it: the planner
was shown the crew's goal and the **ids** of its agents and tasks — no description, no expected output,
no dependency, no tool — so all it could decide was an order; the instructions, agent assignments and
parallel groups it returned were parsed and dropped; its order, followed under the dependencies, decided
the crew's output, the context a task read and Parallel's round-robin, and a task it cited twice ran
twice; Hierarchical and Autonomous made the plan and ignored it; on the echo provider every
`planning: true` crew failed, each task "missing from the plan"; and `.ork.ts` had no switch (GAP-31):

- **The planner reads the crew.** Its goal, a line on how the mode orders the tasks and picks their
  agents, the run's variables (`initial_context` included), and each task by number, in the order the
  run takes them: its description and expected output with the variables in them, as its agent reads
  them; its dependencies, by number; and its agent's role, goal and tool names where the mode runs a
  task on the agent it names — elsewhere what the mode does instead, with the crew's agents listed once.
  Never an id. Bounded by `PlanningDefaults`: a description to 1,500 characters, an expected output to
  500, an agent's goal to 300, 15 tool names per agent, the variables to 2,000.
- **It writes one step-by-step plan per task** — `{"plans": [{"task": 1, "plan": "1. …"}]}`, at most
  8 numbered steps naming the tools to use —, the reply constrained as far as the provider can
  (`json_schema`, else `json_object`) and read tolerantly (a fence or a sentence around it). The call
  takes the run's token: cancelling the run cancels it.
- **Each task reads its own plan in its prompt, in all six modes**, after the task and before the
  context variables, under *Plan for this task, from the crew's planner — follow it where it helps; the
  task above prevails:*, cut at 2,000 characters — the worker and each revision in Hierarchical, every
  candidate in Consensual (not a ballot), each attempt in Graph, the agent that claims a task and the
  peer it is handed to in Autonomous. The run holds its plan in a scope of its own (`CrewPlanScope`),
  read where every mode's executions compose their prompt: no strategy carries it, the task's own
  description is never changed, and two runs of one crew never read each other's plan. Each task's plan
  is logged at `Information`.
- **The plan changes neither the order nor the agents.** With or without a plan, a crew runs the same
  tasks, in the same order, on the same agents.
- **An unreadable plan no longer fails the run.** It is asked for once more, with what could not be
  read, then the crew runs without a plan, with a warning — which says so when the reply stopped at the
  profile's `MaxTokens`; a task the plan leaves out runs without one, a number given twice keeps its
  first plan, an unknown number is ignored, each with a warning. A provider that fails still fails the
  run, before its first task, with its reason.
- **The echo provider skips planning** with a warning (*planning skipped — the echo provider cannot
  plan*, and `orkeon init`): `LlmProviderCapabilities.ReplaysPrompt`, declared by `UndefinedLlmProvider`
  alone, and passed through by `MeteredLlmProvider` and — now — `RateLimitedLlmProvider`, whose
  `Capabilities` hid the wrapped provider's. A keyless `orkeon run` of a `planning: true` crew exits 0.
- **`.ork.ts`: `crewBuilder().planning(value = true)`**, YAML's `planning: true`, on the host's default
  profile; the procedural shape warns that it plans nothing.
- A streamed run (`KickoffStreamingAsync`) did not plan, and warned once — until the streaming kickoff
  became the crew's own run (above).
- **Removed:** `IPlanningStrategy` and `DefaultPlanningStrategy` (no caller); the plan's order, agent
  assignments, parallel groups and dependencies (`ExecutionPlan.Assignments`, `WithTask`,
  `WithAgentAssignment`, `GetAssignedAgent`, `GetTasksInOrder`, `GetParallelGroups`,
  `PlannedTask.ExecutionOrder`, `ParallelGroup`, `Dependencies`, `PlannedTask.Create`) and the planner's
  cycle check; the `plan` parameter of `IProcessStrategy.ExecuteSequentialAsync` and
  `ExecuteParallelAsync` and of `ConsensualProcessStrategy.ExecuteConsensualAsync`;
  `IExecutionPlanParser.ParseAsync` and `ParserName`, and the text fallback of `ExecutionPlanParser`.

Breaking: `ExecutionPlan` is one plan per task (`ExecutionPlan.Create(IEnumerable<PlannedTask>)`,
`ExecutionPlan.Empty`, `InstructionsFor(TaskId)`, `new PlannedTask(TaskId, string)`);
`CrewPlanner.Create(provider, parser)` takes no strategy and `CreatePlanAsync(PlanningContext,
CancellationToken)` returns a `CrewPlanningOutcome` (the plan and its warnings), its `PlanningContext`
built of `PlanningTask` and `PlanningAgent` sheets; `IExecutionPlanParser.Read(reply, numberedTasks)`
returns an `ExecutionPlanReading`; `IProcessStrategy` implementations lose their `plan` parameter;
`SequentialCrewOrchestrator` takes an optional `ITaskRepository`, and a hand-built orchestrator needs it
and its `IAgentRepository` to plan.

Migration: drop the plan argument of a strategy call (`strategy.ExecuteSequentialAsync(crew, variables,
token)`); a custom `IProcessStrategy` removes the parameter, and a custom `IExecutionPlanParser`
implements `Read`. A crew that relied on the plan's order declares it in `dependencies:` — the order a
crew runs in is its own now, with or without a plan. Nothing changes for a YAML crew: `planning: true`
keeps its meaning and gains its effect.

### Fixed — `asyncExecution: true` runs a task alongside the next ones in a sequential crew, and is refused where the mode orders its tasks itself **[breaking]**

A task's `asyncExecution: true` (CrewAI's `async_execution`) was read, mapped and stored on the task,
and no mode read it: the two research tasks a sequential pipeline marked asynchronous ran one after the
other, and `taskBuilder().asyncExecution()` promised a concurrency nothing delivered (GAP-22):

- **Sequential honours it, with CrewAI's semantics.** The task is launched without being waited for
  and the next task starts at once; a task that depends on it (`dependencies:`, CrewAI's `context`)
  waits for it, then reads its output — and is skipped if it failed; the crew waits for every task it
  launched before it reports, and its output stays the last declared task's. A failure fails the crew,
  as in every mode, and the tasks already running go to their end. The task starts when it is launched
  and ends when the run waits for it (its lifecycle events); the hooks hear each task as it finishes,
  so `AUTO_SUMMARY.md` and `orkeon run --events` follow the order the tasks finish in. It runs through
  the same execution service as a task of a parallel wave, and a coworker it delegates to works in its
  context.
- **What a task reads never depends on timing.** An asynchronous output enters the context of the next
  tasks once a task has waited for it, in declared order. Each task's context now holds its own copy
  of the outputs so far: every task shared one list the run kept appending to, so a context captured
  during a task — by a handler, a test double — kept growing after it.
- **Parallel accepts it** without an effect of its own: a wave already runs at once.
- **Hierarchical, Consensual, Graph and Autonomous refuse it at load** — their manager, vote, graph or
  budget order the tasks — naming every task that sets it and proposing `process: sequential` or
  `parallel`: in YAML (`YamlCrewMapper`), in a `.ork.ts` crew when the run adapts it
  (`JsCrewConfigurationAdapter`), in a configuration built in code (`CrewDefinitionValidator`), and in
  C# (`CrewBuilder.Build()` throws `BuilderValidationException`). `asyncExecution: false`, the default,
  loads everywhere; `ProcessType.AcceptsAsyncExecution` says which modes accept the flag.

The three hierarchical examples that set it (87 fleet management, 95 crisis management, 101 crew of
crews) no longer do — it never had an effect there —, and 100 civilization simulator declares its three
faction strategies together, so they run at once. The limitation that said the flag was honoured by no
mode is gone from [Known limitations](docs/reference/limitations.md).

Breaking: a crew with `asyncExecution: true` outside `process: sequential` and `process: parallel` no
longer loads, and `CrewBuilder.Build()` refuses a task built with `.Async()` in those modes.

Migration: remove `asyncExecution: true` from the tasks of a hierarchical, consensual, graph or
autonomous crew — it never ran them concurrently —, or move the crew to `process: sequential`,
declaring in `dependencies:` the asynchronous tasks a task needs (a task that does not depend on them
starts without their output), or to `process: parallel`.

### Fixed — a run moves its tasks and agents through their lifecycle and raises their events as it goes, and a delegation runs in the context of the task it serves **[breaking]**

A run raised the crew's domain events and nothing else: no mode moved a `CrewTask` or an `Agent`
through its lifecycle, so `TaskStartedEvent`, `AgentCompletedTaskEvent` and their siblings never went
out, the shipped `AgentCompletedTaskHandler` / `AgentFailedTaskHandler` never logged, and a task read
back from its repository after a run was still `Pending`, without a date (GAP-21):

- **Every mode drives the lifecycle, as the run goes.** The outcome the six modes share
  (`CrewRunOutcome`) records each start, success, failure, skip, task never reached and interruption,
  and `TaskLifecycle` applies the transition — the task assigned to the agent that runs it, started,
  then completed or failed; the agent given the task, started, then completed or failed —, saves both
  in the run's repositories, then dispatches their events through `IDomainEventDispatcher`, before the
  crew's own at the end of the kickoff. A handler that throws is logged, as for the crew's events; a
  transition the aggregate refuses is a warning, never a failed run.
- **What each mode adds.** Parallel starts a task as it launches it and ends a wave's tasks once the
  wave has joined, an agent running two tasks of a wave at once. Graph starts a task once and keeps it
  running through its retries. In Hierarchical the agent the manager assigns starts the task —
  re-assigned from a declared one — and ends it once, its revisions included. A consensual task starts
  once, under no single agent; every agent that answers starts it and ends it with its own answer, and
  the task completes under the author of the retained one. In Autonomous a task handed to a peer is
  failed by the agent that claimed it and completed by the peer.
- **A task that does not run is cancelled, saying why**: skipped behind a dependency that did not
  succeed (neither started nor ended), never reached by an exhausted budget, or caught by a
  cancellation (`the run was cancelled`); a run that stops on an exception fails the task it was
  running. After a run the repository holds `Completed`, `Failed` or `Cancelled`, with the dates; a crew
  kicked off again without being reloaded reopens its tasks (`CrewTask.Reopen`).
- **`StepsExecuted` is real.** `TaskCompletionInfo.StepsExecuted`, handed to every `ICallbackHandler`
  and logged by `LoggingCallbackHandler`, is the number of turns the agent loop ran
  (`TaskResult.IterationsUsed`); it was 1 whatever the loop did.
- **A delegation runs in the context of the task it serves.** `delegate_work_to_coworker` derives the
  coworker's context from the delegating task's with `with` — the crew's id, the memory scope, the
  outputs so far and the parent's settings, so it recalls when the parent does — and turns
  `StoreResultInMemory` off: under `memory: true` a coworker's sub-answer was stored as a task result,
  and the parent's settings were lost. Without the context of a crew run, a synchronous delegation is
  refused rather than run under a fresh crew id. A crew kicked off again keeps its agents: their
  delegation tools are now replaced, so they read the new run's context — the second run of a
  Sequential or Graph crew whose agents may delegate failed at its start ("Tool
  delegate_work_to_coworker already exists").
- **An Autonomous takeover is the task.** The peer runs the failed task itself — not a copy built from
  its description — in the failed attempt's context, derived under the crew's id, with the run's inputs
  and the outputs so far: it recalls the crew's memory, and its output, when it succeeds, is stored
  once, under it. It ran under a fresh crew id: nothing stored, nothing recalled. A `delegate` request
  naming no task of the run is refused.

Breaking: the dead surfaces of the agent aggregate are removed — `Agent.UpdateMemory` and
`AgentMemoryUpdatedEvent`, with `Agent.Memories`, `AgentSnapshot.Memories` and
`AgentDefaults.MaxMemories` (a crew's memory is the memory coordinator's), and `Agent.CollaborateWith`
and `AgentCollaborationStartedEvent`, with `CollaborationId` (agents collaborate through delegation);
31 domain events remain. An agent runs several tasks at once: `Agent.CurrentTask` becomes
`CurrentTasks` (`AgentSnapshot.CurrentTask` too), `CompleteTask` and `FailTask` take the task's id, and
`AssignTask` accepts a busy agent. `CrewTask.AssignTo` re-assigns a pending or running task (the same
agent changes nothing), `Start` no longer blocks a task that has dependencies — the run decides when it
starts —, `Start()` starts a task no single agent runs and `TaskStartedEvent.AgentId` is nullable for
it, `Fail` records `CompletedAt`, `GetExecutionTime` counts from `StartedAt` only, and `Reopen` is new.
`CrewStrategyDependencies`, `ParallelProcessStrategy` and `HierarchicalProcessStrategy` take an optional
`IDomainEventDispatcher`, which the container fills. `DelegateWorkTool` refuses `wait_for_result: true`
when its context supplier supplies no context. The limitation that said a run raised the crew's events
only is gone from [Known limitations](docs/reference/limitations.md).

Migration: call `CompleteTask(taskId, output)` / `FailTask(taskId, reason)` and read `CurrentTasks`;
expect a null `AgentId` on the `TaskStartedEvent` of a consensual task; register an
`IDomainEventHandler<T>` for the task and agent events to follow a run task by task; remove calls to
`UpdateMemory` and `CollaborateWith` — store and recall through `IMemoryCoordinator`, collaborate
through the delegation tools; give a `DelegateWorkTool` built by hand a context supplier for
synchronous delegation.

### Fixed — a crew with `memory: true` recalls its earlier work before each task, and a memory that fails no longer fails the task **[breaking]**

Every crew stored each successful output, whatever its `memory:` said, and nothing in a run read the
memory back into a prompt (GAP-30, the four findings left by GAP-20):

- **`memory:` is the switch.** With `memory: true` (`.memory(true)` in `.ork.ts`,
  `CrewBuilder.EnableMemory()` in C#) a run stores the result of each task and, before each task,
  recalls the crew's closest memories into its prompt; with `memory: false`, or no `memory:`, nothing
  is stored or recalled and no memory system is materialized. The default is unchanged: off.
  `memoryProvider:` without `memory: true` is refused, the remedy in the message — at load, and by
  `Crew.Create` (so `CrewBuilder.Build()`) and `Crew.UpdateConfiguration`.
- **A named crew that names no provider lives in the host's default store** (`Memory:Provider`,
  In-Memory when unset), scoped by its name, as the aggregate's contract said. It lived in a store of
  its own that did not outlast the run, so an `.ork.ts` crew — the DSL has no `memoryProvider` —
  never kept anything. `orkeon-host` now remembers from one message to the next; `orkeon run` from one
  process to the next only with a durable store (`memoryProvider: sqlite`, or a durable
  `Memory:Provider`). An unnamed C# crew keeps a store of its own.
- **The recall is a vector search in the crew's scope.** `AgentExecutionService` asks
  `IMemoryCoordinator.RecallAsync` for the memories closest to the task — on the query of the
  knowledge retrieval: description, expected output, variables — leaving out those the prompt already
  carries as previous outputs. The user prompt renders them after the previous outputs and before the
  retrieved knowledge, under `PromptDefaults.MemoriesHeader`, one `--- date · role · task ---` line
  before each; the Guardian screens them with the rest of the prompt. The new `Orkeon:CrewMemory`
  section (`CrewMemoryOptions`, bound by `AddOrkeonInfrastructure()`) bounds it: `RecallLimit` 5,
  `MinScore` 0.6 (cosine, measured on the local model), `MaxChars` 4,000. A consensual candidate and a
  hierarchical attempt recall — they answer the task; a ballot does not
  (`SimpleExecutionContext.RecallFromMemory`, turned off by `AgentBallotCollector`).
- **Every memory is embedded, and the embedder is checked at kickoff.** A memory is embedded on its
  task and its output by the host's `IEmbeddingProvider`, the port of the RAG. Before its first LLM
  call, a crew with `memory: true` embeds a probe and searches its memory once
  (`IMemoryCoordinator.EnsureReadyAsync`): a missing embedder, a refused key, an unreachable store or
  a vector of the wrong dimension fails the run, naming the crew, the cause and the remedies.
- **A memory that fails during a run is a warning.** A store or a recall that fails is logged as a
  `Warning` — the crew, the task, the agent, the store, the cause — and the task keeps its output; a
  failed recall leaves it without memories. A failed store used to fail the task, its output lost, and
  since GAP-03 the crew: on Pinecone and ChromaDB, every task. `ConsensualProcessStrategy` no longer
  fails a retained answer it cannot store.
- **`Hierarchical` stores the accepted output only**, once, under the assigned agent: every attempt
  runs with `StoreResultInMemory` off — a task stored up to three outputs, all rejected when the
  manager rejected them all. The revision contexts are derived (`with`), so their settings travel.
- **Pinecone and ChromaDB stop faking.** Their `StoreAsync` refuses an item without an embedding
  before any request (`ArgumentException`), as their collection paths did — they sent no vector, which
  a real server rejects. Pinecone's `SearchAsync` throws `NotSupportedException`: it has no text
  search, and sent an empty vector with an exact match on the content. ChromaDB's sends
  `POST …/get` with `where_document: {"$contains": query}` and the filter as `where` — case-sensitive,
  as the server matches; an empty query sends the filter alone — where it sent `query_texts`, which only
  the clients serve. The `memory_provider` health check reads an absent key, which every provider
  serves, where it searched by text.
- **The cognitive memory works in the crew's memory** (`AddOrkeonCognitiveMemory`): it stores what it
  remembers once — it stored it twice — stamped like the run's memories (`CrewMemoryScope`, now public),
  so a crew has one memory. Recall, contradiction candidates and consolidation search it by similarity
  within the crew's scope — the recall searched every crew's memories and the RAG chunks; a conflict is
  resolved, and a consolidation merges and prunes, in the store the memories came from; a merged
  memory is embedded.
- **A forge trial runs without memory**, whatever the plan says: the promoted crew would recall the
  trial's outputs as its earlier runs. **The streaming kickoff** (`KickoffStreamingAsync`) neither
  recalled nor stored, and said so in a warning — until it became the crew's own run (above).

Breaking: a crew without `memory: true` stores nothing, one that names a `memoryProvider:` without it
is refused, and one with it needs an embedder at kickoff. `IMemoryCoordinator` is reduced to
`EnsureReadyAsync`, `RecallAsync` (which replaces `RetrieveRelevantMemoriesAsync`) and
`StoreTaskResultAsync` — `StoreAgentExperienceAsync` and `UpdateWorkingMemoryAsync`, which nothing
called, are removed; `MemoryCoordinator` requires the registry and takes the embedder and
`IOptions<CrewMemoryOptions>`. `CrewMemoryProviderRegistry.Record` takes `memoryEnabled`, with
`IsMemoryEnabled` and `GetName`. `ILongTermMemory` gains `SearchSimilarAsync` and `RemoveAsync`.
`HierarchicalProcessStrategy` requires an `IMemoryCoordinator`; `SequentialCrewOrchestrator` takes an
optional one, and `MemoryService` the application's `IMemoryProvider`. `CognitiveMemoryService` no
longer takes an `IMemoryProvider`; `MemoryConsolidator` takes an `IEmbeddingProvider` in its place,
and `ConsolidateAsync` the memories found (`ScoredMemoryItem`) with the memory they came from.
[Known limitations](docs/reference/limitations.md) now say what the crew's memory does not do: no
retention or reset, recall by vector only, durable only in a durable store.

Migration: add `memory: true` to a crew that names a `memoryProvider:`, or drop the provider; give a
host that runs a crew with memory an embedder — `AddOrkeonLocalEmbeddings()` or the
`Orkeon:Embeddings` section; `orkeon run` and `orkeon-host` register the local model unless
`RaggableTree:Enabled: false`, the REPL always — whose dimension is the store's (the local model gives
384, LanceDB defaults to 1,536); store items with their embedding in Pinecone and ChromaDB, and search
Pinecone with `SearchSimilarAsync`; implement the two new members in a custom `ILongTermMemory`; call
`RecallAsync` where `RetrieveRelevantMemoriesAsync` was called; tune the recall in `Orkeon:CrewMemory`
— `MinScore` is on the embedder's scale.

### Fixed — a call's configuration completes its provider's, `planning: true` plans, and the REPL's chat client has a key **[breaking]**

A configuration passed with one LLM call replaced the provider's whole (`config ?? Config`), and every
caller outside the chat client builds one to set its temperature (GAP-29):

- **Calls lost the key, the endpoint and the timeout of their provider.** On a real vendor, the crew
  planner, the cognitive memory's analyses (`MemoryAnalyzer`, `ContradictionDetector`,
  `MemoryConsolidator`), the context-window summary (`OpenAIContextWindowManager`), the RaggableTree
  node summarizer (`LlmNodeSummarizer`), the text and native agent loops and the output-validation
  correction round answered "API key is required" — or left for the vendor's default URL, on a 30 s
  timeout whatever `Llm:TimeoutSeconds` said, without the provider's `Thinking` nor Azure's API version
  — and fell back without a word: a plan "missing" its tasks, a default analysis, a truncation, no
  summary. A call's configuration now **completes** the provider's (`LlmConfig.InheritFrom`, applied by
  `HttpLlmProviderBase.EffectiveConfig` in the sixteen providers): every field the call leaves unset is
  the provider's, every field it sets wins, custom parameters merge. The sampling settings that cannot
  be unset — `Temperature`, `TopP`, the penalties, the tool mode — stay the caller's. The chat client
  adapter registered without a base configuration starts from the provider's own.
- **`LlmConfig.TimeoutSeconds` is nullable.** It defaulted to 30, so a call could not tell "not set"
  from "30": null now inherits the provider's timeout, else `LlmDefaults.DefaultTimeoutSeconds` (30 s,
  `LlmConfig.ResolveTimeoutSeconds()`).
- **`planning: true` plans.** The orchestrator planned only when the crew carried a planning provider,
  which only C#'s `WithPlanningLlm` sets: a YAML crew, or a C# one calling `.Planning(true)` alone,
  planned nothing while the documentation said it did (the `.ork.ts` DSL has no planning switch). It plans now, before the first task, on the crew's
  planning provider, else on the host's default profile. The plan's order is followed wherever the
  declared dependencies allow it — the planner sees task ids, not their dependencies, and a plan taken
  as is could run a task before one it depends on. A plan that fails says why: the planner read a failed
  call's empty content and reported every task "missing from the plan".
- **`orkeon-repl` serves a chat client on its `Llm` section.** It registered its provider without one,
  so its `IChatClient` — agent turns, RAG answers, judges — was the infrastructure's fallback, a second
  provider with no key on OpenAI's endpoint. It registers its provider the way `orkeon run` does — one
  instance on the three surfaces, the chat client on the section's configuration, the section read by
  the runners' `LlmSettings` — and the echo provider, with the runner's warning, without an `Llm`
  section.
- **The RaggableTree summarizer names no model by default**: `SummarizerOptions.Model` and
  `LlmNodeSummarizerOptions.Model` were `claude-haiku-4-5` on whatever vendor the host runs; the
  provider's own model now.
- **Removed:** `AddOrkeonInfrastructure()`'s model fallbacks — the keyless `IBasicLlmProvider` and
  `IChatClient` it registered when the host had none (a container without a model fails at its first
  LLM resolution, naming what to register); and the planning settings nothing read:
  `Orkeon.Domain.Crew.Planning.PlanningConfiguration`, `Orkeon.Application.Agent.PlanningConfiguration`,
  `OrkeonApplicationOptions.EnablePlanning` and `PlanningLlmModel`, `LlmDefaults.DefaultPlanningModel`.

Breaking: `LlmConfig.TimeoutSeconds` is an `int?`, and so is `CreateValidated`'s `timeoutSeconds`; a
call can no longer erase a provider setting by leaving it null — it overrides it
(`Thinking = { Enabled = false }`, `LlmResponseFormat.Text()`). A container that registers no model
fails where it got a keyless OpenAI provider. A crew with `planning: true` makes its planning call now —
on the echo provider (no `Llm` section), which replays the prompt, it fails at its plan.
`SequentialCrewOrchestrator` takes an optional `ILlmProfileRegistry`.
[Known limitations](docs/reference/limitations.md) say what planning does not do yet: the planner sees
ids only, and Hierarchical and Autonomous make the plan without using it.

Migration: read `ResolveTimeoutSeconds()` for the timeout a request runs on; a provider deriving from
`HttpLlmProviderBase` that reads a call's configuration takes `EffectiveConfig(config)` instead of
`config ?? Config`; a host registers its model with `AddOrkeonLlmProvider(…)`, which serves the three
surfaces — one that registered `ILlmProvider` and an `IChatClient` by hand registers `IBasicLlmProvider`
too (`new LlmProviderAdapter(provider)`), which the keyless fallback used to fake; drop the planning options
— `planning: true` (`.Planning(true)`) is the switch, `WithPlanningLlm` the C# provider.

### Fixed — a crew's memory is its own: scoped by its name, without RAG chunks, without ballots **[breaking]**

A crew that names a memory provider (`memoryProvider:`) keeps its long-term memory in that type's
provider, the one instance every crew of that type shares — with the RAG store of that type too when
`Orkeon:Rag:Provider` is unset (GAP-08). What it stored and what it read did not account for that
(GAP-20):

- **A crew read every crew's memory.** Entries were stored under a bare id and a search scanned the
  whole store: the "legal watch" crew could get the "customer follow-up" crew's outputs, and any RAG
  chunk — or chunk manifest — whose text held the query. Each entry now carries `kind = crew-memory`
  and `crew = <the crew's name>`, and the crew's searches ask the provider for both. The name — the
  `name:` of the YAML file, crew directory or `.ork.ts` crew, `CrewBuilder.Name` in C# — is the same
  from one run to the next, so a crew still reads what its earlier runs stored, and only that. A crew
  without a name is scoped by its id, for one run. `MemoryCoordinator` also tags each memory
  `crew:<name>`.
- **A consensual task stored every candidate and every ballot.** Each agent's answer and each
  `{"ranking": …}` ballot ran through the execution service, which stores a successful result: the
  crew's memory held the answers the vote rejected, and the ballots, as task outputs. A new
  `SimpleExecutionContext.StoreResultInMemory` (on by default) is off for the candidates and the
  ballots — `AgentBallotCollector` turns it off for any ballot it casts — and the strategy stores the
  retained answer once, under the agent that wrote it. A store that fails fails the task, as in the
  other modes.
- **The providers' text search had no filter.** `IMemoryProvider.SearchAsync` takes the `filter` of
  `SearchSimilarAsync` (`source`, `tag`/`tags`, any other key an equality on a custom property), and
  every provider applies it before its limit: in memory for In-Memory, Redis and SQLite, as ChromaDB's
  `where`, Pinecone's metadata filter and LanceDB's prefiltered predicate. On the way: ChromaDB refused
  a filter of two keys (they go under one `$and` now); SQLite's and LanceDB's filters matched a custom
  property by substring — of the whole custom-properties JSON for SQLite, of the value for LanceDB — so
  `crew = "legal"` found `"legal ops"`, and they compare the property now (LanceDB its `"key":"value"`
  pair, spelled as the stored JSON spells it); ChromaDB and Pinecone dropped an item's custom
  properties in their default collection/namespace, where a filter on one found nothing — they keep
  and restore them, as the named ones did.

Breaking: `IMemoryProvider.SearchAsync(query, limit, filter, cancellationToken)` and
`MemoryProviderBase.SearchAsync` take the filter before the token — an implementation adds the
parameter, a call that passed the token by position names it (`cancellationToken:`).
`CrewMemoryProviderRegistry.SetProvider(crewId, type)` becomes `Record(crewId, type, crewName)`, with
`GetScope(crewId)`; `MemoryCoordinator` takes the registry, `ConsensualProcessStrategy` an
`IMemoryCoordinator`; `Crew.Name`, `CrewCreateOptions.Name` and `CrewBuilder.Name(name)` are new. A
crew no longer reads what other crews stored in a shared provider, nor entries stored before this
version (they carry no `crew`). Give a C# crew a name (`CrewBuilder.Name`) to keep its memory from one
run to the next. [Known limitations](docs/reference/limitations.md) now say what a run does with a
crew's memory: it writes it, and nothing in a shipped run reads it back into a prompt.

### Fixed — a model left unset is the profile's own, on every path **[breaking]**

GAP-17 made a crew's `llm:` block that names no model run on its profile's model; other paths
still pinned OpenAI's default model (`LlmDefaults.DefaultModelName`) on whatever vendor served the
call, and fifteen of the sixteen providers refuse it ("model not found") (GAP-18):

- **A C# agent's sugar.** `AgentBuilder.Thinking()` and `.MaxOutputTokens(n)` seeded
  `LlmConfig.Create(LlmDefaults.DefaultModelName)`: on a DeepSeek or Anthropic host, such an agent
  sent OpenAI's model. They start from `LlmConfig.OnProfile()` now, and the agent runs on the
  host's model.
- **`llm.default_`** on a host whose provider configures no model returned — and an agent
  configured with it pinned — OpenAI's model. It reads an empty `model` now, and the call carries
  the provider's own.
- **The calls outside the chat client.** The text and native agent loops, the output-validation
  correction round and the crew planner called with `LlmConfig.Default()`; they name no model now
  (the planner keeps its temperature 0.3). So do the forge's judge and `ctx.llm`'s call-time
  overrides on a provider that declares no configuration, the context-window summarizer, the
  cognitive memory's analysis calls without `AnalysisModel` (documented as "the default provider
  model"), the chat client registered without a base configuration
  (`AddOrkeonLlmProvider(provider)`), and `orkeon llm probe` on a provider without a default of its
  own (Azure).
- **The providers resolve an empty model.** A call's configuration replaces the provider's, and
  the providers fell back with `config.Model ?? default`, which an empty string defeats: such a
  call went out with `"model": ""`. `HttpLlmProviderBase.ResolveModel(config)` is the one rule now —
  the call's model, else the model the provider is configured with (its profile's), else its
  `DefaultModel` — for the payload, the response's `Model` (what the meter records), the output-cap
  lookup and the error hints, in the OpenAI-compatible family, Anthropic, Azure OpenAI (whose
  deployment no longer falls back to OpenAI's model before the configured one) and Ollama (whose
  six hard-coded `"llama2"` fallbacks become its documented default, `llama3.2`). The adapters
  over an `IChatClient` leave `ModelId` unset instead of empty, and the meter attributes a stream
  that named no model to the provider's.
- **An `Llm` section — or a profile — without `Model`** runs on the inferred provider's own default
  model: the settings reader, and the REPL's, filled in OpenAI's, which a DeepSeek endpoint inferred
  from `BaseUrl` refused. With no model to read, the vendor inference also reaches the API-key
  shape it documents (`xai-` → Grok, `hf_` → HuggingFace), which the filled-in OpenAI model always
  pre-empted.
- **Removed:** `LlmConfig.Default()` — a name that says "default" and pins one vendor's model was
  the trap. `LlmConfig.Model` is empty by default, and `LlmConfig.OnProfile()` is the configuration
  that names no model. `DefaultModel` moved from `OpenAICompatibleProviderBase` to
  `HttpLlmProviderBase` (abstract): Anthropic and Ollama declare theirs.

Migration: replace `LlmConfig.Default()` with `LlmConfig.OnProfile()` — the same settings, on the
model of the provider the call reaches — or with `LlmConfig.Create(model)` to pin one
(`LlmConfig.WithDefaultModel()` still names OpenAI's default model, explicitly). A provider that
derives from `HttpLlmProviderBase` directly overrides `DefaultModel`.

### Fixed — `balanced` and `quality` work in `orkeon-host` and the REPL; runs resolve scoped services in their own scope

The `balanced` and `quality` RAG profiles (and `adaptive`, whose `SingleShot` route delegates to
`balanced`) rerank with the ONNX cross-encoder, and only the `orkeon` CLI registered it: a hosted
crew whose `knowledge:` asked for either profile, or `rag_search` in `orkeon-repl`, failed its first
retrieval with "Unknown reranker 'onnx'" (GAP-25). `orkeon-host` and `orkeon-repl` now reference
`Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model` and call `AddOrkeonOnnxReranker()`, as the CLI does —
and the CLI now does in `orkeon forge` too, whose trials run the crew `orkeon run` will run once
promoted: a forged crew's `rag_search` on a host set to `balanced` failed its trial and worked after
promotion. `Orkeon.Hosting` (a NuGet package) stays free of ONNX. Each of the two binaries gains
23.4 MB, 16.5 MB once compressed in an archive (the embedded int8 weights; ONNX Runtime was already
there for the local embeddings). The daemon's own registrations moved from `Program.cs` to
`HostServiceRegistration.AddHostServices`, so a test builds the very host it runs. The weights'
Apache-2.0 notice travels with them: `THIRD-PARTY-NOTICES.md` lists the three binaries that carry
them, and the `orkeon-host` service MSI now installs that file next to `LICENSE.md` (it shipped the
BGE-micro-v2 embedding weights without it).

`orkeon run` (and `--validate`) resolved the crew factory, the orchestrator and the agent repository
— all scoped — from the root provider, and so did each `orkeon forge` trial; the load and the
kickoff now run in one scope, as `orkeon-host` does per run, and the runner host and the daemon are
tested with scope validation on (a Development host's default). The Agent Framework bridge had the
same defect, in its public API:

- **Breaking** — `ICrewAgentFactory` was a singleton holding the root-resolved orchestrator, so
  every `CrewAgent` of a host shared it (and its repositories) for the life of the process, and a
  host validating scopes refused to resolve it. It now holds the `IServiceScopeFactory`, and
  `Create(loadCrew, name, description?)` replaces `Create(crewId, name?, description?)` and
  `Create(crew)`: each turn opens a scope, `loadCrew` registers the crew in it and returns its id,
  the scope's orchestrator runs it. `new CrewAgent(scopeFactory, loadCrew, name, description?)` is
  the same form without the factory; `CrewAgent.CrewId` is now `CrewId?` (`null` for that form).
  Migration: move the code that adds the crew's agents, tasks and crew to the repositories (or
  loads it through `ICrewFactory`) into the loader, resolving the repositories from the provider it
  receives, and return the crew's id — see `examples/interop/agent-framework/` and ADR-010's
  amendment. `new CrewAgent(orchestrator, crewId|crew)` is unchanged.

### Fixed — `check-doc-claims.py` reads what git publishes, not the ignored trees of a working clone

The documentation-claims gate walked the file system, so on a working clone it also read the
git-ignored third-party checkouts under `examples/others/` (~34,000 files): more than ten minutes
and some 361,000 false reports, locally only — a CI clone has no such folder (GAP-28). Its three
walks (the counted files, the private-pointer scan, the `.md` files a citation resolves against)
now go through one `git_files()` helper on `git ls-files --cached --others --exclude-standard`:
tracked files and new ones not yet staged, never an ignored path, never a private submodule's
content. `scripts/test-check-doc-claims.py` proves it on a scratch repository and runs in CI.
`validate-all-examples.sh` lists the example crews the same way. The Studio test that still
expected `image_generation`'s retired "key given at the call" wording (red since the Guardian lot)
now expects the stored key `ORKEON_OPENAI_API_KEY` the catalogue declares.

### Added — files and folders can be dropped on the need of the wizard's step 1

Studio's « What work do you want to give this team? » now takes files and folders dragged from the
explorer, several at once (STUDIO-47). Each path that exists is inserted at the caret, between
quotes and separated by a space; a path that does not exist is ignored. A dropped folder — or a
dropped file's own folder — reaches the Folders step (STUDIO-46) already bound to that real folder:
it answers the row the request gave its name, else the default input `/workspace`, else it joins
the list as an input named after the folder (`My PDFs` → `/my-pdfs`), and it is declared in
« Settings › Authorized folders » the way a disk pick is. A hint under the field says so, in the
five languages. The forge protocol is unchanged: the candidates are merged on the Studio side.

### Changed — the forge proposes the team's folders from the need, and they are used everywhere (breaking: forge protocol)

« Where are your folders? » always offered `workspace/` and `output/`, never the folders the need
named, and « Create inside the team » pointed at a folder that existed only after the adoption
(STUDIO-46). The folders now come from the request, are confirmed once, and are the team's folders
from the plan to the adopted team.

- **`ForgeBrief.folders[]`** (`path`, `role` `input`|`output`, `purpose`): the forge assistant lists
  the folders the request names — « …PDFs in `/inpdf`, the Markdown in `/outmd` » — and when it
  names none the forge proposes `/workspace` to read (only when something comes in) and `/output`
  to write. A folder is one absolute segment; `/forge` and the runner's roots are refused; at least
  one output.
- **Forge protocol** — a new event and a new answer between the brief and the plan:
  `folders.proposed` (`folders`) out, `folders.confirmed` (`folders`, each with an optional `dir`,
  the real directory bound behind it) in. `brief.ready` now follows the confirmation and carries
  the confirmed list. A list that breaks the rules is a recoverable `FORGE-FOLDERS-INVALID`. The
  terminal and `--auto` take the proposal as it is and print it. **Migration**: a client of
  `orkeon forge --events jsonl` must answer `folders.proposed` with `folders.confirmed` (sending the
  proposal back unchanged is a valid answer) — the engine waits for it.
- **The confirmed list is used everywhere**: kept in the session's `folders.json` (with the bound
  directories) and in the brief (without them); the plan prompt lists it and a deliverable must
  land in a confirmed output folder (any one, no longer `/output` only); the crew's `mounts:` block,
  the trial bench's mounts (through a VFS scope, so a list confirmed mid-run reaches the trial),
  the deliverable check (`runs/<n>/folders/<name>/`), `forge promote`'s folders and launchers and
  `forge reopen`'s rebuilt session all follow it. `/workspace` and `/output` are no longer added by
  default — the trial drops them unless the list holds them.
- **« Inside the team » works before the adoption**: such a folder lives in the session
  (`.orkeon/forge/<slug>/folders/<name>`), readable by the trial, and `forge promote` moves it into
  the team.
- **`--read <dir>`** now answers the first input folder the confirmed list binds to no directory
  (`/workspace` when the need named none).
- **Studio**: the step-1 folders card is gone; a **Folders step** above the conversation shows the
  proposal — each name editable, its role, « Choose a folder… » (the disk picker, declared on the
  way) or « Inside the team » (an output's default) — and « Confirm the folders » sends the list.
  The Composer rows, the tool chips and the sidecar follow the confirmed list
  (`ForgeSessionModel.Folders`, hydrated from `folders.json` on a resume). Studio.Core gains
  `ForgeEventKinds.FoldersProposed`/`FoldersConfirmed`, `ForgeFolder`,
  `ForgeClient.SendFolders`, `ForgeSessionModel.ProposedFolders`/`FoldersPending`/`Folders`/
  `AcknowledgeFolders`. **Removed, no shim**: `FolderPolicy`, `CreateTeamViewModel.FolderPolicy`,
  `FolderPolicyChoices`, `StepOneRows`, `HasStepOneRows`, `NewRootName`, `NewRootIsReadWrite`,
  `AddNamedRootCommand`, `CanAddNamedRoot`, `NamedRoots`, and the strings
  `Studio.Create.QFolders/FoldersExisting/FoldersInside/FoldersLater/ReadRootTitle/WriteRootTitle/
  FoldersPolicyHint/AddRootHint/AddRootWrite/AddRoot/PrecisionsTitle/PrecisionsSub` in the five
  languages; the capture stops `etape1-dossiers-existants`/`-equipe` became `dossiers-proposes`.

### Changed — the team-creation wizard's step 1 asks for the need only

Step 1 asked four things before « Compose the team » woke up: how often, where the information
lives, what the team must produce, and « describe that result » (STUDIO-45). The frequency only
ever reached the brief's text — the adoption step schedules — and the other two are what the
assistant's conversation exists to settle.

- **Step 1 is the need** (and, until STUDIO-46 just above, where the folders live). « Compose » is live as
  soon as the need is typed; the brief the engine receives is the need plus the standing
  instruction, and the session is named after it.
- **The forge asks only what the need leaves unsaid** (`forge-assistant.ork.js`): a question
  about what comes in or what comes out only when the request does not say it, zero questions
  is a good result, and never a question about frequency. « Three to five questions is the
  norm » is gone.
- **The recap reads the validated brief.** « What comes in » and « What comes out » come from
  the forge's brief (`inputs`, `expectedOutput`, now read by `ForgeSessionModel.BriefInputs` /
  `BriefOutput`); the « Rhythm » row is gone.
- **Removed, no shim**: `CreateTeamViewModel.FrequencyChoices`, `SourceChoices`, `OutputChoices`,
  `Outcome`, `OutcomeRequired`, and their strings (`Studio.Create.QFreq/QSource/QOutput`,
  `Outcome*`, `Freq*`, `Source*`, `Output*`, `BriefFrequency/Source/Output/Shape`, `HintOutcome`,
  `Studio.Chat.FactRhythm`) in the five languages. `Studio.Create.HintAnswers` became
  `HintAssistant` — with the need typed, only the assistant's model can still be missing.

### Fixed — a remembered API key is recognised by Studio the first time, and a failed write shows

« Remember the key », then « Save », then back to the profile: the screen said « no key detected »
and the gesture had to be repeated (STUDIO-44). The key was in the user scope (`HKCU\Environment`),
but Studio read only its own process environment — stale whenever Studio was started by a
terminal, an IDE or a launcher older than the key; and a user-scope write that threw left the
process unwritten, with the error swallowed.

- **Read: the process, then the user scope.** `EnvironmentApiKeyStore.Peek` falls back to the user
  scope and copies a key found there into the process, so every child Studio spawns inherits it.
- **Write: the process first, the user scope off the interface thread.** `IApiKeyStore.Save` is now
  `SaveAsync`: the key is in place for the session when it returns, and the task faults if the
  user scope refuses it. The editor, the key row and the profile list say so, in the five
  languages (`Studio.Settings.KeyPersistFailed`); the session keeps its key.
- **Launches resolve the key through the store.** A team run and the creation assistant lay
  `ORKEON_Llm__ApiKey` from `ModelProfilesViewModel.LaunchEnvironmentOf`, no longer from Studio's
  process block alone.
- The scope access sits behind a new port, `IEnvironmentVariables` (`SystemEnvironmentVariables`).

### Fixed — Studio's connection test waits 30 s, exercises the profile, and says why it failed

The « Test connection » button of a model profile gave up after 5 s with « no answer within 5s »,
and only asked the host for its model list: a Z.AI profile failed on a slow first request, and
looked as if « Thinking: off » were to blame when the setting never reached the request (STUDIO-43).

- **30 s**, or the profile's timeout when it is shorter (`LlmProbeRequest.TimeoutFor`).
- **Two steps.** After `GET /models`, a minimal completion (`max_tokens` 16, one « ping ») on the
  profile's model with its thinking switch, written per dialect from each provider's declared
  thinking level (`LlmProbeDialect`, pinned against the real providers). A model that does not
  exist, or refuses « Thinking: off », now fails the test at that step. It costs a few tokens.
- **A diagnostic.** The verdict is a structured `LlmProbeResult` (`Stage`, `Url`, `Elapsed`,
  `StatusCode`, `Detail`, `Failure`) worded in the interface's five languages by `LlmProbeText`:
  the step, the URL called, the time waited, the HTTP status and the start of the body, or the
  exception down to its innermost cause. The key never appears, not even in the URL.
- **The screen follows.** « Testing the connection… » and a disabled button while it runs;
  changing the thinking switch, the model or the URL clears the previous verdict instead of
  leaving it under the new setting until the editor is reopened. The Llm section and the
  `orkeon-studio-config` TUI run the same two steps.

### Added — one LLM provider per agent: named profiles in the host configuration **[breaking]**

A host talked to one provider, the one its `Llm` section described; every agent of every crew
went through it, and a crew mixing a Claude planner and DeepSeek writers needed two hosts or an
aggregator (GAP-17).

- **Named profiles.** Each child of `Llm:Profiles:<name>` is a provider described with the keys
  of the `Llm` section (`BaseUrl`, `ApiKey`, `Model`, `Temperature`, `MaxTokens`,
  `TimeoutSeconds`, `MaxRetries`, `Thinking`, `Grammar`); the `Llm` section stays the default
  profile. `orkeon run`, `orkeon-host` and the REPL read them (`AddOrkeonLlmProfiles(configuration)`;
  `AddOrkeonLlmProfile(name, provider, baseConfig)` registers one over a provider the factory does
  not build). Each profile's provider is built once, on first use, metered like the default one.
  The profiles are validated at startup: `default` is reserved, an invalid `BaseUrl` or a value
  that is not a number fails the start naming the key.
- **A crew names a profile, never a key or an endpoint.** YAML: `llm: { profile: claude, model: … }`
  at crew level, on an agent, or in a task's `llm_override` (that task only; `profile: default`
  returns to the default). `.ork.ts`: `agentBuilder().llm(llm.profile("claude", overrides?))`;
  a procedural agent configured with it has `ctx.llm` talk to that profile's provider. A
  `response_format` or a thinking block is checked against the agent's own provider.
- **An unknown profile fails the crew load**, like an unknown tool, and the message lists the
  profiles the host offers. `orkeon-host` decides which profiles its third-party crews may name:
  `Orkeon:Host:LlmProfiles` is an allow-list (unset offers them all, `["default"]` the default
  alone; an entry naming an undefined profile refuses the start with exit code 78).
- **The meter follows the provider.** Each `cost.updated` reading already named its call's
  provider; Studio's status bar now breaks the run's tokens down per provider when there are
  several. The hierarchical manager, the planner, the Guardian, the RAG pipelines and the LLM judges
  stay on the default profile.
- **Fixed: an agent's `llm:` block reaches the agent.** `CrewFactory` built every agent without the
  `LlmConfig` the YAML or `.ork.ts` mapping had produced, so a per-agent model, temperature, token
  cap, thinking block, response format or cache request never reached the wire under `orkeon run`
  — every agent ran on the host's settings. A block that names no model now runs on its
  profile's own model (`LlmConfig.OnProfile`, empty `Model`) instead of pinning the framework's
  default model on whatever vendor the host runs; `CrewValidator` accepts an empty model.
- **Removed:** the provider pre-validation of `CrewConfigurationMapper.ToDomainCrew`, which built a
  provider per agent and threw it away; its `ILlmProviderFactory` parameter is now
  `ILlmProfileRegistry? llmProfiles`, and the mapper checks the profile names instead.

Migration: a caller of `CrewConfigurationMapper.ToDomainCrew(toolResolver, llmProviderFactory, …)`
passes the host's `ILlmProfileRegistry` (or null) in place of the factory. A crew whose agents
declared an `llm:` block now runs with it: check the models those blocks name — they reach the
provider for the first time. A crew that mixed providers by running one host per provider moves
the providers into `Llm:Profiles` and names them per agent.

### Fixed — small corrections: the planner warning, `ORKVFS005`, `WithOrkeonSetting`, the Sonar scripts; three dead surfaces removed **[breaking]**

A handful of messages, comments, scripts and tests taught something the code does not do
(GAP-16).

- **The stub-planner warning advises the order that works.** It said to register your own
  `IAgentPlanner` *before* `AddOrkeonApplication`; that registration loses, because
  `AddOrkeonApplication` adds the stub without `TryAdd` and the last registration wins. It now
  says *after*, as `default-behaviors.md` already did.
- **`ORKVFS005` names a service that exists.** Its message sent you to "IVirtualFileSystemWatcher
  via IFileSystemService", which exposes no watcher; it now says to inject
  `IVirtualFileSystemWatcher` and consume `WatchAsync`. The rule id is unchanged. Both copies of
  `SuppressVfsComplianceAttribute` document the seven rules (`ORKVFS001..007`) and the ratified
  reason categories; a test now refuses a `[SuppressVfsCompliance]` reason in `src/` that does not
  start with `EXCEPTION-BOOTSTRAP`, `EXCEPTION-WATCHER-BRIDGE` or `OUT-OF-SCOPE` (four Studio
  capture reasons had none).
- **`WithOrkeonSetting` takes the full configuration path.** Its documentation and the Aspire test
  now say so: `orkeon-host`'s run timeout is `WithOrkeonSetting("Orkeon:Host:RunTimeout", …)`
  (`ORKEON_Orkeon__Host__RunTimeout`). The test used `"Host:RunTimeout"`, a key the host never
  reads. The method itself is unchanged — `Llm:*` stays at the root.
- **`scripts/sonar-analyze.sh` and `.ps1` run the unit and fast suites only**, with the filter of
  `coverage.yml` (`Category!=Integration&Category!=Slow`). They used to run the whole solution,
  Testcontainers included — Docker and gigabytes of images on a local analysis — and swallow the
  failures; the local coverage figure now has the same scope as the published one
  (`quality-gate.md` §7).
- **Contributors:** the pull-request template asks for `dotnet build Orkeon.sln -warnaserror`, the
  command CI runs; `examples/INDEX.md` lists the six folders it missed (`quickstart/`,
  `crew-multifile/`, `appsettings/`, `aspire/`, `interop/`, `forge/`).
- **Removed** (no caller): `Orkeon.Application.Interfaces.IConsensualProcessStrategy` — nothing
  resolved it; `ConsensualProcessStrategy.ExecuteConsensualAsync` stays on the class, and
  `ProcessStrategyFactory` keeps routing `ProcessType.Consensual` through `IProcessStrategy`;
  `Orkeon.Application.Configuration.ConfigurationVersionMetadata`, used by its tests alone; the
  internal `AggregateEventHelper`.
- **Docs:** the long-form `rag:` example of the YAML schema page (EN and FR) no longer shows
  `provider:`, the key GAP-02 removed.

Migration: a host that resolved `IConsensualProcessStrategy` resolves `ConsensualProcessStrategy`
instead (same scoped instance as before); one that used `ConfigurationVersionMetadata` declares
its own record.

### Changed — `Evaluation:EnableLlmJudge` is read; the `Resilience` section and the dead `RaggableTree` keys are gone **[breaking]**

Three configuration sections were bound, sometimes documented, and acted on nothing (GAP-15).
`"Resilience": { "LlmMaxRetries": 1 }` left LLM calls retrying ten times; `"Evaluation":
{ "EnableLlmJudge": true }` with `AddOrkeonInfrastructure(configuration)` left the judges out of
the suite — and calling `AddOrkeonEvaluation(configuration)` as the docs advised did not help
either (the unconfigured call had already registered a closed `IOptions`) while it registered
every evaluator a second time; `"RaggableTree": { "Exclude": ["vendor"] }` still indexed
`vendor/`, because every `index_codebase` call sets its own exclusions.

- **`Evaluation` is wired.** `AddOrkeonInfrastructure(configuration)` binds the section, and
  `EnableLlmJudge = true` puts the coherence, fluency and groundedness judges in the default
  `IEvaluationSuite`, over the registered `IChatClient` (none registered: resolving the suite
  fails, naming the setting, instead of leaving the judges out). `AddOrkeonEvaluation` is
  idempotent — a second call binds the configuration and registers no evaluator twice. The judges
  no longer appear as `IEvaluator` registrations; the placeholder evaluators that stood in for
  them when disabled are gone. **Removed:** `EvaluationOptions.DefaultRunsPerCase` and
  `RegressionThreshold`, which nothing read.
- **`Resilience` is removed.** `ResilienceOptions` and its binding are gone, with the
  `ResiliencePolicies` helpers no component called — `GetRetryPolicy`, `GetCircuitBreakerPolicy`,
  `GetTimeoutPolicy`, `GetCombinedPolicy`, `GetDatabaseRetryPolicy`. The LLM policy
  (`GetLlmApiPolicy`, budget `Llm:MaxRetries`, timeout `Llm:TimeoutSeconds`) and the Redis policy
  (`GetRedisRetryPolicy`, three attempts) stay.
- **`RaggableTree` carries `Enabled` and `Embedding`, nothing else.** The runner refuses to start
  on any other key of the section, naming it. **Removed** from `RaggableTreeOptions`:
  `Languages`, `Exclude`, `RootAlias`, `IndexMode`, `EnrichWithLlm`, `IncludeStatements`,
  `VectorStore`, `Cache`, with the types `RaggableTreeIndexMode`, `VectorStoreKind`,
  `VectorStoreOptions` and `CacheOptions`. A `Summarizer.Provider` other than `None` now registers
  the LLM summarizer on its own; each call still chooses with `enrich_with_llm`.
- **`index_codebase` declares what the build reads.** `include_statements`, `summarizer_model`,
  `summarizer_max_tokens` and `summarizer_concurrency` are removed from `IndexCodebaseRequest`:
  statements were always extracted, and the summarizer settings were never read. An agent that
  still sends one is not refused — the argument is ignored.
- **Docs:** the configuration reference, the security page's Resilience section, the RaggableTree
  architecture page, the opt-in catalog (EN and FR) and the RaggableTree crew-YAML example say what
  is read; the limitations bullet about the `Resilience` section is gone.

Migration: replace `Resilience:LlmMaxRetries`/`LlmTimeoutSeconds` with `Llm:MaxRetries`/
`Llm:TimeoutSeconds`, and delete the other `Resilience` keys; a host that used the removed
`ResiliencePolicies` helpers builds the same Polly policies itself. Delete
`Evaluation:DefaultRunsPerCase`/`RegressionThreshold`. Move `RaggableTree:Exclude`/`RootAlias`
into the agent's `index_codebase` arguments (`exclude`, `root_alias`) and delete `IndexMode`,
`EnrichWithLlm` and `IncludeStatements` from the section; C# hosts drop the removed
`RaggableTreeOptions` members and set `Summarizer` alone to enable enrichment.

### Removed — the Flows subsystem (`FlowEngine`, flow steps, flow YAML) **[breaking]**

Nothing ran a flow: `orkeon run` treats every YAML file as a crew, and no host, CLI or `.ork.ts`
binding reached `IFlowEngine`. The C# API itself misbehaved — a `crew` step given a crew's id
launched a random one and reported a failed crew as a success, a flow's `timeout_seconds` cut
nothing, a backward `NextStep` looped forever, and `GetFlowMetrics(name)` compared the name with
the id (GAP-05). The executable surfaces already cover the need: a `.ork.ts` script chains crews,
LLM calls, tools and conditions in real code under `orkeon run` (with `--events`), and the `Graph`
process mode runs bounded cycles behind a circuit breaker. The flows are removed rather than
mended.

- **Removed:** the `Orkeon.Domain.Flows` namespace (`IFlow`, `IFlowStep`, `IFlowDefinition`,
  `FlowStep`, `FlowState`, `FlowStepParameters`, `FlowConfigurationSettings`, `FlowResult`,
  `FlowStepResult`, `FlowExecutionResult`, the `[Flow]`/`[Start]`/`[Listen]`/`[Router]` marker
  attributes, the flow event types…), `FlowId`, `FlowStepId` and `FlowEventId`;
  `IFlowEngine` with `FlowValidationResult` and `FlowMetrics`, `IFlowStepExecutor`,
  `FlowDefinitionBuilder`/`FlowStepBuilder` (`Orkeon.Application.Flow`); the whole
  `Orkeon.Infrastructure.Flows` namespace — `FlowEngine`, `DefinitionBasedFlow`,
  `FlowStepExecutor`, `YamlFlowDefinitionLoader`, `InMemoryFlowDefinition`, `FlowStepBase<TInput,
  TOutput>`, the six steps (`CrewFlowStep`, `LlmFlowStep`, `ToolFlowStep`, `ConditionalFlowStep`,
  `HumanInputFlowStep`, `DelayFlowStep`), `FlowExecutionTracker` and `FlowGraphSerializer`;
  `AddOrkeonFlows()` and `AddOrkeonFlowVisualization()`, which `AddOrkeonInfrastructure()` no
  longer calls; `OrkeonApplicationOptions.EnableFlowPersistence` (read by nothing).
- **The `flow` usage operation is gone.** `LlmUsageOperations.Flow` and its Studio mirror
  `RunCostOperations.Flow` are removed: a `cost.updated` reading never carries
  `operation: "flow"`.
- **Examples no longer claim `FlowEngine`.** 26, 29, 59, 61, 76, 83, 90 and 100 said a flow
  engine drove their cycles, and 60, 96 and 98 listed `IFlowEngine` as a key feature; they are
  sequential crews (97 consensual), and their titles, goals, backstories and READMEs now say what
  one run does.
- **Docs:** the Flows page (EN and FR) and its entries in the table of contents, the index, the
  process-types guide, the opt-in catalog and the limitations page are removed.

Migration: a flow becomes a `.ork.ts` script — each step a `crew.run()`, `ctx.llm.*` or tool call,
conditions and loops in plain code — run by `orkeon run`; a cyclic crew uses `process: graph`.
Delete calls to `AddOrkeonFlows()`/`AddOrkeonFlowVisualization()` and any `EnableFlowPersistence`
setting. A cost report grouping by `operation` drops its `flow` bucket.

### Changed — `orkeon-host` connects its MCP servers and routes chat by room; the tool registry ships in `Orkeon` **[breaking]**

The same `MCP:Servers` gave `orkeon run` the servers' tools and `orkeon-host` none; every chat
message reached the first hosted crew; a host built on the `Orkeon` package resolved no YAML tool
name, because the DI-backed registry lived in the unpublished `Orkeon.Hosting`; and the EventHub
tools swallowed `metadata` and returned identifiers they made up (GAP-11).

- **`orkeon-host` connects the `MCP:Servers` at startup**, through the runners' own step, from its
  first hosted service (`McpConnectionService`): the servers' tools are in the registry before the
  channel can deliver a message, and the servers disconnect after the drain. An unreachable server
  costs one error line, and the daemon starts without it.
- **Chat is routed by room.** `Orkeon:Host:Discord:Routes` maps a Discord channel id to a hosted
  crew: a thread opened in that channel starts that crew, and any other thread starts
  `Orkeon:Host:Discord:DefaultCrew` — the first declared crew when unset. One thread is still one
  run. A route to an undeclared crew, a route key that is not a channel id, an unknown
  `DefaultCrew`, or two hosted crews with one name refuse the start (exit 78); a crew no room
  reaches is named by a startup warning.
- **`ToolRegistry` (`Orkeon.Infrastructure.Tools`) is the default `IToolRegistry`** that
  `AddOrkeonInfrastructure()` registers: seeded from every `IBaseTool` in DI, concurrent, a
  duplicate DI name fails its construction with both types named, and `RegisterToolAsync` still
  refuses a name another tool holds. The runners use it as they are; `ServiceProviderToolRegistry`
  (`Orkeon.Hosting`) and the empty `InMemoryToolRegistry` stub are removed.
- **An agent's own tool instance wins** over a registered tool of the same name
  (`TaskToolbelt.Compose`): an agent built in C# with its own `rag_search` calls it, not the
  host's. The registered tools still supply `human_input`.
- **EventHub: nothing given is swallowed, no identifier is invented.** `post_message` and
  `send_request` deliver their `metadata` into the envelope the recipient reads;
  `IEventHub.PostAsync` and `SendAsync` take a `MailboxOptions` (with `Metadata`) before the
  `CancellationToken`. `PublishAsync` and `PostAsync` return the message's `MessageId`, which
  `publish_event`'s `event_id` and `post_message`'s `message_id` now are; `send_request` no longer
  returns a `correlation_id`. The run event bus's `hub.message` gains `messageId?` and
  `metadata?`. `post_message` and `MailboxAddress` document the fourth scheme, `client://`.
- **Removed, never answering:** `IToolRegistry.GetToolsByTagsAsync` and
  `GetToolsByCapabilityAsync` (always empty — no tool carries tags or capabilities), and
  `McpServerOptions.ExposeResources`/`ExposePrompts` (read by nothing; the MCP server exposes tools
  only).

Migration: delete `services.AddSingleton<IToolRegistry, ServiceProviderToolRegistry>()` — and any
loop feeding the registry from `GetServices<IBaseTool>()` — `AddOrkeonInfrastructure()` covers it.
Callers of `IEventHub.PostAsync`/`SendAsync` pass `options: null` (or a `MailboxOptions`) before
the token; an `IEventHub` implementation returns the `MessageId` from `PublishAsync`/`PostAsync`.
Drop `MCP:Server:ExposeResources`/`ExposePrompts` from settings, and any call to the two registry
lookups (filter `GetAllToolsAsync()` yourself). A Discord deployment hosting several crews adds
`Routes` (and, if the first crew should not be the default, `DefaultCrew`).

### Changed — a run dispatches the crew's domain events, and `ICallbackHandler` sees every tool call **[breaking]**

An `IDomainEventHandler<CrewExecutionCompletedEvent>` registered in DI was never called by a
kickoff, the domain callbacks were never invoked, and `ICallbackHandler` received task
notifications only (GAP-06).

- **The end of a kickoff dispatches the crew's domain events** through `IDomainEventDispatcher`
  — on success, failure and cancellation — then empties the aggregate, so a second kickoff never
  dispatches them again. `KickoffForEachAsync`, `KickoffAsyncNoWait` and both paths of
  `KickoffStreamingAsync` go through the same point. A run delivers the construction events still
  queued (`CrewCreatedEvent`, …), then `CrewExecutionStartedEvent`, then
  `CrewExecutionCompletedEvent` or `CrewExecutionFailedEvent`. A handler that throws is logged and
  skipped: the `CrewOutput` and the run's error are unchanged. `SequentialCrewOrchestrator` takes an
  `IDomainEventDispatcher` after its `IExecutionPlanParser`. `DomainEventDispatcher` is now scoped,
  so the scoped handlers resolve from the run's scope rather than the root provider.
- **`ICallbackHandler` receives a step per tool call.** `OnStepStartedAsync` before each call of
  the agent loops (chat-client, native, text, streaming), `OnStepCompletedAsync` after it, with the
  tool's raw result and `Success = false` when the tool failed, threw or was blocked by the
  guardian. `StepNotifyingToolInvocationPipeline` wraps the tool-invocation point;
  `ExecutionOrchestrator.Callbacks` (set by `AddOrkeonApplication()`) and an optional
  `ICallbackOrchestrator` on `StreamingAgentExecutionService` feed it. `ICallbackOrchestrator`
  gains `NotifyStepStartedAsync` / `NotifyStepCompletedAsync`.
- **Removed, never raised:** `AgentSpawnedEvent`, `CrewCompletedEvent`, `TaskBlockedEvent`,
  `TaskUnblockedEvent`, `TaskDelegatedEvent`, `DelegationCompletedEvent` (with its
  `Orkeon.Domain.Delegation.Events.DelegationOutcome`), `DelegationQueuedEvent`,
  `AgentRegisteredForDelegationEvent`, `AgentUnregisteredFromDelegationEvent`,
  `HumanInputRequestedEvent`, and `TaskContextUpdatedEvent` with `TypedTaskContext<T>.GetEvents()` /
  `ClearEvents()`. 33 domain events remain, each raised by its aggregate.
- **Removed, never invoked:** `IStepCallback`, `ITaskCallback`, `IStepProgressHandler` and
  `StepProgressContext`; `Agent.StepCallback`, `Crew.StepCallback`/`TaskCallback`,
  `CrewTaskBase.Callback`, their `*CreateOptions`/`*Snapshot`/`TaskOutputOptions` members and the
  matching parameters of `Agent.Create`, `Crew.Create` and `CrewTask.Create`;
  `AgentBuilder.WithStepCallback`, `CrewBuilder.WithStepCallback`/`WithTaskCallback`,
  `CrewTaskBuilder.WithCallback`; `NullStepCallback`, `NullStepProgressHandler`,
  `NullTaskCallback`. From `ICallbackHandler`: `OnTaskProgressAsync`, `OnFlowStepStartedAsync`,
  `OnFlowStepCompletedAsync` and their `TaskProgressContext`, `FlowStepStartedContext`,
  `FlowStepIdentity`, `FlowStepCompletedContext`. From `ICallbackOrchestrator`: the
  `CallbackHandlers` parameter (always null) and class, `NotifyStepProgressAsync` with
  `StepProgressInfo`; `CallbackOrchestrator.NotifyToolUsedAsync`/`NotifyDelegationAsync`;
  `TaskCallbacks` and `CallbackExamples` (with `CustomWorkflowCallbackHandler`).

Migration: an `IStepCallback` or `ITaskCallback` becomes an `ICallbackHandler` (derive from
`BaseCallbackHandler`) registered in DI — `OnStepStartedAsync`/`OnStepCompletedAsync` per tool
call, `OnTaskStartedAsync`/`OnTaskCompletedAsync` per task — or an `ICrewExecutionHook` for task
and crew outcomes; drop the `With*Callback` builder calls. Code constructing
`SequentialCrewOrchestrator` by hand passes an `IDomainEventDispatcher`. A handler of a removed
event has nothing to handle and is deleted.

### Changed — a task's `tools:` reach its agent; the task state machine and `circuitBreaker:` are gone **[breaking]**

Three task YAML fields were read, validated and dropped, and two families of types had no
consumer (GAP-07).

- **A task's `tools:` add to its agent's tools, for that task only.** They never replace them.
  `CrewFactory` resolves them like an agent's — same registry, same `StrictTools` check, so an
  unknown name now fails the load — and the task carries them (`CrewTask.Tools`,
  `CrewTaskBuilder.WithTool(s)`). One composition, `TaskToolbelt.Compose` (agent ∪ task, one tool
  per name, then `human_input` when the task asks for it), feeds the three agent loops, the
  streaming path, the prompt's tool list and the guardrails' tool rules, so `human_input` now
  reaches the native and text loops too. The `.ork.ts` `taskBuilder().tools([...])` takes the same
  path. `CrewConfigurationMapper` exports a task's tools.
- **`circuitBreaker:` is removed and refused at load**, at the crew root and on a task, with a
  message naming `graphConfig`. The task block configured `TaskExecutionStateMachine`, which no
  execution path ran; its three guard limits were read by nothing; the crew block was read by
  Graph alone, where `graphConfig` carries the same settings. A task is bounded by its agent loop
  (`maxIter`, the stop on identical tool errors, the output-validation retries). Removed:
  `TaskExecutionStateMachine`, `TaskExecutionState`/`TaskExecutionEvent`,
  `TaskExecutionGuardContext`, `CircuitBreakerConfig`, `CircuitBreakerYamlConfig`,
  `Crew.CircuitBreaker`, `CrewBuilder.WithCircuitBreaker`, the `CircuitBreaker` members of
  `CrewConfiguration`/`TaskConfiguration`/`CrewCreateOptions`/`CrewMappingSettings`, and
  `CircuitBreakerPolicyFactory.Resolve`/`CreateTaskFsm`/`CreateGuardContext`;
  `ResolveGraph(graphConfig, fallback)` lost its `crewDefault`. The generic `StateMachine<,>`
  engine and `CircuitBreakerPolicy` stay (forge, Graph, the corrective RAG graph). Example 103,
  which existed for the task FSM, is deleted; 102 covers its scenario.
- **Dead duplicates removed.** `Orkeon.Application.Configuration`'s second configuration model
  (`ImmutableConfigurations.cs`: `CrewConfiguration`, `AgentConfiguration`, `TaskConfiguration`,
  `LlmConfiguration`, `MemoryConfiguration`, `RetryConfiguration`, `ValidationResult`, a
  four-value `ProcessType`, `VerbosityLevel`, `AgentType`, `TaskPriority`) and its builders
  (`ConfigurationBuilder.cs`) had no consumer. `Orkeon.Application.Execution.MemoryType` and
  `Orkeon.Domain.Agent.MemoryType` are gone: `AgentMemory.Type` is
  `Orkeon.Domain.Memory.MemoryType`, the one enum left. `TaskConfiguration.TimeoutSeconds`, which
  nothing set or read, is removed, and `TaskConfiguration.RequiredTools` is renamed `Tools`.
- C#: `CrewTaskBase.RequiredTools`/`AddRequiredTool(ToolId)`, `CrewTaskBuilder.RequiresTool` and
  `CrewTaskSnapshot.RequiredTools` (opaque ids nothing filled) become `Tools`/`AddTool(IBaseTool)`,
  `WithTool`/`WithTools` and `CrewTaskSnapshot.Tools`. `GuardrailsPromptRenderer.AppendAgentAndTaskGuardrails`
  takes the task's toolbelt.

Migration: delete every `circuitBreaker:` block; on a `process: graph` crew, move `preset`,
`maxTransitions`, `maxStateVisits` and `maxTotalDurationSeconds` into `graphConfig`
(`circuitBreakerPreset`, same names for the rest). A crew that listed a tool on a task and also
on its agent to make it work can drop the agent's copy. Code using the removed configuration
builders builds `Orkeon.Domain.Configuration.CrewConfiguration` (or uses `CrewBuilder`).

### Changed — the scripting toolchain: `Orkeon:Scripting:Toolchain` is read, the typings ship with the tool **[breaking]**

Four defects of the DSL tooling (GAP-13), all between what the docs promised and what ran.

- **`Orkeon:Scripting:Toolchain` is read.** No host bound it: `EsbuildPath` was the first place
  the lookup claimed to search (the error message cited it) and `EsbuildTimeout` could not be
  changed. Every host now builds its transpiler with `EsbuildTranspiler.Create(configuration)`
  (`ScriptingToolchainOptions.FromConfiguration`): `orkeon run`, the shared runner, `orkeon
  doctor`, `orkeon forge` (its esbuild probe and its trial) and the REPL's `*.cmd.ts` loader.
  `RunnerSettings.ReadConfiguration(settingsPath)` gives a CLI path the settings file plus the
  `ORKEON_*` variables before a host exists.
- **`orkeon typings` writes the editor typings.** `orkeon.d.ts` (the DSL) is embedded in
  `Orkeon.Scripting` by its build and `orkeon-cli.d.ts` in the `orkeon` tool; the new verb writes
  both into `./.orkeon/` (`--out <dir>`), overwriting what is there. A `dotnet tool install` user
  had neither. The pack item of the non-packable `Orkeon.Scripting` and the
  `orkeon-cli.d.ts` resource of `Orkeon.Cli.Commands.Scripting`, which nothing read, are gone.
  The dotnet tool still does not ship esbuild — the docs and `orkeon doctor` now say so and say
  how to get it (`npm install -g esbuild`, `ORKEON_ESBUILD_PATH` or `EsbuildPath`).
- **`orkeon forge --events` no longer swallows the need.** The option takes a value only when it
  is `jsonl`: `orkeon forge --events veille des prix` forges "veille des prix" (it used to send
  "des prix"). Studio's `--events jsonl` is unchanged.
- **`ctx.command.rawInput` is the line the user typed** (`/deploy prod --force`), not the
  command's name; a `completed` replay reads the line that launched its ticket.
- **`orkeon-cli.d.ts` declares what the runtime serves.** `runCrew` returns `CrewRunOutput`
  (not a `Promise` — `.then` on it threw), `"configuration"` is no longer listed as a service
  key, `services.get("commands")` / `get("script-host")` are typed, and an `args` schema gives
  the handler typed arguments (they were `unknown`). `scripts/check-scripting-typings.sh` now
  typechecks every `examples/**/*.cmd.ts` against it.
- C#: `ScriptServiceKeys.Configuration` (reserved, never served) is removed.

Migration: replace `runCrew(...).then(f)` with `f(runCrew(...))` (an `await` keeps working). A
host that used `ScriptServiceKeys.Configuration` to add a filtered view writes the key
`"configuration"` itself. A script that read the command name in `ctx.command.rawInput` reads
`ctx.command.name`. Run `orkeon typings` instead of copying the `.d.ts` files from a clone.

### Changed — the `.ork.ts` typings and the runtime describe one DSL **[breaking]**

The declarations in `Typings/*.d.ts` and the Jint runtime had drifted apart in three ways
(GAP-12): code typed correctly failed (`err instanceof ReceiveTimeoutError` threw a
`ReferenceError`, `ctx.send("writer", m)` was refused, `agent.role` was `undefined`), code
typed correctly was ignored without a word (`.llm("gpt-4o")`, `{ temperature: 0 }` on a
`ctx.llm` call, `crew.run({ inputs })`, `withTask(b => …)`, `withTaskTool`), and code that
worked was refused by the typechecker (`withResponseFormat`, `withResponseSchema`,
`crew.findById`, `ctx.crew`). `ctx.llm.act` did not offer the agent's `withAutonomousTool`
instances to the model, which example 09 and the guide asked it to call.

- **One honest `llm` surface.** `llm.default_` is the host's provider on its configured model;
  `llm.model(name, overrides?)` is the same provider on another model. The eight per-vendor
  factories (`llm.openai()` … `llm.mammouth()`) are removed: an agent always talks to the host's
  provider, and `llm.anthropic()` on an OpenAI host sent `claude-haiku-4-5` to OpenAI.
  `Orkeon:DefaultLlmProvider`, which only renamed that provider, is removed with them, and so is
  the `llm.default` alias. `LlmConfig.with(...)` applies `model`, `temperature`, `maxTokens` and
  `responseFormat` and refuses any other key (`baseUrl`, which nothing applied, included).
- **Nothing a script passes is dropped any more: it is applied or refused at the call.**
  `.llm(...)` refuses anything but an `LlmConfig`; `withAgent(s)` / `withTask(s)` refuse a
  builder callback or a plain object; `withAutonomousTool(s)` refuses anything but a
  `toolBuilder()` tool.
- **`act` runs the agent's tool instances.** `ctx.llm.act` offers the `withAutonomousTool`
  instances next to the `.tools([...])` built-ins; the loop posts each call to a JS pump in the
  `act` trampoline, which runs the tool's `execute` on the engine's thread and hands the result
  back — through the permission gate and the host's tool-invocation pipeline like any tool.
- **The error classes are real.** `errors.d.ts`'s classes are planted as globals and a host
  error is an instance of its class, with its fields (`agentName`, `timeoutMs`, `dimension`…),
  plus `clrType` and `clr` — including a rejection from `queue.pop`, `ctx.receive` and `act`.
  `BudgetExhaustedException` is declared as `BudgetExhaustedError`.
- **By name.** `ctx.send`, `ctx.delegate`, `crew.remove` and `crew.has` take an agent or its
  name. `ctx.delegate` now runs exactly as `crew.runAgent`: the delegated body gets its own
  context, its semaphore and its `onError` policy (it used to receive no `ctx`).
- **Smaller fixes.** `agent.role`; `ctx.log.info(message, ...args)` writes every argument;
  `withTaskTool` is removed (nothing read it).
- **The typings say what the runtime does**: `ErrorCode` lists the codes the mapper produces
  (`ErrorCodeMapper.CodeToolError`, `CodeLlmError` and `CodeLockTimeout`, never produced, are
  removed), `ErrorContext` is `{ code, message, exception, attempt, agent }`, `LlmCallOptions` is
  `{ responseFormat, llm: { model } }`, `ChatResponse` is `{ content, tokensUsed, model }`,
  `act` resolves to `{ output, iterations }`, `runStream` emits `agent.start` / `agent.stop`,
  `CrewRunOptions` has no `inputs`, `PublishedEvent` has no `publisher` and
  `EventTopicOptions` no `maxHandlers`; `withResponseFormat`, `withResponseSchema`,
  `crew.agents`, `crew.findById`, `ctx.crew`, `ctx.stateWith` and `receive({ timeout })` are
  declared.
- **Two gates.** `TypingsRuntimeParityTests` compares each declared interface with its runtime
  type, member by member; `scripts/check-scripting-typings.sh` now typechecks every `.ork.ts`
  under `examples/`, not only `examples/scripting/`.
- C#: `JsEngineFactory` and `LlmNamespaceBinding.Register` lose their `IConfiguration`
  parameter; `JsCrewBuilder.withAgent`, `JsExecutionContext.delegate`/`send`/`receive` and
  `JsEventQueue.pop` change shape (JS-facing).

Migration: replace `llm.openai({ model: "x" })` (and its siblings) with `llm.model("x")`, and
`llm.default` with `llm.default_`; delete `Orkeon:DefaultLlmProvider` from configuration —
choose the provider with the host's `Llm` section. Replace `.llm("x")` and `.llm({ provider, model })`
with `.llm(llm.model("x"))`. Build agents and tasks before `withAgent` / `withTask`; pass
`withAutonomousTool` a `toolBuilder()` tool and built-in names to `.tools([...])`. Replace
`withTaskTool(t)` with `.tools([t])`. Read `act`'s answer on `.output`; an `onError` handler
reads `err.message` and `err.agent.name` (not `err.error` / `err.agentName`). Catch
`BudgetExhaustedError`, not `BudgetExhaustedException`. A C# host drops the `configuration`
argument of `new JsEngineFactory(...)`.

### Changed — `grammar` reaches only an endpoint configured for it, and Ollama refuses audio **[breaking]**

A `structured_output` deliverable put a GBNF `grammar` field on every request to the
OpenAI-compatible providers — a field no vendor API documents — without a word, while Ollama
sent it on `/api/generate` and silently left it off `/api/chat`. On a cloud provider nothing
constrained the output: the only guard was the JSON parse after the fact. Ollama also dropped
audio and file parts and sent the text alone, where OpenAI and Anthropic refuse them (GAP-14):

- **`grammar` is a capability the configuration switches on.** `LlmProviderCapabilities.GbnfGrammar`
  is declared by no provider; `Llm:Grammar: true` (`LlmConfig.GrammarEnabled`, read by the runner
  host and the REPL) turns it on for a llama.cpp-compatible server — Docker Model Runner,
  `llama-server` — behind the OpenAI-compatible providers or Ollama. Without it, both payload
  builders of `OpenAICompatibleProviderBase`, both Ollama endpoints and Anthropic drop the grammar
  with a structured warning (event id 110) naming the key. Anthropic never takes it.
- **`structured_output` uses `json_schema` on a provider that declares it.** The deliverable's
  schema now also travels as a non-strict `json_schema` response format
  (`LlmChatOptionsKeys.StructuredOutput`); the chat-client adapter sends the grammar to an endpoint
  configured for it, the schema to a provider whose `ResponseFormat` is `JsonSchema`, never both,
  and a response format the crew set itself still wins. `StructuredOutputResolver` keeps parsing
  the answer.
- **Ollama refuses audio and file parts.** `ContentConverter.ToOllamaMessage` throws
  `NotSupportedException`, like the OpenAI and Anthropic converters, and any non-text part routes
  the conversation to `/api/chat` (buffered or streamed), so nothing is flattened away on
  `/api/generate` first.
- Removed: `MultiModalOptions.AutoResizeImages` and `MaxImageDimension` (nothing resized an image)
  and `LlmLoggingExtensions.AddLlmExchangeFileLogging` (no caller, and it registered neither
  `LlmLoggingOptions` nor the options singleton the RaggableTree embedding client reads).
- A provider declares its capabilities by overriding `HttpLlmProviderBase.DeclaredCapabilities`;
  `Capabilities` is no longer virtual — it is the declaration plus the configured grammar switch.

Migration: a setup that relied on `grammar` against a llama.cpp server sets `"Llm": { "Grammar":
true }`. A message with an audio or file part sent to Ollama now fails: send text and images
only. Delete `Orkeon:MultiModal:AutoResizeImages` and `MaxImageDimension` from configuration;
replace `AddLlmExchangeFileLogging(dir)` with `AddLlmExchangeLogging(dir, options)`. A custom
provider deriving from `HttpLlmProviderBase` renames its `Capabilities` override to
`protected override LlmProviderCapabilities DeclaredCapabilities`.

### Fixed — an A2A task runs the agent it names, and the server starts with the host **[breaking]**

An A2A task submitted to an Orkeon server came back `Completed` with
`Task routed to agent 'Researcher' with input: …` — no model call, no tool. The router
matched the request's `skillId` against agent **roles**, substring included (`writer` picked
`Ghostwriter`), while the agent card publishes the agent **id** as the skill id, so a peer that
followed the card got `Failed`. Nothing started the server either: a host had to resolve
`IA2AServer` and call `StartAsync` itself. `A2A:Enabled` and `A2A:RemoteAgents` were bound and
read by nothing (GAP-10):

- **The router runs the agent.** `A2ATaskRouter` picks the agent whose id equals the `skillId`
  exactly — the `id` of a skill on `/.well-known/agent.json` — and runs an ad hoc task built
  from `input` (its `metadata` become the task variables) through `IAgentExecutionService`,
  resolved in the request's scope. `Completed` carries the agent's output, `Failed` its error;
  a host without `AddOrkeonApplication()` gets `Failed` saying so, never a pretend success.
- **Cancellation reaches the agent.** The server serves requests concurrently and runs each task
  under its own token: `DELETE /a2a/tasks/{id}` cancels a task the agent is still working on
  (the submitting request answers `Cancelled`, the record ends `Cancelled`), answers 409 for a
  finished task — whose record keeps its state — and 404 for an unknown one, store or not.
- **The server is hosted.** `AddOrkeonA2A` with `EnableServer` registers a hosted service: a
  generic host starts the server with itself and stops it on shutdown. A start failure (a
  declared scheme without a validator, mutual TLS without a trust anchor) fails the host's
  start. Calling `AddOrkeonA2A` twice registers one server.
- **The client authenticates.** `A2A:Security:ClientAuthScheme` (`Bearer` or `ApiKey`) and
  `ClientCredentialSecretName` make `A2AClient` send `Authorization` on every task call, the
  secret read through `ISecretProvider` (new optional `secretProvider` constructor parameter);
  a credential that cannot be read fails the call before anything is sent.
- Removed: `A2AOptions.Enabled` (calling `AddOrkeonA2A` is the switch) and
  `A2AOptions.RemoteAgents` (no agent tool calls a peer; `IA2AClient` and `IA2AAgentDiscovery`
  remain for C# hosts).

No shipped binary enables A2A yet; exposing it in `orkeon-host` is a follow-up.

Migration: a peer sends the agent's id (the skill `id` from the agent card) as `skillId`, not its
role. A host serving A2A also calls `AddOrkeonApplication()`, and drops its own
`IA2AServer.StartAsync`/`StopAsync` calls when it runs the generic host. Delete `A2A:Enabled`
and `A2A:RemoteAgents` from configuration. A client calling a server that declares
`AllowedAuthSchemes` sets `ClientAuthScheme` and `ClientCredentialSecretName`.

### Security — every agent turn runs through the Guardian, A2A tokens are validated, and no tool takes a key as an argument **[breaking]**

Six security surfaces were registered, configured — and called by nothing. Setting
`Orkeon:Guardian:DefaultPolicy:BlockedTools` did not stop a call; a web page saying *"ignore
previous instructions"* reached the model as it was; `Security:ToolResults` changed nothing;
`Orkeon:Auth:AzureAD` authenticated nothing; the audit trail stayed empty, and the NIST report
read it empty. `image_generation` took the OpenAI key as a call argument, so it travelled
through the conversation, the tool-call log and the usage record. And
`A2A:Security:AllowedAuthSchemes = ["Bearer"]` let `Authorization: Bearer anything` submit a
task (GAP-09):

- **One invocation point for every tool call.** `IToolInvocationPipeline`
  (`ToolInvocationPipeline`, registered by `AddOrkeonApplication()`) is called by the
  chat-client, native, text and streaming loops, the automatic-function-invocation path and
  `ctx.llm.act`. It chains the Guardian's tool phase (`ToolGuard`: path traversal, SSRF
  targets, SQL injection outside the `*_query` tools) — or, for `delegate_work_to_coworker` /
  `ask_question_to_coworker`, the delegation phase (`DelegationGuard`: depth, self-delegation,
  a target already in the chain of synchronous delegations) — then the call, the one
  truncation rule (`AgentDefaults.ResolveMaxToolResultLength`), the result sanitizer, and a
  `ToolExecution` audit event. A blocked call never runs; the model reads
  `Error: Blocked by Guardian (…): <reason>`.
- **The input phase screens the composed prompt** — task, previous outputs, retrieved
  knowledge — before the first provider call. Under the default `Security:Prompt:Policy`,
  now `Block`, a High or Critical pattern fails the task with
  `AgentExitReason.GuardianBlocked` and the pattern named; a lower one is a logged, audited
  warning.
- **Tool results are tagged, never rewritten.** `Security:ToolResults:Policy` (default `Warn`)
  is now read: the result reaches the model between `DATA CONTEXT - NOT INSTRUCTIONS` markers
  and each injection pattern leaves a `SecurityEvent`; `Block` withholds a High/Critical
  result and says so. The `email_*` tools are trusted by default — they screen what they read
  themselves (ADR-012).
- **The Guardian is on by default and never rewrites silently.** `Orkeon:Guardian:Enabled` is
  the real switch. The `Strip` policy, which cut matched phrases out of what the model read,
  is gone: content passes unchanged, tagged, or is refused as a whole. The detection is
  tightened where it blocked honest prompts: *"forget"* followed by anything no longer
  matches (only forgetting the instructions does), *"you are now going to…"* no longer reads
  as an identity override, and the exfiltration phrases are Medium (reported, not blocked).
- **The audit trail is fed**: a `ToolExecution` event per call (never the arguments) and a
  `SecurityEvent` per Guardian block or warning and per injection pattern in a result.
- **A2A credentials are validated.** `Bearer` tokens go to the registered
  `IAuthenticationProvider`s — `AzureAdAuthProvider` (`A2A:Security:AzureAD`, signing keys now
  read from the tenant's OpenID configuration) and `OidcAuthProvider` (`A2A:Security:Oidc`),
  registered by `AddOrkeonA2A(configuration)` for each filled section; `ApiKey` keys are read
  through `ISecretProvider` from `A2A:Security:ApiKeySecretNames` and compared in constant
  time. A rejected credential is a 401; a server that declares a scheme without a validator
  refuses to start. The validation is `A2ACredentialValidator`, public for other inbound
  surfaces.
- **`image_generation` reads `OPENAI_API_KEY` through `ISecretProvider`** (`ORKEON_OPENAI_API_KEY`
  by default); a missing key fails naming the secret. `web_search` sends its Tavily key as an
  `Authorization` header instead of in the request body. No tool takes a secret as an argument
  any more — the rule is in [New tool pattern](docs/tools/new-tool-pattern.md).
- Removed: `OutputGuard` (a second run of the output validation pipeline), `PromptShieldBuilder`
  (a second prompt composer), `GuardianPipeline.AutoKillOnCritical`, `AuthenticationGuard`,
  `AddOrkeonAuth()` and its call in `AddOrkeonInfrastructure()`, `IAuthorizationPolicy` with
  `ClaimsAuthorizationPolicy` and `RoleBasedAuthorizationPolicy`, `ToolAccessGuard` and its
  `ToolAccessPolicyExtensions`, `GuardianPolicy.AllowedTools`/`BlockedTools`/`OutputGuardEnabled`
  (tool access is `ToolAccessPolicy`), `GuardianOptions.AuditEnabled`/`LogViolations`,
  `GuardPhase.Output`, `GuardAction.Modify`, `SanitizationPolicy.Strip`,
  `ToolResultSecurityOptions.MaxToolResultLength`, `A2AServer.IsAuthSchemeAllowed`, the audit
  builders nothing called (`LlmCall`, `FileAccess`, `HttpRequest`, `CrewLifecycle`,
  `LlmCallAuditInfo`), `ImageGenerationRequest.ApiKey`, and Studio's "key given at the call"
  requirement (`image_generation` now lists `ORKEON_OPENAI_API_KEY` among the tool keys).

Migration: move `Orkeon:Auth:AzureAD`/`Orkeon:Auth:OIDC` to `A2A:Security:AzureAD`/`A2A:Security:Oidc`.
Put the OpenAI key of `image_generation` in `ORKEON_OPENAI_API_KEY` (or `Secrets:OPENAI_API_KEY`)
and stop passing `api_key`. Replace `Security:Prompt:Policy`/`Security:ToolResults:Policy = Strip`
with `Warn` or `Block`, and `Security:ToolResults:MaxToolResultLength` with nothing (one rule
bounds results). A server declaring `AllowedAuthSchemes` needs a validator: a bearer section or
an `IAuthenticationProvider`, or `ApiKeySecretNames`. To block a tool, use `ToolAccessPolicy`
rather than `GuardianPolicy.BlockedTools`. `new ImageGenerationTool(fileSystem, …)` takes an
`ISecretProvider` second. `GuardContext.TargetAgentId` is `TargetAgentRole`, `DelegationDepth` is
derived from `DelegationChain`. Where the default `Block` input policy refuses a legitimate task,
set `Security:Prompt:Policy` to `Warn`.

### Changed — memory providers connect from one host section each, and Redis works on first use **[breaking]**

No path to a remote memory provider produced one that worked. A crew with
`memoryProvider: "Redis"` (seven examples) failed its first successful task: storing the result
threw *"Redis provider not initialized. Call InitializeAsync first."*, and the task was reported
failed. `Memory:Provider = redis` and `AddOrkeonRedisMemory` did the same, and so did
`Orkeon:Rag:Provider = redis`. A crew's `memoryProvider:` carried the type alone, so `"SQLite"`
(nine examples) was a `:memory:` database lost at the end of the run and `"ChromaDb"` targeted
`localhost:8000`. The `Orkeon:ChromaDb`/`Orkeon:Pinecone` sections reached two concrete
singletons nothing resolved, never the memory. Pinecone built an index host of the legacy
`{index}-{environment}` form that no index has (GAP-08):

- **One host section per provider, and a type everywhere else.** `Orkeon:Redis`
  (`ConnectionString`, `KeyPrefix`), `Orkeon:Sqlite`, `Orkeon:ChromaDb`, `Orkeon:Pinecone` and
  `Orkeon:LanceDb` are bound by `AddOrkeonInfrastructure()`. `Memory:Provider`, a crew's
  `memoryProvider:` and `Orkeon:Rag:Provider` only name the type; the connection is that
  provider's section, whichever path selects it. Secrets stay out of crew files.
- **Redis connects on first use.** `RedisMemoryProvider` takes `RedisMemoryOptions` and opens
  its one `ConnectionMultiplexer` at the first call, safely under concurrent callers; an
  unreachable server is a `RedisConnectionException` on that call, retried on the next.
  `AddOrkeonRedisMemory` binds `Orkeon:Redis`. The connection log shows the endpoints, no
  longer the connection string (which may hold a password).
- **One instance per type, owned by the factory.** `IMemoryProviderFactory.GetProvider(type)`
  hands every caller asking for a type the same provider — the application-wide one, every crew
  of that type, the RAG store (both of its resolutions) — and disposes it with the container.
  Two crews no longer open two connections, and a released crew no longer leaks one. Clearing a
  crew's provider-backed memory deletes only the entries it stored. `SupportedTypes` lists the
  aliases; the RAG store validates `Orkeon:Rag:Provider` against it instead of a copy.
- **ChromaDB, Pinecone and LanceDB take their client from `IHttpClientFactory`** (a named client
  per provider). `AddOrkeonChromaDb`/`AddOrkeonPinecone`/`AddOrkeonLanceDb` expose the shared
  instance by its class.
- **Pinecone reaches the real index host**: `Orkeon:Pinecone:Host` when set, otherwise the
  `host` returned by one `describe_index` call (`GET https://api.pinecone.io/indexes/{IndexName}`)
  made on first use.
- Removed: `Memory:ConnectionString`; `Orkeon:Rag:ConnectionString` and
  `Orkeon:Rag:ProviderOptions` (`RagStoreOptions` keeps `Provider`), and the Studio field that
  edited the former; `PineconeOptions.Environment`; `IMemoryProviderFactory.Create` and
  `CreateAndInitializeAsync`, `MemoryProviderConfigDto` and `MemoryProviderConfigDefaults`;
  `MemoryProviderBase.InitializeAsync`, `Configuration` and `ValidateConfiguration`, and the
  Domain `MemoryProviderConfig` they carried (its `RetentionPeriod` and `MaxItems` were read by
  nothing). `MemoryService` no longer takes an `ILoggerFactory`.

Migration: move `Memory:ConnectionString` to the section of the provider it was for
(`Orkeon:Redis:ConnectionString`, `Orkeon:Sqlite:ConnectionString`, `Orkeon:ChromaDb:BaseUrl`,
`Orkeon:LanceDb:Endpoint`), and `Orkeon:Rag:ConnectionString`/`ProviderOptions:*` likewise. Replace
`Orkeon:Pinecone:Environment` with `Host` (the host the Pinecone console shows), or drop it and let
the provider look it up. Remove every `InitializeAsync` call on a memory provider.
`factory.Create(new MemoryProviderConfigDto(type, …))` becomes `factory.GetProvider(type)` — and
the caller no longer disposes the result. `new RedisMemoryProvider(logger)` becomes
`new RedisMemoryProvider(Options.Create(new RedisMemoryOptions { … }), logger)`;
`new MemoryProviderFactory(fileSystem)` takes an `IHttpClientFactory` and, optionally, the
`MemoryProviderSettings` and an `ILoggerFactory`.

### Changed — RAG runs under `orkeon run crew.yaml`, and knowledge attachments use their profile **[breaking]**

A YAML crew's `rag:` and `knowledge:` blocks did nothing under `orkeon run`: loading logged
*"declares a rag: block but the RAG subsystem is not registered"*, ingested nothing, and the
agent answered without a single excerpt — nothing said so. The same held for a crew
directory, `--events`, a declarative `.ork.ts` crew and `orkeon-host`. Even where the
subsystem was registered, an attachment's `profile`, `rag.defaults.profile`, `rag.provider`
and `Orkeon:Rag:Collection` were read and then ignored (GAP-02):

- **Every runner host registers the RAG subsystem and its tools.** `RunnerHost` calls
  `AddOrkeonRag` + `AddOrkeonRagTools`, so `orkeon run` in all its forms and `orkeon-host`
  ingest a crew's `rag:` collections when it loads, inject its agents' `knowledge:` into their
  prompts, and let an agent list `rag_search`, `rag_ingest` or `rag_eval` in `tools:`. The CLI
  adds the ONNX reranker on every path (`balanced`/`quality`), not only for scripts.
  `--validate` still ingests nothing. The `txt_search`/`mdx_search`/`pdf_search`/
  `directory_search` tools, which failed at every call in the runners for want of the
  subsystem, now search.
- **Nothing is resolved until a crew uses it.** The bootstrapper, the knowledge augmenter, the
  three tools and the ephemeral search engine resolve the store, its provider and the
  embeddings at their first use: a crew without `rag:`/`knowledge:` loads no model for them,
  and an unknown `Orkeon:Rag:Provider` fails the first ingestion or retrieval, no longer every
  crew.
- **An attachment retrieves through its profile**: its `profile`, else `rag.defaults.profile`
  (copied onto it at crew load), else `Orkeon:Rag:Profile`. The augmenter runs that pipeline's
  retrieval half (`IRagRetrievalCapable`) — hybrid and rerank as the profile says, never a
  nested generation. `corrective` and `adaptive` cannot retrieve alone and fail the task with a
  message saying so. `min_score` now filters on the score the profile reports (the
  cross-encoder's under a reranking profile).
- **`Orkeon:Rag:Collection`** is the collection `rag_search` queries when the agent names
  none (else `default`), and the one `rag_eval` evaluates for a dataset that names no
  collection and brings no corpus (`RagEvalRunRequest.DefaultCollection`) — a corpus is never
  ingested into it.
- **An agent with `knowledge:` on a host without the subsystem** gets a load-time warning, as a
  `rag:` block already did.
- **`rag.provider` is removed** (`RagCrewConfig.Provider`, `RagYamlConfig.Provider`): the store
  is the host's (`Orkeon:Rag:Provider`). A crew that still writes it gets a warning at load.
- `KnowledgeContextAugmenter` is built over an `IRagProfileResolver` and a default profile,
  and `Citation.Content` carries the full cited passage. `AddOrkeonRagTools` is idempotent.

Migration: delete `rag.provider` from your crews and set `Orkeon:Rag:Provider` in the settings
instead. A host that called `AddOrkeonRag`/`AddOrkeonRagTools` in a `RunnerHost`
`configureServices` can drop the calls; a double registered there still wins (last
registration). `new KnowledgeContextAugmenter(store, embeddings)` becomes
`new KnowledgeContextAugmenter(profileResolver, defaultProfile)`. A crew whose attachments (or
`Orkeon:Rag:Profile`) name `corrective` or `adaptive` must pick `fast`, `balanced` or
`quality` for its knowledge.

### Changed — the consensual vote weighs the answers **[breaking]**

A consensual crew's vote did not read what its agents wrote: every agent cast one ballot for
itself. Three agents that gave the same answer never reached `Majority`, `SuperMajority` or
`Unanimity`; `BordaCount` handed the task to the first agent declared, failed or not; and the
`AcceptBestScore` and `ManagerDecision` fallbacks both re-ran the first agent, under a crew id
that did not exist. The vote is now a vote on the answers (GAP-04):

- **Peer ballots**: once every agent has answered, every agent ranks the other agents'
  answers, anonymised under labels `A`, `B`, … shuffled per task and round. It casts its
  ballot through its own execution (its LLM configuration, its tokens counted), as a JSON
  object. A reply that cannot be read counts as an abstention and never fails the task. The
  port is `IBallotCollector` (`Orkeon.Application.Interfaces`, default
  `AgentBallotCollector`); a host can register its own.
- **No vote for oneself**, and a share is counted among the ballots that could name the answer
  (`Vote.OwnChoice`): three agents that agree reach every consensus type in round one. A tie
  at the top is no consensus, so two agents, who can only name each other, never decide by
  vote.
- **A failed execution is never a candidate.** A lone successful answer is kept without a
  ballot; a round where every execution failed fails the task with the agents' error, without
  another round.
- **`AcceptBestScore`** keeps the last count's leader and re-runs nothing. **`ManagerDecision`**
  asks the crew's manager agent (`managerAgent`, now kept on consensual crews) to rank the
  last round's answers; a crew without one is refused at kickoff, before any agent runs. A
  declared manager neither answers nor votes.
- **`QuorumPercent` and `AllowAbstention` are enforced**: the quorum is the share of expressed
  ballots; with `AllowAbstention: false` an abstention counts against every answer. The dead
  copy `VotingOptions.MaxVotingRounds` is removed — `Orkeon:Consensus:MaxVotingRounds` is the
  one that counts.
- **The crew's input variables reach every execution and ballot**;
  `IConsensualProcessStrategy.ExecuteConsensualAsync` takes them.
- Cost: a round is N executions + N ballots; a task that agrees in round one costs 2N calls,
  one that never agrees `MaxVotingRounds` × 2N (+ one manager ballot).

Migration: remove `Orkeon:Consensus:VotingOptions:MaxVotingRounds` from your settings. With
`FallbackStrategy: ManagerDecision`, declare a `managerAgent`. A consensual crew of two
agents now always reaches the fallback: give it a third. `new ConsensualProcessStrategy(...)`
takes an `IBallotCollector` after the voting strategy, and a positional `CancellationToken`
passed to `ExecuteConsensualAsync` is now named (`ct:`).

### Changed — a failed task fails the crew in all six modes **[breaking]**

Sequential was the only mode that failed a crew with a failed task (STUDIO-12 C5a, LLM-11).
The five others reported success whatever their tasks did, so `orkeon run` exited 0, the
`run.finished` event said `success: true` and `AUTO_SUMMARY.md` said *completed* over a
half-wrong deliverable. The rule is now one type shared by the six strategies:

- **A failed task fails the crew**: `CrewOutput.Success` is `false`, `Error` names every task
  that did not succeed and why, the execution hook hears `Failed` with that same reason, and
  `orkeon run` exits **2** with it as the last stderr line. The outputs and the tokens the
  crew did produce are kept.
- **A task whose dependency failed is skipped**, as in Sequential: no agent runs it on a
  context that says `Task failed: …`, it shows `⊘ skipped`, and it counts as a failure. The
  tasks that do not depend on it still run.
- **Hierarchical**: a task the manager rejected three times (`[NEEDS REVISION]`), a worker
  that failed, and a task assigned to an agent the crew does not carry each fail the crew.
- **Parallel**: a failed task no longer feeds its failure message to the next wave; its
  dependants are skipped.
- **Graph**: a task still failing after its retries fails the crew — only the circuit breaker
  did. A failed task is now retried **before the next task runs** (it waited for the end of
  the queue), so a dependant only sees a dependency that succeeded or gave up.
- **Autonomous**: an exhausted budget fails the crew, its error opening with
  `Execution budget exhausted: <dimension>` and naming every task it never reached; the hook
  hears `Failed`, no longer `Canceled`, which stays for an actual cancellation. A failed task
  with no peer to delegate to fails the crew instead of throwing `No delegation candidates
  available.` out of the strategy, and no longer spends a delegation level first.
- **Consensual**: a retained result that failed fails the crew. The `Fail` fallback fails its
  task and skips its dependants instead of stopping the crew on the spot; the error still
  reads `Consensus could not be reached for task …`.
- An agent that fails without a word (no final answer) no longer makes the hierarchical,
  autonomous and consensual modes throw `info.RawOutput cannot be empty`: its output reads
  `Task failed: <cause>`, as in the other three.

Migration: a pipeline that read `Success` (or the exit code) and then inspected each task's
result keeps working; one that relied on these modes always reporting success must treat
exit code 2, or `Success == false`, as the run's failure.

### Changed — Graph sizes its circuit breaker from the crew

The Graph mode took its bounds from the Strict preset: five visits of the task node, so a
healthy crew of six tasks was cut short. Without an explicit `graphConfig.maxStateVisits` /
`maxTransitions`, they are now computed from the crew — `tasks × (1 + maxRetryCycles)` visits
and twice that plus one transitions — so the breaker still stops a real loop without capping
the size of a crew. An explicit value wins. `MaxTotalDuration` keeps the preset's (10 minutes
under Strict), a cost bound `maxTotalDurationSeconds` overrides.
`GraphProcessStrategy.CircuitPolicy` is now nullable and null by default (computed); a C#
caller that sets it keeps a fixed policy.

| Before | After |
|---|---|
| a graph crew with more than five task attempts: `maxStateVisits` to raise by hand | nothing to declare |
| `strategy.CircuitPolicy` (`Strict` by default) | `null` by default: bounds computed from the crew |

### Fixed — MCP and `rag_*` tools reach the crew agents that name them; an MCP tool no longer replaces a built-in

- **A crew agent receives every tool the registry resolves.** `CrewFactory` kept a resolved
  tool only when it implemented the empty `ITool` marker, which the three RAG tools and
  `McpToolAdapter` did not. A crew listing `rag_search` (in a host that calls
  `AddOrkeonRagTools()`) or a tool of a connected MCP server failed to load under
  `StrictTools` with an `unknown tool(s)` line whose *available tools* named that same tool,
  and lost the tool silently without it. `AgentMapper.CreateFromRequest` and
  `CrewConfigurationMapper.ToDomainCrew` dropped such tools without a word. All three now
  attach any `IBaseTool`.
- **A tool name belongs to one tool.** `IToolRegistry.RegisterToolAsync` returns `false` when
  another instance already holds the name, and leaves that tool in place; registering the
  same instance again is an idempotent success. Both registries follow the contract:
  `ServiceProviderToolRegistry` and `InMemoryToolRegistry` used to overwrite.
- **An MCP tool named like a registered tool is refused.** Before, a server exposing
  `file_read` replaced the built-in silently, and disconnecting the server unregistered the
  name, so the built-in was gone for the rest of the process. Now the MCP tool is not
  registered, one error line names the server and the tool, on the log and on stderr
  (`McpServerStatus.RejectedToolNames` lists them), the rest of the server's tools are
  registered, and the built-in survives the disconnection. Two servers exposing one name
  behave the same way: the first keeps it. Both `McpToolProvider.ConnectServerAsync` overloads
  now log alike.
- **Two DI tools with one name stop the host with an error that names them.**
  `ServiceProviderToolRegistry` threw a bare "An item with the same key has already been
  added"; the `InvalidOperationException` now names the tool and both types.
- The refusal of a `.ork.ts` script tool that shadows a registered tool is unchanged; it now
  rests on the registry's answer rather than on a lookup made beforehand.

### Changed — `ITool` is removed; `IBaseTool` is the one tool contract **[breaking]**

`Orkeon.Domain.Common.ITool` was an empty interface over `IBaseTool`, and the filter on it is
what kept the tools above from the agents. It is deleted, with no shim, and every signature
that took or returned it takes or returns `IBaseTool`: `Agent.Tools`, `Agent.AddTool`,
`Agent.Create`, `AgentBuilder.WithTool`/`WithTools`, `AgentCreateOptions.Tools`,
`AgentSnapshot.Tools`, `AgentSpawnRequest` and its builder, and
`IToolDecorator.Decorate`. `IToolRegistry.GetToolsAsync(IEnumerable<ITool>)`, which nothing
called, is removed with its two implementations.

| Before | After |
|---|---|
| `ITool` (`Orkeon.Domain.Common`) | `IBaseTool` (`Orkeon.Domain.Tools`) |
| `registry.GetToolsAsync(tools)` | `GetToolByNameAsync(name)` for each name |

### Added — e-mail is a chapter of its own, with a tutorial

- **A new getting-started tutorial, *Give your agents a mailbox*, EN and FR**
  (`docs/getting-started/give-your-agents-a-mailbox.md`). It walks through the
  `examples/scripting/13-email-triage` example: a Gmail app password, the `Orkeon:Tools:Email`
  block, `orkeon email accounts` and `check`, the run, then how to adapt it (folders, the Send
  right behind `Send:AllowedRecipients`, a YAML crew instead of a script), with links into the
  guide for Outlook.com through Microsoft Graph, Gmail OAuth2 and a server of your own.
- **The e-mail guide is no longer one line among the Guides.**
  - An *E-mail* section in the documentation index, EN and FR, holds the tutorial, the guide,
    the `orkeon email` commands and ADR-012, with a reading path of its own.
  - A top-level *E-mail* node sits in the site navigation.
  - The site home page, the README's documentation table, the overview, *Run your first
    example*, the tool inventory and the examples catalog point to the tutorial.
- **`13-email-triage.appsettings.json` raises `Orkeon:Scripting:Limits:ExecutionTimeout` to ten
  minutes.** A script's default is 30 s of wall-clock time for the whole run, awaited IMAP
  reads and model calls included, and one triage takes longer. The guide's troubleshooting
  section says so too.

### Fixed — the documentation matches the code again

Every page under `docs/` and the root community pages were reviewed against the code, topic by
topic, EN and FR together.

- **New pages:**
  - *Flows (FlowEngine)* (`docs/orchestration/flows.md`). It documents the C# flow engine and
    says plainly that no CLI, host or script runs a flow yet.
  - *Microsoft Agent Framework interop* (`docs/reference/agent-framework-interop.md`). It gives
    usage for both directions of the bridge, which only ADR-010 described.
  - `docs/reference/hosting.md` gains the .NET Aspire integration (`AddOrkeonHost`,
    `AddOrkeonCrewRun`) and a telemetry section.
- **Seventeen `AddOrkeon*` extensions that no page named are documented**, each where a reader
  looks for it:
  - Auth, Guardian, YAML, flows, flow visualization, training, memory migration, consensus;
  - the six RAG building blocks that `AddOrkeonRag` already calls;
  - the CLI and Terminal.Gui REPL hosts;
  - the Agent Framework bridge.
- **What ships but is not wired is said, not implied.** Known limitations are now on the pages
  and in `docs/reference/limitations.md`:
  - `rag_*` and MCP tools cannot be attached to a crew agent: they implement only `IBaseTool`.
  - `orkeon run crew.yaml` registers no RAG, so `rag:` and `knowledge:` are inert there.
  - The task FSM and a task-level `circuitBreaker:` are not applied at run time.
  - The shipped A2A router does not run the agent.
  - `orkeon-host` connects no MCP server.
  - 10 of the 44 domain events are never raised, and those raised during a kickoff are never
    dispatched.
- **Orchestration pages rewritten to what the strategies do:**
  - Graph has three breaker mechanisms, not four, and its YAML mode has a fixed topology.
  - Autonomous: the manager assigns tasks, a failed task goes to a peer, and spawning is
    provided by the host.
  - Parallel runs in dependency waves.
  - Hierarchical and Consensual behave as documented, with their caveats.
  - The ProcessType blueprint lists the steps a seventh mode really takes; it was a copy of the
    FSM guide.
- **Reference pages completed:**
  - The CLI reference covers every command, option and exit code, and the `orkeon-repl`
    options.
  - The configuration reference gives the real keys of about 45 sections.
  - The `.ork.ts` reference has a corrected per-shape table and twelve more known gaps.
  - The run-event protocol table matches `RunEventKinds`.
  - The LLM pages cover provider resolution, dialect seams, decorators and exchange logging,
    `json_schema`, and vision on every provider.
- **Samples that did not work are fixed:**
  - The README TypeScript crew ended with `crew.run()`, which ignores the tasks.
  - The bootstrap host could not load a YAML crew (no model, no VFS, an empty tool registry).
  - The tool inventory's call examples used parameter names the tools do not take.
  - Loader and porting samples used disk paths instead of virtual paths.
  - A test sample in CONTRIBUTING did not compile.
- **Smaller fixes:**
  - `index.md` said 14 providers, not 16.
  - `studio.md` EN and FR are reconciled.
  - The publication matrix's discontinued list, the SBOM verification command, and dated notes
    on ADR-002/005/006/008/009 are corrected.
- **Examples whose comments promised what the code does not do are corrected:**
  - `rag/crew-yaml`: the stock CLI only validates it.
  - `scripting/09-tools-and-act`: `act` does not see `withAutonomousTool` instances.
  - The `cli-ts-commands` transcripts gain their leading `/`.
  - `97-multi-party-negotiation` does ship one reference file under `data/`.

### Added — a dev channel: the latest `main` is installable between two tags

- **`publish.yml` gains a `publish-dev` job.** Once CI is green on a push to `main`, every
  packable is packed as `<props version>.dev.<CI run number>` — `1.0.1-dev.<n>` after a
  stable `1.0.0`, which `1.0.0-dev.<n>` would sort below — and pushed to **GitHub Packages
  only**: never NuGet.org, which cannot delete a version. The new
  `scripts/prune-dev-packages.sh` then deletes the older dev builds, so the feed keeps each
  tagged release plus the latest `main`. Tagged versions are never touched and dev builds
  are not attested. `dotnet tool install -g Orkeon.Scripting.Cli --prerelease` against that
  feed installs the latest `main`; the steps are in *Update Orkeon*
  (`docs/getting-started/three-ways-to-run-orkeon.md`), the mechanism in
  `docs/reference/publication-matrix.md`.
- **`scripts/test-prune-dev-packages.sh`** proves the retention rules in CI against a fake
  `gh` — no network, no token: tags are never deleted, nor the kept build, a newer one or a
  package's last version; a 404 from an overlapping run is not a failure, a 403 is.
- **The `Orkeon.ConsoleApp` and `Orkeon.Generators` READMEs pin a release with `--version`.**
  On the GitHub feed, `--prerelease` now resolves the dev channel.
- **An *Update Orkeon* section in *Three ways to run Orkeon*, EN and FR.** The update of
  every channel in one table, then the dev channel step by step: the token, the feed in bash
  and PowerShell, install then update, `orkeon --version` to check, the `PATH` and Studio
  traps when a package already installed `orkeon`, floating versions in a project, and the
  way back to the releases. The README, `index.md`, the documentation index and the CLI
  reference point to it. The publication matrix and the Homebrew formula no longer say that
  `orkeon --version` exits `1`: it prints the version and exits `0`.

### Added — native e-mail tools: IMAP, POP3, SMTP and Microsoft Graph, Gmail and Outlook presets, OAuth2 sign-in (MAIL-01..06)

- **A new tool family, `Orkeon.Tools.Email`** — the eighth of the `Orkeon.Tools` umbrella, and
  the second motivated exception to the scope freeze, decided by the owner on 2026-09-26 (an
  `.ork.ts` script cannot open a socket, and a plugin would not put e-mail in Orkeon itself;
  [ADR-012](docs/adr/ADR-012-email-tool-family.md)). Thirteen tools: `email_accounts`,
  `email_folders`, `email_search`, `email_read`, `email_save_attachment`,
  `email_create_folder`, `email_rename_folder`, `email_move`, `email_mark`, `email_delete`,
  `email_draft`, `email_send`, and `email_parser`, rebuilt (below). Built-in tool classes go
  from 79 to 91.
- **Three backends, one message model.** MailKit 4.18.0 and MimeKit 4.18.1 (MIT) for IMAP,
  POP3 and SMTP; Microsoft Graph over plain HTTP — no SDK — for Outlook.com, Hotmail and
  Microsoft 365, read and written as MIME, with ids that survive a move. Presets: `Gmail`
  (`imap.gmail.com:993` or `pop.gmail.com:995`, `smtp.gmail.com:465`), `Outlook` (Graph by
  default; an IMAP/POP3 + SMTP variant exists and inherits Microsoft's consumer IMAP OAuth
  regression of 2026-09-24), `Custom` (explicit hosts, password only). TLS only; `None` towards
  a loopback test server only.
- **Configuration `Orkeon:Tools:Email`**, validated at first use with every problem reported at
  once — a value the binder cannot even read sets its one account aside instead of failing the
  host: `DefaultAccount`, `CredentialsDirectory`, `Screening:WithholdRejected`, and per account
  `Provider`, `Address`, `DisplayName`, a **mandatory** `Rights` list (`Read`, `Organize`,
  `Draft`, `Send`, `Delete`, `Purge`), `Incoming`, `Outgoing`, `Auth`, `Send`, `TimeoutSeconds`,
  `SaveSentCopy`. Secrets are only ever the **names** of environment variables.
- **Guard rails that do not rely on the model.** An agent names an account, never a server or
  a credential. Sending fails closed: `Send:AllowedRecipients` (address, `*@domain`, `*`) empty
  means nobody; the SMTP envelope is the checked list, passed explicitly; `From` is forced;
  `MaxRecipients` / `MaxPerHour` cap the volume; `email_draft` is the human-review path. Every
  read result opens with an untrusted-content notice and carries the verdict of the RAG
  prompt-injection detector, run on the rendered text; HTML-hidden text is left out and flagged.
  Each tool declares its `ToolAccess`, and `orkeon forge` keeps the twelve mailbox tools out of
  forged crews. Results fit the agent loop's 4000-character cap: a search page is cut after a
  whole message and a body slice where its rendering ends, and `next_cursor` / `next_offset`
  resume exactly there.
- **OAuth2, written by hand, and `orkeon email`.** Device code for Microsoft, authorization code
  with PKCE on a `127.0.0.1` listener for Google — with a paste-the-address fallback for WSL,
  containers and SSH — and refresh with rotation; no MSAL, no Google SDK. The new verb
  `orkeon email accounts | login | logout | check` lists the accounts and their readiness
  without network, signs an account in, forgets its tokens, checks a connection. The tools never
  sign in themselves.
- **Tokens in a new internal VFS root, `/credentials`** (`RunnerVirtualRoots.Credentials`),
  mounted by the runner host only when an OAuth account is declared and written through
  `PrivilegedFileSystemAccess`; physically `<per-user settings directory>/credentials/email/`,
  or the `email` subdirectory of `CredentialsDirectory` for a service, owner-only on Unix. A user
  mount claiming `/credentials` is refused by every command, whatever the accounts. The files are
  plain JSON — out of reach of the VFS tools, not of a shell or code tool running as the same
  user.
- **Everywhere a tool family shows up.** `AddOrkeonEmailTools(configuration)` is called by the
  runner host (`orkeon run`, scripts, `orkeon-host`) and by `orkeon-repl`, where OAuth accounts
  are refused for want of a token store; the `Orkeon.Tools` umbrella embeds the assembly; Studio
  lists the family, twelve of its tools with a new "e-mail account" requirement, in its five
  languages; the scripting typings declare `tools.email*` — arguments and results keep the
  tools' own snake_case keys (`unread_only`, `reply_to_id`, `new_name`) — with
  `examples/scripting/13-email-triage.ork.ts`. Documentation: the
  [e-mail guide](docs/guides/email.md), the tool inventory, security, configuration, CLI,
  limitations and VFS pages, ADR-012 and the `SECURITY.md` threat model, EN and FR.
  `THIRD-PARTY-NOTICES.md` gains MailKit, MimeKit and BouncyCastle.Cryptography, now
  redistributed inside the `orkeon` and `orkeon-repl` tools and the installers.
- **Campaign-pending.** Nothing has run against a real Gmail or Hotmail account yet: the live
  campaign is the owner's (MAIL-07).

### Changed — `email_parser` moves to `Orkeon.Tools.Email`, rebuilt on MimeKit **[breaking]**

- **Registration.** `AddOrkeonFileSystemTools()` no longer registers `email_parser`;
  `AddOrkeonEmailTools(configuration)` does, and both shipped runners call it — a crew listing
  `email_parser` under `orkeon run` or `orkeon-repl` needs no change. A host of your own adds
  the call. The public types `Orkeon.Tools.FileSystem.EmailParserTool`, `EmailParserRequest`,
  `EmailParserResponse` and `EmailAttachmentInfo` are removed, no shim.
- **Parameters.** `path` (the virtual path of the `.eml` file), `offset` and `max_chars` (the
  body comes in slices of 200 to 3000 characters, 2500 by default; `next_offset` continues).
  `extract_attachments` and `parse_html` are gone: attachments are always listed and HTML is
  always rendered as text.
- **Output.** The shape of `email_read`: `notice`, `security` (the prompt-injection verdict),
  `message_id`, `from`, `reply_to`, `to`, `cc`, `date`, `subject`, `attachments` (`index`,
  `file_name`, `content_type`, `size_bytes`, `inline`), `text_offset`, `text_length`,
  `next_offset`, `text` — with `folder` set to the file's path. Migration: read `text` where
  `body` was; `headers` and `priority` have no successor; an attachment's `filename` becomes
  `file_name` and its `encoded_size` / `estimated_size` become one `size_bytes`.
- **`.msg` is no longer supported.** It was never parsed — the old tool returned a placeholder
  for it. Save an Outlook message as `.eml` before handing it to `email_parser`.

### Fixed — a whole number from an `.ork.ts` script passes an integer tool parameter (MAIL-05)

- **Scripts could not pass an integer to a typed tool.** Every number a script passes reaches
  the tool as a `double` — Jint maps each JavaScript number to `System.Double` — and
  `ToolParameterValidator` accepted only `int` and `long` for an `integer` parameter, so
  `tools.emailSearch({ limit: 20 })`, like a script call to any typed tool with an integer
  parameter, was refused with "invalid type. Expected: integer". A finite whole number now
  passes whatever carries it — a `double`, a `float`, a `decimal` or a smaller integer type —
  which is JSON Schema's own definition of an integer; the typed deserializer already turned
  `20.0` into `20`. Found while typing the e-mail tools for scripts.

### Fixed — the third-party notices cover HtmlAgilityPack and read the AngleSharp version actually pinned

- **HtmlAgilityPack gets its entry** (section 11). The parser behind the RAG HTML loader,
  `web_scrape`, the infrastructure's HTML parsing and the e-mail family's text rendering rides
  inside the `orkeon` and `orkeon-repl` tools and the installers, which is the file's own
  criterion (a), yet had no notice. The package ships no license text and the upstream
  `LICENSE` carries no copyright line: the notice is the package's own
  (`Copyright © ZZZ Projects Inc.`), with the MIT text reproduced verbatim from upstream.
- **AngleSharp reads `1.8.2`**, the version `Directory.Packages.props` pins, instead of the
  `1.7.2` it replaced. Its license text is re-sourced from the commit the 1.8.2 package was
  built from (unchanged), and its dependency note is re-checked against that nuspec.

### Fixed — Studio finds the CLI it was installed with on Windows

- **The zip and MSI installs no longer leave Studio without an engine.** Studio lives in
  `libexec\orkeon-studio\` and the CLI in `libexec\orkeon\orkeon.exe`, a directory the lookup never
  probed; `bin\` on `PATH` holds only `.cmd` wrappers, which the Windows lookup (`orkeon.exe` only)
  rejects. Studio therefore reported « the orkeon command-line tool was not located », or silently
  drove an older `orkeon.exe` found on `PATH` (a global dotnet tool). `OrkeonBinaryLocator` now probes
  the `libexec/orkeon` sibling right after the install directories.

### Added — My teams stays readable with many teams: search, order, the Archives view, an undo banner, the archive suggestion (STUDIO-32)

- **A view over the cards, in memory.** The screen gains:
  - a search on the name and the need: every word typed must start a word of them, accents and case
    aside — the gallery's rule;
  - an order, by last activity (the default) or by name;
  - an « Archives (N) » toggle, which closes by itself once the last archive leaves.

  None of these reads the disk again. The sidebar counts the active teams whatever the screen shows
  (`TeamsViewModel.ActiveCount`, formerly `Count`). The empty screen now tells apart no team at all,
  every team archived (« Open the archives ») and a search that found nothing.
- **« Archive » asks no question.** It is a labelled button in novice mode and an icon in expert mode.
  An undo banner then holds the top of the screen for a few seconds, on a timer of its own
  (`StudioServices.UndoDelay`). An archived card offers only « Restore » and « Delete ». Undoing
  « Stop the schedule and archive » says so on the banner and brings the team back without its
  schedule: the card shows « Schedule not installed » with « Install the schedule », and nothing is
  reinstalled.
- **The archive suggestion.** A banner asks « N teams not launched for 60 days — archive them? ».
  - It never lists a scheduled team, nor one whose activity is unknown, and it waits until the launch
    history has been read.
  - Accepting archives exactly the teams it names, each read again at the click, with one undo for all
    of them. « Not now » puts it away for the session.
  - Its switch and threshold are a new card in Settings › Studio, merged into `ui-preferences.json`
    (`ArchiveSuggestion`, `ArchiveSuggestionDays`).
- **Archiving holds even when Studio did not do it.** The Run and Test screens read a team's archived
  state again right before a launch, so a team archived behind their back is refused. A copy's
  arrival counts as activity: a duplicate or an import forgets the copied `lastRunAt` and stamps
  `addedAt` in `studio-team.json` (in a minimal sidecar when the copy came without one), and the last
  activity now reads four dates.
- **Fixes and API changes.**
  - A card's last run falls back on the sidecar's `lastRunAt` once the history has forgotten it.
  - The Test screen's team picker now resolves the team it picks; it used to set only the path, which
    left the trial's buttons disabled.
  - `TeamCatalog.Duplicate`, `TeamCatalog.Import` and `UseCaseImporter.ImportAsync` take the arrival
    date.
  - `ArchiveChanged` now carries every folder one gesture moved (`ArchiveChangedEventArgs.Paths`).

### Added — renaming a team: its folder, session, titles and schedule follow, all or nothing (STUDIO-28)

- **`orkeon forge rename <team-folder> --name <name>`** renames a promoted team, all of it or nothing:
  - the folder takes the name's folder (the one folder rule); the session rule R links to it follows
    (suffixed `-2` past another session's name);
  - `session.json`, `forge.json`, Studio's `studio-team.json` (its `name` only — every other field
    kept), the title and install command of `FORGE.md` and the launchers' header take the new name;
    `schedule/` is regenerated for the new path;
  - a schedule the system runs is reinstalled under the new name, the former registration removed.

  Every step is journaled: a failed one — the disk, the system's scheduler — puts everything back and
  the verb exits 1 (`FORGE-RENAME-FAILED`, or `FORGE-SCHEDULE-REFUSED`). A taken name is refused with
  what holds it (`FORGE-RENAME-TAKEN`); a folder that holds no team is never moved
  (`FORGE-TEAM-UNREADABLE`). New event `team.renamed`; `--name` is no longer reserved to `promote`,
  and `--reference` is now refused on `schedule` / `unschedule` as on the other folder verbs.
- **In Studio**, « Rename » on every team card, in both modes, opens an editor in place of the action
  row:
  - refused while the team runs on the Run or Test screen or is open in the assistant; a taken name is
    said before the engine is asked;
  - the launch history follows the folder — target, working directory, settings file and arguments —
    so the card keeps its last run and « Relaunch » replays where the team is; a launcher aimed at the
    former folder follows it;
  - an allowed folder declared inside the former folder is reported, never rewritten.
- « Modify » still changes the title only; step 4 now says the folder is renamed from My teams.
- The "busy team" hook (`TeamsDependencies.ActivityOf`, `LaunchTabViewModel.RunningTarget`) is shared
  with archiving (STUDIO-31). `examples/forge/promote-demo`'s session now carries an id, like every
  session this build creates.

### Added — Studio archives a team: out of the active list, every link kept (STUDIO-31)

- **Archiving is a flag** in `studio-team.json` (`archived`, `archivedAt`); the folder does not move,
  so its path, session link, scheduled task and history hold. `TeamCatalog.List` takes a filter
  (active by default, archived, all) and never lists a dot folder, nor a hidden or system folder on
  Windows. A duplicate or an import that fails halfway leaves no partial folder.
- **The sidecar is merged, never rebuilt**: a « Modify » re-adoption keeps the archive flag and the
  last run. A duplicate or an import lands active; an export carries the flag.
- **Last activity**: a real run from the Run screen — never a trial, never `--validate` — stamps
  `lastRunAt` into its team; a team's last activity is the most recent of that date, its latest
  history entry and `forge.json` `promotedAt`.
- **Nothing relaunches an archived team by mistake**: the Run and Test screens refuse it under
  « Archived team — restore it? » with « Restore »; « Replay » in the History checks the entry's own
  team and offers the restore instead of running; the Test picker lists active teams only.
- **Rules**: a scheduled team is archived only by stopping its schedule first; archive and restore
  are refused while the team runs on Run or Test, or is open in the assistant — the launchers now
  name the run they have in flight (`TeamActivity`), a seam `forge rename` shares (STUDIO-28).
  « Used by » still counts archived teams, Settings › Team folders marks them « (archived) », and
  the balance covers the active teams. Archiving is Studio's notion: `orkeon run` and the terminal
  launcher ignore it.

### Added — import a use case as it is: `orkeon usecases export` and the gallery's expert action (STUDIO-41)

- **`orkeon usecases export <id> --to <folder> [--lang <code>]`** writes one use case as a team
  folder:
  - `crew/config.yaml` (the example's crew, byte for byte) and its `data/`;
  - a folder behind each of the manifest's mounts (`output/` for every crew that writes files);
  - `studio-team.json`: the title and the problem in `--lang` as the team's name and description,
    and the manifest's mounts relative to the folder (`./data:/data:ro`, `./output:/output:rw`).
  - No `forge.json`: `forge reopen` rebuilds a session from `crew/` (checked on the 90 importable
    examples). Only the manifest's mounts are recorded.
  - A reference-only case is refused with `USECASES-NOT-IMPORTABLE`, a folder that is not empty
    with `USECASES-DESTINATION-NOT-EMPTY`. `--events jsonl` answers with `usecases.exported`.
- **Studio, expert mode:** every gallery card that is not reference-only offers « Import as is ».
  - The name is the title in the UI language. When it is taken, the adoption's banner offers the
    free name (« Import as “… (2)” »), and nothing is exported until the name is free.
  - Studio exports into a staging folder and brings it in through the existing import. The team
    lands in My teams with its name, its description and its data mounted, and « Modify » is
    active.
  - The banner ends on « Team “…” is saved in My teams » and the Import screen's report on the
    folders.
- `ConventionalNames.TeamSidecarFile` and `UseCaseEventKinds.Exported` join the shared constant
  satellites (ADR-009).

### Added — Studio installs and removes a team's schedule; deleting a team leaves nothing behind (STUDIO-27)

- **The operating system runs a scheduled team**; Orkeon still has no scheduler of its own.
- **`orkeon forge schedule <team-folder>`** registers the schedule for the current user, without
  elevation and never through a shell:
  - a Windows task `Orkeon <team>`, which now runs on battery and catches up a missed run;
  - a systemd user timer `orkeon-<team>.timer`;
  - or a crontab line marked `# orkeon:<team>`.

  `--check` answers installed, absent or stale. `orkeon forge unschedule` removes the registration
  and `schedule/`.
- **What was installed is recorded** in `forge.json` (`schedule.installed`). Check and removal act
  only on those recorded names, so a copy never touches its original's task.
  - An OS refusal is `FORGE-SCHEDULE-REFUSED`, carrying the command to run by hand.
  - A new event, `schedule.state`, reports the state.
- **In Studio:**
  - adopting a scheduled team asks « Install the schedule (every day at 08:00)? »;
  - the team card shows the real state (Scheduled, Not installed, To reinstall) with Install and
    Stop actions;
  - the assistant's answers that promised this are now true.
- **Deleting a team stops its schedule first** (a refusal keeps the team). By default it also
  deletes the workshop session that rule R links to it — never the original's, for a copy. The
  Diagnostic lists orphan sessions and cleans each one after confirmation.

### Added — `orkeon forge --reference <id>`: compose a team on the model of a use case (STUDIO-40)

- A new session can start from a use case of the catalogue. The assistant that designs the team
  sees its crew's STRUCTURE: agents with role, goal and tools; tasks with agent, order and output;
  the process.
  - It is shown as a model, not content to copy, in the blueprint phase only, right after the tool
    catalogue.
  - Tools the sandbox lacks are removed.
  - Each text is bounded at 200 characters and the whole outline at 4,000. Measured on the real
    catalogue, it adds 590 to 1,290 estimated tokens per team-design call (median 800, on about
    3,000).
- The session records the reference (`session.json` `reference`, announced on
  `session.started`), and a resume or a `forge reopen` composes with it again.
- The promotion traces it: « Inspiré de : `<title>` (`<id>`) » in `FORGE.md`, and `reference` in
  `forge.json`.
- An unknown id is refused with `USECASES-UNKNOWN-ID`, before any session or model call.
- Orkeon Studio passes the use case chosen in the gallery, and reads it back on resume and on
  « Modify ».

### Added — Studio: the provider balance, where you work (STUDIO-35)

- **The status bar's Balance segment** says what the provider accounts behind the default
  profile, the assistant's profile and the teams' profiles have left.
  - One entry per account (provider + endpoint host + key variable), in the currency the
    provider returns, with the time of each read on hover.
  - Two profiles on one account cost one request.
- **When it reads:** at startup, at the end of each run, trial or composition, and on a click.
  - There is no polling unless Settings › Studio turns on the automatic reading, which is off by
    default.
  - Only DeepSeek, Kimi and OpenRouter accounts ever cost a request; the other providers are
    answered from their documentation.
- **On the bar:** a click on a provider that does not expose its balance opens its console, and an
  optional threshold per provider turns the amount orange.
- **In the profiles:** each model profile row shows its account's balance once read, and the
  profile editor gains a « Read the balance » line.
- Nothing read is written to disk, and no key is ever shown.
- **New Settings tab « Studio »,** holding the automatic reading and the thresholds.
  `ui-preferences.json` is now written by merging, so a theme, language or mode change no longer
  erases other settings.

### Changed — the status bar marks estimated tokens and names the model the agents work on (STUDIO-30 follow-up)

- `cost.updated` gains an optional `operation`: the kind of work the engine attributed the call
  to — `agent`, `manager`, `planning`, `rag`, `memory`, `flow`, `judge`, or a script's
  `ctx.llm.*` method. It is omitted for a call no scope claimed, and the envelope stays `v: 2`.
- Studio's `RunCost.Model` / `Provider` follow the agents' own calls only, a script's `ctx.llm.*`
  included. A judge's, a RAG pipeline's or the manager's reading no longer renames the model on
  the status bar or on the Launch card.
- `RunCost.EstimatedTokens` and `RunProgressModel.FinalEstimatedTokens` carry the estimated part
  of the meter. The Run and Test groups mark ↑ / ↓ with « ≈ » while part of them is an estimate.

### Changed — every LLM call of a run feeds the token counter (STUDIO-42)

- **One measuring point.** `MeteredLlmProvider` wraps every provider that `LlmProviderFactory`
  builds, and every hand-made provider registered through the new `AddOrkeonLlmProvider(...)`. It
  reports each call, buffered or streamed, to `ILlmUsageSink`. With no sink registered, providers
  stay bare.
  - The live `cost.updated` and the final `run.finished.tokens` now count calls they used to miss:
    the hierarchical manager, planning, agent-loop retries and fallbacks, the RAG pipelines,
    flows, memory and LLM judges.
- **Attribution.** `LlmUsageScope`, an `AsyncLocal`, names crew, agent, task and operation
  (`agent`, `manager`, `planning`, `rag`, `memory`, `flow`, `judge`, `ctx.llm.*`). A call no scope
  claims is counted as `unattributed`. `CostUsageEvent` gains `TaskId`.
- **No double count.** The agent loop's and the scripting facade's own reports are removed.
- **Estimates.** A response without usage is estimated and marked, never priced. `cost.updated`
  and `run.finished` gain `estimatedTokens`, omitted when nothing was estimated.
- **A two-part architecture guard:**
  - a source scan (providers built only by the factory or through `AddOrkeonLlmProvider`);
  - a behavioural check (every LLM surface the runner host hands out reports its usage).
- Embedding calls are not generation calls: they stay out of the counter, a limit documented in
  Known limitations.

### Added — Studio: the 105 use cases in the creation wizard, suggested as you type (STUDIO-39)

- Step 1 of « Create a team » keeps its four quick examples first and adds « Browse the use
  cases (N) ». It opens a side panel over the catalogue that `orkeon usecases list` answers:
  - a search box and nine category chips;
  - filters by process, without web access, and without a third-party key;
  - cards with the title and the problem in the UI language, a process badge, and « Reference
    only » on the cases that cannot be imported as they are.
- Choosing a case fills the need with its problem in the UI language, Chinese included. It also
  attaches the case as the creation's reference: a removable « Inspired by: … » chip
  (`ReferenceUseCaseId`).
- After a pause in the typing, the wizard asks one `orkeon usecases search` session and shows
  « N close use cases ». A case counts only when it shares a distinctive term with the need;
  matches by meaning alone never count.
- Without the CLI, the panel shows the wizard's « engine not found » card; there is no
  Studio-side fallback.

### Added — Studio: a status bar with one group per running activity (STUDIO-34)

- A third row at the bottom of the window; the seven overlays now span the three rows.
- **One group per activity that is running** — Run, Test and « Create a team » (the Atelier).
  Each shows only while its activity runs, and a click opens its screen.
- **Run and Test groups** read the run's progress model:
  - team, state (running, waiting for an answer, succeeded, failed), current task, duration;
  - ↑ / ↓, cache and real cost;
  - tools at work (count and first name, the list on hover) and delegations;
  - the provider and model reported by `cost.updated`.
- **The Atelier group** shows its stage, ↑ / ↓ (≈ when estimated) and the remaining budget.
- **At rest, and in expert mode only,** the bar shows the default profile's provider · model.
- **Modes:** novice sees each group's state and ↑ / ↓; expert sees everything. Nothing
  unmeasured is shown as zero.
- **Duration** refreshes on a one-second ticker that only runs while a run is shown. A
  `Balance` slot is reserved for STUDIO-35.

### Changed — adoption names the team, and the session folder follows it (STUDIO-26)

- **`forge promote --name <team>`** hands the engine the team's name, which titles `FORGE.md`,
  `forge.json` and the session. The value is taken as written, a leading dash included, and
  Orkeon Studio always passes it.
- **The session folder follows its team.** Once the promotion is written,
  `.orkeon/forge/<slug>/` takes the destination folder's name as it is.
  - When another session already has that name, a `-2`, `-3`… suffix is added; nothing is ever
    overwritten.
  - `session.json` and `forge.json` take the new slug.
  - The new event `session.renamed {from, to, dir, suffixed}` announces it, and Studio follows it.
- **A rename the disk refuses is a warning, not a failure.** The promotion still exits 0, and a
  new `warning {code, message}` event carries `FORGE-SESSION-NOT-RENAMED`. The session id keeps
  the link.
- **Generated files carry the team folder's name**, not the session slug:
  `orkeon-<team>.service` / `.timer`, the task `Orkeon <team>`, the install command, the
  launchers' header, and `FORGE.md`'s fallback title.
- **A taken name never reaches the engine.** Before promoting a new team, the Studio wizard
  checks its folder:
  - it says what holds the name: a team, a folder or a file;
  - it proposes a free name (« Ma veille (2) » → `ma-veille-2`);
  - when a team holds the name, it offers to open it.
- **Breaking** (no shim): `ForgeArgumentsBuilder.BuildPromote` and `ForgeClient.PromoteAsync` take
  the team name.

### Added — Studio: the progress model holds everything in flight (STUDIO-30)

- `RunProgressModel` now exposes:
  - every tool at work with its start (`ActiveTools`; `ActiveToolName` derives from it) and the
    call tally (`ToolCallCount`, `SucceededToolCalls`, `FailedToolCalls`);
  - the delegations under way (`ActiveDelegations`, from `delegation.started` until its
    `tool.returned`) and the spawned agents (`SpawnedAgents`);
  - `IsWaitingForAnswer`;
  - `StartedAt` and `Elapsed`, with an injectable `TimeProvider`, frozen at the run's own
    duration once it ends.
- A call still open at `run.finished` moves to `UnfinishedTools` / `UnfinishedDelegations` and is
  never counted as a success.
- `Changed` becomes `EventHandler<RunProgressChangedEventArgs>`. Its `Kind` names the event that
  changed the state, so a screen can skip per-token `llm.delta`.

### Added — `orkeon usecases`: the example catalogue in the tool, searched offline in five languages (STUDIO-38)

- `orkeon usecases search "<need>"` ranks the 105 example use cases against a need written in
  French, English, Spanish, German or Simplified Chinese — no LLM, no network, no file read.
  - **By terms (BM25):** each sheet's title and problem in the five languages, its tags, tools,
    category and id words. The query and the sheets are normalized the same way: lowercase,
    accents folded, Chinese split into character bigrams.
  - **By meaning:** the local BGE-micro-v2 model (English) embeds each sheet's English text at
    the first search that needs it. Its ranking is fused by RRF only for the languages where the
    golden set shows a gain.
  - **Without the model,** the search runs by terms and every answer says why.
- Each result gives its id, score, reason (`terms`, `meaning`, `terms+meaning`) and matched terms.
  The answer gives the mode and the language: `--lang`, or read from the text.
- **Session mode.** `--events jsonl` without a text opens a session: `usecases.ready`, then one
  `usecases.results` per `usecases.query` line on stdin, correlated by `correlationId`. The model
  loads once, and closing stdin exits 0. The kinds are declared in
  `Orkeon.Constants.Protocol.UseCaseEventKinds`.
- `list` filters by category, process and tag. `show <id>` prints a sheet and its files, plus its
  crew file with `--crew`. An unknown id is `USECASES-UNKNOWN-ID`. Both also answer in
  `--events jsonl`.
- **Embedded in the tool:** `examples/usecases.json`, each example's crew file and its `data/`
  folder, about 217 KB compressed. The finance examples stay reference only: their shared
  `_tools/` is not embedded.
- **Golden set:** `examples/usecases.golden.yaml` holds 22 queries in the five languages, and
  `UseCaseGoldenSetTests` (Slow) reports recall@5 and MRR per language and per mode.

### Changed — a stable session id and one rule link a team to its forge session (STUDIO-25)

- **A team's link to its session no longer rests on an absolute path.**
  - A forge session gets an id (GUID) at creation. It is written to `session.json`, announced on
    `session.started`, and copied by `forge promote` into the team's `forge.json`.
  - A rebuilt session gets a new id.
- **Rule R decides the link.** It is written once, in `Orkeon.Domain.FileSystem.TeamSessionLink`.
  Take the session that carries the folder's id:
  - it is linked when its `promotedTo` designates the folder;
  - it is also linked when that folder is gone or no longer carries the id: the team was moved or
    renamed;
  - when `promotedTo` designates another existing folder carrying the same id, the folder is a copy
    and is linked to nothing;
  - no id, or an id no session carries, links nothing (no shim).
- **`forge reopen` applies rule R.**
  - A moved team resumes its session: `promotedTo` is re-pointed, and there is no more `-2`
    duplicate.
  - A copy, or a folder without a known id, gets a rebuilt session whose id is written into its
    `forge.json`. A copy thus becomes independent, and its session is named after its own folder.
- **`forge promote --to`** updates the linked folder in place, a moved one included, and refuses a
  copy with the reason.
- **Studio « Modifier » always runs `forge reopen`.** A duplicated team can no longer open its
  original's session.
  - Removed, without a shim: `TeamsViewModel.FindSessionFor`,
    `ForgeSessionCatalog.FindByPromotedTo`, `ForgeSession.FindPromotedTo` and `IsSameDirectory`.
  - New: `ForgeSessionCatalog.FindById` / `ReadTeamSessionId`, `TeamSummary.ForgeSessionId`,
    `ForgeSessionModel.SessionId`.

### Added — the 105 use cases state the user's problem in five languages (STUDIO-37, draft)

- Every `usecase.yaml` now carries a title and a one-line problem in fr, en, es, de and
  zh-Hans, plus two to four search tags.
  - Each problem answers the team wizard's question in the voice of Studio's four example
    chips.
  - Each follows what the crew's config really does, not what its README claims.
- `lint-example-configs.py` now requires the five languages on every sheet.
- The texts are a reviewed first draft: a fluent human review per language is still due.

### Added — every numbered example carries a use-case sheet, gathered in `examples/usecases.json` (STUDIO-36)

- Each of the 105 numbered examples has a `usecase.yaml` beside its crew. It holds the
  hand-written fields:
  - title and problem in fr, en, es, de and zh-Hans, empty until STUDIO-37;
  - tags;
  - the mounts the example needs (`./data:/data:ro`, `./output:/output:rw`);
  - `importable`, false for the 15 finance scripts, which depend on `_tools/`.
- `scripts/generate_examples_index.py` also writes the deterministic manifest
  `examples/usecases.json`, adding the derived fields:
  - format, process, agent and task counts, tools;
  - `hasSampleData` (a `data/` folder exists);
  - `requiresNetwork`, from an explicit tool table that stops on an unknown tool;
  - `requiresKeys`, the third-party key variables beyond the LLM, e.g. `ORKEON_TAVILY_API_KEY`.
- `has_data` now means "a `data/` folder exists", so `examples/INDEX.md` does not change when
  the new sheets are added.
- `lint-example-configs.py` checks each sheet's format and three consistency rules, and fails
  on a missing sheet, a duplicated id or a stale manifest. The five-language requirement waits
  behind `--require-texts`.
- The examples CI checks that the manifest is fresh. `scripts/test-examples-catalog.py`
  (23 tests) covers the generator and the lint.

### Added — Studio can read what is left on a provider account (STUDIO-33)

- `IProviderBalanceProbe` / `HttpProviderBalanceProbe` (Studio.Core) return a typed result
  for every provider, never an exception and never a silence:
  - DeepSeek, Kimi (USD on the `.ai` host, CNY on the `.cn` one) and OpenRouter (what the key
    may still spend under its limit) are read with the profile's own key and host;
  - x.AI and Qwen keep the balance behind an administrative credential;
  - the other cloud providers expose none — their admin APIs report spend, not what is left;
  - local endpoints have no account.
- No detail quotes the provider's answer or the key, and the probe never runs by itself:
  its caller decides when a read is worth a request.
- `LlmPresetInfo.KeyConsoleUri` and `LlmPresets.KeyConsoleFor` give the https link of the
  vendor console, from the existing `KeyConsoleUrl` text.
- Every verdict is sourced from vendor documentation read on 2026-09-24 (provider
  comparison, "Account balance"). The three readers still await a check with a real key.

### Changed — one folder-name rule for the CLI and Studio (STUDIO-24)

- **A name gives one folder, whichever side computes it.** The CLI named a forge session
  with its own slugifier (40 characters cut mid-word, `forge-<timestamp>` when nothing
  usable remained) and Studio named a team folder with another (64 characters cut at a
  word, `equipe`, an exception on a blank name). Both now go through `FolderSlug`, public in
  `Orkeon.Domain.FileSystem`: lowercase ASCII, accents dropped, one dash between words, 64
  characters cut at a word — and `null` when no ASCII letter or digit remains, each caller
  keeping its own fallback (`FolderSlug.TeamFallback`, `equipe`, for a team folder or an
  agent key; the timestamp for a session). A session named from a long need now keeps up
  to 64 characters cut at a word instead of 40 cut mid-word.
- `TeamCatalog.Slugify` and `TeamCatalog.MaxSlugLength` are removed, without a shim;
  `TeamCatalog.MaxNameLength` is `FolderSlug.MaxLength`. A new agent whose name holds only
  spaces previews the key `agent`, as an empty name did, instead of throwing from the slug
  rule.
- `FolderSlugDriftTests` fails when a slug implementation reappears in the CLI or Studio
  sources, and one corpus of names (`FolderSlugCorpus`) is checked by the Domain, CLI and
  Studio suites alike.

<!-- STUDIO-29 -->
### Changed — `cost.updated` carries the ↑/↓ split and the vendor's real cost while the run goes (STUDIO-29)

- **The split no longer waits for the end.** Every `cost.updated` of `orkeon run --events
  jsonl` now carries the run's cumulative `promptTokens` and `completionTokens`, and the
  cache pair `cacheHitTokens` / `cacheMissTokens` once a provider measured it — the fields
  `run.finished` already had. The event names the `provider` on the YAML path too, and its
  envelope carries the `crewId` (the agent's role stays `agentId`). The envelope keeps
  `v: 2`: the fields are additive, and `run-event-bus.md` now writes that rule down.
- **The vendor's real charge reaches the wire, and nothing else does.** OpenRouter bills in
  its answer (`usage.cost`): the provider base already read it into the `cost` metadata, and
  it died at the chat client adapter. It now travels as billed — the provider states its
  currency beside it (`cost_currency`, `USD` for OpenRouter's dollar credits), the adapter
  carries both onto the `ChatResponse` (buffered and streamed fallback), the agent loop and
  the scripting facade put them on the usage event, and `cost.updated` relays `cost`,
  `currency` and `costSource: "vendor"`. A free model bills `0`, relayed as `0`; a vendor
  that bills nothing in its answer leaves no price on the wire at all — the framework's
  price registry never estimates one there (DD-1).
- **`CostUsageEvent.Cost` is `decimal?`** (breaking): null means unknown, and
  `CostBudgetManager` prices from its registry only a null — a vendor's `0` used to be read
  as "not provided" and charged at the registry price. The event also gains `CostCurrency`,
  and the agent loop now fills `Provider`, `CrewId` and `OperationType` (`agent`).
- **`LlmResponse.Cost` is removed** (breaking): nothing read or wrote it; the charge
  travels in the metadata and is read with `LlmVendorCost.TryRead`.
- Studio's `RunProgressModel` folds the new fields into `RunCost` (`PromptTokens`,
  `CompletionTokens`, the cache pair, `Amount`, `Currency`, `Source`); a field a line leaves
  out stays null. The screens that show them are STUDIO-30 and STUDIO-34.

### Fixed — `Orkeon.Compliance.Vfs` is compiled against Roslyn 4.8.0 again

- The 2026-09-21 dependency bump raised the analyzer's `Microsoft.CodeAnalysis.CSharp`
  reference from 4.8.0 back to 5.9.0 — the repository's own compiler — and `1.0.0-rc.4` of
  the package went out that way. Outside this repository that is the rc.3 defect over
  again: a stock .NET 10 SDK (compiler 5.6.0) refuses the analyzer with `CS9057` and skips
  it silently, so a consumer project with a `File.ReadAllText` call builds clean. The
  `VersionOverride` is back on 4.8.0, as the comment above it in the csproj has said since
  rc.3, and the packed README names both inert versions instead of one.

## [1.0.0-rc.4] - 2026-09-21

The release candidate of the Studio. Since `1.0.0-rc.3`: Orkeon Studio takes the team
journey end to end on a real crew — the wizard, the folders that travel with a team
(STUDIO-14, VFS-90: every settings mount carries a ULID, a crew names the entries it uses,
`--mount-id` selects one), the Tools and MCP tabs, the Run screen that shows the task in
progress, and « Modify » on any team, session or not (FORGE-09 `forge reopen`); the
provider fleet reaches **16** with the two aggregators OpenRouter and Mammouth AI (LLM-09);
the output cap follows each model's documented maximum instead of 4096 (LLM-10); a
timed-out call is a failed call and the sequential mode skips what depended on it
(LLM-11); the scripting runtime runs its loops in JavaScript (SCR-25); `Orkeon.Hosting.Aspire`
(ADR-011) and `Orkeon.Interop.AgentFramework` (ADR-010) join the lineup, and a run speaks
the OpenTelemetry GenAI conventions; the README's quickstart is executed by CI, the
provenance chain is documented and closed with an SBOM; and a second full SonarQube
campaign closes the 87 issues the fortnight had accumulated — 0 bug, 0 code smell, 0
hotspot to review, debt back to zero.

The public API surface is frozen at this tag: the 335 additions and 19 removals
accumulated since rc.3 move from `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt`
across the twelve projects that carried them.

<!-- SonarQube campaign of 2026-09-21 -->
### Fixed — a host without an `Llm` section runs on the echo provider again

- **The fallback the warning announces is the fallback the run gets.** Since LLM-11 a call
  the provider refuses fails the task with the provider's own sentence — the right thing for
  a timed-out cloud model, and the wrong thing for a host with no `Llm` section at all: its
  provider was the infrastructure's keyless OpenAI default, so *"No `Llm` section configured
  — falling back to the echo provider"* was followed by *"OpenAI API key is required"* and
  exit code 2. The bundled scripting demos and the offline E2E run had stopped running.
  `RunnerHost` now registers the echo provider itself (`UndefinedLlmProvider`, the one the
  scripting facade answers `<undefined-llm>` with) as `IBasicLlmProvider` and `IChatClient`
  when the section is absent; the crew runs, its answers replay the prompts, and the warning
  stays the one line that says it is not a real model
  (`RunnerHostLlmFallbackWarningTests.MissingLlmSection_RunsOnTheEchoProvider`).

### Changed — the SonarQube campaign of 2026-09-21 (87 issues, 2 hotspots)

- Every issue the analysis raised on the code written since the 5 September campaign is
  closed: the two `S2583` bugs of `ToolCallTextParser` (the JSON block scanner is now an
  explicit `BlockScan` state with one method per transition), the sixteen methods above the
  cognitive-complexity ceiling (`MountSelection.Resolve`, `RunnerHost` configuration,
  `SequentialProcessStrategy`, `ChatClientAgentLoop`, `CrewHandoffDetector`, `TeamCatalog`,
  the Studio view models, …), and the rest. Behaviour is unchanged except for one real
  defect the analysis caught: the shell refreshed the mounts editor's team references on every
  card added to the My-teams list instead of once per rebuild.
- `MountSelectionPlan.WithdrawnIndices`, `TeamSummary.UnknownMountIds` and
  `TeamTarget.UnknownMountIds` are methods now — a property must not copy a collection.
  `ImportTeamViewModel` takes an `ImportTeamDependencies` record, like the other Studio
  screens. `McpToolProvider` is sealed. Every false-positive arbitration lives in the code, as
  a `[SuppressMessage]` or `#pragma` with its justification (13 × S3604 on primary
  constructors, 2 × S1168 third-state nulls, the JS-called members of `JsLlmFacade`, the
  YamlDotNet-populated probe, the vendors' key-console URLs).

<!-- Studio — owner report of 2026-09-21 -->
### Fixed — Studio: My teams and the wizard — a discarded session resets the wizard, « Modify » reaches it at the first click

- **A session deleted under « Sessions in progress » no longer lives on in the wizard.**
  « Modify » on a team card parks its session in that list, « Resume » opens the wizard on
  it, and deleting the row there left the wizard exactly where it was — a Composer over a
  directory that no longer existed, every gesture on it bound to fail. The My-teams screen
  now announces which session went (`TeamsViewModel.SessionDeleted`, not raised for a delete
  the disk refused), the shell relays it, and the wizard open on that directory goes back to
  the blank step 1 of « Restart », a running engine stopped first
  (`CreateTeamViewModel.ForgetSession`). A session the wizard is not open on leaves it
  untouched; paths are compared the catalog's way (full, trailing-separator-blind, case-blind
  on Windows).
- **« Modify » brings the wizard forward on the first click.** With no session pointing at
  the team (imported, or its session deleted), the engine rebuilds one first (FORGE-09), and
  the screen only came forward once it had — a second or two of nothing, so the button got
  clicked again, and the second click landed on the busy engine and vanished. The wizard
  now takes the screen on the click and the rebuild shows as the engine working. A click on
  « Modify » or « Resume » while the engine is busy on another creation is refused in words
  on the wizard's status line (`Studio.Create.EngineBusy`, five languages), the creation
  under way untouched, instead of being dropped.
- **« Modify » no longer lands on step 1 with the rebuilt session sitting on disk.** The
  real cause of the second click: WPF resumes an await begun in an input handler at Send
  priority — every window message is dispatched through an Invoke at Send — above the
  Normal priority the reader thread's posts travel at, so the rebuild read the session off a
  model the engine's events had not reached yet, found nothing, and stayed on step 1 while
  the session it had just written waited on disk for the next click. A forge run's task now
  completes only once its epilogue has landed on the UI thread, and with it every event
  posted before (`CreateTeamViewModel.PostAndAwaitAsync`, the adoption's promote too). The
  disk is the fallback when the stream announces nothing, a failure card says so when
  neither has it, and a resume or reopen that throws anything lands on the wizard's status
  line instead of a discarded task.

<!-- LLM-10 / LLM-08 -->
### Fixed — Together's context window is no longer sent as the output cap (LLM-10, campaign of 2026-09-21)

- **`TogetherAiLlmProvider` sends the 4096 fallback again, not the model's window.** LLM-10
  named the window as the cap on Together (131 072 on `Llama-3.3-70B-Instruct-Turbo`, up to
  1 048 575 on `GLM-5.3-Flash`) on the assumption that `context_length_exceeded_behavior:
  truncate` clamps it to window − prompt. The campaign of 2026-09-21 measured otherwise: only
  one of Together's serverless engines honours the flag, and on the buffered path only — every
  other call was refused (`inputs tokens + max_new_tokens must be <= 131073`,
  `context_length_exceeded`), and the default model fell from 9/1/2 to 2/9/1 for two days. The
  four `together:` entries are withdrawn from `LlmModelOutputLimits`; the flag stays, it costs
  nothing and helps where it is honoured. Omitting the field would be worse: Together then
  stops at 2048 tokens (`finish_reason: length`, measured the same day).
- **The catalogue-cap retry net knows Together's wordings.** A 400/422 naming `max_new_tokens`,
  `context_length_exceeded` or "maximum context length" now counts as the cap being refused,
  so a long prompt on the fallback is retried once without the field instead of failing
  (`OpenAICompatibleProviderBase.TryDropCatalogueOutputCap`).
- **Campaign kit.** `gpt-6-astra` is pinned to `temperature: 1` in
  `llmproviders-test/lib/catalog.json` (`Only the default (1) value is supported`) and, unlike
  Sol, has no `reasoning_effort: none` (`Supported values are: 'low', 'medium', 'high', and
  'xhigh'`): function tools stay refused on chat/completions (`use /v1/responses`). Both facts
  are in `docs/reference/llm-providers-comparison.md`, per-model table.

<!-- FORGE-09 -->
### Added — « Modify » without a session: `orkeon forge reopen <team-folder>` rebuilds one from the team's files (FORGE-09)

- **A promoted team no longer needs its forge session to be modified.** `orkeon forge reopen
  <team-folder>` (offline, `--events` its only option) names the session whose `promotedTo` is
  the folder when one exists, and otherwise **rebuilds** one from the folder itself: the plan is
  read back from `crew/` (per-entity or single-file YAML) into a blueprint, the brief comes from
  the new `forge.json`, the crew is copied verbatim, and the session lands at the `--dry` pause
  with `promotedTo` set — `resume --edit --dry`, `resume`, `resume --adopt` and a `promote --to`
  onto the same folder follow as usual (`ForgeSessionRebuilder`, `ForgeTeamReader`,
  `ForgeTrigger.Rebuilt`, event `team.reopened {slug, dir, path, state, rebuilt, brief}`). A
  folder with no readable YAML crew is refused with `FORGE-TEAM-UNREADABLE` and the reasons.
- **`forge promote` writes `forge.json`** next to `FORGE.md`: the session's slug, title, format,
  promotion instant and the brief — the machine-readable twin of the card, nothing secret in it.
  Without it (teams promoted earlier, imported crews) the rebuild derives a minimal brief from
  the plan and says `brief: derived`.
- **Orkeon Studio.** « Modify » on a team card is enabled for every team whose `crew/` holds a
  YAML definition (`TeamSummary.HasYamlCrew`), session or not; without a session the wizard runs
  `forge reopen` on the folder, reads the rebuilt session off the stream and opens the Composer
  at the dry pause (agents editable, « Try the team » / « Adopt without trying », adoption
  fields seeded from the sidecar, re-adoption pinned to the folder). The tooltip says whether
  the reopen goes through the session or a rebuilt one; only a team with no YAML crew keeps the
  button disabled, with the reason (`Studio.Teams.ModifyRebuild`, `Studio.Teams.ModifyNoSession`
  reworded, five cultures).

### Added — the Launch screen's command can be copied, and « Open the result » opens every writable folder

- Orkeon Studio's COMMANDE well is selectable and « Copy the command » puts the exact
  `orkeon run …` line on the clipboard, ULID and all; the button reads « Command copied » until
  the command changes. Reproducing a Studio launch in a terminal no longer means retyping it.
- « Open the result » opens one Explorer window per folder the run could write to — the team's
  own folders first, then the declared entries kept for the run — and reads « Open the N result
  folders » when there are several. It reads the effective mounts, so a settings entry withdrawn
  for the run (its root went to the entry the team names) is never opened; the history cards
  resolve a `--mount-id` against the declared list the same way.

<!-- VFS-90 -->
### Added — every settings mount carries a ULID; a crew names the entries it uses (`mounts:`), two entries may share a root, and `--mount-id` selects one for the run (VFS-90)

- **A mount has an identity.** An entry of `Orkeon:FileSystem:Mounts` may carry a
  26-character ULID before a `|` — `01J9Z3K4M5N6P7Q8R9S0T1V2W3|C:\data:/output:rw`
  (`FileSystemMount.Id`, `MountId`, strict Crockford validation ahead of the lenient
  `Ulid.Parse`). Two entries may declare one virtual root when both carry an id; a root declared
  twice with an entry that has none, or one id on two entries, is refused before any host
  (`RunnerExecution.EnsureVirtualRootsAreUnique`, `MountSelection.ValidateDeclared`).
- **A crew names the roots it uses.** `config.yaml`/`crew.yaml` gain a `mounts:` block —
  `/output`, or `<ulid>|/output` to pin one settings entry (`CrewConfiguration.Mounts`,
  `MountReference`); `forge promote` writes it. The block selects and validates, it never
  restricts (D-05): `orkeon run crew/` resolves it against the settings with no `--mount`, and
  refuses in one line a root nothing provides or an id no entry carries.
- **`--mount-id <ulid>`** (`orkeon run`, both dialects; several after one flag) keeps the entry
  named among several entries of one root. Precedence (D-10): `--mount` on the root replaces
  every entry of that root, else `--mount-id`, else the crew's `mounts:`, else a unique root
  is mounted as it is; with nothing selecting one, the run is refused naming every id (D-04).
  An entry not kept is **withdrawn for the run** — written to `null` at its own index, not
  whitelisted, its folder not probed. `RunnerHost` applies the same `MountSelection.Resolve`
  while composing the configuration, so a host built without the guards (the daemon, `rag`,
  `forge`, tests) throws the same text instead of "Duplicate virtual paths".
- **Orkeon Studio**: every authorized folder shows its id (copy button, « used by » the teams
  naming it, a confirmation before removing one a team depends on); a save gives an id to an
  entry that has none. The chooser opened for a mount point offers only the entries declared
  under that root and records the pick verbatim; the disk pick declares the folder under the
  row's root — a second `/output`, told apart by its id — instead of renaming it `/docs`. The
  sidecar keeps `<ulid>|<copy>`, the ids travel with a duplicated, exported or imported team,
  a team naming a declaration missing on this machine is refused and the import review offers
  « Authorize them as recorded »; the launch lays `--mount-id` for a declaration and `--mount`
  only for the team's own folders; the effective-mounts table says which entry of a shared
  root is kept. The two folder modals (« Add an allowed folder », « Folders of … ») show the
  whole disk path, a one-word rights pill with the full label as tooltip, and the tail of each
  entry's id, so two `/output` rows are told apart.

### Changed

- `|` at the head of a mount string is reserved for the id prefix; a physical path that really
  starts with a bare token and a `|` is quoted (`"a|b":/x:ro`). `list_mounts` and the
  access-denied messages never show an id (D-08).
- The settings half of the duplicate-root guard reads `Orkeon:FileSystem:Mounts` with its
  indices (`RunnerSettings.ReadDeclaredAgentFacingMounts`); `EnsureMountSourcesExist` skips the
  entries a selection withdrew; the runners pre-read the crew's `mounts:` block from disk
  (`CrewMountDeclarations`, an `EXCEPTION-BOOTSTRAP`) so the refusal comes in one line before
  "Using settings".
- The `orkeon-host` daemon stays out of the selection (D-11): per-crew roots, an id prefix on an
  operator `--mount` parses, no crew block is read. A `studio-team.json` written with ids is not
  read by an Orkeon Studio from before this change (same version, no migration needed the other
  way: a sidecar without ids resolves by folder, root and rights and is upgraded on its next save).
- Dependencies (2026-09-21): the `Microsoft.Extensions.*` family and its `System.*` companions to
  10.0.12, `Microsoft.Extensions.AI` to 10.10.0, `Microsoft.Agents.AI.Abstractions` to 1.22.0,
  `Aspire.Hosting` to 13.5.4, `Microsoft.Data.SqlClient` 7.1.0, `MongoDB.Driver` 3.12.0,
  `Gremlin.Net` 3.8.2, `StackExchange.Redis` 3.3.0, `AngleSharp` 1.8.2, `Polly` 8.8.0,
  `Markdig` 1.4.0, the `IdentityModel` trio 8.23.0, `Azure.Security.KeyVault.Secrets` 4.11.1,
  `AWSSDK.SecretsManager` 4.0.100.14, `OpenTelemetry` 1.19.0, `Microsoft.ML.OnnxRuntime` 1.30.0,
  `Terminal.Gui` 2.5.0, `Jint` 4.16.3; on the test side `Microsoft.NET.Test.Sdk` 18.10.1,
  `xunit.v3` 4.0.1 and `Testcontainers` 4.15.0. `Orkeon.Compliance.Vfs` is built against Roslyn
  5.9.0, the compiler the repo runs, instead of 4.8.0; `Orkeon.Host` references
  `Microsoft.CodeAnalysis.CSharp` directly. Both solutions build warning-free on the new pins and
  the unit and fast suites are green.

<!-- LLM-11 -->
### Fixed — a timed-out LLM call is a failed call, not an empty answer; the sequential mode skips the dependents of a failed task; Studio pins the thinking switch and a reasoning-model timeout (LLM-11)

- **A call the provider never answered fails the task with the provider's reason.** On the
  owner's run of 2026-09-20 (`kimi-k2.6`, `Llm:TimeoutSeconds` 180), the scorer's call timed
  out, the chat-client adapter mapped the refusal to an empty message, the agent loop
  diagnosed "a reasoning model out of budget — raise `Llm:MaxTokens`" and retried once
  without tools, for a second full timeout: six minutes, zero tokens, wrong advice. Now the
  provider's failure travels as `LlmResponse.Error` (`LlmResponseMetadataKeys`), the buffered
  `LlmProviderToChatClientAdapter` path throws it the way the streaming path already did, and
  the loops exit `AgentExitReason.LlmCallFailed` at once — no tool-free retry — with the
  provider's sentence as the task's error, the summary line and the runner's last stderr line.
- **An elapsed timeout says which setting elapsed and the ways out.** In place of HttpClient's
  wording, every provider (`HttpLlmProviderBase.DescribeCallFailure`) answers "*{Provider} did
  not answer within `Llm:TimeoutSeconds` = 180 s … raise `Llm:TimeoutSeconds` (600 s is a safe
  value for a reasoning model), or turn thinking off*", naming the settings key, the crew
  `llm.thinking.enabled` block and the Studio switch.
- **An HttpClient timeout is retried — once.** The retry clause written as
  `!ex.CancellationToken.IsCancellationRequested` never fired on a real timeout (HttpClient
  cancels its own linked token), so every timeout failed on its first attempt whatever
  `Llm:MaxRetries` said. `ResiliencePolicies.IsHttpClientTimeout` recognises the shape (a
  nested `TimeoutException`); the LLM policy retries it once
  (`ResilienceDefaults.LlmTimeoutRetries` — every attempt costs the whole timeout, the ordinary
  budget is for failures that come back in seconds), never when `MaxRetries` is 0, never on a
  caller's cancellation; the generic HTTP policies retry it like any transient failure.
- **The sequential mode skips the dependents of a task that did not succeed.** The writer
  used to run on a context saying "Task failed: …" where the scored JSON should have been, and
  improvised a deliverable from the cleaner's prose. A task whose declared dependency failed
  or was skipped is now skipped in its turn — no agent asked, no token spent — recorded as
  `⊘ skipped` in `AUTO_SUMMARY.md` (with a **Skipped** count), as `task.completed` with
  `skipped: true` on the event stream (`RunTaskProgress.Skipped` in Studio), and in the crew's
  failure reason; the tasks that do not depend on it still run.
- **Studio: the profile pins the thinking switch, and a reasoning provider brings its timeout.**
  The knob existed in the crew YAML (`llm.thinking.enabled`) and the settings file
  (`Llm:Thinking:Enabled`) but had no place in Studio. `ModelProfile.ThinkingEnabled` /
  `ThinkingEffort` travel as `ORKEON_Llm__Thinking__{Enabled,Effort}`; the profile editor
  offers provider default / on / off and the effort hint; `LlmSection` reads and writes the
  same keys, the validator types them, and the TUI form has a `Thinking` field. Picking Kimi,
  DeepSeek, Z.AI or MiniMax — whose default model reasons before it answers — pre-fills the
  timeout with `LlmPresets.ReasoningTimeoutSeconds` (600 s, the settings templates' value) and
  the hint says why; a typed value is never overwritten, and the seed leaves with the card
  that brought it.

<!-- STUDIO-22 -->
### Fixed — Studio: the limits tab shows the engine's defaults as watermarks, and its switches take one click (STUDIO-22)

- **Every empty field of « Settings › Limits & index » now names the engine's default as a
  watermark** — the four per-minute budgets and the queue limit (`RateLimitingOptions`: 60, 30,
  20, 5), the concurrency bound and the log body length as « unlimited » (their engine default is
  0, meaning no bound), the RAG profile (`fast`) and the corrective iteration bound (3). The
  defaults are copies in Studio Core, pinned against the engine's option classes by
  `EngineDefaultsDriftTests`. The watermark is a theme feature: any `Input` text box or `ComboBox`
  shows its `Tag` while empty. Owner review 2026-09-20: nothing said what an empty field meant.
- **A switch bound to an absent key took two clicks** — a check box over a nullable boolean starts
  indeterminate and the first click only turns it to false. The five switches of the tab (the two
  LLM-logging captures, on by default; the three RAG opt-ins, off by default) are plain booleans
  resolving an absent key to the engine's default; setting a switch back to its default removes
  the key. A guard refuses any nullable boolean on a section form.

<!-- STUDIO-21 -->
### Added — Studio: Tools and MCP settings tabs; `orkeon run` connects the MCP servers the settings declare (STUDIO-21)

- **`orkeon run` honours the `MCP` section.** Until now the section was bound by nobody: the
  runners built their host without configuration, so an MCP server written in the settings
  changed nothing. `RunnerHost` now calls `AddOrkeonMcp` when `MCP:Servers` declares at least
  one server and `MCP:Enabled` is not `false`, and an explicit startup step (`McpStartup`)
  connects every server before the crew loads — and before `--validate` judges it and
  `--list-tools` prints the manifest. A server that cannot be connected (missing command,
  silent endpoint, handshake pending after 30 s) costs one error line naming it, on the log and
  on stderr, and the run goes on; a crew naming one of its tools then fails at load under
  `StrictTools`. `McpToolProvider` also implements `IDisposable` so a host disposed
  synchronously releases the child processes. `ConfigurationKeys.McpSection` spells the
  section once for the infrastructure, the runners and Studio.
- **Settings › Tools (both modes).** The keys the tools need — the Tavily key of `web_search`
  (`ORKEON_TAVILY_API_KEY`) and the Brave key of `brave_search` (`BRAVE_API_KEY`) — on the same
  rows and the same store as the API keys of the model tab; the catalogue of every tool a run
  exposes, by family, each tool that needs something saying what (a key above, a key or the
  connection parameters given at the call by the agent, an expert setting below); and, for the
  expert, the `shell_command` allow-list (`Orkeon:Tools:Shell`), never written as an empty
  array. The catalogue lives in Core (`ToolCatalog`) and a test pins it against
  `docs/tools/inventory.md`. Owner request 2026-09-20: the settings had no place for a tool
  key, and nothing listed the tools or what they need.
- **Settings › MCP (expert).** The `MCP` section as cards: the switch and one card per server
  (identifier, transport, command, arguments, environment, URL), written in place through the
  new `McpSection` so unknown keys survive, with the row's own problem said the way the validator
  refuses the save (`STUDIO-MCP-ID`, `-TRANSPORT`, `-COMMAND`, `-URL`, and `-ENV-SECRET` at
  information level for a secret written in clear). `SecretRowViewModel` is its own file, public,
  shared by the two key cards; six settings tabs, two new capture stops, fifty strings in five
  languages.

<!-- T-31 / T-32 -->
### Changed — Studio: editable fields look editable, multi-line fields are five lines (T-31, T-32)

- **A field says «you can type here» (T-31).** Every input — text box, password box, combo
  box, check box, radio dot — now sits on its own surface (`FieldBrush`, `FieldLineBrush`,
  `FieldShadowBrush` in both theme dictionaries: white with a franker line in light, a well
  deeper than the surface in dark, an inset hairline under the top edge) instead of the sunk
  paper the read-only wells keep (YAML, raw JSON, journal, code). A read-only text box goes
  back to the well recipe. Owner design review: nothing distinguished a field from a block
  of text, and the user could not see where to write.
- **The API key is masked while it is typed** — the two key fields (profile editor, API keys
  card) are `PasswordBox`es on the new `InputPassword` style, bound through
  `PasswordBinding.Text` (WPF keeps `Password` out of bindings on purpose; the attached
  property carries it both ways, so the field still empties itself once the key is stored).
- **A multi-line field is five lines high, with a scrollbar from the sixth (T-32).** Two
  styles, `InputMultiline` and `InputMultilineMono`, on `MinLines`/`MaxLines` rather than a
  height — the box follows the font and the theme, never grows the card, never pushes the
  layout. Six fields wear them: the step-1 brief and «what the result must contain», the
  per-step instruction, the agent editor's «what it does», the trial's sample inputs (mono)
  and the conversation composer, whose Enter-sends / Shift+Enter-breaks contract is unchanged.
  The hard `MinHeight`/`MaxHeight` those fields carried are gone with the change.

<!-- STUDIO-20 -->
### Changed — Studio: adopting a team ends the wizard at a blank step 1; the saved card is gone (STUDIO-20)

- **« Save to my teams » ends the tunnel.** On a successful promotion the wizard goes back to a
  blank step 1 — the same slate as « Start over »: step-1 folders and policy, projection, name,
  profile, schedule, mounts, conversation, notes — and one status line stays: « Team “X” is saved
  in My teams » (`Studio.Create.AdoptedLine`, five languages). Owner review 2026-09-20: the
  wizard used to stay parked on step 4 with a « Team saved » card. A refused promotion is
  unchanged: step 4, the failure card.
- **The session leaves « Sessions in progress ».** The engine writes the session `Promoted`
  before it emits `promoted`, and the shell re-reads the catalogs on `TeamAdopted`; the two
  saved-card buttons that resumed the session right after an adoption (« Modify the team »,
  « Run the trial again ») are gone with the card, so nothing puts it back. « Modify » on the
  team card (reverse lookup by `promotedTo`) stays the way to reopen an adopted team, and the
  reopened arbitration offers `retry`. Nothing is deleted on disk.
- **Deleted with the card**: `IsSaved`/`NotSaved`, `SavedPath`, `InstallCommand`,
  `ReopenComposeCommand`, `RetryTrialCommand`, `ReopenAdoptedAsync` and the auto-retry flag,
  the card's markup, eight `Studio.Create.*` strings in five languages. The expert-only
  scheduling hint (`install` of the `promoted` event) is no longer displayed: the schedule lives
  in the sidecar, on the team card and in the team's `schedule/` folder.

<!-- STUDIO-19 -->
### Changed — Studio: « Allow a folder » opens the OS folder dialog directly; the in-app picker modal is gone (STUDIO-19)

- **The « Allow a folder » modal is deleted** — path field and Browse, the one-level tree with
  its « already allowed » notes, the two rights rows, the expert mount-string preview
  (`FolderPickerViewModel`, its overlay in `MainWindow.xaml`, the `modale-declarer-dossier`
  capture stop, eleven `Studio.Settings.*` strings in five languages). Owner review 2026-09-19:
  a relic of an earlier design that the Windows folder dialog replaces outright.
- **Settings › Authorized folders**: the novice card's button calls the OS folder dialog
  (`IPathPicker.PickFolder`, `OpenFolderDialog`); the pick lands read-only under a virtual name
  derived from the folder's own name — `MountDefinition.SuggestVirtualPath`, one derivation in
  Core for both doors, first free suggestion when the name is taken, reserved or unusable.
  The card's rights badge is now a toggle, read-only ↔ read-and-write (`rwnd` narrows to
  read-only); the novice auto-save hears it like any edit.
- **The wizard's « Existing folders » door** (`CreateTeam.PickFolderRequested`): the same OS
  dialog, opened on the reopened team's folder when there is one; the pick is declared in the
  settings under the ROW's rights, saved, and bound behind the row — the five steps of
  STUDIO-14 D-10 unchanged. A cancelled dialog does nothing and says nothing.
- `IDirectoryProbe.ListSubdirectories` and the `LeftIndent` converter leave with their only
  consumer, the tree.

<!-- STUDIO-18 -->
### Fixed — Studio opens on the per-user settings file (STUDIO-18)

- **The window loads `%APPDATA%\Orkeon\appsettings.json` at startup** (`$XDG_CONFIG_HOME/Orkeon/appsettings.json`
  elsewhere) — the file `orkeon init` writes and the last step of the CLI's resolution
  chain. « Settings › Authorized folders » used to open **empty** on a file declaring two
  folders: the editor started on an empty document, and only the expert's *Load* button
  ever read the file. The location stays *Global* (the same file under its own name); a
  machine without the file starts empty with no message; an unreadable file is reported on
  the status line and nothing is saved over it (`ConfigTabViewModel.InitializeAsync`).
- **A novice edit no longer clobbers the file.** The auto-save wrote the empty document plus
  the one edited key over a file that also held the `Llm` section, dropping it; the loaded
  document keeps every key Studio does not model, as the expert's Load/Save cycle always did.
- **Declaring a folder reaches every verdict at once.** The team cards' red chips and the Run
  screen's refusal were computed on an adoption, a target pick or a restart only; every edit
  of the declared list now refreshes them, and the cards built before the file was read are
  rebuilt once it is.
- The screenshot campaign no longer loads the settings file itself (`CaptureShellBuilder.PrepareAsync`):
  the shell does, as the real window does.

<!-- LLM-10 -->
### Changed — the output cap defaults to the model's documented maximum, not 4096 (LLM-10)

- **`LlmConfig.MaxTokens` is a pin, nullable, null by default.** Nothing pinned means the
  request carries the model's **documented maximum output** from the new
  `Orkeon.Constants.Llm.LlmModelOutputLimits` catalogue (each entry with its vendor page and
  the date it was read, 2026-09-19): 128 000 on `gpt-5.6-sol` / `gpt-6-astra` and the Claude 5
  generation, 393 216 on `deepseek-flash`, 131 072 on `kimi-k3` (the vendor's own default),
  the GLM-5 and Qwen 3.7/3.8 families and `MiniMax-M2`, 65 536 on Gemini 3.x Flash, 128 000
  on `grok-4.6`; **no cap at all** where the vendor documents none (Mistral: prompt plus
  `max_tokens` may not exceed the window, so the field is left out; Ollama: `num_predict` is
  left out, the runtime generates to its context); the window itself on Together, whose
  provider now sends `context_length_exceeded_behavior: truncate`; and **4096 only for a model
  the catalogue does not know** — the value the engine used to send for every model, which a
  reasoning model spent thinking and answered empty (owner recette 2026-09-19, `kimi-k3`: two
  green runs, one deliverable). A pinned value — `Llm:MaxTokens`, `ORKEON_Llm__MaxTokens`, a
  Studio profile, a crew's `max_tokens` — always wins, including an explicit 4096, which the
  old `!= 4096` sentinels could not tell from "unset". Resolution lives in one place,
  `LlmConfig.ResolveMaxTokens(provider, defaultModel)`. Unverified figures stay out: MiniMax-M3
  (no vendor figure), HuggingFace's router (per routed provider).
- **A catalogue cap the endpoint refuses is retried once without the field**, with a warning
  naming the model (`OpenAICompatibleProviderBase.TryDropCatalogueOutputCap`, matched on the
  field's name in the vendor's own wording); a pinned value's rejection surfaces unchanged.
  `TryAdaptRejectedPayload` now receives the effective config; Kimi's temperature self-heal
  defers to it.
- **Studio's profile editor says what an empty « Maximum response » means for the chosen
  model** — the documented maximum, no cap, a local runtime, or « not in the catalogue: 4096,
  pin it » — and follows the model and provider as they are edited (four keys in the five
  resx replace the static hint). `orkeon init` writes no `MaxTokens`; the example settings
  templates drop theirs where the model is in the catalogue.
- Breaking in the public API: `LlmConfig.MaxTokens` is `int?`, `CreateValidated(maxTokens)`
  takes `int?` (null = unpinned), `OllamaRequestOptions.Builder.AddNumPredict(int?)`, and the
  YAML exporter writes `maxTokens` only when pinned.

<!-- STUDIO-17 -->
### Added — Studio: the Run screen shows the task in progress, pulses while it runs, stamps its journal and starts each launch clean (STUDIO-17)

- **`task.started` joins the run event stream** (`orkeon run --events jsonl`): every
  orchestration mode announces a task the moment its agent is chosen, through a new
  `ICrewExecutionHook.OnTaskStartedAsync` (`TaskStartSnapshot`: task, agent role, start
  time) dispatched by `CrewHookDispatcher` like the completions. The payload is deliberately
  thin — nothing has been measured yet. The envelope stays at `v: 2`: an older client ignores
  the kind, and a client must not refuse a `task.completed` it never saw started. The
  hierarchical mode's `task.completed` now carries the agent's **role** under `agentRole`,
  as every other mode does, instead of the agent's GUID.
- **The Run screen shows what is running now.** `RunProgressModel` folds `task.started` /
  `task.completed` into an in-flight list and `tool.called` / `tool.returned` into the tool
  at work; the card gets a row per running task — agent, turning glyph, « since HH:mm:ss »
  from the run's own clock — an activity line naming the tool, a summary that says
  « N finished, M in progress », and the number of tool calls on every finished row. The
  « running » badge pulses while the child process lives (a Style-scoped storyboard with
  no `TargetName`; `CapturePose` holds it at full opacity for the screenshot campaign).
- **Every launch starts clean.** The journal and the previous run's verdict — exit badge,
  result row, « Open the result » — are cleared once per click, so a « validate first »
  launch keeps both passes in one journal. « Clear the journal » stays; « Copy » is how a
  journal survives.
- **The technical journal is timestamped.** Each line shows the local time Studio read it,
  and the copied text leads every line with it. The CLI's own log lines keep their
  millisecond prefix.
- **The engine names the cause of an empty first turn.** The warning « produced empty final
  message after N iterations » now carries the request's `max_tokens` and says that the
  tool-free retry cannot call tools and that a reasoning model needs `Llm:MaxTokens` 16384
  or more. Owner recette 2026-09-19: a `kimi-k3` profile without `MaxTokens` (engine default
  4096) spent the budget reasoning, answered empty, and the tool-free retry narrated the
  `.docx` instead of writing it — a green run with no file. The remedy is the profile's
  *Max tokens* field; the journal now says so at the moment it happens.

<!-- LLM catalogue review 2026-09-19 -->
### Changed — the DeepSeek default follows the vendor's rename, vendor prices refreshed, catalogues reviewed (2026-09-19)

- **`LlmProviderDefaultModels.DeepSeek` is `deepseek-flash`.** DeepSeek retired V4 Flash and
  V4 Flash Vision Exp on 2026-09-10 with the V4.1 Flash release: `deepseek-v4-flash` and
  `deepseek-v4-flash-vision-exp` are legacy names the API "temporarily" routes to V4.1 Flash,
  and `deepseek-flash` is the vendor's own id for the Flash tier — 1M context, 384K output,
  thinking on by default, native vision. Every DeepSeek configuration that names no model
  follows; the campaign kit, the `orkeon init` template, the Studio DeepSeek card, the
  examples and the guides name the new id, and the kit's `visionModel` companion is gone (the
  default sees). The 2026-09-07 campaign measured V4 Flash under the old name and is to be
  replayed. This is the only default that changes: every other default is served today, and
  the newer models found are recorded as candidates pending a campaign — the Mistral lesson.
- **`ModelPricingRegistry` follows the vendors' pricing pages (read 2026-09-19).** OpenAI:
  `gpt-5.6-sol` 4 / 20 $/M (promotional through 2026-11-21; was 5 / 30), `gpt-5.6-terra`
  2 / 12, `gpt-5.6-luna` 0.20 / 1.20, `gpt-6-astra` 10 / 50 added (its 2× billing above 272K
  input tokens is not expressible in a flat registry). Anthropic: `claude-sonnet-5` 2 / 10 (the
  rise to 3 / 15 was cancelled), `claude-fable-5-1` and `claude-fable-5` 10 / 50,
  `claude-haiku-4-5` 1 / 5 added.
- **Catalogue review of the 16 providers.** `docs/reference/llm-providers-comparison.md` gains
  a "Defaults and newer models" section (EN/FR) and the `LlmProviderDefaultModels` comments
  carry the dated findings: `gpt-6-astra` (no `none` effort, so the function-tools workaround
  cannot follow it), `claude-fable-5-1`, `gemini-3.8-flash` (GA 2026-09-02, same price until
  2026-12-31), the `glm-5.3` family (thinking cannot be disabled), `qwen3.8-max` /
  `qwen3.8-flash` (no `qwen3.8-plus`), `kimi-k3` (fixed sampling, always reasons),
  `MiniMax-M3`, Together's `zai-org/GLM-5.3-Flash`, HuggingFace's `Qwen/Qwen3.5-9B` (the
  current default has tool support on one routed provider out of four), Ollama's small
  tools + thinking models. Nothing new for chat at Mistral and xAI.

<!-- STUDIO-14 -->
### Added — team folders travel with the team, and the forge trial reads where the documents are (STUDIO-14, lots 0 and 1)

- `orkeon forge … --read <dir>` (new session and `resume`): the trial mounts `<dir>` as
  `/workspace`, read-only, in place of the working directory — which keeps every other role:
  the session still lives under its `.orkeon/forge/<slug>/`, the settings still resolve next
  to it. A folder that does not exist is refused before any session is created (`--read names
  no directory`, exit 1); `promote` refuses the option, since it mounts nothing. A read folder
  outside the process working directory is whitelisted for the file tools the way `orkeon run`
  whitelists its script directory — the forge's mounts are its own three roots. This is the
  engine hook Orkeon Studio's wizard uses to try a team on the folder chosen at its first step.
- Studio's team sidecar (`studio-team.json`) records a team's own folders **relative to the
  team**: a physical segment starting with `./` (`./input:/workspace:ro`,
  `./output:/output:rw`) is a folder inside the team folder, so a team copied, exported or
  moved keeps writing into its own folder without a rebase step. `TeamCatalog` resolves the
  entries once, at its boundary (`Describe`, `DescribeTarget` and `List` return absolute
  paths; the raw entries stay on `Metadata.Mounts`), relativizes on every save (`SaveMetadata`,
  `SaveMounts`) and creates each relative folder there — the single point where an in-team
  folder is materialised. Duplicate, export and import copy the entries verbatim and rewrite
  an older absolute in-team path relative on the way; `TeamCatalog.RebaseMounts` is removed.
  `DeclaredMounts.IsInsideTeam` is public and vouches for a `./` entry before the team folder
  exists; `MountValidator.Validate` takes an optional `teamDirectory` and skips the existence
  check of a relative entry until one is known. `TeamMountPaths` (Studio.Core) is the one
  helper for the convention, and `FolderPolicy` names the step-1 choice the wizard asks
  (Later / InsideTeam / ExistingFolders — the wizard entry below).

<!-- STUDIO-14 recette -->
### Fixed — the forge trial no longer refuses a settings file that names `/output` or `/workspace` (STUDIO-14, owner recette)

- `orkeon forge` refused any settings file whose `Orkeon:FileSystem:Mounts` named
  `/workspace`, `/forge` or `/output` (`ERROR: '/output' is a virtual root reserved by the
  runner…`, exit 1). That guard predates STUDIO-15: since a `--mount` is placed by virtual
  root, the forge's own three mounts replace such an entry for the trial, and the
  `Duplicate virtual paths` crash the guard pre-empted cannot happen. Studio's wizard hit it
  on its first real team — the folder associated at step 1 is declared in the allowed folders
  as `/output`, the Studio convention for a team's write folder, and the trial refused to
  start. The forge now reserves `/sandbox` alone (the one root it does not mount itself); a
  settings entry on `/workspace`, `/forge` or `/output` is replaced for the trial and logged as
  `mount /output: --mount replaces the settings entry`, a line the forge now lets through to
  stderr. `RunnerVirtualRoots.ForgeReserved` is removed.

<!-- STUDIO-14 settings -->
### Added — Studio: Settings › Authorized folders lists each team's own folders, read-only (STUDIO-14, lot 6)

- A team's own folders (`./input`, `./output`…) are vouched for by living inside the team
  and are never written to the global `Orkeon:FileSystem:Mounts` — that would duplicate the
  sidecar in a file every team shares and put one team's private folders in the list every
  other team picks from. The settings screen now shows them all the same: the folders tab
  ends, in both modes, with a read-only « Team folders » section (`TeamFoldersViewModel`,
  `SettingsScreenViewModel.TeamFolders`) — one line per in-team folder of each adopted team,
  « Veille concurrentielle · /output → output » with the one-word rights badge, read from the
  sidecars (team-relative entries, and the absolute-under-the-team spelling of an older
  sidecar), never a disk path. No command, nothing written; the hint says these folders are
  changed from « My teams ». The section follows every change to the team list (adoption,
  import, deletion, duplication, the folders modal) and re-reads the sidecars when the tab
  opens. Keys `Studio.Settings.TeamFolders{Title,Intro,Empty,Row}` in the five cultures; the
  capture seed gives `rapport-hebdo` an in-team `./output:/output:rw`, photographed by
  `reglages-dossiers`.

<!-- STUDIO-14 wizard -->
### Added — Studio wizard: where the folders live, asked at step 1; « Create inside the team »; the trial reads the chosen folder; « Open the folder » (STUDIO-14, lots 2–5 and 8)

- Step 1 « Describe » gains a fourth question, « Where are your folders? », with three chips
  and no obligation — composing never waits for it. « Existing folders » shows the two rows a
  team can address before it has a blueprint (« Your documents » `/workspace` read, « The
  results » `/output` written); « Choose the folder… » on either opens the **disk picker** on
  the row's rights (read-and-write for the results), and the pick is declared in Settings ›
  Authorized folders unless already held, saved, then bound behind the row under the row's
  rights — one gesture, the status line says which happened, a refused save still binds. A
  folder picked inside the reopened team is bound and never declared. « Created inside the
  team » answers both rows team-relative on the spot (`./input:/workspace:ro`,
  `./output:/output:rw`), read « inside the team: input / output », nothing created before
  the adoption. « Later » behaves as before. The chips move the two canonical roots only;
  composing keeps them; « Restart », a resume and « Modify » forget them, policy included.
- The two rows are a start, not a limit (owner review of 2026-09-19): « Add the folder » names
  as many further mount points as the need calls for — a name the agents will use, read or
  written — each a row answered the same two ways (« Created inside the team » answers a new
  one on the spot), shown on the Composer step next to the blueprint's roots, kept across
  compositions, created inside the team at adoption when left unanswered; a folder added at
  step 1 through the picker counts as one of them. A reserved or already-used name cannot be
  added. Keys `Studio.Create.AddRoot`, `AddRootWrite`, `AddRootHint` in the five languages;
  the `etape1-dossiers-existants` stop photographs a third, user-named row.
- Step 2 « Compose » re-shows the step-1 answers, editable as before, and gains « Create every
  folder inside the team » beside « Allow a folder » plus a per-row « Create inside the
  team ». Under the inside-the-team policy a root a later blueprint adds is answered the same
  way as it appears, unless dropped. A row inside the team shows a label, never a disk path,
  and never reads red: `DeclaredMounts.IsVouchedFor` (declared, or the team's own) is now the
  rule of the wizard's rows and of the "My teams" cards, as it already was of the launcher —
  a team's own `/output` no longer reads red on its card. The `input/` warning stays for a
  read root answered inside the team: that `input/` is created just as empty.
- The adoption records team-relative entries for every in-team answer and every root the
  blueprint addresses that nothing answered (`CreateTeamViewModel.SidecarMounts()`, replacing
  `WithDerivedWriteMounts(teamDirectory)` — no team directory needed any more; the save
  creates the folders). « Modify » on a card seeds the rows from the sidecar's own spelling,
  so `./output` reads « inside the team » and is written back as it is.
- The trial reads where the documents are: every engine invocation of the wizard carries
  `--read <dir>` (`ForgeStartRequest.ReadDirectory`, `ForgeArgumentsBuilder`) with the folder
  bound behind `/workspace` — a real folder as it is, a reopened team's own `input/` resolved
  under the team — and nothing before the team exists, where the argv is unchanged and step 3
  says the trial runs on an empty folder. An engine that predates `--read` meets it only when
  a folder is known, and then refuses it on the failure card (STUDIO-13).
- The Run screen turns `--allow-external-mounts` on by itself when a team folder is a real
  folder outside the team, and off when every team folder resolves under it — the launch's
  working directory needs no flag (STUDIO-12 C4, Studio half); the expert checkbox stays for
  the per-launch mounts.
- « Open the folder » in the wizard's header, in both modes at every step: the working
  session — which holds the generated `crew/` — before the adoption, the adopted (or
  reopened) team afterwards, the tooltip says which; present whenever a shell opener is
  wired, like the team cards' button (`CreateTeamDependencies.ShellOpener`).
- `FolderPickerViewModel.Open` takes an `initialRights`; three capture stops
  (`etape1-dossiers-existants`, `etape1-dossiers-equipe`, `etape2-dossiers-dans-equipe`);
  fourteen new `Studio.Create.*` strings in the five languages.

### Fixed — Studio: team cards bounded, the rights badge in one word, the closed rights list says its label (STUDIO-16)

- A team created from a long brief — a README pasted into the name field — filled the
  Run screen's team card and the "My teams" card with the whole page: a WPF `TextBlock`
  renders line breaks even without wrapping and has no `MaxLines`. The name is now one
  line everywhere it is displayed (run card, team card and its tooltip, launch history)
  through `TeamCatalog.NormalizeName` — first line, Markdown stripped, cut at a word
  under 64 characters, the slug's cap, never empty — and adoption writes that normal
  form into the sidecar for every name, typed or proposed; the "Team name" field shows
  it live; import normalizes the name of the sidecar it copies. `ShortName` (48
  characters, applied only to a proposed title) is gone, replaced by the Core rule.
- The Run screen's meta line appended the whole `description` after the agent count and
  the folders — the actual text of the owner's capture, a README in the muted 12 px of
  the meta line, not the bold headline — and the "My teams" card showed it unbounded.
  Both now read `TeamSummary.Summary` / `TargetDescription.Summary`: the first
  paragraph without markup, cut at a word under 240 characters
  (`TeamCatalog.Summarize`), derived at read time and never stored — the sidecar keeps
  the whole need. On "My teams" the block is folded to three lines with a "Show more" /
  "Show less" link shown only when the folded card hides something; unfolded, it shows
  the whole need.
- In Settings › Authorized folders, the rights badge of a mount row carried the full
  52-character label and pushed the virtual name out of the list; the selected
  writable row was seven lines tall with the badge floating in its middle — the badge
  measured first took the whole row, the name was measured at width zero and, wrapping
  (`MonoText`), broke `/output` into one character per line. The row now puts the name
  first, never shrunk, and the badge last in one word (`MountEditorViewModel.RightsBadge`,
  `MountRightsTokens.GetBadge`, keys `Studio.Settings.RightsBadgeRo/Rw/Rwnd` in the
  five cultures) with the full label as its tooltip.
- The three `DisplayMemberPath` drop-downs (the two rights lists, the trial screen's
  team picker) showed `MountRightsChoice { Rights = ReadWrite, … }` once closed: the
  global `ComboBox` template bound `SelectionBoxItemTemplate` but not the item template
  selector `DisplayMemberPath` goes through. The template now binds
  `ContentTemplateSelector` and `ContentStringFormat` like the stock one; a XAML guard
  (`ComboBoxTemplateConformityTests`) pins the four bindings.
- The capture seed gains a team whose sidecar is a pasted README (multi-line name,
  forty-line need), photographed by `executer-carte-equipe` and `equipes-liste`;
  `reglages-dossiers-selection` now selects the writable mount.

<!-- LLM-09 -->
### Added — OpenRouter and Mammouth AI, the fifteenth and sixteenth providers — two aggregators, the one exception to the scope freeze (LLM-09)

The scope freeze said "no 15th provider — point `Orkeon:Llm:BaseUrl` at the endpoint", and
that fallback works today for both. What it cannot do is what the owner lifted the freeze for
on 2026-09-18: declare capabilities (the fallback inherits OpenAI's, so an option the
aggregator ignores is a silent drop), read what the aggregator writes, and be recognised by
Studio, `orkeon doctor` and the campaign kit. Both arrive documentation-first, the MiniMax
way: every declaration is sourced from the vendor's documentation and from cold calls dated
2026-09-18, marked campaign-pending in the code, the comparison table (two † rows) and the
campaign catalogue — the compiled defaults are claims until a live M1 is archived.
`OpenRouterLlmProvider` (`openrouter.ai/api/v1`, key `openrouter`, `OPENROUTER_API_KEY`,
default `google/gemini-3.7-flash` — the fleet's Gemini default under the marketplace's
mandatory `vendor/model` id, so a red behind the aggregator with a green in direct is a fact
about the transport; `openrouter/auto` refused as a default because it drifts) declares
`JsonSchema` / `Budget` / vision, reads its `reasoning` field, writes the thinking controls
as the `reasoning` request object (`enabled` / `effort` / `max_tokens`, the base's
`thinking` block and first-level `reasoning_effort` removed so one intention is not sent
twice), exposes `usage.cost` and its breakdown (`upstream_inference_cost`, `is_byok`,
`cache_write_tokens`, `reasoning_tokens`) and the served `model` as `served_model`, and
sends the two constant attribution headers `HTTP-Referer` (spelled as the vendor documents
it) and `X-OpenRouter-Title`. `MammouthLlmProvider` (`api.mammouth.ai/v1`, key `mammouth`,
`MAMMOUTH_API_KEY`, default `gemini-3.7-flash` — the same model under the bare id the
proxy serves) declares nothing the vendor does not document (`response_format` and thinking
stay `None` with the structured warning, vision follows the vendor's `text, image` list) and
overrides no behaviour. Routing is by host or explicit key for both, plus the unambiguous
`openrouter/` model prefix; a `vendor/model` id alone is not an OpenRouter claim (HuggingFace
and Together use the shape), a bare Mammouth id is the vendor's own string, and the
pre-existing vendor-prefix inference that ignores the `/` is pinned as is — eight routing
rows, negative pins included. The `sk-or-v1-` key prefix is documented by secondary sources
only, so the factory does not infer from it until the first key confirms it. Full fleet
integration otherwise: satellite constants and both `PublicAPI.Unshipped.txt`, factory,
`llm.openrouter` / `llm.mammouth` in the scripting DSL (with `Orkeon:DefaultLlmProvider`
resolving their own models rather than the platform default), doctor, `orkeon llm
probe|models`, Studio cards and detection in five locales, the campaign catalogue with the
questions each first campaign must settle, example settings, and the counts 14 → 16 under
the claims gate. The scope freeze is rewritten in `CONTRIBUTING.md`, `CONTRIBUTING.fr.md`
and `docs/reference/limitations.md` (EN/FR): sixteen providers, two of them aggregators, no
17th — the rule stands for everything else.

### Changed — the OpenAI-compatible base reads what an aggregator writes, and never ends a failed stream cleanly (LLM-09)

Three changes on `OpenAICompatibleProviderBase`, all generic. The reasoning field is a
dialect hook: `ReasoningFieldName` (default `reasoning_content`) names the field the buffered
parser and the stream accumulator read the trace from, and an overload
`ExtractReasoningContent(JsonElement, Builder, string fieldName)` joins the frozen
two-argument one, which delegates to it — DeepSeek, Z.AI and MiniMax parse exactly as before,
their tests untouched; the Orkeon metadata key stays `reasoning_content` whatever the vendor
calls the field. `usage.cost` becomes the `cost` metadata (double) on the buffered path and
from the final usage chunk of a stream — exposed, not accounted for: `CostBudgetManager`
keeps estimating from the pricing registry. And a chunk carrying a root-level `error` after
the HTTP 200 — OpenRouter's documented mid-stream failure shape, and the shape any compatible
vendor writes — used to be skipped as "no choices": the chat stream completed *cleanly* with
truncated content, and the token stream ended normally. Each path now ends the way its
pre-stream refusal does: `ChatStreamingAsync` completes with the `error` / `error_type`
metadata over the content received so far (no reassembled tool calls), `GenerateStreamingAsync`
throws the `HttpRequestException` `StreamingRejectionAsync` throws, carrying the vendor's code
as the status when it is an HTTP one. Secrets in the vendor's message are redacted; a
`"error": null` on a healthy chunk is not an error.

<!-- STUDIO-15 -->
### Changed — a `--mount` is placed by virtual root against the settings (STUDIO-15, STUDIO-12 C3/C4)

- **`RunnerHost` places every `--mount` by virtual root.** On a root the declared
  `Orkeon:FileSystem:Mounts` array (settings file *and* `ORKEON_` environment) already holds,
  the `--mount` is written at that entry's index and replaces it for the run — logged as
  `mount /x: --mount replaces the settings entry`; on a new root it is appended after the
  highest declared index. A settings entry and a `--mount` naming the same root used to reach
  `FileSystemRegistry` as two mounts and fail every such run at kickoff with `Duplicate virtual
  paths` out of a DI factory — which is exactly what Studio's team flow produced by design,
  since a team associates a folder the settings already declare, verbatim. `InternalMounts`
  keep appending. Only a case that failed changes outcome.
- **`RunnerExecution.EnsureVirtualRootsAreUnique`** (public, wired in the YAML runner's
  `TryBuildHost` and in `orkeon run`'s script path): two `--mount` on one root, or a settings
  file declaring one root twice, exit 1 with one actionable line (`ERROR: '/x' is mounted
  twice on the command line: <a> and <b>. Keep one.` / `… declared twice in <settings>`)
  before any host is built. `Using settings: …` is now printed once every mount guard has
  passed, so a refusal never reads as a run that started.
- **A DI-factory failure at kickoff ends stderr with `ERROR: <message>`** as the last line
  (the sentence exit code 2 promises); the `Crew execution failed` log entry carries the
  message only, and the exception with its stack trace moves to a `Debug` entry (`--verbose 2`,
  or `ORKEON_DEBUG=1` on stderr).
- **A mount declared in the settings is always whitelisted for `PathValidator`** (STUDIO-12
  C4): its base path joins `PathSecurity:AdditionalAllowedDirectories` without any flag — a
  declared folder is the machine owner's explicit intent. Until now such a folder outside the
  process working directory was mounted and every access refused as "outside the allowed
  workspace directory", and `--allow-external-mounts` could not rescue it since it only
  whitelists `--mount` arguments — which it still does, and only that.
- **Studio predicts the by-root rule** (`MountOverrideSemantics.ComputeEffectiveMounts`,
  the `Studio.Settings.Explanation` / `ThisLaunch` / `EffectiveReplaced` strings in the five
  languages): the effective-mounts table shows one row per root, the settings entries in
  place, `--mount (replaces «…»)` on a replacement, and the runner's own `/crew` mount
  appended after the declared entries — instead of the index-0 masking that never happened.
  An empty `--mount` entry is reported against the `--mount` option rather than a
  configuration key the arguments alone cannot know.
- **Studio never lays a settings entry twice** (`LaunchMountPlan.WithoutSettingsDuplicates`):
  a team folder the settings already hold — same folder, same name, same rights — stays off
  the command line; the same folder under another name, or with other rights, is laid and
  shown as the replacement it is. The Run screen's settings-mounts list now follows the
  settings the run will find: the pinned `--settings` file, or « Settings › Authorized
  folders » in automatic mode.

### Added — Studio: a failed « Composer l'équipe » is said, with the technical part copyable (STUDIO-13)

In novice mode the creation wizard kept its promise of a screen without machinery right up to
the first failure, where it showed nothing at all: no `orkeon` binary, an `appsettings.json`
the engine refuses, a non-zero exit — a click, a spinner, then step 1 again, with the reason
on a grey one-line status (truncated, untranslated) or nowhere, and the whole stderr in a
technical journal only the expert mode displays.

- `CreateTeamViewModel` carries one failure model, `WizardFailure` (`Kind`, `Headline`,
  `Detail`, `CommandLine`, `ExitCode`, `Stderr`, `EngineError`, `Journal`), exposed as
  `Failure` / `HasFailure`, with `BuildFailureReport()`, `CanCopyFailureReport` and
  `FailureReportCopied` on the diagnostic screen's pattern. Five families, classified on the
  run outcome, the engine's `error` event and the exit code: `EngineMissing` (the locator found
  no binary — `NotStarted`), `ConfigRefused` (an `error` event with `recoverable: false`, a
  `FORGE-*` code), `EngineStopped` (non-zero exit, with or without stderr — and a session that
  reported `failed`), `Unknown` (an exception out of the launch, which used to reach a
  `MessageBox` at best — `RunEngineAsync` now catches it, cancellation excepted) and
  `PromoteRefused` (step 4, no `promoted` event). « Arrêter » never produces one.
- The report is the `orkeon forge` command line, the exit code, the engine's code and message,
  the detail the card shows and the whole journal — the WHOLE stderr, where the status line
  only ever kept the last line.
- `CreateTeamView` shows the card under the status line in **both** modes, bordered in the
  danger colour on the assistant gate card's pattern: the novice sentence of the family
  (localized, five cultures), the engine's own text in mono (wrapped, bounded, scrollable,
  never translated), and « Copier le rapport » (« Copié ! » for 1.6 s), « Réessayer »
  (the compose at step 1, the save at step 4) and, by family, « Ouvrir le diagnostic » (the
  doctor re-runs on arrival) or « Ouvrir les réglages ». It clears on the next composition and
  at the start of every run; the status line, the expert journal and the fault `MessageBox`
  of the other commands are untouched. `Studio.Create.StatusFailed` now says where the detail
  is («the detail is in the card below»), true in both modes.
- Capture campaign: a stop `etape1-echec-moteur` (both modes, both themes, language sweep)
  photographs the card by taking the CLI away from the seeded machine and clicking — the
  owner's own recipe, through the real locator; with the three STUDIO-14 stops the
  catalogue is pinned at 52 stops, 282 shots.
- Tests: `A_missing_engine_is_said_on_step_1_with_a_copyable_report`,
  `A_non_zero_exit_without_stderr_still_shows_a_failure_card`,
  `An_unrecoverable_engine_error_names_its_code_and_message`,
  `The_failure_report_carries_the_command_line_the_exit_code_and_the_whole_stderr`,
  `A_new_compose_clears_the_previous_failure`, `A_refused_promotion_uses_the_same_failure_card`,
  `An_exception_during_the_launch_becomes_a_failure_card_rather_than_a_fault`,
  `Stopping_the_engine_raises_no_failure_card`.

<!-- STUDIO-12 -->
### Fixed — the team journey on a real crew: order, empty answers, single-file teams (STUDIO-12)

- **Task order follows `dependencies`, in every layout.** Without a plan, the sequential
  strategy — and the hierarchical, consensual, graph and autonomous modes, which hand
  their tasks out one after another — now runs the crew's tasks in a stable topological
  order on their declared dependencies (`TaskExecutionOrder` in the Domain,
  `CrewTaskSequencer` in Infrastructure): a task runs after every task it depends on, and
  the declared order is kept wherever the dependencies allow it. The multi-file layout
  lists tasks in the ordinal order of their file names, so `consolidate.yaml` ran before
  the `extract.yaml` it depends on — silently, with a green run. An unknown dependency is
  ignored; a cycle keeps the declared order for the tasks caught in it and logs a warning.
  The orchestrator no longer synthesises a "plan" from `crew.Tasks` when planning is off.
- **An empty final answer is never a green run.** A model that answers with empty text —
  and empty text again on the tool-free retry — exits `AgentExitReason.EmptyFinalAnswer`
  (new member) in the three agent loops, including on the very first turn, which used to
  pass as `Completed`; the task fails with the reason in `TaskResult.Error` (a failed exit
  used to leave it null), a sequential crew with a failed task reports failure with the
  reason (`CrewOutput.Error`, new; `CrewHookStatus.Failed` → AUTO_SUMMARY.md and the
  `error` run event), and the one-shot runner exits **2** with `ERROR: <reason>` as its
  last stderr line — it returned 0 for every crew failure, so `run.finished` said
  `success:true` over an empty output mount. The reason names `Llm:MaxTokens`, the
  setting that usually explains it for a reasoning model.
- **Studio: `MaxTokens` on the model profile.** `ModelProfile.MaxTokens` (null = the
  engine default, 4096), emitted as `ORKEON_Llm__MaxTokens`, round-tripped by the store
  and editable in the profile editor next to temperature and timeout, with a hint that a
  reasoning model needs 16384 or more.
- **Studio: the profile store reads case-insensitively and says when a file is unreadable.**
  A hand-written camelCase `studio-model-profiles.json` loaded zero profiles, silently;
  a file that fails to parse was indistinguishable from an empty one. `LoadAsync` now
  returns a `ModelProfileLoadResult` whose `Error` the settings screen shows.
- **Studio: a folder holding a single-file crew is a team.** The target detector resolves
  a folder with no layout marker and no script but one `*.yaml`/`*.yml` file as that file
  (`RunTargetKind.SingleFileCrewDirectory`: run path = the file, selected path = the
  folder, at the root or under the promoted `crew/` nesting); several YAML files resolve
  to `crew.yaml` then `config.yaml`, else are offered as candidates. `TeamCatalog.Import`
  runs the detector before copying and refuses, with the detector's message, a source it
  cannot resolve — it used to copy any folder verbatim into a team nothing could run.

### Changed — the scripting runtime runs its loops in JavaScript (SCR-25)

A Jint engine has one event loop and one drainer at a time, and the runtime kept
re-entering it from the wrong side: C# loops that invoked script functions after an
`await`, or blocked on a promise from inside one of the engine's own jobs. Measured through
`ScriptHost` against a provider that really suspends (`Task.Delay(3)`, the shape of every
HTTP provider): a crew run placed after *any* top-level `await` hung — a file read or a tool
call before `crew.run()` was enough, and the second of two sequential runs hung too; a state
graph whose nodes suspend timed out at 10 s from a body, and hung outright when `run()`
followed an `await`; five concurrent `ctx.state.with` crashed the engine or timed out; a
topic handler that awaits so much as a microtask timed out when `publish()` came after the
body's first `await` — and was delivered when it came before; FSM `onEntry`/`onExit` that
await the model timed out; an async `onAgentStart` reached through `ctx.spawn` timed out; an
async `onCrewStart` never returned; `act`'s `onDelta` ran on a pool thread while the body was
drained on another; and `for await` over `runStream` threw *The value is not iterable*. The
fifteen reproducers of `EngineThreadingContractTests` pin the family; all of them run.

The fix is one rule, applied everywhere: **every loop that calls back into script code lives
in JavaScript**. `crew.run`/`runAgent`/`runStream`, `stateGraph.run`/`runStream`,
`stateMachine.send`, `ctx.state.with`, `topic.publish`, the event's `lock` and `ctx.llm.act`
are async functions (generators for the streams) built from a JavaScript factory; the CLR
hands them synchronous helpers and `Task`s to await, and never re-enters the engine from a
continuation. A helper that fails throws *in JavaScript* (`JsHostError`: an `Error` carrying
the CLR exception on `clr`), so the script's `catch`/`finally` run and the typed exception
comes back out. The CLR drives the engine at three root pumps only — `ScriptHost` for the
script, `JsCrew.RunAsync` for the `globalThis.crew` handoff, `JsTool.CallAsync` for a script
tool the orchestrator calls — each on an engine at rest, under the per-engine gate, one
thread for the whole evaluation, bounded by its cancellation token: the three 30-minute
promise ceilings (`BodyPromiseTimeout`, `ToolPromiseTimeout`, `LoadPromiseTimeout`) are gone.
The model is written up in `docs/architecture/scripting.md` (EN + FR).

**Breaking for C# callers.** `JsCrew.run`, `runAgent` and `runStream`, `JsStateGraph.run`
and `runStream`, `JsStateMachine.send`, `JsAgentContext.stateWith`, `JsEventTopic.publish`,
`JsPublishedEvent.lock` and `JsLlmFacade.act` are `JsValue` properties now — the JS functions
themselves, not CLR methods. A host runs a crew through `JsCrew.RunAsync(options, ct)`; the
script surface is unchanged, and so are the typings but for one correction: `onCrewError`'s
second argument is declared `string` — the displayed message the runtime has always passed —
where `crew.d.ts` promised an `Error` no script could read `.message` off. What one loop
doing the work of several changes for scripts:

- An `onCrewStart` that throws fails the run and reaches `onCrewError` (it ran outside the
  error path before); a run started on an already-cancelled `signal` skips `onCrewStart`,
  and `onCrewError` still runs (a C# caller with a cancelled token gets
  `OperationCanceledException` before any hook). An `onCrewError` that throws itself is
  logged and the *original* error is what the caller receives — it used to replace it.
- Cancellation bypasses `onError`: a cancelled run rethrows without consulting the policy
  (it used to consult it with code `unknown`, then throw at the next step anyway). And
  `err.code` for a failure that arrived as a faulted `Task` maps from the innermost CLR
  exception (`receive_timeout` for a `ctx.receive({ timeout })` that expired, …) instead of
  `unknown` for the wrapper.
- An async `onAgentStart`/`onAgentStop` that rejects is logged as a warning; it no longer
  throws out of `crew.add`, `ctx.spawn` or `crew.remove`, which stay synchronous and never
  drain the hook.
- `crew.runAgent` runs its target under the instance semaphore, with an `AgentContext` and
  the agent's own `onError` policy; it accepts a name or an `Agent`, honours `opts`
  (`signal`, `timeout`), and throws `RecursiveAgentInvocationException` when called on the
  agent whose body is executing instead of deadlocking on the semaphore that body holds. It
  used to invoke `body(input, undefined)` bare.
- A run opened from a body — `crew.runAgent`, `await sub.run()` on another crew — without
  `{ signal: ctx.signal }` is a child of the run that opened it: cancelling the outer run
  cancels it, and its semaphores come back inside the outer run's own unwind. It used to run
  on with no token of its own, and a sub-crew whose body never settled kept its semaphore for
  good. The link is best-effort under runs interleaved on one event loop (it reads the most
  recently opened attempt still open); the explicit `signal` remains the exact form. And a
  cancellation the loop meets on a Task it awaits itself — the instance semaphore, a retry
  delay — rejects the script with the same bridged `OperationCanceledException` (`clrType`,
  `message`) as a raced body await, not Jint's raw cancelled-task object.
- `crew.runStream` *is* the run, observed as a stream: same semaphore, contexts, hooks and
  spans as `run`; `at` is epoch milliseconds (`Date.now()`) instead of .NET ticks; `break`
  out of the `for await` ends the run without firing `onCrewComplete` or `onCrewError`.
- `ExecutionTimeout` and `MemoryLimitBytes` (`Orkeon:Scripting:Limits`, 30 s and 100 MB by
  default) span a whole CLR-driven run — the `globalThis.crew` handoff through
  `ScriptHost.RunAsync`, `JsCrew.RunAsync` — as they already spanned a whole script; both
  used to re-arm per agent body (Jint resets its constraints at each non-nested entry, and
  the run is one entry now). A host driving a longer run, or a many-agent one that allocates,
  raises the limits, as a long script already required.
- `ScriptHost.RunAsync` and `JsTool.CallAsync` propagate cancellation as
  `OperationCanceledException` — a 30-minute wait that ignored the token, and a failed
  `ToolCallResponse`, before.

### Fixed — the quickstart's first CI run failed, and three things came out of it

The first run of `quickstart.yml` on a GitHub runner did everything the README says —
pinned Ollama, the tool installed from the checkout, the settings found next to the crew —
and wrote no file: `llama3.2:1b` under Ollama 0.34.0 answered the `file_write` call as
*text*, the whole JSON envelope with a broken string inside (`"content": "create_backup":
"False"`), the loop took that text for the final answer, and the run "succeeded" with the
envelope as its deliverable. Reproduced here byte for byte with the same Ollama build (four
runs out of four, 550 tokens each; under 0.12.3 the same model makes a real tool call — the
server's template and parser changed between the two).

1. **A JSON envelope written as text is a tool call.** `ToolCallTextParser` gains the third
   shape next to `[TOOL_CALL]` and `<invoke>`: `{"name", "parameters"|"arguments"}`,
   `{"type":"function","function":{…}}`, `{"type":"function","function":"<name>", …}`, a
   `tool_calls` array, fenced or not, with raw line breaks inside strings repaired before
   parsing.
2. **An answer shaped like a tool call that cannot be executed is not a final answer.**
   The loop hands it back with the fix the model needs (call the tool, or write one valid
   JSON object), twice at most, then lets it stand — `LooksLikeToolCallAttempt` requires
   the envelope vocabulary *and* one of the agent's tool names, so a JSON deliverable is
   never mistaken for one.
3. **The quickstart model is `qwen2.5:1.5b`.** Smaller (986 MB against 1.3 GB) and it
   calls the tool the way a tool is called: measured 5/5 under Ollama 0.34.0 and 3/3 under
   0.12.3, before and after the two guards above; `llama3.2:1b` still fails 3/3 after them
   (it repeats the same broken string). README, quickstart, Aspire example and workflow
   cache key follow.

### Added — `Orkeon.Hosting.Aspire`, and runners that export OpenTelemetry by the standard contract (ADR-011)

The runners built their host with the parameterless `AddOrkeonInfrastructure()` (no
telemetry), never started it (OpenTelemetry's hosted service never created the
providers) and ignored `OTEL_EXPORTER_OTLP_ENDPOINT`: a run launched from .NET Aspire
produced spans that went nowhere. `RunnerHost` now registers telemetry from the
settings, honours the standard OTLP environment for traces, metrics **and** logs, and
resolves the providers itself after the build. Measured with a two-line OTLP sink and
the quickstart crew: `v1/traces` with `invoke_agent Scribe`, `chat llama3.2:1b` and
`execute_tool file_write` (gen_ai.* attributes), `v1/metrics`, and `v1/logs` at
verbosity 1 — with nothing but the environment variable set.

`Orkeon.Hosting.Aspire` (ninth id of the lineup, PUB-25 wrapper on `Orkeon` +
`Aspire.Hosting` 13.5) adds `AddOrkeonHost` (the `orkeon-host` daemon) and
`AddOrkeonCrewRun` (one `orkeon run`, with its `/output` mount created by the AppHost)
as executable resources, `WithOrkeonSetting` / `WithOrkeonModel` for the `ORKEON_`
environment, and `.WithOtlpExporter()` applied. Three launch-free tests evaluate the
arguments and environment Aspire would hand the process (the OTLP endpoint included);
`examples/aspire/AppHost/` ran the quickstart crew as a resource on this machine —
`out/hello.md` written, dashboard up. The decision that goes with it: the Aspire
dashboard is the cross-platform observability surface, and no web Studio will be built.

### Added — `Orkeon.Interop.AgentFramework`: Microsoft Agent Framework, in both directions (ADR-010)

A developer with Microsoft Agent Framework code can try Orkeon without giving anything up,
and an Orkeon crew can be one more agent in a MAF workflow. One package, depending on
`Orkeon` and `Microsoft.Agents.AI.Abstractions` 1.20 only, eighth id of the NuGet lineup:
`CrewAgent : AIAgent` (a crew as a MAF agent — one kickoff per `RunAsync`, the conversation
as initial context, the final output as the assistant message, token telemetry as
`Usage`, stateless sessions that still serialise), `AIAgentLlmProvider : ILlmProvider`
(a MAF agent as the model of an Orkeon agent — `AgentBuilder.WithAgentFrameworkAgent`, one
MAF session for the provider's lifetime) and `AIAgentTool` (a MAF agent as a tool of an
Orkeon agent — `WithAgentFrameworkTool`, the mirror of MAF's `AsAIFunction()`).
`AddOrkeonAgentFramework()` registers an `ICrewAgentFactory`. Eleven tests with
hand-written doubles; `examples/interop/agent-framework/` runs both directions on the
configured model — verified on `llama3.2:1b` via Ollama.

### Fixed — the initial context of a kickoff reaches the agents

`CrewInput.InitialContext` was mapped into the domain input and read by nothing:
`orkeon run --initial-context`, Studio's field and every programmatic
`CrewInput.Empty("…")` reached no prompt — a crew asked to "summarise the text given as
initial context" answered "what text?". The orchestrator now exposes it as the
`initial_context` prompt variable (listed under *Context Variables* and available to
`{initial_context}` templates); a caller-supplied variable of that name wins.

### Changed — a run now produces spans, and they speak the OpenTelemetry GenAI conventions

The telemetry plumbing was complete and unused: `TracingInstrumentation`, `OrkeonMetrics`
and the seven `ActivitySource`s were called by one `IChatClient` decorator that only the
tests instantiated, so a real crew execution emitted no span at all — and the attribute
names were Orkeon's own (`orkeon.llm.model`), which no backend recognises. The
`ActivitySource`s move to the Application layer (`OrkeonActivitySources`, same names;
Infrastructure's `OrkeonDiagnostics` re-exposes the same instances) and the execution
path emits on them: `invoke_agent {agent}` around each agent turn, `chat {model}`
(kind Client) around each model call with the request/response/usage attributes,
`execute_tool {tool}` around each tool call with the model's own call id — children of
the agent span, `error.type` and an error status on failure. The names and attributes
are the [generative-AI semantic conventions](https://opentelemetry.io/docs/specs/semconv/gen-ai/),
declared once in `Orkeon.Constants.Llm.GenAiAttributes` (ADR-009 satellite) and used by
the loop, the tracing helpers and the scripting runtime alike. `OrkeonDiagnosticTags`
keeps its member names but its LLM/agent/tool/error values are now the convention's
(`gen_ai.provider.name`, `gen_ai.request.model`, `gen_ai.usage.input_tokens`…);
`OrkeonMetrics` records `gen_ai.client.token.usage` (per token type) and
`gen_ai.client.operation.duration` (seconds) instead of `orkeon.llm.tokens` / `orkeon.llm.duration`.
The Terminal.Gui tool aggregator identifies tool spans by `gen_ai.operation.name` rather
than by a span name. Documented in `opt-in-subsystems.md` (EN + FR).

### Added — a Contributor License Agreement template, and the check that asks for it

Zero external contributors, zero `Signed-off-by`, and the intent to hand the project to a
foundation one day: the only cheap moment to put a licence frame in place is before the
first outside pull request. `CLA.md` / `CLA.fr.md` are an individual CLA **template**,
marked on their first line as awaiting counsel's validation — the licence is granted to
the current maintainer *and any successor entity*, so the transfer needs no second round
of signatures. `cla.yml` (`contributor-assistant/github-action`, pinned by SHA,
signatures on this repository's `cla-signatures` branch, no external service, no PAT)
posts the one sentence to reply with and stays red until it is posted; the maintainer
account and dependabot are allowlisted. `CONTRIBUTING` (both languages), the pull-request
template and the docs-parity gate know about it.

### Changed — .NET 11 readiness, without moving the target

`ci.yml` gains a non-blocking job that restores, builds and tests `Orkeon.sln` with the
.NET 11 SDK (preview channel until GA on 2026-11-10), widening `global.json`'s
roll-forward on the runner only. Verified locally with `11.0.100-rc.1` — and that build
was red: the 11 SDK ships three analyzer rules the 10 SDK does not, all fixed so the job
starts green. `CA2027` (a `Task.Delay` that lost a `WhenAny` race keeps its timer alive)
in the Studio process launcher, the capture settle and context, and the Terminal.Gui shutdown grace
period, now `WaitAsync(timeout)`; `CA2026` (`JsonDocument.Parse(...).RootElement` leaks
the document) at 55 sites in MCP and the tests, now `JsonElement.Parse`; `CA1860`
(`Any()` on a collection with a `Count`) at 3 sites in test doubles. Both dotnet
tools (`orkeon`, `orkeon-repl`) now declare `RollForward=Major`: a machine whose only
runtime is .NET 11 runs them instead of printing "You must install .NET". The target
framework, `global.json` and the container base images do not move before GA.

### Changed — the pre-release upstream of `Orkeon.Tools.Embeddings.Local` is assumed, in writing

`SmartComponents.LocalEmbeddings` has one version, a pre-release, and an archived
upstream. Rather than vendoring its inference wrapper, the packaging project of the
opt-in silences `NU5104` — the only project that ever carries the dependency — and the
decision is recorded where it is read: one line in the README's installation table, a
paragraph in `limitations.md`. Verified: `dotnet pack Orkeon.sln -p:VersionSuffix= -warnaserror`
produces the nine `1.0.0` packages, the opt-in depending on the pre-release as declared.

### Changed — the README opens on the problem, then on a command that runs without a key

The front page used to open on *one crew, three ways* and a YAML block — a pitch to
someone who had already decided, on the one promise Microsoft Agent Framework has also kept
since July. It now opens on the problem: a coding agent let loose on a repository writes
where it should not, because nothing is there to stop it. Then on what Orkeon puts in front
of the model — the virtual file system and its declared mounts, the sandbox, the execution
budget, and `Orkeon.Compliance.Vfs` as a standalone analyzer for the reader's own code —
and on a three-line block that runs a crew on `llama3.2:1b` with no API key:
`examples/quickstart/` (one agent, one `file_write`, one `rw` mount, its own
`appsettings.json` pointing at Ollama). Measured on 2026-09-11: the crew writes
`out/hello.md` in 14 s; the same crew asked to write `/etc/hello.md` is refused by the
file-system service before any byte lands (`No mount found for virtual path`). `orkeon
forge` gets its own section — the *brief → blueprint → render → validate → test → diagnose
→ verdict* cycle, the four commands, the `FORGE-LLM-UNAVAILABLE` door. *Quick Start —
one crew, three ways* moves down, unchanged. The compliance vocabulary — NIST, DLP,
memory encryption, key rotation — leaves the README and the docfx landing page; every
subsystem stays in the code and in the reference pages (`opt-in-subsystems.md`,
`security.md`). `index.md` / `index.fr.md` open the same way.

### Added — the README's quickstart block is executed by CI, literally

`quickstart.yml` cuts the fenced block between the `<!-- quickstart:begin -->` /
`<!-- quickstart:end -->` markers out of `README.md`, checks that `README.fr.md` carries
the identical block, and runs it on a GitHub runner: a pinned Ollama release checked
against its published digest (no `curl | sh`), the `orkeon` tool installed by the block's
own `dotnet tool install` line — resolved to a pack of the checkout under test through a
`nuget.config` that names that pack as the only source — then `out/hello.md` must exist
and say hello. Change the README, the job runs the new README; break it, the build is red.

### Fixed — a tool-call envelope inside the arguments no longer trips the circuit breaker

`llama3.2:1b` answers a `file_write` call with the whole envelope as the arguments —
`{"type":"function","function":"file_write","parameters":{"path":…,"content":…}}`. The
dispatcher passed it through, the tool saw no `path`, said so, and the model repeated the
identical envelope until the circuit breaker tripped after three iterations. When every
key of the arguments belongs to that envelope (`type`, `function`, `name`, `parameters`,
`arguments`) and the payload is an object — or a JSON-encoded one — `ChatToolDispatcher`
now unwraps it. Anything else is left exactly as it was.

### Changed — coverage is measured in public, and the README quotes no number for it

The README stated 82.7 % line coverage from a SonarQube pass on one machine whose report
the repository does not track. A new `coverage.yml` workflow runs the unit and fast suites
under a pinned `dotnet-coverage` on every push to `main` and weekly, refuses an empty
measurement, writes the summary on the run page and keeps the Cobertura file plus an HTML
report as an artefact. The README's status section links to the workflow and quotes
neither the coverage nor the SonarQube figures any more; `quality-gate.md` explains where
the checkable number comes from and what it covers.

### Changed — the numbers on the front page are counted by one rule, printed with it, and exact

Three sources gave three tool counts: `75+` in the README, `79` in `CLAUDE.md`, `73` from
a grep by base class. All three were "true" under their own rule; none published it. The
rule is now one — a `*Tool.cs` file under `src/` is a built-in tool (the grep undercounted:
the four relational-database tools, the two file-search tools and the code interpreter
derive from intermediate bases) — and the number is **79** on every page, exact: the
`N+` floor `check-doc-claims.py` used to accept is rejected as a number nobody can check.
The example count is measured on disk (`examples/NN-*/NNN-*/` holding a `config.yaml` or
a `main.ork.ts`, 105) and `examples/INDEX.md` is checked against it, rather than the other
way round. The memory-store count (6, classes deriving `MemoryProviderBase`) joins the
gate. `scripts/count-surface.sh` — `check-doc-claims.py --surface` — prints every
front-page number next to the rule that produced it, JSON on request. The walks are
pruned at the directory level: the gate took four minutes on this tree, it takes seventy
seconds.

### Added — the provenance chain is documented, verified on rc.3, and closed with an SBOM

The CI already did what almost no .NET open-source project does — Trusted Publishing by
OIDC, `actions/attest-build-provenance` on every package and every release asset,
`ContinuousIntegrationBuild` at pack time, `SHA256SUMS` per channel — and said so in one
line of one reference page. [Verify what you install](docs/guides/verify-what-you-install.md)
(EN + FR) now gives the exact gestures, each run against the published `v1.0.0-rc.3`
artefacts before being written down, and states what the chain does *not* prove.

Two measurements shaped the page. A release asset verifies as downloaded: the GitHub
attestation API returns one SLSA v1 statement for `orkeon-cli-1.0.0-rc.3-osx-arm64.tar.gz`,
builder `release.yml@refs/tags/v1.0.0-rc.3`, eleven subjects. A package downloaded from
nuget.org does **not**: nuget.org repository-signs every package by appending a
`.signature.p7s` entry, so its digest is no longer the attested one. The signature is
always the last entry, so `scripts/nupkg-unsign.py` (standard library, no re-zipping)
recovers the original bytes exactly — measured digest
`5800062e…cbb8e7` for `Orkeon.1.0.0-rc.3.nupkg`, which the API resolves to the
`publish.yml` statement with its nine subjects. `SECURITY.md` gains a *Verifying what you
install* section and the README installation table a *Verify what you download* row.

The one missing piece of the chain was the cheapest: both `publish.yml` and `release.yml`
now generate a **CycloneDX SBOM** of `Orkeon.sln` (`CycloneDX` dotnet tool 6.2.0, pinned;
199 components on rc.3) right after the build and cover it with the **same** attestation —
a release asset with its `SHA256SUMS` line, and a `sbom` run artefact for the package push.

### Fixed — `Orkeon.Compliance.Vfs` reaches NuGet.org, and works once it gets there

The analyzer was `IsPackable`, packed at every tag, and never pushed to nuget.org: the
publish lineup was a fixed list of six ids in two places. It is the seventh now, in all
seven hand-maintained copies `check-doc-claims.py` compares.

Pushing rc.3 would have shipped an inert package. Built against the repository's pinned
Roslyn 5.9.0, it was refused by the compiler of a stock .NET 10 SDK (10.0.301 ships
5.6.0) with `CS9057` — a *warning*, after which the analyzer is silently skipped. A fresh
project with a `File.ReadAllText` call built clean. The package is now compiled against
Roslyn 4.8.0 (the .NET 8.0.100 compiler; `VersionOverride` on the one reference), and the
same fresh project reports `ORKVFS001` and `ORKVFS002` as errors. Its README is rewritten
for a consumer who has never heard of `IFileSystemService`: the seven rules, the path
exemptions, the name-matched suppression attribute to declare locally, `.editorconfig`
severities.

### Changed — the front page stops asserting what `git tag` already says, and says who answers

The README and `CLAUDE.md` claimed "the latest tag is `v1.0.0-rc.2`" two days after
`v1.0.0-rc.3` was tagged and its six packages were on NuGet.org. Tag state is no longer
asserted in prose anywhere: `scripts/check-doc-claims.py` rejects the sentence shapes that
rotted (`not yet tagged`, `latest tag is`, and their French forms) and, when the clone
carries tags, fails the build if a `v*` tag newer than the props version exists — the
version bump that follows a release can no longer be forgotten silently. `ci.yml` checks
out with tags for that purpose.

`.github/CODEOWNERS` routed every review to `@Orkeon/maintainers`, a team that does not
exist — which GitHub treats as no owner at all. It now names the maintainer account.

`SUPPORT.md` gains an *If the project stops* section: MIT, a build reproducible from a
public clone, every action and base image pinned, no private infrastructure on the path —
so a fork that keeps the tests green is a full replacement. The README links to it.

`CONTRIBUTING.md` opens its *Areas for Contribution* with a scope freeze: no 15th LLM
provider, no new built-in tool, memory store, language adapter or orchestration mode
until real users ask — one maintainer carries the whole surface. The former wish list
(Cohere, Vertex AI, Qdrant, a web UI, calendar tools…) is gone; interoperability,
observability, tests, docs and bugs are what remains open. `limitations.md` records the
rule and the issue chooser links to it before a proposal is typed.

## [1.0.0-rc.3] - 2026-09-07

The release candidate that opens the repository. Since `1.0.0-rc.2`: the NuGet
distribution collapses from a per-layer lineup into a single `Orkeon` package plus
`Orkeon.Tools` and the opt-ins (PUB-25); the example catalogue drops its dedicated C#
trading runner and speaks TypeScript end to end; MiniMax and Grok bring the provider
fleet to **14** (Groq removed, no shims); the VFS boundary is enforced everywhere by its
own analyzer, and virtual paths became the only currency an agent is paid in (ADR-008);
constants shared by two projects moved to zero-dependency satellites (ADR-009); and a
full SonarQube campaign closed every issue above INFO — 0 bug, 0 vulnerability, 0
hotspot, technical debt down from 1 762 minutes to zero, A on all four ratings.

The public API surface is frozen at this tag: the 328 additions and 203 removals
accumulated since rc.2 move from `PublicAPI.Unshipped.txt` to `PublicAPI.Shipped.txt`
across the twelve projects that carried them, and the seven `ORKVFS` analyzer rules ship
with them.

### Changed — the scripting DSL stops dropping half of what a script declares in silence

A `.ork.ts` file picks one of two engines by how it ends, and each engine honours what the
other ignores. A crew that declares tasks and ends with `await crew.run()` ran its agents
and never looked at the tasks; a crew that declares `.body()` and hands itself off with
`globalThis.crew = crew` ran its tasks and never invoked a body. Both produced a run that
succeeded, printed a plausible result, and said nothing about the half it had thrown away.

Both halves now warn, symmetrically and by name. `JsCrew.RunAsync` reports the tasks it
will not read (with the count), a manager it will not use, and a `process(...)` that
reaches only a telemetry tag. `JsCrewConfigurationAdapter.CollectIgnoredFeatures` reports
`.body()`, `.withState`, `.onError`, `.onAgentStart`/`.onAgentStop`, `budget()` and the
crew hooks — each naming the agent it was written on, because "a body was dropped" in a
six-agent crew is a second search. The runner logs them; `orkeon run` also warns on stderr
when `--inputs`, `--inputs-file` or `--memory-limit-mb` is passed to a script that hands its
crew off, since those options have no equivalent on that path and `globalThis.inputs` is
never planted.

`.concurrency(n > 1)` is deliberately not in that list: `JsAgentBuilder.build()` already
throws on it, so no crew carrying one can reach the adapter. A warning for an impossible
state is noise.

### Removed — `crewBuilder().graph()`, a method whose argument no engine ever read

`.graph(stateGraph)` stored its argument in a private field that reached neither
`JsCrewDefinition` nor `JsCrew` nor the declarative adapter — and `process("graph")` refused
to build without it. The one crew mode that needed the method was gated behind a method that
discarded what it was given, and no file in the repository ever called it.

The two facilities share a word and nothing else. `process("graph")` runs the crew on the
domain's graph strategy — a fixed `agent_execute → route_decision` loop with a circuit
breaker, tuned by `GraphConfig`. `stateGraph({ nodes, edges })` is the topology the script
draws, and it runs on `.run()` from an agent `.body()`. Removing the method unblocks the
mode: `process("graph")` now builds on its own. Same treatment as `when()` earlier in this
release, and for the same reason — nothing is released at rc.3, and this repository takes no
compatibility shims.

### Fixed — the ONNX crash CI had been tolerating was a call into a disposed session

`LocalEmbeddingProvider`, shipped in the `Orkeon.Tools.Embeddings.Local` package, set a
`_disposed` flag in `Dispose()` and never read it again. `EmbedBatchAsync` and
`Dimensions` went on to use the `LocalEmbedder` whose native ONNX session had just been
freed. Both now throw `ObjectDisposedException`, so a disposed provider refuses the call
instead of dereferencing freed memory — which for a library means it can no longer take
its host process down at shutdown.

The visible symptom was a test suite that killed its own process.
`Dispose_Releases_Embedder` asserted that a disposed provider "raises any exception",
an assertion written over undefined behaviour. On a warm heap the freed session returned
nonsense (`OnnxRuntimeException: input name cannot be empty`); on a dirty one it was a
SIGSEGV. Measured: the class crashed 5 runs out of 5, while each of its tests run alone
crashed 0 out of 3 — the crash needed the earlier tests to dirty the heap first. After
the fix, 17 consecutive runs exit 0 with all 11 tests green. The test now asserts
`ObjectDisposedException` exactly, and a second one covers `Dimensions`.

That crash had been recorded as an ONNX Runtime teardown artefact (PUB-17 / SONAR-14).
`ci.yml` and `publish.yml` each carried a step tolerating exit 139 whenever "every
discovered test was accounted for" — but the accounting came from the crashed process
itself, so a truncated run always matched, and a crash landing before the first result
reported zero tests and failed the step anyway, which is how the CI run of 2026-09-07
went red. Both steps are plain `dotnet test` runs again, `integration.yml` no longer
excludes the project from the nightly sweep, and `docs/reference/limitations.md` (EN+FR)
now records what the crash was instead of what it was taken for.

### Fixed — the final pre-publication review: what eleven reviewers found in the tree they were about to make public

**Security.** Web tools no longer share the host's ambient `HttpClient`:
`AddOrkeonWebTools` registers its own named client
(`WebToolExtensions.HttpClientName`) with `AllowAutoRedirect = false`, closing an
SSRF-by-redirect hole where a validated first hop could redirect a request to a
metadata endpoint the guard never saw. The RAG `WebPageLoader` gained the guard
it never had: it validates through `IUrlValidator` before any fetch and **fails
closed** when no validator is registered, so `rag_ingest` can no longer be
pointed at `169.254.169.254`. Both SSRF guards learned the IPv6 addresses they
were letting through -- `::`, `ff00::/8` multicast, the `64:ff9b::/96` NAT64
prefix, and IPv4-compatible `::x.y.z.w` forms judged against the IPv4 table --
and a new test pins the two tables against each other so they cannot drift.
`http_api` now runs LLM-supplied headers through the header sanitizer instead of
forwarding them verbatim, with a default sanitizer backing every construction
shape, so a model can no longer set `Host` or `Cookie` or smuggle a CRLF. The
shell tool clears the child's environment down to a named allowlist rather than
handing it every variable the host process holds, and its read-only `git`
guarantee inspects every token instead of the subcommand alone (`git log
--output=/tmp/x` was a write). `LogSanitizer` learned the key shapes this
repository actually ships -- underscores inside keys, `xai-`, `hf_`, `tgp_v1_`,
`tvly-`, `xox[abp]-`, `AIza`, and the dotted DashScope form -- and the two
`sk-` patterns became one, which also fixed a case where the narrower pattern
matched first and left the tail of a key in the log.

**Contracts that lied.** `SandboxOptions.RequireHumanApproval` and
`PreferredSandbox` were public, documented, bound from configuration and read by
nothing; an option that promises a human gate and does nothing is worse than no
option, so both are **removed**. `LlmConfig.ApiKey` stopped being `[Obsolete]`:
it steered every caller to `ApiKeySecretName`, which the framework never
resolves, so following the compiler produced an unconfigured provider --
`ApiKeySecretName` is now documented as reserved and the limitation is recorded.
Azure OpenAI's direct streaming path threw instead of ending on silence, joining
what the other providers already did; and the three buffered streaming fallbacks
(`RateLimitedLlmProvider`, `HttpLlmProviderBase`'s default, the `IChatClient`
adapter) stopped swallowing a provider's refusal and re-emitting it as an empty
stream.

**Distribution.** Both tool packages redistribute ONNX model weights, and neither
carried `THIRD-PARTY-NOTICES.md`; the notices now travel with them, in the two
nupkgs, the installer archives and the Debian package. The package-closure gate
walks the whole transitive `ProjectReference` tree, so an assembly that ships in
no lineup package fails the gate instead of reaching a consumer as a
`FileNotFoundException`.

**Command line.** `orkeon --help`, `orkeon help` and a bare `orkeon` print a
usage page listing every verb and exit 0, instead of listing nothing and exiting
1; an unknown first token is rejected by name rather than falling through to
`run`.

**Studio.** Three English catalogue values still named French buttons; the
orphan-key drift gate filtered on a key prefix no key carries, so it checked
nothing; and a resumed forge session dropped most of the metrics
`last-run.json` carries.

**Documentation.** The pages stopped describing a repository that does not
exist: the install lines carry `--prerelease`, the phantom `--config` flag became
the positional `<config>` the CLI really takes, `examples/README.md` names the
environment variable the runtime actually reads, the release pipeline is drawn
from `release.yml`'s own job graph, the quality-gate policy dates its
measurements against the September report, `SECURITY.md` admits that
`shell_command` ships registered, the runner logging default is Warning and says
so, the English Studio page quotes the English catalogue, and the documentation
site has a published address. Every `src` project has a README.

**Two more guards, caught by the same pass.** The Guardian's own SSRF check was the
third guard on this surface and had kept a hand-written IPv4 regex while the other
two were hardened; it now judges through `UrlValidator`'s tables, so `[::1]`,
`[::]`, the IPv4-mapped and NAT64 forms of the metadata endpoint and `100.64.0.0/10`
are refused where they used to pass. And `DockerSandbox` — the boundary `SECURITY.md`
points at for untrusted code — started its container as root with the daemon's
default capability set; the run line now always carries `--cap-drop=ALL`,
`--security-opt=no-new-privileges` and `--pids-limit`.

**Three more, on the same security surface.** The URL validator logged the credentials
it had just refused: an embedded-credentials denial wrote the password into a Warning
line, and the scheme and port denials logged the query string. Log lines now carry
scheme, host and port only. The VFS analyzer could not see `using static System.IO.File;
ReadAllText(p)` nor `FileStream fs = new(path, …)`, so both shapes escaped ORKVFS001-006;
it now judges invocations on the resolved symbol and registers the target-typed creation
form. And the sandbox's Roslyn denylist ignored `Environment.Exit`,
`Environment.GetEnvironmentVariable` (the environment holds every API key the host
resolved), `AppDomain.CurrentDomain.Load(bytes)`, the target-typed `Process p = new();`
and `unsafe` used as a method modifier — all five are now flagged.

**Gates.** `check-doc-claims.py` compares the CONTRIBUTING copies of the NuGet
lineup (six copies now, not four) and checks the push order in each;
`check-comment-accents.py` reaches unaccented French in C# comments, which is how
twenty-seven comments quoting French UI labels had survived the catalogue
rewrite.

### Fixed — a provider that cannot stream now says so, and the clocks stop deciding tests

- **`ILlmProvider.SupportsStreaming` follows `IsConfigured`** instead of being an
  unconditional `true` on all 14 providers. An unconfigured provider used to declare
  streaming, the caller took the SSE branch, and the branch ended without a single chunk —
  a script's `for await` completed on a silence indistinguishable from a model with nothing
  to say, while the very same provider's buffered path said "API key is required" out loud.
  A direct streaming call on an unconfigured provider now **fails in the open**, and the
  streaming and buffered paths render the same marker. Consumers that branch on the
  declaration (`LlmProviderToChatClientAdapter`, `RateLimitedLlmProvider`, the scripting
  `llm` facade) see it change with the configuration.
- `AgentWorkloadTracker` and `TaskExecutionRouter` take an injected `TimeProvider`. Five
  test classes stop measuring wall-clock time — three of them were assertion bugs rather
  than timing ones.
- Publishing hardening: both release workflows attach **build provenance attestations**,
  the container base images are pinned **by digest** (Ollama stops floating on `:latest`),
  `NuGetAudit` is declared rather than inherited as a side effect of `-warnaserror`, and a
  tag carrying a prerelease suffix now publishes as a **prerelease** instead of becoming
  `Latest` by omission.
- The secret-scan allowlist stops masking whole files and **names the individual values** it
  accepts, so a real credential added to an allowlisted file is still caught.
- Two tests stopped depending on the machine: the `Retry-After` policy test reads a
  **monotonic** `Stopwatch` instead of `DateTime.UtcNow`, and the crew-host start/stop test
  waits for the hosted loop to announce itself instead of racing it.
- The strict docfx build (`--warningsAsErrors`) is green again — the API landing page is
  reachable from the table of contents, and the generated SonarQube reports joined the
  site's content set instead of dangling as broken links.
- The LLM campaign kit (`llmproviders-test/`) generates its reports and its regenerated
  index in **English**; reports dated before 2026-09-06 stay French, and the index says so.

### Fixed — SonarQube campaign: 186 issues resolved, BLOCKER through MINOR **[breaking — constructor shapes]**

A full analysis (SonarQube 9.9.8, scanner .NET 11.2.1, `Sonar way` C# profile) on
the renamed `Orkeon` project reported 186 issues in the BLOCKER..MINOR band across
119 files, plus 17 security hotspots left `TO_REVIEW`. All 186 are now closed and
every hotspot is reviewed: **0 bug, 0 vulnerability, 0 hotspot, 0 issue above INFO**,
technical debt 1 762 min to **0**, and reliability / security / maintainability /
security-review all rated **A**. Coverage (82.7 %) and duplication (1.6 %) are
unchanged — this campaign moved no test and added no dead code.

- **11 BLOCKER `S2699`** — tests that asserted nothing now assert what they claim to
  verify (`Orkeon.Host.Tests`, `Orkeon.Infrastructure.Tests` EventHub, `Callback`,
  `Analysis` hybrid search, `Studio.Core`).
- **42 `S3776`** — cognitive complexity brought under 15 by extraction, not by
  splitting: `ForgeCommandOptions.Parse` (58), `ForgePromote.WriteCard` (50),
  `ForgeEngine.RunAsync` (30), `RagNamespaceBinding` (26), `RunnerExecution` (21),
  `LlmProviderFactory` (19), `OpenAICompatibleProviderBase` (19), and 35 more.
- **35 `S3358`** nested ternaries, **18 `S107`** over-long parameter lists,
  **11 `S125`** commented-out code, **7 `S3267`** loops replaced by LINQ,
  **5 `S1168`** null collections, and the mechanical tail (`S927`, `S1186`, `S1144`,
  `S2365`, `S2479`, `S1854`, `S3264`, `S3871`, `S2223`, `S1751`, `S3903`, ...).
- **20 findings arbitrated as false positives**, each carrying an in-code
  `[SuppressMessage]` (or a local `#pragma`) that states why: `S101` on `I18n`
  (the rule proposes `18N`), `S3604` on `Lock _gate = new()` fields of primary-
  constructor types, `S107` on `[LoggerMessage]` partials, `S1168` where `null` is
  a third state the caller reads (`ICrewLinkProvider.LinksFor`, the shell-tool
  allowlist), `S2737` on catch clauses that exist to carry an exception filter,
  `S3925` on the EventHub exceptions, `S3871` on an executable-internal exception.
- **17 security hotspots reviewed SAFE**: 13 x `S2077` (the only interpolated
  fragment is an allowlist-validated, quoted SQL identifier — SQLite cannot
  parameterize identifiers in DDL/PRAGMA; every caller value is a command
  parameter), 2 x `S4792` (the framework's own logging wiring), 2 x `S5443`
  (`/tmp` is a virtual VFS path, not the shared OS temp directory).

**Breaking (source):** four constructors that took more than seven dependencies now
take a single grouped dependency object — a `sealed record` for the three RAG ones, a
`sealed class` for `CrewStrategyDependencies`. No shim is provided — call sites move
with them.

- `Orkeon.Infrastructure.Crew.Strategies.CrewStrategyDependencies` (new) replaces the
  `(taskRepository, agentRepository, executionService, memoryScope)` quadruple in
  `SequentialProcessStrategy`, `GraphProcessStrategy`, `AutonomousProcessStrategy`
  and `ConsensualProcessStrategy`.
- `Orkeon.Rag.Pipeline.StagedRagPipelineDependencies` and
  `Orkeon.Rag.Pipeline.IngestionPipelineDependencies` (new) replace the optional
  collaborator tails of `StagedRagPipeline`, `CorrectiveRagPipeline` and
  `DefaultIngestionPipeline`.
- The remaining `S107` sites in `Orkeon.Scripting` and `Orkeon.Hosting` follow the
  same shape; every move is declared in the affected projects' `PublicAPI.Shipped.txt`
  (the `Unshipped` files stay header-only, as the release-readiness gate requires).

Out of band and left as-is: 406 `INFO` issues, 404 of them `xUnit2033` (use the
value `Assert.Single` returns instead of re-indexing) plus two `SYSLIB` hints.

### Changed — the example catalogue speaks TypeScript, and the trading assembly retires **[breaking — one package removed]**

The numbered catalogue had two ways to run: the `orkeon` CLI for most of it, and a
dedicated C# runner for the fifteen finance crews, dragging a 14 000-line assembly and
its own package behind it. It now has one.

- **The fifteen finance crews are `main.ork.ts`** — every process type (hierarchical
  with managers, parallel fan-outs, sequential, consensual), memory, the three
  `humanInput` reviews, dependency DAGs, built-ins by name, and their tools attached as
  instances. All validated end to end through the same strict pipeline as YAML. The
  catalogue holds at 105 examples.
- **A shared `_tools` TypeScript module** replaces `Orkeon.Trading.Tools`: 27 tools over
  seven category files plus a small financial-math core (correlation and covariance
  matrices, Acklam's normal inverse CDF, a seeded mulberry32 PRNG). Every tool is
  **deterministic** — same input, same output — which the `Random`-based C# originals
  never were. Simplifications where the C# leaned on MathNet are documented in place
  (grid-scan mean-variance, convex-blend Black-Litterman, midpoint-bisection HRP);
  everything else is a faithful port, Wilder's full ADX included. The seventeen tools no
  finance config ever referenced were not ported.
- **Removed**: `Orkeon.Trading.Tools` (162 files), the trading runner, the two
  interactive example runners and the `examples/_shared` library — from both solutions
  and from the disk. `publish.yml` **no longer packs `Orkeon.Runners.Shared`**: that
  package is discontinued. `MathNet.Numerics` and `YahooFinanceApi` leave the package
  versions with no consumer left, the container image drops its trading publish stage and
  banner line, and the installers drop the `orkeon-trading` launcher.
- **A declarative `.ork.ts` now runs through the real pipeline.** A bare
  `orkeon run crew.ork.ts` used to execute a flat loop over agent bodies that ignored
  tasks, process, manager, `humanInput` and deliverables — the full orchestration was
  reachable only behind `--config`. `run` now routes to the shared one-shot runner
  whenever the script declares the `globalThis.crew` handoff, so the script is evaluated
  exactly once, by the pipeline. Procedural scripts keep the script path untouched.
- Every surface that discovers, validates, lints, indexes or launches the catalogue
  accepts `main.ork.ts` beside `config.yaml`: `run-example`, `test-all-examples`,
  `validate-all-examples`, the config linter (which grew a TypeScript lint checking every
  quoted tool name against the manifest and the shared module), the index generator, and
  the container's `orkeon-example` dispatch. The guides say one CLI, EN and FR.
- Incidental: a Cyrillic-homoglyph agent id the 36 finance YAML files had carried since
  birth is ASCII at last, and the catalogue-wide `CA5394` waiver lifts — the seeded-PRNG
  TypeScript successors made it moot.

### Changed — one `Orkeon` package instead of a per-layer NuGet lineup (PUB-25) **[breaking — packaging only]**

The Domain/Application/Infrastructure split is an internal discipline, not a
distribution contract — and shipping it as three packages had already produced
one real incident (rc.1/rc.2 published on NuGet.org with five unrestorable
`Orkeon.*` dependencies). Distribution is now consolidated; the 39-project
source layout, namespaces, and per-assembly PublicAPI freeze are untouched, so
**consumer code compiles as-is** — only the install line changes.

- **`Orkeon`** (new): the whole framework in one package — eleven embedded
  assemblies (`Orkeon.Domain`, `Orkeon.Application`, `Orkeon.Infrastructure`,
  `Orkeon.Rag(.Abstractions)`, `Orkeon.Analysis(.Abstractions)`,
  `Orkeon.Tools.Abstractions`, and the three constants satellites of the core
  graph). Migration: uninstall the per-layer packages, `dotnet add package
  Orkeon --prerelease`.
- **`Orkeon.Tools`** (new): the seven built-in tool families in one package,
  separate from `Orkeon` only for dependency weight (database drivers, PDF and
  spreadsheet libraries live here). Depends on `Orkeon`.
- `Orkeon.Rag.Onnx` and `Orkeon.Tools.Embeddings.Local` now depend on the
  `Orkeon` package instead of the discontinued per-layer ones.
- The per-layer, per-family and deferred-library packages (`Orkeon.Domain`,
  `Orkeon.Application`, `Orkeon.Infrastructure`, `Orkeon.Constants.*`,
  `Orkeon.Tools.<family>`, `Orkeon.Cli.*`, `Orkeon.Scripting`,
  `Orkeon.Hosting`, `Orkeon.Plugins`) are no longer packed; the published
  rc.1/rc.2 of the three core packages are to be unlisted on NuGet.org.
- `publish.yml` pushes the new lineup (`Orkeon`, `Orkeon.Tools`, the ONNX
  reranker pair, the local-embeddings tool package, and the `orkeon` dotnet
  tool) and a new gate — `scripts/check-package-closure.py` — fails the
  workflow if a lineup package depends on an `Orkeon.*` id outside the lineup
  or if an umbrella's hand-declared external dependencies drift from its
  embedded projects.

### Fixed — publishing hygiene ahead of the public opening

- Release builds emit **embedded portable PDBs** again (`DebugSymbols=false`
  had silently disabled emission, shipping non-debuggable packages with no
  SourceLink attachment point); `EmbedUntrackedSources` is on.
- The `orkeon` dotnet tool package no longer bundles the iOS/Android
  onnxruntime natives a CLI tool can never load: 262.5 MB → 137.6 MB, back under
  the nuget.org size limit.
- The docfx API reference now covers the five `Orkeon.Constants.*` assemblies,
  and `namespaceLayout: flattened` removes the 26 dead breadcrumb links the
  nested layout generated.
- A missing `--mount` source directory now fails with a clean actionable
  `ERROR:` line and exit 1 — in the YAML runner, `--validate`, `--list-tools`
  and `orkeon run` alike — instead of a raw dependency-injection stack trace.
- `CrewConfigurationMapper` reports skipped tools and failed LLM-provider
  resolution through an optional `ILogger` (source-generated warnings) instead
  of writing to the console from the Application layer; the `ExampleCallbacks`
  demo handlers moved out of the published `Orkeon.Application` assembly into
  the test suite that was their only consumer.
- Supply-chain hardening: a root `nuget.config` pins nuget.org as the only
  package source (with wildcard source mapping); esbuild moves to `^0.25.12`
  (GHSA-67mh-4wv8-2f99) and every packaging-time esbuild tarball download now
  verifies the sha512 integrity recorded in the lockfile; Dependabot watches
  the Docker base images (root + deploy).

### Changed — the screenshot campaign photographs an application in use, not an empty one

`orkeon-studio --capture-screens <dir>` existed to be the fidelity reference against the v3
mock, and could not be: it built the window over `CreateForCurrentMachine`, so on a clean
machine every shot was an empty card — no team, no history, no session, a wizard frozen on
step 1. Twenty-two images over roughly a hundred visual states, one theme out of the two the
v3 remediation promised, and no test at all beyond argument parsing.

The campaign now builds the window over a **seeded scenario in a throwaway temp directory** and
walks every screen and every gated state of it, in both modes and both themes, plus a language
sweep over the densest screens.

- **Two worlds, never one mutated into the other.** A populated machine (three adopted teams —
  one of which names a folder nobody declared —, seven past runs, four forge sessions, four
  model profiles, a declared folder that genuinely does not exist on disk, a doctor with one
  warning and one failure) and a first-run machine with nothing on it and no CLI installed.
  "Empty versus populated" is a thing a stop declares rather than a teardown that must
  un-populate a list.
- **The real loaders, over seeded files.** `TeamCatalog`, `ForgeSessionCatalog`, the file-backed
  history and profile stores, the physical settings, directory and target probes — all of them,
  pointed at the sandbox. Only the process boundary is doubled, because it has no other seam.
  `ForgeSessionHydrator` rebuilds a whole wizard session from five JSON files, so the Composer,
  the mount rows and both verdicts are photographable with **no child process anywhere**.
- **The catalogue is data, and it is asserted.** Each stop declares where it stands, what it
  arranges, why that state is worth a pixel, and which ViewModel gates it claims to light up.
  The whole campaign is replayed headless on the Linux runner and every claim is checked — so a
  stop that stops reaching its state fails the build instead of writing a confident picture of
  the wrong screen. Writing those assertions immediately caught four wrong claims, including a
  stacked-modal state this application does not have.
- **Conformity guards**, in the idiom of the suite's existing ones: every sidebar entry maps to a
  capture screen, every scrim modal is opened by some stop, every endless storyboard has a pose,
  every guided-tour step names an element that exists.
- **Output that cannot lie.** Per-stop fault barrier; geometry, non-uniformity, expected-panel
  and binding-error checks; a differs-from-the-previous-shot digest that catches the likeliest
  failure of all, a stop that changed nothing; atomic writes; failed shots quarantined under
  `failed/`; and a `manifest.json` carrying each image's reason, its SHA-256 — so re-running the
  campaign after a UI change and diffing two manifests names the exact screens that moved.

### Fixed — five defects the campaign was hiding

Found while making the collection trustworthy; each produced, or was about to produce, an image
that looked like evidence.

- **The startup plate would have bled into every shot.** `SkipSplashForCapture` ran a
  zero-duration animation and set `Visibility` from its `Completed` handler — and a zero
  *duration* is not a synchronous completion: `BeginAnimation` attaches the clock and the media
  context ticks it on the next render pass. It worked only because the settle slept 120 ms
  afterwards. Replaced by a direct pose/hide pair, alongside a `PoseSplashForCapture` that
  photographs a plate a user could actually have seen instead of five pixels of progress bar.
- **The theme button contradicted its own window in dark mode.** The icon and tooltip were
  refreshed only from the toggle handler, so applying the theme any other way left a moon in the
  title bar — the first place a reviewer looks. `ApplyThemeForCapture` does both, and
  deliberately does not touch the operator's stored preferences.
- **The language menu could never appear.** It is the app's only `Popup`, hosted in its own
  `HwndSource` and therefore invisible to `RenderTargetBitmap`: its shot would have been a
  chevron rotated to 180° above nothing. Popups are now composited into the window's image, with
  the computed placement asserted inside the window's bounds rather than trusted.
- **The settle proved nothing.** WPF orders `Loaded` below `Render`, so the second dispatcher
  fence returned immediately and `Task.Delay(120)` was the only thing creating slack. Replaced by
  a bindings drain, a layout loop that repeats until layout stops dirtying itself, bounded
  composition ticks and an idle drain — with an unstable layout recorded rather than ignored.
- **A failed shot was invisible and inflated the count.** `Save` returned silently on a
  zero-size window *after* the index had been incremented, and the count printed on stdout was
  the stop count, not the file count.

Also: the collection is captured at 1440×900 rather than 1024×768. At the old size the content
column is 752 DIP and the panels declare maximum widths of 880, 1000 and 1080 — so none of them
ever bound, and every image ever produced showed the narrowest layout the app can make, which is
not the one the mock was drawn for. `captures/` joins `.gitignore`.

### Fixed — the pre-push audit: what forty-one reviewers found in the campaign season's own commits

A six-dimension adversarial audit of the fifteen unpushed commits (token/cache accounting,
multimodal, dialect hooks, fleet coherence, documentation, commit hygiene) confirmed no
accounting defect and no payload defect — and a crop of real gaps at the edges, each fixed
test-first:

- **MiniMax, truncated mid-thought**: a reply cut by `max_tokens` inside the `<think>`
  block never reaches the closing tag, and the split hook shipped the whole raw trace as
  visible content — the exact failure the hook exists to prevent, on the exact path where
  the model is most verbose. An unterminated leading block now yields empty content with
  the partial trace in `reasoning_content`. The hook's edges are pinned while at it: a
  mid-content `<think>` survives (quoting is content), an empty block records nothing, and
  the stream-final scrub is now a test, not a hope.
- **The streaming chat path dropped images silently**: `ChatStreamingAsync` built the same
  payload as the buffered path without calling `WarnOnUnsendableAttachments`, so a
  non-vision provider streaming a multimodal conversation lost the image without a word —
  the guarantee depended on which transport the caller picked. Both paths warn now.
- **Ollama's URL-only images**: the converter documents that an image referenced only by
  URL is "reported as skipped rather than silently dropped" (the server never fetches
  URLs), but nothing reported it. `BuildChatPayload` now emits the structured warning with
  the remedy (inline the bytes).
- **The None warnings claimed too much**: "this API has no response-format field" is
  factually wrong for MiniMax — the only provider that triggers it — whose API accepts the
  field and ignores it; same for "no reasoning pass" on a model that always reasons. Both
  texts now state the honest diagnosis (not honoured / no control), and MiniMax's two
  uniquely reachable warning branches are under test.
- **Studio called the mainland MiniMax "custom"**: the detector mapped only
  `api.minimax.io`; `api.minimaxi.com` — a constant this very range introduced, routed by
  the runtime factory — now detects as `minimax`, the Kimi twin pattern.
- **`llm.minimax`**: the scripting namespace gained the accessor (binding, provider-name
  switch, typings, PublicAPI), as `llm.grok` had — a first-class provider should not be
  reachable everywhere but from a script. `llm.grok` gets its first pin alongside.
- **The registry's promise is now kept in print**: "the report header prints the values
  actually used" was false for the M7 effort (`mistral-medium-2604` probes at `high`, the
  header said nothing) — the probe report (markdown + JSON) and both kit report writers
  now print the base and M7 efforts whenever a run departed from the default, and
  `--thinking-effort` is a real flag of both campaign scripts (explicit beats catalogue),
  as their options table already claimed.
- **Documentation swept against the code at HEAD**: the fleet is 14 everywhere (eight
  pages EN+FR still said 13, one said twelve); the response-format matrix lost its stale
  contradictory `Gemini | None` row and gained the missing MiniMax row; the vision table
  no longer lists DeepSeek as both having and lacking the capability and counts all 14;
  the opt-in page's "12 of the 13 declare it (only DeepSeek does not)" was wrong on both
  halves; every "campaign pending / non campagné / NOT yet campaign-verified" left over
  from MiniMax's documentation-first landing now states the campaign; `--m7-effort`
  reached the CLI reference; the Grok custom-endpoint report's paste-ready journal line
  named a path that does not exist; and this file's own `max_completion_tokens` entry
  claimed Groq passed a campaign two sections after recording that Groq never ran one.
- **Pins the audit found missing**: Gemini joins the fleet capability table (its
  declaration was the only one unpinned), and the `xai-` key-prefix inference — the last
  routing arm — gets the test that distinguishes it from a deleted one.

### Added — MiniMax, the fourteenth provider — the first documentation-first integration

Unlike Grok, which arrived preceded by its own live campaign, MiniMax arrives with no key
and says so everywhere: `MiniMaxLlmProvider` (OpenAI-compatible, `api.minimax.io/v1`
international with the `api.minimaxi.com` mainland twin named, the Kimi pattern), canonical
key `minimax`, default model `MiniMax-M2` — every declaration sourced from the vendor's
platform documentation dated 2026-08-30 and marked campaign-pending in the code, the
comparison table (a † row), and the campaign catalogue. `response_format` and thinking stay
undeclared (capability warning, never a silent drop — the Gemini precedent, upgradeable the
day a key arrives); vision follows the documented VL family per D-03. The catalogue entry
records the three questions the first campaign must settle: DeepSeek-style reasoning
replay, cache breakdown, and whether `response_format` works despite being undocumented.
Full fleet integration otherwise: factory routing (key, both regional hosts, `minimax-*`
model prefix), Studio card and detection, doctor, `orkeon llm probe|models`, example
settings, counts 13 -> 14 under the claims gate.

### Fixed — MiniMax speaks its mind out loud, and the dialect now separates the two

The first MiniMax campaign (same day as the integration — 7/2/3 on `MiniMax-M2`, once the
right key arrived: `sk-cp-` keys are coding-plan quotas, `sk-api-` is the API) settled all
three recorded questions and found one real integration defect. MiniMax ships its reasoning
INLINE: every reply opens with a `<think>...</think>` block inside `content`, no separate
field — a one-line hello came back as 158 characters, and an agent built on it would speak
its private reasoning out loud. The dialect now splits the block into `reasoning_content`
(a `SplitReasoningFromContent` hook on the compatible base — the MiniMax twin of the
Mistral chunked-content fix) and re-inlines it verbatim when replaying an assistant turn,
because the vendor documents that history must keep the think blocks. Post-fix re-campaign:
a 24-character hello. The other two answers: `response_format` is accepted but NON-BINDING
(a schema is ignored, `json_object` arrives fenced in markdown — the None declaration
graduates from caution to measurement), and the implicit cache reports no breakdown at an
8k prefix. The standing reds are the model's: system message ignored on both shapes, and
"I'm unable to view the image" — text-only per model (D-03), with the VL family absent from
the platform's `/models` listing, so no vision companion is declarable yet.

### Removed — Groq, superseded by Grok (breaking, no shims)

Groq was never the intended provider: the near-homograph had stood in for Grok since the
provider list was first drawn. With Grok landed and Groq never campaigned (no key ever
supplied, no archived proof), the fleet drops it outright per the pre-release rule - the new
state replaces the old one everywhere, no aliases, no shims. Gone: `GroqLlmProvider`, the
satellite constants (endpoint, key, default model), factory routing (key, groq.com host,
mixtral/groq model inference), the Studio card and detection, the scripting DSL factory
(`llm.groq` becomes `llm.grok`, which also retires its stale hardcoded llama-3.1 default),
the doctor mapping, the campaign-kit entry and the docs rows. The HuggingFace `:groq`
routing suffix stays - that is HF's partner vocabulary, not Orkeon's provider key. Provider
count returns to 13; the claims gate re-derived it and named every count to fix.

### Added — Grok (x.AI), preceded by its own proof

The key supplied for "Groq" turned out to be an x.AI key (`xai-` prefix, refused by
api.groq.com, served by api.x.ai) — and the user's intent turned out to be Grok all along.
Before the provider existed, a full 12-mode campaign had already passed against `api.x.ai`
through the generic OpenAI dialect with nothing but a base-url (archived under
`llmproviders-test/custom-endpoints/`), so `GrokLlmProvider` is that measurement written
down: OpenAI-compatible transport on `api.x.ai/v1`, `json_schema` honoured, effort-only
thinking with the trace replayed, vision, implicit cache on the standard `cached_tokens`.
Canonical key `grok` (alias `xai`), default model `grok-4.6` (verified live), endpoint and
key and default in the `Orkeon.Constants.Llm` satellite, factory routing by key, by
`api.x.ai` host, by `grok-*` model prefix and by the `xai-` key prefix — the grok/groq
near-homograph is exactly the confusion that last inference absorbs. Studio gains the
provider card and endpoint detection; `orkeon llm probe|models` accept it; the campaign kit
carries its catalogue entry; docs and counts follow everywhere the claims gate checks.

### Fixed — what the first real campaigns against six vendors found (2026-08-30)

The first full campaigns ever run against api.openai.com, the Gemini compat surface and an
identity-linked Anthropic key — plus re-runs of DeepSeek, Kimi, Z.AI and Ollama on rc.2 —
turned five findings into fixes. Each one is dated and pinned by an offline test:

- **OpenAI: `max_tokens` is retired on current models.** The campaign failed ten modes out of
  twelve on `gpt-5.6-sol` over that one field (`"Unsupported parameter: 'max_tokens' ... Use
  'max_completion_tokens' instead"`). The OpenAI dialect now writes `max_completion_tokens` —
  verified live to be accepted by the older generations too (`gpt-4o-mini`) — through a
  `MaxTokensFieldName` hook on `OpenAICompatibleProviderBase` that only `OpenAIProvider`
  overrides: DeepSeek and the rest of the compatible family still document and expect
  `max_tokens`, and every provider campaigned that day passed with it.
- **Gemini: `response_format` works and was being refused.** Declared `None` when the compat
  surface left it undocumented (2026-08-18), so every JSON request got a capability warning
  instead of being sent. Measured live: the surface accepts `json_object` and `json_schema`
  and enforces the schema server-side (`additionalProperties` included). The declaration is
  now `JsonSchema`.
- **DeepSeek: vision arrived, per model.** `deepseek-v4-flash-vision-exp` reads a base64
  image (measured live); DeepSeek was the one provider in the fleet with no vision model and
  its images silently degraded to text. `Vision = true` now, with the same per-provider
  declaration / per-model reality the fleet already lives with (D-03): the text-only default
  model answers an image with the vendor's own error.
- **Anthropic: identity-linked API keys were unusable.** They refuse every request without an
  `anthropic-workspace-id` header — Messages API and `/v1/models` alike — and Orkeon had no
  way to send one. `LlmConfig.WorkspaceId` (a scoping identifier, not a secret, same family
  as `ApiVersion`) now travels as that header when set; classic keys change nothing. Wired
  through `orkeon llm probe --workspace-id` and the campaign kit (`workspaceId` per provider).
- **Ollama: every buffered completion was accounted as free.** The buffered
  `/api/generate` parse hard-coded `TokensUsed = 0` behind a comment claiming Ollama
  provides no count in that format — while the live server returns `prompt_eval_count`
  and `eval_count` right beside the durations the same parse was already reading. Zero
  fed the token dimension of `AgentExecutionBudget` and the crew accounting. Found by
  the M1 probe archiving `tokens=0` on a priced exchange; the same gap left M10's
  diagnostic line reading "prompt tokens: unreported" for Ollama, because a tool-less
  conversation flattens onto that very path. Both now report (`tokens=35`,
  `prompt tokens: 2052 then 2052` on the re-run).
- **The probe harness accused the framework twice, wrongly.** M5 rebuilt the assistant
  tool-call turn from parsed calls while the real agent loop replays the vendor's raw
  `tool_calls` fragment verbatim — Gemini rejects a replay that lost its per-call
  `thought_signature`, so the probe failed M5 for a defect the product does not have; it now
  replays the raw fragment whenever the body is OpenAI-shaped (the canonical rebuild remains
  for Anthropic's dialect). And M3/M4 asked for five numbers, which fits in a single event on
  a coarse-chunking stream (Gemini emits ~13-character chunks), reading a genuine stream as a
  buffered fallback; the probe now demands a hundred — thirty sufficed for Gemini,
  then claude-sonnet-5 coalesced the whole count-to-thirty into a single delta the
  same day.

The Mistral default model was broken and nothing could have said so offline:
`LlmProviderDefaultModels.Mistral` carried `mistral-medium-3-5-26-04`, an identifier the API
never served (`Invalid model`, measured 2026-08-30) — it reads like a concatenation of the
alias `mistral-medium-3-5` and the vintage `2604`, the two forms Mistral really serves. Any
configuration naming the provider without a model got a guaranteed 400. Now
`mistral-medium-2604`, the dated snapshot, verified live; same class as the retired defaults
LLM-01 fixed (G-01..G-04), found the same way — by the first real call.

The mandatory values some models dictate are now a per-model registry, not folklore:
`requiredParams` in the campaign catalogue (kimi-k2.6, gpt-5.6-sol and claude-sonnet-5 all
refuse any temperature but 1; gpt-5.6-sol additionally demands `reasoning_effort: "none"`
with function tools), resolved per model by both campaign scripts — per model and not per
provider, because `gpt-4o-mini` rejects `reasoning_effort` outright and a provider-level pin
would break it. Human-readable twin: the "Per-model mandatory parameter values" section of
`docs/reference/llm-providers-comparison.md` (mirrored in French), vendor wording and
measurement date included.

Mistral, first campaign ever (the key arrived last), peeled four findings in a row before
settling at 11/1 on `mistral-medium-2604`, with the G-14 proof in the header
(`api.mistral.ai`, not `localhost`, no base-url passed):

- The compiled default was the broken identifier above — first finding, fixed first.
- The model restricts `reasoning_effort` to `['high', 'none']`, so M7's hard-coded "low"
  was refused: the probe's effort is now a per-model registry value (`m7Effort`,
  CLI `--m7-effort`).
- **Orkeon's omit-top_p-when-1 was a silent drop there.** Mistral's reasoning mode runs an
  internal top_p default of its own and validates greedy sampling against the EXPLICIT
  field, so `temperature: 0` + reasoning with no `top_p` is refused (`"top_p must be 1 when
  using greedy sampling."`) while the same request with an explicit `top_p: 1` passes. The
  Mistral dialect now always writes the configured value (`AlwaysEmitTopP` hook — the base
  keeps omitting: OpenAI's reasoning models reject the explicit field, the same assumption
  broken in the other direction).
- **Reasoning replies were parsed as empty.** With `reasoning_effort` on, Mistral answers
  `message.content` as an ARRAY of typed chunks (`thinking` + `text`) and the string-only
  read dropped both: M7 archived `accepted, no reasoning trace returned, tokens=243` — 243
  tokens billed, nothing kept. The shared parse now walks the text parts (the OpenAI
  multi-part standard) and surfaces the thinking chunks as `reasoning_content`; the re-run
  archives `reasoning trace returned`.

The one standing red is the vendor's: `prompt_tokens_details.cached_tokens` stays 0 on an
identical 5812-token prefix called twice (three measurements) — the field exists, the
implicit cache never hits.

Qwen, first campaign (an international key: `dashscope-intl` base URL, G-12 closed by the
report header): 12/12 — including the fleet's only `Thinking = Budget` wire
(`enable_thinking` + `thinking_budget`) validated live. Its M10 exposed a harness artefact,
not a vendor gap: DashScope's implicit cache hits nothing below a high threshold (0 cached
tokens at the probe's historical ~2050-token prefix, three runs; `cached_tokens: 4352` at
5809), so a working cache read as broken. The M10 prefix now carries ~8200 tokens — the
clean run archives 6528 cached, ratio 0.81; Mistral's zero stayed zero at the long prefix
too, which settles its verdict as the vendor's. And the OpenAI-dialect fallback for custom
compatible endpoints — the path Docker Model Runner, vLLM and LM Studio ride — got its
first real proof: a full 12/12 campaign against `api.x.ai` (`grok-4.6`), archived under
`llmproviders-test/custom-endpoints/`. The key supplied as "Groq" was an x.AI key
(`xai-` prefix, refused by api.groq.com, served by api.x.ai) — measured, not assumed.

Together and HuggingFace, first campaigns (the last two keys): 9/1/2 and 8/2/2 — no Orkeon
defect on either. The one harness defect was found before the campaigns could even start:
Together answers `GET /models` with a bare JSON array, no `data` envelope, and the first
real call crashed `orkeon llm models` with an unhandled `InvalidOperationException` — the
parser now reads both shapes and the command's catch treats a malformed body as a typed
error (red test first, on the vendor's verbatim shape). The oldest suspicion of the effort
is settled by measurement: HuggingFace's compiled default `meta-llama/Llama-3.1-8B-Instruct`,
long flagged unreachable through the router, answers M1 alive — the campaign catalogue
realigns on it. Both M9 reds are the D-03 pattern; HuggingFace's vision companion
(`Qwen3-VL-30B-A3B-Instruct`) reads the image, while Together's four vision candidates are
all non-serverless on the measured tier (dedicated-endpoint only, failure archived as proof).

Campaign verdicts, same day: Kimi's open M3 "stream refused" of August did not reproduce
(4/4 green, replays included); Z.AI's implicit context cache missed once in-campaign
(0 cached tokens) and hit on both replays (1984 tokens, ratio 0.97) — server behaviour, not
a defect. Reports under `llmproviders-test/`.

### Fixed — `dotnet test` runs again, and the tests it skipped are back

The .NET 10 SDK stopped honouring the VSTest target for Microsoft.Testing.Platform test
projects. Every `dotnet test` in this repo answered *"Testing with VSTest target is no longer
supported by Microsoft.Testing.Platform on .NET 10 SDK and later"* and ran nothing — CI, the
release pipeline, the nightly and the SonarQube script alike. `global.json` now opts into the new
runner (`"test": { "runner": "Microsoft.Testing.Platform" }`), which is the only supported answer;
the per-project `TestingPlatformDotnetTestSupport` property no longer does it.

Three things follow from the new runner, and each was a live defect rather than a rename:

- **A filter matching zero tests in a module is an error there** (exit 8), not an empty success.
  CI excluded `Orkeon.Tools.Embeddings.Local.Tests` by name from a solution-wide filter, which
  under MTP means "load that module, match nothing, fail". The exclusion is gone: measurement
  showed the SIGSEGV that motivated it comes from that assembly's **8 `Category=Slow` tests** —
  the ones that boot the real ONNX model — and from those alone (3/3 crash with only them, 5/5
  clean without them). Its other 28 tests now run with everyone else's instead of being skipped,
  and the 8 get a step whose guard reads the runner's own accounting: the crash is tolerated only
  when every discovered test was accounted for and none failed, so a run that dies mid-suite stays
  red. The previous guard accepted that case.
- **The nightly needs the opposite tolerance.** Selecting one category across the whole solution
  leaves most modules empty by construction, so it passes `--ignore-exit-code 8` — plus an
  explicit check that the run executed something, because tolerating "zero tests" per module must
  not let "zero tests anywhere" look green. Its VSTest-only `--logger` argument is gone.
- **Coverage was measured by a collector the runner does not implement.**
  `--collect:"XPlat Code Coverage"` is refused outright (exit 5), `scripts/sonar-analyze.*`
  swallowed the failure with `|| log_warn`, and the analysis went on to import a report nothing
  had written — a Quality Gate evaluating coverage conditions against 0% and reading as a
  measurement. Collection moves to `dotnet-coverage` (Cobertura → SonarQube generic via
  ReportGenerator, in both the shell and PowerShell scripts), and a report holding no class now
  **aborts** the analysis instead of being imported.

One flaky test surfaced with the new scheduler and is fixed rather than tolerated:
`OtelTests.llm_call_emits_a_span_with_method_and_prompt_length_tags` took the first LLM-call span
it saw from a process-global `ActivityListener`, so under a parallel run it asserted on a
concurrent `act()` loop's span. Its three neighbours already discriminated by a unique tag; it now
does too.

### Fixed — BM25: the guard the code index never had, and two comments that promised too much

`Bm25CodeIndex` (Analysis) accepted any `k1`/`b` through its public constructor. A negative `k1`
inverts the term-frequency saturation and a `b` outside `[0, 1]` turns length normalization into
an unbounded multiplier: the index kept answering, with scores no caller could interpret and no
error to notice. It now validates exactly as its prose twin `Bm25Index` (Rag) always did.

The two implementations' doc comments claimed they were kept in step — *"same k1/b defaults"*,
*"Same k, same semantics"*. Nothing enforced it, and on the fusion constant it was already false:
the RAG side is operator-tunable through `Orkeon:Rag:Retrieval:Hybrid:RrfK` while code search
exposes no such knob. The comments now say what is true — the values come from the literature, the
two indexes never score the same corpus, and retuning one must not propagate. ADR-009 records why
these are not satellite candidates: a shared spelling is not a shared value.

The one real duplication there was internal to RAG. `ReciprocalRankFusion.DefaultK` and
`RagDefaults.RrfK` were two declarations of the same product default, backing two option classes
bound to the **same** configuration section (`Orkeon:Rag:Retrieval:Hybrid`) from two projects — so
changing the default would have moved one fusion and not the other. `Orkeon.Rag` already
references the Domain that holds it; the copy is now a reference, and the published value is
unchanged.

### Added — satellites for the constants two projects must agree on (ADR-009)

`Orkeon.Studio.Core` may not reference `Orkeon.Infrastructure` or `Orkeon.Hosting`, and the
reason is measured rather than doctrinal: doing so *"dragged the whole runtime — ONNX runtimes,
tree-sitter grammars, the local embedding model — into every published app, for roughly 230 MB
each"*. So Studio copied the values it needed by hand, and `ConstantDriftTests` existed to stop
the copies diverging.

Five new packages hold them instead, each with **no runtime dependency**, which is what lets
both sides reference one declaration: `Orkeon.Constants.Llm` (provider endpoints, default models,
provider keys), `Orkeon.Constants.FileSystem` (the virtual roots a runner mounts for itself, and
the conventional file names one component writes and another looks for),
`Orkeon.Constants.Configuration` (`Orkeon:*` keys and shared operator wording),
`Orkeon.Constants.Protocol` (the run event kinds a runner emits and Studio reads) and
`Orkeon.Constants.Cli` (the run option names the runners accept and Studio predicts).

Two of those closed drift that had already happened rather than drift that might: Studio's copy of
the run event vocabulary was missing four kinds the runner emits, and an unknown kind is ignored
rather than reported — so tool activity and delegations simply never reached the screen.

`Orkeon.Domain` gains its first runtime project reference, to `Orkeon.Constants.Llm` — its
default model name is the same string as OpenAI's provider default. A project that depends on
nothing inverts no layer and drags nothing in behind it; ADR-009 records the argument.

Nothing published changes shape: `LlmEndpoints`, `ProviderDefaults` and `LlmDefaults` are frozen
surfaces, so their names stay and only their value moves — a `const` initialised from another
assembly's `const` is inlined at compile time. Only `RunnerMounts`, which was unshipped, is
removed; its four roots are now `RunnerVirtualRoots`.

`ConstantDriftTests` goes from 12 facts to 5. The five that remain never guarded duplication:
they ask the endpoint detector to recognise each endpoint, pin the Docker Model Runner defaults
against the committed `appsettings.json` template, and check the declared minimum CLI version.

Publication order matters now: `.github/workflows/publish.yml` pushes the satellites before
`Orkeon.Domain`, or Domain would ship with a dependency that cannot be restored.

### Changed — build toolchain

- **Roslyn pinned to 5.9.0 repo-wide.** The analyzer and the source generator reference
  `Microsoft.CodeAnalysis` 5.9.0, and a Roslyn component that references a newer compiler than the
  one running it is *silently refused* — a warning (CS9057), not an error, after which analysis and
  generation simply do not happen. `Microsoft.Net.Compilers.Toolset` now replaces the SDK's `csc`
  so both are actually loaded. It is `PrivateAssets="all"`, so it never flows into a package;
  a consumer on an older SDK still gets the CS9057 behaviour, which is why the components declare
  the floor they need rather than the newest compiler available.
- **xunit v3 → v4.** Test projects move to Microsoft.Testing.Platform. `dotnet test` in its VSTest
  mode no longer drives them.
- **Jint interop pinned to `ArrayConversionMode.Copy`.** Jint 4.14 changed the default for CLR
  arrays to `LiveView`, under which `Array.isArray` on a returned array is **false**. The scripting
  DSL's typings promise `readonly number[][]` from `llm.embed()`, so a script branching on
  `Array.isArray` silently took the wrong path. The pin restores the behaviour the typings
  describe. Note the scope honestly: this governs CLR *arrays*, so a binding returning
  `IReadOnlyList<T>` is host-wrapped under either mode and `Array.isArray` stays false for it.

### Fixed — a team associates a folder, it does not declare one

In Orkeon Studio, « Autoriser un dossier » on a team opened the folder picker:
a disk tree, a physical path, a rights choice. That is the **declaration**
screen, and it belongs to « Réglages › Dossiers autorisés » — nowhere else.
Wired onto the two team screens, it made every team re-declare its mounts from
scratch, next to a settings list that already held them.

The defect was two lines of shell wiring, not the picker: `MainWindowViewModel`
had all three callers subscribed to the same modal. The two team gestures — the
creation wizard's « Dossiers de cette équipe » block, and « Autoriser un autre
dossier… » on an adopted team — now open a chooser over the folders the
settings declare. Ticked entries are carried over **verbatim, rights included**:
the settings are the one place a folder and its rights are decided, and a team
able to widen them would make that declaration a suggestion. A folder the team
already carries, or whose virtual root another folder already spends, says so
and cannot be picked — two mounts on one root is not a merge the runtime
performs, it is one it drops.

Declaring stays the settings' gesture, and « Déclarer un nouveau dossier… » is
one door to it: the modal closes and the app lands on « Réglages › Dossiers
autorisés », on that tab and not merely on that screen. One door, so a folder
cannot be declared from two places and drift between them.

A team folder the settings do **not** declare now reads red — on the wizard's
chips, the "Mes équipes" cards and the team-mounts modal alike. It is not an
error: a team's `/output` and `/input` are created inside the team at adoption
and are never declared. It is the one thing a row cannot say by naming a virtual
path, and a team reaching outside the machine's authorized folders should not
have to be discovered by reading a sidecar.

A team reaching outside the settings no longer launches. « Exécuter » refuses a
team carrying a folder that no settings entry allows: the run button is disabled
and the card names the folders and the two ways out, with a button onto
« Réglages › Dossiers autorisés ». Discovering that refusal from a run that
failed halfway, its reason buried in a log, is what this replaces. The rule lives
in `Orkeon.Studio.Core` (`DeclaredMounts.BlockingFolders`), not in the WPF
screens, so the TUI launcher cannot answer it differently. A team's own `/output`
and `/input` never block it: they are created inside the team at adoption and are
its own plumbing, and counting them would make every adopted team unlaunchable.

The folders the blueprint implies became removable like any other. They were
informative chips with no ✕ — "edit an agent to change them" — which left a team
carrying a root its owner did not want with no way to say so. Dropping one now
sticks: `WithDerivedWriteMounts` no longer re-adds it, the same silent undo that
method exists to prevent. The screen warns and names the dropped roots, because
nothing will be bound to them and the agents writing there will fail; a single
« Rétablir » is the way back from a wrong ✕.

### Fixed — Virtual paths are the only currency agents are paid in (ADR-008)

The owner found absolute disk folders in Orkeon Studio where only VFS mount
points belong. The leak was in the engine, not the UI.

Runners used to mount their own directories **1:1** (`C:\x:C:\x:ro`) so the
absolute paths framework code had already computed resolved unchanged, and
`FileSystemMount.IsValidVirtualPath` had been widened to accept a Windows drive
path as a *virtual* path to let those strings parse. Mounts parsed from a mount
string are agent-facing, so `list_mounts`, `ShellCommandTool`'s path checks and
every access-denied message — which names the available mounts — handed agents
the operator's disk layout; redaction was explicitly disarmed for exactly those
mounts. The crew directory is now `/crew`, a hosted daemon's crews `/crews`,
`/crews-1`, …, and the loader receives the virtual spelling, so it asks the VFS
whether its target is a directory instead of probing the disk.

`Orkeon:FileSystem:InternalMounts` is new: mounts registered with
`MountVisibility.Internal` — reachable by the VFS, absent from
`GetAvailableMounts()`. The `--llm-log` directory moves there; it holds full
prompts and API payloads and was being advertised to every agent as a writable
mount. As a result, turning `--llm-log` on no longer shifts
`Orkeon:FileSystem:Mounts:{i}`.

A denial message is read by the LLM, so it is an agent-facing surface like
`list_mounts`: `FileSystemRegistry` now builds its "Available mounts" and
"Mounts granting Write" lists from the agent-facing mounts only. It used to
enumerate *every* mount, which named `/llm-logs` to any agent that touched an
unmounted path — and annotated it `(writable)`.

In Studio, the agent editor's « Sur quel dossier » line and the Composer's
folder chips now name mounts the way agents address them (`/output (lecture,
écriture)`) instead of joining raw `physical:virtual:rights` strings. Expert
surfaces — the effective-mounts table, the picker's preview — still show the
exact command line.

**An adopted team now writes where its own agents write.** The trial bench
mounts `/output` and refuses a run that did not produce its promised
deliverable; nothing carried that mount further, so the same team launched from
its own folder was denied `/output`, logged a warning and reported success with
nothing written. `forge promote` derives the write roots from the blueprint,
creates their folders, and spells them in both launchers; Studio binds the same
roots into the sidecar at adoption. A deliverable root the runner reserves —
`/crew`, `/script`, `/llm-logs` — and the traversal spellings `/..` and `/.` are
skipped rather than mounted.

**And the promoted launchers now run from the team's own folder.** Neither
`run.sh` nor `run.cmd` changed directory, while both address the crew by an
absolute anchored path: the runner refuses to read a crew outside the working
directory without `--allow-external-mounts`, and the security whitelist is
rooted on the working directory too. Launched from anywhere else — which is
every scheduled run the generated `schedule/` artifacts install, since a service
starts in the system directory — the team was refused outright, or ran and wrote
nothing. Both launchers now `cd` into their folder first. In the same file,
`--var` was spelled once per sample variable, which the CLI's parser rejects as
a repeated option: several values now go space-separated after one flag, the
rule `--mount` already followed.

**Breaking**: `--mount` no longer accepts a drive-letter virtual path, and a
`--mount` claiming a root a runner reserves for itself — `/crew` and
`/llm-logs` on the YAML path, `/script` and `/llm-logs` on the scripting path,
`/crews*` on the `orkeon-host` daemon (exit 78) — is refused with an actionable
line.
`RunnerHost.Build`'s `llmLogPath` parameter becomes `llmLogVirtualPath` and
takes a virtual path; a new optional `internalMounts` parameter follows it.
`RunnerExecution.LoadCrewAsync` keeps its signature but changes contract: its
`configPath` is now a **virtual** path, so a host passing a physical one is
denied by the VFS instead of loading. `RunnerExecution.EnsureReservedRootsAreFree`
is public, for hosts that inject mounts of their own.

**A mount string can quote its physical path.** Three call sites split a spec
on `:` with three different heuristics, and the one in
`CliWorkspaceMountBootstrapper` had none: on Windows it read
`C:\src:/workspace:ro` as the physical path `"C"`, resolved it against the
working directory and emitted a corrupt mount string. The split now lives once,
in the domain type — `FileSystemMount.TryGetBasePath`, `WithBasePath` and
`Quote` are new, and both duplicate heuristics are gone.

A path the bare form cannot carry — one holding a `:` or a `;`, or ending with a
backslash — is **quoted**: `"/data/odd:name":/data:ro`, `"C:\src\":/workspace:ro`.
Quoting rather than backslash-escaping, because a backslash escape would collide
with the Windows path separator, which is exactly what has to survive here.
Backslashes are ordinary characters, so every existing mount string is
unchanged, and two folders that had no spelling at all now have one: a drive
root (`"C:\":/workspace:ro`) and any path ending in a separator. Those quotes
belong to the mount grammar, so a shell must be told to leave them alone — the
CLI reference spells the bash and PowerShell forms.

Every producer goes through `FileSystemMount.Quote`, not just the parsers
through the split: the runners' own crew / script / exchange-log mounts, the
daemon's crew mounts, the two `orkeon-repl` bootstrappers and Studio's
`MountDefinition.ToMountString`. A folder whose name holds a `;` is legal on
every OS Studio's picker browses, and each of those sites used to emit a spec
its own parser then refused.

**And an adopted team can be launched from its own card again.** Studio's target
detector looked for a crew definition at the root of the folder it was handed,
while `forge promote` keeps it in `crew/` — so « Lancer » on a team card
answered *"holds no crew definition… pick a file inside it instead"*, and
picking `crew/` by hand found no sidecar beside it, so the team's mounts never
reached the command line. The detector now descends into `crew/`: the run path
goes to the definition, the *selected* path stays the team folder, which is what
puts the sidecar in view and the team's own `/output` inside the security root.

Two more Studio screens stopped showing folders: the « Importer une équipe »
recognition report joined the sidecar's raw mount strings — read by whoever
*received* the team, so it disclosed the exporter's disk layout — and the
team-mounts modal fell back to the raw string in a field documented as "the
virtual spelling the agents see". All four renderers now share one
`MountLabels`.

Also fixed: two hosted crews sharing a `Name` resolved to different definitions
(the plan kept the last, `CrewHostRegistry.Find` answers with the first);
`examples/service-host/appsettings.host.json` declared its mounts as objects, a
shape `FileSystemOptions.Mounts` cannot bind; the RaggableTree pages documented
a mount syntax that does not exist; and the `--mount` / `--var` help text said
"Repeatable." of options the parser refuses to see twice.

### Fixed — the VFS boundary, asked the same question everywhere

A sweep over the code the ADR-008 diff did not touch, looking for the same
shapes it had just corrected.

**One containment predicate.** "Is this path inside that directory?" was
answered in three places with three rules. `PathValidator`'s was boundary-safe;
the runners' `--allow-external-mounts` guard was a bare `StartsWith`, and the
two disagreed exactly where it hurts: a crew in a sibling folder whose name
extends the working directory's (`~/proj` vs `~/proj-old`) read as *inside*, so
the opt-in was never demanded, its base path never whitelisted, and the
boundary-safe validator then refused every file the crew touched — never naming
the flag that would have fixed it. `PhysicalPathContainment` now holds the rule,
including the part about case: Windows paths are the same path in any casing.

**`ToVirtualPath` answered with the wrong mount.** The registry orders its
mounts by *virtual* path length, which is what the virtual→physical direction
needs; coming back, it returned the first mount whose *base* path matched. With
nested mounts the two orderings disagree, and a file was handed back under a
parent mount's spelling — a name that re-resolves with different rights. The
most specific physical base now wins.

**`/sandbox` existed in one host out of all of them.** The mount the code
sandboxes write under was provisioned by an `IHostedService`, and the runners
build a host they never start — so every shipped CLI registered `ICodeSandbox`,
`DockerSandbox` and `SecureCodeInterpreterTool` over a virtual root that did not
exist. It is now built with the registry, in every host, started or not.
**Breaking**: `AddSandboxMount` is removed; `AddOrkeonFileSystem` does it.

**`shell_command` could not use its own default.** `working_directory` declared
`"."` — and since ADR-008 a virtual path starts with `/`, so no registry can
resolve it and every call that omitted the field, the shape a model writes for
an optional one, was refused. It now runs in the first readable mount, or in the
parent's directory when nothing is mounted.

**Every promoted team was dead on arrival on Windows.** The launcher built its
`--mount` by concatenation, so the physical segment inherited whatever the
anchor expanded to — and `%~dp0` is always `C:\…`. Four segments, a grammar
error naming a path the user never typed. The generated spec now carries the
grammar's own quotes, and a test runs a real shell against a folder whose name
holds the separator. The forge trial bench and Studio's mount prediction went
through the same concatenation; both now go through `FileSystemMount.Quote`.

**The daemon read its crew list from a file it never told anyone about.**
`orkeon-host` resolved a relative `appsettings.json` against the executable's
directory, while `--help` promises `./appsettings.json` and the host built
moments later reads the working directory — so the mounts came from one file and
everything else from another. It also accepted a malformed `--mount` at startup,
logged READY, and then failed every message; that is now a configuration error
with exit 78, and `--help` states the grammar the parser enforces (three
segments, `ro|rw|rwnd`, quoting).

**`--allow-external-mounts` overwrote the operator's whitelist.** It wrote
`PathSecurity:AdditionalAllowedDirectories:0`, replacing the entry an
`appsettings.json` declares there, while its own documentation says it
*additionally* whitelists. It now appends.

### Fixed — configuration that decided nothing

**`process: parallel` now honours `dependencies:`.** The mode ignored them: every
task started at once, so a final synthesis task ran against an empty context and
reported success on the nothing it had. The documentation said to use Sequential
or Graph instead — and **23 of the 30 shipped `parallel` examples declare
dependencies anyway**, which is the clearest possible statement of what the mode
is for. Tasks are now grouped into waves: everything whose dependencies are
satisfied runs concurrently, the next wave starts when they are done and reads
their outputs. A crew declaring no dependency is one wave — the previous
behaviour, unchanged. A cycle is refused, naming the tasks caught in it.

**`AgentSelectionStrategy` now selects something.** The option, its two real
strategies, `StrategyAgentSelectionService` and their DI wiring all existed —
and `IAgentSelectionService.SelectBestAgentAsync` had no caller anywhere in
`src/`, so setting `Embedding` changed nothing at runtime. `TaskAgentSelector` is
that call site: consulted for a task declaring no `agent:`, never overriding one
that does, degrading to round-robin with a warning when the embedding backend
fails. `FirstFit` — the default — keeps round-robin exactly as before.

**An unknown `process:` is refused instead of guessed.** A hand-rolled switch
fell through to Sequential, so `process: graf` ran a pipeline the author never
asked for. Both the YAML and the scripting paths now use `ProcessType.TryFrom`
and name the valid values. **Breaking**: a crew file with a typo'd `process:`
now fails to load instead of running as Sequential.

**A Graph or Autonomous crew is no longer reported as Sequential.** Three
hand-written `MapProcessType` switches restated the six-mode value object with
four arms each and mapped every other mode to `"Sequential"`. They are gone; the
DTOs carry the value object's own spelling.

**`LogStreamingExchanges` is honoured.** It was bound from configuration, offered
as a checkbox in both Studio surfaces, pinned by Studio's settings validator —
and read by no code, so turning it off still captured every streaming exchange.

**Removed**: `OrkeonConfig` and `OrkeonFeatureFlags`. Four documentation pages
presented them as the framework's predefined configurations (`Default`,
`Development`, `Production`); no production code read either, and no DI entry
point accepted one. A host composes its settings through
`AddOrkeonInfrastructure` / `AddOrkeonApplication` and its `appsettings.json`.

Also: the five DLP interceptors' summaries read as descriptions of what the
framework does ("ensure PII never appears in logs") when nothing invokes them —
they are an opt-in toolkit a host applies, as
`docs/reference/opt-in-subsystems.md` already said, and each class now says so
too. And `asyncExecution:` is documented for what it is: recorded on the task,
honoured by no orchestration mode.

### Fixed — Studio, and the team it hands over

**An adopted team can now read, too.** The trial bench mounts `/workspace` and
the Composer shows the chip; nothing carried it into adoption, so a team whose
agents use `file_read` passed its trial and could then read nothing — the exact
mirror of the missing `/output`. `forge promote` and Studio now bind it to an
`input/` folder **inside** the team, and FORGE.md says to drop the readable
files there. Not the team's own root: `--with-settings` puts an
`appsettings.json` holding API keys at that root, and a read mount over it would
hand them to any agent with a file tool.

**One chip per virtual root.** A root the user allowed a folder for was rendered
twice — once as a removable chip, once as an informative derived one — and
removing the removable one changed nothing at save, because the derived binding
silently took its place. The derived list now shows only the roots no explicit
choice claims, so removing a chip brings the derived one visibly back: the
screen says what the save will do. The `DerivedMounts` documentation claimed
"the sidecar never records them", which was false and was the root of the
confusion.

**Studio's TUI launcher started the CLI in the wrong folder.** It derived the
working directory from `RunPath`, correct only while that path's parent was the
folder the user picked — and the ADR-008 detector change made `RunPath` descend
into a promoted team's `crew/`. The fix had landed in the WPF launcher alone.
Both now read `RunTarget.WorkingDirectory`.

**Studio reported "custom" for Gemini** — the endpoint its own preset catalogue
writes. The drift test guarding the pair asserted the constant had been *copied*,
not that the detector recognised it; it now asks the detector.

**Studio's blueprint→mount derivation gained the guards the CLI's copy has.** A
deliverable naming a reserved root (`/crew`, `/script`, `/llm-logs`) or a
traversal segment reached an adopted team's sidecar through Studio and nowhere
else, producing a team Studio could launch and the runner refused at start.
`ForgeDerivedMountTests` pins the pair.

### Fixed — an internal mount is a boundary now, not a hiding place

ADR-008 introduced `MountVisibility.Internal` and said the limitation out loud:
the mount is withheld from every listing and stays **resolvable**, so an agent
that knows the name can address it. The names are documented. `/llm-logs` holds
every prompt and every API response of the run — one
`file_read /llm-logs/llm-exchanges-….jsonl` was the whole exchange history.

`FileSystemRegistry` now refuses an Internal mount by default, in both
directions: `ToVirtualPath` will not name one either, so a tool's
physical→virtual output rewrite cannot leak it. A refused internal mount is
reported exactly like a path that does not exist, and a nested one does not fall
back to its agent-facing parent.

The two components that legitimately write to an internal root — the LLM
exchange logger and the code sandboxes — ask for the new
`PrivilegedFileSystemAccess` by name. A distinct DI registration rather than a
flag on the interface everyone already holds: a tool cannot obtain it by
accident, and every holder is findable by searching for the type. A host that
wires its own `IFileSystemService` instead of calling `AddOrkeonFileSystem`
keeps exactly the behaviour it had.

**Breaking**: `FileSystemRegistry.ResolveAndCheckRights` and `ToVirtualPath`
take an optional `includeInternal` (default `false`), and `FileSystemService`'s
constructor an optional `internalAccess` (default `false`).

### Fixed — what the tests were not asking

**The shipped scripting typings did not parse.** `tools.d.ts` declared an index
signature as `const [name: string]: …`, which a TypeScript namespace cannot
carry and which is not a declaration at all — so the `tools` namespace this
package advertises was unavailable to every editor that loaded it. The only
guard over the typings asserted that certain substrings were present in the
rolled-up bundle, which cannot fail for that. Every `.d.ts` is now handed to
esbuild, the same front end that reads user scripts. `llm.d.ts` went with it:
it typed `llm.openai`/`anthropic`/`ollama`/`azureOpenai`/`groq` as non-callable
objects with a `name` property, while the runtime exposes them as factories
returning a config whose field is `provider` — every script typed against those
declarations got an error on the correct code.

**`docker build .` failed at restore.** The Dockerfile restated
`Orkeon.ConsoleApp`'s project graph as a hand-written COPY list that had drifted
to 10 of its 22 projects, plus one it no longer references. The list is gone;
the graph is read from the tree.

**Two runs started in the same second shared one exchange-log file.** The run id
was a UTC timestamp truncated to the second, against a class claiming "each
logger instance creates a unique file scoped to that run" — and its per-file
lock serializes writers inside one process, never across two.

**The embedding port blew up at startup instead of saying what was missing.**
`AddOrkeonVectorSearch` — which `AddOrkeonInfrastructure(configuration)` calls
unconditionally — registered `OpenAIEmbeddingProvider` **by type**, and its
constructor needs an M.E.AI `IEmbeddingGenerator<string, Embedding<float>>` that
only a host choosing local embeddings ever registers. MS.DI throws when a
registered service's own dependencies cannot be resolved, so
`GetService<OpenAIEmbeddingProvider>()` threw rather than returning null — which
put **both** graceful fallbacks (this one and
`DefaultEmbeddingProviderResolver`'s) behind an exception naming an interface no
operator has heard of, at container build rather than at first embed.
`UnconfiguredEmbeddingProvider` exists precisely to give an actionable message
deferred to first use; it is now reachable. The provider is built from the
generator when one is present and absent otherwise.

**`orkeon doctor`'s `onnx-reranker` check now looks.** It was a hard-coded `ok`
with a hard-coded detail, on the reasoning that the weights are embedded
resources of a package the CLI always references — true, and still not a check:
a trimmed publish or a renamed resource leaves the reranker broken and the
doctor cheerful. It opens the streams and reports the size.

**Removed**: `ImageHelper` (`Orkeon.Tools.Abstractions`) — no production caller,
and where it disagreed with the live `ContentConverter` it was the wrong one:
it declared SVG a supported image type, which no vision API accepts.
`CliFileSystemService` (`orkeon`) — no production instantiation, a
`ResolveAndValidate` that ignored its `requiredRight` argument entirely, and
prefix compares with no separator boundary.

Also: `AgentMapper` hardcoded `Status = "Active"` while the two handlers that
actually map an agent read `agent.Status` and never called it — it reports the
real status now, and the handlers go through it. The `Orkeon.Tools.FileSystem`
layering guard was a denylist of two names under a doc describing an allowlist
that was already false; it is an allowlist. Four `*_ShouldHandleEvent` tests
whose only assertion was `Assert.True(true)` are gone — their siblings assert
what the handlers produce. And the forge sandbox's stated write boundary now
matches its mounts (the session directory, not the `/output` folder inside it),
`asyncExecution:` and the crew `rag:` block say what they do, and the CLI
reference carries the caveat both getting-started pages already had about
`orkeon run <dir>/crew`.

### Fixed — the adversarial pass, turned on the sweep's own work

The sweep that produced the sections above was reviewed by agents briefed to
refute it rather than confirm it. The deletions held: nothing removed was
reachable, and the database security policy the removal was accused of dropping
was in fact the weaker of two copies — the surviving
`Orkeon.Tools.Data.Relational.DefaultDatabaseSecurityPolicy` blocks stacked DDL
after a benign `SELECT`, neutralises comment prefixes and gates `UPDATE` without
a `WHERE`, none of which the deleted one did. What did not hold was the work
*around* the deletions.

**An encrypted memory item kept its content and lost its identity.** The
decorator rebuilt every item through `MemoryItem.Create`, which mints a fresh
`MemoryItemId` and stamps `CreatedAt = UtcNow`, `AccessCount = 0`,
`LastAccessedAt = null` — fields no caller can pass. `SqliteMemoryRecord`
restores exactly those from storage on purpose; wrapping that provider in
`EncryptedMemoryProviderDecorator` threw the work away again, so an encrypted
long-term memory reported the moment it was decrypted as its creation time and
never accumulated an access count. Ageing and recency-ordering read wrong
values, and only with encryption switched on. The rebuild goes through
`MemoryItem.Restore` now, which carries identity and metadata over whole rather
than enumerating fields that can be forgotten. Consolidating the six inline
rebuilds into one place had fixed the two dropped metadata fields and asserted
completeness without checking identity.

**Removed**: `FeatureFlags` (`Orkeon.Application.Configuration`). Its twin
`OrkeonFeatureFlags` was removed above for having no production reader; this one
sat in the same folder with the same profile, and was the more misleading of the
two — `ShouldUseForAgent` / `ShouldUseForCrew` implement hash-bucketed gradual
rollout over `TrafficPercentage`, so its public surface offers to canary a
percentage of crews onto `UseSequentialCrewOrchestrator`. Nothing called it. Its
only consumer was its own test file.

**Removed** (breaking, packages): `Orkeon.Application.Abstractions.Data`
(`DatabaseQueryOptions`, `IDatabaseProviderFactory`, `IDatabaseSecurityPolicy`)
and `Orkeon.Infrastructure.Data` (`DatabaseProviderFactory`,
`DefaultDatabaseSecurityPolicy`) — recorded in the public-API files but not
here. The migration is a namespace change, not a rewrite: the surviving types
carry the same names under `Orkeon.Tools.Abstractions.Data` and
`Orkeon.Tools.Data.Relational`, so a consumer sees "type or namespace not found"
and needs one `using` changed. `docs/architecture/security.md` names the
namespace now instead of the bare interface.

**`Orkeon.Infrastructure` no longer drags in two database drivers it does not
use.** `Microsoft.Data.SqlClient` (with its `Azure.Identity` /
`Microsoft.Identity.Client` chain) and `MySqlConnector` were referenced for the
removed `DatabaseProviderFactory` alone. `Orkeon.Tools.Data` references them and
is where they belong; every consumer of the Infrastructure package was carrying
their restore weight and CVE surface for code that no longer exists. `Npgsql`
stays — `Checkpointing/PostgresStateStore` uses it.

**A promoted team could not write the deliverable it was built to produce.** The
read mount is derived first, so a deliverable landing under `/workspace` met a
read-only entry and was skipped on the name alone: the launcher spelled
`/workspace:ro`, the deliverable resolver logged a warning, and the run reported
that it had finished. Studio's sibling derivation deduped the other way and left
a read-only `/workspace` beside a read-write one — two chips for one root, the
one promising a write being the one silently dropped. Both hold the same rule
now: one root, one mount, and a write requirement wins over a read one.

**A deliverable folder named with a `:` or a `;` produced a launcher that died at
every start.** Those are the mount grammar's own separators — the launcher quotes
the physical segment and spells the virtual one bare — so `--mount
"…/rapports:2026":/rapports:2026:rw` reached `FileSystemMount.Parse` as four
parts, after `ForgePromoter` had already created the folder, so the team looked
complete. `ForgeBlueprint.Validate` refuses the root at submit time, where a
repair turn can rename the folder, and the derivation refuses it again.

**`FORGE.md` recommended the command the docs warn about.** The card said "the
folder is ordinary: `orkeon run <dir>/crew` launches it too" — which is true, and
launches it *without* the `--mount` arguments the launchers supply, so a team
with deliverables writes nothing and reports success. Both getting-started pages
and the CLI reference carry that caveat; the card is what the colleague receiving
the folder reads, and it was the last surface still giving the bare command.

**The launcher followed a symlink to the wrong folder.** `dirname "$0"` on a
symlink gives the *link's* directory, so symlinking "run this team" onto `PATH` —
normal for a folder the card calls ordinary — made the launcher mount `~/bin/output`
and die naming folders the user never created. It resolves the link chain first,
with plain `readlink` rather than GNU's `-f`, and the `cd` now carries `|| exit 1`.

**An `appsettings.json` mount was silently dropped on every single run.** The
runner writes its own mounts into `Orkeon:FileSystem:Mounts:0`, `:1`, … from an
in-memory source added last — which wins on an identical key. So index 0 did not
add a mount, it replaced the operator's first one; and `cliMounts` is never empty
in a real run, since the crew mount is inserted at index 0 before this code sees
it. Every tool touching that mount then failed "no mount found", with nothing
anywhere saying a mount had been dropped, and `orkeon-host` lost one per hosted
crew directory. The overwrite had already been found and fixed for the sibling
key `PathSecurity:AdditionalAllowedDirectories` — in the same pass that left it
standing here, and then copied its shape into the brand-new `InternalMounts` key.
All three append now, and the regression test asserts it on the merged registry
rather than on the mount strings, which is what the existing suites looked at.

**`docker build .` did not build.** `src/Directory.Build.props` imports the file
above it with an unconditional `<Import>`, so with the root `Directory.Build.props`
absent the expression evaluates to `""` and MSBuild refuses it (MSB4020) —
`restore` tolerates the empty import, `publish` does not, which is why the layer
that fails is not the layer that looks wrong. Replacing the hand-written project
list with `COPY src/` did not fix the Dockerfile, and nothing in CI builds this
file, so the claimed fix was never executed once. It is copied now, and the image
builds.

**`crew.process` was validated where nothing could recover from it.** Unknown
values are refused rather than silently becoming Sequential — but
`ForgeBlueprint.Validate` never checked the field, so `blueprint_submit` accepted
`process: pipeline` and answered "Blueprint submitted", and the throw landed in
`ForgeBlueprintCompiler.Compile`, called un-guarded from the validate stage, the
render stage and `ValidateEditedBlueprint`. None of them turns it into a
validation error, so it never reached the two-attempt repair loop built for
exactly this: the session died at the CLI boundary with the interview and
blueprint turns already paid for, and `forge resume` reloaded the same artifact
and died at the same point. The check now runs at submit time, where the model
can act on it.

**An internal mount was a boundary in the virtual namespace only.** The commit above
refuses the name `/llm-logs`; it refused nothing to the bytes. With the log
directory nested inside an agent-facing mount — the ordinary arrangement, since
`--llm-log ./logs` needs no `--allow-external-mounts` precisely because it stays
under the working directory, and the working directory is what gets mounted for
the agents — `/workspace/logs/llm-exchanges-….jsonl` returned the very file that
`/llm-logs/llm-exchanges-….jsonl` was refused for, and `ToVirtualPath` handed
that address out. Both directions enforce physical containment now. Its own test
suite mounted the two directories as siblings.

**`/sandbox` was mounted in every host and usable in none.** Resolving a virtual
path is two steps: the registry answers *where*, then `IPathValidator` answers
*whether*. Moving the sandbox mount into the registry fixed the first and left
the second denying it — the session directory lives under the temp directory
while the validator's workspace root defaults to the current one — so the first
call of every code execution kept failing, saying "Path is outside the allowed
workspace directory" instead of "No mount found". No flag rescued it:
`--allow-external-mounts` whitelists the CLI and internal mount lists, and the
sandbox root is in neither, because it is injected rather than configured. The
session root is registered as an allowed directory, and the test exercises the
real validator instead of stubbing it.

**The sandbox janitor deleted directories it had not created.** It swept every
subdirectory of `EphemeralRoot` older than the threshold, recursively, checking
neither the name nor whether the owning process was alive — and it now runs in
every process that builds a VFS rather than only in a started host. A directory's
mtime freezes once its direct children exist, so a daemon idle past the threshold
looked exactly like an orphan and a CLI invocation would delete its sandbox
mid-run; and since `EphemeralRoot` is a free-form string whose directory this
code creates, pointing it at an existing folder made the sweep a recursive delete
of user data. Both guards are in place. Runner flows also dispose their host now,
so the session directory goes at the end of the run rather than waiting for a
later sweep, and `/sandbox` joins the reserved virtual roots — a user `--mount`
claiming it was crashing a DI factory instead of printing the one-line refusal.

**A stalled embedding endpoint killed the crew and blamed the user.**
`TaskAgentSelector` rethrew every `OperationCanceledException`, but an HTTP
timeout inside the embedding backend surfaces as one too — and definitionally is
not the crew's token, since the adapter passes `CancellationToken.None` down. It
went straight past the degrade-to-round-robin path the class exists for, and the
terminal event reported a cancellation nobody requested. The guard is conditioned
on the caller's token.

**The scripting typings did not compile, in a new way.** `tools.d.ts` traded an
index signature a namespace cannot carry for a `namespace tools` beside a `const
tools`, which do not merge (TS2300/TS2395); `llm.d.ts` introduced a second
`LlmConfig` colliding with the one in `agent.d.ts` (TS2687 on all six members,
TS2717 on `model`) — a net-new break in a file that pass never opened. Both
passed the new typings test, because it runs esbuild: a transpiler strips types
and reports neither. `tools` is one interface with one `const` now, `LlmConfig`
is declared once, and the suite gained a check for the duplicate-declaration
shapes a transpiler structurally cannot see. The removed `agent.d.ts` copy also
documented a literal the runtime discards — `ExtractLlmConfig` returns null for
anything that is not a `JsLlmConfig`.

**The inbound process-type map still collapsed unlisted modes into Sequential**,
and the DTO enum stopped four modes short of the six the domain carries, so a
crew created through `CrewMapper` could not be Graph or Autonomous at all and
asking for one produced a Sequential crew that ran to completion. The enum
carries all six; an out-of-range value is an argument error. The test that
asserted the fallback asserted it by name.

**A command that is not installed put a disk path in front of the model.**
`ShellCommandTool` now hands `ProcessStartInfo` a resolved physical working
directory, and `process.Start()` sat outside the outbound rewrite — so any
allowlisted-but-missing binary returned "…with working directory '/tmp/…'" to
the LLM. The start is redacted like every other outbound string.

**Two containment copies survived the pass that claimed to unify them.** The
registry's own anti-traversal check — inside the very method the boundary suite
exercises — and both guards in Studio's `TeamCatalog` hardcoded `Ordinal` and
knew nothing of `AltDirectorySeparatorChar`. All five call sites route through
`PhysicalPathContainment` now, and the type's own doc says five rather than
three.

**The VFS exception table pointed at a file that was deleted in the same pass.**
`docs/architecture/vfs-compliance.md` and its French mirror still listed
`Scripting.Cli/CliFileSystemService.cs` as a ratified permanent exception —
doubly wrong, since that file was never in the analyzer's allowlist to begin
with; it carried an inline suppression. The neighbouring bootstrap row was
updated for `SandboxSession` in the same edit, so the row was read and left.

## [1.0.0-rc.2] - 2026-08-25

### Added — Remediation v3: what a run costs, and adoption that is no longer a one-way door

The owner's third design pass (RC2-FEAT-06) lands two engine-backed features and
a conformity sweep of the wizard.

**Usage metrics on the wire (W-08).** The cache dimension joins the token
telemetry end to end: `TokenUsage` carries the prompt-cache hit/miss pair (a
*partition* of the prompt tokens, never an addition) with a computed hit ratio;
every orchestration strategy stamps tokens+cache on its per-task snapshots; the
`CostUsageEvent` sink channel carries the same pair. `orkeon run`'s
`run.finished` now says what the run cost (`durationMs`, prompt/completion
split, cache pair), and the forge trial's `run.finished` and `verdict.ready`
carry the trial's own figures — distinct from the session-cumulative
`cost.updated`. Studio shows them as one chip recipe («12 840 tokens» ·
«cache 62 % · 7 980 tokens» · «59 s») on the verdict card, the launcher's finish
line and the history entries (tolerant schema). Not measured = no chip, never a
zero.

**Modify, re-try, re-adopt (W-09).** `forge resume` of a promoted session (or an
abandoned one that reached a verdict) reopens it at the arbitration — the stored
verdict re-announced first; a new `retry` decision re-runs the trial as-is (zero
compose tokens, one budget iteration, refused recoverably on an exhausted
budget); `forge promote` to the session's own `promotedTo` updates the team
folder in place (generated files regenerated, user files preserved, a dropped
schedule removed) — any other non-empty destination stays refused. In Studio,
« Modifier » on a team card reopens the wizard at Composer with the stepper
fully reachable and the adoption fields seeded from the sidecar; re-adoption is
pinned to the original folder; the saved card offers « Modifier l'équipe » and
« Refaire un essai ». The blueprint's `crew.name` must now be a short display
name (≤ 60 chars, repairable error) — it becomes the team's folder name.

**Edit at the dry pause (W-10).** `forge resume <slug> --edit` amends the
blueprint of a session paused before its trial: the amended JSON travels as the
channel's first inbound line, is validated in full (parse, compile, tool
catalogue), re-announced `blueprint.ready` on the **current** iteration — no
charge: that iteration's trial has not run yet — then re-rendered
deterministically; with `--dry` the session pauses again at the same boundary,
and an invalid edit leaves it exactly where it was (recoverable
`FORGE-BLUEPRINT-INVALID`). This wires Studio's « Modifier » on the Composer
step's agent cards, which was greyed out at the pause: the agent editor now
applies through a `resume --edit --dry` child run and the Composer repaints with
the amended team. At the arbitration, the `edit` decision remains the path.

Conformity (mock v3 volets): the stepper pills centre number and label; the
verdict buttons live in the verdict card (accept primary); one shared chip
recipe (`ToolChip`/`ChipAction`) across every tool, mount and metric chip; card
headers align title and mono meta; the « Définition générée » card shows the
rendered YAML itself with a Copy action; the Adopt step's model setting is a
profile card with unfoldable radio rows (no ComboBox); the Composer folder row
shows the mounts the agents imply (deliverable roots + the sandbox read mount).
Also fixed: a cold resume of the Composer pause left « Essayer l'équipe » dead
(the hydrator now restores the session identity from `session.json`).

**True half-circle pills (W-11).** WPF, unlike CSS, does not clamp
`CornerRadius` to half the element's height — the `999` reflex deformed every
chip's ends into ogives. Every pill now carries a fixed height with a radius of
exactly half of it: `ToolChip` 22/11, new `MetricChip` 20/10 (tokens, cache,
wall time), `BadgeBase` 20/10 (plus `BadgeNeutral`/`BadgeSky` variants), and no
chip border hard-codes a radius in a view any more. Action buttons are not
pills: `ChipAction` drops from 12.5 to the 10 cap. A conformity test sweeps the
XAML (no oversized radius anywhere) and pins radius == height/2 on the pill
styles.

### Added — Remediation v2: team folders end to end, and the blueprint edited by hand

The owner's second design pass (RC2-FEAT-05) closes the gap between the v3 mock
and the app around one idea: **a team's folders are part of the team**. The
adopted team's sidecar now records its mounts; the "Mes équipes" cards show them
as chips with a three-tone badge (scheduled / to try / on demand), the agent
count, the last run from the launch history and the model setting; « Changer les
dossiers » opens the team-mounts modal, and every folder choice goes through the
shared « Autoriser un dossier » picker (path + browse, one-level tree, rights in
two radio rows, expert mount-string preview). Studio lays those mounts on each
launch as `--mount` arguments — the chips and the command cannot disagree.

The forge protocol's reserved `edit` arbitration is implemented: interactive
mode now arbitrates every verdict (accepting a conforming one costs one click),
and `decision.made {edit}` followed by `blueprint.edited {blueprint}` re-renders
deterministically — zero LLM tokens — then re-earns its verdict through the
unchanged validate/test/diagnose path. The wizard's Composer step shows one card
per blueprint agent (its own goal and tools) with « Modifier » opening the agent
editor (name ↔ role, one-sentence goal, togglable capability chips), plus
« Ajouter un agent » and the « Dossiers de cette équipe » block written into the
sidecar at adoption. A novice help rail explains each step in plain words.

Also: « Validation à blanc d'abord » on the Exécuter screen (a failed dry pass
stops the launch), the import report's folders row, the expert TRIAL COMMAND
card, « Copier le rapport » in the Diagnostic header with its « Copié ! »
feedback, the plain-words novice title on the checks card, the Limites cards'
descriptions under their titles, the export action on team cards (never copies
`appsettings.json`), and the removal of the settings validation card (the status
line reports instead).

### Fixed — Kimi's server-mandated temperature self-heals instead of killing the run

Moonshot rejects some models' requests with `400 invalid temperature: only 1 is
allowed for this model` — and which models mandate it is decided server-side.
`KimiLlmProvider` now reads the constraint from the API's own rejection and
re-sends the request **once** with the mandated value, logging a structured
warning (never a silent substitution). The seam is a new overridable on
`OpenAICompatibleProviderBase` (`TryAdaptRejectedPayload` — one adaptive retry
on a 4xx, non-streaming paths only), available to any provider with a
server-stated constraint. Before this, a Studio wizard run on a Kimi profile
died at the brief stage after three identical rejections.

### Changed — The WPF screens remediated against the v3 mock (audit T-01…T-13, screens 01–22)

A full-screen audit against the design mock (the mock is the source of truth)
drove a six-lot remediation of `orkeon-studio`:

- **Foundations** — the logo's rings rotate as one group (they were skewed by
  double rotation origins); MODE and EN/FR are real segmented controls; implicit
  styles for TextBox/ComboBox/CheckBox/RadioButton/ScrollBar erase the native
  Windows chrome; every raw `CornerRadius="999"` outside badges is bounded to
  height/2; the splash mascot PNG is re-flattened so its corners land exactly on
  the plate colour. The window holds a **1024×768 minimum**.
- **Run** — rebuilt as the mock's vertical cards: a sidecar-backed team card, a
  plain-language progress card (state title, tone badge, "Ouvrir le résultat" on
  the first writable mount) and a technical journal folded by default in novice;
  COMMANDE and the option rows are expert-only; a localized banner replaces the
  raw locator message when the CLI is missing.
- **Language** — every user-visible French string says « équipe » (pinned by a
  resx test); mode-dependent subtitles carry the mock's wording; validator
  findings and doctor checks show a per-code plain-language overlay first, the
  raw CLI-grade line staying as expert detail; the Limits cards are titled in
  plain language.
- **Screens** — History becomes a card list with per-run duration
  (`LaunchHistoryEntry.DurationSeconds`, tolerant migration) and per-card
  Replay/Open-result; Test is two columns with a full-height console and a
  sample-inputs field; Import gains a drop zone, a single Browse, a recognition
  report and an expert "Test it first" hop; the mounts form no longer opens in
  an error state and novice gets one card per folder plus an "Allow a folder"
  picker; Diagnostic opens on a verdict card fed by a silent first doctor run;
  the Create gate is a radio list.
- **Startup & secrets** — the window opens at the mock's 1024×768 baseline
  (still resizable up); the Réglages model tab gains an **"API keys" card**:
  one row per environment variable the profiles resolve (status chip, paste
  field, "Mémoriser la clé"), storing through the same `IApiKeyStore` as the
  profile editor — the value lands in a user environment variable and the
  field empties once stored; no key ever enters a file.
- **Polish** — novice Settings auto-saves on every edit (Save/Validate stay
  expert; the validation card is expert-only, keeping the copyable report);
  profile cards carry "Utilisé par" team chips; Limits cards say when the
  engine's defaults apply; the raw-JSON viewer is bounded; the teams counter
  is a quiet right-aligned figure; History/Test/Run have empty-state phrases;
  team cards gain an "Ouvrir le dossier" explorer action.

### Added — Every provider one click away, the API key novice-proof, and a screenshot campaign

The model-profile editor (design v3, "volets" rev. 2) now carries the **full provider
catalogue**: the two local runtimes (Ollama, Docker Model Runner — free, keyless), one
card per cloud the framework ships a provider for (OpenAI, Anthropic, DeepSeek, Mistral,
Gemini, Groq, Together AI, Qwen, Kimi, HuggingFace, Z.AI — endpoint, default model and
key variable pre-filled from the drift-pinned runtime constants; Azure OpenAI stays a
"Compatible OpenAI" entry by design), the OpenAI-compatible catch-all and the echo
fallback, grouped as the mock groups them. A novice clicks a card and pastes the API
key **in the editor**: "Mémoriser la clé" stores it in a user environment variable
(the vendor's conventional name — `DEEPSEEK_API_KEY`, `ANTHROPIC_API_KEY`, …), the
status chip says whether one is in place, the vendor's key console is named, and the
connection test refuses to probe into a guaranteed 401 when the key is missing. The
profile store only ever carries the **name** of the variable (`ModelProfile.KeyEnvName`
— the round-trip test forbids the `ApiKey` substring in the file as a tripwire);
launches resolve it and lay the value over the child process as `ORKEON_Llm__ApiKey`
(Run, Test, history replays, and the wizard's assistant alike).

`orkeon-studio --capture-screens <dir>` walks every screen in both modes (plus the
profile-editor and About overlays) and writes one PNG per stop — the
fidelity-remediation reference collection against the design mock, one command on a
Windows machine.

The Diagnostic screen gains **"Copier le rapport"**: WPF text blocks are not
selectable, so the whole `orkeon doctor` result (verdict, every check verbatim, parse
error if any) is now one click away from the clipboard.

The Settings screen's validation card gains the same copy affordance as the
diagnostic ("Copier le rapport"): summary line plus every finding with its severity,
verbatim.

`OrkeonBinaryLocator` learns the **development-checkout layout**: when Studio runs
from its own `bin/` inside a clone (detected by walking up to `Orkeon.sln`), it probes
`src/scripting/Orkeon.Scripting.Cli/bin/<Configuration>/<tfm>/` — same configuration
first, the sibling second — so F5-from-the-IDE finds the CLI the repo just built
instead of reporting it missing. The not-found message now names the dev gesture
(`dotnet build src/scripting/Orkeon.Scripting.Cli`) — and the whole lookup is now
operator-steerable, in order: the **`--cli-dir <dir>`** argument (WPF app), **next to
the executable**, the **`ORKEON_CLI_DIR`** environment variable, `PATH`, then the
development checkout. The not-found message lists that exact order.

### Changed — Full documentation audit against the implementation (DOC-04)

A five-domain adversarial audit (getting-started/README, architecture, RAG/RaggableTree/
EventHub/ADRs, reference, orchestration/guides) verified every factual claim in the docs
against the code and fixed ~140 confirmed discrepancies, in both languages. The heaviest
classes: **phantom APIs** (`AddOrkeonRuntime`, `CodeSecurityAnalyzer`/`CodeSandbox`/
`SensitiveDataDetector`/`ComplianceChecker`/`RetryPolicy`-family types, the
`ValidateServerCertificate` opt-out, `manager_llm`/`autonomous_budget`/`state_timeout`/
`json_search`/`csv_search` YAML keys, the `raggableTree:` crew-YAML section, the
`Exp07CommandSurfaceTests` suite, `Orkeon.Examples.Runner`); **YAML samples teaching a
shape the loader silently loads as an empty crew** (a `crew:` root wrapper and
sequence-form `agents:`/`tasks:` — the schema is flat-rooted with id-keyed mappings, now
taught correctly in process-types/autonomous/blueprint); **stale capability matrices**
(response-format and vision are capability-driven across the 13 providers — not
"DeepSeek only"/"Anthropic+OpenAI only"; Anthropic thinking is `toggle`, `adaptive` is
its wire value); **wiring claims corrected to what composition roots actually do**
(the `AddOrkeonInfrastructure` overload capability table, MCP being effectively
library-only, the empty `InMemoryToolRegistry` stub vs `ServiceProviderToolRegistry`
in both tool-authoring guides, StrictTools lenient library default, EventHub telemetry/
metrics marked designed-not-built, the autonomous YAML budget being `Permissive` not
`Default`); plus the settings-resolution chain, the complete `orkeon run` flag table,
`yaml-schema.md` gaining the six shipped blocks it omitted (`llm:` full surface,
`links:`, task `tools:`/`deliverable:`/`llm_override:`), `[ToolContract]` added to the
new-tool guide, VFS diagnostic help-link anchors, and ADR-002/005 amendments recording
their drifted counts.

Four code fixes rode along: `RunArgumentsBuilder` now emits a **single** `--mount`/`-V`
flag with space-separated values (the CLI parser rejects a repeated option — multi-mount
and multi-variable Studio launches were broken); `AddOrkeonApplication(Action<…>)` no
longer silently drops `AgentSelectionStrategy` from the options copy; the three
`examples/runners` packables are re-aligned on `1.0.0-rc.2`; and the plugins CA2000
suppression justification now states the real ownership contract.

### Changed — The tool inventory rebuilt against the implementation, and gated (DOC-03)

`docs/tools/inventory.md` (and its French mirror) claimed 43 tool names out of the 79
built-in tool classes, listed two tools that do not exist (`bing_search`,
`google_search`), used registry names the code never had (`delegate_work` and
`ask_question` — the real names are `delegate_work_to_coworker` and
`ask_question_to_coworker`), and flatly denied XLSX support while `xlsx_reader` and
`xlsx_writer` ship in `AddOrkeonDataTools()`. The page is rebuilt as the reference
catalogue: every one of the 79 tools with its exact agent-visible name, class, owning
DI extension and secrets policy; an availability matrix per composition root
(`orkeon run` vs the REPL — they do not register the same suites); the complete
alphabetical YAML mapping; an "outside the catalogue" section (forge-internal tools,
`McpToolAdapter`, test doubles); and the counting rule stated so the number is true by
definition. Secondary surfaces follow: `autonomous.md` no longer implies `spawn_agent`
is wired (no shipped root registers it — now a documented limitation) and its YAML
example stops citing the nonexistent `json_search`/`csv_search`; `bootstrap.md`'s suite
comments match what each extension actually registers; the porting guide and the
READMEs mention Office (DOCX & XLSX) explicitly.

`scripts/check-doc-claims.py` gains the gate that makes the regression class
impossible: it extracts every agent-visible tool name from the code (the
`[ToolContract("…")]` positional or the `Name => "…"` literal of each counted file) and
fails the build if a shipped tool is missing from the inventory (either language), if
the inventory tables a name no tool bears, or if the summary total drifts.

### Changed — Orkeon Studio v3 "volets": the WPF app becomes a team-lifecycle product

The Claude Design v3 handoff is implemented end to end. The window's sidebar now follows the
life of a team — **Agent teams** (Create a team, My teams, Import), **Work** (Test, Run,
History), **Environment** (Settings, Diagnostic) — under a global **Novice/Expert** switch
persisted with the theme and language: Novice explains each step and shows contextual help,
Expert shows the machinery (command lines, raw JSON, technical journal, the Test screen).
The window opens on a startup screen (Kama, the mascot — click to skip) and carries an
About overlay; the guided tour is rewritten to the five v3 stops.

- **Settings, unified**: the former Start/Sections/Mounts/Raw screens fold into one
  "Réglages" entry with four inner tabs (AI model, authorized folders, expert-only
  limits & logs, expert-only raw file with its location and resolution chain). The model
  tab introduces **named model profiles** (`studio-model-profiles.json`, next to
  `studio-history.json`): reusable settings, a default election mirrored into the `Llm`
  section through the ordinary save cycle, per-profile `ORKEON_Llm__*` environment
  overrides, and the profile Studio's own assistant runs on. The API key never enters the
  store. The preset picker retires; the profile editor offers the same `orkeon init`
  catalogue with a live connection probe. The Diagnostic sidebar entry gains a warning dot.
- **The creation wizard replaces the Atelier UI** (breaking for the screen, not the
  engine): "Créer une équipe" walks Décrire ▸ Composer ▸ Essayer ▸ Adopter over the same
  `orkeon forge --events jsonl` child — the stepper projects the engine's milestones, the
  per-step "consigne + questions" blocks travel down `user.message`, arbitration buttons
  come from `decision.needed`'s own options, "Fix and retry" carries the trial consigne,
  and the wizard is gated until the assistant has a model profile (handed to the engine as
  environment overrides). `ForgeClient`, the session model and the protocol are unchanged.
- **My teams**: adoption promotes straight into `~/Orkeon/teams/<slug>` with the engine's
  real schedule grammar (on demand, `daily@HH:mm`, `hourly`) and writes a
  `studio-team.json` sidecar (name, need, profile, displayed schedule — never a key).
  The sidecar's profile rides every launch of the team — Run, Test, and history replays —
  as `ORKEON_Llm__*` environment overrides resolved against the profile store.
  Team folders are listed as cards (launch, duplicate, delete), next to the wizard
  sessions still underway, resumable where they stopped.
- **Import and Test**: "Importer" recognizes a shared team with the launcher's own
  detector, warns loudly when a definition carries a pasted secret, and copies into the
  teams root only on confirmation, under a never-overwriting slug. The expert "Tester"
  screen runs blank-run validations and real trials over a dedicated launcher with no
  history store. In Novice, the run screen hides the options machinery entirely.

The two Terminal.Gui apps (`orkeon-studio-config`, `orkeon-studio-run`) keep their current
surface. Nothing is deployed, so the old WPF screens are removed without shims; the resx
pair moves to 395 keys per language, still pinned by the drift and parity tests.

### Added — Studio staffs the `client://studio` seat: agent requests answered on screen

An agent's `send_request` to the watching client no longer dies of a 30-second timeout: the
Launch screen shows the request and a human answers it.

- **Protocol**: `hub.message` gains an `expectsReply: true` field, emitted **only** on an
  agent's `send`. The peer must not have to guess which correlated lines are questions — a
  topic relay can carry a `correlationId` too, and the answer to the peer's own `send` pairs
  by correlation as well. The field is additive; readers that ignore it lose nothing.
- **Studio Core**: `RunProgressModel` folds marked sends into a `PendingAgentRequest` queue
  (dedupe by correlation id, cleared at `run.finished`, `ReplyAccepted()` mirroring
  `AnswerAccepted()`), alongside the untouched `HubMessages` journal.
- **Studio WPF**: a request panel — asking agent's hub address, raw JSON payload, reply box —
  in the Launch screen's progress card. The typed reply goes down stdin as the
  `{"kind":"reply",…}` line the bridge requires; text that parses as JSON travels as that
  JSON (an agent may await a shape), anything else as a plain JSON string. A refused write
  keeps the request on screen. Hub posts are now listed in a compact panel instead of being
  parsed and shown nowhere. Silence past the agent's own timeout remains a refusal — the
  screen just gives the human a chance to speak before it.

### Added — Real Discord slash commands: `/status` and `/stop`, registered and gated

The host's two commands stop being text parsing and become what Discord users expect:
registered slash commands, autocompleted by the client, answered **ephemerally** — a status
poke or a refusal is the invoker's business, not one more line in everyone's thread.

- **Registration at connect**: globally when `Discord:GuildIds` is empty (zero configuration,
  Discord caches global commands up to ~1 h), or per named guild for immediate availability
  (the dev loop). A registration hiccup logs a warning and leaves the message channel alive —
  it never kills the daemon. An unparseable guild id refuses the start (exit 78).
- **The text parsing is removed** (nothing is deployed, no dual path): the platform's client
  intercepts the slash, and a literal `/stop` arriving as plain message content is a prompt
  like any other.
- **Security fix along the way**: the Stop **button** used to bypass the allow list entirely —
  `component.User` was never read, so anyone who could see the thread could kill a run while
  typing `/stop` was gated. Button and slash commands now converge on one
  `CommandInvocation` path checked against `AllowedUserIds`; an unauthorized click gets an
  ephemeral refusal instead of a silent stop.
- The privileged `MessageContent` intent stays: runs are started by plain thread messages,
  which slash commands do not replace.

### Added — The Atelier: `orkeon forge`, from a need in plain words to a deployable crew (FORGE-01→08)

The missing step between "I have a problem" and a running agent team. `orkeon forge
"summarize my supplier's new offers every morning"` opens a short interview, captures a
structured brief — goal, inputs, **acceptance criteria**, a sample input — plans a team,
renders it as ordinary crew files, validates it, **tries it in a sandbox on that sample**,
and judges the result against the criteria the user stated. Not conforming? The diagnosis
feeds a refine loop, bounded by a hard three-dimension budget (iterations, tokens, wall
time). `orkeon forge promote <slug> --to <dir>` then ships the crew as an ordinary folder.

The spine is deterministic — a `StateMachine<ForgeState, ForgeTrigger>` with 11 states,
checkpointed after every step — and only schema-validated `brief_submit` /
`blueprint_submit` submissions advance it: the conversation is carried by an embedded crew
(the scripting DSL running under Jint, `ctx.llm.act()` reached natively), but it never
steers the cycle. Sessions live under `.orkeon/forge/<slug>/` — resumable
(`forge resume`), diffable between attempts, auditable — and every run emits a versioned
JSONL event protocol (`--events jsonl`), golden-pinned on both sides of the wire.

- **Two formats, one generation.** The assistant produces a single schema-constrained
  blueprint; two deterministic renderers derive from it — the per-entity YAML layout
  (default) or an editable `crew.ork.ts` (`--format script`, needs esbuild; absent, a new
  session falls back to YAML with `FORGE-ESBUILD-MISSING`). Both converge on the same
  `CrewDefinitionValidator`, and the sandboxed try loads the crew **from the rendered
  files** — what was written is what runs.
- **The sandbox restricts by removing tools from the catalogue**, not by hoping the model
  abstains: `shell_command` and `code_interpreter` are gone from the list that feeds *both*
  the blueprint prompt and the validation, and writes are confined to the session's
  `/output` mount, snapshotted per run.
- **The verdict is recomputed, never trusted**: score ≥ 0.7 *and* no blocking finding, with
  a missed `must` criterion blocking regardless of score. With no judge available, the
  verdict announces itself as `deterministic` and leans on mechanical checks — it never
  invents a passing score. Accepting a non-conforming result on sight stays legitimate.
- **Promotion is honest about its limits**: the folder carries `crew/`, `run.sh`/`run.cmd`
  composed against the CLI's own `orkeon run` grammar with the sample inputs pre-filled,
  and `FORGE.md` — the crew's identity card, written in the interview's language. With
  `--schedule daily@HH:mm|hourly`, the Windows task XML, systemd timer and cron line are
  generated and the install command is **displayed, never executed**: Orkeon has no
  scheduler, and pretending otherwise would promise supervision it cannot give.

**In Orkeon Studio**, the same engine drives a new **Solve** screen (WPF): a conversation on
the left, one card at a time on the right — success criteria, the proposal in plain words,
the live try, the ✔/✘ checklist quoting those criteria verbatim, then "what now?". Studio
spawns `orkeon forge --events jsonl` as a child process and answers over stdin; it never
touches an LLM itself, and a capability absent from the event stream exists on no screen.
`Orkeon.Studio.Core` gains a `Forge/` client (tolerant parser pinned against the CLI's
golden lines, session projection, catalogue, resume hydrator) without a single new
dependency. `RunSession`/`RunLaunchRequest`/`TargetSelectionModel`/`LaunchOptionsModel` were
promoted into `Orkeon.Studio.Core.Launch` along the way, so the launch lifecycle now exists
once instead of twice.

Docs: [Forge a team from a need](docs/getting-started/forge-a-team-from-a-need.md) (EN/FR),
the `orkeon forge` section of the CLI reference, and `examples/forge/promote-demo/` — a
bundled ready session whose `list`/`promote` half runs with no LLM at all.

### Added — Watch a run, talk to it, host it: the event bus, the hub pipeline, the service host (BUS, HUB, GATE)

Three chantiers that share one goal: a crew you can see, answer and reach from outside the
process it runs in.

**`orkeon run --events jsonl`** speaks a versioned protocol instead of printing for a person —
one JSON document per line out, one per line back. Task completions in **all six orchestration
modes** — terminal events included: a cancelled or failed run still says so, in every mode —
cost with model and provider (no invented price: the framework has no price table), generation
deltas under `--stream`, tool calls, delegations, and runtime agent spawns when a `spawn_agent`
tool is attached. Tool events come from a single decorator applied where tools enter the
process — the DI registrations, and through the `IToolDecorator` port the per-agent delegation
pair — so the agent loops and the scripting facade are covered at once, `.ork.ts` targets
included. Contract: [The run event bus](docs/architecture/run-event-bus.md),
plus a ~90-line dependency-free reader in `examples/run-events/` with a recorded stream to try
offline.

- **Silence is not consent.** Without the protocol, a task declared `humanInput: true` was
  auto-approved behind the user's back. Asking for the stream replaces that provider: the
  question goes out and the run waits. No answer — closed channel, cancelled run — is a
  **refusal**, never an approval.
- **Studio's Launch screen stopped being a terminal.** It shows finished tasks, cost and the
  run's question on screen; the raw log is demoted, not removed. A silent run says "nothing
  reported yet" rather than implying progress, and a question with no correlation id is not
  shown as pending, because answering needs an address.

**The EventHub's middleware pipeline** ships complete: logging, telemetry, ACL, idempotency and
validation, on both the publish and the receive path. The `links:` YAML grammar lets a crew
declare who it may talk to, and `client://{name}` lets an external process be named — without
it, a watching peer would escape the ACL by simply not being modelled. Idempotency guards
point-to-point delivery only: deduplicating a topic message by identifier would starve every
subscriber but the first. Its memory does not survive the process, which is stated rather than
implied.

**`orkeon-host`** hosts crews as a daemon — systemd unit, Windows service or container, same
binary, and runnable in a terminal because a daemon you cannot run in the foreground is one you
cannot debug. One dependency-injection scope per run, which is what keeps one conversation's
memory from reaching another's. A Discord channel gives the first place people can reach a crew
from: allow-list authorization checked **before** routing, thread-is-run mapping, an immediate
acknowledgement carrying a Stop button, throttled progress, `/status` and `/stop`. An empty
allow list denies everyone and refuses to start.

It hosts crews; it does not schedule them. rc.2 ships no scheduler, and neither the docs nor the
systemd unit implies otherwise. Docs:
[The service host and the chat gateway](docs/architecture/service-host.md) (EN/FR),
`examples/service-host/`, and `deploy/` for the unit, the SCM script and the Dockerfile.

### Changed — EventHub API surface, and the crew configuration

`CrewLink` and `CrewLinkDirection` live in `Orkeon.Domain.EventHub`: a link is what a crew
*declares*, not a transport detail — and it names its target by the crew's **`name:`**, the only
identity a YAML author has (`ICrewLinkRegistry.Register` records the id ↔ name mapping for every
crew, links or not). `CrewConfiguration` gained a nullable `Links` (never-declared and
declared-empty are different answers to the ACL). `MailboxAddress` gained the `client://`
scheme. `Message.NoDeclaredSchemaId` is `"_none"` — a sentinel that cannot collide with a real
schema id. `CrewOutput` gained `Succeeded`, because `KickoffAsync` never throws and a host has
to tell an answer from an apology. `IMemoryService` gained `ReleaseMemorySystem`, the
daemon-side counterpart of loading a crew per message. The forge stream shares the run envelope
and speaks protocol **v2** (was v1); its reserved-name set grew from four to eight. Several
constructors gained an optional trailing parameter — the orchestration strategies
(`ICrewExecutionHook`), `InMemoryEventHub` (middlewares), `CrewFactory` (the link registry),
`AgentDelegationToolsProvider` (the tool decorator) and `RunnerHost.Build` (a builder hook).
Source-compatible; recompile.

### Fixed — Two harness defects that were reporting success

`TraceExplorerService` listened to every process-wide `Orkeon` activity source, so a test
asserting "no traces" was really asserting that no other test emitted one — true by luck, and
less true as the suite grew. Trace capture is now scopeable via `MonitoringOptions.TraceSourcePrefix`.

The E2E CLI tests rebuilt the CLI on every invocation, and two classes doing that concurrently
made MSBuild write its diagnostics onto the stream under assertion. The CLI is now built once
per assembly and run with `--no-build`.

## [1.0.0-rc.1] - 2026-08-18

Orkeon's first release candidate — the version that goes to NuGet.org. Everything
accumulated since `0.9.2-beta` ships here: the RAG subsystem with its five profiles and
the corrective CRAG graph, declared LLM capabilities across all **13 providers**
(Google Gemini joining as the 13th), the dual-era MCP client and server, A2A task
persistence, Orkeon Studio with full localization, the Windows/Debian/macOS install
channels, the TypeScript CLI command layer, the complete API reference site — and a
mechanically frozen public API surface (29 235 declared APIs, `[Experimental]` markers
on the four unstable areas) with the versioning policy to match. Breaking changes below
are called out in their own entries (RAG namespace extraction, API-shape conformance,
`Orkeon.Cli.Commands.Scripting` rename).

### Fixed — `orkeon llm probe|models` now know Gemini (PUB-15 leftover)

`LlmProviderFactory` accepted `gemini`/`google` since PUB-15, but the CLI's
`LlmCatalogClient` had no default base URL for it and did not treat it as
OpenAI-catalog-compatible — `orkeon llm models -p gemini` failed without an
explicit `-u`, and the probe help text stopped at 12 providers. Both wired;
surfaced by the DOC-02 review of the new CLI reference page.

### Changed — Documentation trued up against the code, end to end (DOC-02)

A three-pass audit (docs/ tree, root/examples/OSS surface, facts vs code)
followed by full remediation. Highlights: the quality-gate page no longer
claims a blocking Sonar CI gate that does not exist; the fictional
`autonomousBudget` YAML block is marked not-implemented; two lifted
limitations rewritten (uniform streaming, runtime plugins); every count
trued (79 tool classes, 44 domain events, 13 providers, 33+33 projects);
17 dead maintainer-side references and 4 failing copy-paste commands fixed;
~350 French fragments in EN docs translated; `docs/arkeon/` retired;
`CLAUDE.md` no longer rendered on the docs site; `docs/fr/toc.yml` created
(51 FR pages were orphans) and the ADR register joined the site nav.
New pages (EN + FR): `reference/cli.md`, `reference/configuration.md`,
`architecture/mcp.md`, `architecture/studio.md`. New guards in CI:
`scripts/check-doc-claims.py` (counts checked against the code), a
category-README completeness check, and an informational EN/FR drift
report. New community files: SUPPORT (EN/FR), NOTICE, CODEOWNERS,
dependabot, CodeQL, a documentation issue form.

### Fixed — ExecutionPlanParser no longer throws on non-string JSON values

`ExecutionPlanParser` parses untrusted LLM planning output, yet `"task": 42`,
`"instructions": 42`, `"agent": 42` or a number/null inside `"dependencies"`
escaped the `JsonException` net as an `InvalidOperationException` from
`JsonElement.GetString()` and took the whole planning pass down. Every
string-position read is now guarded by a `ValueKind` check: malformed entries
degrade the same way unknown ids always have (entry skipped, dependency
dropped, agent unassigned) instead of throwing. Surfaced by the SONAR-14
coverage pass on the parser.

### Added — Studio i18n: the whole below-the-view layer follows the language switch (STUDIO-11 tranche 2)

Completes the sweep opened by PUB-19: the `IStudioStrings` registry grows from 4
to 89 keys covering every string Core and the WPF ViewModels fabricate — the
`orkeon init` preset catalogue (labels, guidance, plan errors, via
`LlmPresets.CatalogFor`), the settings resolution chain
(`SettingsLocations.ResolutionChainFor`), the mount-rights labels
(`MountRightsTokens.ChoicesFor`/`GetLabel(rights, strings)`), the directory-run
notice, the `--mount` override semantics, and all ViewModel statuses, summaries,
dialog titles and filters. Every long-lived ViewModel takes the port (optional,
English default — TUIs unchanged) and re-emits its bindings on `CultureChanged`;
transient rows (mounts, overrides, effective-mount table) are refreshed by their
owners. 89 keys mirrored EN/FR in the WPF resx; a new drift test pins
resx-EN ≡ `EnglishStudioStrings` so the two English surfaces cannot diverge.
Deliberately untranslated (CLI-contract policy, like `VALIDATION OK/FAILED`):
`orkeon doctor` check names/details, validator message bodies, target-detector
remediations, LLM probe results, and exit-code descriptions.

### Added — Studio localization port: strings below the view layer follow the language switch (PUB-19 tranche 1)

Foundation for STUDIO-11 ("switching to French leaves some strings in English"):
`Orkeon.Studio.Core` gains a localization port (`IStudioStrings`, key registry,
English defaults) consumed by the shared formatters — `ValidationMessageFormatter`
and `LaunchOutcomeFormatter` first — through additive overloads (the TUIs keep the
English default, no regression). The WPF front bridges the port onto its
resx-backed `I18n` (`I18nStudioStrings`, hot language switch relayed via
`CultureChanged`), with the new keys mirrored EN/FR under the existing resx-parity
test. CLI verdict words (`VALIDATION OK`/`FAILED`) deliberately stay untranslated.
The ViewModel sweep continues on this pattern (tracked in STUDIO-11).

### Added — Test pyramid rebalanced: offline E2E per orchestration mode, nightly integration, no swallowed SIGSEGV (PUB-17)

- **E2E grows from 17 to 43 executed facts, all offline**: a new suite drives the
  full DI stack (Application + Infrastructure) through
  `ICrewOrchestrationService.KickoffAsync` for **each of the six orchestration
  modes** (sequential, hierarchical, parallel, consensual, graph, autonomous) with
  a scripted LLM — per mode: the kickoff completes, produces exactly one output per
  declared task, and demonstrably drives the LLM (no silent no-op path). Two
  `Category=Slow` facts spawn the real `orkeon` CLI from source and `--validate` a
  single-YAML example and a multi-file crew directory end-to-end.
- **`integration.yml`**: the Integration/Slow suites (Testcontainers databases) now
  run nightly (02:17 UTC, also dispatchable). A red run opens or comments a
  tracking issue — failures are visible, not buried in a log.
- **The blanket `continue-on-error` on Embeddings.Local is gone** (ci.yml and
  publish.yml): the step now inspects the results — a genuine test failure fails
  the build; only the known ONNX teardown crash (exit 139 **after** a clean
  "Passed!" summary) is tolerated, explicitly and with a warning annotation.

### Changed — Global zero-warning ratchet: full analyzer set on, CI builds -warnaserror (PUB-18)

The warning-debt story reaches its terminal state. The audit found the "frozen
~2 283 warnings" note in the build props was stale — the 2026-06-15 zero-warning
campaign had already resorbed the debt; a fresh full-analysis inventory
(`AnalysisMode=All`, `AnalysisLevel=latest-all`) surfaced only **four stragglers**
solution-wide, all fixed (unused TUI palette field, `DefaultDllImportSearchPaths`
on the libc `kill` P/Invoke, two per-call `JsonSerializerOptions` allocations).
The complete analyzer rule set is now **enabled permanently** in the root build
props, and CI compiles with **`-warnaserror`** — any new compiler, analyzer, or
NuGet-audit warning fails the build (audit advisories breaking CI is deliberate;
see the SSH.NET precedent). The `.editorconfig` ledger remains the record of the
deliberate per-scope arbitrations.

### Changed — DI default stand-ins fully documented; the stub planner now warns (PUB-23)

The deliberately-minimal DI defaults follow the house rule — never a silent drop —
and the last gap is closed: `AgentPlannerService` (the fixed 4-step stub planner,
the most misleading stand-in since it emits a plausible "plan" every run) now
announces itself with a **one-time Warning naming the replacement gesture**, like
the Infrastructure stubs already did (previously Debug-only, invisible under
default logging). New page `docs/getting-started/default-behaviors.md` (EN + FR)
inventories every default — the six that warn, the six that are silent by design
and why — with the exact replacement snippet for each; linked from limitations and
the Autonomous orchestration guide. No functional behavior change.

### Added — Google Gemini provider: 13th LLM provider (PUB-15)

`GeminiLlmProvider` joins the family through Google's OpenAI-compatible endpoint
(`generativelanguage.googleapis.com/v1beta/openai`, Bearer auth with the Gemini API
key). Capabilities verified against the compatibility documentation (2026-08-18):
effort-only thinking (`reasoning_effort` mapping to Gemini's `thinking_level`),
vision via `image_url` data URIs; `response_format` is undocumented on the compat
surface and therefore stays **undeclared** — a JSON-format request triggers the
structured capability warning instead of a silent drop. Factory auto-detection by
host (`generativelanguage.googleapis.com`) and model prefix (`gemini-*`); default
model `gemini-3.7-flash`; `appsettings.gemini.local.json.example` template added;
provider docs, comparison matrices and counts updated EN/FR. Vertex AI and AWS
Bedrock remain out of scope (OAuth/SigV4 SDK stacks conflict with the simple-HTTP
provider principle — recorded in the PUB-15 fiche).

### Added — A2A task persistence lifts the 501; conformance matrix published (PUB-08)

- **`GET /a2a/tasks/{id}` is real now** — opt-in: register a checkpointing state store
  (`AddOrkeonCheckpointing` / SQLite / Postgres) plus the new
  `AddOrkeonA2ATaskPersistence()`, and the A2A server records every task lifecycle
  transition (`IA2ATaskStore` port over the existing `IStateStore`, best-effort — a
  store outage never fails the task exchange). The endpoint answers `200` with the
  recorded state and `404` for unknown ids; `DELETE /a2a/tasks/{id}` now records the
  cancellation and answers `404` for unknown ids instead of fabricating an
  acknowledgement (cancellation stays advisory). Without the opt-in, the explicit
  `501` remains — never invented state.
- **A2A conformance matrix** (`docs/reference/a2a-conformance.md`, EN + FR): the
  honest inventory of the 0.x-era surface against the A2A v1.0 specification —
  operations, data model (5 vs 9 task states), bindings (own REST dialect; none of
  the three canonical bindings), security. Includes the PUB-08 certificate-revocation
  decision: short-lived certificates over CRL/OCSP for the private-CA mTLS model.
- 6 new persistence tests (adapter round-trip + end-to-end HTTP 200/404/501/cancel).

### Changed — MCP unpinned from 2024-11-05: dual-era client and server (PUB-07)

The MCP integration no longer hardcodes the first protocol revision. Both sides now
speak the **modern stateless lineage (`2026-07-28`)** and fall back to the legacy
initialize-handshake revisions, per the specification's backward-compatibility rules:

- **Client** (`McpClient.ConnectAsync`): probes with `server/discover`; a modern answer
  selects the newest mutually supported revision (renegotiating on
  `UnsupportedProtocolVersionError`, including mid-flight), anything else falls back to
  the legacy `initialize` handshake — which now really negotiates (`2025-11-25`,
  `2025-06-18`, `2024-11-05`) instead of pinning `2024-11-05`, and finally sends the
  required `notifications/initialized` (it never did). Modern requests carry
  per-request `_meta` (protocol version, client info/capabilities); `input_required`
  interim results (MRTR) are surfaced as explicit tool errors rather than partial data.
- **Server** (`McpServer`): dual-era on the same endpoint — implements the mandatory
  `server/discover`, validates the per-request declared version (`-32022`
  `UnsupportedProtocolVersionError` with the supported list), answers legacy
  `initialize` with real version negotiation, no longer replies to notifications,
  returns `tools/list` in deterministic order with the required
  `resultType`/`ttlMs`/`cacheScope` fields plus server identity in result `_meta`.
- **Wire**: JSON-RPC ids are no longer int-only (string ids from external clients now
  round-trip; stdio correlation is id-agnostic); the HTTP transport sends the
  Streamable HTTP headers (`MCP-Protocol-Version`, `Mcp-Method`, `Mcp-Name`) and
  unwraps SSE-framed response bodies.
- 13 new dual-era tests (104 MCP tests total). Remaining gaps are documented in
  `docs/reference/limitations.md`: no `subscriptions/listen`, no MRTR, no MCP OAuth,
  JSON-response mode only on HTTP, and `2025-03-26` excluded (mandatory batching).

### Added — Full API reference site and community templates (PUB-10, PUB-12)

- **docfx now covers every published library** (25 assemblies — 2 916 generated API
  pages, nested-namespace navigation) instead of core + Tools.Abstractions only.
  Metadata is generated from the compiled Release assemblies, so source-generated
  members are included. The never-finished navigation is real now (`toc.yml`,
  `docs/toc.yml`, `api/index.md`), and the new `docs.yml` workflow builds the site
  strictly (`--warningsAsErrors`) on every PR touching sources or docs, and deploys
  it to GitHub Pages on each `v*` tag.
- The strict build flushed out **31 broken documentation links** — including two
  references to the maintainers' private repository and four to a spec folder that
  no longer exists in the public tree — all fixed; ADR index pages (EN/FR) added.
- **Community templates**: structured issue forms (bug report with install-channel
  and reproduction fields, feature request), contact links routing questions to
  Discussions and security reports to SECURITY.md, and a pull-request template whose
  checklist mirrors what CI actually enforces (API freeze declaration, FR parity
  gate, CHANGELOG entry, executable bits, VFS-only I/O).
- 25 public entry-point types that had no XML `<summary>` (RaggableTree contracts,
  scripting JS builders, `BuilderValidationException`…) are now documented.

### Changed — Documentation debt cleared; FR/EN parity is now a CI gate (PUB-09)

- The examples catalog (`docs/reference/examples-catalog.md` + FR) is now editorial
  only: the generated, CI-checked `examples/INDEX.md` is the authoritative inventory,
  so the page no longer maintains counts or paths by hand (the old page announced
  104 examples with a table summing to 101 and folder casings that did not exist).
  It now also covers the RAG, RaggableTree, scripting, CLI-commands, local-embeddings
  and multi-file showcases.
- The last four missing French mirrors are delivered (`run-your-first-example`,
  `example-data-policy`, `hosting`, the example README template): `docs/` parity is
  green for the first time, and `scripts/check-docs-parity.sh` now runs in `ci.yml`
  on every push and PR — a missing mirror fails the build. CONTRIBUTING (EN/FR)
  states the CI-gate wording again.

### Security — Testcontainers 4.13.0 → 4.14.0 (test infrastructure only)

Clears the last build warning, NU1903: Testcontainers 4.13.0 pulled SSH.NET 2025.1.0
transitively, which carries a known high-severity advisory
(GHSA-q939-rpr3-3284); 4.14.0 depends on the fixed SSH.NET 2026.0.0. Test-only
dependency — nothing shipped in the NuGet packages or installers was affected.

### Added — The public API surface is frozen and enforced (PUB-05)

Ahead of the first public release, the API contract is now mechanical, not aspirational:

- **PublicAPI baselines** — every packable library carries `PublicAPI.Shipped.txt`
  (29 235 declared public APIs across 28 projects, source-generated members included)
  and an empty `PublicAPI.Unshipped.txt`, checked by
  `Microsoft.CodeAnalysis.PublicApiAnalyzers`. An undeclared public API addition or
  removal is a **build error** (`RS0016`/`RS0017` promoted via `WarningsAsErrors`).
  Executables (`src/apps/`) opt out — an app's surface is not a contract.
- **`[Experimental]` on the unstable surfaces** — 71 types now carry
  `ExperimentalAttribute` with stable diagnostic IDs, documented in
  `docs/reference/experimental-apis.md` (EN + FR): `ORKEXP001` A2A (pre-v1.0.1
  implementation), `ORKEXP002` Autonomous orchestration (budget, A2A channel, spawn),
  `ORKEXP003` corrective RAG (CRAG contracts), `ORKEXP004` MCP (pinned to `2024-11-05`
  until the protocol upgrade). Referencing them from outside the repository is a
  compile error to suppress explicitly; `src/`, `tests/` and `examples/` suppress the
  four IDs centrally because the framework wires its own experimental surfaces.
- **Versioning policy** — CONTRIBUTING (EN + FR) now states the contract: SemVer, a
  breaking change is any edit to `PublicAPI.Shipped.txt`, `[Obsolete]` ships at least
  one minor version before any removal, and no breaking change to a stable shipped API
  within the 1.x window.
- The 47 `InternalsVisibleTo` declarations were inventoried: 36 target test projects;
  the 11 production-to-production grants all belong to shared-kernel pairs already
  documented by ADR-002/003/006 (abstractions → implementation, Domain → Application/
  Infrastructure, Cli.Abstractions → Cli) plus two grants to the `orkeon` tool
  executable — kept, documented in the PUB-05 closure note.

### Changed — Complete NuGet package metadata (PUB-04)

Every one of the 28 packable projects now ships presentation-grade metadata:

- **Package icon per family** — `assets/nuget/` holds one 128 px icon per `src/` zone
  (core, cli, scripting, analyzers, tools, rag, analysis, generators, hosting, plugins);
  a per-zone `Directory.Build.props` declares the family and the central props pack it
  as `icon.png`, so every `.nupkg` carries its zone's icon with a single `<PackageIcon>`.
- **A dedicated README per package** — each project directory now has a short `README.md`
  (role, install, doc links) packed via `PackageReadmeFile`; the five projects that
  already had a rich developer README ship that one. The shared generic `nuget/README.md`
  that seven packages used to duplicate is removed.
- **`PackageReleaseNotes`** — centralized in `src/Directory.Build.props`, pointing at
  this CHANGELOG.
- **`LICENSE.md`** — copyright aligned with the build props (`2024-2026`).

### Changed — NuGet.org publication wired for real (PUB-03)

`publish.yml` now pushes the three core packages (`Orkeon.Domain`, `Orkeon.Application`,
`Orkeon.Infrastructure` — the v1 set of `docs/reference/publication-matrix.md`) to
**NuGet.org** on a `v*` tag. Authentication is **Trusted Publishing (OIDC)**: a nuget.org
policy for `Orkeon/orkeon` + `publish.yml` lets `NuGet/login` exchange the job's OIDC token
for a short-lived key — no long-lived API secret exists anywhere. The steps are gated on the
`NUGET_USER` repository variable: until the owner finishes the nuget.org setup, they warn
and no-op instead of failing the release. The workflow
also **refuses a tag that does not match the `src/Directory.Build.props` version** — the
guard that makes the 0.9.1-beta silent-skip incident (rc tags re-packing an unchanged
version, `--skip-duplicate` skipping every push) structurally impossible. The README NuGet
badge, which pointed at a package that does not exist on nuget.org yet, is replaced by a
GitHub release badge until the first real push restores it.

### Changed — `Orkeon.Cli.Scripting` renamed to `Orkeon.Cli.Commands.Scripting` (ADR-007, decision D3)

The library of TypeScript-scripted interactive CLI commands loses its near-anagram name
(`Orkeon.Cli.Scripting` vs `Orkeon.Scripting.Cli`): project, PackageId, assembly, root
namespace and test project are now `Orkeon.Cli.Commands.Scripting(.Tests)`. The `orkeon`
dotnet tool (`Orkeon.Scripting.Cli`) keeps its name — its PackageId is the install command.
No published package carried the old name, so nothing breaks outside this repository;
in-repo consumers were updated in the same change. This supersedes ADR-004 and lifts the D3
gate in `docs/reference/publication-matrix.md`. Entries below in this Unreleased block use
the new name even where the work predates the rename.

### Added — Orkeon Studio: a graphical way in, on Windows and Linux

Configuring Orkeon and launching a crew no longer requires a terminal. **Orkeon Studio**
ships as three applications over one shared core (`Orkeon.Studio.Core`, which holds the
appsettings model, the target detection and the `orkeon run` argument building — a feature
absent from the core exists in no UI):

- **`orkeon-studio`** — a WPF desktop app for Windows, two tabs (settings editor, crew
  launcher);
- **`orkeon-studio-config`** — a full-screen Terminal.Gui editor for the settings file:
  provider presets, model and endpoint, and the VFS mount table, saved in the exact form
  `FileSystemMount.Parse` reads back;
- **`orkeon-studio-run`** — the crew launcher in the terminal: pick a `config.yaml`, a crew
  directory or a `.ork.ts` script, set the options (`--validate` included), follow the output
  live, and cancel a run — the process is terminated and the exit code (130 on cancellation)
  is reported, with the UI still alive.

None of them is a second product: they edit the same `appsettings.json` `orkeon init`
writes, and they launch crews by executing the co-installed `orkeon` binary, so the CLI and
Studio are interchangeable on the same machine at any point.

**Distribution follows the platform, not the wish list.** The `win-x64` zip and the MSI carry
`orkeon-studio` (the MSI adds an "Orkeon Studio" **Start-menu shortcut**, its only
MSI-specific authoring); the Debian package and the Linux archives carry
`orkeon-studio-config` and `orkeon-studio-run` (`/usr/bin/orkeon-studio-{config,run}` on the
`.deb`, launcher symlinks in `<prefix>/bin` from a tarball); the macOS **onboarding** channel
— the `orkeon-cli-*-osx-*` tarballs and Homebrew — stays **CLI-only in V1** (the multi-app
`orkeon-*-osx-*` archives carry the two TUIs like every other RID, untested there).
Linux archives and the `.deb` hardlink-deduplicate their payload at staging
time (`scripts/hardlink-dedup.sh` — tar and dpkg both preserve hard links, and
gzip alone cannot deduplicate across files): the byte-identical .NET runtime
and shared Orkeon assemblies of the self-contained apps are stored once
instead of once per app, cutting the cli linux tarball from 176 MB to 104 MB
with strictly identical extracted content.
Every Studio app is published self-contained like the CLI itself, which keeps the `.deb`'s
`Depends` free of any `dotnet-runtime-*` — the onboarding channel's invariant. The app table
in `package-installers.sh` / `.ps1` gained a RID-filter column for this (WPF cannot target
non-Windows RIDs), and `SHA256SUMS` is unchanged in shape: same archive names, richer
contents.

The release smokes assert all of that on real runners rather than at packaging time:
`orkeon-studio --smoke-exit` opens the WPF window, lets it render and exits 0 on
`windows-latest` (both channels — zip and MSI — through one shared assertion file); the
`.deb` and linux tarball smokes require both TUI launchers and run `--version` on each with
**no terminal at all** (stdin from `/dev/null`, both streams redirected), which is the
contract that keeps them scriptable; and the macOS smoke asserts the **absence** of anything
named `orkeon-studio*`, so the day the RID filter regresses, CI fails instead of a Mac user.

### Added — `orkeon run <directory>`: multi-file crews are a first-class target

A crew no longer has to be a single file. `orkeon run` (and every runner's `-c/--config`)
now accepts a **directory**: `config.yaml` for the crew settings, one agent per file under
`agents/`, one task per file under `tasks/`, each file-name stem being the entity id — the
layout `YamlCrewDefinitionLoader.LoadFromDirectoryAsync` already understood, which until now
no CLI could reach because the dispatch was by file extension only. The legacy flat triplet
(`crew.yaml` + `agents.yaml` + `tasks.yaml`) is accepted from a directory too, and every
option behaves identically on a directory and on a file (`--settings`, `-V/--var`,
`--initial-context`, `--mount`, `--validate`, `--verbose`, `--llm-log`). A `.yaml` path
passed directly follows exactly the path it always did.

The classification is explicit rather than convenient: a directory holding both a YAML
layout and a scripting entry point — any `*.ork.ts` or `*.ork.js` sitting directly in it,
whatever the file is called — is refused with both candidates named, and a directory with no
recognized layout is refused with the list of what was searched — no silent precedence, and
a lone script is never executed just because it was the only thing in the folder. The
directory is mounted read-only in the VFS as itself, not as its parent, so a crew directory
opens no wider a surface than a crew file. `examples/crew-multifile/` is the runnable
reference (`orkeon run examples/crew-multifile --validate`).

### Added — macOS channel: osx CLI tarballs, Gatekeeper handling, Homebrew formula

macOS joins Windows and Debian as a first-class install target. The release now carries
`orkeon-cli-<version>-osx-arm64.tar.gz` and `-osx-x64.tar.gz` — the `orkeon` CLI alone,
self-contained and tree-sitter-pruned like every other CLI package, cross-published from the
Linux runner (the apphosts ship in the SDK packs, the natives come from NuGet, and esbuild is
fetched per-RID as `@esbuild/darwin-{arm64,x64}`). No .NET install is required on the Mac.

`install.sh` gained a Darwin-only block that removes the two ways an unsigned binary fails on
macOS. It clears `com.apple.quarantine` from the installed tree — a browser download tags
every extracted file with it, which is what produces *"cannot be opened because the developer
cannot be verified"*, and running the installer is the user's own act of trust. It then runs
`codesign -v` over the bundled Mach-O files and ad-hoc re-signs **only** those that fail,
because Apple Silicon refuses to load an unsigned Mach-O while a valid publisher signature
(onnxruntime's, for instance) must never be replaced by an ad-hoc one. Both halves degrade
quietly when `xattr` or `codesign` is unavailable, no individual failure aborts the install,
and the block is skipped outright off Darwin — Linux behaviour is unchanged. A new blocking
`smoke-macos` job (`macos-latest`, Apple silicon) installs the `osx-arm64` tarball on a real
Mac and walks init → doctor → run → rag → uninstall on it, which is what actually proves the
signing story: a native library killed at load time fails there instead of in a user's
terminal.

A Homebrew formula ships in the repository at `installers/homebrew/orkeon.rb`: a binary
formula that fetches the tarball for the machine's architecture (`on_arm` / `on_intel`),
installs the payload under the Cellar's `libexec`, and writes a `bin/orkeon` wrapper pointing
`ORKEON_ESBUILD_PATH` at the bundled esbuild. Its `test do` runs `orkeon doctor`, not
`orkeon --version` (which exits `1`). `scripts/update-homebrew-formula.sh` regenerates the
version and both url/sha256 pairs from a release's `SHA256SUMS` — idempotent, and it refuses
to write when the formula's structure no longer matches what it knows how to rewrite. The
`Orkeon/homebrew-tap` repository is **not published yet**, so `brew install orkeon` does not
resolve; until it is, the two `sha256` values are explicit placeholders that fail verification
rather than fetch anything unverified.

### Added — `orkeon init` and `orkeon doctor`

Two new verbs make the first ten minutes on a fresh machine self-service. `orkeon init` is
a wizard over five providers — `ollama`, `docker-model-runner`, `openai`, `custom`, `none` —
that writes an `appsettings.json` at the global per-user path (below), then probes the endpoint
to confirm it answers. Every prompt has a flag, so it scripts end to end:
`--provider`, `--base-url`, `--model`, `--api-key-env` (the recommended way to carry a key),
`--api-key` (inline, discouraged), `--path`, `--force`, `--no-probe`. With a non-interactive
stdin and no `--provider`, it refuses rather than hanging. `orkeon doctor` runs nine checks —
`dotnet-runtime`, `appsettings`, `llm-config`, `llm-reachability`, `esbuild`,
`local-embeddings`, `onnx-reranker`, `tree-sitter`, `workspace-write` — prints them as a
✅/⚠️/❌ table, exits `1` as soon as one fails (warnings stay green), and emits a
`[{check, status, detail}]` array under `--json` for CI.

### Added — global per-user configuration path in the settings resolution

Settings resolution gains a fourth step: after the local `appsettings.json` and the walk up
the parent directories, and before the env-vars-only fallback, the CLI now reads
`%APPDATA%\Orkeon\appsettings.json` on Windows and `~/.config/Orkeon/appsettings.json` on
Linux/macOS — the file `orkeon init` writes. An installed `orkeon` therefore works from any
working directory, and configuration never lives in the install directory, which every
(re)install deletes outright.

### Changed — an unconfigured LLM warns instead of silently echoing

Building a runner host with no `Llm` section still falls back to the echo provider, but it now
says so once on stderr — *"No `Llm` section configured — falling back to the echo provider
(`<undefined-llm>`). Run `orkeon init` to create a configuration, or set
`ORKEON_Llm__BaseUrl` / `ORKEON_Llm__Model`."* The fallback itself is unchanged (it is what
makes the scripting demos runnable with no key and no server); what changes is that a crew
replaying its own prompts can no longer be mistaken for a crew talking to a model.

### Changed — every publish prunes the unused tree-sitter grammars

**Behaviour change.** `TreeSitter.DotNet` ships one native library per supported grammar (31,
~69 MB on win-x64) while Orkeon only loads the seven declared in `LanguageRegistry`. A
`PruneUnusedTreeSitterGrammars` target in `src/Directory.Build.targets` — imported wholesale by
`examples/Directory.Build.targets` — now drops the rest from `ResolvedFileToPublish`, so **every
`dotnet publish` under `src/` and `examples/` emits 7 grammars instead of 31**, not just the
release archives. `dotnet build` is untouched. Opt out with
`-p:OrkeonPruneTreeSitterGrammars=false` (useful when diagnosing a grammar-loading problem).
Safe by construction — `LanguageRegistry.Create` throws for any language outside the registry —
and the whitelist ↔ registry agreement is pinned by `TreeSitterGrammarPruningTests`.

### Added — Windows and Debian install channels; hardened `install.ps1`; runtime detection in `install.sh`

Three new release artifacts sit next to the existing multi-app archives, all self-contained:
`orkeon-cli-<version>-win-x64.zip` (the `orkeon` CLI alone, with `install.ps1`),
`orkeon_<version>_amd64.deb` (installable with `sudo apt install ./orkeon_*.deb`; depends on
system libraries only, through libicu/libssl alternations covering Debian 12/13 and Ubuntu
22.04→26.04, never on `dotnet-runtime-*`), and `orkeon-<version>-win-x64.msi` (WiX, per-user,
no administrator rights — one Windows channel at a time, the MSI refuses to install over a zip
install). `release.yml` is restructured into `installers → {smoke-windows, smoke-deb, msi} →
release`, so nothing reaches the Release until it has been installed and exercised on a real
Windows runner and a stock Ubuntu image — the `msi` job carries its own
`msiexec /i /qn` → `orkeon doctor --json` → `msiexec /x /qn` smoke — and it now also runs on
`workflow_dispatch` (everything except the publication).

`install.ps1` gained an "Apps & features" entry (with a working `UninstallString`), rescues an
`appsettings.json` left in a previous install directory into `%APPDATA%\Orkeon` before the
delete-and-replace, preserves the `RegistryValueKind` of the user `PATH` instead of flattening
`REG_EXPAND_SZ` to `REG_SZ`, and only checks for a .NET runtime when the payload actually needs
one (keyed on `hostfxr.dll`). `install.sh` gained the POSIX mirror of that check: it looks for
a framework-dependent app under `libexec/` (no `libhostfxr.so`/`.dylib`), then for a
`Microsoft.NETCore.App 10.x` runtime on the `PATH` or under `DOTNET_ROOT`, and when it finds
none prints the exact commands per distribution — `sudo apt install dotnet-runtime-10.0` on
Ubuntu 25.10+, the `packages.microsoft.com` repository registration on Debian and Ubuntu LTS,
`dotnet-install.sh --runtime dotnet --channel 10.0` under `$HOME` without sudo — and repeats
the reminder at the end. It never installs a runtime, adds a repository or calls sudo on the
user's behalf, and the warning never blocks the install.

### Fixed — `package-installers.sh` aborted at the checksum step with a single `--rids`

The final `ls *.tar.gz *.zip *.deb | xargs sha256sum` left one glob unmatched whenever the run
targeted a single RID; under `set -o pipefail` the failing `ls` took the whole pipeline down
and `set -e` aborted the script — after every archive had already been built. The `ls` is now
wrapped in `{ …; || true; }`, and `*.deb` is part of the glob so `SHA256SUMS` stays complete
when `package-deb.sh` has dropped its package in the same output directory.

### Added — shell_command: bidirectional VFS path rewriting

`shell_command` now speaks virtual paths in both directions, so a coding agent can run
`dotnet build /workspace/App.sln` instead of failing on a path that only exists in the
VFS. Inbound, every argument that names a mount-prefixed virtual path — including the
embedded `--out=/workspace/dist` form (split at the first `=`) — is resolved
virtual→physical through `IFileSystemService.ResolveAndValidate` before the process
starts, with a path-boundary check (`/workspaces` never matches mount `/workspace`); a
denied path fails the call with the redacted denial reason instead of reaching the
process verbatim. Outbound, stdout/stderr are rewritten physical→virtual before
truncation (success and timeout paths alike) using a per-call table built by resolving
each mount root — longest physical base first, backslashes normalized to `/` inside the
rewritten path token — so the model only ever sees virtual paths and stops leaking
physical host paths into its own follow-up `file_read` calls. Scope: `AgentFacing`
mounts, ordinal matching (re-cased Windows output is a documented limitation); with no
mounts configured both passes are exact no-ops.

### Added — shell_command: configurable allowlist

Two new config keys shape the executable allowlist without code changes:
`Orkeon:Tools:Shell:ExtraAllowedCommands` (string array) is ADDITIVE on top of the
default allowlist — the recommended way to allow `make`/`cargo`/etc. for a trusted
coding-agent host; it composes with `AllowInterpreters` (new `extraAllowedCommands`
ctor parameter, unioned after the base list is built). `Orkeon:Tools:Shell:AllowedCommands`
(string array) is a full verbatim REPLACEMENT mapping to the existing `allowedCommands`
ctor parameter — per that contract it cancels `AllowInterpreters` and re-enables the git
read-only subcommand restriction. An absent or empty section binds to `null`, never to
an empty array (which would block every command), so defaults are unreachable by
accident.

### Added — configurable LLM retry budget (default 10) + visible reconnection feedback

`Llm:MaxRetries` (the dormant `LlmConfig.MaxRetries`, never consumed until now) drives
BOTH HTTP paths: the buffered Polly policy (`GetLlmApiPolicy`, previously hardcoded at
5) and the streaming connect-phase loop (previously hardcoded at 3 attempts). Default
raised from 3/5 to **10** (`LlmDefaults.DefaultMaxRetries`) with every wait capped at
30 s (`ResiliencePolicies.LlmRetryDelay` — linear ×1/×2, then ×3 exponential, capped),
so the ladder degrades to a bounded cadence instead of 3⁸ seconds. Both config-binding
hosts read the key (`ConfiguredLlmProviderBootstrapper` for the ConsoleApp REPL,
`RunnerHost` for runner hosts), clamped at 0; the cap is applied before the `TimeSpan`
conversion (an arbitrarily large configured budget never overflows mid-retry) and a
server `Retry-After` is now capped at the same 30 s on the buffered path, matching the
streaming path.

What makes a 10-retry budget acceptable on an interactive turn is that it is now
VISIBLE: a new `ILlmRetryObserver` port (Application) receives every scheduled retry
wait and the final settle; `HttpLlmProviderBase.RetryObserver` fires it from both
paths (never on mid-stream failures, which are still not retried). The CLI implements
it with `LlmRetryProgressObserver`: the status line shows
`✳ Reconnecting to api.moonshot.ai… retry 4/10 in 8s — <reason>` through the existing
`ProgressBroker`, and the ambient `CommandInstance` gets the same line for
`ps`/`inspect`/the agents pane. Settling clears only the banner the observer raised
(label-guarded) — never a crew's own progress. No observer registered = behaviour
unchanged (retries only logged).

### Fixed — transcript errors: one actionable line, never a stringified stack

A crew failure travels as
`PromiseRejectedException(ObjectWrapper(AggregateException(HttpRequestException(SocketException))))`
and its `Message` embeds the full stringified stack — which the REPL used to dump
verbatim into the transcript (the live `/analyze` incident: ~40 lines of .NET frames
for one DNS hiccup). New `ConciseErrors` helper (`Orkeon.Cli.Commands.Scripting`) unwraps the
wrapper layers (JS rejection → carried CLR exception, `AggregateException` flatten,
`TargetInvocationException`) and keeps the first line of the root cause —
`✗ analyze failed: Resource temporarily unavailable (api.moonshot.ai:443)`. Applied at
every transcript-facing site (`ScriptHostFacade` crew failures, `ScriptCommand`
dispatch/completed rejections, `CommandDispatchService` instance failures); the full
exception still goes to the logs at each site.

### Added — LLM streaming: connect-phase retry for transient failures

The buffered HTTP path runs under the Polly `GetLlmApiPolicy`, but
`SendStreamingRequestAsync` was a single bare `SendAsync` — one transient socket
failure killed the whole turn. It now retries the CONNECT/headers phase itself
(3 attempts, 0.5 s/1 s backoff, `Retry-After` honoured capped at 30 s) on transport
errors, client-side connect timeouts, and retriable statuses (408/429/5xx). Once
headers are handed to the caller, a mid-stream failure is never retried — replaying a
partially-consumed stream is the caller's decision. Non-transient statuses (401…)
return immediately, unretried.

### Fixed — TUI: a line typed before the runner's first read was silently dropped

The split-pane input field is live from the first frame, but the scripted-commands
runner only starts reading after its startup script load (57 commands ≈ 30–60 s of
discovery → esbuild → evaluate). A line submitted in that window was echoed to the
transcript and then **discarded** — the live "que fait-on ?" incident: the free-text
request looked accepted and nothing ever happened. `ReplPaneView` now buffers
type-ahead submissions in a FIFO queue and delivers them to subsequent
`ReadLineAsync` calls — the same type-ahead semantics a plain terminal gives for
free. Pinned by three `ReplPaneViewTests` (buffered delivery, FIFO order, live read
still wins).

### Fixed — scripted commands: `async dispatch` / `async completed` now awaited deterministically

`ScriptCommand` unwrapped an async `dispatch`'s promise with the synchronous
`UnwrapIfPromise` (blocking the engine-lock thread until settlement) and the
`completed` drain did the same. Both now await `UnwrapIfPromiseAsync` — same pattern
as the sync-handler path — and surface a rejected dispatch/completed promise as the
same `Error: …` console line as a thrown one, instead of relying on Jint's blocking
unwrap semantics. Pinned by a dispatch-with-pending-promise integration test (the
`/assistant` shape since B-5: await session state, then post).

### Added — end-to-end progress channel for long CLI operations

A `ProgressBroker` singleton (`Orkeon.Cli.Commands.Scripting.Progress`, registered by
`AddScriptCommands`) now carries a live `{label, step/total | percent, message}`
snapshot from whoever is doing long work to whoever renders it. Three publishers:
`ctx.progress(...)` handles from command scripts (which also stamp the ambient
`CommandInstance.ReportProgress` — the field `ps`/`inspect` exposed since design §6 but
nothing ever wrote); a new host tool **`progress_report`** so CREW scripts — which have
no `ctx.progress` — can report through the `tools` global (`Tools.progressReport`);
and an optional `IProgress<IndexBuildProgress>` hook on `RaggableEnrichmentServices`
notified by `RaggableTreeBuilder` per phase and per parsed file (null by default —
zero cost when unwired; the ConsoleApp routes it to the broker as "Indexing codebase").
The TUI status line renders the snapshot as
`✳ Compacting conversation… ▰▰▰▱▱▱▱▱▱▱ 34% (12s)` (indeterminate operations show
elapsed + message instead of a bar), including for background crews that run detached
from the REPL's own turn; the agents pane swaps a running row's intent for its live
progress. `ProgressAmbient` (AsyncLocal) links detached `post`/`postWork` flows to
their instance; completion clears the broker slot by ticket so one instance can never
erase a newer operation's bar.

### Added — `spinnerVerbs`: configurable status-line verbs + spinner animation

The status line's verb rotation ("thinking verbs" in the tweakcc vocabulary) is now
configurable: `TerminalGuiOptions.SpinnerVerbs` seeds a boot-time list (bound from
`Orkeon:Cli:Tui:SpinnerVerbs` in the ConsoleApp), and the live
`TuiIntegration.SpinnerVerbs` delegate — wired by the ConsoleApp to the scripted `/config`
layers (`config_map` session state over `/workspace/.orkeon/config.json`) — wins over
it without a restart. The verb re-draws every fifteen seconds on long turns
(`StatusLineFormatter.VerbFor`), the leading glyph animates through spinner frames
(`✢ ✳ ✶ ✻`, ASCII `| / - \`) at four steps per second (`SpinnerFrame`), and the
status-line timer tightened from 1 s to 250 ms accordingly. Defaults unchanged: the
six Orkeon gerunds.

### Fixed — agents pane: live work only, finished agents leave immediately

`TuiFidelityWiring.BuildAgentRows` collapsed every non-running instance to `idle`, so a
finished crew was indistinguishable from a stuck one (the exact confusion of the
2026-08-06 captures). Per the user ruling that followed — `idle` means *waiting*, not
*finished* — the pane now shows LIVE work only: a finished agent's row disappears at
once (no retention window, no terminal badges; `ps`/`inspect` stay the audit trail),
and `idle` never renders at all. `● main` (filled bullet, no metrics) appears only
while at least one delegated agent runs — with nothing delegated the pane collapses,
matching the reference. Delegated rows render hollow (`○`) with the live
`elapsed · ↓ tokens` pair.

### Added — `ILlmUsageSink`: per-call LLM usage events, per-agent token attribution

New Application port `ILlmUsageSink` (mirror of `ILlmDeltaSink`, same plumbing chain
`JsEngineFactory → crewBuilder → JsCrew → JsLlmFacade`): every `ctx.llm.*` path —
`complete`, `chat`, `extract`, `decide`, `stream` (both variants), and each `act`
iteration (buffered or streamed, counted exactly once) — reports a `CostUsageEvent`
carrying crew/agent/provider/model and the token split (a total-only response lands on
`CompletionTokens` so `Prompt + Completion == TokensUsed` — the pricing registry then
prices that total at the output rate, a deliberate upper bound: conservative for
budgets, an overestimate for cost reporting on split-less providers; a response with
no usage at all reports nothing — "no usage" ≠ "zero tokens"). `extract` reports
before its JSON parse, so a prose reply that throws still counts the paid tokens. Nothing fed `ICostBudgetManager`
before this: the REPL's session token readout summed an event stream no one produced.
`AddScriptCommands` registers `InstanceAttributingUsageSink`, which forwards to the
cost manager (fixing that readout and `/cost`) AND credits the `CommandInstance`
ambient at call time (`ProgressAmbient`, the progress channel's AsyncLocal) — so the
agents pane's `↓ NN.Nk tokens` is now that agent's real usage (`—` only when truly
unattributable). `CommandInstanceView` gains `tokens` (visible to `ps`/`inspect` and
the F4 detail; typed in `orkeon-cli.d.ts`). A sink that throws degrades to unobserved
usage, never to a failed LLM call.

### Added — agents pane: keyboard + mouse selection (F4)

The pane stays non-focusable at rest (its first live launch proved a focusable
read-only pane steals the prompt focus), but **F4** now enters an explicit selection
mode: ↑/↓ move a chevron cursor, **Enter** prints the instance's detail into the
transcript (state, intent, elapsed, progress, result/error — via the new
`TuiIntegration.DescribeAgent` delegate over `dispatch.get(ticket)`), **Esc** hands
focus back to the prompt. A mouse click selects a row without stealing focus; a
double-click opens the same detail. The hint bar advertises `f4 agents` only while the
pane has rows. (F4, not Ctrl+A: the focused panes' select-all already owns Ctrl+A and
global bindings fire before view dispatch.)

### Added — `ActOptions.system`: a real system prompt for scripted `act()` agents

`ctx.llm.act(prompt, { system })` now seeds a `role:"system"` message as the first
message of the tool-calling conversation (re-sent on every loop iteration). Until now
`act()` always sent a single user message, so a scripted agent could not have a system
prompt at all — identity and tool policy travelled inside the user turn with user-level
authority (the same authority as tool results, which also come back as user turns), and
the providers' native system handling (Anthropic top-level `system`, `cache_control`;
`PrependConfiguredSystemMessage` on the OpenAI-compatible providers) never fired. The
conversation-level message wins over `LlmConfig.SystemMessage` on every provider; the
option omitted keeps the historical single-user-message shape byte for byte.
(`JsLlmFacade.ResolveSystem`, `Typings/context.d.ts`.)

### Added — hybrid code search + edit↔search freshness in the RaggableTree (RAG×Tree)

`codebase_search` (and `IRaggableStore.SemanticSearchAsync`) fuses an embedding cosine
ranking with a **code-aware BM25** (camelCase/snake_case sub-tokens + whole identifier)
via Reciprocal Rank Fusion — `SemanticQuery.Mode` (`Hybrid` default / `Vector` /
`Lexical`), `SearchHit.MatchOrigin`. Pure vector missed `getUserById` when the query
said "fetch user", and an exact identifier could rank below prose; each half now covers
the other's blind side. With no embedder wired, `Hybrid` degrades to `Lexical` instead
of the historical silent empty (explicit `Vector` keeps that contract). The BM25/RRF
implementations are Analysis-native twins of the RAG's (`Bm25CodeIndex`, `RankFusion`) —
the dependency must keep pointing Rag → Analysis, never back.

Freshness (an agent that EDITS files invalidates its own index): `FileWriteTool` gains
an optional `IIndexInvalidation` hook (dirty-marking, O(1), same precedent as the
citation validator); `IndexFreshnessService` reindexes the dirty set ∪ the git
working-tree changes (shell edits) BEFORE a read tool answers — grouped, single-flight,
2 s clean-probe debounce, failure degrades to the stale index and keeps the debt.
`codebase_search` reports `refreshed_files`; `index_status` reports `dirty_count`/
`dirty_paths`. `InMemoryRaggableStore` gains a `ReaderWriterLockSlim`: a search running
DURING an incremental reindex no longer risks `InvalidOperationException` on the mutated
dictionaries. The freshness pass is the first real producer on `IRaggableTreeEventBus`;
`IGitDiffProvider` (never registered before — `incremental_reindex`'s commit-range path
could not resolve it) and the bus are now registered by `AddRaggableTree`, and
`IGitDiffProvider` gains `GetWorkingTreeChangesAsync`.

Orkeon.ConsoleApp wires `AddOrkeonRag` + `AddOrkeonRagTools`: `rag_search`/`rag_ingest`/
`rag_eval` are available to the scripted REPL, and `rag_search`'s `raggable-tree`
collection inherits the hybrid + freshness path. Live-validated end to end (an internal probe crew,
8/8): exact identifier ranks `hybrid`, write→search round-trip reports
`refreshed_files: 1`, RAG routing serves the code index.

### Changed — `ctx.llm.stream` now asks for usage and carries the reasoning channel (SCR-24)

`stream` went through `GenerateStreamingAsync`; it now goes through `ChatStreamingAsync`. Both read the same SSE stream, and the difference is what they ask for: the chat path sends `stream_options: { include_usage: true }`, without which most providers emit no usage chunk at all — so a streamed call had **no token accounting**. Measured on a live run whose two streamed requests were `{"model":…,"stream":true}` with no `stream_options`; they carried usage only because Moonshot volunteers it, and the same round on OpenAI would have reported nothing. The calls that stream are the long, expensive ones.

The plain path also dropped `delta.reasoning_content`, so a thinking model's stream is silent for as long as it thinks — round-41's deliverable 13 spent 22 673 of its 32 627 completion tokens reasoning, most of a nine-minute call in which "no chunk yet" and "the stream died" were the same observation.

- `stream(prompt)` keeps yielding strings — no contract change. What the chunks cannot carry is exposed on the returned object: **`usage`** (`{ promptTokens, completionTokens, tokensUsed, cacheHitTokens, model }`, `null` while the stream runs and `null` for good when the provider reported nothing) and **`reasoningChunks`**. Read them after the loop.
- Deliberately NOT callbacks. A callback has to be invoked from the stream's own thread, and Jint's `Engine` is single-threaded: the first cut of this did exactly that, and a measured run — seven area writers streaming concurrently — died of a `NullReferenceException` inside `ScriptFunction.Call`, with all seven writers falling back to a placeholder and the script stopping silently after assembling its document. CLR state read through interop runs on the engine's own thread.
- Reasoning progress is logged by the facade through the host logger every 200 deltas: a script cannot log it for itself, because while the model reasons its loop body never runs.
- `usage` is populated on the non-streaming fallback too, so "this provider does not stream" and "this provider reported no usage" stay distinguishable.

### Added — `rag.retrieve` / `IRagRetrievalCapable`: retrieval without the generation nobody asked for (SCR-24)

A caller that wants the retrieved passages rather than prose was still charged for a full grounded generation, because `IRagPipeline` exposed only `QueryAsync`. Measured on the 2026-08-04 gap round: seven `rag.query` calls whose generated answers were discarded **by design** cost 14 748 completion tokens — 68 % of them reasoning tokens — and 394 s of wall time, on top of retrieval that had already produced every citation the caller used. The generation stage is the expensive half of a RAG call and it is optional far more often than the API shape suggested.

- **`IRagRetrievalCapable` (`Orkeon.Rag.Abstractions`)** — opt-in capability, same shape as `IHybridSearchCapable`: `RetrieveAsync` runs `transform → retrieve → fuse → rerank → assemble` and stops. Declared as a separate interface rather than added to `IRagPipeline` because not every executor can honour it — the corrective graph interleaves evaluation with generation, so "retrieval only" is not a prefix of its run — and a caller must be able to ask instead of discovering the answer through an exception.
- **`StagedRagPipeline`** implements it; `QueryAsync` and `RetrieveAsync` now share one `RetrieveCoreAsync`, so the two cannot drift. The returned `RagAnswer` keeps the same shape (empty `Text`, populated `Citations`) and the trace carries a `generate` step saying the stage was skipped on purpose — an absent step would read as a trace from an older pipeline.
- **`rag.retrieve(question, options)`** in the scripting DSL, same signature as `rag.query`. On a pipeline that is not retrieval-capable it throws rather than falling back to `QueryAsync`: a silent fallback would charge exactly what the caller asked to avoid, with no way to tell.

### Fixed — the `system` role was flattened into the user message on every OpenAI-compatible provider (SCR-24)

`HttpLlmProviderBase.ChatAsync` flattens messages into one prompt shaped `"{role}: {content}"` per line, and `OpenAICompatibleProviderBase` took the structured chat path only when tools, tool-call metadata or a vision payload were present. A plain `system` + `user` conversation — the shape of the entire RAG generation stage, and of every LLM judge, retrieval evaluator and groundedness checker in this repository, none of which declares a tool — therefore reached the provider as a single `user` message whose text began with `system: `. A multi-turn history was concatenated the same way, so an assistant turn arrived as something the user claimed the assistant had said. Evidence: the run's exchange log, all seven RAG generations sent as `messages: [{ role: "user", content: "system: You are a retrieval-augmented assistant…" }]`.

- Every `ChatAsync` call with at least one message now takes the structured path. A lone user message was affected too (it went out as `"user: Hello."`), so no case is left on the flattening path; an empty or null array still delegates to the base, which turns it into an empty single prompt.
- `grammar` (GBNF) was emitted only by the single-prompt builder and is now written by both, so an option cannot appear or vanish with the number of messages sent.
- Around 400 mocked provider tests missed this: they assert on the response, and a mocked handler answers whatever it is sent. The new `OpenAICompatibleProviderBaseRoleFidelityTests` read the outgoing payload on the cases with no tool and no image, which was the remaining blind spot.

### Added — Declared provider capabilities, structured outputs and thinking on all 12 providers (LLM-02, LLM-03, LLM-04)

The audit's central finding was not that the wiring was missing but that its absence was **invisible**: `LlmResponseFormat` and `LlmThinkingConfig` cascaded correctly from crew → agent → task → script → call-site, yet only DeepSeek wrote `response_format` and only DeepSeek and Z.AI wrote `thinking`. On the ten other providers a YAML declaration was silently dropped. Closes G-15, G-16 and G-19.

- **`LlmProviderCapabilities` (Domain)** — each provider declares what its API really supports: `ResponseFormat` (`None` | `JsonObject` | `JsonSchema`), `Thinking` (`None` | `EffortOnly` | `Toggle` | `Budget`), `Vision`, `ExplicitPromptCaching`, `RequiresJsonKeywordInPrompt`, `ReplaysReasoningContent`. Exposed on `ILlmProvider` as a **default-implemented** member (like `BaseConfig`), so third-party providers and test doubles keep compiling, and a provider that declares nothing gets nothing written on its behalf.
- **The end of silent drops** — an option the caller declared that the provider cannot honour now produces an actionable warning naming the option, the provider and the remedy. This is the actual fix for G-15/G-16; the wiring below is the easy half.
- **The OpenAI dialect is written once.** `OpenAICompatibleProviderBase.ApplyProviderSpecificOptions` went from a no-op hook to a capability-driven implementation. DeepSeek and Z.AI held two near-identical copies of the thinking translation and two of the `reasoning_content` extraction; both were refactored onto the base and shrank to what is genuinely theirs (DeepSeek's cache counters, Z.AI's `reasoning_tokens`). Their 33 + 15 existing tests pass unmodified.
- **Structured outputs on all 12 providers (G-15)** — `LlmResponseFormat` gains an optional `Schema` (`LlmJsonSchema { Name, Schema, Strict }`) and a `JsonSchema(...)` factory; `Type` is untouched, so existing configurations behave identically. Dialects: `response_format` (OpenAI-compatible family, with `json_schema` where the vendor validates it), `output_config.format` (Anthropic), `format` (Ollama, which also accepts a full schema). A schema handed to a provider that only guarantees well-formed JSON is **downgraded to `json_object` with a warning**, never in silence.
- ⚠️ **Anthropic has no schema-less JSON mode.** `output_config.format` takes exactly `type` (always `json_schema`) and `schema` — there is no equivalent of `json_object`, and `name`/`strict` do not belong there (`strict` exists, but on individual tools). A `json_object` request on Anthropic is therefore **reported**, not sent in a shape the API would reject. Supply a schema, or state the shape in the prompt.
- ⚠️ **The YAML allow-list is lifted.** `YamlCrewMapper` accepted only `text` and `json_object` and downgraded everything else to `null` — which would have discarded `json_schema` before it reached any provider. Unknown values now travel to the provider **with a warning**: a new vendor value works without a framework release, and a typo surfaces as a provider error rather than as nothing at all. Declare a schema with a `response_schema:` block (`name`, `schema`, `strict`) next to `response_format: json_schema`; a `response_schema:` on its own implies `json_schema`. The scripting DSL gains `withResponseSchema(name, schema, strict?)` on both the agent and task builders.
- **Thinking on all 12 providers (G-16, G-19)** — four dialects: `reasoning_effort` (+ the `thinking` block where the API has an explicit toggle) on the OpenAI-compatible family, `thinking: {type: "adaptive"}` + `output_config.effort` on Anthropic, `think` (boolean or effort level) on Ollama, `enable_thinking` + `thinking_budget` on Qwen's DashScope dialect. `LlmThinkingConfig` gains `BudgetTokens`, mapped **only** to Qwen — the `budget_tokens` shape found in many older sources is rejected with an HTTP 400 by the current Claude generation, so it is reported rather than sent.
- **`reasoning_content` extraction is generic**; its **replay** stays DeepSeek-only, driven by `ReplaysReasoningContent` — it is an API constraint of theirs (HTTP 400 without it), not a property of reasoning models, and Z.AI documents the opposite.
- Ollama's `format` and `think` are wired **on the current `/api/generate` path**: contrary to what the audit reported, both are accepted there, and only native tool calling actually requires the `/api/chat` migration.

### Added — `orkeon llm probe`, the provider campaign harness (LLM-08/C1)

Roughly 400 unit tests cover the LLM providers and **every one of them speaks to a mocked HTTP handler**. A mock proves the framework sends what we believe it sends; it cannot prove the vendor accepts it. Before this, only DeepSeek had archived real-execution traces.

- `orkeon llm probe --provider <name> [--model …] [--base-url …] [--modes M1,M8] [--archive <dir>]` runs the matrix's protocol modes against a live provider and prints a report ready to paste into the matrix journal, evidence level included. Currently exercises **M1** (single prompt), **M2** (multi-turn + system), **M3** (text streaming), **M4** (chat streaming), **M7** (thinking), **M8** (response format), **M12** (typed error on an invalid model) and **M13** (cancellation). M5/M6 (tool calling), M9 (vision), M10 (cache) and M11 (long context) need per-provider assets or a deliberately expensive call and are not covered yet — the harness names what it does not run rather than implying full coverage.
- **The API key is never a command-line argument**: it is read from an environment variable (`--api-key-env`, default `ORKEON_LLM_API_KEY`) and never appears in the report or the archive.
- A mode that a provider's declared capabilities make inapplicable is reported as such, not as a failure; a mode that throws is recorded as a finding rather than aborting the campaign, since the framework's contract is a typed error response, never a throw. A campaign with any failed mode exits non-zero.
- **The campaigns themselves are not run here.** They consume real credits on real accounts, so the decision to spend belongs to whoever owns them; the remaining LLM-08 work (running the campaigns, replaying experiment 08, filling the matrix) is unblocked but outstanding.

### Added — Azure v1 GA API and native Ollama tool calling (LLM-07)

The two gaps that needed a pipeline migration rather than a payload field. Closes G-06, G-10 and G-25.

- **Azure v1 GA API (G-06, G-25)** — `BuildEndpoint` was hardcoded to the dated, deployment-based shape (`/openai/deployments/{deployment}/chat/completions?api-version=2024-02-01`). Azure has served a **v1 GA** surface since August 2025 — `{endpoint}/openai/v1/chat/completions`, no `api-version` — which is the only path to the Responses API and to the non-OpenAI models Azure resells (DeepSeek, Grok); all of it was unreachable. Select it with `api_version: v1` (typed property or custom parameter). **The dated shape stays the default on purpose**: switching it would silently change the URL of every existing deployment-based configuration.
- ⚠️ **Ollama tool calling is now native (G-10)** — the provider targeted `/api/generate`, which has no tool support, so tool calling went through the text-fallback protocol. It now reaches `/api/chat` whenever the conversation declares tools, replays tool calls, or carries an image, and `LlmProviderFactory` wires it with the native OpenAI strategy. The response is reshaped once — Ollama returns `message.tool_calls` with `arguments` as a JSON **object** and no `choices` array, which is precisely why the framework's single parser could not read it — into the OpenAI body (`arguments` as a JSON **string**, positional call ids since Ollama issues none). Everything else keeps `/api/generate`, so NDJSON streaming and GBNF `grammar` are untouched and the text fallback remains in charge for models without tool support.
- **Ollama vision** — images travel in a bare base64 `images` array rather than as OpenAI content parts. Remote image URLs cannot be forwarded (the server never fetches them), so an image referenced only by URL is skipped rather than silently dropped.
- `docs/reference/limitations.md` no longer describes a limit that has been lifted; its Ollama entry now states what actually remains (no `/api/chat` streaming, bytes-only images, text fallback for tool-less models).

### Added — Anthropic parity, vision exposure and HuggingFace routing (LLM-05, LLM-06)

- **Native SSE chat streaming on Anthropic (G-20)** — `ChatStreamingAsync` used to fall back to the base class's buffered emulation, which waits for the whole answer before emitting anything: no token ever arrived early on Claude. It now parses the Messages API event stream directly, emitting `ContentDelta`, `ReasoningDelta` (extended thinking) and a terminal `Completed` whose response is indistinguishable from the buffered one.
- **Anthropic cache metrics (G-21)** — `cache_creation_input_tokens` and `cache_read_input_tokens` feed the typed `CacheHitTokens` / `CacheMissTokens`, so `CacheHitRatio` is finally computable on Claude. The three input counters do not overlap, so `PromptTokens` is their sum; absent counters stay `null` (unmeasured, not zero).
- **Explicit prompt caching (G-17)** — new `LlmCacheConfig` value object, cascaded like `LlmThinkingConfig` (crew → agent → task → call-site, `cache:` block in YAML). Anthropic's cache is explicit: without a `cache_control` breakpoint **nothing is cached**, so a long system prompt is re-billed in full on every turn — Orkeon read the metrics but never placed a breakpoint. Off by default, since a breakpoint changes what the vendor stores and how the call is billed. Marking the system prompt promotes it to the content-block form the API requires; the tools breakpoint lands on the last tool, which covers the whole catalogue.
- **Vision exposed on the 8 remaining capable providers (G-18)** — Azure, Groq, Together, Mistral, Qwen, Kimi, Z.AI and HuggingFace declare the capability and inherit the `image_url` composition from the base; no per-provider override. Text-only calls emit exactly the payload they did before (asserted). Ollama stays out on purpose — it takes a base64 `images` array rather than OpenAI content parts, which belongs with the rest of its request-shape work. DeepSeek has no vision model and degrades an image message to its text fallback.
- **HuggingFace provider-selection suffixes (G-23)** — `:fastest` / `:cheapest` / `:preferred` and partner pinning (`:groq`) are the only cost and latency lever on Inference Providers. The suffix already travelled to the wire; what was missing was any way to know it was wrong. Added `WithRoutingPolicy` / `WithPartner` helpers and validation that **reports** an unrecognised suffix while still forwarding it — the partner list moves faster than this framework releases.
- **Guard on DeepSeek's Anthropic-dialect endpoint (G-22)** — `api.deepseek.com/anthropic` speaks the Messages API, but host inference matched `deepseek.com` and routed it to the OpenAI-compatible provider, producing malformed requests whose error surfaced far from its cause. It now fails immediately with a message naming the cause and the fix.
- **G-24 (Groq server-side tools) is ruled out with a motive, not left pending.** `browser_search` and `code_interpreter` execute on Groq's side, so they pass through neither `IBaseTool`, nor the agent loop, nor the framework's validation, rate limiting and telemetry. Exposing them is a tooling-architecture decision — how they coexist with Orkeon's tool registry and what gets traced — not a provider-support gap. The same reasoning covers the other vendors' server-side tools.

### Changed — LLM provider defaults, host routing and the Azure config guard (LLM-01)

First sheet of the LLM provider remediation plan, closing gaps G-01→G-05, G-07→G-09, G-11, G-12, G-14 and defect D-01 of the 2026-07-27 audit. **Four providers out of twelve failed with their out-of-the-box configuration**; four more targeted a superseded generation.

- ⚠️ **Default models changed — this changes the behaviour of every configuration that does not specify a model.** Each identifier was confirmed on the vendor's official documentation on 2026-07-27: OpenAI `gpt-4` → **`gpt-5.6-sol`** (`gpt-4` reaches end of life 2026-10-23 and caps context at 8 192 tokens), Anthropic `claude-3-5-sonnet-20241022` → **`claude-sonnet-5`** (retired 2025-10-28), DeepSeek `deepseek-chat` → **`deepseek-v4-flash`** (retired 2026-07-24), Kimi `moonshot-v1-8k` → **`kimi-k2.6`** (the `moonshot-v1-*` series sunsets 2026-08-31), Qwen `qwen-turbo` → **`qwen3.7-plus`** (absent from the catalogue), Mistral `mistral-large-latest` → **`mistral-medium-3-5-26-04`**. `LlmDefaults.DefaultModelName` — the fallback of `LlmConfig.Model` itself — moves with the OpenAI default. Pin a model explicitly to keep the previous behaviour.
- ⚠️ **Default endpoints changed** — HuggingFace `api-inference.huggingface.co` → **`router.huggingface.co`** (the old host is gone, so the provider could not work at all), Kimi `api.moonshot.cn` → **`api.moonshot.ai`** (the mainland host was the default for every account, including international ones; set `BaseUrl` explicitly for a mainland account).
- **Every default now lives in `ProviderDefaults`** — Groq and HuggingFace held theirs inline — and a pinning test asserts the model and endpoint of all twelve providers, read from live instances rather than from constants. Drift becomes a failing test, not a silent change.
- **International hosts are routed to their dedicated provider** instead of falling back to the generic OpenAI one: `api.moonshot.ai` (G-11), `dashscope-intl.aliyuncs.com` and the per-workspace `*.maas.aliyuncs.com` hosts (G-12). `router.huggingface.co` was already matched by the existing `huggingface.co` rule — the audit's G-13 was a false positive, and is now pinned by a test.
- **Mistral routing trap fixed (G-14)** — `InferFromModel` routed *every* `mistral*` model to Ollama, so a cloud identifier such as `mistral-medium-3-5-26-04` was sent to `localhost:11434`. Only the bare `mistral` and its Ollama tags (`mistral:7b`) stay local; versioned identifiers, including `ministral-*`, now reach the Mistral cloud. Setting `BaseUrl` still wins over model-name inference.
- **Azure configuration guard aligned across all four entry points (D-01)** — `ChatStreamingAsync` was not overridden, so with a missing `BaseUrl` it reached `BuildEndpoint` and threw a `NullReferenceException` where the other paths returned a typed error. It now emits the standard `Completed` event carrying that same error. `GenerateStreamingAsync` still ends in an empty stream — `IAsyncEnumerable<string>` has no error channel — but logs it instead of failing silently.
- **Cost tracking follows** — `gpt-5.6-sol` / `-terra` / `-luna` and `claude-opus-5` / `claude-sonnet-5` registered in `ModelPricingRegistry`; `gpt-4` keeps its own entry so configurations that pin it still produce a real cost.
- **`LlmConfig.Gpt4()` → `LlmConfig.WithDefaultModel()`** (and `Gpt4WithSecret` → `WithDefaultModelSecret`). These factories have always returned the *platform default* model, not literally `gpt-4`; with the default moved, the old names became actively misleading. The former names remain as `[Obsolete]` forwarders with identical behaviour — pass `"gpt-4"` to `LlmConfig.Create` if you really want that model.

### Fixed — post-RAG coherence audit (2026-07-26)

- **`LlmProviderToChatClientAdapter` honors the standard `ChatOptions.ResponseFormat`** — the adapter only read the `LlmChatOptionsKeys.ResponseFormat` AdditionalProperties key stashed by the orchestrator, so callers built on plain Microsoft.Extensions.AI options (RAG retrieval evaluator, groundedness checker, query complexity classifier setting `ChatResponseFormat.Json`) never reached providers wiring `response_format` (e.g. DeepSeek `json_object`). The adapter now falls back to `ChatOptions.ResponseFormat` when the key is absent (the explicit key keeps priority) — the JSON constraint was already enforced by strict prompts + tolerant parsing, this makes the API-level guarantee real on the providers that support it.
- **The corrective mechanism test now locks the rank-1 claim** — `CorrectiveRagMechanismSlowTests` asserted only that `notes-power.md` was cited; it now also asserts it is the **first** citation, matching the wording in the RAG-06 task sheet and the eval README.
- Documentation drift cleanup: version references aligned on `src/Directory.Build.props` (0.9.2-beta) across `README(.fr).md`, `CLAUDE.md`, `limitations.md`, `publication-matrix.md`; `publication-matrix.md` workflow narrative matched to reality (all NuGet pack/push lives in `publish.yml` → GitHub Packages; nothing on NuGet.org); `CONTRIBUTING(.fr).md` no longer claims a CI parity gate that was never wired; ADR-006 amended with the RAG-06 decisions (CRAG on `StateGraph`, web-fallback policy/transport split, `corrective` preset, no separate `rag-adr.md`); `docs/INDEX.md` links `rag-pipeline.md`; `limitations.md` + `opt-in-subsystems.md` document the ONNX requirement (`balanced`/`quality`/`adaptive`) and the double-opt-in web fallback; French mirror `docs/fr/architecture/rag-pipeline.md` added; eval README header corrected to 9 cases / 12 documents; `Orkeon.Tools.Rag` NuGet description lists its three tools; stale "lands with RAG-0x" comments rewritten in delivered code.

### Added — Corrective RAG & vitrine (RAG-06): CRAG graph on `StateGraph`, `corrective` profile, opt-in web fallback, examples

The showcase piece of the RAG plan (guide §8): Corrective RAG built on Orkeon's own Graph orchestration mode — the corrective engine *is* a Domain `StateGraph`, RAG demonstrates the `Graph` mode and vice versa — plus the vitrine layer (docs, three runnable examples, final all-profile evaluation).

- **CRAG graph pipeline (C1)** — `CorrectiveRagPipeline` (`Orkeon.Rag.Corrective`), an `IRagPipeline` whose execution is a `StateGraph<RagGraphState>` (immutable record state) with conditional edges and controlled cycles: `retrieve` → `evaluate` (`IRetrievalEvaluator` → `RetrievalVerdict` `Correct | Incorrect | Ambiguous`) → per verdict `generate` / `refine` (decompose-then-recompose, never empties the working set) / `rewrite_query` (vocabulary-gap rewrite, loops back to `retrieve`) → `generate` (original question, same `[n]` rank-based markers, token budget and `edges` layout as the staged pipeline) → `check_groundedness` (`IGroundednessChecker`; ungrounded → re-loop). Every node run is traced as `corrective:<node>` with the iteration ordinal; verdicts, rewritten probes and loop count land in `RagTrace.Verdicts` / `QueryVariants` / `Iterations`. Evaluator and checker are LLM-backed when an `IChatClient` is registered (`LlmRetrievalEvaluator` / `LlmGroundednessChecker`, constrained via `LlmResponseFormat` — `json_object` where wired, e.g. DeepSeek — tolerant JSON parsing elsewhere), deterministic heuristics with a warning otherwise. `AddOrkeonCorrectiveRag(configuration)` (idempotent `TryAdd`, called by `AddOrkeonRag`).
- **Double loop bound (C1)** — `Orkeon:Rag:Corrective:MaxIterations` (default 3) bounds both the rewrite cycle and the groundedness re-loop, and the graph engine's own `CircuitBreakerPolicy` is explicitly derived from that budget as a second, independent layer. Exhaustion → best-effort generation with the best available chunks, traced; a tripped breaker is caught, traced (`corrective:circuit_breaker`) and degraded — the pipeline never throws for a loop condition and can never loop forever (circuit-breaker test included).
- **Opt-in web fallback + anti-injection (C1 security, 6C)** — after rewriting is exhausted the graph may fire `web_fallback`, gated by **two** separate off-by-default switches: `Orkeon:Rag:Corrective:WebFallback` (pipeline policy, Abstractions) and `Orkeon:Rag:WebFallback` (transport — `WebSearchDocumentRetriever`, SearxNG-compatible JSON search, `ApiKeyEnvVar` only, `AddOrkeonRagWebFallback`). Every downloaded page passes `PromptInjectionDocumentValidator` (deterministic heuristics — model-addressed directives EN+FR, chat-template control tokens, hidden HTML, exfiltration vectors; verdict `Clean | Suspicious | Rejected`): `Rejected` never leaves the retriever, `Suspicious` is flagged or discarded per `SuspiciousAction`, content is never rewritten. Threat model + honest limits (pattern-based, evadable) in `docs/architecture/security.md`. Web chunks carry `ScoreOrigin = "web"`.
- **`corrective` profile + Adaptive lift (C2, 6D)** — `RagProfile.Corrective` / `corrective` in `RagProfilePresets` and the resolver (same `IRagPipeline` façade; the profile selects the executor). Preset: hybrid BM25 + RRF (rewritten probes need the lexical leg), **no linear rerank stage** (the graph corrects by looping — no ONNX package needed), `Groundedness.Enabled = false` on purpose (the graph runs its native `check_groundedness` node whenever a checker is registered). The `adaptive` profile's `Iterative` route now delegates to the memoized `corrective` pipeline — the RAG-05 documented fallback to `quality` is **lifted** (`route` step traces `delegate=corrective`).
- **Vitrine (C3, 6B)** — `docs/architecture/rag-pipeline.md` completed (CRAG topology, verdicts, double bound, web fallback, ADR pointers); three new runnable offline examples `examples/rag/hybrid-retrieval` (BM25+RRF vs vector-only), `examples/rag/custom-reranker` (host `IReranker` via `IRerankerRegistrar`), `examples/rag/crew-yaml` (crew `rag:`/`knowledge:` blocks) + scripting variant `examples/scripting/08-rag.ork.ts`; all four `examples/rag/*` projects in `Orkeon.Examples.sln`; `examples/INDEX.md` regenerated.
- **Final evaluation, published honestly (C3)** — `orkeon rag eval --compare fast,balanced,quality,corrective,adaptive` (2026-07-26, offline): fast/balanced/quality/adaptive at 0.89 recall@5 / 0.89 MRR; **`corrective` at 0.78 / 0.64 — worse than `quality` offline, and documented as such**: the extractive stub degrades the graph's LLM nodes (pseudo-random verdicts from the tolerant parser, degenerate rewrite probe shared by all cases), so the offline row measures the loop's guard rails, not rewrite quality — full per-case analysis in `examples/rag/eval/README.md`. The causal end-to-end proof of the mechanism (verdict `Incorrect` → rewrite → `notes-power.md` cited on the seeded q-007 case that `quality` misses, same store and embeddings) is `CorrectiveRagMechanismSlowTests` (scripted LLM for the two roles CI cannot provide, labelled). CI gate unchanged (`balanced`, `correctif` cases excluded, gated aggregates 1.00/1.00).

### Added — RAG query translation & adaptive routing (RAG-05): transformers, MMR, classifier, Adaptive profile

Stage 1 of the pipeline (query transformation, guide §6) plus Adaptive-RAG routing (guide §8.4), measured with the RAG-04 harness.

- **Query transformers (C1)** — `IQueryTransformer { Name, Kind, TransformAsync }` with retrieval semantics per `QueryTransformKind`: `MultiQueryTransformer` (`multi-query`, **Union** — one LLM call produces N phrasings, retrieval runs per query, rankings merged by chunk-id union keeping the original store scores, max on duplicates), `RagFusionTransformer` (`rag-fusion`, **Fusion** — same variants, per-query rankings fused by Reciprocal Rank Fusion, same k as the hybrid stage), `HydeTransformer` (`hyde`, **Replacement** — a hypothetical document is embedded as the retrieval probe INSTEAD of the question; generation and citations always use the ORIGINAL user question), `IdentityQueryTransformer` (`none`). Factory pre-populated via `AddOrkeonQueryTransforms()` (chat client resolved lazily; unknown names fail loudly); options `Orkeon:Rag:QueryTransform` (`Mode` default `none`, `VariantCount` default 3). Unusable/failing LLM responses degrade to `[original]` with a warning — never an exception.
- **Pipeline integration (5D)** — the `StagedRagPipeline` transform stage resolves the configured transformer and wires retrieve/fuse per `Kind` (union / rrf / replacement probe). The transform step traces `transformer`, `kind`, `variants` (count) and the truncated `variant_n` texts; `RagTrace.QueryVariants` carries the full produced texts (original excluded); the fuse step traces its `method` (`union` / `rrf` / `dedup`).
- **MMR diversification (C2, opt-in)** — `MaximalMarginalRelevance.Select` applied after fusion/dedup and before rerank when `Orkeon:Rag:Retrieval:Mmr:Enabled` is set (`Lambda` default 0.7): re-orders the fused candidates by `λ·relevance − (1−λ)·redundancy`. Candidate embeddings are **not** recomputed — the documented lexical (Jaccard) fallback with min-max-normalised scores is the pipeline path; the embedding overload exists for callers that already hold them. Traced on the fuse step (`mmr`, `mmr_lambda`, `mmr_in`/`mmr_out`).
- **Query-complexity classifier (C3)** — `IQueryComplexityClassifier.ClassifyAsync(query) → QueryRoute { NoRetrieval, SingleShot, Iterative }`: `HeuristicQueryComplexityClassifier` (default — deterministic rules, zero LLM) and `LlmQueryComplexityClassifier` (constrained JSON, tolerant parsing, SingleShot fallback with warning). `AddOrkeonQueryRouting(configuration)`, options `Orkeon:Rag:QueryRouting:Classifier` (`heuristic` | `llm`; `llm` without a chat client falls back to heuristic with a warning).
- **`Adaptive` profile (C3/5D)** — `RagProfile.Adaptive` / `adaptive` in `RagProfilePresets` and the resolver: the `AdaptiveRagPipeline` classifies first, then `NoRetrieval` → direct LLM answer (no retrieval, empty citations), `SingleShot` → delegates to the memoized **balanced** pipeline, `Iterative` → **documented fallback to `quality`** until the corrective/iterative engine ships with RAG-06. The decision is always traced: `RagTrace.Route` + a first `route` step (`route`, `classifier`, `delegate`, fallback detail). `Orkeon:Rag:Profile=adaptive` routes the default pipeline through the resolver.
- **Measured comparison** (golden dataset, offline, heuristic judge, 2026-07-26): fast / balanced / quality / adaptive all at 0.89 recall@5 / 0.89 MRR (4 / 217 / 166 / 141 ms/case). **Adaptive equals balanced on this dataset by construction**: the heuristic classifier routes all 9 golden questions to SingleShot → balanced (each has exactly one interrogative word, a single `?`, and fewer than 25 words); the ms/case delta is a warm-ONNX-session artifact of run order, not a quality gain. Offline honesty: `--offline` swaps in a deterministic extractive chat client, so the LLM-backed transformers (multi-query / rag-fusion / hyde) and the `llm` classifier have no real LLM to call — the measured path is `Mode=none` + heuristic routing; the transformer/MMR levers are wired and unit-tested, their quality delta will be measured with a real `IChatClient` and/or the RAG-06 corpus growth.

### Added — RAG quality phase (RAG-04): evaluation harness, hybrid retrieval, reranking, profiles

Measured before proclaimed — the whole phase is driven by an offline CI-runnable evaluation harness (local BGE embeddings, embedded ONNX cross-encoder, deterministic extractive generation, labelled judge — zero network, zero API key).

- **Evaluation harness (C1, plan §9)** — versioned golden dataset `examples/rag/eval/golden.yaml` (7 cases over a 10-document corpus, incl. the seeded hard-retrieval case `q-007` tagged `correctif`), deterministic retrieval metrics (recall@k, precision@k, MRR), generation metrics via LLM-judge with deterministic heuristic fallback (the mode used is **always labelled**), `IRagEvaluator`/`IRagEvalHarness`, agent tool `rag_eval`, CLI `orkeon rag eval --dataset … [--profile|--compare] [--offline] [--min-recall --min-mrr]`, dedicated workflow `.github/workflows/rag-eval.yml`.
- **Hybrid retrieval (C2, plan §5)** — in-process `Bm25Index` + `ReciprocalRankFusion` (RRF k=60) behind the `HybridSearchDocumentStore` decorator (works over all 6 memory providers; in-process index memory documented), native provider hybrid preferred via the new `IHybridSearchCapable` Domain capability (LanceDB), native scores via `IScoredVectorSearch` (ChromaDB/Pinecone/LanceDB).
- **Reranking (C3, plan §7)** — opt-in packages `Orkeon.Rag.Onnx` (cross-encoder runtime, ms-marco-MiniLM-L-6-v2, Apache-2.0) + `Orkeon.Rag.Onnx.Model` (int8 weights **embedded** — guaranteed offline, no download ever) registered with `AddOrkeonOnnxReranker()`; `LlmListwiseReranker` (universal fallback over the 12 providers) and `NoopReranker`; default cascade CandidateK 50 → TopN 5.
- **Staged pipeline + profiles (C4, plan §5.1–5.2)** — `StagedRagPipeline` replaces `LinearRagPipeline` (breaking rename, beta window): transform (hook `none`, RAG-05) → retrieve (CandidateK, per-query hybrid) → fuse (RRF/dedup) → rerank (named, `RerankerFactory`) → assemble (token budget + **anti-Lost-in-the-Middle `edges` ordering**: ranks 1, 3, 5… open the context, …6, 4, 2 close it — best two chunks at the extremities, rank-stable `[n]` markers) → cited generation → optional groundedness hook (checker ships with RAG-06); every stage traced in `RagAnswer.Trace`. `RagProfile { Fast, Balanced, Quality }` + `RagProfilePresets` → **`RagOptions` v2** bound on `Orkeon:Rag` (profile = preset, configuration = per-key override; defaults in `Orkeon.Domain.Constants.Rag.RagDefaults`); `ProfileRagPipelineResolver` builds and memoizes one pipeline per profile (unknown names fail loudly listing `fast, balanced, quality, default`). Default profile is `fast` (deviation from plan §5.2's `balanced`: balanced requires the opt-in ONNX package; opt in with one key `Orkeon:Rag:Profile=balanced`). The store is now always wrapped in the hybrid decorator (ingestion feeds BM25; disabled default mode = strict passthrough); `RetrievalQuery.Hybrid` toggles fusion per query.
- **Measured comparison** (golden dataset, offline, heuristic judge, 2026-07-26): fast 0.89 recall@5 / 0.89 MRR / 3 ms/case; balanced 0.89 / 0.89 / 139 ms/case; quality 0.89 / 0.89 / 105 ms/case (9 cases, incl. two exact-identifier lookups q-008/q-009 that vector-only also resolves at this corpus scale). The three profiles tie **on this dataset by construction**: the eight regular cases are saturated by plain vector retrieval (1.00/1.00 each, `correctif` excluded ⇒ gated aggregates 1.00/1.00 for all profiles), and the seeded `q-007` defeats the cross-encoder too (measured score of the relevant `notes-power.md`: 0.0000, dead last; decoy `faq-battery.md`: 0.9997) — vocabulary bridging is exactly the RAG-05/RAG-06 lever (plan §9.1: `correctif` must fail until Corrective). The CI gate runs on the **balanced** profile and the fast/balanced/quality table is published in the workflow step summary.

### Changed — **BREAKING: RAG subsystem extraction (RAG-02, no shims)**

The RAG feature set is promoted to a first-rank subsystem (`src/rag/` — `Orkeon.Rag.Abstractions` contracts + `Orkeon.Rag` implementations, agent tools in `src/tools/Orkeon.Tools.Rag`; see [ADR-006](docs/adr/ADR-006-rag-subsystem.md)). The legacy namespaces `Orkeon.Application.Interfaces.Rag.*`, `Orkeon.Application.Rag.*`, `Orkeon.Application.Interfaces.Knowledge.*` and `Orkeon.Infrastructure.Knowledge.*` are **removed without `[Obsolete]` shims** (assumed break, decision 2026-07-25, `0.9.x-beta` window).

Opt-in wiring: `services.AddOrkeonRag(configuration)` (namespace `Orkeon.Rag.DependencyInjection`, self-sufficient `TryAdd*`, default `IDocumentStore` = `MemoryProviderDocumentStore` over the ambient `IMemoryProvider`) + `services.AddOrkeonRagTools()` (`Orkeon.Tools.Rag.DependencyInjection`, registers `rag_search`). Neither is called by `AddOrkeonInfrastructure()`.

Migration table (old type → new type):

| Old (removed) | New |
|---|---|
| `Orkeon.Infrastructure.Knowledge.RagTool` (`rag_search`) | `Orkeon.Tools.Rag.RagSearchTool` (`rag_search` — same name, schema `question`/`top_k`/`collection`, and output format `answer` + `Sources:` block; `collection = "raggable-tree"` still routes to `IRaggableStore`) |
| `AddOrkeonRag` (Infrastructure `RagServiceExtensions`) + `AddOrkeonKnowledge` + `AddOrkeonRagValidation` | `AddOrkeonRag(configuration)` (`Orkeon.Rag.DependencyInjection.RagServiceCollectionExtensions` — loaders + ingestion validation + pipelines + factories) + `AddOrkeonRagTools()` |
| `Orkeon.Application.Interfaces.Rag.IRagPipeline` (`ExecuteAsync(question, RagOptions)` → `RagResult`) | `Orkeon.Rag.Abstractions.Interfaces.IRagPipeline` (`QueryAsync(RagQuery)` → `RagAnswer` with citations + trace) |
| `Orkeon.Application.Rag.RagPipeline` + `ChatClientResponseGenerator` | `Orkeon.Rag.Pipeline.StagedRagPipeline` (named `LinearRagPipeline` until RAG-04/C4) |
| `KnowledgeService` (ingestion side) | `Orkeon.Rag.Pipeline.DefaultIngestionPipeline` (`IIngestionPipeline`) |
| `IKnowledgeService` (Application port) | `Orkeon.Rag.Abstractions.Interfaces.IDocumentStore` (storage/search) + `IIngestionPipeline` (ingestion) + `IRagPipeline` (query) |
| `TextFileLoader` / `CsvDocumentLoader` / `HtmlDocumentLoader` / `PdfDocumentLoader` / `DocumentLoaderFactory` (`Infrastructure.Knowledge.Loaders`) | `Orkeon.Rag.Loaders.*` (same names, `IDocumentLoader` over `SourceDescriptor` → `RagDocument`) |
| `WebPageLoader` (`Infrastructure.Knowledge.Loaders`) | `Orkeon.Rag.Loaders.WebPageLoader` (typed `HttpClient`, kinds `url`/`web`) |
| `RecursiveTextChunker` / `SentenceChunker` (+ tool-local chunker copies) | `Orkeon.Rag.Chunking.*` — `IChunkingStrategy` implementations `recursive`, `sentence`, `structural`, `semantic` |
| `ContentIntegrityValidator` / `PromptInjectionDocumentValidator` / `DataValidationPipeline` / `ProvenanceTracker` / `IQuarantineStore` + `InMemoryQuarantineStore` (`Infrastructure.Knowledge.Validation`) | `Orkeon.Rag.Validation.*` (same names — validation stays on the ingestion path) |
| `AnalysisEmbeddingProviderAdapter` (`Infrastructure.LLMs.Embeddings`) | `Orkeon.Rag.Embeddings.AnalysisEmbeddingProviderAdapter` |
| `SimpleEmbeddingService` (hash-based, `[Obsolete]`) | **Removed without replacement.** The default `IEmbeddingService` now adapts the `IEmbeddingProvider` port (`EmbeddingProviderServiceAdapter`): local BGE → remote `Orkeon:Embeddings` → fail-fast at first use. Semantic agent selection inherits the real chain. |
| `HybridScorer` (`Infrastructure.Knowledge.Retrieval`, dead code) | **Removed without replacement** (hybrid search capability lives in `Orkeon.Domain.Memory.IHybridSearchCapable`) |
| `FileKnowledgeSource` / `DirectoryKnowledgeSource` / `WebKnowledgeSource` / `DatabaseKnowledgeSource` (`Infrastructure.Knowledge.Sources`) | **Removed without replacement** — describe sources with `SourceDescriptor` and run them through `IIngestionPipeline` (`IngestionRequest`). The Domain contract `Orkeon.Domain.Knowledge.IKnowledgeSource` remains (no framework implementations). |
| `RagOptions` / `RagPipelineOptions` / `RagDefaults` / `KnowledgeContext` / `KnowledgeItem` / `RagTypes` (`RagResult`, `RetrievalOptions`…) | `Orkeon.Rag.Abstractions.Models.*` (`RagQuery`, `RagAnswer`, `Citation`, `ScoredChunk`, `RetrievalQuery`…) + `Orkeon.Rag.Abstractions.Options.RagOptions` v2 (section `Orkeon:Rag`, RAG-04/C4) / `RagIngestionOptions` (section `Orkeon:Rag:Ingestion`); defaults in `Orkeon.Domain.Constants.Rag.RagDefaults` |
| `ResearchFindings` (was in `Orkeon.Application.Rag`) | **Kept** (not RAG) — moved to `Orkeon.Application.Services.Generic` |

## [0.9.2-beta] - 2026-07-24

First version actually published to GitHub Packages since `0.9.1-beta` (2026-07-04): the intermediate `v0.9.1-beta.rc*` tags re-packed the unchanged `0.9.1-beta` version from `Directory.Build.props`, so `--skip-duplicate` silently skipped every push. This release bumps the props version so the feed picks up everything below.

### Added

- **`IFileSystemScope`** (`Orkeon.Domain.FileSystem`) — ambient per-scope mount override for the VFS.
- **`ILlmDeltaSink`** (`Orkeon.Application.Interfaces.Ports`) — streaming delta sink port for LLM output.

### Security

- **A2A mTLS server now authenticates the client certificate instead of merely checking its dates** (SEC-012, R9.1). With `RequireMutualTls = true`, an incoming certificate must chain to one of `A2ASecurityOptions.TrustedCertificateAuthorities` (X509 `CustomRootTrust` chain — also covers validity dates, removing the last `DateTime.Now` in `src/`) or match the new `TrustedClientCertificateThumbprints` pin list; unpinned self-signed certificates are rejected (403). Starting the server with `RequireMutualTls` and no trust anchor now **throws** (fail-closed) — previously any date-valid certificate passed the guard. Revocation is not checked (private CAs without CRL/OCSP assumed).
- **A2A client-side CA pinning no longer bypasses host-name validation** (SEC-011, R9.1). `A2ASecurityHandlerFactory` only vouches for `RemoteCertificateChainErrors` (private CA unknown to the OS store); `RemoteCertificateNameMismatch`/`RemoteCertificateNotAvailable` are never accepted. The explicit `ValidateServerCertificate = false` opt-out now logs a security warning (local development only).
- **A2A mTLS handler is built once and cached instead of per call** (ANT-018, R9.2). On every A2A call (`send`, `sendSubscribe`, `cancel`, `status`) the client used to re-read the PFX through the VFS, re-import the `X509Certificate2` (never disposed) and create a fresh handler+client — a full mTLS handshake per call. `A2ASecurityHandlerFactory` now returns a pooled `SocketsHttpHandler` (`SslOptions.ClientCertificates`, `PooledConnectionLifetime` 2 min) cached lazily by `A2AClient`; per-call clients wrap it with `disposeHandler: false`. `A2AClient` is now `IDisposable` and disposes the handler and the imported certificate exactly once. Certificate rotation requires a new client instance (options snapshot at construction).

### Changed

- **Public API reshaped to .NET design-guideline conformance; 8 API-shape rules frozen as build errors** (R11.7 / maintainer decision D1 = "fix everything", **breaking, 0.9.0-beta**). The API-shape analyzer family was driven to zero across all `src/` with no `severity = none` carve-out:
  - **Exposed collections are read-only** (CA1002/CA2227/CA1819): `List<T>`/`T[]` properties and returns become `IReadOnlyList<T>`; settable collection properties become `init`/get-only. Deserialization-safe by construction — `IConfiguration`-bound options use `Collection<T>` get-only, YamlDotNet DTOs keep a settable `Collection<T>?`, System.Text.Json DTOs use `IReadOnlyList<T>` init; graph/builder state keeps a private mutable backing field exposed read-only.
  - **URLs are `System.Uri`** (CA1054/CA1056): string URL parameters and properties across `IHttpClient`, `IA2AClient`, `LlmConfig.BaseUrl`, options and tool DTOs now use `System.Uri`.
  - **Cross-language-safe naming** (CA1716): interface/virtual parameters and members renamed off reserved keywords (`ISpecification<T>.And/Or/Not` → `AndWith/OrWith/Negate`, `IAgentRegistrationStore.Get` → `GetById`), and the **`Orkeon.Domain.Shared` namespace renamed to `Orkeon.Domain.SharedKernel`** solution-wide (it collided with the VB `Shared` keyword).
  - **Getters and nesting** (CA1024/CA1034): getter methods (`GetX()`) become properties; public nested types are un-nested (with a qualifying rename where the bare name was generic, e.g. `OrkeonDiagnostics.Tags` → `OrkeonDiagnosticTags`) or made `internal` when they are implementation details.

  All changes are behaviour-preserving (order, JSON wire format, and config/YAML binding verified empirically per layer). `dotnet build Orkeon.sln` stays at 0 warning / 0 error. Note: this is a deliberate breaking change to the public surface, taken inside the 0.9.0-beta window before the first NuGet tag; some test assertions (URL equality, null-argument exception types, read-only collections) are updated accordingly and validated on the Windows test run.
- **Redundant default-value initializers removed and `System.Random` audited across `src/`; CA1805 + CA5394 frozen as build errors** (R11.5, zero-warning campaign hygiene wave). The 89 src fields/auto-properties explicitly initialized to their default value (`= 0/false/null/default/new()`) had the redundant initializer removed (CA1805, mechanical, no behaviour change). The 3 src `System.Random` uses flagged by CA5394 are all provably non-security (a SHA256-seeded deterministic pseudo-embedding fallback, retry-backoff jitter, and a simulated research-confidence score) and now carry a tight justified `#pragma warning disable CA5394`; the rule is frozen as `error` so any new `Random` use trips the build and gets a security review. CA1822 (mark members static) was intentionally deferred — several flagged members are exposed to JS scripts via Jint instance reflection and making them static would break the scripting API. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Logging converted to the `LoggerMessage` source generator across all `src/`; CA1848 + CA1873 frozen as build errors** (R11.3, zero-warning campaign wave B3). The 113 src `ILogger.Log*` call sites flagged by CA1848 (use the LoggerMessage delegates) are now `[LoggerMessage]` source-generated partial methods (per-class EventIds, message templates and structured placeholder names preserved verbatim, exceptions passed as method arguments), and the 34 CA1873 sites (arguments evaluated even when the level is disabled) are fixed by that conversion or by hoisting the expensive expression into a local inside an `IsEnabled` guard. The single dynamic-level audit sink uses cached `LoggerMessage.Define` delegates. Both rules are locked as `error` for `src/**`. Behaviour is unchanged (same levels, templates, args, exceptions); tests and `examples/` keep their occurrences and are not frozen. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Externally-visible method parameters are null-guarded across all `src/` and CA1062 frozen as a build error** (R11.2, zero-warning campaign wave B2). Every externally-visible method that dereferenced a reference parameter without checking it for null now guards it — 605 unique src sites get `ArgumentNullException.ThrowIfNull(param)` as their first statement (the netstandard2.0 analyzer project uses the classic `if (x is null) throw` form), and CA1062 is locked as `error` for `src/**` so no public entry point can ship unguarded. Body-only edits, no signature/behaviour change: expression-bodied methods became block bodies, constructor-initializer dereferences use `(param ?? throw …)`, and the contractually-nullable null-tolerant JS-facing logging shims coalesce (`?? JsValue.Undefined`) instead of throwing (a throw there would have regressed their documented null-tolerance). Tests and `examples/` (separate solution / test doubles) keep their occurrences and are not frozen. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Globalisation analyzer family resorbed to zero across all `src/` and frozen as build errors** (R11.1, zero-warning campaign wave B1). The culture-sensitive string-operation rules CA1307/CA1310 (`StringComparison`), CA1305/CA1304 (`IFormatProvider`/`CultureInfo`), CA1311 (culture-aware case) and CA1308 (`ToLower`) are fixed at 363 unique sites and locked as `error` for `src/**` in `.editorconfig`, so framework code can never silently reintroduce one. Arbitrage "Ordinal default + protect ToLower": comparisons → `StringComparison.Ordinal` (`OrdinalIgnoreCase` for identifier/key matching), formatting → `CultureInfo.InvariantCulture`, and the 91 `ToLowerInvariant()` sites that *produce* a wire/storage/switch value keep their lowercase form under a tight justified `#pragma warning disable CA1308` (6 comparison-only sites rewritten to ordinal-ignore-case). The same six rules are neutralised (`severity = none`) in `tests/.editorconfig` — test assertions compare literal values where ordinal is already the default — a documented arbitrage, not a suppression. These rules are outside the default analysis set, so enabling them as errors enforces them in the normal build without `AnalysisMode=All`; `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Pinned Terminal.Gui to 2.0.1** (was 2.1.0). v2.1.0 shipped 2026-05-08 with a major API redesign + rendering bugs (gray-on-gray default scheme, focus on read-only widgets, missing `FakeDriver` for tests). 2.0.1 is the last stable release before that redesign. Same code compiles unchanged (API surface compatible). The xUnit `ModuleInitializer` crash (TUI-12) exists in both versions, so view-touching tests stay skipped.

### Fixed

- **Null-argument contracts reconciled with the R11.2 guards across Domain/Application/Scripting tests** (R11.2 follow-up, surfaced by the full Windows test run). Six tests and one value object that still encoded pre-guard behaviour are aligned with the `ArgumentNullException.ThrowIfNull` guards added in R11.2: `ValidationResult.Combine`, `TypedTaskContext.Transform`, `AgentMapper.ToDto`/`CreateFromRequest` and `SequentialCrewOrchestrator.KickoffAsync` now correctly reject null (the orchestrator's `CrewInput` has no empty form, so a null input is genuinely invalid — the test that expected "graceful" handling now expects the throw). `TaskDescription.From` is the one behavioural fix: the R11.2 `ThrowIfNull` had fragmented its validation so a null value surfaced `ArgumentNullException` ("Value cannot be null") instead of the value object's unified `ArgumentException` ("… cannot be null or whitespace") used for empty/whitespace — it now validates null/empty/whitespace uniformly in one guard (still CA1062-clean). A telemetry test (`OtelTests.tool_call_emits_a_span_with_tool_name_tag`) was also made robust against the process-global `ActivityListener` picking up a concurrent test's tool-call span, by filtering on the unique `tool.name` tag like the sibling crew/agent span tests already do. `dotnet build Orkeon.sln` stays at 0 warning / 0 error.
- **Tool URL parameters typed as `System.Uri` no longer break the agent tool contract** (R11.7-D1 follow-up). The D1 API-shape wave (CA1054/CA1056) converted tool request/response URL properties to `System.Uri`, but the typed-tool schema generator mapped `Uri` to JSON type `"object"` — so every tool call carrying a string URL was rejected before execution with `Parameter 'url' has invalid type. Expected: object` (≈30 direct failures cascading across `web_scrape`, `scrape_element`, `http_api`, `cache_search` and `arcadedb_query`). Fixed at a single point rather than reverting the DTOs: `ToolSchemaGenerator` now maps `Uri` to `"string"` (format `uri`) like `Guid`/`DateTime`, and a new tolerant `UriTolerantConverter` (registered only in the component pipeline's options) round-trips `Uri`↔string — empty/whitespace maps to `null` (so a tool's own "URL cannot be empty" validation reports a friendly error instead of an opaque JSON failure), relative values are accepted (`UriKind.RelativeOrAbsolute`, e.g. a host-substring filter), and writes use `OriginalString` to preserve the exact URL with no trailing-slash canonicalisation. The `Uri` typing (and the frozen CA1054/CA1056 errors) are kept. The same `string`→`Uri?` conversion had also silently flipped the required `url` parameter of `web_scrape`/`scrape_element`/`http_api` to optional in the generated schema (a non-nullable `string` infers required; a nullable `Uri?` infers optional); those three inputs are marked `[FieldSchema(IsRequired = true)]` to restore the pre-D1 schema while keeping the property nullable for the tool's own emptiness check (`cache_search`'s URL substring filter stays optional; `arcadedb_query`'s `bolt_uri` was already required). Also corrected three test concerns surfaced by the same Windows run: the `MockHttpMessageHandler`/`TestHttpMessageHandler` doubles now capture a **buffered clone** of each request so post-send body assertions survive the provider's R10.2 content disposal (was `ObjectDisposedException` ×27), the `IUrlValidator` SSRF stub records `OriginalString` instead of the canonicalised form, and three `LlmBasedManager` null-argument tests now expect the `ArgumentNullException` the R11.2 guards correctly throw (was `NullReferenceException`). `dotnet build Orkeon.sln` stays at 0 warning / 0 error; the test suite is re-run on Windows.
- **Conversation roles are matched case-insensitively everywhere** (SML-009, R12.5). The HTTP providers compared `msg.Role == "assistant"` (case-sensitive) while `ConversationPolicy` used `OrdinalIgnoreCase`, so a mixed-case role like `"Assistant"` was serialized one way and policy-matched another. A new `LlmRoles` (canonical lowercase wire values + a single `Is`/`IsX` helper) now routes every comparison and message construction in the Anthropic/OpenAI-compatible providers, `ConversationPolicy`, and the `LlmMessage` factories. This fixes a real bug where a mixed-case `"Tool"` orphan survived history trimming and produced the orphan `tool_result` the trim exists to prevent. The scripting default models (`LlmNamespaceBinding`) were de-duplicated (they were defined twice in the same file); reconciling them with `ProviderDefaults` is a maintainer product decision (the values differ).
- **`release.yml` and `ci.yml` no longer publish divergent NuGet perimeters** (OSS-011, R8.3). `release.yml` was missing `Orkeon.Infrastructure` even though the README documents installing it; both workflows now pack the same three core libraries. The published set is documented in `docs/reference/publication-matrix.md` (promotion of the tools family / `orkeon` tool is held until decision D3 so the scripting twins' names are not locked into NuGet before a possible rename). Stale metadata fixed: the `CONTRIBUTING` clone/upstream URLs (`Orkeon/orkeon`), the `Orkeon.ConsoleApp` local `1.0.0` version override (now inherits `0.9.0-beta`), and the obsolete `+orkeon` coverage-filter comment.
- **`HttpRequestMessage`/`HttpResponseMessage` are disposed on the LLM request path** (ANT-006, R10.2). The 20 per-call CA2000 leaks in the Anthropic/OpenAI-compatible/Ollama providers and `HttpClientAdapter` are fixed by real lifetime: `using var` for non-streaming request/response, dispose-after-send for the streaming request (the response escapes but the request body is already transmitted), and the previously-leaked cloned retry request in `ExecuteHttpRequestAsync`. The 11 `LlmProviderFactory` sites are ownership transfers (the provider is wrapped in the returned adapter; its `Dispose` is a no-op) — collapsed into one generic `Adapt<T>` helper carrying a single justified suppression.
- **Semantic memory recall no longer silently returns empty on the default configuration** (MAT-017, R10.1). `IMemoryProvider.SearchSimilarAsync` was a default interface method returning empty, and `InMemoryProvider`'s real cosine implementation was unreachable through the interface (C# does not re-map derived members onto a base-implemented interface). `SearchSimilarAsync` is now an **abstract member of `MemoryProviderBase`** (compiler-enforced for every provider), the four affected providers re-list the interface, and Redis/ChromaDB/Pinecone — which had **no** implementation at all — gained real vector searches (server-side query for Chroma/Pinecone, client-side cosine for Redis). A reflection test locks the interface map for future providers.
- **`ICodeSandbox` resolution no longer launches a `docker version` process under the DI singleton lock** (ORG-012, R10.3). The availability probe lives in a memoized lazy decorator (`LazyProbingCodeSandbox`) and runs at the first `ExecuteAsync` (async, cancellable); the probe process is now killed on timeout/cancellation. Fail-closed semantics of the sandbox gate are preserved byte-for-byte. An architecture test bans `GetAwaiter().GetResult()`/`.Result` in `src/` DI factories (single documented exemption: plugins).
- **Script command loading no longer blocks the DI thread** (ANT-002, R10.3). `ScriptCommandRegistry` resolution is pure wiring; discovery + esbuild transpilation + Jint evaluation are deferred to a memoized task awaited at runner startup (failures are not memoized — next call retries).
- **Script `ctx.services.get("tools")` no longer materializes a new set of transient disposable tools per call** (ANT-005, R10.4). The whitelist resolves the tool set once (thread-safe lazy) and serves a fresh shallow copy of the same instances — unbounded memory growth in long REPL sessions is gone.
- **`MemoryProviderFactory` no longer creates bare `HttpClient`s** (ANT-013, R10.5). Chroma/Pinecone/LanceDB clients use `SocketsHttpHandler` with `PooledConnectionLifetime` (2 min) so rotating cloud endpoints are re-resolved; ChromaDB and Pinecone providers gained the missing `Dispose` (the client was never released) and all three providers are now `IDisposable`.
- **`CrewOutput.TokensUsed` is real telemetry for all 6 process types** (MAT-004, R10.8). Parallel/Hierarchical/Consensual/Autonomous now record token usage (new thread-safe `TokenUsageTally`), Graph propagates its existing internal count (success and circuit-breaker paths), and the prompt/completion split is extracted from `UsageDetails` instead of being discarded. **Breaking (0.9.0-beta)**: `TokensUsed` is now nullable — `null` means "not measured", never a fabricated `TokenUsage(0,0,0)`; the persisted checkpoint projection records `TokensMeasured` so the distinction survives round-trips.
- **Application placeholders implemented or removed** (MAT-018, R10.9). `CrewConfigurationMapper.ToConfiguration` exports agents and tasks for real (round-trip tested; **breaking**: the caller now provides the materialized entities); `CrewValidator` validates LLM configs (model + canonical numeric bounds); `CrewPlanner` consumes its previously-ignored `strategy` parameter; the stub `AgentPlannerService` is explicitly documented and logs at use. **Removed**: `IMemoryCoordinator.ClearTemporaryMemoriesAsync` (no production caller, no "temporary" marker in the model — the no-op could not be made honest).
- **`AddOrkeonA2A()`/`AddOrkeonInfrastructure()` call order no longer matters for the A2A agent directory** (ANT-019, R9.3). `IAgentRepository` is now registered with `TryAddScoped` by the infrastructure: when A2A ran first, its `SharedStoreAgentRepository` upgrade used to be silently won back by the later `AddScoped` (per-scope empty directory — ANT-001's failure mode, no crash). Side effect: a host repository registered **before** the Orkeon extensions is no longer shadowed by the in-memory default; hosts that override `IAgentRepository` **after** `AddOrkeonInfrastructure()` without `Replace` keep the last-wins behaviour as before.
- **Ctrl+C in TUI cancels the current command instead of being intercepted as SIGINT** (TUI-20) — `Console.TreatControlCAsInput = true` is set after `Application.Init` so Ctrl+C reaches Terminal.Gui's input loop. The .NET runtime's `Console.CancelKeyPress` hook is skipped in TUI mode (would race with the keystroke path). A global `Application.KeyDown` handler in `TerminalGuiHost` is the source of truth: 1×Ctrl+C requests `IInteractiveRunner.RequestCommandCancellation` (with REPL-pane feedback `⏹  Cancellation requested...`); 2×Ctrl+C within 2s force-quits the TUI as an escape hatch. `RunOneShotAsync` now accepts an `externalCt` parameter so `VerifyCommand` propagates the per-command CT into the inner crew kickoff.
- **Ctrl+Q during a running command shows a confirmation dialog** — uses `MessageBox.Query` (Terminal.Gui's idiomatic modal). The previous custom `QuitConfirmDialog` exposed a v2 runnable-stack race that swallowed the post-dialog `Application.RequestStop`.
- **Inner-host logs (RunOneShotAsync) leaked to stdout in TUI mode** (TUI-19) — when `verify` spawned a child host with `AddSimpleConsole`, those writes hit `System.Console.Out` (commandeered by Terminal.Gui's alt-screen) and dumped on shutdown. New `Orkeon.Cli.Abstractions.Logging.AmbientLoggerProvider` is published by the TUI host on Initialize; `RunnerExecution.ConfigureVerboseLogging` resolves it via reflection (no project coupling) and substitutes it for `AddSimpleConsole` when present. Inner host logs now flow into the logs pane.
- **Default `LoggerFactory` minimum level too restrictive in TUI** — runner now sets `LogLevel.Trace` upstream so all entries reach `TerminalGuiLoggerProvider`; visible filtering is owned by the provider (toggled at runtime via F2 / Shift+F2 in the status bar).
- **Gray-on-gray invisible text + missing show/hide logs** — Terminal.Gui default scheme paints fg and bg in the same gray, so panes appear empty until you select with the mouse. New `Orkeon.Cli.TerminalGui.Layout.SchemeFactory` builds explicit white-on-black schemes wired into LogsPaneView/ReplPaneView/SplitPaneToplevel. `Ctrl+G` now toggles the logs pane visibility (REPL fills the screen when hidden).
- **Initial focus landed on the read-only logs pane instead of the prompt input** (TUI-16) — `_history.CanFocus = false` removes it from the focus cycle, plus an `Initialized` hook on `ReplPaneView` calls `_input.SetFocus()` once the view is laid out so typing works immediately without clicking.
- **Status bar shortcuts swallowed by focused TextField** (TUI-17) — every Shortcut now sets `BindKeyToApplication = true` so keystrokes route at app level regardless of focus.
- **Ctrl+Q hung the host when the runner was blocked in `Console.ReadLine`** (TUI-17) — sync-over-async wrapper passed `CancellationToken.None`. `ReplPaneView.CancelPendingRead()` is invoked from the host's finally block to forcibly complete the pending TCS with `null` so the runner's loop sees `ReadLine() == null` and exits.
- **Hosts hung when the runner ignored cancellation during a long command** — added a 2s grace period after `linkedCts.Cancel()`; if the REPL task doesn't honour cancellation within the window, it's abandoned (orphaned task finishes in background, host exits cleanly).
- **Terminal.Gui split-pane silently exited at startup** (TUI-14) — `TerminalGuiHost(options, loggerProvider)` formed a cycle with the `TerminalGuiLoggerProvider` DI factory; `.Host.Build()` exited with code 0 before any UI was rendered. `TerminalGuiHost` now takes only `TerminalGuiOptions`; the status bar is built and attached from the `TerminalGuiLoggerProvider` factory once both exist. Also: explicit `DOTNET` driver on Linux/macOS (TUI-13), since the default driver emits no output in WSL.

### Added

- **Bilingual documentation parity gate** (OSS-012, R8.4). `scripts/check-docs-parity.sh` fails CI when any `docs/**.md` lacks its `docs/fr/` mirror (or vice versa), or when a root `README`/`CONTRIBUTING`/`CODE_OF_CONDUCT`/`SECURITY` `.md` lacks its `.fr.md` pair; wired as the `docs-parity` job. The rule is documented in both `CONTRIBUTING` files.
- **Blocking npm vulnerability audit** (DEP-010, R8.6). `dependency-audit.yml` gains an `npm-vulnerability-audit` job (`npm ci --dry-run` integrity + `npm audit --audit-level=high` over the esbuild bootstrap), so a CVE on `esbuild`/`@esbuild/*` fails CI instead of being an ignorable Dependabot PR. NuGet lockfile pinning was considered and deferred (bump friction outweighs the reproducibility gain already covered by Dependabot + the blocking audit).
- **Native tool calling for Azure OpenAI** (FON-011, R10.7). `AzureOpenAILlmProvider` is rebased on `OpenAICompatibleProviderBase` (its hand-rolled pipeline only read `choices[0].message.content`) and the factory passes the OpenAI strategy: `tools`/`tool_choice` are injected and `tool_calls` parsed natively, with the text fallback kept as the safety net. Ollama stays on the text protocol — its `/api/generate` pipeline is prompt completion and the native-tools `/api/chat` response format is incompatible with the OpenAI parser; documented in `docs/reference/limitations.md`.
- **`runCrew` script calls are bounded by a configurable timeout** (ANT-007/ANT-010, R10.10). Default 10 minutes (`ScriptHostFacadeOptions.RunCrewTimeout`, ≤ 0 disables): an infinite crew no longer freezes the REPL; scripts get a clear `TimeoutException`. `IConsoleAdapter` gains `ReadLineAsync`/`ReadKeyAsync` as non-breaking default interface methods, with real TUI overrides on the existing TCS machinery. The assumed-blocking design (Jint is synchronous) is documented in the scripting guide (EN+FR).
- **Terminal.Gui split-pane console (`Orkeon.Cli.TerminalGui`)** — new `IConsoleAdapter` + `ILoggerProvider` that route REPL I/O and `ILogger` writes into separate panes (logs on top, REPL on bottom), driven by Terminal.Gui v2.0.1. All 4 interactive runners (`ClaimVerifierRunner`, `MainMenuRunner`, `QaRunner`, plus the new `--ui` flag in `Orkeon.ConsoleApp` + `examples/runners/interactive-claim-verification`) accept `--ui tui|plain|auto`, default `auto` (TUI when interactive TTY, plain in CI/pipe via `TtyDetector`). Wire-up: `services.AddOrkeonCliTerminalGui()`. Full keybindings: Ctrl+L (clear logs), Ctrl+K (clear REPL), Ctrl+F (find), Ctrl+G (toggle logs pane), Ctrl+↑/↓ (resize split), F2 (more log details) / Shift+F2 (less log details), Ctrl+C (cancel current command — 2× to force-quit), Ctrl+Q (quit, with confirmation dialog if a command is running). TUI-18 tracks the Terminal.Gui 2.1.x re-evaluation follow-up.
- **`IInteractiveRunner` interface** in `Orkeon.Cli.Abstractions.Runners` exposes `IsCommandRunning` + `RequestCommandCancellation()`. `InteractiveRunnerBase` implements it; the TUI uses it to drive Ctrl+C cancellation and Ctrl+Q confirmation dialogs.
- **`AmbientLoggerProvider`** in `Orkeon.Cli.Abstractions.Logging` — process-wide ambient `ILoggerProvider` registry letting child hosts re-route their logs into a parent TUI's pane without project coupling (resolved via reflection by `RunnerExecution.ConfigureVerboseLogging`). Wrapped in a non-owning `LeasedLoggerProvider` so child host disposal doesn't kill the parent's provider.
- **TUI key event diagnostic** (`examples/runners/tui-keytest`) — small standalone runner that boots Terminal.Gui and logs every keystroke arriving at `Application.KeyDown` to `/tmp/tui-keytest.log`. Used to diagnose terminal-specific keystroke routing issues (TUI-20). Run with `dotnet run --project examples/runners/tui-keytest`.
- **Virtual FileSystem v2.2** — enumeration + streaming surface on `IFileSystemService` (`EnumerateFilesAsync`, `OpenReadStreamAsync`, `TryReadAllBytesAsync`, `TryReadAllTextAsync`, `GetEntryKindAsync`). `FileSystemDiscoverer` now goes through the VFS instead of raw `System.IO`.
- **Virtual paths across the RaggableTree pipeline** — `RaggableNode.FilePath` → `VirtualFilePath`, propagated through adapters, tools, DTOs (`SourceSlice`, `SymbolSourceResponse`), serializer (bumped to v2.0), and the store. Builder reads source via `IFileSystemService.TryReadAllTextAsync`.
- **Index introspection tools** — `index_status` and `is_path_indexed` let agents check which virtual roots are indexed and whether a given virtual path is covered (longest-match on overlapping roots). `IRaggableStore.GetIndexedRoots()` exposes the underlying list.

### Removed / Breaking

- **`ImprovedAgentExecutionService` renamed to `AgentExecutionService`** (SML-008, R12.4) — it is the only implementation of `IAgentExecutionService`, so the "Improved" qualifier was meaningless. The value object `Version` (which shadowed `System.Version`) is renamed `SemanticVersion`. Internal/pre-freeze renames in the 0.9.0-beta window. The dead `JsonToolCallParser` (`[Obsolete]`, no usage, no DI registration) and the empty `JsAgentInstance` placeholder are removed.
- **`--prebuild-index` CLI flag** removed from the standard runner. Agents now call `index_codebase(root_path="/src")` themselves (optionally gated by `is_path_indexed`). The flag pre-built an index the agent could not scope; letting the agent index what it needs, when it needs it, removed a whole-tree cost from every run.
- **Serialization format bump** — RaggableTree on-disk cache goes from v1.0 to v2.0 (field rename `FilePath` → `VirtualFilePath`). Existing caches will fail to load and need to be rebuilt.

## [0.9.1-beta] - 2026-07-04

Published to GitHub Packages only, without a dedicated changelog section at the
time; its changes are folded into the [0.9.2-beta] entries above. Recorded here
so the version chain has no gap. The `v0.9.1-beta.rc*` tags that followed
re-packed this unchanged version, so `--skip-duplicate` silently skipped every
push — the incident that motivated the tag↔version guard in the publish
workflow (see [0.9.2-beta]).

## [0.9.0-beta] - 2026-03-27

### Added

- **Typed pipeline architecture**: `ComponentBase<TRequest, TResponse>` as the core abstraction for all components, replacing `Dictionary<string, object>` signatures throughout the codebase
- **`ToolBase<TReq, TRes>`** generic tool base class with typed request/response, YAML defaults merging, and output filtering
- **`EvaluatorBase<TInput, TResult>`** and **`FlowStepBase<TInput, TOutput>`** typed base classes bridging interfaces with the typed pipeline
- **15+ built-in tools**: FileRead, FileWrite, WebScrape, HttpApi, JSON, CSV, PDF, XML, Database, GitHub, CodeExecution, and more in dedicated `Orkeon.Tools.*` projects
- **`SimpleCrewOrchestrator`** replacing the Akka.NET actor model with straightforward async/await orchestration
- **Semantic agent selection** using embedding-based similarity to match tasks to the most suitable agent
- **Strongly typed configurations**: `AgentConfiguration`, `TaskContext`, `LlmConfig`, and related value objects throughout Domain and Application layers
- **5 LLM providers**: OpenAI, Ollama, Anthropic, Azure OpenAI, and Groq — all HTTP-based implementations extending `HttpLlmProviderBase`
- **Memory providers**: Redis (with vector search), SQLite (long-term persistence), and InMemory (for development and testing)
- **Fluent Builder API**: `AgentBuilder`, `TaskBuilder`, `CrewBuilder`, and `FluentBuilderFactory` for ergonomic agent/crew construction
- **YAML configuration support**: full round-trip export/import for agents, tasks, crews, and tool schemas; `[FieldSchema]`, `[ComponentContract]`, and related attributes for schema generation
- **`ToolSchemaGenerator`**: auto-generates JSON/YAML schemas from typed `[FieldSchema]` attributes, with `$ref`-based nested type extraction
- **Tool validation framework**: security validation, rate limiting, and telemetry hooks on every tool execution
- **Batch tool execution** for parallel tool operations
- **Structured tool calling protocol** (JSON-based) with `ToolCallRequest<TParameters>` and `FunctionCallInfo`
- **CQRS pipeline**: commands and queries for Agent, Crew, and Task aggregates; `ValidatingCommandHandler` decorator; `UnitOfWork` integration for post-persistence domain event dispatch
- **Strongly-typed entity IDs**: 28 concrete `EntityId<T>` types (ULID-based) — `AgentId`, `TaskId`, `CrewId`, etc. — replacing primitive string identifiers
- **A2A (Agent-to-Agent) communication protocol**: `AgentCard`, discovery, `AgentCommunicationClient/Server`, `TaskRouter`, mTLS support, and DI integration (subsequently renamed to `AgentCommunication/`)
- **Session checkpointing**: `IStateStore`, `CheckpointManager`, `ResumeEngine` with three backing stores; time-travel checkpoint history with fork, replay, and diff
- **Cognitive memory system**: LLM-powered remember/recall with LanceDB embedded vector store support
- **Enterprise auth**: Azure AD and OIDC integration, claims-based authorization
- **Memory encryption at-rest**: AES-256-GCM encrypted Redis and SQLite decorators with key rotation
- **DLP (Data Loss Prevention)**: `PiiDetector` with 5-channel interceptors and per-channel policy
- **RAG data validation**: integrity checks, injection detection, provenance tracking, and quarantine
- **Agent kill switch**: `IAgentLifecycleManager` for controlled agent termination
- **`InMemoryAgentMemoryStoreRepository`** (Infrastructure) for fast in-process agent memory
- **E2E test project** (`Orkeon.Infrastructure.Tests`) with 10+ integration tests; `IConfiguration` wired into test DI container
- **XML documentation** on all public types across all projects (CS1591 enforcement enabled)
- **`ToolCallRequest<TParameters>`** generic typed tool protocol
- **Manual mock library** for LLM, Knowledge, Memory, Security, MCP, Process, and Tool interfaces — replaces Moq across the entire test suite
- **Clean Architecture + DDD audit reports** (ADRs) documenting architectural decisions and conformance

### Changed

- **Complete rename to Orkeon** (via the interim Arkeon name) across the entire codebase (solution file, namespaces, projects, docs, HTML, scripts, and examples)
- **Infrastructure layer redesigned** without Akka.NET: simple HTTP-based implementations, direct `async/await` service calls, standard dependency injection replacing the actor model
- **Domain encapsulation hardened**: private/internal constructors on all value objects and aggregate roots; `Restore()` factory methods for persistence; `IReadOnlyList<T>` replacing mutable `List<T>` on domain types
- **15+ anemic domain types converted** to immutable records (`init`-only properties)
- **Value objects refactored**: `AgentSkill`, `AgentCapability`, `AgentSelectionResult`, `CrewVariables`, `DomainValueObjects.cs` split into per-feature files; renamed duplicates (`MemoryEntity`, `TypedTaskContext`, `DelegationToolParameters`)
- **Domain reorganized feature-first**: Builders, Templates, Callbacks, DomainEvents, and ValueObjects moved to bounded-context folders; `TrainingScenario` moved to Training BC; `A2A/` renamed to `AgentCommunication/`
- **Application layer reorganized** feature-first: DTOs co-located with feature folders; dead infrastructure port interfaces removed; `KickoffAsync` extracted from `Crew` aggregate to the Application orchestrator
- **Infrastructure layer reorganized** by feature/BC: Persistence separated per-aggregate
- **`MemoryRelevanceRanker` renamed** to `MemoryRelevanceService`
- **`AgentStep` string ID replaced** with typed `AgentStepId`
- **`IRepository` simplified**: `GetAllAsync` and `IPredicateRepository` removed (AP4/R45)
- **`IAgent.Tools` typed** as `IReadOnlyList<IBaseTool>` (previously untyped)
- **CQRS handlers wired** into the ConsoleApp via the standard pipeline; DI lifetimes aligned
- **Test suite migrated** from Moq to manual mocks and from FluentAssertions to xUnit `Assert`; test methods renamed to `Should_When` convention; Handler-level directory organization
- **Trading tools migrated** to typed generic pipeline `TradingToolBase<TRequest, TResponse>`; tool definitions extracted to YAML
- **Examples updated** to use Fluent Builder API and Docker Model Runner LLM configuration
- **ComponentBase JSON serialization** extracted from Domain to Infrastructure (N1)
- **`ShouldRetain`/`ShouldPromoteToLongTerm`** made internal to enforce aggregate boundary (R35/R8)
- **`EntityMemory`/`EpisodicMemory`** made internal to the aggregate (R8/R35)
- **`MemoryItem` mutation methods** made internal (R35)
- **`AgentCapabilities`/`CrewOutput` constructors** privatized (R28)
- **`CrewInput` constructor** made internal (R10/R28)
- **`ToolResult`/`ToolUsageMetrics`** converted to init-only properties (R25)
- **Validation pipeline** wired in: `CreateTaskCommandValidator` handles `AgentId` validation; `ValidatingCommandHandler` decorates the CQRS chain
- **`UnitOfWork` try/finally guard** added to ensure domain events are dispatched after persistence even on exceptions (R21/R39)
- **`ITaskRepository`** scoped documented; `SaveChangesAsync` removed from repository, delegated to `IUnitOfWork`

### Fixed

- Infrastructure `ChatClient` adapter no longer overrides caller-supplied LLM configuration
- Empty task output in process strategies handled gracefully
- `IMemoryScope` and `IMemoryProviderFactory` registered in DI (defaults to `NullMemoryScope`)
- LLM DI registration corrected across all examples
- LLM `BaseUrl` changed from `host.docker.internal` to `localhost` in examples
- Missing `AgentBuilder`/`CrewTaskBuilder` `using` directives in email-management and research-assistant examples
- ClassicTrading example: raw strings wrapped with Value Object factories; `Agent.AgentId` renamed to `Agent.Id`; namespace corrections for `LlmConfig` and `ILlmProvider`
- Duplicate `MemoryProviderConfigDto` removed (kept in `Memory/`, removed from `Common/`)
- Duplicate `MemoryRelevanceRanker` stale reference cleaned up (R43/R50)
- Domain event dispatch order standardized; DI lifetimes aligned (N5/N6)
- `PromptShieldBuilder` tests fixed after `AgentBackstory` VO migration (null backstory handling)
- Post-refactoring compilation errors resolved across Infrastructure project
- CS4014 warning: async Timer callback wrapped with `try/catch` and discarded correctly
- Null safety and structured logging fixes (CS8604, CA1873) across Application and Infrastructure
- Code quality fixes: cognitive complexity (S3776), unused parameters (S1172), collapsed ifs (S1066), assertion improvements (xUnit2013, xUnit2032), and more

### Removed

- **Akka.NET dependency** and all actor-model code (cluster sharding, distributed data, CRDT, `CollaborationActor`)
- **All TODO comments** from the codebase
- **`sonar-project.properties`** file (caused scanner conflicts; all parameters now passed via CLI)
- **Legacy `CodeInterpreterTool`** (replaced by `SecureCodeInterpreterTool`)
- **`GetAllAsync` and `IPredicateRepository`** from `IRepository<T>` (AP4/R45)
- **5 dead infrastructure port interfaces** from Application layer (R16)
- **Moq and FluentAssertions** package references from all test projects
- **`[Obsolete]` `FunctionCall` dictionary property** replaced by `FunctionCallInfo` (T17)
- **Telemetry infrastructure** (`StartSpan` → migrated to `StartActivity`; unused telemetry packages removed)
- **Direct Domain usings** from ConsoleApp services (R51)

---

## [0.1.0-alpha] - 2025-09-21

Initial public development snapshot. Core domain model established in C# following Clean Architecture principles, as an independent implementation.

### Added

- Initial solution structure: `Domain`, `Application`, `Infrastructure`, `ConsoleApp` projects
- Core domain entities: `Agent` (Worker, Manager, Observer, Human), `Crew`, `CrewTask`
- `IBaseTool` interface and initial tool implementations
- `ILlmProvider` with OpenAI and Ollama HTTP implementations
- Basic memory abstractions (`IMemoryProvider`)
- Initial Akka.NET actor-based agent execution (later replaced)
- Communication protocols: Direct, Broadcast, Consensus, Feedback
- Redis memory provider with vector search
- SQLite persistence for long-term memory
- ClassicTrading example (agent crew for trading workflows)
- Standalone mode (no Redis required)
- Console application entry point

[Unreleased]: https://github.com/Orkeon/orkeon/compare/v1.0.0-rc.4...HEAD
[1.0.0-rc.4]: https://github.com/Orkeon/orkeon/compare/v1.0.0-rc.3...v1.0.0-rc.4
[1.0.0-rc.3]: https://github.com/Orkeon/orkeon/compare/v1.0.0-rc.2...v1.0.0-rc.3
[1.0.0-rc.2]: https://github.com/Orkeon/orkeon/compare/v1.0.0-rc.1...v1.0.0-rc.2
[1.0.0-rc.1]: https://github.com/Orkeon/orkeon/compare/v0.9.2-beta...v1.0.0-rc.1
[0.9.2-beta]: https://github.com/Orkeon/orkeon/compare/v0.9.1-beta.rc1...v0.9.2-beta
[0.9.1-beta]: https://github.com/Orkeon/orkeon/compare/v0.9.0-beta...v0.9.1-beta.rc1
[0.9.0-beta]: https://github.com/Orkeon/orkeon/compare/v0.1.0-alpha...v0.9.0-beta
[0.1.0-alpha]: https://github.com/Orkeon/orkeon/releases/tag/v0.1.0-alpha
