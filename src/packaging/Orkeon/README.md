# Orkeon

Build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon** is the complete framework in a single package: the domain model (Agent, Crew, CrewTask, the 6 orchestration modes), the application services, and the adapters — 14 LLM providers (OpenAI, Anthropic, Azure OpenAI, Ollama, Mistral, DeepSeek, Kimi, Qwen, TogetherAI, HuggingFace, Z.AI, Gemini, Grok, MiniMax), 6 memory stores (InMemory, Redis, SQLite, ChromaDB, Pinecone, LanceDB), the staged RAG pipeline with its corrective graph, the RaggableTree semantic codebase analysis, the virtual file system, and the A2A channel. One install, a working framework.

The package carries the framework assemblies (`Orkeon.Domain`, `Orkeon.Application`, `Orkeon.Infrastructure`, `Orkeon.Rag`, `Orkeon.Analysis`, the shared contracts and constants) — namespaces are unchanged, so code written against the earlier per-layer packages compiles as-is.

## Install

```
dotnet add package Orkeon --prerelease
```

Add [Orkeon.Tools](https://www.nuget.org/packages/Orkeon.Tools) for the built-in agent tool families, and [Orkeon.Rag.Onnx](https://www.nuget.org/packages/Orkeon.Rag.Onnx) for the offline ONNX reranker.

## Quick start

```csharp
using Orkeon.Domain.Agent;
using Orkeon.Domain.Crew;

var crew = new CrewBuilder()
    .Goal("Complete research")
    .Sequential()
    .WithAgent(a => a.Role("Researcher").Goal("Find data"))
    .WithTask(t => t.Description("Search web").ExpectedOutput("Report"))
    .Build();
```

## Documentation

- [Overview](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/overview.md)
- [Three ways to run Orkeon](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/three-ways-to-run-orkeon.md)
- [YAML, builders and CrewFactory](https://github.com/Orkeon/orkeon/blob/main/docs/getting-started/yaml-and-builders.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
