# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Orkeon is a C# port of the Python Orkeon library. The project enables creation and management of AI agent teams that collaborate on complex tasks using LLMs. 

**Current Status**: Working towards V1 iso-functional with Python Orkeon. Domain and Application layers are ~90% complete. Infrastructure layer has been redesigned (Akka.NET removed; replaced with simple HTTP-based implementations on .NET 10).

## Common Development Commands

### Build and Test

```bash
# Build Domain project (compiles successfully)
dotnet build src/core/Orkeon.Domain/Orkeon.Domain.csproj

# Build Application project (compiles successfully)
dotnet build src/core/Orkeon.Application/Orkeon.Application.csproj

# Build Infrastructure project (compiles successfully)
dotnet build src/core/Orkeon.Infrastructure/Orkeon.Infrastructure.csproj

# Build full solution
dotnet build Orkeon.sln

# Run Domain tests
dotnet test tests/core/Orkeon.Domain.Tests/Orkeon.Domain.Tests.csproj

# Run Application tests
dotnet test tests/core/Orkeon.Application.Tests/Orkeon.Application.Tests.csproj

# Run CLI tests (incl. Terminal.Gui split-pane console)
dotnet test tests/cli/Orkeon.Cli.Abstractions.Tests/Orkeon.Cli.Abstractions.Tests.csproj
dotnet test tests/cli/Orkeon.Cli.Tests/Orkeon.Cli.Tests.csproj
dotnet test tests/cli/Orkeon.Cli.TerminalGui.Tests/Orkeon.Cli.TerminalGui.Tests.csproj

# Run Tool tests (dedicated projects)
dotnet test tests/tools/Orkeon.Tools.Abstractions.Tests/Orkeon.Tools.Abstractions.Tests.csproj
dotnet test tests/tools/Orkeon.Tools.Code.Tests/Orkeon.Tools.Code.Tests.csproj
dotnet test tests/tools/Orkeon.Tools.Data.Tests/Orkeon.Tools.Data.Tests.csproj
dotnet test tests/tools/Orkeon.Tools.FileSystem.Tests/Orkeon.Tools.FileSystem.Tests.csproj
dotnet test tests/tools/Orkeon.Tools.Web.Tests/Orkeon.Tools.Web.Tests.csproj

# Run Analysis (RaggableTree) tests
dotnet test tests/analysis/Orkeon.Analysis.Tests/Orkeon.Analysis.Tests.csproj

# Run specific test
dotnet test --filter "FullyQualifiedName~TestName"

# Test new tool calling protocol
dotnet test --filter "FullyQualifiedName~ToolCall"

# Test agent selection by embeddings
dotnet test --filter "FullyQualifiedName~AgentSelection"
```


### Package and Restore
```bash
# Restore dependencies
dotnet restore Orkeon.sln

# Package for distribution
dotnet pack Orkeon.sln --configuration Release --output ./artifacts
```

### SonarQube Analysis

The analysis scripts handle everything automatically: starting SonarQube, running the scanner with code coverage, waiting for results, and generating a detailed Markdown report.

```bash
# Full analysis (Linux/macOS) — starts SonarQube if needed, runs analysis, generates report
export SONAR_TOKEN=<votre-token>
bash scripts/sonar-analyze.sh
# Report output: sonarqube/sonarqube-report-YYYY-MM-DD.md

# Full analysis (Windows)
$env:SONAR_TOKEN="<votre-token>"
.\scripts\sonar-analyze.ps1

# Manual SonarQube management (optional — scripts auto-start it)
docker compose -f docker-compose.sonarqube.yml up -d   # Start
docker compose -f docker-compose.sonarqube.yml down     # Stop
```

**Key details:**
- Scripts use `sonar.login` (not `sonar.token`) for SonarQube 9.9 LTS compatibility
- Coverage uses OpenCover format (`Format=opencover`, glob `**/coverage.opencover.xml`)
- `sonar-project.properties` was removed — all parameters are passed via CLI to avoid conflicts
- Report includes: coverage per project/directory, all issues, all code smells by project, security hotspots
- Requires `jq` for report generation (analysis works without it)

## Architecture Overview

