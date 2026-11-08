> 🇬🇧 [English version](../../adr/ADR-015-model-provider-categories-and-decision-stages.md)

> **Voir aussi** : [Fournisseurs de modèles](../architecture/model-providers.md) · [Modèles de décision](../architecture/decision-models.md) · [Fournisseurs LLM](../architecture/llm-providers.md) · [Retour à l'index](../INDEX.md)

# ADR-015 — Des catégories de fournisseurs de modèles, et des étapes de décision qui jugent sans réécrire

**Statut** : Accepté · **Date** : 2026-10-27
· **Portée** : `Orkeon.Constants.Llm` (`ModelProviderCategory`, `ModelProviderCatalog`), `Orkeon.Application` (`IDecisionModel`, `IDecisionStage`, `DecisionVerdict`, les DTO de décision), `Orkeon.Infrastructure` (`Decision/TypeSafe/`, `LLMs/Decision/`, `system_one_decide`), `Orkeon.Rag` (`DecisionModelReranker`)

## Contexte

TypeSafe sert **Jev**, un modèle « System One ». Il reçoit un état (texte, JSON ou images) et des
questions typées, et répond par des décisions : un `choice` parmi des options, un `score` sur une
échelle ordonnée, un `noul` (la probabilité d'un oui) ou une `box` dans une image, chacune avec
des probabilités calibrées et une confiance. Il n'écrit aucun texte. Sa propre documentation le
présente comme un complément du LLM, pas comme un remplaçant.

Orkeon connaissait un seul genre de fournisseur de modèles : les 16 fournisseurs génératifs
derrière `ILlmProvider`. Les embedders et les rerankers existaient déjà, chacun avec son contrat,
mais rien ne les nommait comme des catégories. Jev n'entre dans aucun de ces contrats : le forcer
derrière `ILlmProvider` lui donnerait une signature `messages → texte` qu'il ne peut pas tenir, et
chaque vérification de capacité devrait l'exclure.

## Décision

1. **Les fournisseurs de modèles se rangent en catégories** : `Generation`, `Embedding`,
   `Reranking` et `Decision` (`ModelProviderCategory`). Une catégorie est une description, pas un
   contrat : chacune garde son interface, sans base commune. `ModelProviderCatalog` liste chaque
   fournisseur distant avec ses catégories, sa variable de clé et son endpoint par défaut. Un
   fournisseur qui sert plusieurs catégories (OpenAI, Ollama) n'y figure qu'une fois.
2. **`Decision` a son propre port**, `IDecisionModel` (`DecideAsync`), avec des
   DTO typés : des questions polymorphes (`choice`, `score`, `noul`, `box`) et leurs réponses.
   `SystemOneDecisionModel` en est l'adaptateur pour les fournisseurs System One, opt-in via
   `AddOrkeonDecisionProvider`. Le compte
   des fournisseurs LLM reste à 16.
3. **L'usage principal est une étape de décision autour d'un appel au LLM.** `DecisionLlmProvider`
   est un décorateur `ILlmProvider` qui exécute des étapes avant et après l'appel qu'il enveloppe.
   Une étape (`IDecisionStage`) renvoie un `DecisionVerdict` : `Continue`, `Block`, `Route` ou
   `Escalate`. **Une étape juge et ne réécrit jamais** : les messages et la réponse passent
   inchangés, et c'est le code qui agit selon le verdict. Trois étapes sont livrées (`route`,
   `guard`, `verify`) ; un hôte ajoute les siennes en C#.
4. **Un profil de décision est un profil LLM.** Il se déclare sous `Orkeon:Decision:Profiles`,
   s'enregistre via `AddOrkeonLlmProfile`, et une crew l'utilise avec `llm: { profile: <nom> }`.
   Il ne peut nommer que des profils LLM, jamais un autre profil de décision : aucun cycle ne peut
   se former ; l'hôte refuse un profil de décision nommé comme cible à sa construction, et un
   profil LLM qui n'existe pas à la première résolution du profil de décision.
5. **Quand le modèle de décision échoue, l'appel continue** avec un avertissement (fail-open),
   sauf si le profil pose `FailClosed` : il est alors bloqué. C'est la règle d'un *profil* de
   décision ; le juge LLM du gardien suit le défaut inverse (voir l'amendement du 2026-11-08 plus bas).
6. **Les autres usages sont secondaires** et réutilisent le même port : le juge LLM du gardien
   `LlmJudgeGuard` (l'entrée de l'agent et la sortie d'une tâche), le reranker RAG `decision` et le tool
   `system_one_decide`.

   > Amendement 2026-10-31 : `IDecisionModel.ListModelsAsync` supprimée (aucun appelant).

   > Amendement 2026-10-31 : la catégorie `Decision` gagne des fournisseurs, Liquid et Kev à côté de TypeSafe, tous derrière `IDecisionModel` et servis par `SystemOneDecisionModel` ; l'hôte nomme ou déduit un fournisseur actif. La forme de la catégorie ne change pas.

## Conséquences

- Un nouveau genre de modèle (parole, vision, un second fournisseur de décision) ajoute une
  catégorie et un port ; il ne tord pas `ILlmProvider`.
- Une étape de décision coûte un aller-retour de plus par phase où elle tourne. Un `Route` ou un
  `Escalate` après la réponse rappelle le LLM une fois ; un second est journalisé et ignoré, de
  sorte qu'un profil ne boucle jamais.
- L'étape ne voit que ce qu'on lui donne : les étapes de requête interrogent le dernier message
  utilisateur, les étapes de réponse la requête et la réponse. Une décision sur toute la
  conversation demande une étape personnalisée.
- Le compteur compte un appel de décision comme un usage `decision`, une seule fois, dans
  l'adaptateur.
- Depuis l'amendement du 2026-11-08, le `GuardPhase` du pipeline d'agent a une phase `Output`, portée
  par le juge LLM ; une étape de réponse d'un profil de décision juge toujours la réponse au sein d'un
  appel ([Limites connues](../reference/limitations.md)).

## Amendement du 2026-11-08 (FWK-19) : le juge LLM du gardien

- `GuardPhase.Output` existe : la sortie d'une tâche, une fois validée, est jugée avant le livrable,
  la mémoire et `task.completed`. `DecisionInputGuard` est devenu `LlmJudgeGuard`, qui juge les phases
  `Input` et `Output` par l'unique `IDecisionModel` (pas de clé `Profile` propre), sous
  `Orkeon:Guardian:Llm`. `Orkeon:Decision:Guard` a disparu.
- Le défaut du juge est **fail-closed** sur les deux phases quand il est activé (`FailMode: Closed`) :
  un texte que le modèle n'a pas pu juger est refusé. `FailMode: Open` le laisse passer avec un
  avertissement ; les deux modes auditent `guardian.judge.unavailable`. Le juge est coupé par défaut (un appel de
  modèle par phase).
- Tant que la phase de sortie est gardée, les deltas streamés d'une tâche sont retenus jusqu'au jugement et abandonnés s'il bloque : une sortie bloquée n'est jamais streamée. Un texte long est jugé en entier par blocs de `MaxContentChars` sous `FailMode: Closed` (un bloc qui bloque bloque ; 32 blocs au plus, un texte plus long est refusé), et sur sa tête et sa queue (la moitié de `MaxContentChars` chacune) sous `Open`, où le milieu n'est pas jugé (amendé après la revue finale FWK). La phase couvre les tâches de crew, pas `ctx.llm.act`.
- Les validateurs d'une sortie en corrigent la forme ; le juge en décide la politique. Ils ne
  fusionnent pas.
- Le juge tourne une fois par appel, hors de la chaîne de repli des profils (FWK-05) : un refus est un
  échec de tâche, jamais une raison de passer à un autre profil. Un appel que le fournisseur n'a
  jamais répondu n'a pas de sortie et n'est pas jugé.
- Le filtrage de sortie par une étape de réponse d'un profil de décision reste possible mais ne porte
  plus la phase de sortie du pipeline.

## Alternatives examinées

- **Un 17ᵉ fournisseur LLM** : rejeté. `ILlmProvider` promet du texte, et chaque consommateur
  (agents, planificateur, consensus) aurait dû savoir que celui-ci n'en produit pas.
- **Une étape qui réécrit le prompt ou la réponse** : rejetée. Un filtre qui réécrit cache ce que
  le modèle a dit et rend un échec difficile à auditer ; un verdict se journalise, s'audite et se
  teste.
- **Une interface commune `IModelProvider` à toutes les catégories** : rejetée. Les contrats ne
  partagent qu'un nom ; une interface commune ne porterait que des casts.
