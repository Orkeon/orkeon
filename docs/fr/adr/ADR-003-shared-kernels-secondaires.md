> 🇬🇧 [English version](../../adr/ADR-003-shared-kernels-secondaires.md)

> **Voir aussi** : [ADR-002 — Tool abstractions shared kernel](./ADR-002-tool-abstractions-shared-kernel.md) · [ADR famille Tools.* hétérogène](./ADR-005-famille-tools-heterogene.md) · [Retour à l'index](../INDEX.md)

# ADR-003 — Shared kernels secondaires : `Analysis.Abstractions` et `Analysis`

**Statut** : Accepté · **Date** : 2026-06 · **Portée** : `Orkeon.Application` → `Orkeon.Analysis.Abstractions` ; `Orkeon.Infrastructure` → `Orkeon.Analysis`

## Contexte

Deux références traversent le sens « canonique » de l'oignon décrit dans `CLAUDE.md`
(« Application → Domain » / « Infrastructure → Domain + Application »), au profit du sous-système
RaggableTree (`src/analysis/`) :

1. **`Application → Orkeon.Analysis.Abstractions`**
   (`src/core/Orkeon.Application/Orkeon.Application.csproj`).
   Usage **minimal** : un seul fichier consomme ce projet —
   `src/core/Orkeon.Application/Crew/DeliverableResolvers/FinalMessageResolver.cs`, pour l'interface
   `IInlineFqnValidator`, injectée en dépendance **optionnelle**. `Orkeon.Analysis.Abstractions` ne
   dépend lui-même que de `Domain` ; ce n'est donc pas une inversion du sens des dépendances, mais
   l'Application paie une référence de projet entière (64 fichiers d'interfaces/DTOs RaggableTree)
   pour une seule interface.

2. **`Infrastructure → Orkeon.Analysis` (concret, pas seulement les abstractions)**
   (`src/core/Orkeon.Infrastructure/Orkeon.Infrastructure.csproj`).
   Usage **circonscrit à la composition DI** : `Orkeon.Analysis` n'est référencé que par
   `src/core/Orkeon.Infrastructure/DependencyInjection/RaggableTreeInfrastructureExtensions.cs`.
   L'Infrastructure joue ici un rôle de composition root pour câbler RaggableTree dans le conteneur.

## Décision

**Documenter et assumer** ces deux couplages en l'état pour la version courante, sans les résorber
dans l'immédiat :

- `Orkeon.Analysis.Abstractions` est traité comme un **shared kernel secondaire** (même statut que
  `Tools.Abstractions`, cf. [ADR-002](./ADR-002-tool-abstractions-shared-kernel.md)) : projet
  d'abstractions ne dépendant que de `Domain`, donc consommable par `Application` sans cycle ni
  inversion.
- La référence `Infrastructure → Orkeon.Analysis` concret est acceptée comme **câblage de
  composition** localisé à un unique fichier d'extensions DI, l'Infrastructure assumant son rôle de
  composition root pour le sous-système RaggableTree.

## Option de résorption (différée)

Cette décision n'est pas définitive. Une résorption reste possible et préférable à terme :

- **Application** : déplacer le port `IInlineFqnValidator` dans `Application` (ou l'interface dans
  `Domain`), supprimant la référence à `Analysis.Abstractions` et rendant `Application` iso-`CLAUDE.md`
  (« Application → Domain »).
- **Infrastructure** : remonter la composition RaggableTree dans l'hôte (composition root applicatif
  réel, p. ex. `ConsoleApp`), retirant la référence `Analysis` concret de l'Infrastructure.

Tant que ces résorptions ne sont pas faites, la présente ADR sert de justification consultable.

## Conséquences

- **Positif** : les couplages sont désormais **traçables et challengeables** au lieu d'être
  renégociés à l'aveugle à chaque revue. Aucun n'inverse le sens des dépendances ni n'introduit de
  cycle (vérifié au commit `b5179b3c`).
- **Vigilance** : `Analysis.Abstractions` doit **rester dépendant du seul `Domain`** ; toute
  extension de l'usage de `Analysis` concret dans l'Infrastructure (au-delà du câblage DI) doit
  rouvrir cette ADR.