The project follows Clean Architecture with clear separation of concerns:

- **Orkeon.Domain** (✅ 95% Complete): Core business entities and interfaces (Agents, Crews, Tasks, Tools, LLMs)
- **Orkeon.Application** (✅ 90% Complete): Use cases, services, orchestration logic, abstractions
- **Orkeon.Infrastructure** (🔄 Redesigned): External integrations, persistence, LLM providers, memory stores

### Current Implementation Status

**Working Features**:
- ✅ Complete Agent, Task, Crew domain models with all Python attributes
- ✅ 76 built-in tool classes (FileRead, FileWrite, WebScrape, HttpApi, JSON, PDF, CSV, XML, DirectoryRead, EmailParser, DatabaseQuery, RagTool, SearchTool, AskQuestion, DelegateWork, SecureCodeInterpreter, EventHub tools, RaggableTree analysis tools, etc.)
- ✅ 12 LLM providers: OpenAI, Ollama, Anthropic, AzureOpenAI, Groq, Mistral AI, DeepSeek, Kimi, Qwen, TogetherAI, HuggingFace, Z.AI (GLM)
- ✅ YAML configuration support
- ✅ Memory abstractions (IMemoryProvider interface)
- ✅ Tool validation framework with security, rate limiting, telemetry
- ✅ Batch tool execution for parallel operations
- ✅ Typed inputs and dependencies
- ✅ **NEW**: Structured tool calling protocol (JSON-based)
- ✅ **NEW**: Configurable agent selection (`OrkeonApplicationOptions.AgentSelectionStrategy`: `FirstFit` default, `Embedding`, `Skill`). Semantic (`Embedding`) selection requires a real embedding provider — see `docs/reference/limitations.md`
- ✅ **NEW**: Strongly typed configurations (AgentConfiguration, TaskContext, etc.)
- ✅ **NEW**: SequentialCrewOrchestrator (Akka.NET replacement)
- ✅ **NEW**: Typed Request/Response pipeline (ComponentBase<TReq,TRes>)
- ✅ **NEW**: Autonomous orchestration mode (`ProcessType.Autonomous`) with multi-dimensional execution budget, recursive delegation, agent self-spawn, and A2A request/response communication
- ✅ **NEW**: FSM orchestration (`StateMachine<TState, TEvent>`) with circuit breaker (4 mechanisms)
- ✅ **NEW**: Graph orchestration (`StateGraph<TState>`) LangGraph-style with conditional edges and controlled cycles
- ✅ **NEW**: LLM exchange logging — full HTTP request/response capture (headers + payload) via `DelegatingHandler`
- ✅ **NEW**: `LlmResponseFormat` value object + `LlmConfigOverride` cascade
  (crew → agent → task → script → call-site). DeepSeek câble
  `response_format: json_object`.

**Infrastructure Layer Components**:
- ✅ Redis memory provider with vector search (`RedisMemoryProvider`, `EncryptedRedisMemoryProvider`)
- ✅ OpenAI LLM provider implementation (`OpenAIProvider`)
- ✅ Ollama LLM provider implementation (`OllamaLlmProvider`)
- ✅ Anthropic LLM provider implementation (`AnthropicLlmProvider`)
- ✅ Azure OpenAI LLM provider implementation (`AzureOpenAILlmProvider`)
- ✅ Groq LLM provider implementation (`GroqLlmProvider`)
- ✅ Mistral AI LLM provider implementation (`MistralLlmProvider`, OpenAI-compatible cloud API)
- ✅ DeepSeek LLM provider implementation (`DeepSeekLlmProvider`)
- ✅ Kimi (Moonshot) LLM provider implementation (`KimiLlmProvider`)
- ✅ Qwen LLM provider implementation (`QwenLlmProvider`)
- ✅ TogetherAI LLM provider implementation (`TogetherAiLlmProvider`)
- ✅ HuggingFace LLM provider implementation (`HuggingFaceLlmProvider`)
- ✅ Z.AI (Zhipu GLM) LLM provider implementation (`ZaiLlmProvider`, thinking + context-cache metrics)
- ✅ SQLite memory provider (`SqliteMemoryProvider`, Microsoft.Data.Sqlite — persistent storage, cosine vector search; wire with type `"sqlite"` in `MemoryProviderFactory`)
- ✅ File system tools implementations
- ✅ ChromaDB vector store (`ChromaDbMemoryProvider`)
- ✅ Pinecone vector store (`PineconeMemoryProvider`)
- ✅ LanceDB vector store (`LanceDbMemoryProvider`, remote LanceDB Cloud/Enterprise REST server — Arrow IPC payloads, server-side vector & full-text search)

