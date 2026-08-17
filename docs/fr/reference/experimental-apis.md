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
| `ORKEXP001` | **A2A (protocole agent-à-agent)** — `IA2AClient`, `IA2AServer`, `IA2AAgentDiscovery`, `IA2ATaskRouter`, `IAgentRegistrationStore`, les implémentations `A2A*` et `AddOrkeonA2A` | L'implémentation précède la spécification A2A v1.0.1 ; la passe de conformité (PUB-08) en remaniera des pans (persistance des tâches, révocation de certificats). |
| `ORKEXP002` | **Orchestration Autonomous** — `AgentExecutionBudget` et ses types de budget, `IAgentChannel`/`InMemoryAgentChannel`, `AutonomousProcessStrategy`, `SpawnAgentTool` | Le mode d'orchestration le plus jeune : dimensions du budget, sémantique du spawn et contrats du canal A2A peuvent encore bouger avec le retour terrain. |
| `ORKEXP003` | **RAG correctif** — `CorrectiveRagPipeline`, `IRetrievalEvaluator`, `IGroundednessChecker` et les implémentations d'évaluateurs/vérificateurs `Corrective` | Les contrats de la boucle CRAG (verdicts, bornes de re-boucle, politique de repli web) sont calibrés sur un corpus d'évaluation jeune. |
| `ORKEXP004` | **Intégration MCP** — `McpClient`, `McpServer`, transports, options et types du protocole | Épinglée sur la version de protocole `2024-11-05` ; la mise à niveau vers la spécification MCP courante (PUB-07) changera la surface filaire. |

Tout ce qui n'est pas marqué `[Experimental]` et figure dans le `PublicAPI.Shipped.txt`
d'un paquet est couvert par l'engagement de stabilité : y toucher est un **breaking
change**, traité comme tel par la politique de versionnement.

La **valeur** `ProcessType.Autonomous` elle-même n'est pas expérimentale — sélectionner
le mode depuis le YAML continue de fonctionner ; l'attribut couvre les types .NET
référencés depuis le code.

À l'intérieur de ce dépôt (`src/`, `tests/`, `examples/`), les quatre IDs sont
supprimés centralement : le framework câble — et ses tests et exemples exercent — ses
propres surfaces expérimentales à dessein.
