# Orkeon.Analysis.Abstractions

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Analysis.Abstractions** holds the interfaces, DTOs and models of RaggableTree, Orkeon's semantic codebase graph: the 6-level stratified graph (monorepo → package → module → symbol → statement + edges), language adapter contracts, and the store/watcher/event-bus ports.

## Install

```
dotnet add package Orkeon --prerelease
```

> This assembly ships inside the [`Orkeon`](https://www.nuget.org/packages/Orkeon) umbrella package — the whole framework in one install; it is no longer a standalone NuGet package. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [RaggableTree — semantic graph](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/raggable-tree.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
