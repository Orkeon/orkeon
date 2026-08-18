> 🇬🇧 [English version](../../getting-started/default-behaviors.md)

# Comportements par défaut (et comment les remplacer)

> **Voir aussi** : [Bootstrap et exécution](./bootstrap.md) · [Sous-systèmes opt-in](../reference/opt-in-subsystems.md) · [Limites et contraintes](../reference/limitations.md) · [Retour à l'index](../INDEX.md)

Les défauts DI d'Orkeon suivent un principe : **aucune magie implicite**. Rien
n'appelle un LLM que vous n'avez pas configuré, rien ne persiste là où vous n'avez
pas pointé, et chaque défaut délibérément minimal **s'annonce par un Warning
unique** qui nomme le geste de remédiation. Cette page est l'inventaire de ces
défauts — ce que chacun fait, comment il signale qu'il est actif, et le geste exact
pour le remplacer.

Tous les enregistrements ci-dessous utilisent `TryAdd` : enregistrer votre propre
implémentation **avant** `AddOrkeonApplication()` / `AddOrkeonInfrastructure()`
gagne, sans autre changement.

## Les défauts qui avertissent au premier usage

| Service | Défaut | Ce qu'il fait réellement | Le remplacer par |
|---|---|---|---|
| `IAgentPlanner` | `AgentPlannerService` | Émet le **même plan fixe en 4 étapes** pour chaque tâche (confiance 0,8). Le raffinement et la validation sont réels ; la *création* de plan ignore le contenu de la tâche. | `services.AddSingleton<IAgentPlanner, VotrePlanner>();` — un planner adossé au LLM est à l'étude pour V1.x. |
| `ITaskDelegator` | `NullTaskDelegator` | **Refuse toute demande de délégation** ; `FindBestAgentForTaskAsync` retourne le premier agent disponible. La délégation Hierarchical/Autonomous reste inerte tant qu'il n'est pas remplacé. | `services.AddSingleton<ITaskDelegator, VotreDelegator>();` |
| `IKnowledgeStore` | `InMemoryKnowledgeStore` | Store de connaissances no-op — rien n'est embarqué ni récupéré. | Activer le sous-système RAG (`AddOrkeonRag(configuration)`) et utiliser son ingestion/récupération, ou enregistrer votre propre store. |
| `IAgentExecutionService` | `NullAgentExecutionService` | Placeholder — les hôtes doivent le surcharger (le runner host le fait). | `services.AddScoped<IAgentExecutionService, VotreService>();` |
| `IAgentSelectionService` (FirstFit) | `SimpleAgentSelectionService` | Prend le **premier agent disponible** — le repli sûr explicite. | Passer `OrkeonApplicationOptions.AgentSelectionStrategy` à `Skill` (lexical) ou `Embedding` (sémantique — exige un vrai fournisseur d'embeddings). |
| `IEmbeddingProvider` | chaîne de résolution | BGE local (quand `AddOrkeonLocalEmbeddings()` est enregistré) → fournisseur distant via `Orkeon:Embeddings` → **fail-fast au premier usage** avec une exception actionnable. Jamais de repli hash silencieux. | Enregistrer `AddOrkeonLocalEmbeddings()` ou configurer `Orkeon:Embeddings`. |

## Les défauts silencieux (à dessein)

Inoffensifs par construction, ils restent en Debug ou n'émettent rien :

| Service | Défaut | Pourquoi le silence convient |
|---|---|---|
| `ILlmCache` | `NullLlmCache` | Un cache qui rate toujours change le coût, pas la justesse. |
| `IYamlDiffService` | `NullYamlDiffService` | Le diff est un confort d'observabilité optionnel. |
| `ITemplateInstantiator` | `NullTemplateInstantiator` | **Lève** `NotSupportedException` s'il est réellement utilisé — plus bruyant que n'importe quel log. |
| `IToolRegistry` | `InMemoryToolRegistry` | Registre vide ; le runner host le remplace par le registre adossé à la DI. Les crews qui référencent des outils échouent bruyamment sous `StrictTools`. |
| `IMemorySystem` | `InMemoryMemorySystem` | Vraie implémentation en mémoire — correcte, juste pas persistante. |
| Callbacks step/task | `Null*Callback` | Hooks d'observabilité no-op ; Debug par conception. |

## Pourquoi c'est conçu ainsi

Un framework qui glisserait silencieusement un appel LLM, une dépendance réseau ou
un store persistant derrière un défaut prendrait des décisions qui vous
appartiennent. La contrepartie : un hôte fraîchement bootstrappé en fait moins
qu'un hôte configuré — et le dit dans le log, une fois par service, avec le
correctif dans le message. Si vous voyez l'un de ces warnings en production, ce
n'est jamais du bruit : une capacité que vous attendez probablement tourne sur son
remplaçant minimal.
