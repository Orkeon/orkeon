# Orkeon.Tools.Abstractions

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Tools.Abstractions** provides the base classes for Orkeon agent tools: the typed `ToolBase<TRequest, TResponse>` pipeline (normalize → deserialize → validate → execute → serialize), `HttpToolBase` and validation, over the `IBaseTool` interface and contract attributes of `Orkeon.Domain`.

## Install

```
dotnet add package Orkeon
```

> This assembly ships inside the [`Orkeon`](https://www.nuget.org/packages/Orkeon) umbrella package — the whole framework in one install; it is not a standalone NuGet package. See the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Creating a new tool](https://github.com/Orkeon/orkeon/blob/main/docs/tools/new-tool-pattern.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
