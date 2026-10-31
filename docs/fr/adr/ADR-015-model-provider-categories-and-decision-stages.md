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
2. **`Decision` a son propre port**, `IDecisionModel` (`DecideAsync`, `ListModelsAsync`), avec des
   DTO typés : des questions polymorphes (`choice`, `score`, `noul`, `box`) et leurs réponses.
   `TypeSafeDecisionModel` en est le premier adaptateur, opt-in via `AddOrkeonTypeSafe`. Le compte
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
   sauf si le profil pose `FailClosed` : il est alors bloqué.
6. **Les autres usages sont secondaires** et réutilisent le même port : la garde d'entrée des
   agents `TypeSafeInputGuard`, le reranker RAG `decision` (alias `typesafe`) et le tool
   `system_one_decide`.

   > Amendement 2026-10-31 : `IDecisionModel.ListModelsAsync` supprimée (aucun appelant).

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
- Le `GuardPhase` du pipeline d'agent n'a pas de phase `Output` ; le filtrage des sorties passe
  par une étape de réponse d'un profil de décision ([Limites connues](../reference/limitations.md)).

## Alternatives examinées

- **Un 17ᵉ fournisseur LLM** : rejeté. `ILlmProvider` promet du texte, et chaque consommateur
  (agents, planificateur, consensus) aurait dû savoir que celui-ci n'en produit pas.
- **Une étape qui réécrit le prompt ou la réponse** : rejetée. Un filtre qui réécrit cache ce que
  le modèle a dit et rend un échec difficile à auditer ; un verdict se journalise, s'audite et se
  teste.
- **Une interface commune `IModelProvider` à toutes les catégories** : rejetée. Les contrats ne
  partagent qu'un nom ; une interface commune ne porterait que des casts.
