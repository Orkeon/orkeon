# Orkeon.Infrastructure

Part of [Orkeon](https://github.com/Orkeon/orkeon) — build and orchestrate AI agent teams in .NET, described in declarative YAML, programmatic TypeScript (`.ork.ts`) or pure C#.

**Orkeon.Infrastructure** provides the adapters: 13 LLM providers (OpenAI, Anthropic, Azure OpenAI, Ollama, Groq, Mistral, DeepSeek, Kimi, Qwen, TogetherAI, HuggingFace, Z.AI, Gemini), 6 memory stores (InMemory, Redis, SQLite, ChromaDB, Pinecone, LanceDB), the 6 orchestration strategies, the virtual file system, and the A2A channel.

## Install

```
dotnet add package Orkeon.Infrastructure --prerelease
```

## Documentation

- [LLM providers](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/llm-providers.md)
- [Memory system](https://github.com/Orkeon/orkeon/blob/main/docs/architecture/memory-system.md)
- [Repository & getting started](https://github.com/Orkeon/orkeon)

MIT © Orkeon Contributors
