> 🇬🇧 [English version](../../adr/ADR-004-jumeaux-de-nommage-scripting.md)

> **Voir aussi** : [ADR-002 — Tool abstractions shared kernel](./ADR-002-tool-abstractions-shared-kernel.md) · [Retour à l'index](../INDEX.md)

# ADR-004 — Jumeaux de nommage : `Orkeon.Cli.Scripting` vs `Orkeon.Scripting.Cli`

**Statut** : Accepté (documentation de l'existant) · **Date** : 2026-06
· **Portée** : `src/cli/Orkeon.Cli.Scripting`, `src/scripting/Orkeon.Scripting.Cli`

## Contexte

Deux projets portent des noms quasi anagrammes, distincts par simple permutation de segments —
un piège permanent pour les issues, la documentation, et les recherches NuGet :

| PackageId | Dossier | Rôle | LOC (commit `b5179b3c`) |
|---|---|---|---:|
| `Orkeon.Cli.Scripting` | `src/cli/` | **Commandes interactives** scriptées TypeScript ↔ CLI. Adaptateur entre `Orkeon.Cli.Abstractions` et `Orkeon.Scripting` (Jint + esbuild). | 4 746 |
| `Orkeon.Scripting.Cli` | `src/scripting/` | **Point d'entrée en ligne de commande** du DSL de scripting : le tool installable `orkeon run script.ork.ts` (`PackAsTool=true`, `ToolCommandName=orkeon`, `AssemblyName=orkeon`). | 483 |

Les deux noms sont structurellement valides (chemin = suffixe de namespace) ; ce n'est pas une dérive
de convention, mais une **collision cognitive** : `Cli.Scripting` = « du scripting *dans* la CLI »,
`Scripting.Cli` = « la *CLI* du scripting ».

## Décision

Pour la version courante, **conserver les deux noms tels quels et documenter explicitement leur
distinction**, plutôt que de renommer dans l'immédiat.

- La mise en garde et le rôle de chaque projet sont consignés ici et dans `CLAUDE.md` (section
  « Working Directory Structure » / « Important Notes »), pour que tout contributeur ou agent IA
  désambiguïse les deux paquets sans avoir à inspecter les `.csproj`.
- Le **renommage effectif** (ORG-007) est un chantier de code distinct et plus lourd (rupture de
  PackageId, redirections, mise à jour des références) : il relève d'une décision humaine séparée et
  n'est **pas** tranché par cette ADR. Cette ADR documente la situation et fournit le repère ;
  elle n'engage pas un nom cible.

## Conséquences

- **Positif** : la confusion la plus fréquente (« lequel est le tool `orkeon` ? ») a maintenant une
  réponse écrite et unique. C'est `Orkeon.Scripting.Cli` (segment final `.Cli` = l'exécutable).
- **Vigilance** : tant que le renommage n'est pas fait, toute nouvelle mention dans la doc ou les
  issues doit lever l'ambiguïté en rappelant le rôle (tool `orkeon` vs commandes interactives CLI).
- **Repère mnémotechnique** : le projet dont le **dernier** segment est `Cli` (`Orkeon.Scripting.Cli`)
  est l'**exécutable** ; celui dont le dernier segment est `Scripting` (`Orkeon.Cli.Scripting`) est la
  **bibliothèque de commandes**.