### Key Architectural Components

**LLM Integration**: 12 providers implemented (OpenAI, Ollama, Anthropic, AzureOpenAI, Groq, Mistral AI, DeepSeek, Kimi, Qwen, TogetherAI, HuggingFace, Z.AI), all extending `HttpLlmProviderBase`. Simple HTTP-based providers. Full HTTP exchange logging via `LlmLoggingDelegatingHandler` (headers + payload, sanitized).

**Tool System**: Extensible architecture with IBaseTool interface, validation, batch execution, and 76 built-in tool classes.

**Memory System**: Provider-based architecture supporting Redis, In-Memory, ChromaDB, Pinecone, LanceDB, and SQLite.

**Orchestration Strategies** (6 modes via `ProcessType` value object):
- `Sequential` — Fixed linear pipeline
- `Hierarchical` — Manager LLM delegates to agents
- `Parallel` — Independent task execution
- `Consensual` — Voting-based agreement
- `Graph` — LangGraph-style state graph with conditional edges, cycles, circuit breaker (`StateGraph<TState>`, `GraphProcessStrategy`)
- `Autonomous` — Self-organizing agents with recursive delegation, self-spawn, multi-dimensional budget (`AgentExecutionBudget`, `AutonomousProcessStrategy`)

**Autonomous Orchestration** (key components):
- `AgentExecutionBudget` (Domain) — 5-dimension budget: tool calls, delegation depth, wall time, tokens, spawned agents. Thread-safe, presets (Strict/Default/Permissive), child budget derivation.
- `IAgentChannel` (Application) — Bidirectional A2A communication (request/response + broadcast)
- `InMemoryAgentChannel` (Infrastructure) — Lock-free `ConcurrentDictionary` implementation
- `SpawnAgentTool` (Infrastructure) — Runtime agent creation via `IAgentFactory`
- `DelegateWorkTool` (Infrastructure) — Extended with optional `AgentExecutionBudget` for recursive depth control

**RaggableTree** (semantic codebase graph, `src/analysis/`):
- 6-level stratified graph (Monorepo → Package → Module → Symbol → Statement + Edges) built from Tree-sitter ASTs
- 5 language adapters: TypeScript, C#, Python, Go, Rust (via `ILanguageAdapter`)
- 15 agent tools in `Orkeon.Tools.Analysis`: `index_codebase`, `codebase_map`, `symbol_detail`, `flow_trace`, `impact_analysis`, `complexity_report`, `codebase_search`, etc.
- Pipeline: discovery → parse/extract → resolve edges → fingerprint → embed → persist
- `IncrementalReindexEngine` for diff-based updates, `ICodebaseWatcher` + `IRaggableTreeEventBus` for live sync, `ICodebaseContextProvider` for auto-injected summaries
- See `docs/architecture/raggable-tree.md` for the full guide and `docs/architecture/raggable-tree-adr.md` for the ADR

## Core Components

### Agents
There is a single `Agent` aggregate root (no subclasses). Agent behavior is configured via properties such as `AllowDelegation`, `MaxIterations`, `Verbose`, and assigned tools. Manager behavior in hierarchical crews is handled via `IManagerAgent` interface and `LlmBasedManager`.

### Memory System Architecture

**Application Layer** (Abstractions & Orchestration):
- `IMemoryProvider` interface
- `IMemoryService` for memory management
- `MemoryProviderFactory` for provider selection
- Memory types: ShortTerm, LongTerm, Episodic, Entity

