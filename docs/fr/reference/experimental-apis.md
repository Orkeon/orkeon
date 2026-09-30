> 🇬🇧 [English version](../../reference/experimental-apis.md)

# APIs expérimentales

Certaines surfaces d'Orkeon sont livrées pour recueillir des retours mais ne sont **pas
encore couvertes par l'engagement de stabilité API** (voir
[CONTRIBUTING — Versionnement et stabilité API](../../../CONTRIBUTING.fr.md#versionnement-et-stabilité-api)).
Elles portent `[Experimental]`
(`System.Diagnostics.CodeAnalysis.ExperimentalAttribute`) : les référencer produit une
**erreur de compilation** avec l'un des IDs de diagnostic ci-dessous, à supprimer
explicitement pour s'inscrire — cette suppression vaut reconnaissance que la surface
peut changer ou disparaître dans n'importe quelle version, mineures comprises.

```xml
<!-- opt-in, par projet -->
<PropertyGroup>
  <NoWarn>$(NoWarn);ORKEXP002</NoWarn>
</PropertyGroup>
```

ou, par site d'appel :

```csharp
#pragma warning disable ORKEXP002
var budget = AgentExecutionBudget.Default;
#pragma warning restore ORKEXP002
```

## IDs de diagnostic

| ID | Surface | Pourquoi expérimental |
|---|---|---|
| `ORKEXP001` | **A2A (protocole agent-à-agent)** — `IA2AClient`, `IA2AServer`, `IA2AAgentDiscovery`, `IA2ATaskRouter`, `IA2ATaskStore`, `IAgentRegistrationStore`, les implémentations `A2A*`, leurs options et types filaires, `StateStoreA2ATaskStore`, et `A2AExtensions` (`AddOrkeonA2A`, `AddOrkeonA2ATaskPersistence`) | L'implémentation précède la spécification A2A v1.0 ; la passe de conformité (PUB-08) a ajouté la persistance des tâches et tranché la révocation de certificats, et l'alignement des bindings vers la v1.0 (HTTP+JSON d'abord) reste à faire — voir la [matrice de conformité](./a2a-conformance.md). |
| `ORKEXP002` | **Orchestration Autonomous** — `AgentExecutionBudget` et ses types de budget, `IAgentChannel`/`InMemoryAgentChannel` et ses records de requête/réponse, `AutonomousProcessStrategy`, `SpawnAgentTool` et ses records de requête/réponse | Le mode d'orchestration le plus jeune : dimensions du budget, sémantique du spawn et contrats du canal A2A peuvent encore bouger avec le retour terrain. |
| `ORKEXP003` | **RAG correctif** — `CorrectiveRagPipeline` (+ `CorrectiveRagPipelineDependencies`, `RagGraphState`), `IRetrievalEvaluator`, `IGroundednessChecker`, `IWebDocumentRetriever`, les implémentations heuristiques et LLM des évaluateurs/vérificateurs, et `CorrectiveRagExtensions` (`AddOrkeonCorrectiveRag`) | Les contrats de la boucle CRAG (verdicts, bornes de re-boucle, politique de repli web) sont calibrés sur un corpus d'évaluation jeune. |
| `ORKEXP004` | **Intégration MCP** — `McpClient`, `McpServer`, `McpToolProvider`/`McpToolAdapter`, les transports (`IMcpTransport`, `StdioMcpTransport`, `SseMcpTransport`), les options, les types JSON-RPC et du protocole, et `McpServiceExtensions` (`AddOrkeonMcp`) | Dual-era depuis PUB-07 (`2026-07-28` moderne + révisions legacy à initialize), mais des pans de la surface moderne restent non implémentés (`subscriptions/listen`, requêtes multi-aller-retour, OAuth) et les types filaires peuvent encore bouger. |

Tout ce qui n'est pas marqué `[Experimental]` et figure dans le `PublicAPI.Shipped.txt`
d'un paquet est couvert par l'engagement de stabilité : y toucher est un **breaking
change**, traité comme tel par la politique de versionnement.

L'attribut est posé sur les types : le diagnostic se déclenche là où votre code **nomme**
l'un d'eux. Enregistrer les points d'entrée stables ne le déclenche pas :
`AddOrkeonInfrastructure(configuration)` câble MCP et `AddOrkeonRag(configuration)` câble le
graphe correctif sans que votre code référence un type expérimental — seuls l'appel à
`AddOrkeonMcp`, `AddOrkeonA2A`, `AddOrkeonCorrectiveRag` ou la résolution/l'implémentation de
l'un des types ci-dessus exigent l'opt-in.

La **valeur** `ProcessType.Autonomous` elle-même n'est pas expérimentale — sélectionner
le mode depuis le YAML continue de fonctionner ; l'attribut couvre les types .NET
référencés depuis le code.

À l'intérieur de ce dépôt (`src/`, `tests/`, `examples/`), les quatre IDs sont
supprimés centralement, dans chacun des trois `Directory.Build.props` : le framework câble — et ses tests et exemples exercent — ses
propres surfaces expérimentales à dessein.
