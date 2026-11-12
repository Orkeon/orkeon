> 🇬🇧 [English version](../../reference/experimental-apis.md)

# APIs expérimentales

Certaines surfaces d'Orkeon sont livrées pour recueillir des retours mais ne sont **pas
encore couvertes par l'engagement de stabilité API** (voir la
[politique de stabilité et de versionnement](./stability.md)).
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
| `ORKEXP001` | **A2A (protocole agent-à-agent)** — `IA2AClient`, `IA2AServer`, `IA2AAgentDiscovery`, `IA2ATaskRouter`, `IA2ATaskStore`, `IAgentRegistrationStore`, les implémentations `A2A*`, leurs options et types filaires, `StateStoreA2ATaskStore`, et `A2AExtensions` (`AddOrkeonA2A`, `AddOrkeonA2ATaskPersistence`) | L'implémentation précède la spécification A2A v1.0 ; elle a la persistance des tâches et une position tranchée sur la révocation de certificats, et l'alignement des bindings vers la v1.0 (HTTP+JSON d'abord) reste à faire — voir la [matrice de conformité](./a2a-conformance.md). |
| `ORKEXP002` | **Orchestration Autonomous** — `AgentExecutionBudget` et ses types de budget, `IAgentChannel`/`InMemoryAgentChannel` et ses records de requête/réponse, `AutonomousProcessStrategy`, `SpawnAgentTool` et ses records de requête/réponse, et la méthode `CrewBudget.ToExecutionBudget()` (le type `CrewBudget` lui-même est stable) | Le mode d'orchestration le plus jeune : dimensions du budget, sémantique du spawn et contrats du canal A2A peuvent encore bouger avec le retour terrain. |
| `ORKEXP003` | **RAG correctif** — `CorrectiveRagPipeline` (+ `CorrectiveRagPipelineDependencies`, `RagGraphState`), `IRetrievalEvaluator`, `IGroundednessChecker`, `IWebDocumentRetriever`, les implémentations heuristiques et LLM des évaluateurs/vérificateurs, et `CorrectiveRagExtensions` (`AddOrkeonCorrectiveRag`) | Les contrats de la boucle CRAG (verdicts, bornes de re-boucle, politique de repli web) sont calibrés sur un corpus d'évaluation jeune. |
| `ORKEXP004` | **Intégration MCP** — `McpClient`, `McpServer`, `McpToolProvider`/`McpToolAdapter`, les transports (`IMcpTransport`, `StdioMcpTransport`, `StreamableHttpMcpTransport`), les options, les types JSON-RPC et du protocole, et `McpServiceExtensions` (`AddOrkeonMcp`, `AddOrkeonMcpServer`) | Double ère (la révision moderne `2026-07-28` plus les révisions legacy à initialize), mais des pans de la surface moderne restent non implémentés (requêtes multi-aller-retour) et les types filaires peuvent encore bouger. |
| `ORKEXP005` | **Capacités par modèle** — `IModelCapabilityRegistry`, `ModelCapabilityRegistry`, `ModelCapabilities` et `ModelCapabilityOverride` (audio en entrée et en sortie, vision, efforts de raisonnement, par préfixe d'identifiant de modèle), lus par `ContentConverter`, les fournisseurs compatibles OpenAI et le registre de profils ; l'hôte la surcharge par `Llm:ModelCapabilities:<id>` | La table intégrée est un instantané des familles des éditeurs et les listes d'efforts changent à chaque version ; la forme d'une entrée (audio en sortie, limites par modèle au-delà des efforts) peut encore grandir. |
| `ORKEXP006` | **Sécurité HTTP du host** — `HttpRequestAuthenticator`, `HttpAuthorization` et `HttpSecurityOptions`, l'authentification d'un listener HTTP extraite du serveur A2A pour l'API du host | Nouvelle, et façonnée par son seul consommateur à ce jour (`orkeon-host`) : le type de verdict et les options peuvent bouger quand un second listener s'en servira. |
| `ORKEXP007` | **Ports d'historique de run** — `IRunEventSink`, `IRunHistoryReader`, `RunEvent`, `RunEventScope`, `RunQuery`, `RunSummary`, `RunStatus`, `RunHistoryStore`, `RunHistoryOptions`, `RunHistoryRetention`, `RunEventWire`, `RunEventSinkExtensions`, `SqliteRunEventSink`, `RedactingRunEventSink`, et les types de vivacité `RunLiveness`, `RunProcessIdentity`, `IRunProcessProbe` et `SystemRunProcessProbe`, et la propriété `ScriptingHostPorts.RunEvents` (le type `ScriptingHostPorts` lui-même est stable) | Les ports sont neufs : la forme de l'événement, la requête et le résumé peuvent bouger quand l'API de l'hôte lira l'historique en HTTP. |

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

À l'intérieur de ce dépôt, les IDs sont supprimés centralement : les sept dans `src/` et `tests/`,
`ORKEXP001` à `ORKEXP005` dans `examples/` (le `Directory.Build.props` de chaque dossier) — le framework câble,
et ses tests et exemples exercent, ses propres surfaces expérimentales à dessein.
