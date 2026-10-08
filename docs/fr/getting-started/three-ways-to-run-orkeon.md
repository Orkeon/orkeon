> 🇬🇧 [English version](../../getting-started/three-ways-to-run-orkeon.md)

# Trois façons d'exécuter Orkeon

> **Voir aussi** : [Lancer votre premier exemple](./run-your-first-example.md) · [Vue d'ensemble](./overview.md) · [Retour à l'index](../INDEX.md)

Les crews Orkeon se lancent de trois façons. Choisissez celle qui correspond à
ce que vous acceptez d'installer :

| Voie | Prérequis | Temps avant la première exécution | Idéal pour |
|---|---|---|---|
| **1. Depuis les sources** | SDK .NET ≥ 10.0.300, clone git | ~5 min (+ build) | Contributeurs, lecture/modification du code, exécution de n'importe lequel des 104 exemples embarqués |
| **2. Binaire de release** | Rien pour les paquets CLI (zip/MSI Windows, `.deb` Debian) — ils embarquent le runtime ; **runtime** .NET 10 pour les launchers supplémentaires de l'archive multi-apps | ~2 min | Exécuter des exemples et des vitrines sans clone des sources |
| **3. Conteneur** | Docker | ~1 min (après le pull de l'image) | CI, exécutions reproductibles, aucun .NET local |

Les trois pilotent le même **CLI `orkeon`** et acceptent les mêmes flags. La
config de la crew est l'argument positionnel de `orkeon run <config>` ; les flags
optionnels (`--settings`, `-v`, `--mount`, `--var`, `--llm-log`, …) sont documentés une
seule fois, en détail, dans
[Lancer votre premier exemple](./run-your-first-example.md#chaque-flag-expliqué).

> **Vous préférez une fenêtre à une invite ?** Les paquets de release Windows et
> Linux embarquent aussi **[Orkeon Studio](#orkeon-studio-la-voie-graphique)** —
> une interface graphique par-dessus ce même CLI, installée à côté de lui. Ce
> n'est pas une quatrième façon d'exécuter une crew : Studio édite le même
> fichier de configuration et délègue au même `orkeon run`.

---

## 1. Depuis les sources

Clonez, compilez, et exécutez n'importe quel `config.yaml` sous `examples/` via
le projet CLI `orkeon` :

```bash
git clone https://github.com/Orkeon/orkeon.git
cd orkeon
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run \
  examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json
```

C'est la voie la plus souple — elle peut exécuter **tous** les exemples et prend
en compte vos modifications locales. Guide complet, configuration des profils LLM
et dépannage : [Lancer votre premier exemple](./run-your-first-example.md).

Ce qu'il faut sur le poste dépend de jusqu'où vous allez :

| Pour… | Il vous faut |
|---|---|
| **Lancer un exemple** (`dotnet run`, ci-dessus) | le **SDK .NET 10**, 10.0.300 ou plus récent (`dotnet --version` ; `global.json` l'épingle), et **git**. Le premier build restaure ses paquets depuis nuget.org |
| **Construire une archive d'installation** depuis le clone (`scripts/package-installers.sh`, ou `scripts/package-installers.ps1` sous Windows), ou [installer ce que vous venez de cloner](#installer-ce-que-vous-venez-de-cloner), qui en construit une et vérifie tout cela d'abord | les deux ci-dessus, plus : **Python 3** — `python3` sur le `PATH`, `python` sous Windows (l'alias du Microsoft Store qui porte ce nom ne compte pas) ; **`tar`** ; un accès réseau à **nuget.org** et à **`registry.npmjs.org`**, où le script va chercher le binaire esbuild qu'il embarque. Sous Windows, le script exige **PowerShell 7** (`pwsh`) : Windows PowerShell 5.1, celui qui est installé avec le système, s'y arrête. Sous Linux, le script appelle aussi `curl`, `openssl` et `sha256sum` |

Une archive construite ainsi s'installe comme une archive téléchargée — voir
[Binaire de release](#2-binaire-de-release) plus bas, prérequis Windows compris.

### Installer ce que vous venez de cloner

`dotnet run` lance le CLI depuis le clone. Pour avoir sur votre `PATH` l'`orkeon` que vous
avez compilé — l'installation que fait une archive de release, à la version de votre
checkout — une commande, depuis le clone :

```powershell
git clone https://github.com/Orkeon/orkeon.git
cd orkeon
.\scripts\install-from-source.ps1     # Windows, depuis PowerShell 7 (pwsh)
```

```bash
./scripts/install-from-source.sh      # Linux, macOS
```

Le script vérifie d'abord ce dont le build a besoin — la seconde ligne du tableau
ci-dessus — et nomme **tout** ce qui manque en une fois, avec l'adresse de chaque outil,
avant de compiler quoi que ce soit. Il n'installe rien de tout cela à votre place. Il
construit ensuite l'arbre de l'archive du CLI pour votre poste, sans écrire l'archive
(plusieurs minutes ; le premier passage restaure les paquets NuGet et va chercher le binaire
esbuild, un passage suivant ne retélécharge rien de ce qui est en cache), puis l'installe
avec l'installeur de l'archive : `%LOCALAPPDATA%\Programs\Orkeon` sous Windows, `~/.local`
ailleurs.

`orkeon --version` répond alors la version de votre checkout —
`1.0.0-rc.4.local.<date du commit>` hors tag, jamais le nom d'une release — et
`orkeon doctor` nomme le canal `source`.

| Windows | Linux, macOS | |
|---|---|---|
| `-AppSet full` | `--app-set full` | tous les launchers — le REPL et l'hôte de service aussi — au lieu du seul jeu CLI |
| `-InstallDir <dossier>` | `--prefix <dossier>` | où installer ; `--modify-path` est passé lui aussi à `install.sh` |
| `-WhatIf` | `--dry-run` | s'arrêter après les contrôles, et afficher la version et les commandes qu'il lancerait |
| `-Uninstall` | `--uninstall` | retirer l'installation. Ne construit rien ; votre configuration reste |

- **Après un `git pull`, relancez-le** : l'installation est remplacée, et la version suit
  le commit.
- **Windows** : le build exige PowerShell 7. Lancé depuis Windows PowerShell 5.1, le script
  le dit, et où le prendre. Si Orkeon est installé par le MSI, ou tourne depuis le
  répertoire d'installation, l'installeur refuse et le script relaie sa phrase.
- **Debian et Ubuntu** : le canal `dev` du [dépôt apt](../guides/install-with-apt.md) sert
  les builds de `main` sans rien compiler sur votre poste. Le script le dit, et continue.
- Un clone superficiel (`git clone --depth 1`) convient. Les sous-modules privés des
  mainteneurs ne sont ni nécessaires ni touchés.

---

## 2. Binaire de release

Chaque [GitHub Release](https://github.com/Orkeon/orkeon/releases) attache un
**paquet CLI par plateforme**, les **archives multi-apps** historiques, et un
[tool dotnet](https://www.nuget.org/) NuGet :

| Artefact | Contenu | Prérequis runtime | Idéal pour |
|---|---|---|---|
| **`orkeon-cli-<version>-win-x64.zip`** | le CLI `orkeon` + **Orkeon Studio** (`orkeon-studio`, l'application de bureau) + `install.cmd` et `install.ps1` | pas de .NET — self-contained. Double-cliquez `install.cmd` : il tourne sur le PowerShell livré avec Windows. Voir [Avant de commencer](#avant-de-commencer) | **Windows : le téléchargement recommandé** |
| **`orkeon-<version>-win-x64.msi`** | les deux mêmes, MSI per-user, avec un raccourci menu Démarrer « Orkeon Studio » | pas de .NET — self-contained. Non signé : Windows demande avant de le lancer, voir [Avant de commencer](#avant-de-commencer) | Windows, si vous préférez le double-clic et une entrée « Applications installées » |
| **`orkeon_<version>_amd64.deb`** / **`_arm64.deb`** | le CLI `orkeon` en `/usr/bin/orkeon` + les deux applications terminal **Orkeon Studio** | aucun — self-contained | **Debian / Ubuntu** — de préférence depuis le [dépôt apt](../guides/install-with-apt.md), qui sert ces mêmes paquets et leurs mises à jour |
| **`orkeon-cli-<version>-osx-arm64.tar.gz`** / **`-osx-x64.tar.gz`** | le seul CLI `orkeon` + `install.sh` (pas de Studio en V1 — le canal d'onboarding macOS reste CLI seul) | aucun — self-contained | **macOS**, Apple Silicon et Intel respectivement |
| **`orkeon-<version>-<rid>.tar.gz`** / **`.zip`** | **tous** les launchers (`orkeon`, `orkeon-repl`, `orkeon-host`…) + les applications Studio que la plateforme supporte + `install.sh` / `install.cmd` et `install.ps1` | mixte — voir le tableau des commandes ci-dessous | Le REPL et l'hôte de service |
| **`dotnet tool install --global Orkeon.Scripting.Cli --prerelease`** | le seul CLI `orkeon` — **sans esbuild** : un script `.ork.ts` demande `npm install -g esbuild` (ou `ORKEON_ESBUILD_PATH`) ; les crews YAML non. `orkeon typings` écrit les typings d'éditeur | **SDK** .NET 10 | Obtenir uniquement le CLI sur un poste qui compile déjà du .NET |

`<rid>` vaut `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` (`.tar.gz`) ou
`win-x64` (`.zip`). Dans chaque nom de fichier, `<version>` est la version du tag
(`1.0.0-rc.4`). `SHA256SUMS` couvre tous les artefacts de la release sauf le
MSI, qui a son propre `SHA256SUMS.msi` (chaque fichier est produit par le job CI
qui a construit l'artefact).

L'archive multi-apps est la seule à embarquer plus que le CLI :

| Commande | Ce qu'elle exécute | Runtime |
|---|---|---|
| `orkeon` | **Le CLI principal et le point d'entrée par défaut** — `orkeon run <config.yaml>` pour tout crew YAML, ou `orkeon run script.ork.ts` pour le DSL de scripting | self-contained |
| `orkeon-slim` | Le même CLI, framework-dependent et bien plus petit | requiert .NET 10 |
| `orkeon-repl` | Console REPL interactive complète (tous les outils intégrés, analyse de code, embeddings locaux) | requiert .NET 10 |
| `orkeon-host` | Le daemon d'hébergement — enregistre des crews et les sert en continu (unité systemd, service Windows via le script embarqué ou son MSI per-machine dédié, passerelle de chat, canal Discord ; voir [le service host](../architecture/service-host.md)) | autonome |
| `orkeon-studio` | **Orkeon Studio**, l'application de bureau — archives Windows uniquement (voir [plus bas](#orkeon-studio-la-voie-graphique)) | self-contained |
| `orkeon-studio-config` / `orkeon-studio-run` | **Orkeon Studio** dans le terminal : éditeur de settings et lanceur de crew | self-contained |

> **Le prérequis runtime, en une ligne.** Les paquets CLI (zip, MSI, `.deb`), les
> launcher `orkeon` et les applications Orkeon Studio
> embarquent leur propre runtime et n'exigent aucune installation .NET. Tout le
> reste de l'archive multi-apps — et
> le tool dotnet — requiert le
> [runtime .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) (à
> vérifier avec `dotnet --list-runtimes`). `install.sh` et `install.ps1`
> détectent le cas et affichent les commandes d'installation de votre
> plateforme ; ils n'installent jamais de runtime à votre place.

### Windows

Deux canaux, tous deux per-user (aucun droit administrateur, rien d'écrit hors
de votre profil). **Installez un seul canal à la fois** — le MSI refuse de
s'installer par-dessus une install ZIP, donc désinstallez l'autre d'abord si
vous changez.

#### Avant de commencer

Aucun des deux canaux n'a besoin de .NET. Ce que Windows lui-même peut mettre en travers :

- **PowerShell.** Rien à installer. Le zip contient `install.cmd`, qui lance `install.ps1`
  sur Windows PowerShell 5.1, celui qui est installé avec Windows ; `install.ps1` est écrit
  pour lui et pour PowerShell 7 (`pwsh`). Chaque release installe l'archive sur une vraie
  machine Windows sous l'un et sous l'autre.
- **Politique d'exécution.** Rien à changer. Un poste Windows qui n'a jamais lancé de script
  refuse `.\install.ps1` tapé dans PowerShell — *« l'exécution de scripts est désactivée sur
  ce système »* — et `install.cmd` est la réponse à cela : il lance le script sous une
  politique qui ne vaut que pour cette commande et ne change aucun réglage du poste. Ne
  changez jamais la politique du poste ni celle de votre compte pour installer Orkeon. Si
  vous lancez le script vous-même, la même politique d'une seule commande s'écrit
  `powershell -ExecutionPolicy Bypass -File .\install.ps1`.
- **Un fichier venu d'Internet.** Un zip téléchargé par un navigateur est marqué comme
  venant d'Internet, et ce que l'Explorateur en extrait l'est aussi : Windows peut vous
  demander de confirmer avant de lancer `install.cmd`. La marque n'arrête pas le lanceur.
  Pour qu'aucune question ne soit posée, levez-la avant d'extraire : `Unblock-File
  .\orkeon-cli-<version>-win-x64.zip`.
- **Une politique imposée par votre organisation** (stratégie de groupe) l'emporte sur
  l'option du lanceur : là, seul un script signé tourne, et l'installeur d'Orkeon ne l'est
  pas. Voyez votre administrateur, ou passez par un poste où vous avez le droit de lancer
  des scripts.
- **SmartScreen.** Aucun des deux MSI n'est signé — `orkeon-<version>-win-x64.msi` ici, pas
  plus que celui de l'[hôte de service](../architecture/service-host.md),
  `orkeon-host-<version>-win-x64.msi`. Windows affiche *« Windows a protégé votre
  ordinateur »* avec un éditeur inconnu : **Informations complémentaires** → **Exécuter
  quand même**. Vérifiez d'abord ce que vous avez téléchargé — sa somme de contrôle et son
  attestation de build, dans
  [Vérifier ce que vous installez](../guides/verify-what-you-install.md) — car
  l'avertissement ne dit rien du fichier lui-même. Ou passez par le canal ZIP.

#### Installer

Canal A — le zip (recommandé). Extrayez-le, puis **double-cliquez `install.cmd`** : la
fenêtre reste ouverte sur le résultat. Ou depuis un terminal, PowerShell ou `cmd` :

```powershell
Expand-Archive orkeon-cli-<version>-win-x64.zip -DestinationPath .
cd orkeon-cli-<version>-win-x64
.\install.cmd     # -> %LOCALAPPDATA%\Programs\Orkeon, PATH utilisateur, entrée « Applications installées »
.\install.cmd -Uninstall
```

`install.cmd` passe ses arguments à `install.ps1` (`-InstallDir <dossier>`, `-Uninstall`)
et rend son code de sortie. Installer par-dessus une installation la remplace — c'est
ainsi que le canal zip se met à jour. **Fermez Orkeon Studio et laissez finir une crew en
cours d'abord** : tant qu'un programme de l'installation tourne, l'installeur refuse, le
nomme, et laisse l'installation comme elle était.

```powershell
# Canal B — MSI (double-clic, ou en silencieux)
msiexec /i orkeon-<version>-win-x64.msi          # même répertoire d'install, même entrée de PATH
msiexec /x orkeon-<version>-win-x64.msi /qn      # désinstallation
```

Dans les deux cas, votre configuration vit dans
`%APPDATA%\Orkeon\appsettings.json`, et **les deux désinstalleurs n'y touchent
pas**. (Un `appsettings.json` laissé dans un répertoire d'installation par une
install antérieure y est migré à la mise à jour.)

### Linux

Le **dépôt apt est le canal recommandé** sur Debian et Ubuntu, amd64 et arm64 :
ajoutez une fois la source signée — le bloc à copier est dans
[Installer avec apt](../guides/install-with-apt.md) — et Orkeon se met à jour avec le reste du
système. Le paquet est self-contained, il ne tire donc jamais de paquet `dotnet-runtime` ni de
dépôt Microsoft :

```bash
sudo apt install orkeon orkeon-archive-keyring   # installe /usr/bin/orkeon
sudo apt update && sudo apt upgrade              # chaque version suivante
```

Sans le dépôt — une machine qui n'atteint pas le contenu brut de GitHub —, installez
directement le `.deb` d'une release ; il ne reçoit aucune mise à jour :

```bash
sudo apt install ./orkeon_<version>_amd64.deb   # _arm64.deb sur ARM
sudo apt remove orkeon
```

Le `tar.gz` multi-apps + `install.sh` est l'alternative per-user (et la seule
option quand vous voulez le REPL et l'hôte de service) :

```bash
tar -xzf orkeon-<version>-linux-x64.tar.gz
cd orkeon-<version>-linux-x64
./install.sh                 # installe dans ~/.local ; --prefix /usr/local pour tout le système
./install.sh --modify-path   # ajoute aussi ~/.local/bin à vos fichiers rc de shell
./install.sh --uninstall
```

Comme cette archive embarque des launchers framework-dependent, `install.sh`
cherche un runtime `Microsoft.NETCore.App 10.x` (sur le `PATH` ou sous
`DOTNET_ROOT`) et, s'il n'en trouve aucun, affiche les commandes exactes de
votre distribution — `sudo apt install dotnet-runtime-10.0` sur Ubuntu 25.10+,
l'enregistrement du dépôt `packages.microsoft.com` sur Debian et Ubuntu LTS, ou
`dotnet-install.sh --runtime dotnet --channel 10.0` sous `$HOME` quand vous
n'avez pas sudo. L'avertissement ne bloque jamais l'installation : les fichiers
sont posés dans tous les cas, et `orkeon` lui-même fonctionne, étant
self-contained.

### macOS

Aucun prérequis dans les deux cas : les archives CLI macOS sont self-contained,
aucune installation .NET n'entre en jeu.

**Homebrew** sera le canal recommandé. La formule vit dans le dépôt, en
`installers/homebrew/orkeon.rb`, mais le dépôt `Orkeon/homebrew-tap` **ne sera
publié qu'à la première release taguée** — d'ici là, la commande ci-dessous ne
résout pas, et la voie d'entrée est le tar.gz :

```bash
brew tap orkeon/tap        # une fois le tap publié
brew install orkeon
```

**Archive + `install.sh`** fonctionne dès aujourd'hui. Prenez `osx-arm64` sur
Apple Silicon, `osx-x64` sur Intel :

```bash
tar -xzf orkeon-cli-<version>-osx-arm64.tar.gz
cd orkeon-cli-<version>-osx-arm64
./install.sh                 # installe dans ~/.local ; --modify-path pour mettre à jour votre rc de shell
./install.sh --uninstall
```

> **Gatekeeper.** Orkeon n'est pas signé avec un Apple Developer ID, et macOS
> marque tout ce qui est téléchargé via un navigateur avec
> `com.apple.quarantine` — c'est ce qui produit *« impossible d'ouvrir car le
> développeur ne peut pas être vérifié »*. `install.sh` traite les deux moitiés
> du problème tout seul : il retire l'attribut de quarantaine de l'arbre
> installé, et il re-signe en ad-hoc uniquement les fichiers Mach-O embarqués
> que `codesign -v` rejette réellement (une signature d'éditeur valide n'est
> jamais écrasée). Un téléchargement par `curl` ne pose aucun attribut de
> quarantaine, et `brew` le retire lui-même. Si une commande est malgré tout
> tuée ou refusée, l'échappatoire manuelle est
> `xattr -dr com.apple.quarantine ~/.local/lib/orkeon`.

Le `tar.gz` multi-apps (REPL, hôte de service) existe aussi pour
les deux architectures macOS, et requiert le runtime .NET 10 pour les launchers
framework-dependent qu'il embarque — `install.sh` affiche le
[lien de téléchargement](https://dotnet.microsoft.com/download/dotnet/10.0)
quand il manque.

### Premier lancement

Ouvrez un **nouveau** terminal (pour que la modification du PATH soit prise en
compte), puis :

```bash
orkeon init      # écrit la config globale : quel LLM, quel modèle, quel endpoint
orkeon doctor    # vérifie l'installation : runtime, config, profils LLM, joignabilité du LLM, esbuild, grammaires, …
orkeon run chemin/vers/crew.yaml
```

`orkeon init` est un assistant à 5 choix — `ollama`, `docker-model-runner`,
`openai`, `custom`, ou `none` — et écrit `%APPDATA%\Orkeon\appsettings.json` sur
Windows, `~/.config/Orkeon/appsettings.json` sur Linux et macOS. Il est
scriptable de bout en bout (`--provider`, `--base-url`, `--model`,
`--api-key-env` — ou le déconseillé `--api-key` en clair —, `--path`, `--force`,
`--no-probe`), et c'est ce qu'utilise la CI. `orkeon doctor` affiche ✅ / ⚠️ / ❌ par vérification, sort en `1` dès qu'une
vérification échoue, et accepte `--json` pour les scripts.

> **Aucun LLM configuré ?** Orkeon n'échoue pas et ne reste pas muet : il
> avertit sur stderr — *« No `Llm` section configured — falling back to the echo
> provider … Run `orkeon init` … »* — et exécute la crew contre le **fournisseur
> echo**, qui rejoue le prompt au lieu d'y répondre. C'est ce repli qui rend les
> démos de scripting exécutables sans clé ni serveur ; ce n'est jamais un LLM
> qui fonctionne. Si vous voyez cet avertissement alors que vous attendiez un
> vrai modèle, lancez `orkeon init`, puis `orkeon doctor` pour confirmer que
> l'endpoint est joignable.

### Orkeon Studio, la voie graphique

Les paquets Windows et Linux installent **Orkeon Studio** à côté du CLI. C'est
une interface, pas un second produit : elle édite le `appsettings.json` qu'écrit
`orkeon init`, et lance les crews en exécutant le binaire `orkeon` co-installé.
Tout ce qu'elle fait est faisable depuis le terminal, et tout ce qu'elle écrit
est lu par le CLI — vous pouvez passer de l'une à l'autre à tout moment.

| Plateforme | Commande | Livrée dans | Ce qu'elle apporte |
|---|---|---|---|
| **Windows** | `orkeon-studio` | le zip `win-x64` et le MSI | Une fenêtre de bureau à navigation latérale — éditeur de settings (presets, sections, montages, JSON brut, diagnostic) et lanceur de crew (exécution + historique) — avec thème clair/sombre, sélecteur de langue à cinq entrées (anglais, français, espagnol, allemand, chinois) et visite guidée. Le MSI enregistre en plus un **raccourci menu Démarrer « Orkeon Studio »**, donc aucun terminal n'est nécessaire pour la lancer |
| **Linux** | `orkeon-studio-config` | le `.deb` et les archives linux | Un éditeur plein écran dans le terminal pour le fichier de settings : presets de fournisseur, modèle et endpoint, et la table des points de montage VFS |
| **Linux** | `orkeon-studio-run` | le `.deb` et les archives linux | Choisir une cible (un `config.yaml`, un dossier de crew, ou un script `.ork.ts`), régler les options d'exécution — dont `--validate` pour un essai à blanc — puis suivre la sortie en direct et annuler au besoin |
| **macOS** | — | — | Pas en V1 sur le canal d'onboarding : les tarballs `orkeon-cli-*-osx-*` et Homebrew n'embarquent que le CLI. Les archives multi-apps `orkeon-<version>-osx-*` contiennent bien les deux applications terminal (seule l'application WPF a un filtre RID), non testées sur macOS en V1 |

```bash
orkeon-studio-config    # écrire ~/.config/Orkeon/appsettings.json sans l'assistant
orkeon-studio-run       # choisir une crew, la lancer, la suivre
```

Sous Windows, double-cliquez sur **Orkeon Studio** dans le menu Démarrer (canal
MSI) ou lancez `orkeon-studio` depuis un terminal — c'est la même fenêtre dans
les deux cas. Les deux applications terminal acceptent également `--version` et
`--help`, qu'elles affichent sans ouvrir d'interface plein écran : c'est ce qui
les rend scriptables et vérifiables en CI.

### Exécuter

Le CLI résout les settings LLM exactement comme depuis les sources.
`orkeon init` couvre le cas courant ; passez `--settings` pour pointer un profil
précis à la place (voir la
[matrice des profils](./run-your-first-example.md#3-choisir-un-profil-llm)) :

```bash
orkeon run chemin/vers/config.yaml \
  --settings chemin/vers/appsettings.local.json \
  --mount ./out:/output:rw
```

Les settings sont résolus dans cet ordre : `--settings`, puis l'`appsettings.json`
voisin du fichier de config, puis — en remontant les répertoires parents — un
sous-répertoire `appsettings/appsettings.json` à chaque niveau (la matrice de
profils partagée des exemples ; `_shared/appsettings.json` reste un fallback
déprécié), puis le fichier global per-user écrit par `orkeon init`, puis les
variables d'environnement `ORKEON_*` seules.

**Une crew peut aussi être un dossier**. Pointez
`orkeon run` sur un répertoire contenant une crew multi-fichiers — `config.yaml`
pour les réglages de la crew, un agent par fichier sous `agents/`, une task par
fichier sous `tasks/`, le nom de chaque fichier servant d'identifiant — et il se
charge exactement comme un fichier YAML unique. Le triplet plat historique
(`crew.yaml` + `agents.yaml` + `tasks.yaml`) est également accepté, et toutes les
options se comportent à l'identique sur un dossier (`--settings`, `-V/--var`,
`--initial-context`, `--mount`, `--validate`, `--verbose`, `--llm-log`) :

```bash
orkeon run examples/crew-multifile --validate
# VALIDATION OK: …/examples/crew-multifile (agents=2, tasks=2, tools resolved=0)
```

Un dossier contenant à la fois une disposition YAML et un point d'entrée de
scripting — n'importe quel `*.ork.ts` ou `*.ork.js` posé directement dedans, quel
que soit son nom — est refusé, en nommant les deux candidats, tout comme un dossier
sans disposition reconnue : Orkeon ne devine jamais lequel vous vouliez. Voir
[YAML et builders](./yaml-and-builders.md) pour la disposition elle-même.

**Une crew peut nommer les dossiers qu'elle utilise.** Un bloc `mounts:` dans
`config.yaml` (ou `crew.yaml`) liste les racines virtuelles que la crew lit et écrit —
`/output`, ou `<ulid>|/output` pour épingler une entrée des settings quand plusieurs
déclarent cette racine (une entrée des settings peut porter un identifiant de
26 caractères devant son `|` ; Orkeon Studio en écrit un à chaque enregistrement).
`orkeon run crew/` résout alors le bloc face au fichier de settings sans aucun
`--mount`, et refuse en une ligne une racine absente ou ambiguë ; `--mount-id <ulid>`
choisit une entrée depuis la ligne de commande, et un simple
`--mount <dossier>:/output:rw` remplace toutes les entrées des settings de cette racine
pour le run.

Vous pouvez aussi exécuter une commande directement depuis l'archive extraite,
sans installer : `./libexec/orkeon/orkeon run …`.

---

## 3. Conteneur

L'image `ghcr.io/orkeon/orkeon-runners` (construite depuis `Dockerfile.runners`,
publiée sur GHCR) a le **CLI `orkeon` comme point d'entrée par défaut**, embarque le
**REPL** (`orkeon-repl`) à côté **plus les 104 exemples fournis** — zéro .NET local
requis. L'hôte de service n'y est pas : il a sa propre image, construite depuis
`deploy/Dockerfile.host` (voir [l'hôte de service](../architecture/service-host.md)).

Chaque image garde la licence et les notices de ce qu'elle redistribue sous
`/usr/share/doc/orkeon/` (`LICENSE.md`, `THIRD-PARTY-NOTICES.md`), le chemin du paquet
Debian ; le runtime .NET est celui de l'image de base de Microsoft, qui porte les siennes.

### La convention `/workspace`

Montez votre répertoire de projet sur `/workspace` et référencez tout depuis là.
**Un seul volume, aucun flag supplémentaire** :

```bash
docker run --rm -v "$PWD:/workspace" ghcr.io/orkeon/orkeon-runners \
  run /workspace/crews/my-crew/config.yaml \
  --settings /workspace/appsettings.local.json \
  --mount /workspace/data:/data:ro /workspace/output:/output:rw
```

Trois commodités de l'image rendent cela immédiat :

1. **Pas besoin de `--allow-external-mounts`** — l'image fixe
   `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`, parce que la frontière du conteneur met
   déjà en bac à sable tous les chemins atteignables (passez
   `-e ORKEON_ALLOW_EXTERNAL_MOUNTS=0` pour rétablir le garde-fou).
2. **Les écritures fonctionnent** — l'entrypoint adopte l'uid/gid propriétaire
   de `/workspace` avant de s'exécuter, donc la crew peut écrire dans votre bind
   mount et les fichiers créés vous appartiennent sur l'hôte. Pas de `--user`,
   pas de `chmod`. (Il reste non privilégié : sans montage, il retombe sur
   l'utilisateur non-root `app` de l'image. Un `docker run --user` explicite
   contourne cette adoption, et `-e ORKEON_STAY_ROOT=1` garde root.)
3. **`/workspace` existe toujours** — même sans rien monter, pour que les mêmes
   commandes fonctionnent en CI.

Les montages de volumes sont la façon dont le système de fichiers hôte atteint
le [VFS](../architecture/vfs-compliance.md) de la crew : le `-v` de Docker mappe
hôte → conteneur, le flag `--mount` mappe conteneur → chemins virtuels de la
crew (`/data`, `/output`, …).

### Exécuter un exemple embarqué

Les exemples sont livrés dans l'image sous `/app/examples`, avec un utilitaire
qui rend leur exécution immédiate. La session la plus simple possible :

```bash
# une fois, sur l'hôte : récupérer le modèle par défaut (Docker Desktop → Model Runner)
docker model pull ai/granite-4.0-h-tiny

docker run -it --rm -e ORKEON_RUNNER=shell -v "$PWD/out:/output" \
  ghcr.io/orkeon/orkeon-runners
# puis, dans le shell :
orkeon-example list            # parcourir les 104 exemples embarqués
orkeon-example run 1           # exécuter le n°1 (assistant de recherche)
orkeon-example show 42         # lire d'abord le README d'un exemple
orkeon-example settings        # quels settings LLM s'appliquent, et pourquoi
```

`orkeon-example run` résout le numéro vers sa config, monte `/output` pour les
résultats fichiers, et choisit les settings LLM pour vous (section suivante). Quand
un numéro existe dans deux catégories (`16`, `102`), il liste les candidats —
qualifiez avec la catégorie : `orkeon-example run 02/16` (une sous-chaîne du nom,
comme `research-assistant`, marche aussi ; les arguments qui suivent l'identifiant
vont à `orkeon run`).

**Settings LLM dans le conteneur** — le défaut intégré vise **Docker Model
Runner sur votre hôte** (`host.docker.internal:12434`) : le `docker model pull`
ci-dessus est la seule mise en place, et tous les exemples fonctionnent ensuite
sans aucun flag. Si vous l'oubliez, `orkeon-example run` échoue vite, *avant* la
crew, en affichant la commande de pull exacte (et, quand l'endpoint sert
d'autres modèles, la surcharge `-e ORKEON_Llm__Model=<name>` pour en utiliser
un — `docker model list` sur l'hôte montre ce que vous avez). Pour utiliser
autre chose, choisissez un profil dans `/etc/orkeon/profiles` avec
`ORKEON_LLM_PROFILE` :

| `-e ORKEON_LLM_PROFILE=` | Endpoint | Nécessite |
|---|---|---|
| *(non défini)* = `host-dmr` | Docker Model Runner sur l'hôte, `:12434` | `docker model pull …` sur l'hôte |
| `host-ollama` | Ollama sur l'hôte, `:11434` | `ollama pull llama3.2` sur l'hôte |
| `openai` | Cloud OpenAI | `-e ORKEON_Llm__ApiKey=sk-…` |
| `local` | modèle embarqué dans l'image | la variante d'image `local-llm` (ci-dessous) |

Les variables d'environnement `ORKEON_Llm__*` surchargent n'importe quel profil
(ex. `-e ORKEON_Llm__Model=…`). Sur un moteur Linux nu (sans Docker Desktop),
ajoutez `--add-host=host.docker.internal:host-gateway` pour que les profils hôte
résolvent.

**Fenêtre de contexte plus grande (modèles hôte)** — Docker Model Runner sert
chaque modèle avec sa taille de contexte par défaut. Sur les versions récentes
de Docker Desktop, vous pouvez l'augmenter par modèle, par exemple 128K pour
Gemma 4 :

```bash
docker model configure --context-size 131072 gemma4:latest   # voir : docker model configure --help
docker model configure show gemma4:latest                    # vérifier — list/inspect ne montrent que le packaging
```

Un gros cache KV est gourmand en RAM (plusieurs Go supplémentaires à 128K) —
dimensionnez l'hôte en conséquence. `Llm.MaxTokens` dans les settings Orkeon
plafonne la longueur de la *réponse* et est indépendant de la taille de contexte
côté serveur.

Tout ce qui touche aux modèles locaux (pièges de DMR, Ollama, changement de
modèle, dimensionnement du contexte, dépannage) est consolidé dans le
[guide des modèles locaux](../guides/local-models.md).

Aucun modèle du tout ? Les démos de scripting embarquées tournent sur le repli
LLM echo — pas de clé, pas de serveur, pas de réseau. L'exécution l'annonce sur
stderr (*« No `Llm` section configured — falling back to the echo provider … Run
`orkeon init` … »*), donc un conteneur non configuré n'est jamais pris pour un
modèle qui fonctionne :

```bash
orkeon run /app/examples/scripting/01-hello-world.ork.ts
```

### Tout en local : embarquer un modèle dans votre image

Construisez une variante qui n'exige **ni serveur de modèle côté hôte, ni clé
d'API** — le `llama-server` de llama.cpp plus un GGUF sont embarqués et servis
dans le conteneur sur la même forme d'URL que celle qu'utilisent déjà les
settings par défaut :

```bash
# Granite 4.0 h-tiny (Apache 2.0, ~4,2 Go de poids → image ~6 Go)
docker build -f Dockerfile.runners --target local-llm \
  --build-arg LOCAL_MODEL_URL=https://huggingface.co/ibm-granite/granite-4.0-h-tiny-GGUF/resolve/main/granite-4.0-h-tiny-Q4_K_M.gguf \
  -t orkeon-runners:granite .

# Gemma 4 E4B avec un contexte de 128K (licence Gemma — gardez l'image locale, ne la poussez pas)
docker build -f Dockerfile.runners --target local-llm \
  --build-arg LOCAL_MODEL_URL=https://huggingface.co/unsloth/gemma-4-E4B-it-qat-GGUF/resolve/main/gemma-4-E4B-it-qat-UD-Q4_K_XL.gguf \
  --build-arg LOCAL_MODEL_NAME=ai/gemma4 \
  --build-arg LOCAL_MODEL_CTX=131072 \
  -t orkeon-runners:gemma4 .
# LOCAL_MODEL_CTX fixe la taille de contexte de llama-server (défaut 8192) ; surchargez
# par exécution avec -e ORKEON_LOCAL_LLM_CTX=… — à 128K, prévoyez plusieurs Go de RAM.

docker run -it --rm -m 8g -e ORKEON_RUNNER=shell orkeon-runners:granite
# le modèle se charge au démarrage (30-90 s), puis :
orkeon-example run 1
```

Inférence CPU : comptez environ 5 à 15 tokens/s et donnez de la mémoire au
conteneur (`-m 8g` ; sous WSL2, augmentez la mémoire de la VM dans `.wslconfig`
si nécessaire). `-e ORKEON_LOCAL_LLM=0` saute le serveur embarqué. Ces variantes
sont à construire soi-même par conception — aucun tag pré-construit n'est
publié, donc les obligations de licence des modèles restent de votre côté du
mur.

Le coup unique (sans shell) fonctionne toujours — le point d'entrée **est**
`orkeon` :

```bash
docker run --rm \
  -v "$PWD/out:/output" \
  ghcr.io/orkeon/orkeon-runners \
  run examples/01-enterprise/01-research-assistant/config.yaml \
  --mount /output:/output:rw
```

### Autres runners et shell interactif

Le point d'entrée **est** `orkeon`, donc tout ce qui suit le nom de l'image est
un argument du CLI (`run <config> …`). Pour lancer un autre runner, définissez
la variable d'environnement `ORKEON_RUNNER` — `orkeon` (la valeur par défaut),
`repl` ou `shell` ; toute autre valeur est refusée avec une ligne d'usage :

```bash
docker run -it --rm -e ORKEON_RUNNER=repl \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners
```

Il n'y a pas de runner par famille d'exemples : les crews finance sont des
scripts `main.ork.ts` qu'exécute la même CLI `orkeon`, comme tous les autres
exemples.

```bash
docker run --rm \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners \
  run examples/03-finance-trading/31-algo-trading/main.ork.ts \
  --settings /app/appsettings.local.json
```

`ORKEON_RUNNER=shell` ouvre un **zsh** interactif dans l'image (en démarrant
dans `/workspace`) — pratique pour fouiller les exemples embarqués ou déboguer
des montages. Une bannière d'accueil liste les commandes et chemins disponibles
(supprimez-la avec `-e ORKEON_NO_BANNER=1`), et chaque runner est sur le PATH
sous les mêmes noms que dans les archives de release (`orkeon`,
`orkeon-repl`, …) :

```bash
docker run -it --rm -e ORKEON_RUNNER=shell -v "$PWD:/workspace" \
  ghcr.io/orkeon/orkeon-runners
# orkeon /workspace % orkeon-example run 1
# orkeon /workspace % orkeon run /workspace/crews/my-crew/config.yaml --validate
```

---

## Laquelle choisir ?

- **Vous voulez juste voir tourner une crew ?** Prenez un binaire de release
  (voie 2) ou le conteneur (voie 3) et pointez `orkeon run` sur n'importe quel
  `config.yaml`.
  - Sur **Windows** : `orkeon-cli-<version>-win-x64.zip` + `install.cmd`, ou le
    MSI si vous préférez le double-clic. Un canal à la fois.
  - Sur **Debian / Ubuntu** : le [dépôt apt](../guides/install-with-apt.md), puis
    `sudo apt install orkeon orkeon-archive-keyring`.
  - Puis `orkeon init` → `orkeon doctor` → `orkeon run`.
- **Vous préférez ne rien taper de tout cela ?** Sous Windows et Linux, ces
  mêmes paquets installent [Orkeon Studio](#orkeon-studio-la-voie-graphique) —
  une fenêtre (ou une application terminal plein écran) par-dessus le même
  fichier de configuration et le même `orkeon run`.
- **Vous voulez le REPL ou l'hôte de service ?** L'archive
  multi-apps (voie 2) — et installez le runtime .NET 10, dont ces launchers ont
  besoin.
- **Vous modifiez Orkeon ou exécutez des exemples arbitraires ?** Depuis les
  sources (voie 1).
- **CI / reproductible / aucune chaîne d'outils locale ?** Conteneur (voie 3).

Quel que soit votre choix, les flags et l'histoire des profils `appsettings`
sont identiques — lisez-les une fois dans
[Lancer votre premier exemple](./run-your-first-example.md).

---

## Mettre à jour Orkeon

Mettre à jour, c'est installer la version plus récente par le canal qui a servi à
l'installation. Vos réglages ne font partie d'aucune installation : l'`appsettings.json`
écrit par `orkeon init` reste où il est, quel que soit le canal mis à jour.

Vous ne savez plus quel canal c'était ? **`orkeon doctor` vous le dit** : sa ligne
`install-channel` nomme le canal par lequel cette installation est passée et la commande qui
le met à jour. `orkeon --version --verbose` imprime le même canal sur une ligne à part, pour
un script.

| Installé avec | Passer à la dernière release |
|---|---|
| Le tool dotnet | `dotnet tool update -g Orkeon.Scripting.Cli --prerelease` |
| Des paquets NuGet, dans un projet | `dotnet add package Orkeon --prerelease` — et `Orkeon.Tools` si le projet le référence — réécrit la version |
| Zip Windows, tarball macOS ou Linux | Extraire la nouvelle archive et lancer son `install.cmd` / `install.sh` : il supprime l'installation précédente et met la nouvelle à sa place. Sous Windows, fermer Orkeon Studio d'abord : l'installeur refuse tant qu'un programme de l'installation tourne |
| MSI Windows | Lancer le nouveau MSI : il remplace celui qui est installé, y compris entre deux pré-releases |
| Le dépôt apt | `sudo apt update && sudo apt upgrade` |
| Un paquet Debian téléchargé | `sudo apt install ./orkeon_<version>_amd64.deb` (`_arm64.deb` sur ARM) — ou passer au [dépôt apt](../guides/install-with-apt.md), qui se met à jour seul |
| Conteneur | `docker pull ghcr.io/orkeon/orkeon-runners` — `:latest` avance à chaque release |
| Depuis les sources | `git pull` ; le prochain `dotnet run` recompile. Une installation faite [depuis le clone](#installer-ce-que-vous-venez-de-cloner) : `git pull`, puis relancer `scripts/install-from-source` |

`orkeon --version` affiche ensuite la version qui tourne : `orkeon <version>`. Une installation faite avant qu'`orkeon doctor` sache nommer son canal y répond `unknown` jusqu'à sa prochaine mise à jour.

### Suivre `main` : le canal dev

Entre deux releases, chaque commit validé par la CI sur `main` est publié en
`<version>.dev.<n>` — `n` étant le numéro de ce run de CI — sur le flux Orkeon de GitHub
Packages, où le suivant le remplace. La numérotation et l'élagage des versions sont décrits
dans la
[matrice de publication](../reference/publication-matrix.md#canal-dev--le-dernier-main-entre-deux-tags).
Le canal sert les **canaux NuGet** : le tool `orkeon`, le tool `orkeon-repl` (paquet
`Orkeon.ConsoleApp`) et les paquets. Sous Debian et Ubuntu, le paquet `orkeon` suit aussi
`main`, par le canal apt `dev` — voir
[Installer avec apt](../guides/install-with-apt.md#suivre-main-builds-dev). Les autres
installeurs, l'image conteneur et Orkeon Studio pour Windows ne sont construits que sur les
tags.

1. **Créez un jeton.** GitHub Packages en exige un même pour lire un paquet public : un
   personal access token (classic) avec le scope `read:packages` —
   [le créer ici](https://github.com/settings/tokens/new?scopes=read:packages).
2. **Déclarez le flux, une fois.** Lancez cette commande, comme toutes celles de cette
   section, depuis un répertoire hors de tout clone de ce dépôt : dans un clone, son
   `nuget.config` garde nuget.org comme unique source, et vous obtiendriez la dernière
   release à la place.

   ```bash
   dotnet nuget add source https://nuget.pkg.github.com/Orkeon/index.json \
     --name orkeon-github \
     --username <votre-utilisateur-github> \
     --password <PAT-avec-read:packages> --store-password-in-clear-text
   ```

   ```powershell
   dotnet nuget add source https://nuget.pkg.github.com/Orkeon/index.json --name orkeon-github --username <votre-utilisateur-github> --password <PAT-avec-read:packages>
   ```

   Windows chiffre le jeton. Linux et macOS ne le peuvent pas, d'où le flag : le jeton est
   alors stocké tel quel dans `~/.nuget/NuGet/NuGet.Config`.
3. **Installez, puis mettez à jour.** La mise à jour vous amène au `main` le plus récent à
   chaque exécution :

   ```bash
   dotnet tool install -g Orkeon.Scripting.Cli --prerelease   # la première fois
   dotnet tool update -g Orkeon.Scripting.Cli --prerelease    # toutes les fois suivantes
   ```

   NuGet garde la liste des versions d'un flux pendant 30 minutes : juste après un merge,
   ajoutez `--no-http-cache` pour voir le nouveau build.
4. **Vérifiez.** `orkeon --version` affiche `orkeon <version>.dev.<n>`. Le run `#<n>` du
   [workflow CI](https://github.com/Orkeon/orkeon/actions/workflows/ci.yml) donne le commit, et
   la section `[Unreleased]` du [CHANGELOG](../../../CHANGELOG.md) liste ce qui a changé depuis
   la dernière release. Le site de documentation suit les tags : pour un build dev, lisez la
   documentation sur `main`.

**Déjà installé par un paquet** — le `.deb`, le zip, le MSI, un tarball ? Deux `orkeon` se
trouvent alors sur la machine, et c'est le premier du `PATH` qui s'exécute. Sous Linux, le
`/usr/bin/orkeon` du `.deb` passe en général avant `~/.dotnet/tools` : `orkeon` continue donc
de lancer la release. `command -v orkeon` (PowerShell : `Get-Command orkeon`) montre lequel
répond ; désinstallez le paquet, ou appelez `~/.dotnet/tools/orkeon` explicitement. Orkeon
Studio lance l'`orkeon` installé à côté de lui, qui reste la release ; l'application Windows
prend `--cli-dir` pour en lancer un autre :
`orkeon-studio --cli-dir "$env:USERPROFILE\.dotnet\tools"`.

**Dans un projet**, faites flotter la version plutôt que de l'épingler :
`<PackageReference Include="Orkeon" Version="*-*" />` (et de même pour `Orkeon.Tools`), puis
`dotnet restore --force-evaluate` (avec `--no-http-cache` juste après un merge) pour passer au
build le plus récent. En gestion centrale des paquets, la version flottante va dans
`Directory.Packages.props` et exige
`<CentralPackageFloatingVersionsEnabled>true</CentralPackageFloatingVersionsEnabled>` — sans
ce réglage, la restauration échoue avec NU1011. Un épinglage exact sur un build dev — un
manifeste de tools, un `packages.lock.json` en mode verrouillé — casse dès que le build
suivant le remplace ; un simple `Version="<version>.dev.<n>"` se restaure encore, sur le
build suivant, avec l'avertissement NU1603. Épinglez une release pour tout ce qui doit durer.

**Revenir aux releases :**

```bash
dotnet nuget remove source orkeon-github   # ou `dotnet nuget disable source orkeon-github`, pour garder le jeton
dotnet tool update -g Orkeon.Scripting.Cli --version <release> --allow-downgrade
```

`<release>` est un tag de la [page des releases](https://github.com/Orkeon/orkeon/releases)
sans son `v` initial. Tant que le flux reste déclaré, `--prerelease` et les versions
flottantes résolvent le build dev de tous les paquets Orkeon, ceux de NuGet.org compris ;
`--version` épingle une release.
