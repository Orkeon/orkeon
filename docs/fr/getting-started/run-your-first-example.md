> 🇬🇧 [English version](../../getting-started/run-your-first-example.md)

# Exécuter votre premier exemple (depuis les sources)

> **Voir aussi** : [Trois façons d'exécuter Orkeon](./three-ways-to-run-orkeon.md) · [Vue d'ensemble](./overview.md) · [Bootstrap et exécution](./bootstrap.md) · [Retour à l'index](../INDEX.md)

C'est le point d'entrée si vous avez cloné le dépôt et voulez exécuter un crew
embarqué depuis les sources. En cinq minutes environ, vous exécutez un vrai exemple
à 3 agents et récupérez un rapport de synthèse. Si vous préférez télécharger un
binaire précompilé ou utiliser un conteneur, voir
[Trois façons d'exécuter Orkeon](./three-ways-to-run-orkeon.md).

> **Chemin le plus rapide (ni build, ni clone)** :
> `docker run -it --rm -e ORKEON_RUNNER=shell ghcr.io/orkeon/orkeon-runners`
> puis `orkeon-example run 1` — détails dans
> [Trois façons d'exécuter Orkeon §3](./three-ways-to-run-orkeon.md#3-conteneur).

## Prérequis

| Prérequis | Notes |
|---|---|
| **SDK .NET ≥ 10.0.300** | Le dépôt épingle le SDK dans `global.json` avec `rollForward: latestFeature`. Un SDK plus ancien fait échouer le build (voir [Dépannage](#dépannage)). Vérifiez avec `dotnet --version`. |
| **Git** | Pour cloner le dépôt. |
| **Un endpoint LLM + une clé** | N'importe lequel des 13 fournisseurs supportés, ou un endpoint local type Docker Model Runner / Ollama. Fourni via un profil `appsettings` (ci-dessous). |

## 1. Cloner et builder

```bash
git clone https://github.com/Orkeon/orkeon.git
cd orkeon
dotnet build Orkeon.sln
```

## 2. Choisir un exemple

Chaque crew sous `examples/` est décrit par un `config.yaml`. Parcourez le
[catalogue des exemples](../reference/examples-catalog.md), ou commencez simplement
par l'assistant de recherche — un crew séquentiel à 3 agents qui scrape le web,
analyse des documents et écrit un rapport de synthèse cité :

```
examples/01-enterprise/01-research-assistant/config.yaml
```

## 3. Choisir un profil LLM

Les runners lisent leur configuration LLM (endpoint, modèle, clé API) dans un
`appsettings.json`. Le dépôt livre une **matrice de profils** sous
`examples/appsettings/` pour ne pas en écrire un à la main :

| Fichier | Cible |
|---|---|
| `appsettings.json` | Défaut — [Docker Model Runner](https://docs.docker.com/desktop/features/model-runner/) sur `localhost:12434` (sans clé API) |
| `appsettings.docker-model-runner.local.json.example` | Gabarit Docker Model Runner |
| `appsettings.deepseek.local.json.example` | DeepSeek cloud |
| `appsettings.openai.local.json.example` | OpenAI cloud |
| `appsettings.glm.local.json.example` / `appsettings.glm-medium.local.json.example` | Z.AI (GLM) |
| `appsettings.gemini.local.json.example` | Google Gemini |
| `appsettings.local.json.example` | Gabarit vierge à remplir |

Copiez le gabarit de votre fournisseur, déposez-le en vrai `*.local.json`
(git-ignoré) et collez votre clé :

```bash
cp examples/appsettings/appsettings.deepseek.local.json.example \
   examples/appsettings/appsettings.deepseek.local.json
# puis éditez le fichier et renseignez votre clé API
```

> Les placeholders de style `${DEEPSEEK_API_KEY}` ne sont **pas** développés par la
> configuration .NET — remplacez-les par la clé littérale, ou laissez le fichier
> tel quel et surchargez via variable d'environnement :
> `export ORKEON_Llm__ApiKey=sk-...` (préfixe `ORKEON_`, `__` comme séparateur de
> section).

> Si Docker Model Runner (ou un autre endpoint `localhost:12434`) tourne déjà, le
> `examples/appsettings/appsettings.json` par défaut n'exige ni clé ni copie —
> passez directement à l'étape 4 sans `--settings`.

Il n'y a pas de champ `Provider` à renseigner : le fournisseur est auto-détecté
depuis l'hôte de `Llm.BaseUrl` du profil ; changer de fournisseur revient à pointer
le bon profil.

## 4. L'exécuter

Chaque exemple hors finance s'exécute via la **CLI `orkeon`**. Depuis un checkout
des sources, invoquez-la via son projet (aucune installation — elle embarque aussi
vos modifications locales) :

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run \
  examples/01-enterprise/01-research-assistant/config.yaml \
  --settings examples/appsettings/appsettings.deepseek.local.json \
  --mount ./out:/output:rw \
  -v 1
```

Cette seule commande exécute le crew de bout en bout et se termine. L'outil
`file_write` de l'agent rédacteur dépose son rapport sur le montage `/output`,
c'est-à-dire votre répertoire local `./out`.

> **CLI installée ?** Avec une [archive de release](./three-ways-to-run-orkeon.md)
> ou `dotnet tool install`, la même exécution devient :
> `orkeon run examples/01-enterprise/01-research-assistant/config.yaml --settings … --mount ./out:/output:rw -v 1`.
>
> **Exemples finance / trading** (`examples/03-finance-trading/*`) : ils tournent
> sur le runner spécialisé `orkeon-trading` — `orkeon-trading --config
> examples/03-finance-trading/<nom>/config.yaml --settings …` — qui ajoute les
> 44 outils de trading que la CLI de base n'embarque pas.

## Chaque flag, expliqué

La CLI `orkeon` et les runners spécialisés partagent les mêmes options de base.
Celles que vous utiliserez vraiment :

| Flag | Court | Ce qu'il fait |
|---|---|---|
| `<config>` (positionnel) | — | **Requis.** La définition de crew passée à `orkeon run <config>` — un fichier `.yaml` ou un fichier `.ork.ts` de [scripting](../architecture/scripting.md). Le runner `orkeon-trading` la prend en `--config <chemin>` / `-c`. |
| `--settings <chemin>` | `-s` | Chemin de l'`appsettings.json` portant la config LLM. Optionnel — voir la [résolution des settings](#comment-les-settings-sont-résolus). |
| `--verbose <0-2>` | `-v` | Verbosité. `0` (défaut) = silencieux, `1` = échanges LLM & outils, `2` = debug complet. |
| `--mount <phys>:<virt>:<droits>` | `-m` | Expose un répertoire hôte au système de fichiers virtuel du crew. `droits` vaut `ro` ou `rw`. Plusieurs montages se passent **séparés par des espaces derrière un seul flag** (`--mount a:/x:ro b:/y:rw`) — le parseur rejette un `--mount` répété. Un crew qui écrit des résultats a besoin d'un montage `:rw` (`/output` est la convention qui déclenche l'écriture automatique du résumé). |
| `--allow-external-mounts` | | Autorise des montages (et un `--config` / `--llm-log-path`) situés **hors** du répertoire de travail. Sans lui, les chemins externes sont refusés par garde-fou. La variable d'env `ORKEON_ALLOW_EXTERNAL_MOUNTS=1` l'active pour chaque invocation (l'image conteneur `orkeon-runners` l'embarque). |
| `--var CLE=VALEUR` | `-V` | Injecte une variable dans l'entrée du crew. Les descriptions de tâches contenant `{CLE}` sont développées en `VALEUR`. Plusieurs variables se passent séparées par des espaces derrière un seul `-V` (un flag répété est rejeté). **Crews YAML seulement** — ignoré pour les scripts `.ork.ts`, qui prennent `--inputs`. |
| `--initial-context <texte>` | | Une chaîne de contexte libre passée à l'entrée du crew. **Crews YAML seulement** — ignoré pour les scripts `.ork.ts`. |
| `--inputs <json>` | | Entrées JSON inline transmises à un script comme variable globale `inputs` (voie `.ork.ts`). |
| `--inputs-file <chemin>` | | Comme `--inputs`, lu depuis un fichier JSON. |
| `--llm-log` | | Capture chaque échange HTTP LLM (requête + réponse, en-têtes + payload) en `.jsonl` sous `./llm-logs`. |
| `--llm-log-path <dir>` | | Comme `--llm-log`, mais écrit dans `<dir>` (et implique `--llm-log`). |
| `--validate` | | Dry-run : résout les settings, construit l'hôte et charge la crew (résolution stricte des outils) **sans** sonder le LLM ni rien exécuter. Imprime `VALIDATION OK/FAILED` et sort 0 / non-zéro. |
| `--list-tools` | | Construit l'hôte et imprime les noms d'outils enregistrés, triés, un par ligne, puis sort — aucune crew requise. |
| `--events jsonl` | | Émet le protocole d'événements JSONL versionné sur stdout au lieu du texte brut (c'est ainsi qu'Orkeon Studio pilote un run) — voir [le bus d'événements de run](../architecture/run-event-bus.md). |
| `--stream` | | Avec `--events`, émet aussi les événements `llm.delta` token par token (verbeux par nature ; désactivé sauf demande). |
| `--client <nom>` | | Avec `--events`, le nom du pair observateur sur le hub (`client://<nom>`, défaut `studio`). |
| `--memory-limit-mb <n>` | | Plafond mémoire Jint pour un run `.ork.ts` (surcharge l'appsettings ; `0` le désactive). |

### Les montages et le VFS

Le code du framework Orkeon ne touche jamais le disque directement — toute E/S
passe par le [système de fichiers virtuel](../architecture/vfs-compliance.md).
`--mount` est le pont entre un répertoire hôte et cet espace virtuel :

```
--mount ./out:/output:rw        # hôte ./out  ->  virtuel /output  (lecture-écriture)
--mount ./data:/data:ro         # hôte ./data ->  virtuel /data    (lecture seule)
```

Le runner monte automatiquement en lecture seule le répertoire du config lui-même,
**sous le nom `/crew`**, si bien que le YAML et ses fichiers voisins sont toujours
visibles — un `data.csv` à côté de `config.yaml` se lit en `/crew/data.csv`, jamais
par son chemin sur votre disque
([ADR-008](../adr/ADR-008-virtual-paths-are-the-only-currency.md)). Les chemins
hors du répertoire de travail exigent `--allow-external-mounts` (ou
`ORKEON_ALLOW_EXTERNAL_MOUNTS=1` dans l'environnement — le défaut de l'image
conteneur).

### Comment les settings sont résolus

Quand vous omettez `--settings`, le runner cherche un `appsettings.json` dans cet
ordre (premier trouvé gagne) :

1. Le `--settings <chemin>` explicite, s'il est donné.
2. L'`appsettings.json` voisin du fichier `--config`.
3. En remontant l'arborescence depuis le config, en cherchant à chaque niveau un
   sous-répertoire `appsettings/appsettings.json` — c'est ainsi qu'est trouvée la
   matrice de profils partagée `examples/appsettings/appsettings.json`
   (l'ancien `_shared/appsettings.json` reste un fallback pour une release).
4. Le config global per-user écrit par `orkeon init`
   (`%APPDATA%\Orkeon\appsettings.json` sous Windows,
   `~/.config/Orkeon/appsettings.json` ailleurs).

Si rien n'est trouvé, le runner se rabat sur les seules variables d'environnement
et imprime un avertissement. Être explicite avec `--settings` reste l'option la
plus prévisible.

## Dépannage

Les symptômes possibles sur une machine fraîche, avec le message exact et le
correctif :

| Symptôme / message | Cause | Correctif |
|---|---|---|
| `error CS9057: analyzer assembly ... references version 5.3.0 of the compiler` | SDK .NET plus ancien que 10.0.300 | Installez un SDK ≥ 10.0.300 (la version épinglée dans `global.json`). Vérifiez avec `dotnet --version`. |
| `error CS8795: Partial method ... must have an implementation part` | Le générateur de source n'a pas tourné — généralement le même SDK périmé que ci-dessus | Passez le SDK à ≥ 10.0.300 et rebuiltez. |
| `error NU1008: Projects that use central package version management should not define the version` | Un `Version=` égaré sur un `PackageReference` alors que la gestion centralisée est active | Retirez la version inline ; déclarez-la dans `Directory.Packages.props`. |
| `dotnet: command not found` (dans un script, alors que `dotnet` marche en interactif) | `dotnet` est un alias/fonction shell invisible des shells non interactifs | Mettez le SDK sur le `PATH` dans `~/.zprofile` / `~/.profile`, p. ex. `export PATH="$HOME/.dotnet:$PATH"`. |
| `Connection refused (localhost:12434)` | Le profil par défaut vise Docker Model Runner, qui ne tourne pas | Démarrez Docker Model Runner, ou copiez un profil cloud (p. ex. `appsettings.deepseek.local.json`) et passez-le avec `--settings`. |
| `401 (Unauthorized)` au restore depuis GitHub Packages | Le token `gh` n'a pas le scope `read:packages`, ou vous avez utilisé un PAT fine-grained | Utilisez un PAT **classique** avec `read:packages` (les tokens fine-grained ne sont pas supportés). Test : `curl -u <user>:$TOKEN https://nuget.pkg.github.com/Orkeon/orkeon.hosting/index.json` doit retourner `200`. |
| `ERROR: --allow-external-mounts is required ...` | Votre `--config`, un `--mount` ou `--llm-log-path` pointe hors du répertoire de travail | Ajoutez `--allow-external-mounts` (ou posez `ORKEON_ALLOW_EXTERNAL_MOUNTS=1`), ou ramenez les chemins sous le cwd. |
| `WARNING: No appsettings.json found. Using environment variables only.` | La résolution des settings n'a rien trouvé | Passez `--settings <chemin>` explicitement (voir l'[ordre de résolution](#comment-les-settings-sont-résolus)). |

## Étapes suivantes

- [Trois façons d'exécuter Orkeon](./three-ways-to-run-orkeon.md) — binaires et conteneurs, sans checkout des sources.
- [YAML, Builders et CrewFactory](./yaml-and-builders.md) — le schéma derrière chaque `config.yaml`.
- [Catalogue des exemples](../reference/examples-catalog.md) — 100+ crews sur 9 domaines.
- [Inventaire des outils](../tools/inventory.md) — ce que les agents savent réellement faire.