**Infrastructure Layer** (Implementations):
- `RedisMemoryProvider`: Distributed memory with vector search
- `EncryptedRedisMemoryProvider`: Redis with at-rest encryption
- `InMemoryProvider`: Fast local memory for development
- `SqliteMemoryProvider`: Persistent local storage with SQLite (embeddings as BLOB, cosine vector search)
- `EncryptedMemoryProviderDecorator`: At-rest encryption decorator wrapping any provider (incl. SQLite)
- `ChromaDbMemoryProvider`: ChromaDB vector database (REST API v2 — servers ≥ 0.6.x / 1.x; tenant/database configurable, defaults `default_tenant`/`default_database`)
- `PineconeMemoryProvider`: Pinecone cloud vector database
- `LanceDbMemoryProvider`: Remote LanceDB Cloud/Enterprise server (Lance REST Namespace protocol, server-side search)

### Collaboration
- Communication protocols (Direct, Broadcast, Consensus, Feedback)
- Team formation strategies with skill-based matching
- Consensus building with configurable voting strategies

### Testing Framework
Uses xUnit for unit and integration testing, with **native xUnit assertions only** — no
mocking framework (no Moq, NSubstitute, FakeItEasy) and no fluent assertion library
(no FluentAssertions, Shouldly, Verify).

Test doubles are **hand-written by design** (a deliberate choice, not a gap): plain classes
named `Mock*`, `Fake*`, or `Stub*` that implement the production interface directly. This keeps
test dependencies minimal and the doubles' behavior explicit and debuggable.

**Convention for adding a new double**:
- Place it in the consuming test project under a `Doubles/` (or `Fakes/`) folder
  (e.g. `tests/core/Orkeon.Infrastructure.Tests/Doubles/`).
- Name it after the interface it replaces, prefixed by `Mock`/`Fake`/`Stub`
  (e.g. `MockTaskRepository` implements `ITaskRepository`).
- Expose plain fields/properties to inspect recorded calls or configure return values.
- See `tests/core/Orkeon.Infrastructure.Tests/Doubles/MockTaskRepository.cs` as the reference.

## Development Guidelines

### Virtual File System (VFS) — mandatory for all I/O

Framework code MUST NOT call `System.IO.File.*`, `System.IO.Directory.*`, `new FileStream/FileInfo/DirectoryInfo/FileSystemWatcher` directly. Route all filesystem access through `IFileSystemService` (mount-aware, rights-audited, virtual paths). See `docs/architecture/vfs-compliance.md` for the full spec; the baseline audit and second-pass review live in `project/audit-vfs-compliance-*.md`.

Allowed exceptions:
- **VFS implementation** (`src/core/Orkeon.Domain/FileSystem/`, `src/core/Orkeon.Infrastructure/FileSystem/`) — where the abstraction itself lives.
- **Bootstrap code** that provisions mounts before DI (e.g., `SandboxMountBootstrapper`). Must carry an inline `// EXCEPTION-BOOTSTRAP` comment.
- **OUT-OF-SCOPE system probing** (e.g., discovering the `docker` binary on PATH or the .NET SDK install path). Must carry an inline `// OUT-OF-SCOPE` comment.
- **Tests** — may use real disk via `DiskBackedFileSystemService` fixtures.

Adding a new tool or service that needs filesystem access? Inject `IFileSystemService` and work in virtual paths (e.g., `/workspace/...`, `/output/...`, `/tmp/...`). The service validates paths against mounts + `FileAccessRights` (Read/Write/Create/Delete) and redacts physical paths from error messages.

### Shell Scripts — executable bit is mandatory

Every `.sh` script — and any script with a shebang line (`#!`), e.g. `.py` or `.mjs` meant to be run directly — MUST be tracked by git as executable (mode `100755`). This workspace often lives on a Windows/NTFS mount where `chmod +x` does not persist and everything appears as `rwxrwxrwx`, which masks the problem — but macOS/Linux clones faithfully restore the committed mode, and a script committed as `100644` fails there with `permission denied`.

When creating or modifying such a script, always set the bit directly in the git index before committing:

```bash
git update-index --chmod=+x path/to/script.sh
```

Verify with `git ls-files -s -- '*.sh'` — every line must start with `100755` (same check for shebang `.py`/`.mjs` files). Scripts without a shebang that are only invoked via an interpreter (`python script.py`) may stay `100644`. This applies to this repository and to the `experiments` and `backstage` submodules alike.

