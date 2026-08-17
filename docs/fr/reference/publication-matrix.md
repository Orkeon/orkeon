> 🇬🇧 [English version](../../reference/publication-matrix.md)

# Matrice de publication NuGet

Ce fichier est la source de vérité unique pour **les projets publiés sur NuGet**, afin que les
workflows (`ci.yml` validation, `publish.yml` pack + push sur tag, `release.yml` installateurs)
ne divergent plus jamais (OSS-011 / R8.3).

> **Statut — proposition, en attente de confirmation du mainteneur.** Seules les trois
> bibliothèques cœur sont câblées vers NuGet.org aujourd'hui (PUB-03, 2026-08-17) ; le
> premier push réel partira au prochain tag `v*` une fois le secret `NUGET_API_KEY`
> configuré — d'ici là l'étape avertit et ne fait rien. La décision **D3** (les jumeaux de
> nommage scripting) est **tranchée** — PUB-02, 2026-08-17,
> [ADR-007](../adr/ADR-007-d3-renommage-cli-commands-scripting.md) : la bibliothèque de
> commandes a été renommée `Orkeon.Cli.Scripting` → `Orkeon.Cli.Commands.Scripting` avant
> qu'une publication NuGet ne fige l'ancien nom. L'extension au-delà du cœur n'attend plus que
> la confirmation par le mainteneur de l'intention produit ci-dessous.

## Publié en v1 (aujourd'hui)

| PackageId | Pourquoi |
|---|---|
| `Orkeon.Domain` | Entités et interfaces cœur — la racine des dépendances. |
| `Orkeon.Application` | Cas d'usage, ports, orchestration. |
| `Orkeon.Infrastructure` | Adaptateurs (LLM, mémoire, stratégies). Documenté comme installable dans le README ; c'est pourquoi `release.yml` a été corrigé pour le packager. |

## Proposé pour une version ultérieure (différé)

L'écosystème annoncé (la famille d'outils, le tool CLI `orkeon`, hosting, plugins) est censé être
installable, mais retenu jusqu'à ce que les paquets cœur soient éprouvés sur NuGet (D3 est
tranchée — ADR-007). Chaque entrée ci-dessous est `IsPackable=true` et atterrit donc déjà sur le **feed
interne GitHub Packages** via `publish.yml` (voir plus bas), mais n'est **pas** poussée vers
NuGet.org par un workflow pour l'instant.

| PackageId | Note |
|---|---|
| `Orkeon.Tools.Abstractions`, `Orkeon.Tools.Analysis`, `Orkeon.Tools.Code`, `Orkeon.Tools.Data`, `Orkeon.Tools.Embeddings.Local`, `Orkeon.Tools.EventHub`, `Orkeon.Tools.FileSystem`, `Orkeon.Tools.Rag`, `Orkeon.Tools.Web` | Famille d'outils — publier en lot une fois le cœur stabilisé. |
| `Orkeon.Rag.Abstractions`, `Orkeon.Rag`, `Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model` | Sous-système RAG (RAG-02…06, ADR-006). `Orkeon.Rag.Onnx` + `Orkeon.Rag.Onnx.Model` forment la paire cross-encoder opt-in (runtime + poids int8 embarqués) — publier les deux ensemble. |
| `Orkeon.Analysis`, `Orkeon.Analysis.Abstractions` | RaggableTree. |
| `Orkeon.Cli`, `Orkeon.Cli.Abstractions`, `Orkeon.Cli.TerminalGui` | Bibliothèques CLI. |
| `Orkeon.Cli.Commands.Scripting` | Renommé depuis `Orkeon.Cli.Scripting` (D3 tranchée — ADR-007, 2026-08-17) avant toute publication. |
| `Orkeon.Scripting`, `Orkeon.Scripting.Cli` | `Orkeon.Scripting.Cli` est le tool dotnet `orkeon` (`PackAsTool`) ; nom conservé par l'ADR-007 (le PackageId est la commande d'installation). |
| `Orkeon.Hosting` | Hôte d'empaquetage (créé par R1.5) — candidat sérieux à livrer avec le lot cœur. |
| `Orkeon.Plugins` | Système de plugins. |

## Publiés sur GitHub Packages pour `experiments/` (dotnet tools)

