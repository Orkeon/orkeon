> 🇬🇧 [English version](../../reference/agent-framework-interop.md)

# Interop Microsoft Agent Framework

`Orkeon.Interop.AgentFramework` relie Orkeon et **Microsoft Agent Framework** (MAF,
`Microsoft.Agents.AI`) dans les deux sens : une crew Orkeon s'exécute comme un `AIAgent` MAF, et un
`AIAgent` MAF travaille à l'intérieur d'une crew Orkeon — comme modèle d'un agent ou comme l'un de
ses outils. La décision et ses limites sont consignées dans
[l'ADR-010](../adr/ADR-010-agent-framework-interop.md).

| | |
|---|---|
| Paquet | `Orkeon.Interop.AgentFramework` — dépend du parapluie `Orkeon` et de `Microsoft.Agents.AI.Abstractions` (voir la [matrice de publication](publication-matrix.md)) |
| Espaces de noms | `Orkeon.Interop.AgentFramework`, `Orkeon.Interop.AgentFramework.DependencyInjection` |
| Exemple | [`examples/interop/agent-framework`](https://github.com/orkeon/orkeon/blob/main/examples/interop/agent-framework/Program.cs) — les deux sens sur un même hôte et un même modèle |

## Installer et enregistrer

```bash
dotnet add package Orkeon.Interop.AgentFramework --prerelease
dotnet add package Microsoft.Agents.AI --prerelease   # seulement pour construire vous-même des agents MAF (ChatClientAgent, AsAIAgent)
```

Le paquet apporte `Orkeon` et `Microsoft.Agents.AI.Abstractions` avec lui. Le pont a besoin d'un hôte
Orkeon — la crew s'exécute par son `ICrewOrchestrationService` — il se pose donc par-dessus les
enregistrements habituels :

```csharp
services.AddOrkeonApplication();
services.AddOrkeonInfrastructure(configuration);
// … les suites d'outils qu'utilisent vos crews …
services.AddOrkeonAgentFramework();   // Orkeon -> MAF seulement ; MAF -> Orkeon ne demande aucun enregistrement
```

## Ce qui traverse le pont

| Sens | Entre | Sort | Reste de son côté |
|---|---|---|---|
| Crew Orkeon → MAF (`CrewAgent`) | la conversation MAF, comme contexte initial de la crew | la sortie finale de la crew en un message assistant, son usage de jetons | les outils, montages, budget, mémoire et orchestration de la crew — MAF voit un agent qui répond |
| Agent MAF → modèle d'un agent Orkeon (`WithAgentFrameworkAgent`) | les prompts de l'agent Orkeon, rôles projetés | le texte de la réponse MAF et son usage | les outils et le `LlmConfig` de l'agent Orkeon ; les outils propres de MAF restent utilisables de son côté |
| Agent MAF → outil Orkeon (`WithAgentFrameworkTool`) | la chaîne `request` | `answer` et `agent` | les outils, la session et la mémoire de l'agent MAF |

## Une crew Orkeon comme agent MAF — `CrewAgent`

`CrewAgent : AIAgent` enveloppe une crew Orkeon. Chaque `RunAsync` est **un kickoff de crew**
(`ICrewOrchestrationService.KickoffAsync`) :

- **Entrée** — la conversation devient le contexte initial de la crew : un message seul tel quel ;
  plusieurs messages sous la forme `Conversation so far:` (les tours précédents, une ligne
  `rôle: texte` chacun) suivie de `Request:` et du dernier message.
- **Sortie** — un message assistant portant la sortie finale de la crew, signé du nom de l'agent.
  `FinishReason` vaut `Stop`, ou `error` quand la crew n'a pas réussi. `Usage` porte les jetons de
  prompt, de complétion et le total quand le run les a mesurés, et il est absent sinon.
- **Streaming** — `RunStreamingAsync` fonctionne, mais une crew répond quand elle a fini : le flux
  est la réponse finale, pas un flux de jetons.
- **Session** — ne contient rien. Une crew ne garde aucun état de conversation entre deux kickoffs
  (la mémoire, quand elle est activée, est l'affaire de la crew) ; la session se sérialise en objet
  vide et se désérialise depuis n'importe quoi, si bien que les appelants qui persistent leurs
  sessions continuent de fonctionner.
- **Identité** — l'identifiant de l'agent est `orkeon-crew-<name>` pour un agent construit par la
  fabrique, et `orkeon-crew-<crewId>` (aussi son nom par défaut) pour un agent construit sur un
  orchestrateur. Les `AgentRunOptions` ne sont pas lues.
- **Scope** — un agent construit par la fabrique exécute **chaque tour dans un scope à lui** :
  l'orchestrateur et les dépôts de crews, d'agents et de tâches sont des services scopés, si bien
  que deux tours, ou deux agents, ne les partagent jamais, et la fabrique se résout sur un hôte qui
  valide les scopes.

```csharp
services.AddOrkeonAgentFramework();   // enregistre ICrewAgentFactory

var factory = host.Services.GetRequiredService<ICrewAgentFactory>();
AIAgent crewAgent = factory.Create(
    async (services, ct) =>           // le scope du tour : y enregistrer la crew, rendre son id
    {
        await services.GetRequiredService<IAgentRepository>().AddAsync(summariser, ct);
        await services.GetRequiredService<ITaskRepository>().AddAsync(summarise, ct);
        await services.GetRequiredService<ICrewRepository>().AddAsync(crew, ct);
        return crew.Id;
    },
    "orkeon-summariser",
    "Summarises text in two sentences");

var answer = await crewAgent.RunAsync("Summarise last week's incidents");
```

Le chargeur peut aussi bien charger un fichier de crew par l'`ICrewFactory` du scope
(`CreateFromFileAsync`) et rendre l'identifiant de ce qu'il a chargé. `AddOrkeonAgentFramework()`
n'enregistre que le singleton `ICrewAgentFactory` (`TryAdd`), sur l'`IServiceScopeFactory` de
l'hôte ; `new CrewAgent(scopeFactory, loadCrew, name, description?)` fait de même sans la fabrique,
et `new CrewAgent(orchestrator, crewId, name?, description?)` / `new CrewAgent(orchestrator, crew)`
exécutent une crew enregistrée par un orchestrateur **que l'appelant possède**, avec le scope où il
vit. Le résultat s'insère dans n'importe quel workflow, orchestration ou chaîne `AsAIFunction()` de
MAF.

## Un agent MAF à l'intérieur d'une crew Orkeon

### Comme modèle d'un agent — `WithAgentFrameworkAgent`

```csharp
var agent = new AgentBuilder()
    .Role("Reviewer").Goal("Review the change")
    .WithAgentFrameworkAgent(mafAgent)
    .Build();
```

`WithAgentFrameworkAgent(agent)` équivaut à `WithLlm(new AIAgentLlmProvider(agent))` : l'agent
Orkeon garde son rôle, son objectif et ses tâches, et chaque prompt qu'il envoie est une exécution
de l'agent MAF. `AIAgentLlmProvider : ILlmProvider` :

- garde **une seule session MAF pour toute sa durée de vie**, créée au premier usage, si bien qu'un
  agent MAF doté de mémoire ou de fournisseurs de contexte voit une conversation continue d'une
  itération de l'agent Orkeon à l'autre ;
- projette les rôles `system`, `assistant` et `tool`, et tout autre rôle sur `user` ;
- ignore le `LlmConfig` qu'on lui passe (température, jetons max, format de réponse) — l'agent MAF
  se configure de son côté ;
- déclare `Name` = `agent-framework:<nom ou id>` et `LlmProviderCapabilities.Unknown`, et relaie
  l'usage MAF comme décompte de jetons de la réponse ;
- laisse **l'appel d'outils côté MAF** : l'agent MAF utilise les outils qu'il porte, et les outils
  propres de l'agent Orkeon ne lui sont pas proposés. Un agent qui a besoin des deux enveloppe
  plutôt l'agent MAF en outil.

### Comme outil — `WithAgentFrameworkTool`

```csharp
var planner = new AgentBuilder()
    .Role("Planner").Goal("Write a plan and have it reviewed")
    .WithAgentFrameworkTool(reviewer, "ask_reviewer", "Ask the reviewer for the biggest risk of a plan")
    .Build();
```

`WithAgentFrameworkTool(agent, toolName?, description?)` ajoute un `AIAgentTool` à côté des autres
outils de l'agent — le miroir de l'`AsAIFunction()` de MAF :

| | |
|---|---|
| Nom | `toolName`, ou `agent_<slug>` du nom de l'agent MAF (en minuscules, tout autre caractère devient `_`) |
| Description | `description`, ou celle de l'agent MAF, ou une phrase générée |
| Catégorie | `Delegation` |
| Entrée | `request` (requis, refusé s'il est vide) — envoyé comme un message utilisateur |
| Sortie | `answer` (le texte de l'agent MAF), `agent` (son nom) |
| Session | une par instance d'outil : les appels successifs poursuivent la même conversation MAF |

## Ce que le pont ne fait pas

- Il ne traduit pas les outils d'un côté à l'autre : les outils d'un agent Orkeon restent ceux
  d'Orkeon, ceux d'un agent MAF restent ceux de MAF.
- Il ne diffuse pas de jetons hors d'une crew (`CrewAgent` renvoie la réponse finale).
- Il n'enregistre rien dans les runners livrés : `orkeon`, `orkeon-host` et le REPL ne référencent
  pas le paquet. Un hôte qui embarque l'ajoute — l'exemple le fait par le hook `configureServices`
  de `RunnerHost.Build` ([hébergement](hosting.md)).

---

> **Voir aussi** : [ADR-010](../adr/ADR-010-agent-framework-interop.md) ·
> [Matrice de publication](publication-matrix.md) · [Hébergement](hosting.md) ·
> [Retour à l'index](../INDEX.md)