### Tool Development (Typed Pipeline)

New tools should use the typed `ToolBase<TRequest, TResponse>` pattern:

```csharp
// 1. Define typed request/response records
public sealed record MyToolRequest
{
    public string Input { get; init; } = "";
    public int Count { get; init; } = 10;
}

public sealed record MyToolResponse
{
    public bool Success { get; init; }
    public string Result { get; init; } = "";
}

// 2. Inherit ToolBase<TReq, TRes>
public class MyTool : ToolBase<MyToolRequest, MyToolResponse>
{
    public override string Name => "my_tool";
    public override string Description => "Does something useful";

    protected override Task<MyToolResponse> ExecuteTypedAsync(
        MyToolRequest request, CancellationToken ct)
    {
        // Typed access — no dictionary extraction needed
        return Task.FromResult(new MyToolResponse
        {
            Success = true,
            Result = $"Processed {request.Input} x{request.Count}"
        });
    }
}
```

For tools inheriting `FileToolBase` or `HttpToolBase`, use the **composition pattern** with a private `ComponentBase` inner class (see `FileReadTool.cs` for reference).

**Key classes**:
- `ComponentBase<TRequest, TResponse>` (Domain) — Core pipeline: normalize → deserialize → validate → execute → serialize
- `ToolBase<TReq, TRes>` (Infrastructure) — Tool-specific wrapper with YAML defaults merging
- `EvaluatorBase<TInput, TResult>` (Infrastructure) — Typed evaluator base
- `FlowStepBase<TInput, TOutput>` (Infrastructure) — Typed flow step base

### Tool Development (Legacy)
Implement `IBaseTool` interface. Tools should:
- Handle errors gracefully
- Validate inputs
- Support cancellation tokens
- Return `ToolResult` with success/failure status

### Fluent Builder API

New code should prefer the Fluent Builder API for creating Agents, Tasks, and Crews:

```csharp
using Orkeon.Domain.Agent;   // AgentBuilder
using Orkeon.Domain.Crew;    // CrewBuilder

var agent = new AgentBuilder()
    .Role("Analyst")
    .Goal("Analyze data")
    .WithTool(myTool)
    .Verbose()
    .Build();

var crew = new CrewBuilder()
    .Goal("Complete research")
    .Sequential()
    .WithAgent(a => a.Role("Researcher").Goal("Find data"))
    .WithTask(t => t.Description("Search web").ExpectedOutput("Report"))
    .Build();
```

The factory methods `Agent.Create()`, `CrewTask.Create()`, `Crew.Create()` remain available.
Builders delegate to these internally.

### DTO Conventions

All DTOs in `Orkeon.Application` follow these conventions:

- **Type**: `sealed record` with `{ get; init; }` properties
- **Required fields**: Use the `required` keyword (not `[Required]` attribute)
- **Collections**: `ImmutableList<T>`, `ImmutableDictionary<K,V>` (never `List<T>` or `Dictionary<K,V>`)
- **Naming suffixes**:
  - `*Dto` — Read/transfer DTOs (AgentDto, CrewDto, TaskDto)
  - `*Request` — Input DTOs for commands (CreateAgentRequest, UpdateCrewRequest)
  - `*Response` — API response wrappers (via `ApiResponse<T>`)
- **JSON serialization**: `[JsonPropertyName("snake_case")]` on all API-exposed DTOs
- **Validation**: `ICommandValidator<T>` for business rules (no Data Annotations)
- **One DTO per concept**: No legacy/modern duality, no duplicate definitions
- **Enums**: In dedicated files (*Enums.cs) within the entity's DTOs folder

### Memory Provider Development
Implement `IMemoryProvider` interface with:
- `StoreAsync` - Store embeddings with metadata
- `SearchAsync` - Semantic search with cosine similarity
- `GetAsync`, `DeleteAsync` - Read and delete operations

### LLM Provider Development
Extend `HttpLlmProviderBase` or implement `ILlmProvider`:
- Support retry logic
- Handle rate limiting
- Implement timeout handling

