> 🇬🇧 [English version](../../reference/publication-matrix.md)

# Matrice de publication NuGet

Ce fichier est la source de vérité unique pour **les projets publiés sur NuGet**, afin que les
workflows (`ci.yml` validation, `publish.yml` pack + push sur tag, `release.yml` installateurs)
ne divergent plus jamais (OSS-011 / R8.3).

> **Statut — lineup consolidé implémenté (PUB-25, 2026-09-01).** La distribution est un
> paquet `Orkeon` plus une poignée d'opt-ins, câblés dans `publish.yml` et gardés par le
> gate `scripts/check-package-closure.py` ; le premier push de ce lineup part au tag
> `v1.0.0-rc.3`. Le Trusted Publishing vers NuGet.org est **opérationnel** — les paquets
> par couche désormais abandonnés `Orkeon.Domain` / `Orkeon.Application` /
> `Orkeon.Infrastructure` y ont publié `1.0.0-rc.1` (2026-08-18) et `1.0.0-rc.2`
> (2026-08-25), et seront délistés une fois le nouveau lineup publié (voir les actions
> propriétaire plus bas). La décision **D3** (les jumeaux de nommage scripting) est
> **tranchée** — PUB-02, 2026-08-17,
> [ADR-007](../adr/ADR-007-d3-renommage-cli-commands-scripting.md) : la bibliothèque de
> commandes a été renommée `Orkeon.Cli.Scripting` → `Orkeon.Cli.Commands.Scripting` avant
> qu'une publication NuGet ne fige l'ancien nom.

## Le lineup NuGet.org

Un paquet installe le framework entier ; tout le reste du lineup est un opt-in tenu à part
uniquement pour ce qu'il imposerait à chaque consommateur (poids des dépendances, natifs,
un amont en pré-release).

