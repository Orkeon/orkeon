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
