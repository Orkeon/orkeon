> 🇫🇷 [Version française](../fr/architecture/run-event-bus.md)

# The run event bus

**Scope**: `orkeon run --events jsonl`, the protocol it speaks, and the seat it gives a watching process at the run's [EventHub](event-hub-and-crew-lifecycle.md).
**Audience**: anyone driving Orkeon from another program — a desktop front-end, a gateway, a CI job, your own tooling.

A run without `--events` prints text for a human. A run with it speaks a versioned protocol: one JSON document per line on stdout, one per line on stdin coming back. Nothing else changes — the same crew, the same host, the same exit codes.

---

## 1. Why a protocol rather than parsed output

Text meant for a person changes when someone improves the wording. A program reading it breaks on a comma. The protocol exists so the two audiences stop sharing a channel: humans keep the rendering, programs get a contract.

The contract is **the outbound stream**. The inbound one is deliberately tolerant: a malformed line, an unknown verb, a payload the run cannot use are ignored rather than fatal. A client that sends nonsense should not stop a crew that was working.

---

## 2. The envelope

Every outbound line is an object carrying these keys, and only these as reserved names:

| Key | Meaning |
|---|---|
| `v` | Protocol version. Currently `2`. |
| `seq` | Monotonic sequence number within the run, starting at 1. |
| `ts` | ISO-8601 UTC timestamp. |
| `kind` | What happened (see §3). |
| `crewId`, `agentId` | Who it concerns, when the run knows. |
| `correlationId` | Ties a question to its answer, a request to its reply. |
| `causationId` | The event that caused this one, so a client can rebuild the tree. |

**An absent key is omitted, never written as `null`.** A client treats every identity field as optional.

**`v` moves only for a change an existing client would misread** — a field removed, renamed, or given another meaning. Adding is not such a change: a client ignores the kinds and fields it does not know (§6), so a new kind or a new payload field keeps `v: 2`.

The eight names above are **reserved**: a payload field colliding with one is dropped rather than allowed to impersonate the envelope. This is why the human-input payload names its field `inputKind`.

Payload fields sit **flat** beside the envelope, not nested under a `payload` key. The one exception is `hub.message`, whose payload is opaque to the protocol and is carried as-is.

---

## 3. Outbound — what a run says

The vocabulary is declared once, in the dependency-free `Orkeon.Constants.Protocol` satellite ([ADR-009](../adr/ADR-009-shared-constants-satellites.md)): `RunEventKinds` names the nineteen kinds — the eighteen below and the inbound `input.given` (§4) — and `RunEventKinds.All` is the set a client asserts it handles. The CLI writes them and a client reads them without either referencing the other. A C# host reads the same words in process: the events `ICrewOrchestrationService.KickoffStreamingAsync` yields carry these kinds — `task.started`, `tool.called`, `tool.returned`, `llm.delta`, `task.completed`, `error`, `run.finished` (with the run's `CrewOutput`) — and the error codes of `Orkeon.Constants.Protocol.RunEventErrorCodes`: `crew_failed` (a task did not succeed, or the run stopped on an error) and `crew_cancelled` (a timeout, Ctrl+C, a consumer that left the stream). The CLI's own stream stays on the execution hook and the meter: it does not go through that API.

