# 09 - Avant-Garde & Experimental (96-102)

Experimental and cutting-edge use cases: self-adaptive crews, multi-party negotiation, legacy codebase archaeology, ethical AI jury, civilization simulation, and crew-of-crews orchestration.

**Runner**: `standard`

| # | Example | Process | Quality |
|---|---------|---------|---------|
| 96 | Crew Evolutive Auto-Adaptative | FlowEngine (cyclique) | Robustesse |
| 97 | Negociation Multi-Parties -- Budget de Concessions | Consensual + FlowEngine | Robustesse |
| 98 | Archeologie Numerique de Codebase Legacy | Sequential + FlowEngine | Simplicite |
| 99 | Jury Ethique Multi-Perspectives pour Decisions IA | Parallel -> Sequential -> Human | Securite |
| 100 | Simulateur de Civilisation Emergente | FlowEngine (tours) + A2A | Robustesse |
| 101 | Crew de Crews -- L'Orchestre des Orchestres | Hierarchical (meta) | Simplicite + Securite + Robustesse + Fiabilite |
| 102 | Graph-Based Orchestration (LangGraph-Style) | Graph (arêtes conditionnelles, cycles bornés) | Robustesse |

## streaming-demo

`streaming-demo/` is a standalone console project (not a `standard`-runner crew)
showing real-time streaming of agent execution via `IStreamingAgentExecutionService`.
Run it directly with `dotnet run --project 09-experimental/streaming-demo`. It reads
its own `appsettings.json` (Docker Model Runner by default).
