> 🇬🇧 [English version](../../adr/ADR-010-agent-framework-interop.md)

> **Voir aussi** : [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md) · [ADR-005](./ADR-005-famille-tools-heterogene.md) · [Retour à l'index](../INDEX.md)

# ADR-010 — L'interopérabilité avec Microsoft Agent Framework est un paquet séparé, dans les deux sens

**Statut** : Accepté · **Date** : 2026-09-11
· **Périmètre** : `src/interop/Orkeon.Interop.AgentFramework`, `src/packaging/Orkeon.Interop.AgentFramework`, la gamme NuGet

## Contexte

Microsoft Agent Framework (MAF, `Microsoft.Agents.AI`) est GA depuis avril 2026 et ses
workflows YAML déclaratifs depuis juillet. Son abstraction `AIAgent` est ce qu'une part
croissante des développeurs .NET ont déjà dans leurs projets. La question n'était pas de
rivaliser avec lui sur la promesse par laquelle le README ouvrait — « décrivez vos agents en
YAML » — mais de savoir comment un développeur qui a du code MAF peut essayer Orkeon **sans
rien abandonner**, et comment un crew Orkeon peut être un agent de plus dans un workflow MAF.

Orkeon portait déjà deux adaptateurs `IChatClient` (`LlmProviderToChatClientAdapter`,
`ChatClientToLlmProviderAdapter`) : la couche modèle était pontée. La couche agent ne l'était
pas : rien ne transformait un crew en `AIAgent`, rien ne laissait un `AIAgent` répondre pour
un agent Orkeon ni être appelé par lui.

## Décision

1. **Un paquet, `Orkeon.Interop.AgentFramework`, dépendant de `Orkeon` et de
   `Microsoft.Agents.AI.Abstractions` seulement.** Il n'est *pas* fondu dans l'ombrelle
   `Orkeon` : la dépendance MAF est un choix du consommateur, et un consommateur qui n'utilise
   jamais MAF ne doit pas la restaurer. Le paquet suit le pattern des wrappers PUB-25
   (`src/packaging/`) et rejoint la gamme NuGet.org comme huitième identifiant.
2. **Les deux sens, trois types, aucune nouvelle abstraction côté Orkeon.**
   - `CrewAgent : AIAgent` — un crew Orkeon comme agent MAF. `RunAsync` est un kickoff : la
     conversation devient le contexte initial du crew, la sortie finale le message assistant,
     la télémétrie de tokens l'`Usage`. Les sessions ne contiennent rien (un crew n'a pas
     d'état de conversation propre entre deux kickoffs) et se sérialisent en un sac vide.
   - `AIAgentLlmProvider : ILlmProvider` — un agent MAF comme *modèle* d'un agent Orkeon
     (`AgentBuilder.WithAgentFrameworkAgent`). L'agent Orkeon garde rôle, objectif et tâches ;
     l'agent MAF répond, sur une seule session MAF pendant la vie du provider.
   - `AIAgentTool : ToolBase<…>` — un agent MAF comme *outil* d'un agent Orkeon
     (`AgentBuilder.WithAgentFrameworkTool`), le miroir du `AsAIFunction()` de MAF.
   La bibliothèque s'appuie sur des ports qu'Orkeon expose déjà (`ICrewOrchestrationService`,
   `ILlmProvider`, `ToolBase`) ; rien n'a changé dans Domain, Application ou Infrastructure
   pour elle — à une exception près ci-dessous, qui était un bug.
3. **Le contexte initial d'un kickoff atteint les agents.** `CrewInput.InitialContext` était
   mappé dans l'entrée domaine et lu par rien : `--initial-context`, le champ de Studio et
   tout `CrewInput.Empty("…")` programmatique n'atteignaient aucun prompt. L'orchestrateur
   l'expose désormais comme variable de prompt `initial_context` (une variable de ce nom
   fournie par l'appelant l'emporte). `CrewAgent` s'y appuie ; tout utilisateur qui a un jour
   passé `--initial-context` aussi.

## Conséquences

- Un workflow MAF peut appeler un crew Orkeon comme n'importe quel agent ; un crew Orkeon
  peut déléguer à un agent MAF, ou être répondu par lui. Vérifié de bout en bout sur un
  modèle local (`examples/interop/agent-framework/`, les deux sens, `llama3.2:1b` via Ollama).
- L'interop suit `Microsoft.Agents.AI.Abstractions` 1.x. Un changement cassant côté MAF est
  absorbé dans ce seul paquet ; l'ombrelle ne le voit jamais.
- `Orkeon.Interop.AgentFramework` relève de l'exception *interopérabilité* du gel de
  périmètre (CONTRIBUTING) : l'interop abaisse le coût d'essayer Orkeon, elle n'élargit pas
  sa surface.
- Le streaming depuis un crew est la réponse finale en une seule mise à jour : un crew n'a
  pas de flux de tokens à transmettre. Un appelant en streaming fonctionne ; il ne voit pas
  la sortie intermédiaire des agents.

## Amendement — 2026-10-01 (GAP-25) : un scope par tour

`AddOrkeonAgentFramework()` enregistrait un `ICrewAgentFactory` **singleton** qui capturait
l'`ICrewOrchestrationService` **scopé** de l'hôte : tous les `CrewAgent` d'un hôte partageaient
un orchestrateur, et ses dépôts scopés, pour la vie du processus, et un hôte qui valide les
scopes (le défaut en Development) refusait de résoudre la fabrique. L'orchestrateur reste scopé
— ses dépôts le sont par conception — et la fabrique ne tient plus que l'`IServiceScopeFactory`
de l'hôte.

