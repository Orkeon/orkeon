# Orkeon.Application

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Application** holds the use cases and ports of the framework: crew orchestration services, agent execution, memory abstractions (`IMemoryProvider`), A2A communication ports (`IAgentChannel`), DTOs and validation. It depends only on `Orkeon.Domain`; implementations live in `Orkeon.Infrastructure`.

## Install

```
dotnet add package Orkeon.Application
```

## Documentation

- [Bootstrap and execution](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/bootstrap.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
