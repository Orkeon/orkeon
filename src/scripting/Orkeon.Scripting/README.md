# Orkeon.Scripting

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Scripting** is the TypeScript-syntax scripting DSL (`.ork.ts`): scripts are transpiled with esbuild and executed in a sandboxed Jint runtime, with the full Orkeon surface (agents, crews, tools, FSM, graphs, events, RAG retrieval) exposed through fluent builders.

## Install

```
dotnet add package Orkeon.Scripting --prerelease
```

> This package is published on the [GitHub Packages feed](https://github.com/orgs/Orkeon/packages); add the feed as a NuGet source first — see the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Scripting DSL](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/scripting.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
