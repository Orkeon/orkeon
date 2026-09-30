> 🇫🇷 [Version française](../fr/reference/a2a-conformance.md)

# A2A protocol — conformance matrix

Honest position of Orkeon's A2A implementation (marked `[Experimental]`,
diagnostic `ORKEXP001`) against the **A2A specification v1.0** (Linux
Foundation / a2aproject). Summary: the implementation belongs to the **0.x
era** of the protocol — a compact REST surface plus SSE streaming — and covers
the core send/stream/status/cancel loop; the v1.0 binding formalism (JSON-RPC,
gRPC, HTTP+JSON mappings of the canonical proto schema), push notifications
and the richer task lifecycle are not implemented. This page is the reference
for what interoperates and what does not.

## Abstract operations (spec §core)

| v1.0 operation | Orkeon endpoint | Status | Notes |
|---|---|---|---|
| Send Message | `POST /a2a/tasks/send` | 🟡 Partial | 0.x-era shape (`A2ATaskRequest`: id/skillId/input/inputMode/metadata), not the v1.0 `Message`/`Part` data model. The shipped router **does not run the agent** — see [Task execution](#task-execution). |
| Send Streaming Message | `POST /a2a/tasks/sendSubscribe` (SSE) | 🟡 Partial | Working → final update → `[DONE]`; no `TaskStatusUpdateEvent`/`TaskArtifactUpdateEvent` typed events. |
| Get Task | `GET /a2a/tasks/{id}` | 🟡 Partial | **Real since PUB-08** when task persistence is enabled (`AddOrkeonA2ATaskPersistence()` over a checkpointing `IStateStore`): 200 with the recorded state, 404 for unknown ids. Without the opt-in: explicit `501` (never fabricated state). |
| List Tasks | — | 🔴 Absent | |
| Cancel Task | `DELETE /a2a/tasks/{id}` | 🟡 Partial | With persistence: 404 for unknown ids and the record flips to `Cancelled`. **Advisory only**: in-flight work is not interrupted. Without persistence: legacy acknowledgement. |
| Subscribe to (existing) Task | — | 🔴 Absent | Streaming exists only at submission time. |
| Push Notification Configs (create/get/list/delete) | — | 🔴 Absent | No webhook delivery. |
| Get Extended Agent Card | — | 🔴 Absent | Single public card only. |

## Data model

| v1.0 concept | Orkeon | Status |
|---|---|---|
| Task states (9: incl. `SUBMITTED`, `INPUT_REQUIRED`, `AUTH_REQUIRED`, `REJECTED`) | 5 states (`Pending`, `Working`, `Completed`, `Failed`, `Cancelled`) | 🟡 Terminal states map 1-1; the interrupted states have no equivalent. |
| `Message` / `Part` (text, file, data parts) | `input` string + `inputMode` MIME hint | 🟡 Text-first; no multi-part payloads. |
| Artifacts | `output` string | 🟡 Single textual output. |
| AgentCard | `GET /.well-known/agent.json` | 🟡 Served with name/description/skills; v1.0 fields (`capabilities`, `securitySchemes`, `securityRequirements`, `supportedInterfaces`, `signatures`) absent. |
| AgentCard discovery path (`/.well-known/agent-card.json`) | `/.well-known/agent.json` | 🔴 The v1.0 well-known path is not served; a client following the convention gets a 404 before any binding question arises. |
| `A2A-Version` service parameter | — | 🔴 Not read; there is no version negotiation. |

## Bindings

v1.0 defines three canonical bindings (JSON-RPC 2.0, gRPC, HTTP+JSON) mapped
from the proto schema. Orkeon speaks **its own 0.x-era REST dialect** — none of
the three canonical bindings — so v1.0-strict clients will not interoperate
without an adapter. Aligning on the HTTP+JSON binding is the natural first
step when this surface is promoted.

## Security

| Requirement | Orkeon | Status |
|---|---|---|
| Client identity verification | mTLS (fail-closed: `RequireMutualTls` refuses to start without a trust anchor; CA chain or pinned thumbprints; 403 on failure) + `AllowedAuthSchemes` (401) | 🟢 For the mTLS deployment model. 🟡 `AllowedAuthSchemes` only checks that the `Authorization` header uses an allowed scheme with a non-empty credential — it never validates the token, so it is a filter, not an authentication. |
| Agent card access | `GET /.well-known/agent.json` | 🟢 Public by design: the security checks apply to the task endpoints only. |
| `securitySchemes` declaration in the AgentCard | — | 🔴 Schemes are enforced but not advertised. |
| Certificate revocation | Not checked | 🟡 **Decision (PUB-08 T3): prefer short-lived certificates over CRL/OCSP.** The A2A mTLS trust model targets private CAs, where CRL/OCSP endpoints rarely exist and OCSP adds an availability dependency; a 24–72 h certificate lifetime bounds the exposure window with no new runtime dependency, and rotation already fits the existing options (a new client instance picks up the new PFX). CRL support stays out of scope until a deployment proves the need. |
| Push-notification webhook auth | — | 🔴 No webhooks. |

## Task execution

`A2ATaskRouter`, the `IA2ATaskRouter` the opt-in registers, picks the agent whose
role equals or contains the request's `skillId` (the card lists one skill per
available agent: id → `id`, role → `name`, goal → `description`, `text/plain` in
and out). It then answers `Completed` with a routing acknowledgement
(`Task routed to agent '<role>' with input: …`) — **it does not execute the
agent**. A host that wants the matched agent to work registers its own
`IA2ATaskRouter` before calling `AddOrkeonA2A` (the default is registered with
`TryAdd`, so the host's wins).

## Activation

Nothing in the shipped binaries turns A2A on: no `orkeon` command, `orkeon-host`
or the REPL calls it. An embedding host opts in with `AddOrkeonA2A(configuration)`
(sections `A2A` and `A2A:Security`) or `AddOrkeonA2A(configure, configureSecurity)`,
plus `AddOrkeonA2ATaskPersistence()` for durable task records, then resolves
`IA2AServer` and calls `StartAsync` itself — see
[Opt-in subsystems](./opt-in-subsystems.md).

| `A2A` key | Default | Effect |
|---|---|---|
| `EnableServer` | `false` | Registers `IA2AServer` (an `HttpListener` on `Host:Port`). |
| `Host`, `Port` | `http://localhost`, `5002` | The listener prefix. |
| `AgentName`, `AgentDescription`, `AgentVersion`, `Organization`, `ContactUrl` | `Orkeon`, `Orkeon A2A Agent`, `1.0.0`, —, — | The agent card. |
| `TimeoutSeconds` | `30` | Client request timeout. |
| `Enabled`, `RemoteAgents` | `true`, empty | Bound but read by nothing yet. |

`A2A:Security` carries `ClientCertificatePath`/`ClientCertificatePassword` (the
client's own certificate), `TrustedCertificateAuthorities`,
`TrustedClientCertificateThumbprints`, `RequireMutualTls` and `AllowedAuthSchemes`.

## Where this leaves consumers

- **Orkeon ↔ Orkeon** across processes/hosts: the wire works end to end — card,
  send, stream, status, cancel, mTLS — now including durable task status with
  the persistence opt-in; the work itself needs the host's own
  `IA2ATaskRouter` ([Task execution](#task-execution)).
- **Orkeon ↔ third-party v1.0 agents**: not yet — wait for (or contribute to)
  the HTTP+JSON binding alignment tracked by the PUB-08 follow-up.

Every gap above is deliberate scope, not an oversight: the surface is marked
`[Experimental]` (`ORKEXP001`) precisely so it can be reshaped toward v1.0
without a breaking-change debt. See also
[Experimental APIs](./experimental-apis.md) and
[Limits and constraints](./limitations.md).
