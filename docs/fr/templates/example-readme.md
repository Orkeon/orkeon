> 🇬🇧 [English version](../../templates/example-readme.md)

# Gabarit de README d'exemple

> Un gabarit pour les `examples/**/README.md`. Copiez le bloc ci-dessous dans le
> `README.md` d'un nouvel exemple et remplissez chaque section. Supprimez ce
> préambule et toute section optionnelle sans objet. Restez court — le but est que
> quelqu'un exécute l'exemple sans lire le code.
>
> **Sections** : Ce qu'il fait · Prérequis · Données requises · L'exécuter ·
> Sortie attendue · Durée & coût approximatifs. Les dix vitrines suivent ce plan ; les
> autres exemples gardent leurs propres sections autour d'un bloc **Run it** que
> `scripts/add-readme-run-section.py` génère entre des commentaires `BEGIN run-it` /
> `END run-it` (modifiez le script, pas le bloc — les vitrines et `16-interactive-qa`
> sont rédigés à la main et ignorés par le script).
>
> **Ce que vérifie la CI** (`scripts/lint-example-readmes.py`, sur chaque exemple
> numéroté qui a un `config.yaml` ou un `main.ork.ts`) : le README existe ; un titre de
> lancement (`## Run`, `## Run it`, `## Running`, `## Usage`, `## Lancer`, `## Exécuter` — à
> n'importe quel niveau de `##` à `######`) contient une commande `orkeon run <config>`, ou
> la forme depuis les sources `dotnet run --project …Scripting.Cli -- run <config>`, dont le
> chemin `.yaml`/`.ork.ts` — écrit depuis la racine du dépôt — existe (une commande hors
> d'un tel titre n'est qu'un avertissement) ; chaque lien relatif se résout ; le README
> de la catégorie mentionne l'exemple ; et `examples/INDEX.md` / `examples/usecases.json`
> sont à jour. La liste de sections ci-dessus est éditoriale — non vérifiée.
>
> **À côté du README** : un exemple numéroté porte aussi un `usecase.yaml` — son titre
> et sa formulation du problème en cinq langues, ses tags de recherche, les montages
> dont il a besoin et s'il peut être importé comme équipe. Copiez celui d'un voisin et
> suivez [le format de la fiche de cas d'usage](https://github.com/Orkeon/orkeon/blob/main/examples/README.md#use-case-sheet-usecaseyaml) ;
> la CI (`scripts/lint-example-configs.py`) échoue sans elle, ou quand la fiche contredit
> le crew (un crew qui écrit des fichiers exige un montage `:rw`, un montage `./data`
> exige un dossier `data/`).
>
> NB : les README d'exemples sont rédigés en anglais (ils vivent hors de `docs/`,
> le contrat de parité ne s'y applique pas) — ce gabarit est traduit pour référence.

---

# `<Titre de l'exemple>`

> Une ou deux phrases : ce que produit ce crew et pourquoi c'est intéressant.

## Ce qu'il fait

- **Process** : `Sequential` | `Hierarchical` | `Parallel` | `Consensual` | `Graph` | `Autonomous`
- **Agents** : `<n>` — liste brève des rôles
- **Outils** : `tool_a`, `tool_b`, …
- **Points clés** : ce que l'exemple démontre (dépendances de tâches, mémoire, A2A, …)
- **Runner** : CLI `orkeon` (précisez « crew TypeScript » quand le crew est un `main.ork.ts`)

## Prérequis

- SDK .NET ≥ 10.0.300 (sources) — ou le runtime .NET 10 (binaire de release)
- Un profil LLM (voir la matrice de profils sous `examples/appsettings/`) ; cet exemple a
  été validé contre `<provider>`.
- `<Tout autre prérequis : un service qui tourne, une clé API au-delà du LLM, …>`

## Données requises

Si le crew lit des fichiers d'entrée, listez-les pour que le lecteur sache quoi
monter. S'il n'en lit aucun, dites-le en une ligne (le bloc d'exécution généré dit
« this example does not ship sample data yet » et renvoie à la
[politique de données des exemples](../reference/example-data-policy.md)).

| Chemin virtuel | Flag de montage | Rôle |
|---|---|---|
| `/data/<fichier>` | `--mount ./data:/data:ro` | `<ce qu'il contient>` |

Reportez les mêmes montages sous `mounts:` dans le `usecase.yaml` de l'exemple,
relatifs au dossier de l'équipe (`./data:/data:ro`, `./output:/output:rw`).

## L'exécuter

Donnez la commande **exacte**, copiable-collable, avec le chemin du crew écrit depuis
la racine du dépôt (`examples/<chemin>/config.yaml`, ou `examples/<chemin>/main.ork.ts`
pour un crew TypeScript). La CLI `orkeon` est le point d'entrée par défaut
(`orkeon run <config>`) ; mentionnez la forme depuis les sources à côté, et ajoutez une
ligne conteneur quand elle aide. Plusieurs montages se passent séparés par des espaces
après **un seul** `--mount`.

**Avec la CLI `orkeon`** (binaire de release installé ou `dotnet tool install`) :

```bash
orkeon run examples/<chemin>/config.yaml \
  --settings examples/appsettings/appsettings.<provider>.local.json \
  --mount ./out:/output:rw
```

Pour seulement confirmer que le crew se charge et que ses montages sont acceptés (sans
appel LLM), ajoutez `--validate`.

**Depuis un checkout des sources** (sans installation — exécute votre code local) :

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run \
  examples/<chemin>/config.yaml \
  --settings examples/appsettings/appsettings.<provider>.local.json \
  --mount ./out:/output:rw
```

**Depuis le conteneur** (facultatif — le point d'entrée est `orkeon` ; les exemples
embarqués vivent sous `/app/examples`) :

```bash
docker run --rm \
  -v "$PWD/out:/output" \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners \
  run examples/<chemin>/config.yaml \
  --settings /app/appsettings.local.json \
  --mount /output:/output:rw
```

> Référence des flags : [Exécuter votre premier exemple](../getting-started/run-your-first-example.md#chaque-flag-expliqué)
> (ajustez le chemin relatif une fois ce fichier dans un dossier d'exemple).

## Sortie attendue

Décrivez ce qu'une exécution réussie affiche et/ou écrit. Si le crew écrit un
fichier sur `/output`, nommez-le et décrivez sa forme (p. ex. « un rapport Markdown
avec résumé exécutif, sections thématiques et bibliographie »).

## Durée & coût approximatifs

- **Durée** : ~`<n>` min sur `<provider/modèle>`
- **Coût** : ~`<n>` appels LLM ; estimation grossière tokens / \$ si connue (dire
  « modèle local — aucun coût API » le cas échéant)
