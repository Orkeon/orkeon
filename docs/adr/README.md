> 🇫🇷 [Version française](../fr/adr/README.md)

# Architecture Decision Records

Dated, immutable records of the structural decisions that shaped Orkeon. A
superseded ADR keeps its original text as an archive; the superseding one links
back.

| ADR | Decision | Status |
|---|---|---|
| [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) | `Orkeon.Tools.Abstractions` as a shared kernel (the `Infrastructure → Tools.Abstractions` exception) | Accepted |
| [ADR-003](./ADR-003-shared-kernels-secondaires.md) | Secondary shared kernels: `Application → Analysis.Abstractions`, `Infrastructure → Analysis` | Accepted |
| [ADR-004](./ADR-004-jumeaux-de-nommage-scripting.md) | The scripting naming twins, documented without renaming | Superseded by ADR-007 |
| [ADR-005](./ADR-005-famille-tools-heterogene.md) | The heterogeneous `Tools.*` family (`Tools.Web`/`Tools.EventHub → Application`) | Accepted |
| [ADR-006](./ADR-006-rag-subsystem.md) | The `src/rag/` subsystem: `Orkeon.Rag.Abstractions` shared kernel, legacy RAG namespaces removed without shims | Accepted |
| [ADR-007](./ADR-007-d3-renommage-cli-commands-scripting.md) | Decision D3: `Orkeon.Cli.Scripting` renamed to `Orkeon.Cli.Commands.Scripting` before any NuGet publish | Accepted |

The RaggableTree design decision lives with its architecture guide:
[RaggableTree ADR](../architecture/raggable-tree-adr.md).
