> 🇬🇧 [English version](../../adr/README.md)

# Architecture Decision Records

Archives datées et immuables des décisions structurantes d'Orkeon. Un ADR
remplacé conserve son texte d'origine ; celui qui le remplace pointe en retour.

| ADR | Décision | Statut |
|---|---|---|
| [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) | `Orkeon.Tools.Abstractions` en shared kernel (l'exception `Infrastructure → Tools.Abstractions`) | Accepté |
| [ADR-003](./ADR-003-shared-kernels-secondaires.md) | Shared kernels secondaires : `Application → Analysis.Abstractions`, `Infrastructure → Analysis` | Accepté |
| [ADR-004](./ADR-004-jumeaux-de-nommage-scripting.md) | Les jumeaux de nommage scripting, documentés sans renommage | Remplacé par l'ADR-007 |
| [ADR-005](./ADR-005-famille-tools-heterogene.md) | La famille `Tools.*` hétérogène (`Tools.Web`/`Tools.EventHub → Application`) | Accepté |
| [ADR-006](./ADR-006-rag-subsystem.md) | Le sous-système `src/rag/` : shared kernel `Orkeon.Rag.Abstractions`, anciens namespaces RAG retirés sans shims | Accepté |
| [ADR-007](./ADR-007-d3-renommage-cli-commands-scripting.md) | Décision D3 : `Orkeon.Cli.Scripting` renommé en `Orkeon.Cli.Commands.Scripting` avant toute publication NuGet | Accepté |

La décision de conception de RaggableTree vit avec son guide d'architecture :
[ADR RaggableTree](../architecture/raggable-tree-adr.md).
