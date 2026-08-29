# Orkeon.Constants.Llm

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Constants.Llm** holds the LLM vocabulary the runtime and the tooling must agree on: provider endpoints, the default model of each provider, provider ids and the wire field names.

It depends on nothing. That is the point: see [ADR-009](https://github.com/Orkeon/orkeon/blob/main/docs/adr/ADR-009-shared-constants-satellites.md) — a value two projects must agree on is declared once here and referenced by both, instead of being copied by hand and guarded by a drift test.
