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
| Agent MAF → modèle d'un agent Orkeon (`WithAgentFrameworkAgent`) | les prompts de l'agent Orkeon, rôles projetés, chaque message une fois sur la session MAF | le texte de la réponse MAF et son usage, compté une fois | le `LlmConfig` de l'agent Orkeon (chaque option déclarée produit un avertissement) ; aucun outil Orkeon — refusés ; les outils propres de MAF restent utilisables de son côté |
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
var reviewer = new AgentBuilder()
    .Role("Reviewer").Goal("Find the biggest risk of a change")
    .WithAgentFrameworkAgent(mafAgent, logger)   // logger facultatif : il entend les options non transmises
    .Build();
```

`WithAgentFrameworkAgent(agent, logger?)` équivaut à `WithLlm(new AIAgentLlmProvider(agent, logger))` :
l'agent MAF devient le fournisseur propre de l'agent Orkeon (`Agent.Llm`). L'agent Orkeon garde son rôle,
son objectif et ses tâches ; ce qui répond, c'est l'agent MAF.

- **Ce qui tourne sur lui** — les tours des tâches de l'agent et leur correction de sortie, son rôle de
  manager hiérarchique (il assigne et relit), son bulletin dans un vote consensuel. Un autre agent peut
  toujours lui déléguer du travail (`delegate_work_to_coworker`). L'agent MAF reçoit le prompt qu'Orkeon a composé —
  le message système (rôle, objectif, backstory, garde-fous, gabarit de réponse) et le message
  utilisateur (la tâche, son résultat attendu, son plan, les variables, les sorties précédentes, les
  souvenirs rappelés, la connaissance), examiné d'abord par le Guardian — et y ajoute ses propres
  instructions et fournisseurs de contexte.
- **L'ordre** — le profil que nomme le `llm_override` d'une tâche l'emporte pour cette tâche, `default`
  compris ; sinon l'agent tourne sur son propre fournisseur. Un agent tourne sur son propre fournisseur
  ou sur un profil de l'hôte, jamais les deux : `Build()` et `Agent.Create` refusent le fournisseur
  accompagné de `WithLlmConfig(LlmConfig.OnProfile(nom))`. Le `WithManagerLlm` d'une crew l'emporte toujours sur le
  fournisseur propre de son agent manager.
- **Aucun outil Orkeon** — un agent MAF appelle les outils qu'il porte, jamais ceux d'Orkeon :
  `AIAgentLlmProvider` déclare `LlmProviderCapabilities.RunsOwnTools`. Un tel agent se voit refuser
  `WithTool`, `WithTools` et `AllowDelegation` à sa construction (`BuilderValidationException`), et
  `AddTool` ensuite ; une tâche qui tombe sur un fournisseur qui fait ses propres outils — celui de
  l'agent, celui d'un profil ou le défaut de l'hôte — avec des outils à tenir (ses propres `tools:`,
  `human_input`, les `delegate_work_to_coworker` et `ask_question_to_coworker` d'un agent qui permet la
  délégation, comme le fait un agent YAML qui n'écrit pas `allowDelegation: false`) échoue avant tout appel. Le message nomme les
  outils et les deux remèdes : donner l'outil à l'agent MAF, ou donner l'agent MAF à un agent Orkeon comme
  outil (`WithAgentFrameworkTool`, ci-dessous) — et, pour les outils de délégation, couper la délégation.
  Un même agent Orkeon ne peut pas tenir le même agent MAF des deux façons.
- **Une session, chaque message une fois, un appel à la fois** — le fournisseur garde une seule session
  MAF pour toute sa durée de vie, créée au premier usage, si bien qu'un agent MAF doté de mémoire ou de
  fournisseurs de contexte voit une conversation continue. La boucle d'agent renvoie toute la
  conversation à chaque tour ; un appel qui prolonge la conversation que tient la session — les messages
  de l'appel précédent, puis la réponse que le fournisseur y a faite — n'envoie à la session que la
  suite, et tout autre appel — une nouvelle tâche — envoie tous ses messages, la session gardant la tâche
  précédente. Les appels passent un à la fois : les tâches d'un même agent MAF passent l'une après
  l'autre, même dans une vague parallèle ou à côté d'une tâche `asyncExecution`. La session grandit à
  chaque tâche, et la sortie d'une tâche précédente atteint le modèle deux fois — par la session et par
  les sorties précédentes du prompt ; un `ChatReducer` côté MAF la borne.
- **Compté une fois** — le run construit un client sur le fournisseur, une fois par instance, et le
  compte comme le travail de l'agent (`operation: agent`, `manager` pour les appels d'un manager). Un
  agent MAF bâti sur le modèle compté d'Orkeon est compté une fois, par le compteur de ce modèle, au nom
  du vrai fournisseur et du vrai modèle : le compteur le plus proche du modèle compte. Un agent MAF sur un
  client qu'Orkeon ne compte pas est compté sous `agent-framework:<nom>`, avec l'usage que porte sa
  réponse (estimé quand elle n'en porte pas).
- **Streaming** — un tour diffusé (`--stream`, `KickoffStreamingAsync`) reçoit la réponse MAF en un seul
  fragment, compté une fois.
- **Options** — le pont n'envoie à l'agent MAF que des messages : les options du `LlmConfig` d'un appel
  (modèle, température, jetons max, top-p, format de réponse, réflexion, grammaire…) ne l'atteignent
  jamais. Chacune qu'un appel déclare — une température ou un top-p quelle que soit sa valeur
  (GAP-36) — produit un avertissement structuré — `Option '<nom>' was declared
  but agent-framework:<nom> does not support it — it was not sent` (événement 110, celui des
  fournisseurs HTTP) —, une fois par run d'une crew et par option, par le logger passé à
  `WithAgentFrameworkAgent`, sinon par l'`ILoggerFactory` qu'expose l'agent MAF (`GetService`), sinon
  nulle part. Une sortie structurée attendue d'un agent MAF n'a pour garde que la validation de sortie et
  sa correction.
- Il projette les rôles `system`, `assistant` et `tool`, et tout autre rôle sur `user`, déclare
  `Name` = `agent-framework:<nom ou id>`, et relaie l'usage MAF comme décompte de jetons de la réponse.

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
  d'Orkeon — refusés sur un agent auquel répond un agent MAF —, ceux d'un agent MAF restent ceux de
  MAF.
- Il ne diffuse pas de jetons : `CrewAgent` renvoie la réponse finale d'une crew, et un agent auquel
  répond un agent MAF reçoit cette réponse en un seul fragment.
- Il n'envoie aucune option à un agent MAF : chacune déclarée produit un avertissement, jamais un
  abandon silencieux.
- Il n'enregistre rien dans les runners livrés : `orkeon`, `orkeon-host` et le REPL ne référencent
  pas le paquet. Un hôte qui embarque l'ajoute — l'exemple le fait par le hook `configureServices`
  de `RunnerHost.Build` ([hébergement](hosting.md)).

---

> **Voir aussi** : [ADR-010](../adr/ADR-010-agent-framework-interop.md) ·
> [Matrice de publication](publication-matrix.md) · [Hébergement](hosting.md) ·
> [Retour à l'index](../INDEX.md)
