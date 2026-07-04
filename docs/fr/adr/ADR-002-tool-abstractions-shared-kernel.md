> 🇬🇧 [English version](../../adr/ADR-002-tool-abstractions-shared-kernel.md)

> **Voir aussi** : [ADR shared kernels secondaires](./ADR-003-shared-kernels-secondaires.md) · [ADR jumeaux de nommage](./ADR-004-jumeaux-de-nommage-scripting.md) · [Retour à l'index](../INDEX.md)

# ADR-002 — `Orkeon.Tools.Abstractions` comme *shared kernel* de l'Infrastructure

**Statut** : Accepté · **Date** : 2026-06 · **Portée** : `Orkeon.Infrastructure` → `Orkeon.Tools.Abstractions`

## Contexte

L'architecture en oignon d'Orkeon impose un sens de dépendance strict : `Domain` (noyau), puis `Application`, puis `Infrastructure`. Le `CLAUDE.md` résume cette règle par « Infrastructure → Domain + Application ».

`Orkeon.Infrastructure` référence pourtant un quatrième projet : `Orkeon.Tools.Abstractions`
(`src/core/Orkeon.Infrastructure/Orkeon.Infrastructure.csproj`, ItemGroup commenté `ADR-002`).
L'usage est *load-bearing* et non marginal : **13 fichiers** de l'Infrastructure consomment
`using Orkeon.Tools.Abstractions` (vérifié au commit `b5179b3c`). L'Infrastructure héberge en effet
les implémentations concrètes d'outils (LLM tools, file system, orchestration), qui ont besoin des
contrats de base d'outils (`IBaseTool`, `ToolResult`, bases typées) pour s'enregistrer et s'exécuter.

La question : cette référence est-elle une violation du sens des dépendances de l'oignon ?

## Décision

Traiter `Orkeon.Tools.Abstractions` comme un **shared kernel** : un second noyau d'abstractions,
au même titre que `Orkeon.Domain`, dont les couches externes peuvent dépendre sans enfreindre la
règle de l'oignon.

Justification : `Orkeon.Tools.Abstractions` **ne dépend que de `Orkeon.Domain`**. Il n'introduit
donc aucune dépendance entrante vers `Application` ou `Infrastructure`, et ne crée aucun cycle. C'est
un paquet d'interfaces et de bases pures, exactement le profil d'un hub d'abstractions attendu dans
une architecture en oignon (in-degree élevé, out-degree minimal).

La référence `Infrastructure → Tools.Abstractions` est donc une **exception architecturale assumée**,
documentée par cette ADR, et signalée à la source par un commentaire inline pointant vers ce dossier
`docs/adr/`.

## Alternatives rejetées

1. **Déplacer les contrats d'outils dans `Orkeon.Domain`.** Rejeté : `Tools.Abstractions` agrège des
   contrats spécifiques à l'écosystème d'outils (validation, batch, télémétrie) qui n'ont pas leur
   place dans le noyau métier pur ; cela gonflerait le Domain et exposerait les contrats d'outils à
   tous les projets, y compris ceux qui n'en ont nul besoin.
2. **Réimplémenter des contrats d'outils privés dans l'Infrastructure.** Rejeté : duplication des
   interfaces, divergence inévitable avec les paquets `Tools.*`, perte de l'interopérabilité.
3. **Inverser via une interface de port dans `Application`.** Rejeté : la surface partagée
   (`IBaseTool` et bases typées) est trop large pour être réduite à un port ; le shared kernel est
   plus honnête et plus simple.

## Conséquences

- **Positif** : une seule source de vérité pour les contrats d'outils, réutilisée par l'Infrastructure
  et les six paquets `Tools.*` ; pas de cycle ; règle de l'oignon respectée dans l'esprit (dépendance
  vers un noyau d'abstractions sans état).
- **Négatif / vigilance** : `Tools.Abstractions` doit **rester sans dépendance autre que `Domain`**.
  Toute introduction d'une dépendance vers `Application`/`Infrastructure` dans ce paquet
  transformerait l'exception en violation réelle (cycle). À surveiller en revue.
- Le commentaire inline du `.csproj` doit pointer vers un chemin réel (`docs/adr/`) — corrigé dans le
  même chantier que cette ADR.