## Clean Architecture Layers

### Domain Layer (Inner Circle)
- Pure business logic, no external dependencies
- Entities: Agent, Crew, Task, Tool interfaces
- Value Objects: LlmParameters, TypedParameters, ProcessType (6 modes)
- `Autonomous/` folder: `AgentExecutionBudget`, `BudgetSnapshot`, `BudgetExhaustedException`, `BudgetDimension`
- Domain Events and Exceptions

### Application Layer (Middle Circle)
- Use cases and orchestration logic
- Service interfaces (ports): `IAgentChannel`, `IAgentExecutionService`, `IAgentFactory`
- DTOs and ViewModels
- `NullMemoryScope` singleton (public, for contexts without memory)
- Application-specific business rules
- Does NOT contain:
  - Database access code
  - External API calls
  - Framework-specific code

### Infrastructure Layer (Outer Circle)
- Implementations of Application interfaces (adapters)
- External service integrations:
  - LLM Providers (OpenAI, Ollama, Anthropic, AzureOpenAI, Groq, Mistral AI, DeepSeek, Kimi, Qwen, TogetherAI, HuggingFace, Z.AI)
  - Memory Stores (Redis, SQLite, InMemory, ChromaDB, Pinecone, LanceDB)
  - File System access
  - HTTP clients
- Orchestration strategies: `Crew/Strategies/` (Sequential, Hierarchical, Parallel, Graph, Autonomous)
- Communication: `InMemoryAgentChannel` (A2A lock-free)
- Autonomous tools: `SpawnAgentTool`, `DelegateWorkTool` (with budget)
- Framework-specific code
- Database contexts and repositories

## Working Directory Structure

The repository contains **22 src projects** and **24 test projects**, plus two solutions:
`Orkeon.sln` (root) and `examples/Orkeon.Examples.sln`.

