> 🇫🇷 [Version française](../fr/adr/README.md)

# Architecture Decision Records

Dated, immutable records of the structural decisions that shaped Orkeon. A
superseded ADR keeps its original text as an archive; the superseding one links
back.

> **Why does the numbering start at 002?** There is no ADR-001 — the first
> decision recorded as an ADR was numbered 002 and the gap was kept rather than
> renumbering, so existing cross-references stay stable.

| ADR | Decision | Status |
|---|---|---|
| [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) | `Orkeon.Tools.Abstractions` as a shared kernel (the `Infrastructure → Tools.Abstractions` exception) | Accepted |
| [ADR-003](./ADR-003-shared-kernels-secondaires.md) | Secondary shared kernels: `Application → Analysis.Abstractions`, `Infrastructure → Analysis` | Accepted |
| [ADR-004](./ADR-004-jumeaux-de-nommage-scripting.md) | The scripting naming twins, documented without renaming | Superseded by ADR-007 |
| [ADR-005](./ADR-005-famille-tools-heterogene.md) | The heterogeneous `Tools.*` family (`Tools.Web`/`Tools.EventHub → Application`) | Accepted |
| [ADR-006](./ADR-006-rag-subsystem.md) | The `src/rag/` subsystem: `Orkeon.Rag.Abstractions` shared kernel, legacy RAG namespaces removed without shims | Accepted |
| [ADR-007](./ADR-007-d3-renommage-cli-commands-scripting.md) | Decision D3: `Orkeon.Cli.Scripting` renamed to `Orkeon.Cli.Commands.Scripting` before any NuGet publish | Accepted |
| [ADR-008](./ADR-008-virtual-paths-are-the-only-currency.md) | A physical path is never a virtual path: runners mount under a name, infrastructure mounts are invisible to agents | Accepted |
| [ADR-009](./ADR-009-shared-constants-satellites.md) | A constant two projects must agree on lives in a zero-dependency satellite, not in a hand-written copy guarded by a drift test | Accepted |
| [ADR — RaggableTree](../architecture/raggable-tree-adr.md) | 5-level-plus-edges stratified semantic graph via Tree-sitter (unnumbered — lives with its architecture guide; amended 2026-08-18) | Accepted |
