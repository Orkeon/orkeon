# Orkeon.Tools.Abstractions

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Tools.Abstractions** provides the base classes and interfaces for Orkeon agent tools: `IBaseTool`, the typed `ToolBase<TRequest, TResponse>` pipeline (normalize → deserialize → validate → execute → serialize), validation, and the YAML contract attributes.

## Install

```
dotnet add package Orkeon.Tools.Abstractions --prerelease
```

> This package is published on the [GitHub Packages feed](https://github.com/orgs/Orkeon/packages); add the feed as a NuGet source first — see the [publication matrix](https://github.com/Orkeon/orkeon/blob/main/docs/reference/publication-matrix.md).

## Documentation

- [Creating a new tool](https://github.com/Orkeon/orkeon/blob/main/docs/tools/new-tool-pattern.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
