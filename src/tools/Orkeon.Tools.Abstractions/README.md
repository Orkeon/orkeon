# Orkeon.Tools.Abstractions

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Tools.Abstractions** provides the base classes and interfaces for Orkeon agent tools: `IBaseTool`, the typed `ToolBase<TRequest, TResponse>` pipeline (normalize → deserialize → validate → execute → serialize), validation, and the YAML contract attributes.

## Install

```
dotnet add package Orkeon.Tools.Abstractions
```

## Documentation

- [Creating a new tool](https://github.com/Orkeon/orkeon/blob/main/docs/tools/new-tool-pattern.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
