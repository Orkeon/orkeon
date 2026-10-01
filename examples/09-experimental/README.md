# 09 - Avant-Garde & Experimental (96-102)

Experimental and cutting-edge use cases: self-adaptive crews, multi-party negotiation, legacy codebase archaeology, ethical AI jury, civilization simulation, and crew-of-crews orchestration.

**Runner**: `standard`

Several crews here declare `memoryProvider: "Redis"` or `"SQLite"`: the connection comes from the `Orkeon:Redis` / `Orkeon:Sqlite` section of your settings — see [Memory providers](../appsettings/README.md#memory-providers-memoryprovider-in-a-crew).

| # | Example | Process | Quality |
|---|---------|---------|---------|
| 96 | Crew Evolutive Auto-Adaptative | Sequential | Robustesse |
| 97 | Negociation Multi-Parties -- Budget de Concessions | Consensual | Robustesse |
| 98 | Archeologie Numerique de Codebase Legacy | Sequential | Simplicite |
| 99 | Jury Ethique Multi-Perspectives pour Decisions IA | Parallel -> Sequential -> Human | Securite |
| 100 | Simulateur de Civilisation Emergente | Sequential + A2A | Robustesse |
| 101 | Crew de Crews -- L'Orchestre des Orchestres | Hierarchical (meta) | Simplicite + Securite + Robustesse + Fiabilite |
| 102 | Graph-Based Orchestration (LangGraph-Style) | Graph (arêtes conditionnelles, cycles bornés) | Robustesse |

## streaming-demo

`streaming-demo/` is a standalone console project (not a `standard`-runner crew)
showing real-time streaming of agent execution via `IStreamingAgentExecutionService`.
Run it directly with `dotnet run --project 09-experimental/streaming-demo`. It reads
its own `appsettings.json` (Docker Model Runner by default).

## llm-response-format

`llm-response-format/` is a minimal demo of the `response_format` cascade
(forced JSON output at the provider boundary), showing the same crew expressed
in YAML and through the fluent API. See its [README](llm-response-format/README.md).
