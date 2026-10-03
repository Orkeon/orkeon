> 🇬🇧 [English version](../../orchestration/process-types.md)

# Guide comparatif des ProcessTypes

> **Prérequis** : [Vue d'ensemble](../getting-started/overview.md) et [YAML et Builders](../getting-started/yaml-and-builders.md)

---

## Vue d'ensemble

Orkeon propose **6 stratégies d'orchestration** via le value object `ProcessType` (Domain layer). Chaque stratégie définit comment les agents se coordonnent pour exécuter les tâches d'une Crew. Le choix du ProcessType est le levier architectural le plus impactant sur le comportement d'un système multi-agents.

```csharp
// Orkeon.Domain.SharedKernel.ValueObjects.ProcessType (sealed record)
ProcessType.Sequential    // Pipeline linéaire
ProcessType.Hierarchical  // Manager + workers
ProcessType.Parallel      // Vagues de dépendances, concurrence dans une vague
ProcessType.Consensual    // Chaque agent exécute chaque tâche, puis un vote
ProcessType.Graph         // Graphe d'états avec cycles de retry contrôlés
ProcessType.Autonomous    // Auto-organisation sous budget
```

En YAML, le mode est `process:` (insensible à la casse, `sequential` en son absence) ; une valeur inconnue fait échouer le chargement en nommant les six valeurs valides (`YamlCrewMapper.ParseProcessType`). Avec le Fluent Builder, `CrewBuilder` offre `.Sequential()`, `.Hierarchical(manager)`, `.Parallel()` et `.Consensual()` ; Graph et Autonomous passent par `.Process(ProcessType.Graph)` / `.Process(ProcessType.Autonomous)`.

### Architecture d'implémentation

```
ICrewOrchestrationService.KickoffAsync (SequentialCrewOrchestrator)
     │  planification (si activée, tenue dans la portée de plan du run), puis un switch sur crew.ProcessType
     ▼
IProcessStrategyFactory.CreateStrategy(ProcessType)   (ProcessStrategyFactory, Infrastructure)
     │
     ├── SequentialProcessStrategy      ← ExecuteSequentialAsync
     ├── HierarchicalProcessStrategy    ← ExecuteHierarchicalAsync(id du manager)
     ├── ParallelProcessStrategy        ← ExecuteParallelAsync
     ├── ConsensualProcessStrategy      ← ExecuteSequentialAsync
     ├── GraphProcessStrategy           ← ExecuteSequentialAsync
     └── AutonomousProcessStrategy      ← ExecuteAutonomousAsync(AgentExecutionBudget.Permissive)
```

`IProcessStrategy` (Domain) a quatre points d'entrée — `ExecuteSequentialAsync`, `ExecuteHierarchicalAsync`, `ExecuteParallelAsync`, `ExecuteAutonomousAsync`. `SequentialCrewOrchestrator` choisit le point d'entrée d'après `ProcessType.Value` ; Graph et Consensual réutilisent le point d'entrée séquentiel, et une stratégie lève `NotSupportedException` sur ceux qu'elle ne sert pas. Les stratégies sont enregistrées en scoped par `AddOrkeonInfrastructure()` (Consensual via `AddOrkeonConsensus()`, qu'il appelle).

Ce que toutes les stratégies partagent :

