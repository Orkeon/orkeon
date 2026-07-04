> 🇬🇧 [English version](../../adr/ADR-005-famille-tools-heterogene.md)

> **Voir aussi** : [ADR-003 — Shared kernels secondaires](./ADR-003-shared-kernels-secondaires.md) · [Retour à l'index](../INDEX.md)

# ADR-005 — Famille `Tools.*` hétérogène : `Tools.Web` et `Tools.EventHub` dépendent de `Application`

**Statut** : Accepté · **Date** : 2026-06 · **Portée** : `Orkeon.Tools.Web` → `Orkeon.Application` ; `Orkeon.Tools.EventHub` → `Orkeon.Application`

## Contexte

La famille de paquets d'outils `Tools.*` n'est pas homogène dans ses dépendances :

- **Quatre paquets** (`Tools.Code`, `Tools.Data`, `Tools.FileSystem`, `Tools.Analysis`) se contentent
  de `Orkeon.Tools.Abstractions` (+ abstractions), conformément au profil attendu : un outil ne
  devrait dépendre que des contrats d'outils et du `Domain`.
- **Deux paquets** (`Tools.Web` et `Tools.EventHub`) montent jusqu'à `Orkeon.Application`.

Cette asymétrie peut surprendre un contributeur qui s'attend à ce que tous les paquets `Tools.*`
partagent le même socle de dépendances.

## Décision

**Accepter et documenter** que `Tools.Web` et `Tools.EventHub` dépendent de `Application`, parce que
leurs outils ont besoin de services applicatifs (ports/use cases) que `Tools.Abstractions` seul
n'expose pas — par exemple la coordination de tâches, les canaux A2A, ou des services de
contexte applicatif.

Le sens de dépendance reste correct : `Tools.Web`/`Tools.EventHub` sont des paquets périphériques qui
dépendent de `Application` (couche interne), et non l'inverse. Aucun cycle, aucune inversion.

## Alternatives rejetées

1. **Forcer tous les `Tools.*` à ne dépendre que de `Tools.Abstractions`.** Rejeté : cela exigerait
   de dupliquer ou de remonter dans `Tools.Abstractions` des contrats applicatifs qui n'ont pas leur
   place dans un paquet d'abstractions d'outils pures, ou d'introduire des indirections artificielles.
2. **Déplacer `Tools.Web`/`Tools.EventHub` hors de la famille `Tools.*`.** Rejeté : ce sont bien des
   fournisseurs d'outils ; leur place dans la nomenclature `Tools.*` est cohérente côté découverte et
   packaging.

## Conséquences

- **Positif** : la règle implicite « un paquet `Tools.*` peut dépendre de `Application` quand ses
  outils requièrent des services applicatifs » est désormais explicite. Les revues n'ont plus à
  trancher ce cas au coup par coup.
- **Vigilance** : préférer dépendre de `Tools.Abstractions` seul tant qu'un outil n'a pas de besoin
  applicatif avéré ; ne remonter vers `Application` que sur nécessité documentée.
