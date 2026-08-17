> 🇬🇧 [English version](../../adr/ADR-007-d3-renommage-cli-commands-scripting.md)

> **Voir aussi** : [ADR-004 — Jumeaux de nommage (remplacé)](./ADR-004-jumeaux-de-nommage-scripting.md) · [Retour à l'index](../INDEX.md)

# ADR-007 — D3 tranchée : renommage de `Orkeon.Cli.Scripting` en `Orkeon.Cli.Commands.Scripting`

**Statut** : Accepté · **Date** : 2026-08-17 · **Remplace** : ADR-004 · **Clôt** : la décision D3 (gate de publication), ORG-007
· **Portée** : `src/cli/Orkeon.Cli.Commands.Scripting`, `tests/cli/Orkeon.Cli.Commands.Scripting.Tests`

## Contexte

L'ADR-004 documentait la paire quasi anagramme `Orkeon.Cli.Scripting` (bibliothèque de
commandes interactives scriptées en TypeScript) / `Orkeon.Scripting.Cli` (le tool dotnet
installable `orkeon`) sans trancher le renommage — renvoyé à une décision séparée (ORG-007).
La matrice de publication NuGet conditionnait toute extension de l'écosystème à cette décision
(**D3**), parce que la fenêtre de renommage se ferme définitivement au premier
`dotnet nuget push` : les PackageId se figent à vie.

Trois options ont été pesées (fiche PUB-02) : **A** garder les deux noms et vivre avec la
collision cognitive permanente ; **B** renommer la bibliothèque (4 746 LOC, consommateurs
internes uniquement) ; **C** renommer le tool (483 LOC, mais son PackageId *est* la commande
`dotnet tool install` — le nom le plus visible publiquement).

## Décision

**Option B.** La bibliothèque de commandes est renommée :

| | Avant | Après |
|---|---|---|
| PackageId / assembly / namespace racine | `Orkeon.Cli.Scripting` | `Orkeon.Cli.Commands.Scripting` |
| Dossier du projet | `src/cli/Orkeon.Cli.Scripting/` | `src/cli/Orkeon.Cli.Commands.Scripting/` |
| Projet de test | `Orkeon.Cli.Scripting.Tests` | `Orkeon.Cli.Commands.Scripting.Tests` |

`Orkeon.Scripting.Cli` (le tool `orkeon`) **garde son nom** : son PackageId est la commande
d'installation que tapent les utilisateurs, et le mnémonique de l'ADR-004 (« dernier segment
`Cli` = l'exécutable ») reste vrai partout où il a été ancré.

Le coût du renommage est payé une seule fois, avant toute publication : aucun paquet NuGet n'a
jamais porté l'ancien nom — pas de redirect, pas de shim de dépréciation, aucune casse de
consommateur hors de ce dépôt.

## Conséquences

- **D3 est levée** dans `docs/reference/publication-matrix.md` — l'extension NuGet n'attend
  plus que la confirmation produit du mainteneur (et le câblage PUB-03).
- Le nouveau nom dit le rôle directement : des *commandes* pour le CLI, réalisées par du
  *scripting* — ce n'est plus une permutation du nom du tool.
- Les mentions historiques (`CHANGELOG.md`, ADR-004) gardent l'ancien nom à dessein ; tout ce
  qui est actif (code, solution, docs et miroirs FR, exemples) utilise le nouveau.
- ORG-007 est clos par cet ADR.