`publish.yml` (tag `v*`) packe `Orkeon.sln` et pousse chaque projet packable vers
**GitHub Packages** (`nuget.pkg.github.com/Orkeon`) avec `--skip-duplicate`. C'est ce feed que
`experiments/` consomme en mode packages. Les runners interactifs sont livrés en dotnet tools
pour qu'aucun launcher n'exige un clone source :

| PackageId | Commande tool | Projet source |
|---|---|---|
| `Orkeon.Runners.Shared` | — (bibliothèque) | `examples/runners/_shared` |
| `Orkeon.Runners.ClaimVerification` | `orkeon-claim-verify` | `examples/runners/interactive-claim-verification` |
| `Orkeon.Runners.InterviewSpecForge` | `orkeon-spec-forge` | `examples/runners/interactive-interview-spec-forge` |
| `Orkeon.ConsoleApp` | `orkeon-repl` | `src/apps/Orkeon.ConsoleApp` |
| `Orkeon.Scripting.Cli` | `orkeon` | `src/scripting/Orkeon.Scripting.Cli` |

## Archives d'installation (`release.yml`)

Sur un tag `v*`, `release.yml` construit tous les artefacts d'installation, **smoke-teste les
deux canaux d'onboarding sur de vrais runners**, et seulement ensuite les attache à la GitHub
Release. Le pipeline est `installers → {smoke-windows, smoke-deb, smoke-macos, msi} → release` ;
`workflow_dispatch` exécute la même chose sans la publication (pas de tag, donc pas de Release
à alimenter). Le runner `orkeon-examples`, retiré, n'est **plus packagé** — le CLI `orkeon` le
remplace (`orkeon run crew.yaml` exécute les crews YAML de `examples/` ;
`orkeon run script.ork.ts` exécute le DSL de scripting).