| `kind` | Payload | When |
|---|---|---|
| `run.started` | `target`, `stream` | The run begins. |
| `task.started` | `taskId`, `agentRole`, `attempt` | A task begins, in every orchestration mode — the moment its agent is chosen and before it is asked anything. Between this and the matching `task.completed` a watcher shows the task as in progress; the payload is deliberately thin because nothing has been measured yet. A client must not refuse a `task.completed` it never saw started. `attempt` (integer, always present) is 1 for the first run of the task and 2, 3, … for its retries ([Task retry](yaml-schema.md#task-retry)): a task with a `retry:` announces itself again at each retry, and a client treats a repeated `task.started` for a task already in progress as the same task. |
| `task.completed` | `taskId`, `agentRole`, `success`, `skipped`, `resumed`, `attempt`, `durationMs`, `tokens`, `toolCalls`, `skipReason`, `defaulted`, `isDecision`, `decision` | Each task finishes, in every orchestration mode — failure and cancellation included (see `error`). `attempt` (integer, always present) is the attempt that ended the task: 1 when it ran once, 3 for a task whose third attempt it is ([Task retry](yaml-schema.md#task-retry)); a task that never ran says 1. `resumed: true` marks a task of a resumed run (`orkeon run --resume`) whose result came back from the checkpoint: no model was asked for it (`false` otherwise, always present, in every orchestration mode). `skipped: true` marks a task that never ran, either because a task it depends on did not succeed or because its `when` condition did not hold (`success` is then `false` and `durationMs` `0`). `tokens` and `toolCalls` are `0` when the mode does not track them, never absent. `skipReason` (string or `null`) says why a skipped task did not run: the dependency that did not succeed, or the `when` condition that did not hold ([ADR-016](../adr/ADR-016-decision-tasks-and-conditional-fan-out.md)); it is `null` unless `skipped` is `true`. `isDecision` (boolean) is `true` for a decision task, which has no agent; `decision` is then its answers as a compact JSON string, and `null` otherwise. `defaulted` (boolean) is `true` for a decision task that answered from its fallback because the decision circuit is off (`--no-decision`, `Orkeon:Decision:Enabled=false` or no decision model configured). All four are always present. |
| `tool.called` | `toolName`, `argsSummary?` | A tool is invoked. `argsSummary` is a digest of the argument names, never the arguments: a call can carry a whole file. |
| `tool.returned` | `toolName`, `success`, `durationMs` | The tool finished — **including when it threw**, so a watcher never shows a step running forever. Correlated with its `tool.called`. For a fanned-out turn (parallel read-class calls), the `tool.called` events of a parallel group may all precede their `tool.returned` events, and the `tool.returned` events follow the call order. |
| `delegation.started` | `toRole?` | One agent handed work to another (the `delegate_work_to_coworker` tool). The task description stays off the stream, like every other argument value. |
| `agent.spawned` | `role?`, `reason?` | The team grew at runtime — emitted when a `spawn_agent` tool call is observed. The framework wires that tool into no agent by default, so this kind only appears in deployments that attach it themselves. |
| `graph.node.entered` | `node`, `visit`, `task?`, `graph?` | A graph engine is about to run a node, after its circuit breaker let it through. `visit` counts the entries of that node in the run (1 on the first, 2 when a loop or a retry comes back). A crew in the Graph process names the task (`task`: its id; `node`: its name), a script's `stateGraph` names the graph (`graph`). A reader that does not know the kind ignores it. |
| `cost.updated` | `tokens`, `promptTokens`, `completionTokens`, `cacheHitTokens?`, `cacheMissTokens?`, `estimatedTokens?`, `model?`, `provider?`, `operation?`, `cost?`, `currency?`, `costSource?` | The meter moves — after **every generation call** of the run, while it goes: agent turns and their retries, the hierarchical manager, the planner, the RAG pipelines, the memory services, LLM judges, `ctx.llm.*` calls (see *What the meter counts* below). Every figure is the run's cumulative total, like `run.finished`'s: `promptTokens` is what went up, `completionTokens` what came back, and the cache pair (a partition of the prompt side) appears once a provider measured it — unmeasured is absent, never `0`. `estimatedTokens` appears once a provider counted nothing for a call and the runtime estimated it: the part of `tokens` that is approximate, which a client marks rather than passes off as a count. `model` and `provider` say who answered, and `operation` what the call was for — `agent`, `manager`, `planning`, `rag`, `memory`, `judge`, or the `ctx.llm.*` method of a script (`complete`, `chat`, `stream`, `extract`, `decide`, `act`) — absent for a call nothing claimed: a client that names the model a team works on reads its agents' readings, not a judge's or a RAG pipeline's; the envelope carries `crewId` and, as `agentId`, the role of the agent the call was made for — absent for a call no agent made, such as the plan. **The price is the vendor's own or nothing**: `cost` appears once a vendor billed in its answer (OpenRouter's `usage.cost`), with `costSource: "vendor"` and `currency` (ISO 4217) when the provider states it; a free call bills `0`, relayed as `0`. No estimate ever travels here — the framework's price registry serves budgets, and a figure computed from it would read as a bill. |
| `llm.delta` | `deltaKind`, `text` or `toolCall` | A fragment of what the model generates, as it arrives: each agent turn of a crew (YAML, crew directory or declarative `.ork.ts`) and each `ctx.llm.*` call of a script. `deltaKind` is `content` (the text, in `text`) or `tool_call` (a fragment of a tool call the model is building, in `toolCall: {index, id?, name?, argumentsDelta}`: `id` and `name` come on the fragment that carries them, `argumentsDelta` is a piece of the JSON arguments; Ollama sends a call whole, as one fragment). A consumer that reads `text` ignores a `tool_call` fragment; the final tool call is still the `tool.called` event. A host that registered DLP (`AddOrkeonDlp`) gets the arguments of each call held until the call ends, scanned once and sent as one redacted `tool_call` fragment. The fragments are held with the text while the guardian judges the output. **Only under `--stream`** — without it the turns are not streamed at all. |
| `llm.call.started` | `callId`, `mode`, `estimatedPromptTokens` | A model call was sent, by any caller the meter counts: `mode` is how it goes on the wire (`stream` or `buffer`, the setting's `CallMode` resolved), and `estimatedPromptTokens` what the runtime estimates went up. Written whether or not `--stream` is on. |
| `llm.call.progress` | `callId`, `estimatedCompletionTokens` | A streamed call received more: the cumulative estimate of what came back. At most one line per call every 250 ms, and always the last one before `llm.call.finished`; a buffered call writes none. |
| `llm.call.finished` | `callId`, `mode`, `fellBack`, `promptTokens?`, `completionTokens?` | The call ended, answered or failed: `mode` is the mode actually used, `fellBack` says a refused stream was redone in buffer, and the counts are the provider's when it gave them. A client drops the call's estimate when this line carries the provider's count, which the `cost.updated` that follows adds to the cumulative figures, and keeps it, marked as estimated, when none comes. |
| `llm.fallback` | `profile`, `from`, `to`, `reason` | A profile's [fallback chain](llm-providers.md#profile-fallback) moved a call to the next profile: `profile` is the one a crew named, `from` the profile that failed, `to` the one the call goes to, `reason` the class of the failure (`server_error`, `rate_limited`, `timeout`, `transport`, `exhausted`). One line per switch, so a chain of three can write two for one call. The metered `llm.call.*` lines and `cost.updated` name the provider that answered. |
| `input.needed` | `inputKind` (`text`\|`confirm`\|`choice`\|`multi`\|`tool_approval`), `prompt`, `requestId?`, `choices?`, `defaultValue?`, `taskDescription?`, `taskKey?`, `toolName?`, `suspending?` | A task declared `humanInput: true` is asking, or the permission gate asks about a tool call. With `Orkeon:Checkpointing:Enabled` the question is **durable** (§ 4): `requestId` (`<task>#<n>.<q>`, the same on every resume) names it, the correlation id of the event is that id, and `suspending: true` says the run stops on it. |
| `hub.message` | `from?`, `topic?`, `payload?`, `expectsReply?`, `messageId?`, `metadata?` | The run's hub relayed something to this process. `from` is the sender's own hub address (`agent://{crew}/{agent}`, `crew://{crew}`) so the peer can attribute and answer; it is absent when the hub does not know (the answer to a `send`, which pairs by `correlationId` instead). `expectsReply: true` appears only on an agent's `send`: the sender is blocked awaiting a `reply` under its own timeout, and silence past it is a refusal. The peer must not have to guess which correlated lines are questions — a topic relay can carry a `correlationId` too. `messageId` is the hub message's id — on an agent's `post`, the id its `post_message` returned — and `metadata` the envelope's string pairs, written only when there are any; the answer to a `send` carries neither. |
| `error` | `code`, `message`, `recoverable` | Something went wrong. A run that stops — cancelled or failed, in any mode, and before its first task too (a memory that cannot work, a plan whose provider failed) — ends with `code: crew_cancelled` or `crew_failed` before `run.finished`. |
| `run.finished` | `success`, `exitCode`, `status?`, `requestId?`, `tokens`, `durationMs`, `promptTokens`, `completionTokens`, `cacheHitTokens?`, `cacheMissTokens?`, `estimatedTokens?` | The run ends, with what it spent: the token total and its split — the sum of every `cost.updated` reading, so the two cannot disagree — the wall time, the cache pair when a provider measured it, and the estimated part when a provider left a call uncounted. |

A run that reports nothing is not a run going well — it is a run reporting nothing. A client should show the difference rather than hide it.

### What the meter counts

Every generation call of the run, measured in one place: the provider factory wraps every provider it builds in a metering decorator, and a host that registers a provider of its own does it through `AddOrkeonLlmProvider`, which wraps it the same way. A call is therefore counted by the provider it went to, whoever made it — never by the caller, never twice.

Each reading says whose call it was. The orchestrator opens a scope for every task — the crew, the agent, the task — and the part of the run that makes a call names the work, which the reading carries as `operation`: `agent`, `manager`, `planning`, `rag`, `memory`, `judge`, or the `ctx.llm.*` method. A RAG pipeline queried by an agent's tool stays that agent's. A call nothing claimed is counted all the same — `unattributed` in the engine's own reports, and no `operation` on the wire.

Two things are not on the meter: **embedding calls**, which are not generation calls, and an `IChatClient` a host registers that is not built on an Orkeon provider. A text-only stream carries no usage at all: it is counted when it ends, as an estimate, under `estimatedTokens` — and an estimate is never priced. See [Known limitations](../reference/limitations.md).

---

## 4. Inbound — what a watching process can say

One JSON document per line on stdin. Each verb maps onto a member of `IEventHub`, except the human answer:

| `kind` | Payload | Effect |
|---|---|---|
| `input.given` | `correlationId?` (or `requestId?`), `value` | Answers a pending `input.needed`. Also outbound: `{requestId}` when a held run received its answer (`POST /runs/{id}/input`) or a resumed run was given one (`--input`). |
| `post` | `to`, `payload` | `IEventHub.PostAsync` — writes to one mailbox. |
| `send` | `to`, `payload`, `timeoutMs?`, `correlationId?` | `SendAsync` (`timeoutMs` defaults to 30 s); the answer comes back as a correlated `hub.message`. |
| `publish` | `topic`, `payload`, `retainAs?` | `PublishAsync`. |
| `reply` | `correlationId`, `payload` | Answers a question **an agent asked this process**. |
| `subscribe` / `unsubscribe` | `topic` | Opens or closes a relay of that topic into `hub.message`. |

An `input.given` **without** a correlation id answers the oldest question waiting: a human typing into a terminal has no identifier to quote. A durable question's id is its `requestId`: `correlationId` or `requestId` may carry it.

**A question that outlives the process.** With `Orkeon:Checkpointing:Enabled`, a question to a human is persisted with the run's checkpoint session (`PendingInput`, session phase `WaitingForInput`) under a stable id, `<TaskKey>#<n>.<q>` (`n` the invocation of the task, `q` the question within it). `Orkeon:HumanInput:Pause` says what the run does while nobody has answered: `Hold` waits, up to `HoldTimeout`, then suspends; `Suspend` stops at once. Unset, a run with a person to ask (a terminal, a stream a client reads) holds, one with nobody (a plain `orkeon run`) suspends; the daemon and `--events` hold by default (30 minutes, none). A suspended run ends with `run.finished` `status: "waiting_for_input"`, `exitCode: 75`, `success: false` and the question's `requestId`: it is neither a success nor a failure. It resumes with `orkeon run <crew> --resume <id> --input "..."`: the task that asked is replayed (granularity: the task), asks the same question under the same id, and finds the answer; the prompt shown is the one persisted the first time, never recomputed, and the answer is matched by `requestId` alone. The first answer wins.

A hub command that arrives before the run's hub exists is held — up to 64 of them — and replayed once it does; beyond that it is dropped, like any other line the run cannot use.

### Silence is not consent

Without `--events`, a task declared `humanInput: true` is **auto-approved** — a defensible fallback for an unattended run, and the wrong answer entirely once a screen is watching. Asking for the protocol replaces that provider: the question goes on the stream and the run waits.

If no answer comes — channel closed, run cancelled — the confirmation is **refused**, never granted.

---

## 5. The seat at the hub

A watching process is addressable at `client://{name}` (`--client`, default `client`; the name must be non-empty and hold no `/`, or the run refuses to start). Agents post and send to it exactly as they would to another agent, and it can post, publish and subscribe back.

**Who may reach it is the crew's decision**, not the protocol's. A crew authorizes the exchange by naming the peer in its `links:` block:

```yaml
name: billing-crew
links:
  - to: "client:client"
    direction: bidirectional
    allowed_topics: [run.progress]
```

Without a declared link the ACL's default policy lets traffic through, so a crew that declares nothing keeps working — but a crew that declares any link is held to what it declared. The full rules are in [EventHub §10](event-hub-and-crew-lifecycle.md).

---

## 6. Driving a run from another program

```bash
orkeon run crew.yaml --events jsonl --client my-watcher
```

Read stdout line by line, parse each as JSON, switch on `kind`. Write answers and commands to stdin, one JSON document per line, flushed. Three facts a driver can rely on:

- **Both dialects speak it.** A `.ork.ts` target is observed through the same seams as a YAML crew — the tools, the token meter, the hub bridge and the human-input provider all flow into the script host.
- **stdout is the protocol and nothing else.** On an observed run every log line goes to stderr; a log between two JSONL documents would be a parser error on your side. A command line the CLI refuses writes nothing on stdout: the stream does not open, the refusal is one line on stderr and the exit code is `1`.
- **`jsonl` is the only value `--events` accepts**, and it says so rather than guessing; `--client` without `--events` warns instead of being silently ignored.

A minimal exchange:

```
→ {"v":2,"seq":1,"ts":"…","kind":"run.started","target":"crew.yaml","stream":false}
→ {"v":2,"seq":2,"ts":"…","correlationId":"c-1","kind":"input.needed","inputKind":"confirm","prompt":"Publish the report?"}
← {"kind":"input.given","correlationId":"c-1","value":"yes"}
→ {"v":2,"seq":3,"ts":"…","kind":"task.started","taskId":"t1","agentRole":"writer","attempt":1}
→ {"v":2,"seq":4,"ts":"…","kind":"cost.updated","crewId":"01K…","agentId":"writer","tokens":1840,"promptTokens":1600,"completionTokens":240,"model":"google/gemini-3.7-flash","provider":"openrouter","operation":"agent","cost":0.0021,"currency":"USD","costSource":"vendor"}
→ {"v":2,"seq":5,"ts":"…","kind":"task.completed","taskId":"t1","agentRole":"writer","success":true,"attempt":1,"durationMs":4200,"tokens":1840,"toolCalls":3}
→ {"v":2,"seq":6,"ts":"…","kind":"run.finished","success":true,"exitCode":0,"tokens":1840,"durationMs":4300,"promptTokens":1600,"completionTokens":240}
```

Two rules worth building against. **Ignore a `kind` you do not know** — a newer Orkeon says more than an older client understands, and crashing on an unread line is worse than showing a little less. And **keep what you could not parse**: a line that is not protocol is still something the run said, and losing it loses the diagnosis.

---

## 7. What reads this today

- **Desktop and terminal front-ends** read the stream to show progress — the task in progress and the tool at work, from `task.started` and `tool.called`, as well as the tasks done — cost and the run's questions instead of scrollback. Such a client can also staff the hub seat: an agent's `send` to `client://<name>` (marked `expectsReply`) appears as a request the user answers, and the reply travels back down stdin; hub posts are listed rather than dropped. Silence past the agent's own timeout is still a refusal — the same rule silence follows everywhere on this bus — the client just gives a human the chance to speak before it. `RunClient`-style peers that take the seat headlessly follow the same shape: subscribe, post, and reply.

The same envelope carries [the Atelier](../reference/cli.md#orkeon-forge)'s own stream and the [`orkeon usecases`](../reference/cli.md#orkeon-usecases) session (`usecases search --events jsonl`, whose kinds `UseCaseEventKinds` declares beside `RunEventKinds`), so a client that reads one reads them all — and the sign-in of an e-mail account, [`orkeon email login --events jsonl`](../reference/cli.md#orkeon-email), whose kinds `EmailEventKinds` declares (five `email.*` kinds and `error`).

## 8. Where the events go: sinks and the run history

The observers of a run (`RunEventObserver`, `ObservedTool`, `RunEventsLlmCallObserver`, in `Orkeon.Hosting`) write to one
`IRunEventSink` (`Orkeon.Application.Runs`, experimental `ORKEXP007`), a `RunEventPublisher`. The publisher stamps each event
once (`seq`, `ts`, the run identity) and hands it to every destination, so every destination holds the same lines:

- **`StdoutJsonLinesRunEventSink`** (the CLI): the stream of `orkeon run --events jsonl`. Its bytes are pinned by a
  golden test (`StdoutEventContractGoldenTests`). The other streaming verbs (`forge`, `email`, `drive`, `usecases`)
  keep their own writer, which shares the envelope code and the model-call observer.
- **`JsonLinesFileRunEventSink`** (`Orkeon.Hosting`): `<run id>.jsonl` and an `index.jsonl` under the `/runs` mount, through the
  VFS, one serialized writer per run.
- **`SqliteRunEventSink`** (`Orkeon.Infrastructure`): tables `runs` and `run_events`, WAL, in `runs.db` under the same mount.

A destination that fails is logged and skipped: observing a run never changes its outcome. The history is the section
[`Orkeon:RunHistory`](../reference/configuration.md#orkeonrunhistory) (off in the library, on in `orkeon-host` and the
`orkeon init` template). Its settings are read when the host is built, after `run.started` was written, so the publisher keeps
the first events and replays them to the history when it joins. `llm.delta` stays on the stdout stream and is kept only with
`IncludeDeltas`. A purge applies `RetentionDays` and `MaxRuns` when a process first writes and when a run finishes, never when the history is only read. Several processes write one folder, so the JSONL index is only ever appended to (a purge appends a tombstone line instead of rewriting it), a run file the index lost is still listed, summarised from its own events, and the file system the store writes through is its own, because the closing event of `orkeon run` is written after its host is disposed. Secrets are masked (`RedactingRunEventSink`, the logs' own masking) before a file or Sqlite sink writes; stdout is not. A run resumed under its own id continues the sequence the history holds for it (the second `run.started` carries `resumed: true`), so `seq` stays strictly increasing within a run id. `run.started` is recorded with the pid and host of the writing process: a run still `running` whose process is gone from the same host is read as `orphaned`.

`IRunHistoryReader` (`ListAsync`, `GetAsync`, `ReadEventsAsync`) reads a run back; [`orkeon runs`](../reference/cli.md#orkeon-runs)
prints it as the lines of the stream. `orkeon-host` records each hosted run under the run's `ExecutionId`, which is also the daemon's own run id (one id
end to end: the host API, A2A, the history and the checkpoints name the run by it); `hostRunId` in `run.started` repeats it; the task events and the closing event are recorded, the `tool.*`,
`llm.call.*` and `cost.updated` events of a hosted run are not (see [limitations](../reference/limitations.md)). A reader
ignores a `kind` it does not know, and the history stores it untouched.
