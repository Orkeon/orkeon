# Orkeon.ConsoleApp (`orkeon-repl`)

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.ConsoleApp** is the interactive REPL of the framework, shipped as the `orkeon-repl` dotnet tool: a Terminal.Gui split-pane console (live logs on one side, prompt on the other) with the full framework stack wired in — agents, crews, memory, RAG, RaggableTree code analysis, and the TypeScript-scripted command layer (`*.cmd.ts`).

Not to be confused with the `orkeon` CLI (package `Orkeon.Scripting.Cli`), which runs crews and scripts non-interactively (`orkeon run crew.yaml`).

## Install

```
dotnet tool install --global Orkeon.ConsoleApp --prerelease
orkeon-repl
```

> This package is published on the [GitHub Packages feed](https://github.com/orgs/Orkeon/packages); add the feed as a NuGet source first — see the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Three ways to run Orkeon](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/three-ways-to-run-orkeon.md)
- [TypeScript CLI commands](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/cli-ts-commands.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors — this package also redistributes third-party components, including machine-learning model weights; their notices are in `THIRD-PARTY-NOTICES.md` at the package root ([repository copy](https://github.com/Orkeon/orkeon/blob/main/THIRD-PARTY-NOTICES.md)).