- `ICrewAgentFactory.Create(loadCrew, name, description?)` remplace `Create(crewId, …)` et
  `Create(crew)`. Chaque tour de l'agent qu'elle rend ouvre un scope, appelle `loadCrew` avec le
  fournisseur du scope — il y enregistre la crew (ajoute ses agents, ses tâches et la crew aux
  dépôts du scope, ou la charge par l'`ICrewFactory` du scope) et rend son identifiant —, lance
  cette crew par l'orchestrateur du scope, puis libère le scope. Une crew enregistrée une fois
  dans un dépôt résolu à la racine était l'ancien contrat ; un scope ne la voit jamais.
- La même forme est publique : `new CrewAgent(IServiceScopeFactory, loadCrew, name, description?)` ;
  son identifiant d'agent est `orkeon-crew-<name>` et son `CrewId` vaut `null` (une crew neuve à
  chaque tour). `new CrewAgent(orchestrator, crewId|crew)` reste, pour l'appelant qui possède
  l'orchestrateur et son scope.

## Amendement — 2026-10-03 (GAP-34) : l'agent MAF répond

**Ce qui était faux.** `WithAgentFrameworkAgent` posait `Agent.FunctionCallingLlm`, un champ que rien
ne lisait : un agent ainsi construit exécutait ses tâches sur le modèle par défaut de l'hôte (ou sur
son profil), avec ses outils Orkeon, et l'agent MAF n'entendait rien — ni ses instructions, ni ses
outils, ni sa mémoire ne servaient. Le test d'interop vérifiait le champ ; l'exemple n'exerçait que la
forme « outil ». La conséquence « un crew Orkeon peut … être répondu par un agent MAF » n'avait
jamais tourné de bout en bout, et la phrase de la décision 2, « rien n'a changé dans Domain,
Application ou Infrastructure pour elle », ne tient plus :

- **Domain.** `Agent.FunctionCallingLlm` devient `Agent.Llm` (`AgentCreateOptions.Llm`,
  `AgentSnapshot.Llm`, le paramètre `llm` d'`Agent.Create`) : le fournisseur sur lequel tournent les
  tours de l'agent, le `llm` de CrewAI donné comme objet. Un agent tourne sur son propre fournisseur ou
  sur un profil de l'hôte, jamais les deux — `Agent.Create` et `AgentBuilder.Build()` refusent les deux.
  `LlmProviderCapabilities.RunsOwnTools`, que déclare `AIAgentLlmProvider` : un agent dont le
  fournisseur propre fait ses propres outils se voit refuser outils Orkeon et délégation à sa création,
  et `AddTool` comme `UpdateConfiguration` gardent la règle.
- **Application.** `ILlmProfileRegistry.ForProvider(fournisseur)` construit le client du fournisseur
  propre d'un agent, une fois par instance, compté. L'orchestrateur y fait tourner les tours de l'agent,
  leur correction et son bulletin, après le profil que nomme le `llm_override` d'une tâche et avant le
  profil de l'agent. Une tâche qui tombe sur un fournisseur qui fait ses propres outils — celui de
  l'agent, celui d'un profil, le défaut — avec des outils à tenir (les siens, `human_input`) échoue avant
  tout appel, en nommant les outils et les deux remèdes.
- **Infrastructure.** `LlmProfileRegistry.ForProvider` est une nouvelle entrée du chemin compté.
  `ManagerLlmResolver` fait tourner le fournisseur propre d'un agent manager hiérarchique
  (`provider:<nom>`), après `Crew.ManagerLlm`. `MeteredLlmProvider` compte un appel une fois : un appel
  extérieur pendant lequel un compteur plus proche du modèle a compté ne rapporte rien, si bien qu'un
  agent MAF bâti sur le modèle d'Orkeon est compté au nom du vrai fournisseur et du vrai modèle, et un
  agent MAF sur un client qu'Orkeon ne compte pas sous `agent-framework:<nom>`.

**La session.** Une par fournisseur, comme l'a choisi la décision 2, pour que la mémoire d'un agent MAF
couvre les tours et les tâches de l'agent Orkeon — mais nourrie une fois : un appel qui prolonge la
conversation que tient la session (les messages de l'appel précédent, puis la réponse du fournisseur)
n'envoie que la suite, tout autre appel — une nouvelle tâche — envoie tout, et les appels passent un à
la fois. Les tâches d'un même agent MAF passent donc l'une après l'autre, même dans une vague Parallel ;
la sortie d'une tâche précédente atteint le modèle par la session et par les sorties précédentes du
prompt, et la session grandit à chaque tâche (un `ChatReducer` côté MAF la borne).

**Ce que le pont n'envoie pas, il le dit.** Les options de la configuration d'un appel (modèle,
température, format de réponse, réflexion…) n'atteignent jamais un agent MAF ; chacune déclarée produit
un avertissement structuré, une fois par run d'une crew et par option. Le streaming ne change pas : un
tour diffusé reçoit la réponse MAF en un fragment.

**Vérifié.** De bout en bout dans la suite d'interop (l'agent MAF reçoit le prompt composé, le défaut
n'est pas appelé, un seul événement d'usage au nom de l'agent ; compté une fois sur le modèle d'Orkeon ;
outils refusés au build, à `AddTool` et au run), et par une troisième section de
`examples/interop/agent-framework/`, lancée sur le fournisseur écho ; la campagne sur un modèle local
revient au propriétaire.
