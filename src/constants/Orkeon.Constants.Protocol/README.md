# Orkeon.Constants.Protocol

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET.

**Orkeon.Constants.Protocol** declares the wire vocabulary two Orkeon processes exchange: the
event kinds a run streams as it executes (`run.started`, `task.completed`, `llm.delta`, …), the
ones the use-case search answers with (`usecases.results`, …), and the ones the sign-in of an
e-mail account says its steps with (`email.login.device_code`, …). It **depends on nothing** — no
Orkeon project, no third-party package — so any layer may reference it without dragging the
runtime along ([ADR-009](https://github.com/Orkeon/orkeon/blob/main/docs/adr/ADR-009-shared-constants-satellites.md)).

It exists because the producer and the consumer live in projects that cannot reference each other:
the CLI runner emits the stream, Orkeon Studio reads it. Nothing validates their agreement — an
event kind one side stopped emitting, or never learned to read, is silently ignored rather than
reported, and that had already happened. The same kinds name the events a C# host reads in
process from `ICrewOrchestrationService.KickoffStreamingAsync`, which is why `Orkeon.Application`
references this satellite and the `Orkeon` package carries it.

## Install

This project is not distributed as a NuGet package of its own — its assembly ships inside the `Orkeon` umbrella package (it is part of `Orkeon.Application`'s closure) and through the release artifacts; inside this repository, reference it from source (`ProjectReference`). See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Contents

| Type | What it declares |
|---|---|
| `RunEventKinds` | The event kinds a run emits, plus `All` — the set, so a consumer can assert it handles every one. |
| `RunEventErrorCodes` | The `code` of the `error` event a stopped run ends on: `crew_failed` or `crew_cancelled`. |
| `UseCaseEventKinds` | The event kinds `orkeon usecases` exchanges with the process driving it — the search session's query and answer, the catalogue, a sheet, an export — plus `All`. |
| `EmailEventKinds` | The event kinds `orkeon email login --events jsonl` writes for the process driving it — the device code, the authorization address, the completion, the refusal — plus `All`. |

## License

MIT
