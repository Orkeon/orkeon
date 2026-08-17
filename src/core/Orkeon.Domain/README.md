# Orkeon.Domain

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Domain** is the dependency root of the framework: entities and aggregates (Agent, Crew, CrewTask), value objects (`ProcessType` with its 6 orchestration modes, `LlmParameters`, `AgentExecutionBudget`), domain events, and the core interfaces (tools, LLM providers, memory, FSM/graph orchestration). Pure business logic, no external dependencies.

## Install

```
dotnet add package Orkeon.Domain
```

## Documentation

- [Overview](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/overview.md)
- [YAML, builders and CrewFactory](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/yaml-and-builders.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
