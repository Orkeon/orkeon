> 🇫🇷 [Version française](../fr/architecture/run-event-bus.md)

# The run event bus

**Scope**: `orkeon run --events jsonl`, the protocol it speaks, and the seat it gives a watching process at the run's [EventHub](event-hub-and-crew-lifecycle.md).
**Audience**: anyone driving Orkeon from another program — Studio, a gateway, a CI job, your own tooling.

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

The eight names above are **reserved**: a payload field colliding with one is dropped rather than allowed to impersonate the envelope. This is not theoretical — the human-input payload originally called its field `kind` and lost it, which is why the wire name is `inputKind`.

Payload fields sit **flat** beside the envelope, not nested under a `payload` key. The one exception is `hub.message`, whose payload is opaque to the protocol and is carried as-is.

---

## 3. Outbound — what a run says

| `kind` | Payload | When |
|---|---|---|
| `run.started` | `target`, `stream` | The run begins. |
| `task.completed` | `taskId`, `agentRole`, `success`, `durationMs`, `tokens`, `toolCalls` | Each task finishes, in every orchestration mode — failure and cancellation included (see `error`). `tokens` and `toolCalls` are `0` when the mode does not track them, never absent. |
| `tool.called` | `toolName`, `argsSummary?` | A tool is invoked. `argsSummary` is a digest of the argument names, never the arguments: a call can carry a whole file. |
| `tool.returned` | `toolName`, `success`, `durationMs` | The tool finished — **including when it threw**, so a watcher never shows a step running forever. Correlated with its `tool.called`. |
| `delegation.started` | `toRole?` | One agent handed work to another (the `delegate_work_to_coworker` tool). The task description stays off the stream, like every other argument value. |
| `agent.spawned` | `role?`, `reason?` | The team grew at runtime — emitted when a `spawn_agent` tool call is observed. rc.2 wires that tool into no agent by default, so this kind only appears in deployments that attach it themselves. |
| `cost.updated` | `tokens`, `model?`, `provider?` | The token meter moves. `tokens` is cumulative; `model` when the provider reports it; `provider` only on the scripting facade's calls. There is **no price field**: the framework has no price table, and inventing one would be worse than omitting it. |
| `llm.delta` | `text` | A fragment of generated text. **Only under `--stream`.** |
| `input.needed` | `inputKind` (`text`\|`confirm`\|`choice`), `prompt`, `choices?`, `defaultValue?`, `taskDescription?` | A task declared `humanInput: true` is asking. |
| `hub.message` | `from?`, `topic?`, `payload?`, `expectsReply?` | The run's hub relayed something to this process. `from` is the sender's own hub address (`agent://{crew}/{agent}`, `crew://{crew}`) so the peer can attribute and answer; it is absent when the hub does not know (the answer to a `send`, which pairs by `correlationId` instead). `expectsReply: true` appears only on an agent's `send`: the sender is blocked awaiting a `reply` under its own timeout, and silence past it is a refusal. The peer must not have to guess which correlated lines are questions — a topic relay can carry a `correlationId` too. |
| `error` | `code`, `message`, `recoverable` | Something went wrong. A run that stops — cancelled or failed, in any mode — ends with `code: crew_cancelled` or `crew_failed` before `run.finished`. |
| `run.finished` | `success`, `exitCode`, `tokens` | The run ends. |

A run that reports nothing is not a run going well — it is a run reporting nothing. A client should show the difference rather than hide it.

---

## 4. Inbound — what a watching process can say

One JSON document per line on stdin. Each verb maps onto a member of `IEventHub`, except the human answer:

| `kind` | Payload | Effect |
|---|---|---|
| `input.given` | `correlationId?`, `value` | Answers a pending `input.needed`. |
| `post` | `to`, `payload` | `IEventHub.PostAsync` — writes to one mailbox. |
| `send` | `to`, `payload`, `timeoutMs?`, `correlationId?` | `SendAsync`; the answer comes back as a correlated `hub.message`. |
| `publish` | `topic`, `payload`, `retainAs?` | `PublishAsync`. |
| `reply` | `correlationId`, `payload` | Answers a question **an agent asked this process**. |
| `subscribe` / `unsubscribe` | `topic` | Opens or closes a relay of that topic into `hub.message`. |