```
/workspace/
├── Orkeon.sln                    # Main solution file (root level)
├── src/                          # 22 projects
│   ├── Directory.Build.props     # Shared build properties (version, NoWarn, VFS analyzer)
│   ├── core/
│   │   ├── Orkeon.Domain/        # ✅ Core entities (95% complete)
│   │   ├── Orkeon.Application/   # ✅ Use cases (90% complete)
│   │   └── Orkeon.Infrastructure/# 🔄 External integrations (LLMs, memory, strategies)
│   ├── cli/
│   │   ├── Orkeon.Cli.Abstractions/    # Pure contracts: IInteractiveCommand, runner base, console adapter
│   │   ├── Orkeon.Cli/                 # Reusable common commands (help/exit/clear) + DefaultCommandRegistry
│   │   ├── Orkeon.Cli.Scripting/       # TypeScript-scripted interactive commands (adapter Cli.Abstractions ↔ Scripting). NB: distinct du jumeau Orkeon.Scripting.Cli (entrypoint du tool `orkeon`)
│   │   └── Orkeon.Cli.TerminalGui/     # Terminal.Gui v2 split-pane console (logs + REPL); IConsoleAdapter + ILoggerProvider
│   ├── scripting/
│   │   ├── Orkeon.Scripting/           # TypeScript-syntax scripting DSL (.ork.ts) — Jint runtime + esbuild transpile
│   │   └── Orkeon.Scripting.Cli/       # CLI entrypoint of the scripting DSL — packs as dotnet tool `orkeon` (`orkeon run script.ork.ts`)
│   ├── analyzers/
│   │   └── Orkeon.Compliance.Vfs/      # Roslyn analyzer forbidding direct System.IO in framework code (routes via IFileSystemService)
│   ├── tools/
│   │   ├── Orkeon.Tools.Abstractions/  # Tool base classes & interfaces
│   │   ├── Orkeon.Tools.Analysis/      # RaggableTree agent tools (15 tools)
│   │   ├── Orkeon.Tools.Code/          # Code-related tools
│   │   ├── Orkeon.Tools.Data/          # Data manipulation tools
│   │   ├── Orkeon.Tools.Embeddings.Local/ # Local on-device embeddings (SmartComponents BGE-micro-v2 ONNX, 384 dims, CPU, no API key)
│   │   ├── Orkeon.Tools.EventHub/      # EventHub agent tools (publish_event, post_message, send_request, reply_to, receive_message, wait_for_event, get_last_value)
│   │   ├── Orkeon.Tools.FileSystem/    # File system tools
│   │   └── Orkeon.Tools.Web/           # Web/HTTP tools
│   ├── analysis/
│   │   ├── Orkeon.Analysis.Abstractions/ # RaggableTree interfaces + DTOs + models
│   │   └── Orkeon.Analysis/             # Tree-sitter pipeline, adapters, store, watcher
│   ├── plugins/
│   │   └── Orkeon.Plugins/       # Plugin system (IOrkeonPlugin, ALC-isolated discovery/loading, AddOrkeonPlugins — see docs/architecture/plugins.md)
│   └── apps/
│       └── Orkeon.ConsoleApp/    # Console application
├── tests/                        # 24 projects
│   ├── core/                     # Orkeon.Domain.Tests, Orkeon.Application.Tests, Orkeon.Infrastructure.Tests
│   ├── cli/                      # Orkeon.Cli.Abstractions.Tests, Orkeon.Cli.Tests, Orkeon.Cli.Scripting.Tests, Orkeon.Cli.TerminalGui.Tests
│   ├── scripting/                # Orkeon.Scripting.Tests, Orkeon.Scripting.Cli.Tests
│   ├── analyzers/                # Orkeon.Compliance.Vfs.Tests
│   ├── tools/                    # Abstractions, Analysis, Code, Data, Embeddings.Local, EventHub, FileSystem, Web (8 projects)
│   ├── analysis/                 # Orkeon.Analysis.Tests (RaggableTree)
│   ├── plugins/                  # Orkeon.Plugins.Tests
│   ├── apps/                     # Orkeon.ConsoleApp.Tests
│   ├── e2e/                      # Orkeon.E2E.Tests
│   ├── examples/                 # Orkeon.Examples.Runners.Tests
│   └── shared/                   # Orkeon.Tests.Shared (common test fixtures)
├── examples/                     # Orkeon.Examples.sln (separate solution)
│   ├── 01-enterprise/ … 09-experimental/  # 9 thematic example categories
│   ├── raggable-tree/            # RaggableTree: basic-indexing, crew-yaml, custom-adapter
│   ├── scripting/                # .ork.ts scripting examples
│   ├── cli-ts-commands/          # TypeScript CLI command examples
│   ├── local-embeddings/         # Local embedding example
│   ├── runners/, _shared/, others/
│   ├── Orkeon.Examples.sln
│   └── run-example.sh / run-example.ps1 / test-all-examples.*
├── tools/
│   └── scripting-esbuild/        # npm package.json + lockfile bootstrapping esbuild for the scripting DSL
├── scripts/
│   ├── sonar-analyze.sh / .ps1   # SonarQube analysis + report
│   └── validate-use-cases.sh / .ps1 / validate_use_cases.py
├── sonarqube/                    # Generated SonarQube reports (*.md)
├── docker-compose.sonarqube.yml
├── docs/
│   ├── INDEX.md                  # Documentation map
│   ├── getting-started/          # bootstrap.md, overview.md, yaml-and-builders.md
│   ├── architecture/             # raggable-tree.md, scripting.md, vfs-compliance.md, security.md, llm-providers.md, etc.
│   ├── guides/, orchestration/, reference/, tools/
│   └── arkeon/                   # llm-providers-comparatif.md
└── project/                      # Project management (non-code)
    ├── marketing/                # Positioning, personas, brand, assets
    ├── roadmap/                  # Feature backlog & gap analysis
    ├── features/                 # Feature specifications
    ├── experiments/
    ├── tasks/                    # Actionable dev tasks (1 file per task)
    └── prompts/                  # Claude/AI assistant prompts
```

## Important Notes

