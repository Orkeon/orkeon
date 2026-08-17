# Orkeon.Cli.Commands.Scripting

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Cli.Commands.Scripting** lets interactive CLI runners load commands written in TypeScript (`*.cmd.ts`, `defineCommand`) at startup, without .NET recompilation — the adapter between `Orkeon.Cli.Abstractions` and the `Orkeon.Scripting` engine. Not to be confused with `Orkeon.Scripting.Cli`, the installable `orkeon` tool (ADR-007).

## Install

```
dotnet add package Orkeon.Cli.Commands.Scripting
```

## Documentation

- [TypeScript CLI commands](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/cli-ts-commands.md)
- [TypeScript coding agent](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/coding-agent-ts.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
