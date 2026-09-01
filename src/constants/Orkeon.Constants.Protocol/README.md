# Orkeon.Constants.Protocol

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET.

**Orkeon.Constants.Protocol** declares the wire vocabulary two Orkeon processes exchange: the
event kinds a run streams as it executes (`run.started`, `task.completed`, `llm.delta`, …). It
**depends on nothing** — no Orkeon project, no third-party package — so any layer may reference it
without dragging the runtime along ([ADR-009](https://github.com/Orkeon/orkeon/blob/main/docs/adr/ADR-009-shared-constants-satellites.md)).

It exists because the producer and the consumer live in projects that cannot reference each other:
the CLI runner emits the stream, Orkeon Studio reads it. Nothing validates their agreement — an
event kind one side stopped emitting, or never learned to read, is silently ignored rather than
reported, and that had already happened.

## Install

This project is no longer distributed as a NuGet package — reference it from source (`ProjectReference` inside this repository); its assembly ships through the release artifacts. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Contents

| Type | What it declares |
|---|---|
| `RunEventKinds` | The event kinds a run emits, plus `All` — the set, so a consumer can assert it handles every one. |

## License

MIT