- **Namespace collision**: `Orkeon.Domain.Task` collides with `System.Threading.Tasks.Task`. In Application-layer files that import both, all `Task` references must be fully qualified as `System.Threading.Tasks.Task`.
- **Naming twins — do not confuse**: two near-anagram projects exist and are easy to mix up (see [ADR-004](docs/adr/ADR-004-jumeaux-de-nommage-scripting.md)):
  - `Orkeon.Cli.Scripting` (`src/cli/`) — library of **TypeScript-scripted interactive commands** for CLI runners (adapter between `Orkeon.Cli.Abstractions` and `Orkeon.Scripting`).
  - `Orkeon.Scripting.Cli` (`src/scripting/`) — the installable **`orkeon` tool** entrypoint (`orkeon run script.ork.ts`, `PackAsTool=true`, `AssemblyName=orkeon`).
  - Mnemonic: the project whose **last** segment is `Cli` is the executable.
- **Architecture Decision Records** live in `docs/adr/`. Notably ADR-002 documents the `Infrastructure → Tools.Abstractions` shared-kernel exception; ADR-003 covers the `Application → Analysis.Abstractions` and `Infrastructure → Analysis` couplings; ADR-005 covers `Tools.Web`/`Tools.EventHub → Application`.
- `InMemoryUnitOfWork` intentionally has no durable persist step (aggregates live in the in-memory repositories; `SaveChangesAsync` dispatches domain events). The former EF-migration TODO has been removed (R3.8). Durable crew **execution-state** persistence is a separate opt-in: `AddCrewExecutionStatePersistence(...)` + a checkpointing `IStateStore` (see `docs/reference/opt-in-subsystems.md`)
- ChromaDB, Pinecone, and LanceDB are implemented (REST API-based), not placeholders
- Infrastructure layer has been redesigned without Akka.NET; all projects target `net10.0`
- Version is `0.9.0-beta` (see `src/Directory.Build.props:11-12` — `VersionPrefix` `0.9.0` + `VersionSuffix` `beta`)
- Focus on V1 iso-functional parity, not advanced features
- `sonar-project.properties` has been removed (caused scanner conflicts — all params passed via CLI)
- SonarQube 9.9 LTS: use `sonar.login` (not `sonar.token`) for authentication
- Use `Format=opencover` for SonarQube coverage collection

## Typed Request/Response Architecture

The project uses a typed pipeline to eliminate `Dictionary<string, object>` from component signatures:

```
Dict<string,object> → NormalizeParameters (snake_case) → JsonDeserialize<TRequest> → Validate → ExecuteTyped → JsonSerialize<TResponse> → Dict<string,object>
```

**Core infrastructure** (Domain layer):
- `ComponentBase<TRequest, TResponse>` — Abstract base with full pipeline
- 6 YAML attributes (ComponentContract, ToolContract, FieldSchema, ReturnSchema, TypeDefinition, TypeProperty)

**Serialization converters** (Infrastructure layer):
- 6 tolerant/invariant JSON converters (BoolTolerant, IntTolerant, LongTolerant, DecimalInvariant, DoubleInvariant, RawObject) for YAML string coercion

**Component bridges** (Infrastructure layer):
- `ToolBase<TReq, TRes>` — Wraps ComponentBase with tool-specific concerns (YAML defaults, output filtering)
- `EvaluatorBase<TInput, TResult>` — Bridges IEvaluator interface with typed pipeline
- `FlowStepBase<TInput, TOutput>` — Bridges IFlowStep interface with typed pipeline

**Remaining Dict<string,object>**: Only in extensibility bags (Extensions, Metadata, CustomSettings, Context, Parameters).

## Infrastructure Layer Design Principles

1. **Simple HTTP-based implementations** instead of Actor model
2. **Direct service calls** instead of message passing
3. **Standard async/await** patterns instead of Akka streams
4. **Dependency injection** for all external services
5. **Testable adapters** for all external integrations

## Migration from Application to Infrastructure

When moving code from Application to Infrastructure:
1. **Interfaces stay in Application** (ports)
2. **Implementations move to Infrastructure** (adapters)
3. **DTOs and contracts stay in Application**
4. **External dependencies only in Infrastructure**

Example:
- `ILlmProvider` interface → Domain/Shared/
- `OpenAIProvider` class → Infrastructure/LLMs/
- `RedisMemoryProvider` class → Infrastructure/Memory/
- `FileReadTool` implementation → Tools/Orkeon.Tools.FileSystem/