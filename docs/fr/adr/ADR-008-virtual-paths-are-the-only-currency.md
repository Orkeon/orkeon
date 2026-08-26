> 🇬🇧 [English version](../../adr/ADR-008-virtual-paths-are-the-only-currency.md)

> **Voir aussi** : [Conformité VFS](../architecture/vfs-compliance.md) · [Référence CLI](../reference/cli.md) · [Retour à l'index](../INDEX.md)

# ADR-008 — Les chemins virtuels sont la seule monnaie versée aux agents

**Statut** : Accepté · **Date** : 2026-08-25
· **Portée** : `Orkeon.Domain/FileSystem`, `Orkeon.Hosting`, `Orkeon.Host`, `Orkeon.Scripting.Cli`, `Orkeon.Studio.*`

## Contexte

La doctrine de système de fichiers d'Orkeon a été fixée tôt et tenue fermement : la règle **Q3,
« chemins virtuels partout »** — aucune API, propriété ou DTO ne porte de chemin physique hors de
`FileSystemService` lui-même — appuyée par l'analyseur `Orkeon.Compliance.Vfs`, par un `MountInfo`
délibérément sans `BasePath`, et par des tests vérifiant qu'un message de refus ne nomme jamais un
chemin disque. Quand un flag CLI a un jour accepté un chemin réel (`--prebuild-index <real-path>`),
il a été **retiré** plutôt que toléré : la codebase se monte via `-m ../path:/src:ro`.

La doctrine tenait à l'intérieur du framework. Elle ne tenait pas à la frontière où les montages
se créent.

Le code du framework calcule des chemins physiques absolus avant que le VFS n'existe — le dossier
de définition du crew, le dossier `--llm-log`. Ces chemins doivent ensuite être lisibles *à
travers* le VFS. Le raccourci pris a été de les monter **1:1**, physique égal virtuel :

```
{configDir}:{configDir}:ro          # RunnerExecution
{llmLogPath}:{llmLogPath}:rw        # RunnerExecution, RunCommand
{crewDir}:{crewDir}:ro              # HostCrewMounts, par crew hébergé
```

pour que le chemin absolu déjà en main se résolve tel quel, « sans gymnastique de préfixe » comme
le disait le code. Pour que ces chaînes se parsent, `FileSystemMount.IsValidVirtualPath` avait été
élargi pour accepter un chemin de lecteur Windows comme chemin **virtuel**. La brèche était donc
dans le contrat du VFS lui-même, pas chez un appelant.

Les conséquences n'étaient pas théoriques. Un montage issu d'une mount-string reçoit
`MountVisibility.AgentFacing` — `Parse` n'a aucun moyen de dire autre chose — donc ces dossiers
étaient listés aux agents :

- `list_mounts` retournait `C:\Users\…\mon-equipe` comme **chemin** de montage ;
- tout message de refus nomme les montages disponibles, et
  `FileSystemService.CollectBasePaths` **exemptait délibérément les montages identité de la
  rédaction** (les rédiger aurait produit `Available mounts: [REDACTED]`), donc le garde-fou était
  désarmé pour exactement les montages qui en avaient besoin ;
- la réécriture de chemins de `ShellCommandTool` travaillait sur la même liste ;
- là où la table de montages est rendue dans un prompt système, un chemin disque absolu
  apparaissait sous la phrase *« All file operations must use these virtual paths. Absolute or
  unmounted paths are not allowed. »*

Studio reflétait ensuite tout cela fidèlement — y compris dans deux écrans que lit un novice :
les chips de dossiers du Composer et, pire, la ligne « Sur quel dossier » de l'éditeur d'agent,
qui joignait les mount-strings `physique:virtuel:droits` brutes sous un libellé parlant de ce que
cet agent peut voir.

Le raccourci n'a jamais eu de nécessité technique. Le chemin scripté montait déjà son entrée en
`/script:ro`, le banc de la forge monte `/workspace`, `/forge` et `/output`, et le chargeur de
crew est agnostique : il lit le chemin qu'on lui donne via `IFileSystemService`.

## Décision

**Un chemin physique n'est jamais un chemin virtuel.** Concrètement :

1. **Les runners montent sous un nom.** Le dossier du crew est `/crew` (un crew mono-fichier
   s'adresse en `/crew/<fichier>.yaml`), les crews d'un démon sont `/crews`, `/crews-1`, …, le
   dossier d'un script reste `/script`. Le chargeur reçoit l'orthographe virtuelle.
2. **`IsValidVirtualPath` est resserré au seul `/…`.** Un chemin virtuel à lettre de lecteur est
   refusé avec un message qui nomme le remède. Le segment *physique* garde son traitement de
   lettre de lecteur — ce côté-là est légitimement un chemin disque.
3. **Les montages d'infrastructure sont invisibles.** `Orkeon:FileSystem:InternalMounts` porte
   les montages enregistrés en `MountVisibility.Internal` : résolubles par le VFS, absents de
   `GetAvailableMounts()` et donc de `list_mounts`, des tables de montages des prompts et des
   messages de refus — un message de refus est lu par le LLM, donc `FileSystemRegistry` construit
   ses listes « Available mounts » et « Mounts granting … » à partir de l'ensemble agent, pas de
   tous les montages. Le journal d'échanges LLM y vit — le VFS doit l'atteindre, aucun agent n'a
   à l'adresser. C'est une clé de configuration plutôt qu'un service hébergé parce que les
   runners ne démarrent jamais l'hôte : un `IHostedService` ne se déclencherait jamais sous
   `--validate` ou `--list-tools`.

   **`Internal` est une visibilité, pas un isolement.** Le montage reste *résoluble*, et
   `IFileSystemService` n'a aucune notion de qui appelle — le journal d'échanges écrit par l'API
   même qu'utilisent les outils agents. Un agent qui connaît le nom peut donc toujours l'adresser.
   Rien ne va derrière un montage interne qu'un agent connaissant son nom ne doit pas lire ; en
   faire une vraie frontière demande un accesseur privilégié, ce que la section ci-dessous réserve.
4. **La règle des écrans est délimitée, pas absolue.** Aucun chemin physique dans un contexte
   **agent ou novice**. Les surfaces **expertes** — la table des montages effectifs, l'aperçu de
   mount-string du sélecteur — continuent d'afficher les chaînes réelles : leur raison d'être est
   d'énoncer la ligne de commande exacte.
5. **Un `--mount` utilisateur revendiquant une racine réservée est refusé** avec une ligne
   actionnable, au lieu de remonter en exception « chemins virtuels dupliqués » depuis une
   fabrique DI.

La dérogation de rédaction survit pour le seul cas restant : un montage Unix écrit pareil des deux
côtés (`/output:/output:rw`, la convention conteneur). Elle ne couvre plus l'injection du runner.

## Conséquences

- Un agent ne peut plus apprendre l'organisation disque de l'opérateur via le VFS, et les
  messages de refus sont rédigés sans condition pour tout dossier injecté par le runner.
- Le journal d'échanges cesse d'être annoncé aux agents comme un montage inscriptible — il
  contient les prompts complets et les charges utiles des API.
- **Rupture** : `--mount` n'accepte plus de chemin virtuel à lettre de lecteur ; le dossier du
  crew est `/crew` et les crews hébergés `/crews*`. Rien dans le dépôt ne reposait sur l'ancienne
  orthographe, et aucun crew publié ne le peut : les mount-strings sont fournies par lancement,
  jamais stockées dans un crew.
- Activer `--llm-log` ne décale plus `Orkeon:FileSystem:Mounts:{i}`, le journal ayant sa propre
  clé. La prédiction d'index de Studio se simplifie et reste vraie.
- Studio duplique les trois racines virtuelles (il ne peut pas référencer `Orkeon.Hosting`) ; un
  test de dérive les épingle sur `RunnerMounts`.

## Ce que cet ADR ne tranche délibérément pas

Un crew ne peut toujours **pas déclarer les dossiers dont il a besoin**. `CrewYamlConfig` n'a pas
de bloc `filesystem:`, donc le liage virtuel→physique est fourni entièrement depuis l'extérieur de
l'artefact portable — par un argument `--mount`, par `appsettings`, ou par le sidecar de Studio.
C'est ce trou qui permettait de lancer une équipe promue sans aucun `/output` alors que ses propres
tâches déclaraient `deliverable: /output/…` ; la casse immédiate est refermée en dérivant ces
racines à la promotion et à l'adoption, ce qui est un remède aux bords, pas le contrat lui-même.

Déclarer les besoins de fichiers sur le crew — sur le modèle du bloc `links:` existant, validé par
`orkeon run --validate` plutôt qu'en échouant au premier appel d'outil — change la grammaire YAML,
le DSL scripté, le blueprint de la forge et l'API publique de deux assemblies. Cela appartient à
une version autorisée à bouger la grammaire, pas à une release candidate. Cet ADR en réserve la
place.

Il ne donne pas non plus au VFS d'**appelant privilégié**. `IFileSystemService` est une seule
surface pour le framework comme pour les outils agents, donc `MountVisibility.Internal` peut
retirer un montage de tous les listages mais ne peut pas refuser un agent qui l'adresse par son
nom. Fermer cela demande un second accesseur — une poignée scopée que le runner détient et que le
registre d'outils ne voit jamais — qui touche le contrat du Domain et toutes ses implémentations.
D'ici là la règle est une discipline, énoncée sur `FileSystemOptions.InternalMounts` : un montage
interne cache un répertoire, il ne le protège pas.