- **Ordre des tâches** — les modes qui distribuent les tâches l'une après l'autre (tous sauf Parallel) les exécutent dans l'ordre résolu par `CrewTaskSequencer` : un tri topologique stable sur les `dependencies` déclarées (détaillé sous Sequential). Un plan ne le change jamais, dans aucun mode.
- **Planification** — `planning: true` (YAML), `.Planning(true)` (C#) ou `crewBuilder().planning()` (`.ork.ts`) fait écrire par un planificateur un plan pas à pas pour chaque tâche avant la première, et chaque tâche lit son propre plan dans son prompt — dans les six modes. Le plan ne change ni l'ordre des tâches ni qui les exécute. Voir [Planification](#planification-planning-true) plus bas.
- **Hooks de cycle de vie** — chaque mode rend compte via `ICrewExecutionHook` (`OnTaskStartedAsync`, `OnTaskCompletedAsync`, `OnCrewCompletedAsync`, `OnCrewFailedAsync`), sur toutes les sorties, annulation comprise ; c'est ce qui alimente `AUTO_SUMMARY.md`, le flux `orkeon run --events` et la progression affichée par l'hôte.
- **Issue** — une tâche échouée fait échouer la crew et saute ses dépendantes, dans tous les modes (détaillé sous Sequential) : `Success = false`, une `Error` qui nomme chaque tâche échouée ou sautée, un hook `Failed`, code de sortie 2.
- **Télémétrie des tokens** — l'usage réel (prompt, complétion, hits/misses de cache quand le fournisseur les rapporte) voyage dans les métadonnées de `CrewOutput` ; `CrewOutput.TokensUsed` reste `null` quand rien n'a été mesuré.

### Qui exécute une tâche qui ne nomme aucun agent

Un `agent:` au niveau de la tâche est une décision, pas une suggestion : Sequential, Parallel et Graph le respectent toujours. Pour une tâche sans agent, ces trois modes interrogent `TaskAgentSelector`, piloté par `OrkeonApplicationOptions.AgentSelectionStrategy` :

| Valeur | Comportement |
|--------|--------------|
| `FirstFit` (défaut) | Round-robin sur les agents de la crew |
| `Embedding` | Correspondance sémantique entre la tâche et le profil de chaque agent (exige un vrai fournisseur d'embeddings — voir les [limitations](../reference/limitations.md)) |
| `Skill` | Correspondance lexicale (Jaccard) sur les mots-clés rôle/objectif/backstory, sans fournisseur d'embeddings |

Une sélection qui échoue, ou qui désigne un agent absent de la crew, retombe sur le round-robin avec un avertissement. Hierarchical et Autonomous laissent choisir le LLM manager (l'`agent:` d'une tâche n'y est pas consulté) ; Consensual fait exécuter chaque tâche par tous les agents.

### Planification (`planning: true`)

`planning: true` (YAML), `.Planning(true)` (C#) ou `crewBuilder().planning()` (`.ork.ts`) — le `planning=True` de CrewAI, coupé par défaut. Avant la première tâche, un planificateur écrit un **plan pas à pas pour chaque tâche**, et chaque tâche lit ensuite son propre plan dans son prompt.

- **Sur quel modèle** — le fournisseur que fixe `CrewBuilder.WithPlanningLlm` (C# ; il exige `.Planning()` : un fournisseur de planification sur une crew qui ne planifie pas est refusé à sa construction), sinon le **profil par défaut** de l'hôte — la section `Llm` : le planificateur y reste, quels que soient les profils que nomment les agents. Un appel par run, à la température 0,3, compté sous `operation: planning` dans `cost.updated` — le fournisseur C# comme un fournisseur que l'hôte enregistre —, et annulé avec le run.
- **Ce qu'il lit** — le but de la crew, une ligne sur la façon dont le mode ordonne les tâches et choisit leurs agents, les variables du run (`initial_context` compris), et chaque tâche **par numéro**, dans l'ordre où le run les prend : sa description et sa sortie attendue avec les variables dedans, telles que son agent les lit ; ses dépendances, par numéro ; et son agent — rôle, but, noms des outils qu'il tient pour la tâche — là où le mode exécute une tâche sur l'agent qu'elle nomme (Sequential, Parallel, Graph). Ailleurs, le planificateur apprend ce que fait le mode à la place — « confié par le manager » (Hierarchical, Autonomous), « chaque agent répond » (Consensual), « choisi au run » (une tâche qui ne nomme pas d'agent) —, et les agents de la crew sont listés une fois. Jamais un identifiant : le planificateur répond par numéro. Borné (`PlanningDefaults`) : une description à 1 500 caractères, une sortie attendue à 500, le but d'un agent à 300, 15 noms d'outils par agent, les variables à 2 000 en tout — un texte coupé finit par ` […]`. Ni backstory, ni connaissance, ni mémoire.
- **Ce qu'il écrit** — `{"plans": [{"task": 1, "plan": "1. …"}]}` : au plus 8 étapes numérotées par tâche, qui nomment les outils à employer là où un outil aide. La forme de la réponse est contrainte autant que le fournisseur le peut (`json_schema`, sinon `json_object`, sinon le prompt seul) et lue avec tolérance — un bloc entre ```` ``` ```` ou une phrase autour de l'objet.
- **Où il va** — dans le prompt utilisateur de la tâche, après la tâche (description, sortie attendue, livrable) et avant les variables de contexte, sous *Plan for this task, from the crew's planner — follow it where it helps; the task above prevails:*, coupé à 2 000 caractères. Le Guardian le filtre avec le reste du prompt, et le plan de chaque tâche est journalisé sur une ligne en `Information` (`--verbose 1`). La tâche elle-même n'est jamais modifiée : la requête de connaissance, le souvenir rangé et les prompts du manager lisent la tâche, pas son plan. Le run tient son plan dans une portée à lui (`CrewPlanScope`), lue là où les exécutions de chaque mode composent leur prompt — deux runs d'une même crew ne lisent jamais le plan l'un de l'autre.
- **Dans chaque mode** — Sequential, Parallel et Graph (chaque essai) : l'agent de la tâche. Hierarchical : le travailleur que choisit le manager, et chaque révision qu'il demande ; les prompts du manager ne changent pas. Consensual : chaque réponse candidate — un bulletin, tâche à part, non. Autonomous : l'agent qui prend la tâche, et le pair à qui elle est confiée après un échec, qui reprend la tâche elle-même.
- **Il ne change ni l'ordre ni les agents** — avec ou sans plan, une crew exécute les mêmes tâches, dans le même ordre, sur les mêmes agents. (Un plan rendait un ordre, des affectations d'agents et des groupes parallèles ; son ordre était suivi sous les dépendances, si bien que la sortie de la crew, le contexte d'une tâche et le tourniquet de Parallel en dépendaient.)
- **Quand ça se passe mal** — le plan est un conseil. Une réponse illisible est redemandée une fois, avec ce qui n'a pas pu être lu, puis la crew tourne sans plan, avec un avertissement — qui le dit quand la réponse s'est arrêtée au `MaxTokens` du profil. Une tâche que le plan oublie s'exécute sans plan, avec un avertissement qui la nomme ; un numéro donné deux fois garde son premier plan et un numéro inconnu est ignoré, chacun avec un avertissement. Seul un fournisseur en panne — une clé refusée, un point d'accès injoignable — fait échouer le run, avant sa première tâche, avec la raison du fournisseur.
- **Sur le fournisseur écho** — celui d'un hôte sans section `Llm`, qui renvoie son prompt au lieu d'y répondre (`LlmProviderCapabilities.ReplaysPrompt`) — la planification est sautée avec un avertissement (*planning skipped — the echo provider cannot plan*, avec le remède, `orkeon init`), et la crew va au bout comme tout run sans clé.
- **Coût** — un appel par run (deux après une réponse illisible), donc un par entrée sous `KickoffForEachAsync` et un par message dans `orkeon-host`, dont le prompt croît avec la crew ; et le plan de chaque tâche voyage avec chaque tour de la boucle de son agent, ce que la borne de 2 000 caractères contient.
- **En streaming aussi** — un run en streaming (`KickoffStreamingAsync`, une API C#) est le même run : il planifie, et chaque tâche lit son plan, comme avec `KickoffAsync` (GAP-32).

**Classes clés** : `CrewPlanner` et `PlanningContext` (Domain), `ExecutionPlanParser`, `CrewPlanScope` et `AgentPromptComposer`.

---

## Matrice de comparaison rapide

| Critère | Sequential | Hierarchical | Parallel | Consensual | Graph | Autonomous |
|---------|-----------|-------------|---------|-----------|-------|-----------|
| **Modèle d'exécution** | Linéaire | Linéaire + revue du manager | Vagues de dépendances, concurrence dans une vague | Tous les agents par tâche + bulletins de pairs | Linéaire + cycle de retry | Assignation par le manager, délégation sur échec |
| **Choix de l'agent** | Déclaré, sinon sélecteur | LLM manager | Déclaré, sinon sélecteur | Tous les agents | Déclaré, sinon sélecteur | LLM manager |
| **Dépendances entre tâches** | Ordre + saut sur échec | Ordre + saut sur échec | Vagues + saut sur échec | Ordre + saut sur échec | Ordre + saut sur échec | Ordre + saut sur échec |
| **Une tâche échouée fait échouer la crew** | ✅ (dépendantes sautées) | ✅ (dépendantes sautées) | ✅ (dépendantes sautées) | ✅ (dépendantes sautées) | ✅ après ses retries (dépendantes sautées) | ✅ (dépendantes sautées ; un budget épuisé aussi) |
| **Circuit breaker** | — | — | — | — | ✅ 3 mécanismes | — |
| **Budget d'exécution** | — | — | — | — | — | ✅ 5 dimensions (Permissive) |
| **Retry automatique** | — | Jusqu'à 2 ré-exécutions après revue | — | Rounds de vote | ✅ `maxRetryCycles` | Une délégation à un pair |
| **`asyncExecution: true` d'une tâche** | ✅ Tourne pendant les tâches suivantes | Refusé au chargement | Accepté, sans effet propre | Refusé au chargement | Refusé au chargement | Refusé au chargement |
| **Manager** (`managerAgent:` ; `WithManagerLlm` en C#) | Refusé au chargement | ✅ Un agent (requis) ou, en C#, un LLM : il assigne et relit | Refusé au chargement | ✅ Un agent : l'arbitre de `ManagerDecision` (un LLM est refusé) | Refusé au chargement | Un LLM seulement : le profil par défaut, ou `WithManagerLlm` en C# (un agent est refusé) |
| **`planning: true` : chaque tâche lit son plan** | ✅ | ✅ Le travailleur, chaque révision | ✅ | ✅ Chaque candidate | ✅ Chaque essai | ✅ Celui qui la prend, le pair |
| **Complexité** | ⭐ | ⭐⭐ | ⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ |
| **Coût LLM relatif** | Bas | Moyen | Bas | Élevé | Bas à moyen | Moyen |
| **Cas d'usage principal** | Pipelines ETL | QA, boucles de revue | Fan-out + synthèse | Plusieurs tentatives indépendantes | Tâches instables à relancer | Exploration, R&D |

---

## 1. Sequential — Pipeline linéaire

### Principe

Les tâches s'exécutent **une par une** — sauf une tâche `asyncExecution: true`, qui tourne pendant les tâches qui la suivent (voir [Tâches asynchrones](#tâches-asynchrones-asyncexecution)). Chaque tâche reçoit en contexte les sorties des tâches exécutées avant elle. L'assignation des agents suit la règle ci-dessus : l'`agent:` de la tâche s'il est déclaré, sinon le sélecteur configuré (round-robin par défaut).

**Ordre d'exécution** — avec ou sans plan, qui ne le change jamais : les tâches s'exécutent dans un **ordre topologique stable sur leurs `dependencies` déclarées** — une tâche passe après toutes celles dont elle dépend, et partout où les dépendances le permettent l'ordre déclaré est conservé, si bien qu'une crew qui ne déclare aucune dépendance s'exécute exactement comme écrite. Cela vaut dans toutes les dispositions : la disposition multi-fichiers (`tasks/*.yaml`) liste les tâches dans l'ordre ordinal de leurs noms de fichier, si bien que sans ce tri `consolidate.yaml` passait avant l'`extract.yaml` dont elle dépend. Une dépendance qui nomme un identifiant de tâche inconnu est ignorée ; un cycle ne fait jamais échouer la crew — l'ordre déclaré est conservé pour les tâches prises dedans et un avertissement les nomme. La même règle ordonne les modes hierarchical, consensual, graph et autonomous, qui distribuent eux aussi leurs tâches l'une après l'autre ; le mode parallel garde sa propre sémantique (des **vagues** de dépendances, et un cycle y est refusé).

**Gestion des échecs (tous les modes)** : une tâche dont une dépendance déclarée n'a pas réussi — échouée, ou elle-même sautée — est **sautée**, jamais exécutée sur un contexte qui dit `Task failed: …` là où son entrée aurait dû se trouver : elle apparaît comme `⊘ skipped` dans `AUTO_SUMMARY.md` et comme un événement `task.completed` avec `skipped: true`, les tâches qui n'en dépendent pas s'exécutent quand même, et la crew échoue en nommant chaque tâche échouée ou sautée (LLM-11). La règle est la même dans les six modes : une crew dont une tâche a échoué rend `Success = false`, son hook reçoit `Failed` avec la même raison, et `orkeon run` sort en 2. Un mode qui tolère un échec — Graph et ses retries, Autonomous et sa délégation — le fait avant que la tâche compte comme échouée.

### Tâches asynchrones (`asyncExecution`)

Une tâche `asyncExecution: true` — l'`async_execution` de CrewAI ; `.Async()` sur `CrewTaskBuilder`, `.asyncExecution()` dans un script — est **lancée sans être attendue** : la tâche suivante démarre aussitôt, et les deux tournent ensemble.

- **Une tâche qui en dépend l'attend** — par `dependencies:`, l'équivalent du `context` de CrewAI —, puis lit sa sortie ; si elle a échoué, cette tâche est sautée comme toute dépendante d'une tâche échouée.
- **Sa sortie entre dans le contexte une fois qu'une tâche l'a attendue** : une tâche qui n'en dépend pas démarre sans elle, et toute tâche après cette attente la lit, dans l'ordre déclaré — ce qu'une tâche lit ne dépend jamais du minutage. Une tâche qui en a besoin la déclare.
- **La crew attend toutes les tâches qu'elle a lancées** avant de rendre son issue — rien ne survit au run —, et sa sortie reste celle de la **dernière tâche déclarée**, pas de la dernière à finir ; `CrewOutput.TaskOutputs` liste les tâches dans l'ordre déclaré.
- Un échec suit la règle de tous les modes : la crew échoue et les dépendantes de la tâche sont sautées ; les tâches déjà lancées vont à leur terme, rien n'est annulé en cascade. Un run annulé attend qu'elles s'arrêtent, puis les annule.
- La tâche démarre (`TaskStartedEvent`, `task.started`) quand elle est lancée et se termine (`TaskCompletedEvent` ou `TaskFailedEvent`) quand le run l'attend ; le crochet de fin — `AUTO_SUMMARY.md`, `task.completed` — entend chaque tâche quand elle finit, dans l'ordre où les tâches finissent. Un collègue à qui elle délègue travaille dans son contexte. Il n'y a pas de plafond de concurrence : deux tâches asynchrones appellent leur LLM en même temps.

```yaml
process: sequential
tasks:
  research:
    description: "Research the market"
    expectedOutput: "Market findings"
    asyncExecution: true
  survey:
    description: "Survey the customers"
    expectedOutput: "Survey results"
    asyncExecution: true               # tourne en même temps que research
  synthesis:
    description: "Write the recommendation"
    expectedOutput: "Recommendation"
    dependencies: [research, survey]   # attend les deux, lit les deux
```

`process: parallel` accepte `asyncExecution: true` sans effet propre — toutes les tâches d'une vague tournent déjà en même temps. Hierarchical, Consensual, Graph et Autonomous ordonnent leurs tâches eux-mêmes (le manager, le vote, le graphe, le budget) : une crew qui y écrit `asyncExecution: true` est **refusée au chargement**, chaque tâche nommée, en YAML comme dans un script `.ork.ts`, et `CrewBuilder.Build()` refuse de même en C#. `asyncExecution: false`, la valeur par défaut, se charge partout.

### Mécanisme interne

```
Tâche 1 → Agent A → sortie₁
                       ↓ (contexte enrichi)
Tâche 2 → Agent B → sortie₂
                       ↓
Tâche 3 → Agent C → sortie₃ → résultat final
```

**Classes clés** : `SequentialProcessStrategy`, `CrewTaskSequencer`, `TaskAgentSelector`, `AgentDelegationToolsProvider` (les agents avec `allowDelegation: true` reçoivent `delegate_work_to_coworker` et `ask_question_to_coworker`)

### Configuration YAML

```yaml
# Racine plate — il n'y a pas de clé enveloppe crew:, et agents:/tasks: sont des
# mappings indexés par id (un fichier enveloppé dans crew: se charge SILENCIEUSEMENT
# comme une crew vide).
name: "pipeline"
goal: "Démo séquentielle"
process: sequential
tasks:
  collect:
    description: "Collecter les données"
    expectedOutput: "Données brutes"
  analyze:
    description: "Analyser les données"
    expectedOutput: "Rapport d'analyse"
    dependencies: [collect]
  recommend:
    description: "Générer les recommandations"
    expectedOutput: "Plan d'action"
    dependencies: [analyze]
```

### Configuration Fluent Builder

```csharp
var crew = new CrewBuilder()
    .Sequential()
    .WithAgent(a => a.Role("Collector").Goal("Gather data"))
    .WithAgent(a => a.Role("Analyst").Goal("Analyze data"))
    .WithTask(t => t.Description("Collect").ExpectedOutput("Raw data"))
    .WithTask(t => t.Description("Analyze").ExpectedOutput("Report"))
    .Build();
```

### Avantages

- **Simplicité maximale** : aucune configuration complexe, comportement prévisible
- **Traçabilité** : chaque étape est clairement identifiable dans les logs
- **Contexte cumulatif** : chaque tâche bénéficie des résultats précédents
- **Résultat honnête** : une étape échouée fait échouer la crew et arrête ses dépendantes (comme dans tous les modes)

### Inconvénients

- **Peu de parallélisme** : le temps total est la somme des tâches, sauf celles marquées `asyncExecution`
- **Point de défaillance unique** : un échec bloque la suite de la chaîne — ses dépendantes sont sautées, seules les tâches indépendantes s'exécutent encore
- **Pas de retry** : aucune reprise automatique en cas d'erreur
- **Rigide** : l'ordre est fixe, pas de branchement conditionnel

### Quand l'utiliser

- Pipelines ETL (extraction → transformation → chargement)
- Rédaction séquentielle (recherche → rédaction → relecture)
- Workflows où chaque étape dépend strictement de la précédente
- Prototypage rapide et POC

### Quand ne pas l'utiliser

- Beaucoup de tâches indépendantes qui pourraient toutes tourner en parallèle — Parallel exécute chaque vague de dépendances d'un coup ; `asyncExecution` convient à quelques étapes concurrentes d'un pipeline
- Workflows qui devraient relancer une étape instable (Graph)

---

## 2. Hierarchical — Manager + Workers

### Principe

Un **manager** (`IManagerAgent`, implémenté par `LlmBasedManager`) coordonne les workers. Pour chaque tâche, il choisit un worker via `AssignTaskAsync()`, le worker exécute, puis le manager **revoit la sortie** avec `ReviewOutputAsync()`.

- La crew nomme son manager : `managerAgent:` en YAML (`crewBuilder().manager(agent)` en `.ork.ts`, `.Hierarchical(manager)` / `.WithManager(agent)` en C#) — cet agent est retiré du pool de workers. En C#, `.WithManagerLlm(fournisseur)` (le `manager_llm` de CrewAI) donne au manager un fournisseur à lui : une telle crew se passe d'agent manager, et chaque agent est alors un worker. Une crew qui n'a ni l'un ni l'autre est refusée à sa construction, et un `managerAgent:` qui ne nomme aucun agent de la crew fait échouer le chargement, en listant ses agents.
- Un manager n'a de sens qu'à deux autres endroits : un agent manager arbitre le vote d'une crew Consensual, un LLM manager distribue les tâches d'une crew Autonomous. Sequential, Parallel et Graph n'ont pas de manager, Autonomous pas d'agent manager et Consensual pas de LLM manager : une telle crew est refusée à son chargement ou à sa construction, le message nommant la clé et le mode — l'agent n'aurait été qu'un worker de plus, le fournisseur jamais appelé.
- Les décisions du manager passent par **le LLM que la crew lui donne**, résolu une fois par run : le fournisseur posé par `WithManagerLlm` (C# ; compté comme un fournisseur que l'hôte enregistre), sinon le fournisseur que porte l'agent manager (C# : `.WithLlm(fournisseur)`, ou un agent Microsoft Agent Framework par `.WithAgentFrameworkAgent(agent)` ; compté de même, nommé `provider:<nom>`), sinon le bloc `llm:` de l'agent manager — son profil et son modèle, un modèle non précisé étant celui du profil (`llm: { profile: claude }`) — sinon le **profil par défaut** de l'hôte. Un profil que l'hôte n'offre pas fait échouer le chargement de la crew, comme pour tout agent. Les appels sont comptabilisés sous le rôle du manager (`operation: manager`). Le YAML n'a pas de clé `manager_llm` : le bloc `llm:` de l'agent manager en tient lieu.
- Assignation : le LLM manager répond en JSON ; une réponse illisible retombe sur une heuristique rôle/mots-clés, une erreur du LLM sur le premier worker. L'`agent:` d'une tâche n'est pas consulté.
- Revue : jusqu'à **3 revues par tâche**, donc au plus **2 ré-exécutions** (chacune avec une variable de contexte `revision_feedback`). Un troisième rejet conserve la dernière sortie, préfixée `[NEEDS REVISION]` et marquée en échec — et la tâche fait échouer la crew. Une revue qui échoue vaut approbation.
- Une tâche assignée à un agent absent de la crew ne s'exécute jamais, et fait échouer la crew. Une tâche dont une dépendance a échoué est sautée sans consulter le manager.

**Ce que la crew mémorise** (`memory: true`) : la sortie que le manager a acceptée, une fois, sous l'agent auquel la tâche a été assignée — jamais un essai rejeté. Chaque essai s'exécute avec `SimpleExecutionContext.StoreResultInMemory` à faux (il rappelle quand même les souvenirs de la crew : il répond à la tâche), et la stratégie range la sortie acceptée via `IMemoryCoordinator`. Rien n'est rangé quand le manager rejette les trois essais, ni quand le travailleur échoue ; une revue qui échoue vaut approbation, et cette sortie est alors rangée. Voir [Système de mémoire](../architecture/memory-system.md#la-mémoire-dune-crew--provider-et-portée).

### Mécanisme interne

```
                  ┌─ Manager (LLM de la crew) ────┐
                  │   AssignTaskAsync()            │
                  │   ReviewOutputAsync()          │
                  └────────┬───────────────────────┘
                           │
            ┌──────────────┼──────────────┐
            ▼              ▼              ▼
        Worker A       Worker B       Worker C
        (choisi)
            │
            ▼
        Sortie → Revue → approuvée ? → oui → tâche suivante
                           → non → ré-exécution (max 2), puis [NEEDS REVISION]
```

**Classes clés** : `HierarchicalProcessStrategy`, `IManagerAgent`, `LlmBasedManager`, `TaskAssignment` (`TaskId`, `AssignedAgent`, `Reason`, `AssignedAt`)

**Interface `IManagerAgent`** (`Orkeon.Application.Interfaces`) :
- `AssignTaskAsync(task, availableAgents, context, llm)` → `TaskAssignment`
- `ReviewOutputAsync(output, originalTask, llm, cancellationToken)` → `bool` (approuvée ou non) ; le jeton est celui du run : Ctrl+C, `RunTimeout` et `/stop` arrêtent une revue

`llm` est le `ManagerLlm` que la stratégie a résolu pour le run (`ManagerLlmResolver`) : le client de chat que le manager interroge, le modèle qu'il demande, et son nom dans le journal (`provider:<nom>` ou `profile:<nom>`). Chacun de ses appels attend d'abord son tour dans la fenêtre `maxRpm` de l'agent manager et dans celle de la crew (GAP-38).

### Configuration YAML

```yaml
name: "delivery-team"
goal: "Démo hiérarchique"
process: hierarchical
managerAgent: lead          # la clé de l'agent manager sous agents: (il n'existe pas de clé manager_llm)
llm:                        # LLM par défaut de la crew, appliqué aux agents qui n'en ont pas
  model: gpt-4o
agents:
  lead:
    role: "Tech Lead"
    goal: "Coordonner la livraison"
    llm:                    # le manager assigne et revoit sur ce LLM :
      profile: claude       # un des profils de l'hôte (Llm:Profiles:claude),
      model: claude-sonnet-5  # sur ce modèle (non précisé, le gpt-4o de la crew s'appliquerait)
  dev:
    role: "Développeur senior"
    goal: "Écrire le code de production"
  qa:
    role: "Ingénieur QA"
    goal: "Tester et valider"
```

### Avantages

- **Allocation intelligente** : le manager choisit le worker le plus adapté à chaque tâche
- **Contrôle qualité intégré** : boucle de revue automatique
- **Traçabilité** : chaque assignation porte une justification (`TaskAssignment.Reason`)

### Inconvénients

- **Surcoût LLM** : un appel d'assignation + un à trois appels de revue par tâche, en plus des workers
- **Goulot d'étranglement** : tout passe par le manager (pas de parallélisme)
- **Révisions fixes** : 3 revues par tâche, en dur

### Quand l'utiliser

- Revue de code (le manager assigne le relecteur le plus compétent)
- Projets aux spécialistes hétérogènes (dev, QA, design, rédaction)
- Situations où la qualité prime sur la vitesse

### Quand ne pas l'utiliser

- Tâches homogènes (le round-robin suffit)
- Contraintes strictes de coût LLM
- Workflows à haute fréquence (le manager est un goulot)

---

## 3. Parallel — Vagues de dépendances

### Principe

Les tâches sont groupées en **vagues de dépendances**. Une vague contient toutes les tâches dont les `dependencies` déclarées sont déjà satisfaites ; ses tâches s'exécutent **en concurrence** (`Task.WhenAll`), et la vague suivante démarre quand elles sont toutes terminées, en **lisant leurs sorties** comme contexte. Une crew qui ne déclare aucune dépendance forme une seule vague — un fan-out à plat.

- Une dépendance qui nomme une tâche absente de la crew compte comme satisfaite.
- Un **cycle de dépendances est refusé** : l'exécution échoue en nommant les tâches prises dedans.
- Une tâche échouée n'arrête pas ses voisines de vague ; ses dépendantes des vagues suivantes sont **sautées**, et la crew échoue en nommant chaque tâche échouée ou sautée.
- Il n'y a pas de plafond de concurrence : toutes les tâches d'une vague appellent leur LLM en même temps.
- Le `asyncExecution: true` d'une tâche est accepté et ne change rien : sa vague tourne déjà en même temps.

### Mécanisme interne

```
Vague 1 ──┬── Tâche A → Agent 1 ──┐
          └── Tâche B → Agent 2 ──┤  (concurrentes)
                                  ▼
Vague 2 ────── Tâche C (dépend de A, B) → lit A + B → résultat final
```

**Classes clés** : `ParallelProcessStrategy`, `TaskAgentSelector`

### Configuration YAML

```yaml
name: "market-scan"
goal: "Démo parallèle"
process: parallel
tasks:
  france:
    description: "Analyser le marché français"
    expectedOutput: "Rapport France"
  germany:
    description: "Analyser le marché allemand"
    expectedOutput: "Rapport Allemagne"
  synthesis:
    description: "Comparer les deux marchés"
    expectedOutput: "Synthèse comparative"
    dependencies: [france, germany]   # deuxième vague, lit les deux rapports
```

### Avantages

- **Vitesse** : temps total = somme de la tâche la plus longue de chaque vague
- **Simplicité** : aucune coordination au-delà des dépendances déclarées
- **Isolation** : l'échec d'une tâche n'affecte pas ses voisines de vague

### Inconvénients

- **Ordonnancement grossier** : une tâche attend toute sa vague, pas seulement ce qu'elle a déclaré
- **Pics de consommation d'API** : toutes les requêtes LLM d'une vague partent en même temps (rate limiting)
- **Pas de retry** : une tâche échouée fait échouer la crew, et saute ce qui en dépend

### Quand l'utiliser

- Analyses multi-marchés ou multi-sources indépendantes
- Génération de contenu en lot (un article par marché, par langue)
- Un fan-out suivi d'une synthèse : les collecteurs tournent ensemble, la synthèse les lit

### Quand ne pas l'utiliser

- Relance de tâches instables (utilisez Graph)
- API au rate limiting strict

---

## 4. Consensual — Vote et consensus

### Principe

Pour chaque tâche, **chaque agent de la crew l'exécute** (en concurrence). Puis **chaque agent remplit un bulletin** : il classe les réponses réussies des autres agents, anonymisées sous des étiquettes (`A`, `B`, …). L'`IVotingStrategy` configurée dépouille les bulletins. Si aucun consensus n'est atteint et que `EnableDiscussion` est actif, un nouveau round a lieu où chaque agent voit les réponses précédentes des autres (une variable de contexte `discussion_context`). Après `MaxVotingRounds` rounds sans consensus, la `FallbackStrategy` tranche.

### Mécanisme interne

```
Tâche N ──┬── Agent A → réponse A ──┐                   ┌── A classe les réponses de B, C ──┐
          ├── Agent B → réponse B ──┼── étiquettes A,B,C ┼── B classe les réponses de A, C ──┼── Dépouillement ── Consensus ? ── oui → réponse gagnante
          └── Agent C → réponse C ──┘                   └── C classe les réponses de A, B ──┘                        │
                                                                                                                   non
                                                                                                                    ↓
                                                                               Round suivant (avec discussion_context)
                                                                                                                    ↓
                                                                                 Rounds épuisés → FallbackStrategy
```

**Enregistrement** : `AddOrkeonConsensus()` (`Orkeon.Infrastructure.DependencyInjection`) lie `ConsensualProcessOptions` à la section `Orkeon:Consensus`, enregistre `IVotingStrategyFactory` → `VotingStrategyFactory` et l'`IVotingStrategy` construite à partir du `ConsensusType` configuré (singletons, `TryAdd`), `IBallotCollector` → `AgentBallotCollector` (scoped), ainsi que `ConsensualProcessStrategy` (scoped). `AddOrkeonInfrastructure()` l'appelle déjà — ne l'appelez vous-même que dans un hôte qui n'utilise pas `AddOrkeonInfrastructure()` ; enregistrer votre propre `IVotingStrategy` ou `IBallotCollector` avant lui remplace celui par défaut.

**Classes clés** : `ConsensualProcessStrategy`, `IBallotCollector` / `AgentBallotCollector`, `BallotRequest`, `BallotCandidate`, `Ballot`, `IVotingStrategy`, `IVotingStrategyFactory` / `VotingStrategyFactory`, `Vote`, `VoteResult`, `VotingOptions`, `ConsensualProcessOptions`, `ConsensusFallback`

### Comment un bulletin est formé

- **Candidats** : seules les exécutions réussies. Une exécution en échec n'est jamais candidate — son agent vote quand même, sur toutes les réponses. Une réponse réussie seule est retenue sans bulletin ; quand toutes les exécutions d'un round ont échoué, la tâche échoue avec l'erreur des agents, sans round de plus.
- **Anonymisés** : les réponses portent les étiquettes `A`, `B`, … dans un ordre mélangé par tâche et par round (déterministe : la même exécution donne les mêmes étiquettes). Le votant ne voit ni le rôle ni l'identifiant des auteurs : il vote pour la réponse, pas pour l'agent.
- **Pas de vote pour soi** : un agent classe les réponses des autres agents, jamais la sienne. La part d'un choix est donc comptée parmi les bulletins qui pouvaient le désigner (`Vote.OwnChoice`) : trois agents qui rendent la même réponse atteignent `Majority`, `SuperMajority` et `Unanimity` au premier round.
- **Qui le remplit** : l'agent votant lui-même, via `IAgentExecutionService` — sa propre configuration LLM, et ses jetons comptés dans la télémétrie de la tâche. On lui demande un objet JSON (format de réponse `json_object`) : `{"ranking": ["B", "A"], "abstain": false, "confidence": 0.8, "justification": "…"}`. Une réponse qui n'est pas cet objet, un classement qui ne nomme aucune étiquette proposée, une exécution de bulletin en échec ou `"abstain": true` est une **abstention** : elle est journalisée et ne fait jamais échouer la tâche.
- **Ce qui est compté** : `Majority`, `SuperMajority`, `Unanimity` et `WeightedConsensus` comptent le premier choix ; `BordaCount` compte le classement entier. La `confidence` du bulletin est lue avec `UseWeightedVotes`.
- **Égalités** : une égalité en tête n'est pas un consensus. Deux agents ne peuvent désigner que l'autre, leur vote est donc toujours à égalité — une crew consensuelle a besoin de trois agents ou plus pour trancher par le vote.
- **Le manager** : le `managerAgent` d'une crew (`.manager(agent)` en `.ork.ts`, `CrewBuilder.WithManager` en C#) ne répond ni ne vote ; il arbitre seulement en `ManagerDecision`. Sous un autre repli — `AcceptBestScore`, le défaut, ou `Fail` — il ne décide rien, et le run le dit par un avertissement à son début qui nomme l'agent et le repli de l'hôte : posez `Orkeon:Consensus:FallbackStrategy: ManagerDecision`, ou retirez `managerAgent:` pour que l'agent réponde et vote. Une crew consensuelle ne lit pas de LLM manager : `WithManagerLlm` (C#) est refusé.

**Replis**, après `MaxVotingRounds` rounds sans consensus :

- `AcceptBestScore` garde la réponse que le dernier dépouillement a placée en tête, **sans rien relancer**. Quand aucun bulletin de ce round n'a désigné de réponse (tous les votants se sont abstenus), la tâche échoue.
- `ManagerDecision` : l'agent manager de la crew classe les réponses du dernier round, anonymisées comme les pairs les ont vues, et son premier choix est retenu ; s'il s'abstient, la tâche échoue. Une crew sans agent manager est **refusée au lancement, avant qu'aucun agent ne s'exécute**, avec un message qui nomme la correction (le repli est un réglage d'hôte : le validateur YAML ne le voit pas).
- `Fail` fait échouer la tâche (« Consensus could not be reached for task … »).

Le résultat retenu est celui de la tâche : s'il est en échec, la crew échoue et les dépendantes de la tâche sont sautées ; les tâches qui n'en dépendent pas s'exécutent quand même.

**Ce que la crew mémorise** : la réponse retenue, une fois, sous l'agent qui l'a écrite — jamais une candidate que le vote a écartée, jamais un bulletin. Les candidates et les bulletins s'exécutent avec `SimpleExecutionContext.StoreResultInMemory` à faux (`AgentBallotCollector` le met à faux pour tout bulletin qu'il fait voter), et la stratégie range la réponse retenue via `IMemoryCoordinator` ; une tâche sans réponse retenue ne range rien. Une candidate rappelle les souvenirs de la crew avant de répondre — elle répond à la tâche — et un bulletin non (`RecallFromMemory` à faux). Si le rangement échoue, la réponse retenue reste le résultat de la tâche et l'échec est un avertissement, comme dans les autres modes. Voir [Système de mémoire](../architecture/memory-system.md#la-mémoire-dune-crew--provider-et-portée).

**Coût** : un round coûte N exécutions plus N bulletins — un appel LLM court par votant, dont le prompt contient les réponses soumises au vote : son entrée croît avec N × la longueur des réponses (une réponse longue est tronquée pour tenir dans une description de tâche). Trois agents d'accord coûtent 3 exécutions + 3 bulletins par tâche. Une tâche qui n'atteint jamais le consensus coûte `MaxVotingRounds` × (N + N) appels, plus un bulletin du manager en `ManagerDecision` ; `AcceptBestScore` ne relance rien. Les variables d'entrée de la crew atteignent chaque exécution et chaque bulletin ; les sorties gagnantes des tâches précédentes sont transmises en contexte.

```yaml
name: review-board
process: consensual
managerAgent: chair             # l'arbitre sous FallbackStrategy: ManagerDecision (inactif, avec un avertissement, sous un autre repli)
```

### Types de consensus disponibles

Le mécanisme de vote est **sélectionnable par configuration** : `Orkeon:Consensus:VotingOptions:ConsensusType` (`appsettings.json` .NET) choisit la stratégie via `IVotingStrategyFactory`. Défaut : `Majority`. Chaque type exige aussi le quorum (`QuorumPercent`) et une seule réponse en tête ; une part est comptée parmi les bulletins qui pouvaient désigner la réponse.

| Type | Stratégie résolue | Règle de consensus | Seuil par défaut |
|------|-------------------|--------------------|------------------|
| `Majority` | `MajorityVotingStrategy` | Part en tête strictement supérieure à 50 % | 50 % |
| `SuperMajority` | `SuperMajorityVotingStrategy` | Part en tête ≥ `ConsensusThreshold` | 66,7 % |
| `Unanimity` | `UnanimityVotingStrategy` | Chaque bulletin qui pouvait désigner le gagnant l'a désigné | 100 % |
| `WeightedConsensus` | `WeightedConsensusStrategy` | Poids du rôle (`RoleWeights`) × confiance, part au-dessus de `ConsensusThreshold` | 66,7 % |
| `BordaCount` | `BordaCountStrategy` | Meilleur score de Borda sur les classements complets ; pas de consensus en cas d'égalité en tête | N/A |

### Configuration (.NET, section `Orkeon:Consensus`)

```json
{
  "Orkeon": {
    "Consensus": {
      "MaxVotingRounds": 3,
      "VotingOptions": {
        "ConsensusType": "SuperMajority",
        "ConsensusThreshold": 75,
        "UseWeightedVotes": true,
        "QuorumPercent": 50,
        "AllowAbstention": true
      },
      "EnableDiscussion": true,
      "FallbackStrategy": "AcceptBestScore",
      "RoleWeights": {
        "Senior Analyst": 2.0,
        "Junior Analyst": 1.0
      }
    }
  }
}
```

| Clé | Défaut | Effet |
|-----|--------|-------|
| `MaxVotingRounds` | 3 | Rounds avant le repli ; un round = N exécutions + N bulletins |
| `VotingOptions:ConsensusType` | `Majority` | Stratégie de vote |
| `VotingOptions:ConsensusThreshold` | 66,7 | Seuil de `SuperMajority` / `WeightedConsensus`, en **pourcentage (0–100)** — 75 % s'écrit `75` |
| `VotingOptions:UseWeightedVotes` | `false` | Poids × confiance du bulletin au lieu d'une voix par bulletin |
| `VotingOptions:QuorumPercent` | 50 | Part minimale, en pourcentage, de bulletins exprimés (hors abstentions) parmi les votants ; en dessous, pas de consensus |
| `VotingOptions:AllowAbstention` | `true` | `false` : une abstention compte comme un vote contre toutes les réponses — elle reste au dénominateur de chaque part et rompt l'unanimité ; avec `BordaCount`, toute abstention empêche le consensus |
| `EnableDiscussion` | `true` | Les rounds après le premier voient les réponses des autres agents |
| `FallbackStrategy` | `AcceptBestScore` | `AcceptBestScore` (la tête du dernier dépouillement, rien n'est relancé), `Fail`, `ManagerDecision` (l'agent manager de la crew choisit ; obligatoire) |
| `RoleWeights` | vide | Rôle → poids, lu par `WeightedConsensus` |

### Avantages

- **Plusieurs tentatives indépendantes** par tâche, exécutées en concurrence
- **Un vote sur les réponses** : chaque agent classe les réponses anonymisées des autres ; une tentative en échec n'est jamais retenue
- **Vote configurable** : 5 stratégies, quorum, abstention, pondération par rôle
- **Discussion** : les agents voient les réponses des autres entre les rounds

### Inconvénients

- **Coût LLM élevé** : N exécutions + N bulletins par tâche et par round (+ un bulletin du manager en `ManagerDecision`)
- **Les juges sont des LLM** : un bulletin est l'avis d'un modèle ; un angle mort partagé par les modèles de tous les agents n'est pas corrigé par le vote
- **Deux agents sont toujours à égalité** : le vote tranche à partir de trois agents
- **Complexité de configuration** : beaucoup de paramètres

### Quand l'utiliser

- Tâches où des tentatives indépendantes de plusieurs agents valent leur coût
- Crews où l'échec d'un agent doit être absorbé par les autres (une réponse en échec n'est jamais candidate)

### Quand ne pas l'utiliser

- Tâches factuelles à réponse unique
- Contraintes de budget LLM (multiplié par 2 × N agents × R rounds : réponses et bulletins)
- Workflows à haute fréquence (trop lent)

---

## 5. Graph — Graphe d'états avec retry contrôlé

### Principe

La crew s'exécute à travers un **graphe d'états typé** (`StateGraph<CrewGraphState>`) à topologie fixe : `execute_task` exécute la prochaine tâche en attente, `route` remet une tâche échouée qui a encore des retries en tête de file — elle est relancée avant la tâche suivante — et reboucle tant qu'il reste du travail. Un **circuit breaker à 3 mécanismes** borne la boucle. Les arêtes conditionnelles et les topologies arbitraires sont disponibles via l'API Domain `StateGraph<TState>` en C# ; le mode YAML ne déclare pas son propre graphe.

### Mécanisme interne

```
START ──→ execute_task ──→ route ──┬── tâches en attente (ou échouées avec retries restants) ──→ execute_task
                                   │
                                   └── plus rien ──→ END
```

**Classes clés** :
- `StateGraph<TState>` — Définition du graphe (nœuds + arêtes fixes et conditionnelles)
- `GraphRunner<TState>` — Moteur d'exécution avec circuit breaker
- `GraphProcessStrategy` — Implémentation de `IProcessStrategy`
- `CircuitBreakerPolicy` — Limites (le record partagé avec la FSM)
- `CrewGraphState` — État typé qui traverse le graphe

### `CrewGraphState` — Propriétés de l'état

| Propriété | Type | Description |
|-----------|------|-------------|
| `PendingTaskIds` | `Queue<TaskId>` | Tâches restant à exécuter |
| `FailedTaskIds` | `Queue<TaskId>` | Tâches échouées que le nœud `route` remet en tête de file |
| `RetryCounts` | `Dictionary<string, int>` | Compteur de retries par tâche |
| `MaxRetryCycles` | `int` | Retries par tâche échouée (défaut : 2) |
| `ApplicationOutputs` / `DomainResults` | `IReadOnlyList<…>` | Sorties accumulées, une par tentative |
| `TotalTokensUsed` | `int` | Tokens consommés (propagés dans les métadonnées de `CrewOutput` sous `totalTokens`) |
| `PromptTokensUsed` / `CompletionTokensUsed` | `int` | Ventilation prompt/complétion (0 quand le fournisseur ne la rapporte pas) |
| `CacheHitTokensUsed` / `CacheMissTokensUsed` | `long` | Ventilation du cache de prompt (0 si non rapportée) |
| `AgentIndex` | `int` | Curseur round-robin des tâches sans agent |

### Circuit breaker — 3 mécanismes de protection

| Mécanisme | Calculé (défaut) | Preset Strict | Preset Default | Preset Permissive |
|-----------|--------|--------|---------|------------|
| `MaxTransitions` (exécutions de nœuds) | 2 × visites + 1 | 50 | 100 | 1000 |
| `MaxStateVisits` (visites d'un nœud) | tâches × (1 + `maxRetryCycles`) | 5 | 10 | 50 |
| `MaxTotalDuration` | celle du preset (10 min en Strict) | 10 min | 30 min | 2 h |

Chaque tentative de tâche est une visite de `execute_task` (et une de `route`). Sans `maxStateVisits` / `maxTransitions` explicites, les deux sont **calculés depuis la crew** : `tâches × (1 + maxRetryCycles)` visites — le maximum qu'une crew peut faire, chaque tâche épuisant ses retries — et deux fois plus une transitions ; une crew saine n'est donc jamais coupée, quelle que soit sa taille, et une vraie boucle déclenche encore le disjoncteur. Une valeur explicite l'emporte ; le preset (`circuitBreakerPreset`, Strict par défaut) ne fournit plus que ce qui n'est pas calculé, à commencer par la durée totale. Le quatrième mécanisme de la FSM, le timeout par état, n'est pas vérifié par le runner du graphe. Un disjoncteur déclenché renvoie un `CrewOutput` en échec (« Graph execution stopped by circuit breaker: … ») avec les sorties produites jusque-là.

### Configuration YAML

```yaml
name: "review-loop"
goal: "Démo graphe"
process: graph
graphConfig:
  circuitBreakerPreset: "strict"   # ou "default", "permissive" — fournit la durée
  maxRetryCycles: 3
  # Surcharges individuelles possibles (visites et transitions sont calculées sinon) :
  maxTransitions: 75
  maxStateVisits: 20
  maxTotalDurationSeconds: 1800
```

### Avantages

- **Retry intégré** : une tâche échouée est relancée avant la suivante, jusqu'à `maxRetryCycles`
- **Sûreté** : un circuit breaker borne les exécutions, les visites et la durée
- **Observabilité** : événements `OnNodeCompleted` / `OnCircuitBroken`, journalisés par la stratégie
- **Bornes à la taille de la crew** : visites et transitions calculées depuis le nombre de tâches et les retries

### Inconvénients

- **Durée par défaut de 10 minutes** : la `MaxTotalDuration` du preset Strict borne encore le run — relevez `maxTotalDurationSeconds` pour une crew longue
- **Topologie fixe en YAML** : le routage conditionnel exige l'API C# `StateGraph<TState>`

### Quand l'utiliser

- Pipelines aux étapes instables qui méritent d'être relancées (web scraping, API instables)
- Exécutions qui doivent être bornées en nombre d'exécutions et en durée

### Quand ne pas l'utiliser

- Pipelines linéaires simples sans étape qui mérite d'être relancée (Sequential)
- Tâches indépendantes (Parallel)

> **Voir aussi** : [Orchestration Graph](./graph.md) pour tous les détails.

---

## 6. Autonomous — Auto-organisation avec budget

### Principe

Pour chaque tâche, le LLM manager (`LlmBasedManager`, comme en Hierarchical — sur le fournisseur que pose `CrewBuilder.WithManagerLlm` en C#, sinon sur le profil par défaut de l'hôte : une crew autonome n'a pas d'agent manager, et `managerAgent:` est refusé au chargement) choisit l'agent qui la **réclame**. Quand l'exécution de cet agent échoue et que l'agent autorise la délégation, la tâche est **déléguée à un pair** via le canal A2A (`IAgentChannel`), sous un budget enfant dérivé. Sans pair dans la crew, l'échec est maintenu. Un **budget multi-dimensionnel** (5 axes) borne le run. L'API est expérimentale (`ORKEXP002`, voir les [API expérimentales](../reference/experimental-apis.md)).

### Mécanisme interne

```
                    ┌─── AgentExecutionBudget (5 dimensions) ───┐
                    │  appels d'outils · profondeur · durée      │
                    │  tokens · agents créés                     │
                    └──────────────────┬─────────────────────────┘
                                       │
pour chaque tâche : Manager (LLM) ── assigne ──→ Agent A ── exécute
                                       │
                          échec + allowDelegation ?
                                       │ oui
                                       ▼
                  channel.RequestAsync("delegate") ──→ premier autre agent
                                                        (budget enfant)
```

**Classes clés** :
- `AutonomousProcessStrategy` — Stratégie d'orchestration
- `AgentExecutionBudget` — Budget multi-dimensionnel (Domain)
- `IAgentChannel` / `InMemoryAgentChannel` — Canal A2A requête/réponse
- `SpawnAgentTool` — Création dynamique d'agents (construit par l'hôte, pas injecté par la stratégie)
- `BudgetExhaustedException` — Levée quand un axe est épuisé

### Budget multi-dimensionnel — 5 axes

| Dimension | Strict | Default | Permissive | Description |
|-----------|--------|---------|------------|-------------|
| `MaxToolCalls` | 8 | 15 | 50 | Appels d'outils ; la stratégie en compte un par tâche distribuée |
| `MaxDelegationDepth` | 1 | 2 | 4 | Sauts de délégation ; le compteur, commun à la crew, n'est jamais décrémenté |
| `MaxTokensConsumed` | 8 000 | 16 000 | 64 000 | Tokens imputés au budget (exécutions déléguées) |
| `MaxSpawnedAgents` | 1 | 3 | 10 | Agents créés via `SpawnAgentTool` |
| `MaxWallTime` | 2 min | 5 min | 15 min | Durée d'exécution maximale |

`ICrewOrchestrationService` exécute toujours ce mode avec **`AgentExecutionBudget.Permissive`** ; un autre budget exige d'appeler directement `AutonomousProcessStrategy.ExecuteAutonomousAsync(crew, budget, …)`.

**Budgets enfants** : `CreateChildBudget()` donne au délégué l'allocation **restante** du parent sur chaque axe (un niveau de délégation en moins, au moins 1 appel d'outil et 100 tokens). Chaque budget enfant est une copie indépendante : deux délégués reçoivent chacun la totalité du reste.

### Communication A2A

```csharp
// Requête/Réponse (timeout par défaut : 30 s)
var request = AgentChannelRequest.Create(
    from: analyst.Id,
    to: researcher.Id,
    intent: "find_data",
    payload: "Statistiques du marché 2025");

var response = await channel.RequestAsync(request, timeout: TimeSpan.FromSeconds(30));

// Broadcast (aucune réponse attendue) vers les membres enregistrés pour cette crew
await channel.BroadcastAsync(analyst.Id, crew.Id, "Résultats disponibles", ct);
```

### Configuration YAML

```yaml
name: "research-team"
goal: "Démo autonome"
process: autonomous
# Il n'existe PAS de clé de budget autonome en YAML : une crew autonome YAML
# tourne toujours sous AgentExecutionBudget.Permissive (50 appels d'outils,
# profondeur 4, 15 min, 64 000 tokens, 10 spawns) — voir le guide Autonomous
# et yaml-schema.md.
```

### Avantages

- **Assignation pilotée par LLM** : le manager associe chaque tâche à un agent
- **Deuxième chance** : une tâche échouée est confiée à un pair
- **Terminaison garantie** : le budget et sa durée bornent le run
- **Télémétrie du budget** : les métadonnées de la crew portent la consommation de chaque axe

### Inconvénients

- **Expérimental** : `ORKEXP002`, la sémantique peut encore bouger
- **Non déterministe** : les choix du manager varient d'une exécution à l'autre
- **Budget épuisé = run écourté** : les tâches restantes ne sont pas exécutées, et la crew échoue en nommant la dimension épuisée et chaque tâche qu'elle n'a pas atteinte
- **Budget fixe** via l'orchestrateur (Permissive)

### Quand l'utiliser

- Exploration (recherche, R&D, analyse exploratoire)
- Équipes hétérogènes où l'association tâche-agent n'est pas connue à l'avance

### Quand ne pas l'utiliser

- Workflows déterministes et bien définis (Sequential ou Graph)
- Scénarios réglementaires exigeant une traçabilité complète du chemin d'exécution

> **Voir aussi** : [Orchestration Autonomous](./autonomous.md) pour tous les détails.

---

## Arbre de décision

```
Vos tâches ont-elles des dépendances entre elles ?
│
├── NON
│   └── Voulez-vous que plusieurs agents tentent chaque tâche ?
│       ├── OUI → Consensual
│       └── NON → Parallel
│
└── OUI
    └── Une tâche instable doit-elle être relancée automatiquement ?
        │
        ├── NON
        │   └── Avez-vous besoin d'un manager qui assigne et revoit ?
        │       ├── OUI → Hierarchical
        │       └── NON → Sequential
        │
        └── OUI
            └── Relancer la même tâche, ou la confier à un pair ?
                ├── Même tâche, bornée → Graph
                └── Un pair, assigné par LLM → Autonomous
```

---

## Combinaisons et complémentarité

Les ProcessTypes sont exclusifs à l'échelle d'une crew, mais se combinent avec les autres briques :

**Sequential/Graph + outils de délégation** : dans ces deux modes, un agent avec `allowDelegation: true` reçoit `delegate_work_to_coworker` et `ask_question_to_coworker`, ce qui donne un comportement hiérarchique local sans manager.

**Graph + FSM** : `GraphProcessStrategy` et le moteur [FSM](./fsm.md) partagent `CircuitBreakerPolicy` et ses presets. La FSM est un moteur Domain générique (`orkeon forge` tourne dessus) ; aucune stratégie ne fait passer une tâche par une machine à états — la boucle d'agent borne chaque tâche.

**DSL de scripting** : une crew `.ork.ts` déclare son mode avec `crewBuilder().process("…")` (les mêmes six valeurs). La forme déclarative (`globalThis.crew = crew`) passe par les stratégies ci-dessus ; la forme procédurale (`await crew.run()`) exécute les corps des agents dans l'ordre de déclaration et se contente d'étiqueter le mode — voir [Scripting](../architecture/scripting.md).

---

## Synthèse coûts et performances

| ProcessType | Exécutions (N tâches, M agents) | Appels LLM supplémentaires | Latence | Prévisibilité |
|-------------|------------------|-----------------|---------|---------------|
| Sequential | N | — | Σ(durées) | ⭐⭐⭐⭐⭐ |
| Hierarchical | N × (1 à 3) | N assignations + N × (1 à 3) revues | Σ(durées) × 1,5–3 | ⭐⭐⭐⭐ |
| Parallel | N | — | Σ(tâche la plus longue de chaque vague) | ⭐⭐⭐⭐ |
| Consensual | N × M × rounds | N × M × rounds bulletins (+ N bulletins du manager en `ManagerDecision`) | Σ((max(durées) + max(durées des bulletins)) × rounds) | ⭐⭐⭐ |
| Graph | N × (1 + retries) | — | Σ(durées des tentatives) | ⭐⭐⭐⭐ |
| Autonomous | N (+ délégations) | N assignations | Bornée par la durée maximale | ⭐⭐ |

---

## Références croisées

| Sujet | Document |
|-------|----------|
| Architecture et concepts fondamentaux | [Vue d'ensemble](../getting-started/overview.md) |
| Fonctionnalités détaillées | [YAML et Builders](../getting-started/yaml-and-builders.md) |
| Schéma YAML (`process`, `graphConfig`) | [Schéma YAML](../architecture/yaml-schema.md) |
| FSM (moteur Domain générique) | [./fsm.md](./fsm.md) |
| Orchestration Graph | [./graph.md](./graph.md) |
| Orchestration Autonomous | [./autonomous.md](./autonomous.md) |
| Blueprint d'un nouveau ProcessType | [../guides/blueprint.md](../guides/blueprint.md) |
