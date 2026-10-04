> 🇬🇧 [English version](../../reference/publication-matrix.md)

# Matrice de publication NuGet

Ce fichier est la source de vérité unique pour **les projets publiés sur NuGet**, afin que les
workflows (`ci.yml` validation, `publish.yml` pack + push sur tag et canal dev sur `main` au
vert, `release.yml` installateurs)
ne divergent plus jamais (OSS-011 / R8.3).

> **Statut — lineup consolidé implémenté (PUB-25, 2026-09-01).** La distribution est un
> paquet `Orkeon` plus une poignée d'opt-ins, câblés dans `publish.yml` et gardés par le
> gate `scripts/check-package-closure.py`. Ses six premiers paquets sont partis avec
> `1.0.0-rc.3` (2026-09-09) ; `Orkeon.Compliance.Vfs`, `Orkeon.Interop.AgentFramework` et
> `Orkeon.Hosting.Aspire` ont rejoint le lineup le 2026-09-11, leur premier push NuGet.org
> est donc le premier tag posé après cette date — neuf paquets en tout. Le Trusted
> Publishing vers NuGet.org est **opérationnel** — les paquets par couche désormais
> abandonnés `Orkeon.Domain` / `Orkeon.Application` / `Orkeon.Infrastructure` y ont publié
> `1.0.0-rc.1` (2026-08-18) et `1.0.0-rc.2` (2026-08-25), et ont été délistés le 2026-09-07
> (voir les actions propriétaire plus bas). La décision **D3** (les jumeaux de nommage scripting) est
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
| `Orkeon` | Le paquet ombrelle — les douze assemblies de la fermeture du cœur (`Orkeon.Domain`, `Orkeon.Application`, `Orkeon.Infrastructure`, `Orkeon.Constants.{Llm,FileSystem,Configuration,Protocol}`, `Orkeon.Tools.Abstractions`, `Orkeon.Analysis.Abstractions`, `Orkeon.Rag.Abstractions`, `Orkeon.Analysis`, `Orkeon.Rag`) embarquées dans un seul nupkg. Une installation = le framework complet : agents, crews, six modes d'orchestration, 16 fournisseurs LLM, 6 stores mémoire, RAG, RaggableTree. Le découpage Clean Architecture reste une discipline d'arborescence source, pas un contrat de distribution. |
| `Orkeon.Tools` | Les huit familles d'outils intégrés (`Analysis`, `Code`, `Data`, `Email`, `EventHub`, `FileSystem`, `Rag`, `Web`) dans un seul nupkg. Séparé d'`Orkeon` **uniquement pour le poids des dépendances** : les outils Data tirent des drivers de bases de données, des bibliothèques PDF et tableur, les outils e-mail MailKit et MimeKit — des dépendances dont un consommateur qui ne s'en sert jamais ne devrait pas hériter. Dépend d'`Orkeon`. |
| `Orkeon.Rag.Onnx`, `Orkeon.Rag.Onnx.Model` | Paire opt-in du reranker cross-encoder ONNX (runtime + poids int8 embarqués) — poussés ensemble ; charge native onnxruntime. `Orkeon.Rag.Onnx` dépend d'`Orkeon` ; `Orkeon.Rag.Onnx.Model` n'a aucune dépendance (ressources embarquées seulement) et se référence à côté de lui. |
| `Orkeon.Tools.Embeddings.Local` | Embeddings locaux sur la machine (BGE-micro-v2 ONNX). Reste **hors de l'ombrelle** parce qu'il porte une dépendance SmartComponents en pré-release, d'un amont archivé — l'inclure dans `Orkeon` imposerait cette pré-release à chaque consommateur. Dépend d'`Orkeon`. |
| `Orkeon.Scripting.Cli` | Le tool dotnet `orkeon` (`PackAsTool` ; le PackageId est la commande d'installation — ADR-007). Publiable sur NuGet.org depuis l'exclusion des natifs onnxruntime iOS/Android qu'un tool CLI ne peut jamais charger : 262,5 Mo → 137,6 Mo, sous la limite de taille de nuget.org. |
| `Orkeon.Compliance.Vfs` | L'analyseur Roslyn qui refuse `System.IO` direct dans votre propre code (règles `ORKVFS001`–`ORKVFS007`, `analyzers/dotnet/cs`, `DevelopmentDependency`). Autonome par conception : il ne dépend de rien d'Orkeon et fonctionne dans n'importe quel projet C# — ajoutez le `PackageReference`, compilez, et chaque appel `File.*`/`Directory.*` devient un diagnostic. Dans le lineup NuGet.org depuis le 2026-09-11, donc son premier push y est le premier tag posé après cette date : `1.0.0-rc.3` n'a atteint que GitHub Packages (la boucle de push NuGet.org était une liste figée de six identifiants) et était de toute façon inerte, compilé contre un Roslyn plus récent que le compilateur du SDK. |
| `Orkeon.Interop.AgentFramework` | Le pont vers Microsoft Agent Framework, dans les deux sens : un crew Orkeon comme `AIAgent` MAF (`CrewAgent`), un `AIAgent` MAF comme cerveau (`WithAgentFrameworkAgent`) ou comme outil (`WithAgentFrameworkTool`) d'un agent Orkeon. Séparé de `Orkeon` parce que la dépendance `Microsoft.Agents.AI.Abstractions` est un choix du consommateur. Dépend de `Orkeon`. |
| `Orkeon.Hosting.Aspire` | L'intégration d'hébergement .NET Aspire : `AddOrkeonHost` (le démon `orkeon-host`) et `AddOrkeonCrewRun` (un `orkeon run`) comme ressources d'AppHost, `WithOrkeonModel` / `WithOrkeonSetting` pour l'environnement `ORKEON_`, export OTLP câblé pour que le dashboard lise le run. Séparé de `Orkeon` parce que `Aspire.Hosting` est un choix de l'AppHost. Dépend de `Orkeon`. |

### Comment les projets d'empaquetage sont construits

- Les nupkgs du lineup sortent de **projets d'empaquetage** dédiés sous `src/packaging/` —
  six au total : les ombrelles `Orkeon` et `Orkeon.Tools`, plus les **wrappers**
  `Orkeon.Rag.Onnx.Package`, `Orkeon.Tools.Embeddings.Local.Package`,
  `Orkeon.Interop.AgentFramework.Package` et `Orkeon.Hosting.Aspire.Package`, qui packent les
  quatre assemblies opt-in avec une dépendance nuspec sur l'ombrelle `Orkeon`. Les autres
  paquets du lineup sont packés directement depuis leur propre projet :
  `Orkeon.Rag.Onnx.Model` (`src/rag/`), le tool `orkeon`
  (`src/scripting/Orkeon.Scripting.Cli`) et l'analyseur
  (`src/analyzers/Orkeon.Compliance.Vfs`). Les projets de bibliothèques embarqués sont eux-mêmes `IsPackable=false` et leur arborescence source,
  leurs namespaces et leur gel PublicAPI par assembly sont intouchés ; les projets opt-in
  réels gardent des `ProjectReference` normales, les consommateurs in-repo ne voient donc
  aucune différence — seuls les wrappers portent le motif d'embarquement ci-dessous.
- Chaque projet d'empaquetage référence son ou ses projets embarqués avec
  `PrivateAssets="all"` (ce qui les tient hors de la liste de dépendances du nuspec) et packe
  leurs DLL+XML dans `lib/`. Comme `PrivateAssets="all"` empêche aussi les `PackageReference`
  externes des projets embarqués de remonter dans le nuspec, l'**union de ces références est
  re-déclarée à la main** dans le csproj d'empaquetage.
- `scripts/check-package-closure.py` (lancé par `publish.yml` juste après le pack, sur le
  tag comme sur le canal dev) fait échouer le workflow si (1) un paquet du lineup déclare une
  dépendance `Orkeon.*` hors du lineup — la classe d'incident NU1101 —, (2) les externes
  re-déclarés d'une ombrelle dérivent de ce que ses projets embarqués exigent réellement, ou
  (3) une assembly atteinte transitivement par une assembly embarquée n'est livrée nulle part
  dans le lineup. Sa moitié « source » (sans nupkg) tourne aussi sur chaque pull request via
  `scripts/check-doc-claims.py`, qui échoue en plus quand un projet packable n'est ni dans le
  lineup ni réservé à GitHub Packages, ou quand l'une des copies manuscrites du lineup
  diverge.
- `publish.yml` pousse **`Orkeon` en premier** : les autres paquets du lineup en dépendent et
  NuGet n'ordonne pas les pushes ; pousser un dépendant d'abord exposerait un paquet dont la
  restauration échoue.

## Paquets abandonnés

Les PackageIds suivants ne sont **plus packés** (`IsPackable=false`) : `Orkeon.Domain`,
`Orkeon.Application`, `Orkeon.Infrastructure`, les cinq satellites `Orkeon.Constants.*`,
`Orkeon.Analysis`, `Orkeon.Analysis.Abstractions`, `Orkeon.Rag`, `Orkeon.Rag.Abstractions`,
`Orkeon.Tools.Abstractions` et les huit projets `Orkeon.Tools.<famille>`, `Orkeon.Cli`,
`Orkeon.Cli.Abstractions`, `Orkeon.Cli.Commands.Scripting`, `Orkeon.Cli.TerminalGui`,
`Orkeon.Scripting`, `Orkeon.Hosting`, `Orkeon.Plugins`. Ceux qu'embarquent les ombrelles
`Orkeon` et `Orkeon.Tools` sont toujours livrés — dans le nupkg de l'ombrelle, pas sous leur
propre identifiant.

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

**2026-09-09 — la gamme v1 est publiée.** Les six paquets en `1.0.0-rc.3` ont été poussés
par `publish.yml` via Trusted Publishing (propriétaire `arion-orkeon`, le compte qui détient
la réservation du préfixe `Orkeon`). La famille a enfin une page sur nuget.org.

Il aura fallu deux tentatives, et la première mérite d'être conservée : cette exécution est
allée jusqu'au bout — build, tests, pack, gate de closure, attestation de provenance,
GitHub Packages, échange de clé OIDC — et est morte sur la dernière étape, sans rien
pousser. Le glob du lineup `artifacts/${id}.[0-9]*.nupkg` était entre guillemets, il est
donc arrivé littéral à `dotnet nuget push` ; or cette CLI résout ses jokers via le
`PathResolver` de NuGet, qui connaît `*` et `?` et aucune classe de caractères. Elle a
répondu `File does not exist` à propos d'un fichier bel et bien présent dans `artifacts/`.
Le push GitHub Packages du même job ne l'a jamais rencontré — `artifacts/*.nupkg` reste
dans ce dialecte. Corrigé en laissant le shell expanser le motif.

Un paquet fraîchement poussé n'est **pas immédiatement téléchargeable** : NuGet.org le
valide d'abord, et tant que ce n'est pas fini sa page répond 200 avec « not been indexed »
pendant que `v3-flatcontainer` renvoie encore 404. `Orkeon.Rag.Onnx.Model` y est resté le
plus longtemps, ce qui est attendu — c'est le paquet qui embarque les poids du modèle en
int8. Rien à faire sinon attendre : ce n'est pas un push raté, et `--skip-duplicate` rend
de toute façon un nouveau push sans effet.

## Publiés sur GitHub Packages

`publish.yml` (tag `v*`) packe `Orkeon.sln` et pousse **chaque projet packable** vers
**GitHub Packages** (`nuget.pkg.github.com/Orkeon`) avec `--skip-duplicate`. Soit le lineup
NuGet.org ci-dessus **plus** les paquets build-time et runners qui restent hors de NuGet.org :
`Orkeon.ConsoleApp` et `Orkeon.Generators` :

| PackageId | Commande tool | Projet source |
|---|---|---|
| `Orkeon.ConsoleApp` | `orkeon-repl` | `src/apps/Orkeon.ConsoleApp` |
| `Orkeon.Scripting.Cli` | `orkeon` | `src/scripting/Orkeon.Scripting.Cli` |

### Canal dev : le dernier `main`, entre deux tags

Dès que `ci.yml` passe au vert sur un push vers `main`, le job `publish-dev` de `publish.yml`
packe les mêmes projets en `<version des props>.dev.<n>`, `n` étant le numéro de ce run de CI
— `1.0.0-rc.4.dev.412` est donc le commit validé par le run de CI n° 412 — et les pousse
**uniquement sur GitHub Packages** : jamais sur NuGet.org, qui sait masquer une version mais
jamais la supprimer. `scripts/prune-dev-packages.sh` supprime ensuite les builds dev plus
anciens : le feed contient **chaque version taggée plus le dernier `main` au vert**, rien
entre les deux. Une version taggée n'est jamais supprimée ; une version ni taggée ni build dev
est signalée, pas supprimée — le même script les retire lors d'un passage ponctuel et relu
(`--include-untagged`, à blanc d'abord).

- En SemVer, `1.0.0-rc.4.dev.<n>` est au-dessus de `1.0.0-rc.4` et sous la rc suivante :
  `--prerelease` résout donc le build dev. Quand la version des props n'a pas de suffixe (une
  release stable), les builds dev passent au patch suivant — `1.0.1-dev.<n>` après `1.0.0` —
  car `1.0.0-dev.<n>` tomberait sous la release.
- Une version dev vit jusqu'au merge vert suivant. Un `PackageReference` qui en épingle une
  se restaure encore — NuGet lit la version comme un minimum et prend le build suivant, avec
  l'avertissement NU1603 (une erreur si les avertissements sont traités en erreurs) — mais les
  épinglages exacts cassent : un manifeste `dotnet tool`, un `packages.lock.json` en mode
  verrouillé. Utilisez une version flottante (`*-*`, et `dotnet restore --force-evaluate` pour
  passer à la plus récente) pour suivre le canal ; épinglez une version taggée pour tout usage
  durable.
- Un build dev n'est pas une release : ni attestation, ni SBOM, rien sur NuGet.org.
- Une fois le flux déclaré comme source, `--prerelease` et les versions flottantes résolvent
  les builds dev de tous les paquets Orkeon, ceux de NuGet.org compris ; `--version` épingle
  une release.
- GitHub Packages exige un jeton même pour un dépôt public : un personal access token
  (classic) avec `read:packages`. Lancez les commandes hors d'un clone de ce dépôt — dans un
  clone, son `nuget.config` garde nuget.org comme unique source et vous obtiendriez le
  dernier tag à la place.

Pas à pas — le jeton, les deux shells, la vérification de ce qui tourne, le retour aux
releases : [Suivre `main` : le canal dev](../getting-started/three-ways-to-run-orkeon.md#suivre-main--le-canal-dev).

```bash
dotnet nuget add source https://nuget.pkg.github.com/Orkeon/index.json \
  --name orkeon-github \
  --username <votre-utilisateur-github> \
  --password <PAT-avec-read:packages> --store-password-in-clear-text

dotnet tool install -g Orkeon.Scripting.Cli --prerelease   # déjà installé : dotnet tool update, mêmes arguments
# dans un projet : <PackageReference Include="Orkeon" Version="*-*" />
```

## Archives d'installation (`release.yml`)

Sur un tag `v*`, `release.yml` construit tous les artefacts d'installation, **smoke-teste chaque
canal d'onboarding sur un vrai runner** (zip CLI Windows, service Windows, paquet Debian,
tarball macOS), et seulement ensuite les attache à la GitHub Release. Le pipeline est
`installers → {smoke-windows, smoke-windows-service, smoke-deb, smoke-macos, msi} →
release → apt-publish → verify-apt` (les deux derniers sont décrits dans
[Le dépôt apt](#le-dépôt-apt)) ; derrière ces mêmes cinq jobs, `runners-image` pousse l'image conteneur
`orkeon-runners` sur GHCR en parallèle de `release`, de sorte qu'un smoke rouge ne déplace
ni la Release ni le tag `:latest`. `workflow_dispatch` exécute la même chose sans la
publication (pas de tag, donc pas de Release à alimenter). Un tag portant un segment de
pré-version (`v1.0.0-rc.4`) est publié **comme prerelease**, et ne devient donc jamais la
release *latest* du dépôt ; un `v1.0.0` nu, si. Le runner `orkeon-examples`, retiré, n'est **plus packagé** — le CLI `orkeon` le
remplace (`orkeon run crew.yaml` exécute les crews YAML de `examples/` ;
`orkeon run script.ork.ts` exécute le DSL de scripting).

| Artefact | Produit par | Contenu | Runtime |
|---|---|---|---|
| `orkeon-<version>-<rid>.tar.gz` / `.zip` | `package-installers.sh` (`--app-set full` par défaut) | tous les launchers — `orkeon`, `orkeon-slim`, `orkeon-repl`, `orkeon-host` — + les apps Orkeon Studio admises par leur filtre RID (le WPF `orkeon-studio` est réservé à `win-x64` ; les deux TUI partout) + un esbuild partagé + l'arbre `deploy/` (unité systemd, script d'enregistrement SCM, Dockerfile.host) | mixte : `orkeon`, `orkeon-host` et les apps Studio self-contained, les autres framework-dependent |
| `orkeon-cli-<version>-win-x64.zip` | `package-installers.sh --app-set cli --rids win-x64` | le CLI `orkeon` + `orkeon-studio` (Orkeon Studio WPF) + `install.ps1` | self-contained |
| `orkeon_<version>_amd64.deb` / `orkeon_<version>_arm64.deb` | `package-deb.sh --arch amd64\|arm64` (réutilise les arbres de staging `linux-x64` et `linux-arm64` — un publish, deux paquets par architecture) | le CLI `orkeon` en `/usr/bin/orkeon` + les TUI Studio en `/usr/bin/orkeon-studio-config` et `/usr/bin/orkeon-studio-run` | self-contained ; `Depends` uniquement sur des bibliothèques système (`libicu78` jusqu'à `libicu70`, `libssl3t64 \| libssl3`, `libc6 (>= 2.34)`…), jamais sur `dotnet-runtime-*` ; `Recommends: orkeon-archive-keyring` |
| `orkeon-archive-keyring_<YYYY.MM.DD>_all.deb` | `package-keyring-deb.sh`, une fois par version du trousseau (une date, comme `2026.10.04`, tirée de `installers/apt/keyring.version`) ; les Releases suivantes rattachent les octets déjà publiés, jamais une reconstruction | `/usr/share/keyrings/orkeon-archive-keyring.gpg`, la clé publique du [dépôt apt](#le-dépôt-apt) | — |
| `orkeon-<version>-win-x64.msi` | `build-msi.ps1` (WiX, portée per-user), moissonnant le zip CLI extrait | le CLI `orkeon` + `orkeon-studio` (WPF, avec un raccourci menu Démarrer « Orkeon Studio »), même publish élagué que le zip | self-contained |
| `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | `package-installers.sh --app-set cli --rids osx-arm64 osx-x64` (cross-publiés depuis le runner ubuntu) | le seul CLI `orkeon` + `install.sh` (pas de Studio en V1 — le canal macOS reste CLI seul) | self-contained |
| `SHA256SUMS` | les scripts d'empaquetage du job `installers` (`package-deb.sh` rafraîchit sa propre ligne) | une ligne par artefact ci-dessus **sauf le MSI**, plus le SBOM (`orkeon-<version>.sbom.cdx.json`) | — |
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
`ubuntu-latest` standard, et un runner `ubuntu-24.04-arm` pour le paquet arm64) installe le
`.deb` via `apt`, déroule la même chaîne, puis retire le paquet ; `smoke-macos` (runner `macos-latest`, Apple Silicon) extrait l'archive `osx-arm64`,
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

**Après publication.** `release.yml` appelle lui-même `release-verify.yml` une fois la
Release publiée — une Release créée avec le `GITHUB_TOKEN` du workflow ne déclenche aucun autre
workflow, un déclencheur `release: published` ne partirait donc jamais ; il tourne aussi à la
demande pour un tag donné. Il télécharge les assets *depuis la page de la Release*, contrôle
chacun d'eux contre `SHA256SUMS` et `SHA256SUMS.msi` — un asset sans ligne, ou une ligne sans
asset, fait échouer le run — et rejoue dessus les smokes d'onboarding du `.deb` et
d'`osx-arm64`, ce qui attrape un asset qu'un envoi, un remplacement ou une altération aurait
rendu différent de ce que les smokes ci-dessus ont installé.

`smoke-macos` est aussi le seul endroit où l'histoire Gatekeeper / signature est éprouvée : une
bibliothèque native non signée, en quarantaine ou malformée (`libtree-sitter*.dylib`,
onnxruntime, le binaire esbuild) est tuée au chargement, donc l'échec se produit là plutôt que
dans le terminal d'un utilisateur.

Le nom du fichier `.deb` porte la version du tag (`orkeon_1.0.0-rc.4_amd64.deb`), tandis que
le champ `Version:` du paquet remplace `-` par `~` (`1.0.0~rc.4`) pour qu'une pré-version se
classe avant sa version finale au sens de `dpkg`. Les deux divergent à dessein : GitHub réécrit
`~` dans le nom d'un asset envoyé, et apt lit le nom du fichier dans son index, jamais dans le
paquet. Aucun nom d'asset ne porte de caractère hors de `[A-Za-z0-9._-]` — le job `installers`
échoue sinon — et `SHA256SUMS` liste donc chaque asset sous le nom avec lequel il est publié.
(Les Releases `1.0.0-rc.3` et `1.0.0-rc.4` précèdent la règle : leur `.deb` a été publié sous
`orkeon_1.0.0.rc.N_amd64.deb` alors que leur `SHA256SUMS` le nomme avec `~`.)

Le `ProductVersion` du MSI, lui, perd carrément le suffixe : Windows Installer ne porte que trois
champs numériques, donc `build-msi.ps1` tronque `1.0.0-rc.1` en `1.0.0` pour la propriété que
`<MajorUpgrade>` compare réellement. Rien n'est perdu en silence — la version complète survit
dans le nom du `.msi` (`orkeon-1.0.0-rc.1-win-x64.msi`) et dans la propriété `ARPCOMMENTS`
affichée dans « Applications installées ».

Le CLI `orkeon` est distribué via **huit canaux** :

| Canal | Artefact | Runtime | Public |
|---|---|---|---|
| Tool dotnet NuGet | `Orkeon.Scripting.Cli` (`PackAsTool`, commande `orkeon`) | requiert le SDK .NET 10 (`dotnet tool install`) | développeurs .NET. Fait partie du lineup NuGet.org (PUB-25) — publiable depuis que le paquet est passé de 262,5 Mo à 137,6 Mo (natifs onnxruntime iOS/Android exclus) ; premier push au tag `v1.0.0-rc.3` |
| Zip Windows + `install.ps1` | `orkeon-cli-<version>-win-x64.zip` | self-contained | onboarding Windows — le canal recommandé. Livre `orkeon-studio` (Orkeon Studio WPF) à côté du CLI |
| MSI Windows (per-user) | `orkeon-<version>-win-x64.msi` | self-contained | Windows, installation au double-clic et entrée « Applications installées ». Livre `orkeon-studio` avec un raccourci menu Démarrer. Un canal à la fois : le MSI refuse de s'installer par-dessus une install zip |
| Dépôt apt Debian / Ubuntu | les paquets `.deb` ci-dessous, indexés sur la branche `apt` (canaux `stable`, `rc`, `dev`) | self-contained | Debian / Ubuntu, amd64 et arm64 — le canal recommandé : `apt install`, `apt upgrade`. Voir [Le dépôt apt](#le-dépôt-apt) |
| Paquet Debian | `orkeon_<version>_amd64.deb` / `_arm64.deb` | self-contained | Debian / Ubuntu sans le dépôt (une version, sans mise à jour). Livre les TUI `orkeon-studio-config` / `orkeon-studio-run` à côté du CLI |
| Archive macOS + `install.sh` | `orkeon-cli-<version>-osx-arm64.tar.gz` / `-osx-x64.tar.gz` | self-contained | onboarding macOS aujourd'hui ; `install.sh` retire l'attribut de quarantaine Gatekeeper et re-signe en ad-hoc les Mach-O que `codesign -v` rejette |
| Homebrew | les mêmes archives osx, via `installers/homebrew/orkeon.rb` | self-contained | macOS, une fois le tap créé — **pas encore publié**, voir ci-dessous |
| Archive d'installation multi-apps | launchers `orkeon` / `orkeon-slim` | `orkeon` self-contained, `orkeon-slim` framework-dependent | devs voulant aussi le REPL, l'hôte de service ou les applications Studio |

**Homebrew — formule dans le repo, tap pas encore créé.** `installers/homebrew/orkeon.rb` est
une formule binaire : elle télécharge l'archive osx correspondant à l'architecture de la
machine (`on_arm` / `on_intel`), installe la charge utile sous le `libexec` du Cellar, et écrit
un wrapper `bin/orkeon` qui pointe `ORKEON_ESBUILD_PATH` vers l'esbuild embarqué — le même
contrat que `wrapper.sh.tmpl`. Son bloc `test do` exécute `orkeon doctor` plutôt que
`orkeon --version` : doctor lance aussi l'esbuild embarqué et vérifie que les bibliothèques
natives sont en place, et un LLM non configuré n'y est qu'un avertissement.

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

### Le dépôt apt

Les utilisateurs Debian et Ubuntu ajoutent une source, puis gèrent Orkeon avec apt — le côté
utilisateur est dans [Installer avec apt](../guides/install-with-apt.md). Le dépôt vit dans ce
dépôt GitHub, en deux moitiés :

- **Les paquets restent des assets de Release** — les `.deb` que `release.yml` construit,
  smoke-teste et atteste déjà. Rien n'est copié ailleurs.
- **L'index signé vit sur la branche orpheline `apt`** : un répertoire par canal
  (`InRelease`, `Release`, `Release.gpg`, `Packages`, `Packages.gz`, `by-hash/SHA256/…`), plus
  le trousseau binaire `orkeon-archive-keyring.gpg` à sa racine pour la première mise en place.
  Seuls les workflows y poussent, un commit par publication ; un ruleset interdit sa
  suppression et tout force-push. Elle ne contient aucun `.deb` et ne pèse que quelques
  kilo-octets.

La source porte `URIs: https://github.com/Orkeon/orkeon/` et `Suites: raw/apt/<canal>/` — un
dépôt « plat ». apt lit l'index à `…/raw/apt/<canal>/`, que GitHub redirige vers
`raw.githubusercontent.com`, et résout le `Filename:` de chaque paquet —
`releases/download/<tag>/<asset>` — contre la même racine : le téléchargement aboutit sur
l'asset de la Release. La contrepartie est la limite de débit de GitHub sur les accès anonymes
à raw (`429`) : `apt update` n'en fait qu'un avertissement, un build Docker réessaie.

| Canal | Répertoire | Reçoit |
|---|---|---|
| `stable` | `stable/` | chaque tag `v*` sans segment de pré-version — le même test qui fixe `prerelease:` sur la Release. Vide tant qu'aucune version finale n'est publiée |
| `rc` | `rc/` | chaque tag `v*` : tout paquet de `stable` est donc aussi dans `rc` |
| `dev` | `dev/` | le paquet `orkeon` de chaque push vert sur `main`, en version `<version des props avec ~>.dev.<n>` (`1.0.0~rc.4.dev.<n>`) ; ses assets sont sur l'unique prerelease à tag fixe `apt-dev` ; seuls les trois derniers builds sont gardés. Jamais publié sur NuGet.org, jamais attesté comme une release |

Le fichier `Release` de chaque canal garde `Origin: Orkeon` et `Label: Orkeon` pour toujours (un
changement obligerait chaque machine à le confirmer), fixe `Suite:` au chemin de la source sans
son `/` final et ne porte pas de `Codename` (sinon apt avertit « Conflicting distribution »),
déclare `Architectures: amd64 arm64` et `Acquire-By-Hash: yes`, ne liste que des empreintes
SHA-256, porte une `Date` strictement croissante (apt ignore un `InRelease` plus ancien que celui
qu'il a) et pas de `Valid-Until`. L'index est produit par `apt-ftparchive` ; les fichiers
`by-hash` sont élagués par les scripts de publication, qui gardent au moins trois générations.

**Historique et immuabilité.** Un canal liste **toutes** les versions encore attachées à une
Release : `apt install orkeon=<version>` peut donc toujours revenir en arrière. Une stanza
publiée ne change jamais d'empreinte pour un même paquet, une même version et une même
architecture : le générateur refuse. `release.yml` ne réécrit jamais les assets d'un tag publié
— il crée la Release en brouillon, y attache les assets, puis la publie, et les Releases sont
immuables — car un asset renvoyé ferait échouer chaque machine en « Hash Sum mismatch ».

**La chaîne sur un tag.** `release` publie la Release ; `apt-publish` (environnement GitHub
`apt-signing`, le seul endroit qui détient la sous-clé de signature, dans les secrets
`APT_SIGNING_KEY` et `APT_SIGNING_PASSPHRASE`, déployable depuis les tags `v*` seulement) ajoute
les nouveaux paquets à `rc` — et à `stable` pour une version finale —, signe l'index et pousse
la branche `apt` ; `verify-apt` rejoue ensuite **mot pour mot** le bloc d'installation du
guide, dans des conteneurs neufs Debian 12 et 13 et Ubuntu 22.04, 24.04 et 26.04, en amd64 et
arm64 : installation, montée depuis la version précédente, désinstallation dans l'ordre
documenté. Il réessaie tant que raw sert encore l'index précédent. Un contrôle hebdomadaire
compare l'index aux assets, l'échéance de la clé au seuil de 180 jours, et vérifie la
disponibilité de raw.

**La maintenance hors tag** passe par `apt-maintenance.yml` (`workflow_dispatch`, lancé **depuis
un tag** pour que la règle de l'environnement l'admette), avec les mêmes scripts, le même
environnement et le même groupe de concurrence qu'`apt-publish` : `resign` signe de nouveau les
deux canaux avec une `Date` plus récente ; `yank <paquet>=<version>[/<arch>]` retire une stanza
et signe de nouveau ; `seed <tag>…` indexe des assets publiés avant que le dépôt existe.

#### Procédure : prolonger ou faire tourner la sous-clé de signature

La sous-clé est valable deux ans et se prolonge **au moins six mois** avant son échéance — la CI
échoue sous 180 jours. Une clé expirée casse `apt update` sur toutes les machines, sans repli.

1. Sur la machine hors ligne, prolonger la sous-clé
   (`gpg --quick-set-expire <empreinte primaire> 2y <empreinte sous-clé>`) — ou, pour une
   rotation, en ajouter une nouvelle (`gpg --quick-add-key <empreinte primaire> ed25519 sign 2y`).
2. Exporter le certificat public, remplacer `installers/apt/orkeon-archive-keyring.asc`, changer
   `installers/apt/keyring.version`, et mettre à jour le SHA-256 du trousseau dans `SECURITY.md`,
   `SECURITY.fr.md` et les deux pages d'installation (la garde de la CI vérifie leur accord).
3. La Release suivante publie le nouvel `orkeon-archive-keyring` ; les utilisateurs le
   reçoivent par `apt upgrade`. Une prolongation s'arrête là ; lancer `apt-maintenance.yml` en
   mode `resign` si l'index doit être signé de nouveau avant le tag suivant.
4. Pour une rotation seulement, **une Release plus tard** : le mainteneur remplace
   `APT_SIGNING_KEY` par la nouvelle sous-clé, exportée seule
   (`gpg --export-secret-subkeys <empreinte sous-clé>!`). L'ancienne sous-clé expire, ou est
   révoquée.

#### Procédure : retirer une version (yank)

1. Lancer `apt-maintenance.yml` en mode `yank`, depuis un tag, en nommant
   `<paquet>=<version>[/<arch>]`.
2. Vérifier que l'`InRelease` du canal sur la branche `apt` porte une `Date` plus récente et ne
   liste plus la version ; après un `apt update`, `apt-cache policy orkeon` ne la montre plus.
3. Seulement ensuite, et seulement si besoin, supprimer l'asset de sa Release. Toute purge
   d'assets de Release commence par cette procédure — un asset indexé qui disparaît devient un
   404 pour chaque utilisateur.

#### Procédure : révoquer la clé

1. Publier le certificat de révocation (gardé hors ligne depuis la création de la clé) dans
   `SECURITY.md`, `SECURITY.fr.md` et les pages d'installation.
2. Geler la publication : aucun tag, aucun lancement d'`apt-maintenance.yml`, jusqu'à ce que la
   nouvelle clé soit en place.
3. Créer une nouvelle clé et un nouveau trousseau, publier leur empreinte et leur SHA-256 comme
   dans la procédure de prolongation, et signer l'index avec la nouvelle sous-clé. Les
   utilisateurs doivent télécharger eux-mêmes la nouvelle clé (les premières lignes du bloc
   d'installation) — le seul cas où ils doivent agir.

#### Procédure : la clé a expiré

Les utilisateurs voient `EXPKEYSIG` à chaque `apt update`, et le canal ne se met plus à jour.
Prolonger la sous-clé (procédure ci-dessus, étape 1), publier le certificat et une nouvelle
version d'`orkeon-archive-keyring`, puis lancer `apt-maintenance.yml` en mode `resign`. Une
machine dont le trousseau est antérieur à la prolongation ne peut pas vérifier le nouvel index :
elle réinstalle le trousseau par les premières lignes du bloc d'installation, ou installe le
nouvel `orkeon-archive-keyring_<YYYY.MM.DD>_all.deb` téléchargé depuis sa Release
(`sudo apt install ./orkeon-archive-keyring_<YYYY.MM.DD>_all.deb`).

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
| `Orkeon.Host` | `IsPackable=false` — livré uniquement comme binaire `orkeon-host` dans les archives de release. |
| `Orkeon.Studio.{Core,Config,Run,Wpf}` | `IsPackable=false` — livrés uniquement via les installeurs de release (voir [Orkeon Studio](../architecture/studio.md)). |

## Câblage de la publication

- Tout le packaging et le push NuGet vivent dans **`publish.yml`** (tag `v*`) : le garde
  tag/version et `scripts/check-release-readiness.py` (section CHANGELOG coupée, PublicAPI
  livrée), un build Release et les tests unitaires + rapides, `dotnet pack Orkeon.sln` piloté par `IsPackable`, le gate
  `scripts/check-package-closure.py` sur les artefacts packés, un push de tout vers
  **GitHub Packages** avec `--skip-duplicate` (ré-exécutions idempotentes), puis le **lineup
  NuGet.org** (le tableau ci-dessus, `Orkeon` en premier) vers **NuGet.org**. Son job
  `publish-dev` packe en plus chaque push vert sur `main`, pour GitHub Packages seulement (voir
  *Canal dev* plus haut). `ci.yml` valide (build + tests) et ne package rien ; `release.yml`
  construit les archives d'installation et l'image conteneur, sans packaging NuGet.
- L'authentification NuGet.org est le **Trusted Publishing (OIDC)** — aucune clé API longue
  durée. Une politique nuget.org (dépôt `Orkeon/orkeon`, workflow `publish.yml`) permet à
  `NuGet/login` d'échanger le jeton OIDC du job contre une clé éphémère ; les étapes sont
  conditionnées à la **variable de dépôt `NUGET_USER`** (le profil nuget.org propriétaire de
  la politique). Les deux sont **en place et éprouvées** — les pushes rc.1/rc.2 sont passés
  par ce chemin ; si la variable venait à disparaître, les étapes émettraient un
  avertissement et ne feraient rien plutôt que de faire échouer le tag.
  Étendre le lineup NuGet.org est une décision de mainteneur consignée d'abord dans cette
  matrice (et reflétée dans le `LINEUP` de `check-package-closure.py`, la boucle de push du
  workflow, les arguments du gate de fermeture et le processus de release de CONTRIBUTING —
  `check-doc-claims.py` échoue dès qu'une copie diverge), jamais une retouche de workflow en
  passant.
- `publish.yml` **refuse un tag qui ne correspond pas à la version de
  `src/Directory.Build.props`**. Leçon de l'incident 0.9.1-beta (voir CHANGELOG 0.9.2-beta) :
  les tags `v0.9.1-beta.rc*` ont re-packé la version inchangée des props et
  `--skip-duplicate` a sauté chaque push en silence — une « release » qui n'a rien publié.
  Le garde maintient `--skip-duplicate` honnête.
- **Provenance et SBOM.** Les deux workflows attestent ce qu'ils publient avec
  `actions/attest-build-provenance` (SLSA v1, signé par l'instance Sigstore de GitHub) :
  `publish.yml` chaque `*.nupkg` d'un tag (les builds du canal dev ne sont pas attestés),
  `release.yml` chaque archive, `.deb` et MSI. L'image `orkeon-runners` que `release.yml`
  pousse sur GHCR n'est pas attestée. Tous deux
  génèrent un **SBOM CycloneDX** de `Orkeon.sln` juste après le build (outil dotnet
  `CycloneDX`, version épinglée, aucune action tierce) — `orkeon-<version>.sbom.cdx.json` — et
  le couvrent par la **même** attestation : un asset de release à côté des archives et une
  ligne dans `SHA256SUMS` pour `release.yml`, un artefact de run nommé `sbom` pour
  `publish.yml`. Comment vérifier tout cela, et pourquoi un téléchargement nuget.org doit
  d'abord perdre sa signature repository, est dans
  [Vérifier ce que vous installez](../guides/verify-what-you-install.md).
- La version provient de `src/Directory.Build.props` (actuellement `1.0.0-rc.4`), la source de vérité unique : aucun projet ne la surcharge, et le garde-fou de tag du workflow de publication refuse tout tag `v*` qui la contredit. Le canal dev en dérive son `<version>.dev.<n>`.
