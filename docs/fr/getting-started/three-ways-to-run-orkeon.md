> 🇬🇧 [English version](../../getting-started/three-ways-to-run-orkeon.md)

# Trois façons d'exécuter Orkeon

> **Voir aussi** : [Lancer votre premier exemple (EN)](../../getting-started/run-your-first-example.md) · [Vue d'ensemble](./overview.md) · [Retour à l'index](../INDEX.md)

Les crews Orkeon se lancent de trois façons. Choisissez celle qui correspond à
ce que vous acceptez d'installer :

| Voie | Prérequis | Temps avant la première exécution | Idéal pour |
|---|---|---|---|
| **1. Depuis les sources** | SDK .NET ≥ 10.0.300, clone git | ~5 min (+ build) | Contributeurs, lecture/modification du code, exécution de n'importe lequel des 100+ exemples embarqués |
| **2. Binaire de release** | Rien pour les paquets CLI (zip/MSI Windows, `.deb` Debian) — ils embarquent le runtime ; **runtime** .NET 10 pour les launchers supplémentaires de l'archive multi-apps | ~2 min | Exécuter des exemples et des vitrines sans clone des sources |
| **3. Conteneur** | Docker | ~1 min (après le pull de l'image) | CI, exécutions reproductibles, aucun .NET local |

Les trois pilotent le même **CLI `orkeon`** et acceptent les mêmes flags. La
config de la crew est l'argument positionnel de `orkeon run <config>` (le runner
`orkeon-trading` la prend en `--config` à la place) ; les flags optionnels
(`--settings`, `-v`, `--mount`, `--var`, `--llm-log`, …) sont documentés une
seule fois, en détail, dans
[Lancer votre premier exemple (EN)](../../getting-started/run-your-first-example.md#every-flag-explained).

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
en compte vos modifications locales. Les exemples finance passent par
`examples/03-finance-trading` sur le runner `orkeon-trading`. Guide complet,
configuration des profils LLM et dépannage :
[Lancer votre premier exemple (EN)](../../getting-started/run-your-first-example.md).

---

## 2. Binaire de release

Chaque [GitHub Release](https://github.com/Orkeon/orkeon/releases) attache un
**paquet CLI par plateforme**, les **archives multi-apps** historiques, et un
[tool dotnet](https://www.nuget.org/) NuGet :

| Artefact | Contenu | Prérequis runtime | Idéal pour |
|---|---|---|---|
| **`orkeon-cli-<version>-win-x64.zip`** | le seul CLI `orkeon` + `install.ps1` | aucun — self-contained | **Windows : le téléchargement recommandé** |
| **`orkeon-<version>-win-x64.msi`** | le seul CLI `orkeon`, MSI per-user | aucun — self-contained | Windows, si vous préférez le double-clic et une entrée « Applications installées » |
| **`orkeon_<version>_amd64.deb`** | le seul CLI `orkeon`, en `/usr/bin/orkeon` | aucun — self-contained | **Debian / Ubuntu : le téléchargement recommandé** |
| **`orkeon-cli-<version>-osx-arm64.tar.gz`** / **`-osx-x64.tar.gz`** | le seul CLI `orkeon` + `install.sh` | aucun — self-contained | **macOS**, Apple Silicon et Intel respectivement |
| **`orkeon-<version>-<rid>.tar.gz`** / **`.zip`** | **tous** les launchers (`orkeon`, `orkeon-repl`, `orkeon-trading`, les runners TUI…) + `install.sh` / `install.ps1` | mixte — voir le tableau des commandes ci-dessous | Le REPL, les runners TUI, la vitrine trading |
| **`dotnet tool install --global Orkeon.Scripting.Cli`** | le seul CLI `orkeon` | **SDK** .NET 10 | Obtenir uniquement le CLI sur un poste qui compile déjà du .NET |

`<rid>` vaut `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64` (`.tar.gz`) ou
`win-x64` (`.zip`). `SHA256SUMS` couvre tous les artefacts de la release sauf le
MSI, qui a son propre `SHA256SUMS.msi` (chaque fichier est produit par le job CI
qui a construit l'artefact).

L'archive multi-apps est la seule à embarquer plus que le CLI :

| Commande | Ce qu'elle exécute | Runtime |
|---|---|---|
| `orkeon` | **Le CLI principal et le point d'entrée par défaut** — `orkeon run <config.yaml>` pour tout exemple hors finance, ou `orkeon run script.ork.ts` pour le DSL de scripting | self-contained |
| `orkeon-slim` | Le même CLI, framework-dependent et bien plus petit | requiert .NET 10 |
| `orkeon-trading` | Runner de la vitrine trading — `orkeon-trading --config <config.yaml>` ; ajoute 44 outils de trading spécialisés | self-contained |
| `orkeon-repl` | Console REPL interactive complète (tous les outils intégrés, analyse de code, embeddings locaux) | requiert .NET 10 |
| `orkeon-interactive` | Runner Terminal.Gui interactif | requiert .NET 10 |
| `orkeon-claim-verify` | Runner interactif de vérification d'affirmations | requiert .NET 10 |
| `orkeon-spec-forge` | Runner interactif d'interview / spec-forge | requiert .NET 10 |
| `orkeon-tui-keytest` | Utilitaire de diagnostic clavier Terminal.Gui | requiert .NET 10 |

> **Le prérequis runtime, en une ligne.** Les paquets CLI (zip, MSI, `.deb`) et
> les launchers `orkeon` / `orkeon-trading` embarquent leur propre runtime et
> n'exigent aucune installation .NET. Tout le reste de l'archive multi-apps — et
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

```powershell
# Canal A — ZIP + install.ps1 (recommandé)
Expand-Archive orkeon-cli-<version>-win-x64.zip -DestinationPath .
cd orkeon-cli-<version>-win-x64
.\install.ps1     # -> %LOCALAPPDATA%\Programs\Orkeon, PATH utilisateur, entrée « Applications installées »
.\install.ps1 -Uninstall
```

```powershell
# Canal B — MSI (double-clic, ou en silencieux)
msiexec /i orkeon-<version>-win-x64.msi          # même répertoire d'install, même entrée de PATH
msiexec /x orkeon-<version>-win-x64.msi /qn      # désinstallation
```

Le MSI **n'est pas signé**, donc SmartScreen affiche un avertissement d'éditeur
au premier lancement — « Informations complémentaires » → « Exécuter quand
même », ou passez par le canal ZIP.

Dans les deux cas, votre configuration vit dans
`%APPDATA%\Orkeon\appsettings.json`, et **les deux désinstalleurs n'y touchent
pas**. (Un `appsettings.json` laissé dans un répertoire d'installation par une
install antérieure y est migré à la mise à jour.)

### Linux

Le **`.deb` est le canal recommandé** sur Debian et Ubuntu — self-contained, il
ne tire donc jamais de paquet `dotnet-runtime` ni de dépôt Microsoft :

```bash
sudo apt install ./orkeon_<version>_amd64.deb   # installe /usr/bin/orkeon
sudo apt remove orkeon
```

Le `tar.gz` multi-apps + `install.sh` est l'alternative per-user (et la seule
option pour `linux-arm64`, ou quand vous voulez le REPL et les runners TUI) :

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

Le `tar.gz` multi-apps (REPL, runners TUI, vitrine trading) existe aussi pour
les deux architectures macOS, et requiert le runtime .NET 10 pour les launchers
framework-dependent qu'il embarque — `install.sh` affiche le
[lien de téléchargement](https://dotnet.microsoft.com/download/dotnet/10.0)
quand il manque.

### Premier lancement

Ouvrez un **nouveau** terminal (pour que la modification du PATH soit prise en
compte), puis :

```bash
orkeon init      # écrit la config globale : quel LLM, quel modèle, quel endpoint
orkeon doctor    # 9 vérifications : runtime, config, joignabilité du LLM, esbuild, grammaires, …
orkeon run chemin/vers/crew.yaml
```

`orkeon init` est un assistant à 5 choix — `ollama`, `docker-model-runner`,
`openai`, `custom`, ou `none` — et écrit `%APPDATA%\Orkeon\appsettings.json` sur
Windows, `~/.config/Orkeon/appsettings.json` sur Linux et macOS. Il est
scriptable de bout en bout (`--provider`, `--base-url`, `--model`,
`--api-key-env`, `--path`, `--force`, `--no-probe`), et c'est ce qu'utilise la
CI. `orkeon doctor` affiche ✅ / ⚠️ / ❌ par vérification, sort en `1` dès qu'une
vérification échoue, et accepte `--json` pour les scripts.

> **Aucun LLM configuré ?** Orkeon n'échoue pas et ne reste pas muet : il
> avertit sur stderr — *« No `Llm` section configured — falling back to the echo
> provider … Run `orkeon init` … »* — et exécute la crew contre le **fournisseur
> echo**, qui rejoue le prompt au lieu d'y répondre. C'est ce repli qui rend les
> démos de scripting exécutables sans clé ni serveur ; ce n'est jamais un LLM
> qui fonctionne. Si vous voyez cet avertissement alors que vous attendiez un
> vrai modèle, lancez `orkeon init`, puis `orkeon doctor` pour confirmer que
> l'endpoint est joignable.

### Exécuter

Le CLI résout les settings LLM exactement comme depuis les sources.
`orkeon init` couvre le cas courant ; passez `--settings` pour pointer un profil
précis à la place (voir la
[matrice des profils (EN)](../../getting-started/run-your-first-example.md#3-choose-an-llm-profile)) :

```bash
orkeon run chemin/vers/config.yaml \
  --settings chemin/vers/appsettings.local.json \
  --mount ./out:/output:rw
```

Les settings sont résolus dans cet ordre : `--settings`, puis `appsettings.json`
dans le répertoire courant, puis le même en remontant les répertoires parents,
puis le fichier global per-user écrit par `orkeon init`, puis les variables
d'environnement `ORKEON_*` seules.

Pour une vitrine finance/trading, prenez le runner spécialisé :
`orkeon-trading --config chemin/vers/config.yaml --settings …`.

Vous pouvez aussi exécuter une commande directement depuis l'archive extraite,
sans installer : `./libexec/orkeon/orkeon run …`.

---

## 3. Conteneur

L'image `ghcr.io/orkeon/orkeon-runners` (construite depuis `Dockerfile.runners`,
publiée sur GHCR) a le **CLI `orkeon` comme point d'entrée par défaut** et
embarque tous les autres runners **plus les exemples** — zéro .NET local requis.

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
   l'utilisateur non-root `app` de l'image.)
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
orkeon-example list            # parcourir les 105 exemples embarqués
orkeon-example run 1           # exécuter le n°1 (assistant de recherche)
orkeon-example show 42         # lire d'abord le README d'un exemple
```

`orkeon-example run` résout le numéro vers sa config, dispatche automatiquement
la catégorie `03-finance-trading` vers `orkeon-trading`, monte `/output` pour
les résultats fichiers, et choisit les settings LLM pour vous (section
suivante). Quand un numéro existe dans deux catégories (`16`, `102`), il liste
les candidats — qualifiez avec la catégorie : `orkeon-example run 02/16`.

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
docker model inspect gemma4:latest                           # vérifier la config appliquée
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
la variable d'environnement `ORKEON_RUNNER` — `trading`, `repl`, `interactive`,
`claim-verify`, `spec-forge`, `tui-keytest`, ou `shell` :

```bash
docker run --rm -e ORKEON_RUNNER=trading \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners \
  --config examples/03-finance-trading/31-algo-trading/config.yaml \
  --settings /app/appsettings.local.json
```

`ORKEON_RUNNER=shell` ouvre un **zsh** interactif dans l'image (en démarrant
dans `/workspace`) — pratique pour fouiller les exemples embarqués ou déboguer
des montages. Une bannière d'accueil liste les commandes et chemins disponibles
(supprimez-la avec `-e ORKEON_NO_BANNER=1`), et chaque runner est sur le PATH
sous les mêmes noms que dans les archives de release (`orkeon`,
`orkeon-trading`, `orkeon-repl`, …) :

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
  - Sur **Windows** : `orkeon-cli-<version>-win-x64.zip` + `install.ps1`, ou le
    MSI si vous préférez le double-clic. Un canal à la fois.
  - Sur **Debian / Ubuntu** : `sudo apt install ./orkeon_<version>_amd64.deb`.
  - Puis `orkeon init` → `orkeon doctor` → `orkeon run`.
- **Vous voulez le REPL, les runners TUI, ou la vitrine trading ?** L'archive
  multi-apps (voie 2) — et installez le runtime .NET 10, dont ces launchers ont
  besoin.
- **Vous modifiez Orkeon ou exécutez des exemples arbitraires ?** Depuis les
  sources (voie 1).
- **CI / reproductible / aucune chaîne d'outils locale ?** Conteneur (voie 3).

Quel que soit votre choix, les flags et l'histoire des profils `appsettings`
sont identiques — lisez-les une fois dans
[Lancer votre premier exemple (EN)](../../getting-started/run-your-first-example.md).