| Artefact | Produit par | Contenu | Runtime |
|---|---|---|---|
| `orkeon-<version>-<rid>.tar.gz` / `.zip` | `package-installers.sh` (`--app-set full` par défaut) | tous les launchers CLI + les apps Orkeon Studio admises par leur filtre RID (le WPF `orkeon-studio` est réservé à `win-x64` ; les deux TUI partout) + un esbuild partagé | mixte : `orkeon`, `orkeon-trading` et les apps Studio self-contained, les autres framework-dependent |
| `orkeon-cli-<version>-win-x64.zip` | `package-installers.sh --app-set cli --rids win-x64` | le CLI `orkeon` + `orkeon-studio` (Orkeon Studio WPF) + `install.ps1` | self-contained |
| `orkeon_<version>_amd64.deb` | `package-deb.sh` (réutilise l'arbre de staging `linux-x64` — un publish, deux paquets) | le CLI `orkeon` en `/usr/bin/orkeon` + les TUI Studio en `/usr/bin/orkeon-studio-config` et `/usr/bin/orkeon-studio-run` | self-contained ; `Depends` uniquement sur des bibliothèques système (alternations libicu / libssl), jamais sur `dotnet-runtime-*` |
| `orkeon-<version>-win-x64.msi` | `build-msi.ps1` (WiX, portée per-user), moissonnant le zip CLI extrait | le CLI `orkeon` + `orkeon-studio` (WPF, avec un raccourci menu Démarrer « Orkeon Studio »), même publish élagué que le zip | self-contained |
| `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | `package-installers.sh --app-set cli --rids osx-arm64 osx-x64` (cross-publiés depuis le runner ubuntu) | le seul CLI `orkeon` + `install.sh` (pas de Studio en V1 — le canal macOS reste CLI seul) | self-contained |
| `SHA256SUMS` | les scripts d'empaquetage du job `installers` (`package-deb.sh` rafraîchit sa propre ligne) | une ligne par artefact ci-dessus **sauf le MSI** | — |
| `SHA256SUMS.msi` | `build-msi.ps1`, dans le job `msi` | le MSI seul | — |

Deux fichiers de sommes plutôt qu'un : le job ubuntu `installers` écrit `SHA256SUMS` avant que
le MSI n'existe — il est construit plus tard, sur `windows-latest`. Chaque fichier de sommes
est produit par le job qui a produit l'artefact qu'il couvre.

**Smokes bloquants.** `smoke-windows` (runner `windows-latest`) installe
`orkeon-cli-*-win-x64.zip` et déroule la chaîne d'onboarding dessus ; `smoke-deb` (image
`ubuntu-latest` standard) installe le `.deb` via `apt`, déroule la même chaîne, puis retire le
paquet ; `smoke-macos` (runner `macos-latest`, Apple Silicon) extrait l'archive `osx-arm64`,
l'installe via `install.sh` et déroule la même chaîne avant de désinstaller ; le job `msi`
déroule sa propre chaîne `msiexec /i /qn` → `orkeon doctor --json` → `msiexec /x /qn`, en
vérifiant que le répertoire d'installation, l'entrée ARP et l'entrée de `PATH` utilisateur
apparaissent puis disparaissent. Tous quatre installent depuis les artefacts **de job**, jamais
depuis la Release : une charge utile cassée est donc attrapée avant toute publication — le job
`release` les a tous en `needs`.

`smoke-macos` est aussi le seul endroit où l'histoire Gatekeeper / signature est éprouvée : une
bibliothèque native non signée, en quarantaine ou malformée (`libtree-sitter*.dylib`,
onnxruntime, le binaire esbuild) est tuée au chargement, donc l'échec se produit là plutôt que
dans le terminal d'un utilisateur.

La version debian remplace `-` par `~` (`0.9.2-beta` → `orkeon_0.9.2~beta_amd64.deb`) pour
qu'une pré-version se classe avant sa version finale au sens de `dpkg`.

Le `ProductVersion` du MSI, lui, perd carrément le suffixe : Windows Installer ne porte que trois
champs numériques, donc `build-msi.ps1` tronque `0.9.2-beta` en `0.9.2` pour la propriété que
`<MajorUpgrade>` compare réellement. Rien n'est perdu en silence — la version complète survit
dans le nom du `.msi` (`orkeon-0.9.2-beta-win-x64.msi`) et dans la propriété `ARPCOMMENTS`
affichée dans « Applications installées ».

Le CLI `orkeon` est distribué via **sept canaux** :

| Canal | Artefact | Runtime | Public |
|---|---|---|---|
| Tool dotnet NuGet | `Orkeon.Scripting.Cli` (`PackAsTool`, commande `orkeon`) | requiert le SDK .NET 10 (`dotnet tool install`) | développeurs .NET. **Attend le go écosystème NuGet.org** (D3 tranchée — ADR-007) : le câblage en place (PUB-03) ne pousse que les trois bibliothèques cœur ; promouvoir le tool est une décision de matrice |
| Zip Windows + `install.ps1` | `orkeon-cli-<version>-win-x64.zip` | self-contained | onboarding Windows — le canal recommandé. Livre `orkeon-studio` (Orkeon Studio WPF) à côté du CLI |
| MSI Windows (per-user) | `orkeon-<version>-win-x64.msi` | self-contained | Windows, installation au double-clic et entrée « Applications installées ». Livre `orkeon-studio` avec un raccourci menu Démarrer. Un canal à la fois : le MSI refuse de s'installer par-dessus une install zip |
| Paquet Debian | `orkeon_<version>_amd64.deb` | self-contained | onboarding Debian / Ubuntu — le canal recommandé. Livre les TUI `orkeon-studio-config` / `orkeon-studio-run` à côté du CLI |
| Archive macOS + `install.sh` | `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | self-contained | onboarding macOS aujourd'hui ; `install.sh` retire l'attribut de quarantaine Gatekeeper et re-signe en ad-hoc les Mach-O que `codesign -v` rejette |
| Homebrew | les mêmes archives osx, via `installers/homebrew/orkeon.rb` | self-contained | macOS, une fois le tap créé — **pas encore publié**, voir ci-dessous |
| Archive d'installation multi-apps | launchers `orkeon` / `orkeon-slim` | `orkeon` self-contained, `orkeon-slim` framework-dependent | devs voulant aussi le REPL, les runners TUI ou la vitrine trading |

**Homebrew — formule dans le repo, tap pas encore créé.** `installers/homebrew/orkeon.rb` est
une formule binaire : elle télécharge l'archive osx correspondant à l'architecture de la
machine (`on_arm` / `on_intel`), installe la charge utile sous le `libexec` du Cellar, et écrit
un wrapper `bin/orkeon` qui pointe `ORKEON_ESBUILD_PATH` vers l'esbuild embarqué — le même
contrat que `wrapper.sh.tmpl`. Son bloc `test do` exécute `orkeon doctor` et non
`orkeon --version`, qui sort en `1`.

`version` et les deux couples `url` / `sha256` sont **générés**, jamais édités à la main :
`scripts/update-homebrew-formula.sh --release <tag>` (ou `--sums <fichier>`) réécrit les cinq
valeurs depuis le `SHA256SUMS` d'une release, de façon idempotente. Jusqu'à la première release
taguée, les deux `sha256` valent littéralement `PLACEHOLDER_SHA256_ARM64` /
`PLACEHOLDER_SHA256_X64` : une installation accidentelle échoue à la vérification plutôt que de
récupérer quoi que ce soit de non vérifié.

Publier le dépôt `Orkeon/homebrew-tap` et y pousser la formule est une **action de release, pas
de CI** (MAC-00 §8) : `brew tap orkeon/tap && brew install orkeon` ne résout pas avant cela. La
soumission à homebrew-core et un installeur `.pkg` restent hors périmètre tant que le projet
n'est pas signé.

Toutes les variantes sont construites depuis le même csproj
`src/scripting/Orkeon.Scripting.Cli` et partagent l'unique esbuild embarqué.
`orkeon-trading` est également self-contained ; les autres launchers CLI restent
framework-dependent — `install.sh` et `install.ps1` détectent ce cas et affichent les
commandes d'installation du runtime plutôt que d'échouer au premier lancement.

> **Changement de comportement du publish.** Chaque `publish` de `src/` et `examples/` élague
> désormais les grammaires tree-sitter inutilisées (31 → 7 bibliothèques natives), via
> `Directory.Build.targets`. Opt-out : `-p:OrkeonPruneTreeSitterGrammars=false`. Tous les
> artefacts du tableau ci-dessus portent la charge utile élaguée, MSI compris — ce qui
> stabilise au passage les GUID de composants WiX d'un upgrade à l'autre.

## Build-time / interne (pas des paquets autonomes)

| PackageId | Note |
|---|---|
| `Orkeon.Generators` | Source generator — consommé au build. |
| `Orkeon.Compliance.Vfs` | Analyseur Roslyn — consommé au build. |

## Câblage de la publication

- Tout le packaging et le push NuGet vivent dans **`publish.yml`** (tag `v*`) :
  `dotnet pack Orkeon.sln` (+ les tools runners) piloté par `IsPackable`, poussé vers
  **GitHub Packages** avec `--skip-duplicate` (ré-exécutions idempotentes), puis les **trois
  paquets cœur** (le tableau « Publié en v1 » ci-dessus) vers **NuGet.org**. `ci.yml` valide
  (build + tests) et ne package rien ; `release.yml` construit les archives d'installation et
  l'image conteneur, sans packaging NuGet.
- L'étape NuGet.org est **conditionnée au secret de dépôt `NUGET_API_KEY`** (action
  propriétaire : créer une clé API nuget.org limitée à `Orkeon.*` et réserver ce préfixe
  d'ID). Tant que le secret n'existe pas, l'étape émet un avertissement et ne fait rien —
  **rien n'a encore atterri sur NuGet.org** ; le premier push réel partira au prochain tag
  `v*` après configuration de la clé. Étendre le périmètre NuGet.org au-delà des trois
  paquets cœur reste conditionné à la confirmation par le mainteneur de la matrice ci-dessus
  (D3 tranchée — ADR-007), et passe d'abord par une édition de la matrice, jamais par une
  retouche de workflow en passant.
- `publish.yml` **refuse un tag qui ne correspond pas à la version de
  `src/Directory.Build.props`**. Leçon de l'incident 0.9.1-beta (voir CHANGELOG 0.9.2-beta) :
  les tags `v0.9.1-beta.rc*` ont re-packé la version inchangée des props et
  `--skip-duplicate` a sauté chaque push en silence — une « release » qui n'a rien publié.
  Le garde maintient `--skip-duplicate` honnête.
- La version provient de `src/Directory.Build.props` (actuellement `0.9.2-beta`) ; aucun projet ne la surcharge.