An `input.given` **without** a correlation id answers whatever question is waiting: a human typing into a terminal has no identifier to quote.

### Silence is not consent

Without `--events`, a task declared `humanInput: true` is **auto-approved** — a defensible fallback for an unattended run, and the wrong answer entirely once a screen is watching. Asking for the protocol replaces that provider: the question goes on the stream and the run waits.

If no answer comes — channel closed, run cancelled — the confirmation is **refused**, never granted.

---

## 5. The seat at the hub

A watching process is addressable at `client://{name}` (`--client`, default `studio`). Agents post and send to it exactly as they would to another agent, and it can post, publish and subscribe back.

**Who may reach it is the crew's decision**, not the protocol's. A crew authorizes the exchange by naming the peer in its `links:` block:

```yaml
name: billing-crew
links:
  - to: "client:studio"
    direction: bidirectional
    allowed_topics: [run.progress]
```

Without a declared link the ACL's default policy still lets traffic through — the hub shipped without an ACL, and refusing undeclared traffic would break every existing crew — but a crew that declares any link is held to what it declared. The full rules are in [EventHub §10](event-hub-and-crew-lifecycle.md).

---

## 6. Driving a run from another program

```bash
orkeon run crew.yaml --events jsonl --client my-watcher
```

Read stdout line by line, parse each as JSON, switch on `kind`. Write answers and commands to stdin, one JSON document per line, flushed. Three facts a driver can rely on:

- **Both dialects speak it.** A `.ork.ts` target is observed through the same seams as a YAML crew — the tools, the token meter, the hub bridge and the human-input provider all flow into the script host.
- **stdout is the protocol and nothing else.** On an observed run every log line goes to stderr; a log between two JSONL documents would be a parser error on your side.
- **`jsonl` is the only value `--events` accepts**, and it says so rather than guessing; `--client` without `--events` warns instead of being silently ignored.

A minimal exchange:

```
→ {"v":2,"seq":1,"ts":"…","kind":"run.started","target":"crew.yaml","stream":false}
→ {"v":2,"seq":2,"ts":"…","correlationId":"c-1","kind":"input.needed","inputKind":"confirm","prompt":"Publish the report?"}
← {"kind":"input.given","correlationId":"c-1","value":"yes"}
→ {"v":2,"seq":3,"ts":"…","kind":"task.completed","taskId":"t1","agentRole":"writer","success":true,"durationMs":4200,"tokens":1840,"toolCalls":3}
→ {"v":2,"seq":4,"ts":"…","kind":"run.finished","success":true,"exitCode":0,"tokens":1840}
```

Two rules worth building against. **Ignore a `kind` you do not know** — a newer Orkeon says more than an older client understands, and crashing on an unread line is worse than showing a little less. And **keep what you could not parse**: a line that is not protocol is still something the run said, and losing it loses the diagnosis.

---

## 7. What reads this today

- **Orkeon Studio**, whose Launch screen shows progress, cost and the run's questions instead of scrollback — see [Studio](studio.md). The Launch screen also staffs the hub seat: an agent's `send` to `client://studio` (marked `expectsReply`) appears as a request panel the user answers, and the reply travels back down stdin; hub posts are listed rather than dropped. Silence past the agent's own timeout is still a refusal — the same rule silence follows everywhere on this bus — the screen just gives a human the chance to speak before it.
- `Orkeon.Studio.Core.Run` — `RunClient` and `RunProgressModel`, a reference client in ~460 lines with no dependency on Infrastructure or any LLM. `RunClient` is the shape to copy for a peer that takes the seat headlessly: subscribe, post, and reply.

The same envelope carries [the Atelier](../reference/cli.md#orkeon-forge)'s own stream, so a client that reads one reads both.
