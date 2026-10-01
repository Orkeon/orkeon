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
| Send Message | `POST /a2a/tasks/send` | 🟡 Partial | 0.x-era shape (`A2ATaskRequest`: id/skillId/input/inputMode/metadata), not the v1.0 `Message`/`Part` data model. The `skillId` is the `id` of a skill on the agent card; the agent runs and the response carries its output — see [Task execution](#task-execution). |
| Send Streaming Message | `POST /a2a/tasks/sendSubscribe` (SSE) | 🟡 Partial | Working → final update → `[DONE]`; no `TaskStatusUpdateEvent`/`TaskArtifactUpdateEvent` typed events. |
| Get Task | `GET /a2a/tasks/{id}` | 🟡 Partial | **Real since PUB-08** when task persistence is enabled (`AddOrkeonA2ATaskPersistence()` over a checkpointing `IStateStore`): 200 with the recorded state, 404 for unknown ids. Without the opt-in: explicit `501` (never fabricated state). |
| List Tasks | — | 🔴 Absent | |
| Cancel Task | `DELETE /a2a/tasks/{id}` | 🟡 Partial | A task the agent is still working on is **interrupted**: its execution token is cancelled and the submitting request answers `Cancelled`. With persistence, a record left `Working` by a stopped server flips to `Cancelled`; a finished task answers 409 and keeps its state. Unknown ids (or, without persistence, any task not running) answer 404. |
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
| Client identity verification | mTLS (fail-closed: `RequireMutualTls` refuses to start without a trust anchor; CA chain or pinned thumbprints; 403 on failure) + `AllowedAuthSchemes` (401): a `Bearer` token validated by an `IAuthenticationProvider` (Azure AD, OIDC, or the host's), an `ApiKey` compared in constant time with the secrets named by `ApiKeySecretNames`; a scheme declared without a validator refuses to start | 🟢 Both credentials are validated, not just matched. `A2AClient` sends its own (`ClientAuthScheme` + `ClientCredentialSecretName`, below). |
| Agent card access | `GET /.well-known/agent.json` | 🟢 Public by design: the security checks apply to the task endpoints only. |
| `securitySchemes` declaration in the AgentCard | — | 🔴 Schemes are enforced but not advertised. |
| Certificate revocation | Not checked | 🟡 **Decision (PUB-08 T3): prefer short-lived certificates over CRL/OCSP.** The A2A mTLS trust model targets private CAs, where CRL/OCSP endpoints rarely exist and OCSP adds an availability dependency; a 24–72 h certificate lifetime bounds the exposure window with no new runtime dependency, and rotation already fits the existing options (a new client instance picks up the new PFX). CRL support stays out of scope until a deployment proves the need. |
| Push-notification webhook auth | — | 🔴 No webhooks. |

## Task execution

`A2ATaskRouter`, the `IA2ATaskRouter` the opt-in registers, runs the agent the
request names. The card lists one skill per available agent (id → `id`, role →
`name`, goal → `description`, `text/plain` in and out), and the request's
`skillId` must equal one of those ids **exactly** — the role is not a key, and
`writer` never selects `Ghostwriter`. The agent then works on an ad hoc task whose
description is the request's `input` (its `metadata` become the task variables),
through the host's `IAgentExecutionService`, resolved in the request's own DI
scope:

- the agent succeeds → `Completed`, `output` is what it produced;
- it fails or throws → `Failed`, `error` is its error;
- the request's token fires (`DELETE /a2a/tasks/{id}`, or the server stopping) →
  `Cancelled`;
- no agent publishes that id, the input is empty, or the host registered no
  execution service (`AddOrkeonApplication()` does) → `Failed`, saying which.

Nothing answers `Completed` without the agent having run. A host that routes
differently registers its own `IA2ATaskRouter` before calling `AddOrkeonA2A` (the
default is registered with `TryAdd`, so the host's wins). Requests are served
concurrently, so a `DELETE` reaches a task the agent is still working on.

## Activation

No shipped binary turns A2A on yet: `orkeon run`, `orkeon-host` and the REPL do
not call `AddOrkeonA2A` (exposing it in `orkeon-host` waits on per-crew routing).
An embedding host opts in with `AddOrkeonA2A(configuration)` (sections `A2A` and
`A2A:Security`) or `AddOrkeonA2A(configure, configureSecurity)`, plus
`AddOrkeonA2ATaskPersistence()` for durable task records. With `EnableServer`,
the extension also registers a hosted service: a generic host starts the server
with itself and stops it on shutdown, with no start-up code of its own; a start
failure (a declared scheme without a validator, mutual TLS without a trust
anchor) fails the host's start. See [Opt-in subsystems](./opt-in-subsystems.md).

| `A2A` key | Default | Effect |
|---|---|---|
| `EnableServer` | `false` | Registers `IA2AServer` (an `HttpListener` on `Host:Port`) and the hosted service that runs it. |
| `Host`, `Port` | `http://localhost`, `5002` | The listener prefix. |
| `AgentName`, `AgentDescription`, `AgentVersion`, `Organization`, `ContactUrl` | `Orkeon`, `Orkeon A2A Agent`, `1.0.0`, —, — | The agent card. |
| `TimeoutSeconds` | `30` | Client request timeout. |

`A2A:Security` carries `ClientCertificatePath`/`ClientCertificatePassword` (the
client's own certificate), `TrustedCertificateAuthorities`,
`TrustedClientCertificateThumbprints`, `RequireMutualTls`, `AllowedAuthSchemes` and
`ApiKeySecretNames`, plus the bearer validators `A2A:Security:AzureAD` and
`A2A:Security:Oidc` (registered by `AddOrkeonA2A(configuration)` when filled in).
Client side, `ClientAuthScheme` (`Bearer` or `ApiKey`) and
`ClientCredentialSecretName` make `A2AClient` send `Authorization: <scheme> <secret>`
on every task call, the secret read through `ISecretProvider` each time; a call
whose credential cannot be read fails before anything is sent. Card discovery
(`IA2AAgentDiscovery`) stays anonymous.

`IA2AClient` and `IA2AAgentDiscovery` serve a C# host that calls a peer; no
shipped agent tool calls one.

## Where this leaves consumers

- **Orkeon ↔ Orkeon** across processes/hosts: the wire works end to end — card,
  send (the agent runs), stream, status, cancel (in-flight work stops), mTLS and
  bearer/API-key credentials on both sides — with durable task status under the
  persistence opt-in. What remains is hosting: a C# host enables it; no shipped
  binary does yet.
- **Orkeon ↔ third-party v1.0 agents**: not yet — wait for (or contribute to)
  the HTTP+JSON binding alignment tracked by the PUB-08 follow-up.

Every gap above is deliberate scope, not an oversight: the surface is marked
`[Experimental]` (`ORKEXP001`) precisely so it can be reshaped toward v1.0
without a breaking-change debt. See also
[Experimental APIs](./experimental-apis.md) and
[Limits and constraints](./limitations.md).
