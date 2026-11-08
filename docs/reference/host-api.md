# The host API and the health probes

`orkeon-host` can serve a small HTTP API on a listener of its own: two health probes for systemd, Docker and Kubernetes, and the routes to start a hosted crew, follow it and stop it. It is **off by default**: a daemon must not open a port nobody asked for. (The A2A server is a separate listener, switched on separately — see [the service host](../architecture/service-host.md#5-other-agents-a2a).)

```json
{
  "Orkeon": {
    "Host": {
      "Api": {
        "Enabled": true,
        "Prefix": "http://localhost:5190/",
        "Security": { "AllowedAuthSchemes": ["ApiKey"], "ApiKeySecretNames": ["ORKEON_HOST_API_KEY"] },
        "ReadinessTags": [],
        "IdempotencyTtl": "24:00:00",
        "IdempotencyMaxKeys": 10000
      }
    }
  }
}
```

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Serves the API. Off, the host listens on no port for it. |
| `Prefix` | `http://localhost:5190/` | The listener prefix: scheme, host, port and a closing slash. `http://+:5190/` listens on every interface. |
| `Security` | none | What a caller presents on every route but the two probes: `AllowedAuthSchemes` (`Bearer`, `ApiKey`), `ApiKeySecretNames`, `RequireMutualTls`, `TrustedCertificateAuthorities`, `TrustedClientCertificateThumbprints` — the rules of A2A's `A2A:Security`, enforced by the same code (`HttpRequestAuthenticator`). `ApiKey` reads its keys through the secret provider (`ORKEON_<NAME>`), on every request. `Bearer` needs a validator: `A2A:Security:AzureAD` or `A2A:Security:Oidc`, or an `IAuthenticationProvider` the host registers. |
| `ReadinessTags` | `[]` | Health-check tags added to readiness. `llm` adds the LLM provider check (see below). |
| `SseKeepAliveInterval` | `00:00:15` | How long a quiet event stream waits before it writes a keep-alive frame. |
| `IdempotencyTtl` | `24:00:00` | How long an idempotency key is remembered. |
| `IdempotencyMaxKeys` | `10000` | The most keys held in memory. |

A prefix beyond the loopback (`http://+:…`) with no authentication scheme and no `RequireMutualTls` is **refused at start** (exit 78): it would run crews for whoever can route to it. So are a prefix without its closing slash, a `Bearer` scheme without a validator, `RequireMutualTls` without a trust anchor, and a port another process holds. Under Windows the listener is HTTP.sys: a prefix other than the loopback needs a URL reservation for the service's account (`netsh http add urlacl url=http://+:5190/ user=<account>`); without it the start is refused with the same exit code and the hint.

## Probes

Both answer without authentication — an orchestrator holds no credential — and with a JSON body `{ "status", "checks": [ { "name", "status", "description" } ] }`. A failed check's exception goes to the log, never to the body.

### `GET /healthz` — liveness

Runs the checks tagged `live`: **`system_resources`** (process memory against `Telemetry:MaxMemoryMB`; over it, `degraded`, which still answers 200). It reads the process and nothing else: **liveness never depends on the LLM, the state store, the run history or any other service**, so a provider outage never makes an orchestrator restart a daemon that is fine. `200`, or `503` once the host is stopping (see below).

### `GET /readyz` — readiness

Runs the checks tagged `ready`, plus those whose tag `ReadinessTags` lists. `200` when none is unhealthy, `503` otherwise.

| Check | What it verifies | Reaches |
|---|---|---|
| `crew_registry` | At least one crew is hosted and the host still admits runs. | memory |
| `state_store` | The checkpoint store answers a read (`IStateStore`). Healthy when none is registered. | the store |
| `run_history` | The run history answers a query (`IRunHistoryReader`). Healthy when the history is off. | the history |
| `memory_provider` | The memory provider answers (`IMemoryProvider`). | the provider |
| `llm_provider` | **Only when `ReadinessTags` contains `llm`.** The LLM provider answers. | the model service |

The LLM check is out of readiness by default: a provider's outage takes every run down but not the daemon, and an orchestrator that pulled the host out of rotation would turn a partial failure into a total one. Add `"llm"` to `ReadinessTags` when a load balancer should route away from a host that cannot reach its model.

**While the host stops**, both probes answer `503` with `"status": "stopping"` from the moment admission closes — the whole `ShutdownGracePeriod` — so a load balancer stops sending traffic while the runs in flight finish.

```bash
curl -s localhost:5190/healthz
curl -s -o /dev/null -w '%{http_code}\n' localhost:5190/readyz
```

systemd has no HTTP probe: use `ExecStartPost` to wait for readiness if a script needs it (`curl --retry 10 --retry-connrefused -f localhost:5190/readyz`), and leave `Type=notify` as the unit's own readiness. Docker and Kubernetes:

```dockerfile
HEALTHCHECK --interval=30s --timeout=3s CMD curl -fs http://localhost:5190/healthz || exit 1
```

```yaml
livenessProbe:  { httpGet: { path: /healthz, port: 5190 } }
readinessProbe: { httpGet: { path: /readyz,  port: 5190 } }
```

(the container's prefix is `http://+:5190/`, with an authentication scheme; the probes stay open.)

## Runs

Every route below needs the credentials of `Security`. Bodies and answers are JSON, with snake_case names; an error is `{ "error": "<code>", "message": "…" }`.

### One id end to end

The id of a run is the one the daemon mints when it admits the run (a ULID, an `ExecutionId`). It is the run's identity everywhere: the `run_id` of this API, the A2A task's run, `orkeon runs` and the run history, the checkpoint session (`--resume`), the `correlationId` of every event. `run.started` repeats it as `hostRunId`.

### `POST /runs`

```json
{ "crew": "support", "prompt": "What changed?", "variables": { "topic": "elections" }, "idempotency_key": "order-8841" }
```

`crew` and `prompt` are required; `variables` holds strings only; unknown fields are refused. The body is capped at 1 MiB.

| Answer | When |
|---|---|
| `202 { "run_id", "idempotent_replay": false }` | The run was admitted; it goes on in the background. |
| `200 { "run_id", "idempotent_replay": true }` | The `idempotency_key` had been seen with the same request: the first request's run. |
| `409 idempotency_conflict` | The key had been seen with a different request. |
| `400 invalid_request` | A malformed body. `413` when it is larger than 1 MiB. |
| `404 unknown_crew` | No hosted crew by that name. |
| `429 at_limit` + `Retry-After: 5` | The crew is at its `MaxConcurrentRuns`. |
| `503 host_stopping` | The host is stopping: nothing starts. |

### `GET /runs`, `GET /runs/{id}`, `POST /runs/{id}/cancel`

`GET /runs?crew=&limit=` lists the recorded runs, newest first (`limit` 1 to 500, 50 by default); `GET /runs/{id}` reads one: `run_id`, `crew`, `status` (`running`, `succeeded`, `failed`, `orphaned`), `started_at`, `finished_at`, `error`. `POST /runs/{id}/cancel` answers `202` and stops a running run (the run then reads `failed`: its `run.finished` carries exit code 130), `409 run_not_running` for a run that is over, `404 unknown_run` for an id nobody knows. The reading routes read the run history: with `Orkeon:RunHistory:Enabled` off they answer `501 run_history_disabled` (the daemon enables it by default).

### `GET /runs/{id}/events`

The run's events, from the history, then live until `run.finished`. With `Accept: text/event-stream` it is SSE: `id: <seq>`, `event: <kind>`, `data: <the event, in the wire form of the run event bus>`. Otherwise it is NDJSON (`application/x-ndjson`), one event per line. `?from=<seq>` starts at that seq (inclusive); the `Last-Event-ID` header, which an SSE client sends when it reconnects, resumes **after** the seq it names and wins over `from`. `seq` increases strictly within a run, across a `--resume` too. While a run is quiet the stream writes a keep-alive frame every `SseKeepAliveInterval` (an SSE comment `: keep-alive`, a blank line in NDJSON): the write is what notices a client that went away, so an abandoned stream ends instead of living as long as the run. A client that disconnects ends its stream quietly (logged at Debug).

## Idempotency

`idempotency_key` (1 to 255 characters) makes `POST /runs` safe to replay.

- **What is compared.** The key, and a SHA-256 of the request (the crew as the host names it, the prompt, the variables sorted by name). Same key and same hash: the first run's id, `200`. Same key, another hash: `409`.
- **Concurrency.** Two requests with the same key that arrive at once start **one** run: the first reserves the key under a lock before it starts anything, the second waits for that start and answers with the first's run id. A refused attempt (`429`, `503`, `404`) releases the key, so the client may retry it.
- **Where keys live.** In memory, for `IdempotencyTtl` (24 hours by default, counted from the run's start), at most `IdempotencyMaxKeys` — the oldest finished ones go first. The key and the hash are also written into the `run.started` event of the run, in the run history.
- **Across a restart.** When the API starts, the keys of the runs the history holds from the last `IdempotencyTtl` (newest 1000) are read back, so a replay after a restart still answers `200` on the same run. This lasts as long as the history keeps the run (`Orkeon:RunHistory` retention) and the TTL has not passed. **With the history off, a restart forgets every key**, and a replay starts a new run. A history that cannot be read at start costs the keys of before the restart, not the start; a warning says so.
- A run sent without a key is never deduplicated.

## Not in this API

Answering a run's human-input question (`POST /runs/{id}/input`) is not here yet. Streaming the run's LLM tokens is not either: the events carry the task and tool progress the event bus publishes.
