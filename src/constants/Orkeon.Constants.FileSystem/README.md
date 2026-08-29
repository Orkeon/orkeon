# Orkeon.Constants.FileSystem

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Constants.FileSystem** holds the filesystem vocabulary shared across Orkeon: the virtual roots a runner mounts for itself, and the conventional folder and file names the tooling has to predict.

It depends on nothing. That is the point: see [ADR-009](https://github.com/Orkeon/orkeon/blob/main/docs/adr/ADR-009-shared-constants-satellites.md) — a value two projects must agree on is declared once here and referenced by both, instead of being copied by hand and guarded by a drift test.
