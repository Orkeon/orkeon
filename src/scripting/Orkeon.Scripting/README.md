# Orkeon.Scripting

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Scripting** is the TypeScript-syntax scripting DSL (`.ork.ts`): scripts are transpiled with esbuild and executed in a sandboxed Jint runtime, with the full Orkeon surface (agents, crews, tools, FSM, graphs, events, RAG retrieval) exposed through fluent builders.

## Install

This project is no longer distributed as a NuGet package — reference it from source (`ProjectReference` inside this repository); its assembly ships through the release artifacts. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Scripting DSL](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/scripting.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
