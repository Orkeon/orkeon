> 🇬🇧 [English version](../../templates/example-readme.md)

# Gabarit de README d'exemple

> Un gabarit pour les `examples/**/README.md`. Copiez le bloc ci-dessous dans le
> `README.md` d'un nouvel exemple et remplissez chaque section. Supprimez ce
> préambule et toute section optionnelle sans objet. Restez court — le but est que
> quelqu'un exécute l'exemple sans lire le code.
>
> **Sections requises** : Ce qu'il fait · Prérequis · Données requises · L'exécuter ·
> Sortie attendue · Durée & coût approximatifs.
>
> NB : les README d'exemples sont rédigés en anglais (ils vivent hors de `docs/`,
> le contrat de parité ne s'y applique pas) — ce gabarit est traduit pour référence.

---

# <Titre de l'exemple>

> Une ou deux phrases : ce que produit ce crew et pourquoi c'est intéressant.

## Ce qu'il fait

- **Process** : `Sequential` | `Hierarchical` | `Parallel` | `Consensual` | `Graph` | `Autonomous`
- **Agents** : <n> — liste brève des rôles
- **Outils** : `tool_a`, `tool_b`, …
- **Points clés** : ce que l'exemple démontre (dépendances de tâches, mémoire, A2A, …)
- **Runner** : CLI `orkeon` (défaut) | `orkeon-trading` (finance) | `orkeon-interactive` | …

## Prérequis

- SDK .NET ≥ 10.0.300 (sources) — ou le runtime .NET 10 (binaire de release)
- Un profil LLM (voir la matrice de profils sous `examples/appsettings/`) ; cet exemple a
  été validé contre `<provider>`.
- <Tout autre prérequis : un service qui tourne, une clé API au-delà du LLM, …>

## Données requises

Si le crew lit des fichiers d'entrée, listez-les pour que le lecteur sache quoi
monter. Supprimez cette section si l'exemple n'a besoin d'aucune donnée d'entrée.

| Chemin virtuel | Flag de montage | Rôle |
|---|---|---|
| `/data/<fichier>` | `--mount ./data:/data:ro` | <ce qu'il contient> |

## L'exécuter

Donnez la commande **exacte**, copiable-collable, pour chaque voie supportée. La CLI
`orkeon` est le point d'entrée par défaut (`orkeon run <config>`) ; la ligne
« depuis les sources », toujours requise, plus, pour les vitrines, une ligne binaire
et une ligne conteneur.

**Avec la CLI `orkeon`** (binaire de release installé ou `dotnet tool install`) :

```bash
orkeon run examples/<chemin>/config.yaml \
  --settings chemin/vers/appsettings.local.json \
  --mount ./out:/output:rw
```

**Depuis un checkout des sources** (sans installation — exécute votre code local) :

```bash
dotnet run --project src/scripting/Orkeon.Scripting.Cli -- run \
  examples/<chemin>/config.yaml \
  --settings examples/appsettings/appsettings.<provider>.local.json \
  --mount ./out:/output:rw
```

**Depuis le conteneur** (vitrines uniquement — le point d'entrée est `orkeon`) :

```bash
docker run --rm \
  -v "$PWD/out:/output" \
  -v "$PWD/appsettings.local.json:/app/appsettings.local.json:ro" \
  ghcr.io/orkeon/orkeon-runners \
  run examples/<chemin>/config.yaml \
  --settings /app/appsettings.local.json \
  --mount /output:/output:rw
```

> **Exemple finance / trading ?** Remplacez la CLI par `orkeon-trading --config
> examples/<chemin>/config.yaml …` (il ajoute les 44 outils de trading). Dans le
> conteneur, sélectionnez-le avec `-e ORKEON_RUNNER=trading`.
> Référence des flags : [Exécuter votre premier exemple](../getting-started/run-your-first-example.md#chaque-flag-expliqué)
> (ajustez le chemin relatif une fois ce fichier dans un dossier d'exemple).

## Sortie attendue

Décrivez ce qu'une exécution réussie affiche et/ou écrit. Si le crew écrit un
fichier sur `/output`, nommez-le et décrivez sa forme (p. ex. « un rapport Markdown
avec résumé exécutif, sections thématiques et bibliographie »).

## Durée & coût approximatifs

- **Durée** : ~<n> min sur <provider/modèle>
- **Coût** : ~<n> appels LLM ; estimation grossière tokens / \$ si connue (dire
  « modèle local — aucun coût API » le cas échéant)
