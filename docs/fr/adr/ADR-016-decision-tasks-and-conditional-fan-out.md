> 🇬🇧 [English version](../../adr/ADR-016-decision-tasks-and-conditional-fan-out.md)

> **Voir aussi** : [Modèles de décision](../architecture/decision-models.md) · [Schéma YAML](../architecture/yaml-schema.md) · [Bus d'événements du run](../architecture/run-event-bus.md) · [ADR-015](./ADR-015-model-provider-categories-and-decision-stages.md) · [Retour à l'index](../INDEX.md)

# ADR-016 — Des tâches de décision, et une condition `when` qui ignore au lieu d'échouer

**Statut** : Accepté · **Date** : 2026-10-28
· **Périmètre** : `Orkeon.Domain` (`TaskDecision`, `DecisionOutcome`, `CrewTaskBase`), `Orkeon.Infrastructure` (`Crew/Strategies/` : Sequential, Parallel et Graph, `DecisionPreflight`), `Orkeon.Application` (`TaskExecutionSnapshot`), le CLI `orkeon run` (`--no-decision`, l'événement `task.completed`) et Studio (`RunProgressModel`)

## Contexte

L'[ADR-015](./ADR-015-model-provider-categories-and-decision-stages.md) a placé le modèle de
décision derrière son propre port et l'a employé autour d'un appel au LLM. Un équipage est un flux
de tâches, et le même modèle peut aiguiller ce flux : lire un ticket, décider sa catégorie, et ne
lancer que la tâche qui convient. Jusque-là, un équipage ne pouvait pas bifurquer sur le jugement
d'un modèle sans qu'un agent écrive un texte que la tâche suivante devait analyser.

## Décision

1. **Une tâche de décision** est une tâche qui porte un bloc `decision` au lieu d'un agent. Elle
   envoie ses questions typées au modèle de décision en une seule requête et sa sortie est les
   réponses, jamais du texte. Une tâche a un agent ou une décision, jamais les deux.
2. **Une condition `when`** sur une tâche suivante nomme une tâche de décision et les réponses
   qu'elle doit avoir données : une option (lue sous un seuil de confiance, 0,5 par défaut) ou un
   seuil sur une probabilité ou un score. `otherwise` vaut quand aucune condition sœur sur la même
   décision n'est vraie.
3. **Une condition fausse ignore, elle n'échoue jamais.** La tâche est ignorée avec la raison
   consignée (`skipReason`), une tâche dont toutes les dépendances ont été ignorées l'est aussi, et
   une tâche ignorée ne laisse rien dans le contexte d'une tâche suivante. Elle ne compte ni comme
   succès ni comme échec. Une décision que le modèle ne peut pas prendre est une tâche en échec et
   suit la règle existante de l'équipage.
4. **Sequential, Parallel et Graph** exécutent les tâches de décision. Hierarchical, Consensual et
   Autonomous n'exécutent pas une tâche sur la tâche qu'elle nomme : un équipage qui emploie une
   tâche de décision ou un `when` avec l'un d'eux est refusé au chargement.
5. **Le circuit peut être coupé.** Sans modèle de décision configuré, ou avec `orkeon run
   --no-decision`, une tâche de décision n'émet aucune requête et répond par son `fallback` ; son
   événement `task.completed` porte `defaulted: true` et les conditions s'évaluent sur cette
   réponse. Une requête en échec n'est jamais transformée en repli.
6. **Le run le rapporte.** `task.completed` porte `skipReason`, `defaulted`, `isDecision` et
   `decision` (les réponses en JSON compact) ; `TaskExecutionSnapshot` porte `SkipReason`,
   `Defaulted`, `IsDecision` et `DecisionAnswers` ; les consoles et le `RunProgressModel` de Studio
   les montrent. `TaskCompletedEvent.AgentId` est nullable, une tâche de décision n'ayant pas
   d'agent.

## Conséquences

- Une tâche de décision coûte une requête par évaluation ; les seules répétitions sont les
  nouvelles tentatives de l'adaptateur sur limitation, et un processus qui rejoue une tâche en
  échec (Graph) évalue la décision de nouveau.
- Un équipage sans tâche de décision tourne comme avant, quel que soit le circuit.
- Un client du bus d'événements doit lire `skipped` et `skipReason` ensemble : une tâche ignorée
  n'est ni un échec ni une sortie.

## Étapes suivantes

Le modèle plus riche de l'entretien est reporté par simplicité (spec § 10) : nœuds de décision
avant ou après une tâche (garde sur son entrée ou sa réponse finale), issues de type « aller à »,
boucles, journal des décisions et chaînes avec réponses par défaut « dernière réponse » ;
correctifs des profils gardés ; regroupement du reranker et surfaces doctor ; questions `box` et
images. Une nouvelle tentative ou un repli quand une requête échoue n'est pas prévu : le repli
n'existe que pour un circuit coupé.

## Alternatives écartées

- **Un agent qui classe et écrit l'aiguillage** : écarté. Il dépense un appel au LLM pour un
  jugement et rend un texte que la tâche suivante doit analyser.
- **Une condition qui fait échouer la tâche quand elle est fausse** : écarté. Une branche non prise
  est le cas attendu, et un échec déclencherait les nouvelles tentatives et la gestion d'erreur de
  l'équipage.