| PackageId | Pourquoi |
|---|---|
| `Orkeon` | Le paquet ombrelle — les onze assemblies de la fermeture du cœur (`Orkeon.Domain`, `Orkeon.Application`, `Orkeon.Infrastructure`, `Orkeon.Constants.{Llm,FileSystem,Configuration}`, `Orkeon.Tools.Abstractions`, `Orkeon.Analysis.Abstractions`, `Orkeon.Rag.Abstractions`, `Orkeon.Analysis`, `Orkeon.Rag`) embarquées dans un seul nupkg. Une installation = le framework complet : agents, crews, six modes d'orchestration, 14 fournisseurs LLM, 6 stores mémoire, RAG, RaggableTree. Le découpage Clean Architecture reste une discipline d'arborescence source, pas un contrat de distribution. |
| `Orkeon.Tools` | Les sept familles d'outils intégrés (`Analysis`, `Code`, `Data`, `EventHub`, `FileSystem`, `Rag`, `Web`) dans un seul nupkg. Séparé d'`Orkeon` **uniquement pour le poids des dépendances** : les outils Data tirent des drivers de bases de données, des bibliothèques PDF et tableur qu'un consommateur qui ne s'en sert jamais ne devrait pas hériter. Dépend d'`Orkeon`. |
| `Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model` | Paire opt-in du reranker cross-encoder ONNX (runtime + poids int8 embarqués) — poussés ensemble ; charge native onnxruntime. `Orkeon.Rag.Onnx` dépend d'`Orkeon` ; `Orkeon.Rag.Onnx.Model` n'a aucune dépendance (ressources embarquées seulement) et se référence à côté de lui. |
| `Orkeon.Tools.Embeddings.Local` | Embeddings locaux sur la machine (BGE-micro-v2 ONNX). Reste **hors de l'ombrelle** parce qu'il porte une dépendance SmartComponents en pré-release, d'un amont archivé — l'inclure dans `Orkeon` imposerait cette pré-release à chaque consommateur. Dépend d'`Orkeon`. |
| `Orkeon.Scripting.Cli` | Le tool dotnet `orkeon` (`PackAsTool` ; le PackageId est la commande d'installation — ADR-007). Publiable sur NuGet.org depuis l'exclusion des natifs onnxruntime iOS/Android qu'un tool CLI ne peut jamais charger : 262,5 Mo → 137,6 Mo, sous la limite de taille de nuget.org. |

### Comment les projets d'empaquetage sont construits

- Les nupkgs du lineup sortent de **projets d'empaquetage** dédiés sous `src/packaging/` —
  quatre au total : les ombrelles `Orkeon` et `Orkeon.Tools`, plus les **wrappers**
  `Orkeon.Rag.Onnx.Package` et `Orkeon.Tools.Embeddings.Local.Package`, qui packent les deux
  assemblies opt-in avec une dépendance nuspec sur l'ombrelle `Orkeon`. Les projets de
  bibliothèques embarqués sont eux-mêmes `IsPackable=false` et leur arborescence source,
  leurs namespaces et leur gel PublicAPI par assembly sont intouchés ; les projets opt-in
  réels gardent des `ProjectReference` normales, les consommateurs in-repo ne voient donc
  aucune différence — seuls les wrappers portent le motif d'embarquement ci-dessous.
- Chaque projet d'empaquetage référence son ou ses projets embarqués avec
  `PrivateAssets="all"` (ce qui les tient hors de la liste de dépendances du nuspec) et packe
  leurs DLL+XML dans `lib/`. Comme `PrivateAssets="all"` empêche aussi les `PackageReference`
  externes des projets embarqués de remonter dans le nuspec, l'**union de ces références est
  re-déclarée à la main** dans le csproj d'empaquetage.
- `scripts/check-package-closure.py` (lancé par `publish.yml` juste après le pack) fait
  échouer le workflow si (1) un paquet du lineup déclare une dépendance `Orkeon.*` hors du
  lineup — la classe d'incident NU1101 — ou (2) les externes re-déclarés d'une ombrelle
  dérivent de ce que ses projets embarqués exigent réellement.
- `publish.yml` pousse **`Orkeon` en premier** : les autres paquets du lineup en dépendent et
  NuGet n'ordonne pas les pushes ; pousser un dépendant d'abord exposerait un paquet dont la
  restauration échoue.

## Paquets abandonnés

Les PackageIds suivants ne sont **plus packés** (`IsPackable=false`) : `Orkeon.Domain`,
`Orkeon.Application`, `Orkeon.Infrastructure`, les cinq satellites `Orkeon.Constants.*`, les
sept paquets `Orkeon.Tools.<famille>`, `Orkeon.Cli`, `Orkeon.Cli.Abstractions`,
`Orkeon.Cli.Commands.Scripting`, `Orkeon.Cli.TerminalGui`, `Orkeon.Scripting`,
`Orkeon.Hosting`, `Orkeon.Plugins`.

**Migration** : le code source et les namespaces sont inchangés, le code consommateur compile
donc tel quel — seule l'installation change. Désinstallez les paquets par couche et
`dotnet add package Orkeon --prerelease` (ajoutez `Orkeon.Tools` si vous utilisiez un paquet
`Orkeon.Tools.<famille>` autre qu'`Embeddings.Local`).

## État réel de NuGet.org, et actions propriétaire restantes

`orkeon.domain`, `orkeon.application` et `orkeon.infrastructure` **ont été publiés** sur
NuGet.org : `1.0.0-rc.1` (2026-08-18) et `1.0.0-rc.2` (2026-08-25), poussés par `publish.yml`
via le Trusted Publishing. Deux des trois n'y étaient pas restaurables (`NU1101`) : ils
déclaraient cinq dépendances `Orkeon.*` jamais publiées — l'incident qui a motivé le gate de
fermeture ci-dessus. Les six versions ont été **délistées le 2026-09-07** — délistées, pas
supprimées : les octets restent servis, donc une restauration qui les épingle fonctionne
toujours.

Actions propriétaire déjà faites :

- ✅ **2026-09-07 — les six versions `rc.1` / `rc.2` du trio par couche sont délistées**
  (`listed: false` sur les trois paquets). Plus personne ne se voit proposer un paquet qui
  ne restaure pas.
- ✅ **2026-09-08 — le préfixe `Orkeon` est réservé** sur nuget.org pour le compte
  propriétaire `arion-orkeon` : l'ID nu `Orkeon` *et* la famille `Orkeon.*`. Tout ID
  correspondant poussé par un autre compte est désormais rejeté — les ~30 noms d'assemblys
  `Orkeon.*` que cette documentation rend publics ne peuvent plus être squattés.
- ✅ La variable de dépôt `NUGET_USER` et la politique de Trusted Publishing sont
  opérationnelles (les pushes rc.1/rc.2 le prouvent).

Action propriétaire restante — **une seule** : re-taguer `v1.0.0-rc.3` pour que le lineup
ci-dessus soit réellement poussé. L'exécution du 2026-09-09 sur ce tag est allée jusqu'au
bout — build, tests, pack, gate de closure, attestation de provenance, GitHub Packages,
échange de clé OIDC — et est morte sur la dernière étape, sans rien pousser vers NuGet.org :
le glob du lineup `artifacts/${id}.[0-9]*.nupkg` était entre guillemets, il est donc arrivé
littéral à `dotnet nuget push`. Cette CLI résout ses jokers via le `PathResolver` de NuGet,
qui connaît `*` et `?` et aucune classe de caractères ; elle a répondu `File does not exist`
à propos d'un fichier bel et bien présent dans `artifacts/`. Le push GitHub Packages du même
job ne l'a jamais rencontré — `artifacts/*.nupkg` reste dans ce dialecte. Corrigé en laissant
le shell expanser le motif ; le prochain tag poussera pour de bon.

Jusqu'à ce push, rien n'est listé sous le nom `Orkeon` sur nuget.org : la famille n'a pas de
page et une recherche libre sur « Orkeon » ne rend rien. La réservation, elle, n'en dépend
pas : elle s'appuie sur la *propriété* des paquets, pas sur leur listage.

## Publiés sur GitHub Packages

`publish.yml` (tag `v*`) packe `Orkeon.sln` et pousse **chaque projet packable** vers
**GitHub Packages** (`nuget.pkg.github.com/Orkeon`) avec `--skip-duplicate`. Soit le lineup
NuGet.org ci-dessus **plus** les paquets build-time et runners qui restent hors de NuGet.org :
`Orkeon.ConsoleApp`, `Orkeon.Generators` et `Orkeon.Compliance.Vfs`. C'est ce feed
:

| PackageId | Commande tool | Projet source |
|---|---|---|
| `Orkeon.ConsoleApp` | `orkeon-repl` | `src/apps/Orkeon.ConsoleApp` |
| `Orkeon.Scripting.Cli` | `orkeon` | `src/scripting/Orkeon.Scripting.Cli` |

## Archives d'installation (`release.yml`)

Sur un tag `v*`, `release.yml` construit tous les artefacts d'installation, **smoke-teste chaque
canal d'onboarding sur un vrai runner** (zip CLI Windows, service Windows, paquet Debian,
tarball macOS), et seulement ensuite les attache à la GitHub Release. Le pipeline est
`installers → {smoke-windows, smoke-windows-service, smoke-deb, smoke-macos, msi} →
release` ; derrière ces mêmes cinq jobs, `runners-image` pousse l'image conteneur
`orkeon-runners` sur GHCR en parallèle de `release`, de sorte qu'un smoke rouge ne déplace
ni la Release ni le tag `:latest`. `workflow_dispatch` exécute la même chose sans la
publication (pas de tag, donc pas de Release à alimenter). Le runner `orkeon-examples`, retiré, n'est **plus packagé** — le CLI `orkeon` le
remplace (`orkeon run crew.yaml` exécute les crews YAML de `examples/` ;
`orkeon run script.ork.ts` exécute le DSL de scripting).

| Artefact | Produit par | Contenu | Runtime |
|---|---|---|---|
| `orkeon-<version>-<rid>.tar.gz` / `.zip` | `package-installers.sh` (`--app-set full` par défaut) | tous les launchers CLI + les apps Orkeon Studio admises par leur filtre RID (le WPF `orkeon-studio` est réservé à `win-x64` ; les deux TUI partout) + un esbuild partagé + l'arbre `deploy/` (unité systemd, script d'enregistrement SCM, Dockerfile.host) | mixte : `orkeon`, `orkeon-host` et les apps Studio self-contained, les autres framework-dependent |
| `orkeon-cli-<version>-win-x64.zip` | `package-installers.sh --app-set cli --rids win-x64` | le CLI `orkeon` + `orkeon-studio` (Orkeon Studio WPF) + `install.ps1` | self-contained |
| `orkeon_<version>_amd64.deb` | `package-deb.sh` (réutilise l'arbre de staging `linux-x64` — un publish, deux paquets) | le CLI `orkeon` en `/usr/bin/orkeon` + les TUI Studio en `/usr/bin/orkeon-studio-config` et `/usr/bin/orkeon-studio-run` | self-contained ; `Depends` uniquement sur des bibliothèques système (alternations libicu / libssl), jamais sur `dotnet-runtime-*` |
| `orkeon-<version>-win-x64.msi` | `build-msi.ps1` (WiX, portée per-user), moissonnant le zip CLI extrait | le CLI `orkeon` + `orkeon-studio` (WPF, avec un raccourci menu Démarrer « Orkeon Studio »), même publish élagué que le zip | self-contained |
| `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | `package-installers.sh --app-set cli --rids osx-arm64 osx-x64` (cross-publiés depuis le runner ubuntu) | le seul CLI `orkeon` + `install.sh` (pas de Studio en V1 — le canal macOS reste CLI seul) | self-contained |
| `SHA256SUMS` | les scripts d'empaquetage du job `installers` (`package-deb.sh` rafraîchit sa propre ligne) | une ligne par artefact ci-dessus **sauf le MSI** | — |
| `orkeon-host-<version>-win-x64.msi` | `build-msi-service.ps1` (WiX, portée per-machine), moissonnant le zip complet extrait | le seul publish self-contained `orkeon-host`, enregistré comme service `Orkeon` sous `NT SERVICE\Orkeon` (pas de CLI, pas de wrapper, pas de `deploy/` — le paquet enregistre déclarativement) | self-contained |
| `SHA256SUMS.msi` | `build-msi.ps1` puis `build-msi-service.ps1`, dans l'ordre, dans le job `msi` | les deux MSI | — |

### Le host de service

`orkeon-host` est livré dans l'archive complète (`--app-set full`), self-contained : un daemon supervisé par systemd ou le SCM Windows ne doit pas dépendre d'un runtime que quelqu'un peut mettre à jour sous ses pieds. Ce n'est **pas** un dotnet tool — il s'installe en service, il ne s'invoque pas depuis un shell. Sous Windows il est aussi livré comme **MSI per-machine** dédié (`orkeon-host-<version>-win-x64.msi`), produit distinct du MSI per-user du CLI : les deux coexistent, et le paquet enregistre le service déclarativement — même compte, mêmes chemins, même politique de redémarrage que le canal script.

Ses artefacts de déploiement vivent dans [`deploy/`](https://github.com/orkeon/orkeon/tree/main/deploy) et sont livrés dans l'archive complète aux côtés du daemon : une unité systemd (`Type=notify`, redémarrage sur échec — les erreurs de configuration sortent en 78 et ne bouclent pas —, durcie), un script PowerShell qui l'enregistre auprès du SCM, et un Dockerfile. Aucun des trois ne porte de secret — le jeton du bot et les clés d'API sont nommés par variable d'environnement dans la configuration et fournis par la machine, donc une unité ou une couche d'image peut être lue par n'importe qui sans rien divulguer.

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
apparaissent puis disparaissent. `smoke-windows-service` installe le canal service du zip
win-x64 **complet** — compte virtuel, `--working-dir` prouvé par un chemin de crew relatif,
configuration refusée qui s'arrête sans boucler et atterrit dans le journal d'événements — et
le job `msi` smoke le MSI service de la même façon, plus une réinstallation silencieuse de
lui-même. Tous installent depuis les artefacts **de job**, jamais depuis la Release : une
charge utile cassée est donc attrapée avant toute publication — le job `release` les a tous en
`needs`.

`smoke-macos` est aussi le seul endroit où l'histoire Gatekeeper / signature est éprouvée : une
bibliothèque native non signée, en quarantaine ou malformée (`libtree-sitter*.dylib`,
onnxruntime, le binaire esbuild) est tuée au chargement, donc l'échec se produit là plutôt que
dans le terminal d'un utilisateur.

La version debian remplace `-` par `~` (`1.0.0-rc.1` → `orkeon_1.0.0~rc.1_amd64.deb`) pour
qu'une pré-version se classe avant sa version finale au sens de `dpkg`.

Le `ProductVersion` du MSI, lui, perd carrément le suffixe : Windows Installer ne porte que trois
champs numériques, donc `build-msi.ps1` tronque `1.0.0-rc.1` en `1.0.0` pour la propriété que
`<MajorUpgrade>` compare réellement. Rien n'est perdu en silence — la version complète survit
dans le nom du `.msi` (`orkeon-1.0.0-rc.1-win-x64.msi`) et dans la propriété `ARPCOMMENTS`
affichée dans « Applications installées ».

Le CLI `orkeon` est distribué via **sept canaux** :

| Canal | Artefact | Runtime | Public |
|---|---|---|---|
| Tool dotnet NuGet | `Orkeon.Scripting.Cli` (`PackAsTool`, commande `orkeon`) | requiert le SDK .NET 10 (`dotnet tool install`) | développeurs .NET. Fait partie du lineup NuGet.org (PUB-25) — publiable depuis que le paquet est passé de 262,5 Mo à 137,6 Mo (natifs onnxruntime iOS/Android exclus) ; premier push au tag `v1.0.0-rc.3` |
| Zip Windows + `install.ps1` | `orkeon-cli-<version>-win-x64.zip` | self-contained | onboarding Windows — le canal recommandé. Livre `orkeon-studio` (Orkeon Studio WPF) à côté du CLI |
| MSI Windows (per-user) | `orkeon-<version>-win-x64.msi` | self-contained | Windows, installation au double-clic et entrée « Applications installées ». Livre `orkeon-studio` avec un raccourci menu Démarrer. Un canal à la fois : le MSI refuse de s'installer par-dessus une install zip |
| Paquet Debian | `orkeon_<version>_amd64.deb` | self-contained | onboarding Debian / Ubuntu — le canal recommandé. Livre les TUI `orkeon-studio-config` / `orkeon-studio-run` à côté du CLI |
| Archive macOS + `install.sh` | `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | self-contained | onboarding macOS aujourd'hui ; `install.sh` retire l'attribut de quarantaine Gatekeeper et re-signe en ad-hoc les Mach-O que `codesign -v` rejette |
| Homebrew | les mêmes archives osx, via `installers/homebrew/orkeon.rb` | self-contained | macOS, une fois le tap créé — **pas encore publié**, voir ci-dessous |
| Archive d'installation multi-apps | launchers `orkeon` / `orkeon-slim` | `orkeon` self-contained, `orkeon-slim` framework-dependent | devs voulant aussi le REPL, l'hôte de service ou les applications Studio |

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
Les autres launchers CLI restent
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
| `Orkeon.Generators` | Source generator — consommé au build. GitHub Packages uniquement. |
| `Orkeon.Compliance.Vfs` | Analyseur Roslyn — consommé au build. GitHub Packages uniquement. |
| `Orkeon.Host` | `IsPackable=false` — livré uniquement comme binaire `orkeon-host` dans les archives de release. |
| `Orkeon.Studio.{Core,Config,Run,Wpf}` | `IsPackable=false` — livrés uniquement via les installeurs de release (voir [Orkeon Studio](../architecture/studio.md)). |

## Câblage de la publication

- Tout le packaging et le push NuGet vivent dans **`publish.yml`** (tag `v*`) :
  `dotnet pack Orkeon.sln` piloté par `IsPackable`, le gate
  `scripts/check-package-closure.py` sur les artefacts packés, un push de tout vers
  **GitHub Packages** avec `--skip-duplicate` (ré-exécutions idempotentes), puis le **lineup
  NuGet.org** (le tableau ci-dessus, `Orkeon` en premier) vers **NuGet.org**. `ci.yml` valide
  (build + tests) et ne package rien ; `release.yml` construit les archives d'installation et
  l'image conteneur, sans packaging NuGet.
- L'authentification NuGet.org est le **Trusted Publishing (OIDC)** — aucune clé API longue
  durée. Une politique nuget.org (dépôt `Orkeon/orkeon`, workflow `publish.yml`) permet à
  `NuGet/login` d'échanger le jeton OIDC du job contre une clé éphémère ; les étapes sont
  conditionnées à la **variable de dépôt `NUGET_USER`** (le profil nuget.org propriétaire de
  la politique). Les deux sont **en place et éprouvées** — les pushes rc.1/rc.2 sont passés
  par ce chemin ; si la variable venait à disparaître, les étapes émettraient un
  avertissement et ne feraient rien plutôt que de faire échouer le tag.
  Étendre le lineup NuGet.org est une décision de mainteneur consignée d'abord dans cette
  matrice (et reflétée dans la liste du lineup du workflow + les arguments du gate de
  fermeture), jamais une retouche de workflow en passant.
- `publish.yml` **refuse un tag qui ne correspond pas à la version de
  `src/Directory.Build.props`**. Leçon de l'incident 0.9.1-beta (voir CHANGELOG 0.9.2-beta) :
  les tags `v0.9.1-beta.rc*` ont re-packé la version inchangée des props et
  `--skip-duplicate` a sauté chaque push en silence — une « release » qui n'a rien publié.
  Le garde maintient `--skip-duplicate` honnête.
- La version provient de `src/Directory.Build.props` (actuellement `1.0.0-rc.3`), la source de vérité unique : aucun projet ne la surcharge, et le garde-fou de tag du workflow de publication refuse tout tag `v*` qui la contredit.
